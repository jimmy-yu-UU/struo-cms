# StruoCMS 完整審核 — 修復項目 Task List（2026-07-21）

> **審核方法**：5 軌平行子代理（後端架構/正確性、安全、資料庫/SqlSugar、前端 Vue/TS、測試品質），全程唯讀。
> **審核範圍**：上次審核（`architecture-audit-2026-07-15.md`，5 批次修復已全部完成）之後的 ~120 commits：FE-R0～R7 前端全面改版、Site-Settings 品牌編輯器、Batch 5 ItemService 大重構；外加上次留下的 6 條 backlog 逐一複核。
> **基準線（實跑）**：後端 `dotnet test` **787/787 全綠**；前端 vitest **515/515 全綠**、`pnpm build`（vue-tsc）通過；無 skip、無 flaky 徵兆。
>
> **總評**：核心架構依然紮實 —— Batch 5 重構品質高（逐字搬移紀律、delegate-cache 全執行緒安全、JsonDocument 釋放乾淨、零回歸級缺陷）；Site-Settings 新攻擊面在授權/CSRF/輸入驗證/機密外洩四方面無 HIGH；XSS 三層防禦完整（無 v-html、TipTap link guard、伺服器 sanitizer）；SEC-1~SEC-6、soft-delete filter、hidden-field 排除（filter/sort/projection/revision）、交易邊界、migration 冪等性經複核全部仍成立。
> 本輪共 **1 HIGH / 20 MED / 26 LOW**（多代理重複發現已合併；ID 延續前次報告家族編號，撞號已重編）。

## 進度總覽

| 批次 | 內容 | 狀態 |
|------|------|------|
| Batch 1 | 1 HIGH + 高價值 MED（後端/安全/資料） | ✅ 完成（Sonnet 實作 + Fable 複核 PASS + live PG gate 4/4 PASS，`dotnet test` 795 綠）；已提交分支 `audit-2026-07-21-batch1` |
| Batch 2 | 後端/資料 MED 尾巴 + 決策項 | ✅ 完成（commit `a735dc3`，分支 `audit-2026-07-21-batch2`）。BL-1/CS-9/SEC-10/SEC-7 + DB-14 決策；Sonnet ×3 平行實作 + Fable ×3 對抗複核（PASS/PASS-WITH-NITS，nits 已修）；`dotnet test` **815 綠**；live PG gate 2/2 PASS（SEC-10 delete 僅清 logofileid、brandname 保留、無 42804；SEC-7 login 429 envelope+Retry-After） |
| Batch 3 | 前端 MED（7 項） | ☐ 未開始 |
| Batch 4 | 測試缺口（e2e + 單元補洞） | ☐ 未開始 |
| Batch 5 | LOW 批次（後端/資料/前端/測試） | ☐ 未開始 |

---

## Batch 1 — HIGH + 高價值 MED（建議最先處理）

- [x] **DB-11 ⭐HIGH — `site_settings` upsert 為 check-then-insert 競態 → 併發 PUT 在 PG 撞 23505 → 500** ✅ 已修（UPDATE-first → INSERT → catch 23505 rollback+retry UPDATE；用 `System.Data.Common.DbException.SqlState` 辨識，**未引入 Npgsql 直接依賴**；補 catch-all rollback + nesting guard）。⏳ live PG gate：兩 Task 併發 PUT 空表、斷言皆 2xx 且單一 row。
  三軌獨立發現（DB／測試／後端架構），本輪唯一 HIGH。
  `src/Struo.Infrastructure/Settings/SqlSugarSiteSettingsStore.cs:15-39`：先 `AnyAsync` 再分支 Insert/Update，無交易、無 upsert 原語、無 23505 catch；固定 PK 首次儲存併發時第二個請求 500（`DomainErrorMap` 無映射）。既有列併發則為靜默 last-writer-wins（`SiteSettings` 無 `Version` 欄，與全站 409 慣例不一致——可接受但應明文決定）。SQLite 測試結構上測不到（天生序列化）。
  **修法**：改 SqlSugar `Storageable`（upsert）或 PG `ON CONFLICT (id) DO UPDATE`，或 catch duplicate-key fallback 為 update；至少包進 `InTransactionAsync`。
  **驗證**：補「PK 違反→重試為 update」單元測試 + **live PG gate：兩個 Task 併發 PUT、斷言皆 2xx 且最終單一 row**（併入 TEST-1）。

- [x] **SEC-13 MED — `TranslationOverlay` 不過濾 Hidden translatable 欄位（H2「隱藏欄位保證」第三個洞）** ✅ 已修（`camelToClr` 過濾 `Translatable && Hidden`；複核追加 `imageFields` 亦加 `!f.Hidden` 堵同源鍵名注入；TDD RED→GREEN，含單筆 + 列表兩路徑測試）。
  `src/Struo.Application/Query/Read/TranslationOverlay.cs:42-48,61-64`（成因 `MetadataScanner.cs:235-245`）：`camelToClr` 由 `tm.Fields` 全量建立，無 `Hidden` 檢查；revision 路徑的 `RevisionSnapshotRedactor.cs:37-39,63-84` 已明確遮蔽 `Hidden && Translatable`，但每天在跑的 live 讀取路徑（REST + GraphQL 共用 `overlay.ApplyAsync`）原樣吐出。需 host 宣告 `Hidden=true` 的 translatable 欄位才可達（框架內建 collection 無此組合），故 MED。
  **修法**：`ApplyAsync` 以 `meta.Fields.Where(f => f.Translatable && !f.Hidden)` 過濾；補「Hidden translatable 不出現在 translations map」單元測試釘住。

- [x] **BL-3 MED — `Program.cs` top-level catch 吞 startup 例外 → process exit 0（抵銷 ARC-5 fail-fast 價值）** ✅ 已修（catch 內加 `Environment.ExitCode = 1;`，位於 Log.Fatal 後、Serilog flush finally 前；複核確認 top-level 無其他 return 繞過）。
  `src/Struo.Api/Program.cs:152-155`：`catch { Log.Fatal }` 無 rethrow / 無 ExitCode；`ValidateOnStart` 拋的 `OptionsValidationException` 也被吞 → orchestrator 看到乾淨結束。
  **修法**：catch 內 `Environment.ExitCode = 1;`（一行修復）。

- [x] **SEC-8 MED — `/api/schema` 匿名暴露完整內容模型（與 production 關閉 GraphQL introspection 矛盾）** ✅ 已修（查證前端 schema 僅登入後載入 → 對 SchemaController 加 `[Authorize(CookieOrBearer)]`；5 個既有匿名形狀測試改認證 client、新增 SchemaControllerTests 釘 401；複核確認無掩蓋）。⚠️ 有意取捨：headless 匿名 API 消費者無法再先抓 schema（管理 SPA 無此路徑，已於 controller 註解記載）。
  `src/Struo.Api/Controllers/SchemaController.cs:8-20` 無 `[Authorize]`；匿名一次拿到所有 collection（含 `user`）的欄位/關聯/旗標地圖，`DisableIntrospection(!env.IsDevelopment())` 形同虛設。
  **修法**：加 `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]`；若匿名端確需 schema，改依 `ICurrentPermissions` 過濾成僅 public-read collection。**注意**：前端 schemaStore 在 login 前是否會打此端點需一併確認（LoginView 流程）。

- [x] **DB-12 MED — `site_settings.updatedat` migration（timestamptz）vs CodeFirst（timestamp）分歧 — DB-5 parity 紀律的第一個新破口** ✅ 已修（採方案 a：entity 加 `[SugarColumn(ColumnDataType = "timestamptz")]`；實測 SQLite 因 NUMERIC affinity 不受影響、InitTables 不爆；複核 PASS）。⏳ live PG gate：確認 `DateTime.UtcNow`→Npgsql→timestamptz Kind 往返。
  `db/migrations/012-site-settings-table.sql`（`timestamptz`）vs `src/Struo.Infrastructure/Settings/SiteSettings.cs:24`（無 `ColumnDataType` → InitTables 建 `timestamp`）→ dev/prod 同表不同型別；prod DB 以 Development 模式開啟時 InitTables 可能對其 diff/ALTER。
  **修法**（擇一並保持一致）：(a) entity 加 `[SugarColumn(ColumnDataType = "timestamptz")]`（維持 DB-7 慣例，驗證 UtcNow Kind 往返）；(b) 補償 migration 013 改回 `timestamp` 與全站既有欄位一致。**不可改已 applied 的 012。**

- [x] **DB-13 MED — Identity 唯一約束只存在 CodeFirst，production 無 migration 對應；repo 無可自舉的 prod baseline** ✅ 已修（新增 `013-identity-unique-constraints.sql`：5 條 unique index 逐欄比對 CodeFirst 吻合 + dedupe-first；複核修正檔頭措辭「名稱分歧但欄位/唯一性收斂 + accepted redundancy」比照 010；README 更新）。⏳ live PG gate：乾淨 PG 冪等套用、可重跑。
  `User.cs:21,35`／`Role.cs:16`／`Permission.cs:19-23`／`UserRole.cs:21-25` 的 `UniqueGroupNameList` 僅 InitTables 會建；001-012 無任何一支建這些 unique index；`SchemaGuard` 也不含。非 dev-provisioned 的 prod DB：email 可重複（SSO JIT 造重複帳號）、bearer token 查詢無索引且 hash 可撞。
  **修法**：新增 `013-identity-unique-constraints.sql`（dedupe-first + `CREATE UNIQUE INDEX IF NOT EXISTS` ×5，套 010/011 模板）；中期產出 reviewed baseline schema（`pg_dump --schema-only` 審閱後入 repo）。

---

### Batch 1 執行紀錄（2026-07-21）
- 流程：2 個 Sonnet 5 子代理平行實作（A 組後端程式碼 / B 組資料層，檔案不重疊）→ 各 1 個 Fable 5 子代理對抗式複核 → 依複核回饋補修 → 統一驗證。
- 驗證證據：`dotnet test` **795/795 全綠**（787 基準 + 8 新測試）；transitive **Npgsql 確認回到 5.0.18**。
- 複核攔截到的關鍵問題（已全部處理）：B 組原用 Npgsql 直接依賴 catch 23505，會把 PG driver 靜默升到 10.0.3（跨 5 major）→ 改用 `DbException.SqlState` 消除依賴；SEC-13 同源 `imageFields` 鍵名注入 → 補 `!f.Hidden`；DB-13 檔頭平價宣稱失準 → 改措辭。
- ⏳ **尚未 commit**；⏳ **尚未跑 live PG gate**（見下方清單）。

### Batch 1 衍生的新發現項目（本輪修復過程中揭露）
- [ ] **SEC-14 LOW — `SchemaService.WithoutHiddenFields` 未過濾 `meta.Translation.Fields`，`/api/schema/{c}` 的 `translation.fields` 仍列出 hidden translatable 欄位「名稱」**
  `src/Struo.Application/Metadata/SchemaService.cs:23-24` 只重寫 `meta.Fields`，未處理 `TranslationMetadata.Fields`。SEC-8 後僅已登入者可見、且只洩名稱不洩值（值由 SEC-13 已堵），GraphQL 側無平行洩漏（`CollectionSchemaBuilder.cs:176` 已過濾）。
  **修法**：`WithoutHiddenFields` 一併 `Translation = tm with { Fields = 過濾後 }`。**修前先查前端是否消費 `translation.fields`（schemaStore 型別/用法）避免 FE 破壞。**
- [ ] **DEP-1 MED — `HtmlSanitizer` 的間接依賴 `AngleSharp 0.17.1` 觸發 NuGet 安全公告 NU1902（GHSA-pgww-w46g-26qg），配 `TreatWarningsAsErrors` 使 build 全壞**
  現況：`Directory.Build.props` 已加**單條 advisory 抑制**（標 TEMP）讓 build 能跑（複核認定做法恰當、不過度）。這是環境/上游公告變動，非本 repo 程式碼問題。
  **長期修法**：升級 `HtmlSanitizer` 至引用已修補 AngleSharp 的版本（用 `dotnet add package` 取得最新版），或加直接 AngleSharp 引用壓過 transitive，然後移除抑制。**注意**：AngleSharp 是 RichText sanitizer 的核心，升級後必須複核 sanitizer 行為未變（§8 XSS 防線）。

### Batch 1 live PG gate 清單 — ✅ 全數 PASS（2026-07-22 實地驗證，Sonnet 執行 + 貼證據）
- [x] **DB-11 併發**：清空 `site_settings` 後 8 路真併發 PUT（同秒處理）→ 全部 200、後端 log 零 Exception、事後 `count(*)=1`（LWW 符合預期）。driver 確認 Npgsql **5.0.18**。
- [x] **DB-12 型別**：PUT branding 寫入 `DateTime.UtcNow` → 200 無 Npgsql Kind 例外、讀回值與 HTTP Date 一致。⚠️ **重要 caveat**：本機既有 dev PG 的 `updatedat` 實測仍為 `timestamp without time zone`——該表由舊 InitTables 建於 attribute 修改前，SqlSugar 不回溯 ALTER 既有欄位。DB-12 attribute 對「全新建表」與 **prod（migration 012 本就建 timestamptz）** 皆正確；**既有 dev/prod 環境需一次性** `ALTER COLUMN updatedat TYPE timestamptz USING updatedat AT TIME ZONE 'UTC'`（或重建表）才能拿到型別修正。已列為部署註記，非 Batch 1 程式碼缺陷。
- [x] **DB-13 冪等**：真 PG 依序套用 013 兩次皆成功（`IF NOT EXISTS` 冪等）；`pg_indexes` 確認 5 條 `uq_*` 齊備 + SqlSugar InitTables 自動命名同欄位 index 並存（accepted redundancy）；執行前重複列檢查全 0 筆、dedupe 為 no-op、identity 列數 2/2/1/1 不變（未誤刪）。
- [x] **回歸**：super-admin 登入 → article create(201)/update-CAS(200,version 遞增)/i18n 往返/revisions list(2 筆)/trash(204)/`?deleted=only`/restore(200)/purge(204) 全綠，未擾動既有 live 行為。

---

## Batch 2 — 後端/資料 MED 尾巴 + 決策項

- [x] **BL-1 MED — `FilesController.CanReadUnpublishedAsync` 重複 middleware 權限解析（7-dep ctor）** ✅ 已修（抽 `IFileAccessPolicy`/`FileAccessPolicy`，吸收 `IPermissionService`/`ICurrentUserAccessor`/`IRolePermissionStore`/`ICurrentPermissions`；FilesController ctor 7→**4**；bearer-adopt 序列抽成 `PermissionResolutionMiddleware.ResolveAndSetAsync` 由 middleware（cookie 路徑）與 policy（bearer 路徑）共用，重複消除；scoped 註冊於 `AuthWiring.cs`）。Fable 複核 **PASS**：bearer-path ordering 逐字保留、SEC-5 `FileUnpublishedRbacTests` 全綠、DI lifetime 正確、404-not-403 決策仍在 controller。
  `FilesController.cs:19-22,115-130`：controller 內重演 `PermissionResolutionMiddleware` 整套 bearer 解析。

- [ ] **🔶決策 SEC-7 MED — 全站仍無 rate limiting（前次 H1 deferred 的現況確認，非新回歸）** ✅ 決策（2026-07-22）：**窄化 app-layer login limiter + config 快取**。理由：邊緣/全域流量限制交給使用者原計畫的 web server（那才是它的強項）；但 per-account login 退避 + 保護 Argon2 CPU 放大面是 web server 做不到、且屬 SEC-7 安全核心，故在應用層做**且僅針對 `POST /api/auth/login`**。不做全域 token-bucket（避與 web server 重疊）。
  `Program.cs` pipeline 無 `AddRateLimiter`；`POST /api/auth/login` 可無限暴力嘗試，且 timing-equalized Argon2id 讓每個匿名請求都燒滿 Argon2 成本（CPU DoS 放大）；匿名 `/api/config` 每次命中查 DB 無快取。
  **修法（已採納，窄化版）**：.NET 內建 `AddRateLimiter` — 僅 login endpoint per-IP fixed window（可設定，預設 5 次/分）；`app.UseRateLimiter()`；`[EnableRateLimiting("login")]`；OnRejected 回 429 且用標準 envelope（新增 `TOO_MANY_REQUESTS` code）。`/api/config` 加 `IMemoryCache`（30s）+ SettingsController upsert 成功時 evict。**不含**全域 token-bucket。
  ✅ 已修：`LoginRateLimitOptions`（`RateLimiting:Login` PermitLimit/WindowSeconds）；partition key = `Connection.RemoteIpAddress`（null→"unknown" fail-closed）；`[EnableRateLimiting("login")]` 僅 Login（logout/me/oidc 不限）；`Retry-After` 用 `Math.Ceiling`。測試以 `WithWebHostBuilder` 起獨立小限額 host 實測 429（base factory 高限額不遮蔽）。Fable 複核 **PASS-WITH-NITS**。
  🚩 **MED-1 部署決策（2026-07-22 更新，取代原 ForwardedHeaders 註記）**：app 層限流依 `Connection.RemoteIpAddress` 分區，在多-pod K8s 有兩個結構問題：(1) 代理後 RemoteIpAddress = ingress pod IP → 全員塌縮成一桶 → 鎖死全站登入（需 ForwardedHeaders + KnownNetworks=Pod CIDR 才能還原真實 IP）；(2) 記憶體限流**每 pod 各算** → N 個 pod ≈ N×限額，非真全域。經與使用者討論後**採方案：app 層限流加 `RateLimiting:Login:Enabled` 開關（config-driven，預設 true 安全預設）**：
  - **直連/單機/dev**：`Enabled=true`（現況，per-IP 5/60s 生效）。
  - **正式（Docker→K8s 多-pod）**：configmap/env 設 `RateLimiting__Login__Enabled=false`，per-IP 限流交 **ingress/edge/WAF**（邊緣天生看得到真實 client IP、位於所有 pod 之前，是唯一能做到真全域的層）。關掉後 `ForwardedHeaders`/`KnownProxies` **不需要**。
  - 真全域 app 層限流（未來若要精準）可改 **Redis-backed 分散式 limiter**（已有 Redis）；本輪未做。
  - ✅ 已修（追加 commit）：`LoginRateLimitOptions.Enabled`；Program.cs `Enabled=false → GetNoLimiter`（policy 仍存在，`[EnableRateLimiting]` 不炸）；appsettings.json `RateLimiting:Login` 區塊 + 部署說明鍵；測試釘 off-case（Enabled=false + PermitLimit=1 → 5×401 不回 429）。

- [x] **🔶決策 DB-14 MED — 仍然零 FK constraint（上次明示接受的殘留，本輪列出完整清單供再決策）** ✅ 決策（2026-07-22）：**維持 app-only + 明文接受（本項即為紀錄）**，不建 FK。理由：(1) app-side purge pipeline 經本輪與前輪審核確認正確（parent-last 刪除順序、交易邊界、restrict 檢查）；(2) 補 FK 僅能經 migration 建立，SqlSugar InitTables（dev）不會建 → 會重新打開 Batch 1 剛關閉的 InitTables-vs-migration parity 破口（DB-12/DB-13 同源問題）；(3) 覆蓋本就不完整——`revisions.itemid`（polymorphic）、`articles.gallery`（JSON 陣列）結構上無法建 FK，app-side 檢查仍須保留。故 DB 端 backstop 帶來的防禦增益不足以抵銷 parity 迴歸成本。
  **明示接受的完整性缺口清單（app-side pipeline 負責，無 DB 端 FK backstop）**：核心七條 `articles.categoryid`→categories、`categories.parentid`（自參照）、`article_tags.articleid/.tagid`、`article_translations.articleid`、`file_translations.fileid`、`user_roles.userid/.roleid`、`permissions.roleid`；File-picker 純量欄位 `site_settings.logofileid`→files、`articles.heroimageid`→files。**風險**：READ COMMITTED 下 restrict 檢查與刪除 commit 之間的併發插入無 DB 端 backstop（app pipeline 在單一交易內執行以縮小窗口）。**未來若引入繞過 purge pipeline 的寫入路徑，須重新評估本決策。**
  完整性全靠 app-side purge pipeline + 交易；READ COMMITTED 下 restrict 檢查與刪除 commit 之間的併發插入無 DB 端 backstop。

- [x] **CS-9 LOW（同家族三站點，一次修）— 寫入路徑 body 形狀邊角以 500 收場而非 400** ✅ 已修（CreateAsync/UpdateCoreAsync 入口統一 `body.ValueKind != Object → QueryException`（400），位於權限檢查後、Deserialize 前，兩處對稱；SyncM2MAsync 元素改 `TryGetInt64`（拒非整數/溢位）+ ValueKind switch（bool/null/object/array → QueryException），保留 `.Distinct()` 與 includeDeleted 分支）。TDD RED→GREEN 8 測試。Fable 複核 **PASS-WITH-NITS**（無 MED+；LOW-2 註解措辭已於 cleanup 修正）。
  (1) `ItemDeserializer.cs:46`→`JsonBodyUtil.cs:23`；(2) `ItemWriteSideSync.cs:146-149`；(3) `ItemService.cs:224`。Batch 5 逐字搬移如實保留的既有缺口，非重構引入。

- [x] **SEC-10 LOW — 品牌 logo 生命週期 TOCTOU：logo 檔事後被 unpublish/刪除 → 匿名登入頁破圖**（合併 DB-15、TEST-10）✅ 已修（`ConfigController` 產 URL 前經 `files.GetAsync` 重新確認 file 存在且 `Status=="published"`，否則回退 appsettings `LogoUrl`；`FileService.DeleteAsync` 在既有 `InTransactionAsync` 內加 entity-typed `SetColumns(s => new SiteSettings{LogoFileId=null}).Where(LogoFileId==id)` 清理，單成員 initializer 僅更新該欄、42804-safe）。TEST-10 定案：檔案事後消失/unpublish → config 回退。Fable 複核 **PASS-WITH-NITS**：SetColumns 單欄語意經三重佐證確認**不會**誤清 BrandName（非資料遺失）。⏳ live PG gate：delete-path UPDATE 的 `WHERE logofileid=@uuid` + typed-NULL 需實跑 PG。**殘留（已接受）**：deletion 不 evict `/api/config` 快取 → ≤30s TTL 內仍可能供舊 URL（破圖窗口從「永遠」縮至 ≤30s）；Infrastructure 依 §2 不可引用 Api 層快取 key。中期：file delete 時對 metadata 已知的 Image/File/Files 欄位做 Restrict-or-SetNull 掃描（未做，超本項範圍）。
  `SettingsController.cs:42-48` 只在儲存時驗證 published；`ConfigController.cs:29-31` 事後無條件產 URL；`FileService.DeleteAsync`（`FileService.cs:71-96`）不清理 `site_settings.logofileid`（也不清 `articles.heroimageid`/`gallery`/OG image 等 File-picker 純量欄位——讀取端有優雅降級，屬資料衛生）。

---

## Batch 3 — 前端 MED（7 項）

- [ ] **FE-13 MED — SettingsView 重複掛載 `<Toast />` → toast 顯示兩次**
  `SettingsView.vue:95` vs `AppShell.vue:55`（全域已有）。FE-R7「parent+child 重複 ConfirmDialog」教訓在 Toast 上重演。
  **修法**：刪 SettingsView 的 `<Toast />` 與 import。

- [ ] **FE-18 MED — MediaDetailDialog 409 後無恢復路徑：version 滯留，重存永遠 409 且丟編輯**
  `MediaDetailDialog.vue:103-104` 只設 `conflict=true`；對照 `ItemFormView.recoverFromConflict()`（`ItemFormView.vue:146-163`）保留編輯刷新 token。
  **修法**：conflict 時重抓 item 僅更新 `model.value.version`（保留使用者編輯），或加「重新載入」按鈕。

- [ ] **FE-19 MED — 暗色主題下 RichText 彈出面板/表格退回淺色硬編碼色（使用不存在的 CSS token）**
  `RichTextColorMenu.vue:55-59`、`RichTextTableMenu.vue:38-40`、`RichTextInput.vue:199-208`、`RepeaterField.vue:93`：`var(--surface-0, #fff)` 等 token 全案無定義 → 永遠 hex fallback，暗色下白底面板。
  **修法**：改用 R0 token（`--surface`/`--border`/`--accent`）。

- [ ] **FE-14 MED — RevisionHistoryDrawer `select()` 無 latest-wins → 快速點選顯示錯誤 revision**
  `RevisionHistoryDrawer.vue:47-73`：A→B 快速點選，A 慢回覆蓋 `detail`，高亮 B 內容卻是 A。
  **修法**：套 `createLatestWins`，或寫回前檢查 `selected.value?.revisionNumber === rev.revisionNumber`。

- [ ] **FE-15 MED — FilePicker 搜尋每鍵擊即發請求、無 debounce、無 latest-wins（SettingsView 曝險面擴大）**
  `FilePicker.vue:64,37-47`；同型：`FilesField.vue:97` 附近、`RichTextInput.vue:192`。
  **修法**：改用 `lib/debounce(300)` + `createLatestWins`，與清單頁一致。

- [ ] **FE-16 MED — 確認對話框文案硬編碼英文（unsavedConfirm / deleteConfirm / purgeConfirm）— zh-TW 使用者看到英文**
  `lib/formDirty.ts:27-32`、`lib/deleteAction.ts:7-15`；ItemFormView/CollectionListView/SettingsView/MediaDetailDialog 全數使用。
  **修法**：helper 改回傳 i18n key 或接受 `t` 參數；zh-TW/en 補 key（locales.test.ts 會強制對稱）。

- [ ] **FE-17 MED — FilePicker 及 field 層元件 UI 字串硬編碼英文（含 RichText 工具列 aria-label）**
  `FilePicker.vue:76-85`、`FilesField.vue:143`、`RichTextInput.vue:145-192`、`RelationPicker.vue:58`。FilePicker 現直接出現在 Settings 頁。
  **修法**：建 `fields` i18n namespace 逐步遷移；優先 FilePicker。

---

## Batch 4 — 測試缺口

- [ ] **TEST-6 MED — e2e 缺三條關鍵 journey：revisions revert、settings 品牌儲存、media library**
  FE-R7 的 3 個 live-smoke bug 全是單元測試結構上抓不到的，目前只靠一次性手動 smoke。
  **修法**：優先補 `revisions.spec.ts`（create→edit→revert→斷言復原）與 `settings.spec.ts`（儲存→重整→brandName 生效 + dirty guard）；media 上傳/detail 次之。

- [ ] **TEST-4 MED — SettingsView logo 流程整條無測試**
  `SettingsView.test.ts:34` 把 FilePicker/Dropzone stub 死，7 測試全走 brandName。未覆蓋：`SettingsView.vue:44-50` mount 時 URL→fileId regex 還原（格式一變靜默失效）、`:52-54` onUploaded、`:65-67` save 失敗 toast、名稱>100 前端擋下。
  **修法**：各補 1 測試；dropzone 用帶 emit 的 functional stub。

- [ ] **TEST-2 MED — ConfigController「已存 BrandName 空白 → 回退 appsettings」分支無測試**
  `ConfigController.cs:26-28`；store 本身不驗證空白（seeding/未來寫入方）。
  **修法**：`store.UpsertAsync("   ", ...)` 後打 `/api/config` 斷言回 `StruoCMS`。

- [ ] **TEST-3 MED — SettingsController 無「匿名 PUT → 401」測試**
  `SettingsControllerTests.cs:48-54` 只測 roleless 403；`UnauthorizedDriftTests` 掃不到此端點。
  **修法**：`_factory.CreateClient()` 直接 PUT 斷言 401 + envelope code。

- [ ] **TEST-5 MED — RevisionHistoryDrawer「visible watch 開啟即載入」未被測到 + 測試直打 vm 內部（實作細節反模式）**
  `RevisionHistoryDrawer.vue:97-99` watch 被誤刪測試照綠；`defineExpose` + `(w.vm as any)` 斷言、`setTimeout(0)` 輕微 flaky 風險。
  **修法**：補 `visible:false→setProps(true)` 斷言 `listRevisions` 被呼叫；長期改 DOM 斷言。

- [ ] **TEST-SQLITE-GATE（流程項）— SQLite vs PG 落差清單納入 live PG gate 慣例**
  需 gate：site_settings 併發 23505（=DB-11）、`SetColumns` typed-NULL 回歸、migration 012 vs InitTables parity（=DB-12；`SchemaGuardTests` 不含 site_settings，可考慮補）。已覆蓋可接受：logoFileId uuid、timestamptz Kind 往返（欄位不對外）。

---

## Batch 5 — LOW（可批次處理或明示接受）

### 後端 / 架構
- [ ] **BL-2 LOW** — Bearer principal 無 `NameIdentifier` 落到 public floor（今日不可觸發）。`FilesController.cs:126-128`＋`HttpContextCurrentUserAccessor.cs:10-11`：adopt principal 後 `GetCurrentUserId() is null → return false` 一行防禦。
- [ ] **BL-4 LOW** — `AddStruoInfrastructure`/`AddStruoData`/`AddStruoFiles` 忽略 `IConfiguration` 參數（簽章騙人）。移除參數為佳（call site 僅 Program.cs 三處）。
- [ ] **🔶決策 BL-5 LOW** — dev 未設 `Database:MigrationsPath` → migration 腳本在 dev 從不排練，未來 ALTER 型腳本首跑就在 prod。建議 dev 設 `"db/migrations"`（InitTables 先跑、腳本冪等，安全）。
- [ ] **ARC-8 LOW** — `SqlSugarItemRepository.cs` 1076 行仍超過 800 上限。非急迫；下次觸碰時抽 translation 相關成 `SqlSugarTranslationRepository`。
- [ ] **SEC-9 LOW** — 上傳全檔緩衝 `MemoryStream`（25MB×併發 記憶體放大）。`FileService.cs:31-32`：magic-bytes 只需前 12 bytes；改 spool-to-temp（`FileBufferingReadStream`）或串流直上 storage。
- [ ] **🔶決策 SEC-11 LOW** — 白名單允許 `image/svg+xml` 且無 magic-bytes/無 sanitize，靠 attachment disposition 緩解（logo `<img>` 路徑實際安全）。若不需向量 logo 直接移出白名單最乾淨；否則上傳時剝除 SVG script/event handler。
- [ ] **SEC-12 LOW** — `MediaDetailDialog.vue:130-136` 剪貼簿失敗靜默吞。加失敗 toast。

### 資料庫
- [ ] **DB-16 LOW** — 建議補索引：`users` 的 `lower(email)` functional index（登入/SSO 路徑；009 header 自承留給 identity-perf pass）、`users.accesstoken`（每 bearer 請求一次；併入 DB-13 即解）、`files.contenttype text_pattern_ops`（FE-R6 型別過濾 `LIKE 'image/%'`）、各 collection `(deletedat, updatedat)` 部分索引（dashboard/列表排序）。
- [ ] **DB-17 LOW** — 翻譯搜尋抓整列 sidecar（含 body 大欄位）只為取 fk；IN 改寫無上限。`SqlSugarItemRepository.cs:910-923`、`RelationFilterResolver.cs:30-42`：`.Select` 只投影 fk；IN >5k 改子查詢。
- [ ] **DB-18 LOW** — revision 列表把 Snapshot 全文一起抓回（FE-R7 抽屜每開一次拉全部版本完整快照）。`SqlSugarRevisionStore.cs:31-38`：`.Select` 只投影 metadata 欄位。
- [ ] **DB-19 LOW** — soft-delete 分支 restrict 檢查在 txn 外（purge 已在 txn 內，不對稱）；restore 併發可重複記 revision（僅歷史噪音）。`ItemService.cs:252-259,297-303`：檢查移進 txn；restore UPDATE 加 `WHERE deletedat IS NOT NULL`。
- [ ] **DB-20 LOW** — migrations README 漂移（「next is 012」但 012 已存在）；012 header 缺 Date/Author/Ticket 欄位。
- [ ] **DB-21 LOW** — 每次 item update 4 次 SELECT（CAS 已保證正確性；`UpdateAsync` 內存在性預讀可省）。純效率。

### 前端
- [ ] **FE-20 LOW** — `--primary`/`--text` 亦為未定義 token（`RevisionHistoryDrawer.vue:164`、`RevisionSnapshotView.vue:78-79`、`FilePicker.vue:113`、`MediaUploadDropzone.vue:99`、`SettingsView.vue:125`）。`--primary`→`--accent`；`--text`→`--fg`/`--muted`。
- [ ] **FE-21 LOW** — SettingsView 缺 `beforeunload`（dirty guard 只擋 SPA 導航，關分頁無聲丟失）。比照 `ItemFormView.vue:249-256`。
- [ ] **FE-22 LOW** — `as unknown as FileRow[]` 雙重強轉 ×6（MediaLibraryView/FilePicker/FilesField/RichTextInput）。集中 `toFileRow()` 轉換/驗證函式。
- [ ] **FE-23 LOW** — catch-fallback 英文字串未收斂且持續擴散（FE-R5 延後項增生：ItemFormView 5 處 + CollectionListView 2 處 + field 元件 + stores）。統一 `t('common.xxxFailed')` 一次收斂。
- [ ] **FE-24 LOW** — `languageStore.load()` 缺並發去重（schemaStore 有 loadPromise，此處只有 loaded 旗標；Dashboard fan-out 可能重複打 `/languages`）。複製 loadPromise 模式。

### FE-R5/R6 當初延後 Minor（本輪確認全部仍未修）
- [ ] **FE-25 LOW** — media list `<tr>` 鍵盤 a11y（`MediaFileList.vue:29` 無 tabindex/role/keydown；MediaGrid 用真 `<button>` 可對照）。
- [ ] **FE-26 LOW** — MediaDetailDialog gating=false / onCopyUrl 無測試（`MediaDetailDialog.test.ts:61` 只掛 canWrite/canDelete=true）。
- [ ] **FE-27 LOW** — onDeleted 用 `load()` 不 `reload()`（`MediaLibraryView.vue:106`）：刪末頁最後一筆停在超範圍空頁。
- [ ] **FE-28 LOW** — `ItemForm.vue:23` 未使用的 `submitting` prop。
- [ ] **FE-29 LOW** — completeness dots `aria-hidden="true"` 無 aria-label（`ItemForm.vue:74`）：完整度資訊對螢幕閱讀器不可見。

### 測試
- [ ] **TEST-7 LOW** — SettingsControllerTests 拒絕案例只斷言 status code 不驗 `error.code`（`SettingsControllerTests.cs:61,70,79,133`）。
- [ ] **TEST-8 LOW** — ConfigEndpointTests 預設值測試對集合內其他測試的清理有隱性依賴（`ConfigEndpointTests.cs:28`）。開頭防禦式清 singleton row。
- [ ] **TEST-9 LOW** — brandName trim 行為未釘住（傳 `"  My Brand  "` 斷言存 `"My Brand"`）。

---

## 決策項彙總（需使用者拍板，標 🔶）

| ID | 問題 | 選項 |
|----|------|------|
| SEC-7 | 全站無 rate limiting（H1 deferred 現況） | ✅ **已拍板（2026-07-22）：窄化 app-layer login limiter + config 快取**。僅 `POST /api/auth/login` per-IP fixed window + `/api/config` IMemoryCache；全域/流量限制交 web server。 |
| DB-14 | 零 FK constraint | ✅ **已拍板（2026-07-22）：維持 app-only + 明文接受**。不建 FK（避免重開 InitTables-vs-migration parity 破口；pipeline 已審核正確；覆蓋本不完整）。缺口清單見 DB-14 條目。 |
| SEC-11 | SVG 上傳白名單 | (a) 移出白名單；(b) 上傳時 sanitize；(c) 接受現有 disposition 緩解 |
| BL-5 | dev 是否設 `MigrationsPath` | 建議設 `"db/migrations"` 讓 dev boot 排練 prod migration 路徑 |

## 各軌審核確認健康的部分（無需動作，留存證據）

- **Batch 5 重構**：17 站點 delegate-cache 全 static ConcurrentDictionary + open-instance delegate 無捕捉狀態；`PropertyAccessorCache` 語意一致；JsonDocument 釋放（含 CS-5 修復）全數正確；IFieldValidator 純函式；ItemService 399 行交易邊界正確、分層零違規；全 src 無 async void / 阻塞等待；DI lifetime 無錯配。
- **Site-Settings 授權面**：super-admin gate、CSRF 覆蓋（PUT 走全域 middleware + 前端自動帶頭）、輸入驗證、entity-typed SetColumns（避 42804）均正確；`/api/config` 無機密外洩；匿名 file serving SEC-5 修復完整（404 不洩存在性）。
- **XSS**：前端零 `v-html`；RevisionSnapshotView 用文字插值；TipTap link 三層防禦（scheme 白名單 + `isAllowedLinkUrl` + 伺服器 Ganss sanitizer）完整。
- **既有防線**：hidden-field 排除（filter/sort/search/relation path/projection/revision）、soft-delete 全域 filter（`DeletedAccessGuard` 無繞過）、GraphQL cost analyzer + depth 12 + prod 關 introspection、上傳白名單 + magic-bytes、錯誤遮蔽（未映射一律 internal）、localStorage 無敏感資料 —— 全部複核成立。
- **資料層**：migrations 001-012 全冪等；revision capture 在寫入 txn 內 + 唯一索引 backstop；`InTransactionAsync` nesting-safe 一致使用；42804/uuid-cast/varchar(1) 三類 gotcha 防護在所有站點一致；RelationExpander batched push-down（含 8c.3a/3b）仍 N+1-safe。
- **前端**：409/dirty guard 核心流程（version-chain、NAV-1 route-update 防護、Esc resolve(false)）正確；生命週期清理完備；清單頁 latestWins 正確；三個 store 職責清楚；envelope 對齊；locale key 對稱由測試強制。

## 修復流程建議

1. 依批次順序執行；每項完成後勾選並在行尾註記 commit hash。
2. 遵循專案慣例：TDD（失敗測試先行）、DB 行為改動必跑 **live PG gate**（SQLite green ≠ PG correct）、前端改動跑 `pnpm build`（vue-tsc）+ vitest。
3. 🔶決策項先拍板再動工；決策結果（含「明示接受不修」）記回本檔。
4. Batch 1 完成後建議先跑一次全量回歸（後端 787 + 前端 515 + live PG smoke）再進 Batch 2。
