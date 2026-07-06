# StruoCMS 架構稽核報告

> **日期：** 2026-07-06（Phase 7f 完成後）
> **範圍：** 全專案架構規劃檢視 — 後端分層、資料層/查詢 DSL、安全/認證、前端 SPA
> **方法：** 四路平行深度審查（各自 read-only），關鍵安全宣稱已於程式碼層交叉驗證
> **狀態：** 評估報告 + 修復進度追蹤

---

## 修復進度

| 項目 | 決策 | 狀態 |
|---|---|---|
| **H2** 隱藏欄位 filter 洩露 | 直接修正 | ✅ 已修復（`QueryValidator` 排除 Hidden；+4 測試，295 綠） |
| **H3** RBAC 自我提權 | 限 super-admin | ✅ 已修復（`CmsCollection.AdminOnly` + `ItemService` 守衛；+4 測試） |
| **H4** 檔案 API 繞過 RBAC | 套用完整 RBAC | ✅ 已修復（`FilesController` CanWrite/CanDelete；+4 測試） |
| **H1** 無 rate limiting | 暫緩 | ⬜ 待處理 |
| **M1** 無 CSRF 防護 | 加 anti-forgery | ✅ 已修復（`CsrfProtectionMiddleware` OWASP 自訂 header + 前端 apiClient；+3 後端 +2 前端測試） |
| **M2** token 永久/無追蹤 | 保留永久 + 加 last-used | ✅ 已修復（User 加 `AccessTokenCreatedAt`/`LastUsedAt`；bearer 認證 throttled 更新；+1 測試） |
| **M3** SSO email 合併 | 維持現狀（接受） | ✅ 已記錄 accepted-risk（`OidcOptions` 註解 + 本文件；部署需 pin tenant/domain） |
| L1–L3 | 未決 | ⬜ 待討論 |
| **D1** 多步驟寫入非原子 | 加 unit-of-work | ✅ 已修復（`IItemRepository.InTransactionAsync` nesting-safe；Create/Update 包單一 tx；+3 測試）**（SQLite 綠，建議補 live-PG gate）** |
| **D2** 無樂觀鎖 | 加 RowVersion + 409 | ✅ 已修復（`AuditableEntity.Version`；repo compare-and-swap `WHERE id AND version=expected`→`ConcurrencyConflictException`(409)；version 進投影；前端 echo；+3 後端 +2 前端）**（SQLite 綠，建議補 live-PG gate）** |
| **D3** 無 production migration | 文件化策略 | ✅ 已產出 `docs/migration-strategy.md`（版本化 SQL 腳本 + 手動套用 runbook；不建框架，日後可升 DbUp） |
| **D4** SQLite 測試掩蓋 PG bug | opt-in 本地 PG 測試 | ✅ 已加 `PostgresIntegrationTests`（設 `STRUO_TEST_PG_CONNECTION` 才跑真 PG，否則 no-op）；覆蓋 D5 offset / D2 compare-and-swap / uuid filter。**需操作者跑一次確認。** |
| **D5** offset 分頁計算錯誤 | 修正 | ✅ 已修復（改 Count + Skip/Take 真 offset 窗；+1 SQLite 測試 +1 PG 骨架） |
| **D6** CSharpTypeName 套用不一致 | 統一 | ✅ 已修復（5 個 hand-built ConditionalModel 依值 CLR 型別設 CSharpTypeName；共用 `ConditionalModelTranslator.SqlSugarTypeName`）**（SQLite 綠，PG 由 D4 骨架驗證）** |
| **D7** 資料路徑重反射 | 待做 | ⬜ 效能（compiled delegate / method 快取），非正確性 |
| **D8** overlay locale=null 全載 | 待做 | ⬜ 規模化正確性 |
| **D9** 多 DB 宣稱名不符實 | 文件化 | ✅ 已修（CLAUDE.md §1 改為 PG 支援、SQLite 測試、其他 provider 標 experimental） |
| **D10** update 三次 SELECT | 待做 | ⬜ 效能，非正確性 |
| **L1** 登入 timing 洩露 | 修正 | ✅ 已修（not-found 走 dummy verify 等化時間；+測試改寫） |
| **L2** 上傳信任 Content-Type | 待做 | ⬜ 有行為變更風險（改預設 allowlist 可能擋掉現有上傳），另議 |
| **L3** 路徑前綴無分隔符 | 修正 | ✅ 已修（比對 root + 分隔符邊界） |
| **A1** §2 sample 引用違規 | 待做 | ⬜ 需另建 Sample.Host，結構性改動 |
| **A2** ItemService god class | 待做 | ⬜ 重構 |
| **A3** AllowAll footgun 在 prod 組件 | 修正 | ✅ 已移到測試專案（Struo.Tests.Support） |
| **A4** discovery GetTypes 未防 | 待做 | ⬜ 加固 |
| F1–F10（前端擴充性） | 未決 | ⬜ 待討論 |

> 基線：後端測試 283 → **309**（含 D4-PG骨架×3〔無 PG 時 no-op〕；L1 測試改寫、A3 移置、D6 皆行為保留故總數不變），前端 157 → **161**，0 失敗。GateGuard hook 本 session 已停用以減少編輯摩擦。
>
> **第二批（未 commit）：** D9（doc）、A3（移 footgun）、L1（timing）、L3（path prefix）、D6（CSharpTypeName 統一，SQLite 綠、PG 待 D4 骨架驗證）。第一批已 commit 於 `fix/architecture-audit-remediation`（c95389d/84594bd）。
>
> **PG-correctness 批次建議（下一輪）：** D6（CSharpTypeName 統一）應與「跑 D4 的 PG 測試套件驗證 D1/D2/D5」一起做——設好 `STRUO_TEST_PG_CONNECTION` 後執行，讓 D6 有 PG 可驗證，同時把 D1/D2/D5 的 SQLite-only 狀態升級為 PG-verified。D7/D10 為純效能、D8 為規模化、D9 為文件，優先序較低。
>
> **D1/D2 live-PG 注意：** 兩者皆在 SQLite 綠。依專案「SQLite 綠 ≠ Postgres 對」紀律，D1 transaction 與 D2 compare-and-swap（raw `WHERE` 用 `GetDbColumnName` + typed 參數；id 綁真 Guid 故 PG uuid 風險低）建議於真 Postgres 跑一次 gate 再視為完全完成。
>
> **M3 accepted-risk 記錄：** SSO JIT 以 email 相等合併既有本地帳號，且 `RequireEmailVerified`/`AllowedTenantId`/`AllowedEmailDomains` 預設關閉。決策為維持現狀以保留零設定開發體驗；**部署時必須**以設定 pin 信任範圍（單一 tenant Authority + `AllowedTenantId`，並建議 `RequireEmailVerified=true`），否則密碼帳號可被同 email 的 IdP 身分接管。

---

## 總結論

核心架構設計是**紮實的**：Clean Architecture 分層清楚、metadata 為 startup-only 不可變快取、查詢 DSL 採 whitelist 驗證、關聯展開已批次化（N+1-safe）、RichText 於寫入路徑完整淨化。

但目前成熟度是**「phase 專案」而非 production-ready**，且累積了數個高風險安全漏洞。問題集中在三處：

1. **授權邊界** — 數個可被利用的權限繞過/提權漏洞
2. **寫入路徑** — 非原子、無並發控制
3. **測試與 schema 演進策略** — SQLite 測試結構性掩蓋 PG bug、無 production migration

修復優先序建議見[文末](#建議修復順序)。

---

## 🔴 一、安全漏洞（最優先）

### H2 — filter/sort 白名單未排除 Hidden 欄位（可盲抽密碼 hash）

**已於程式碼確認。** `QueryValidator.cs:20` 的白名單直接由 `meta.Fields.Select(f => f.Name)` 建立，完全沒有排除 `Hidden` 欄位：

```csharp
var known = meta.Fields.Select(f => f.Name)
    .Concat(meta.Relations.Where(...).Select(r => r.ForeignKey!))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
```

`User.Password` 與 `User.AccessToken` 帶 `[CmsField(Hidden=true, ReadOnly=true)]`。投影（`ItemService.Project`）確實會隱藏這兩個欄位的**值**，但 filter 路徑繞過了投影。

**攻擊情境：** 任何具 `CanRead("user")` 的角色（正是刻意對其隱藏該欄位的「使用者管理員」角色）可發出：

```
GET /api/items/user?filter[password][_startsWith]=$argon2id$v=19$m=...
```

讀 `meta.total`（1 vs 0）即可逐字元抽出完整 Argon2 PHC 字串（含參數與 salt），再離線破解。`accessToken` 的 SHA-256 hash 同理外洩。**`Hidden` 的意義被 filter oracle 完全繞過。**

**修法：** `QueryValidator` 建白名單時排除 `Hidden`（及 credential/system）欄位，而非只在投影排除。修法簡單、影響高，**列為第一優先**。

---

### H3 — RBAC 管理表可透過一般 CRUD 自我提權

**已於程式碼確認。** `ItemService.CreateAsync:259` / `UpdateAsync:274` 僅檢查 `permissions.CanWrite(collection)`：

```csharp
if (!permissions.CanWrite(collection)) throw new PermissionDeniedException("Write not permitted.");
```

`Role`、`Permission`、`UserRole`、`User` 全都是 `[CmsCollection]`，走同一條泛型 CRUD 路徑，對這些 RBAC 管理表**沒有任何額外守衛**（舊的 ProtectedCollections 在 Phase 6b 已移除）。

**攻擊情境（任一即可提權為 super-admin）：**
- 對 `userRole` 有 write → `POST /api/items/userRole {userId:<self>, roleId:<super-admin role id>}` 自派角色
- 對 `role` 有 write → 把自己持有的角色設 `isSuperAdmin=true`
- 對 `permission` 有 write → 插入一列授予自己角色任意 collection 權限

**後果：** RBAC 模型無法表達「能管理使用者但不能提權」——授予任何委派管理員這些表的 write，等同給 super-admin。

**修法：** `role`/`permission`/`userRole`/`user` 的寫入需 super-admin（或不可委派的專屬 capability），在泛型路徑重新引入受保護系統集合守衛。

---

### H4 — 檔案 API 繞過 RBAC（任意登入者可刪除全站媒體，IDOR）

`FilesController` 的 upload/delete 只以 `[Authorize(...CookieOrBearer)]` 檢查「是否登入」（`FilesController.cs:16-18, 69-72`），**無 `CanWrite("file")`/`CanDelete("file")` 檢查、無 ownership**。`FileService.DeleteAsync` 以 id 刪除，無任何授權述詞。非 published 讀取也只檢查「已登入」而非 RBAC。

**攻擊情境：** 任何登入者（含零角色的 SSO JIT 使用者）可 `DELETE /api/files/{id}` 刪除全站任意媒體。檔案 id 會從內容 HTML 的 `data-file-id`、關聯 payload 等處洩漏。

**修法：** 在 FilesController 的變更路由強制 RBAC（`file` collection 讀寫刪）與/或 ownership。

---

### H1 — 完全無 rate limiting / 暴力破解防護

全 codebase 無 `RateLimiter`/`UseRateLimiter`（grep 0 命中）；`AuthController.Login` 接受無限次嘗試。Argon2id 讓每次猜測伺服器端成本高，但無 lockout/throttle/backoff。專案自身 security checklist 明列「Rate limiting on all endpoints」。

**攻擊情境：** 對 `/api/auth/login` 線上密碼噴灑；無限 OIDC challenge；並放大 H2 的盲抽攻擊。

**修法：** `AddRateLimiter` 以 IP（登入另加 per-account）分區的 fixed/sliding window，至少套用於 `/api/auth/*`。

---

### 中風險（M1–M3）

| # | 問題 | 說明 |
|---|---|---|
| M1 | cookie 認證的變更操作無 CSRF token | 僅靠 SameSite + CORS 防護（JSON body 觸發 preflight）。有效但隱性且脆弱——未來任何寬鬆 CORS 或接受 form/text/plain 的端點就重開缺口；login-CSRF 亦可能。建議加 double-submit cookie / anti-forgery token |
| M2 | bearer token 永不過期、不輪替、單槽 | 每使用者一個 `AccessToken`，無 expiry/rotation/last-used；洩漏後永久有效直到人工撤銷。建議加過期、輪替、多具名 token + last-used 追蹤 |
| M3 | SSO 純以 email 合併帳號，預設開放 | `ResolveOrProvisionAsync` 僅以 email 相等連結既有本地帳號；預設 `RequireEmailVerified=false`、`AllowedTenantId=null`、`AllowedEmailDomains=[]`，且 fallback 用 `preferred_username`（Entra UPN，非已驗證 email）。密碼帳號可被同 email 的 SSO 身分接管。建議預設 `RequireEmailVerified=true`、區分 SSO-linked 與 password 帳號、勿把 `preferred_username` 當已驗證 email |

### 低風險（L1–L3）

- **L1** 登入 user-enumeration timing side-channel：`AuthService.cs:8` 找不到使用者時短路跳過 Argon2 verify，可由時間差探測 email 是否存在。修法：not-found 路徑對固定 hash 做 dummy verify。
- **L2** 上傳信任 client Content-Type，無 magic-byte/副檔名驗證，`AllowedContentTypes` 預設空（allow-all）。
- **L3** `LocalFileStorage.cs:14` 路徑前綴守衛 `StartsWith(root)` 無分隔符邊界（`C:\data` vs `C:\dataX`）；目前不可利用（key 為伺服器產生）但應加固。

### 安全做得好的部分

Argon2id 密碼 + 256-bit 隨機 bearer token（SHA-256 儲存、僅比對 hash）；Redis `ITicketStore` 可撤銷 session（HttpOnly/Secure、8h sliding）；RBAC deny-by-default（缺席即拒、無角色 floor 到 public ≤ 匿名、super-admin 顯式）；HTML 淨化 allowlist 完整且 write-path 全覆蓋、無繞過路徑；查詢 DSL 對 SQL injection 防護到位（whitelist + 參數化 + `CSharpTypeName` cast + locale 字元守衛）；OIDC 用 code flow + PKCE、`returnUrl` 淨化、production 關閉 Scalar/OpenAPI；改密碼需現有密碼且限本人。

---

## 🔴 二、資料層 / production 就緒度（結構性缺口）

### D1 — 多步驟寫入非原子（可致髒資料）

`CreateAsync`（`ItemService.cs:262-266`）與 `UpdateAsync` 把三個操作拆成各自獨立的 transaction：

- `repository.CreateAsync` — 父表 insert，無 tx
- `SyncM2MAsync` — 自帶 `BeginTran/Commit`
- `SyncTranslationsAsync` — 自帶 `BeginTran/Commit`

若 translation sync 在父表與 junction 已 commit 後拋錯，資料庫留下違反「default-locale translation 必填」不變量的父列。各子步驟各自原子，但整體 create/update 非原子。`IItemRepository` 未暴露 transaction/unit-of-work handle，不改抽象無法修。

**修法：** 在 `IItemRepository` 加 transaction scope（如 `Task<T> InTransactionAsync(Func<Task<T>> body)`）或 Application 層 `IUnitOfWork`，把父表 insert + M2M + translations 包進單一 `BeginTran/Commit`（`ISqlSugarClient` 為 Scoped，單連線可行）；並移除或改造內層 `BeginTran` 避免巢狀 tx。

### D2 — 無樂觀鎖（靜默 lost update）

`UpdateAsync` 為 read-modify-write：載入現有列 → 覆蓋 body 中的欄位 → 寫回，無 version 欄位、無 `WHERE version=?` 守衛，`AuditableEntity` 無 concurrency token。兩個並發 PUT 靜默互相覆蓋。`DeleteAsync` 的 `OnDelete.Restrict` 檢查亦為 TOCTOU。

**修法：** `AuditableEntity` 加 `RowVersion`/`Version` token，更新以 `WHERE id=? AND version=?`，零列受影響時回 409。

### D3 — 無 production migration 策略（最大單一缺口）

`DatabaseInitializer` 拒絕在 Development 以外執行，且 `InitTables` 本質是 CodeFirst-create，無法 alter/drop 既有欄位。**上線後任何 schema 變更零支援路徑。**

**修法：** 採用明確、可審查的 migration 工具（FluentMigrator 或 DbUp + 手寫 provider-scoped 腳本入庫），並產出對應目前 `InitTables` 輸出的初始 baseline migration。這是「phase 專案」到「可上線」最關鍵的一步。

### D4 — SQLite 作測試 backend 屬結構性不健全

專案反覆踩到「SQLite 綠 ≠ Postgres 對」：uuid=text、`Guid.Parse("")`、bigint→uuid、offset 分頁。這不是偶發，是測試架構問題——SQLite 鬆散型別掩蓋 PG 的 cast/identity/coercion bug，測試在 SQLite 過但 PG 壞。

**修法（系統性）：** CI 以 Testcontainers 跑真 PostgreSQL 把關 merge（專案已有 live-gate 紀律，將其自動化）。SQLite 只留給快速 unit test，但 merge 需 PG run 通過。把「上線才發現」變成 pre-merge failure。

### 其他資料層問題

| # | 問題 | 嚴重度 | 位置 |
|---|---|---|---|
| D5 | offset 分頁計算錯誤：任意 offset 用整除轉成頁碼，非頁對齊 offset 回傳錯誤資料窗（如 offset=25,limit=20 會重送 20–24、跳過 25–29） | MEDIUM | `SqlSugarItemRepository.cs:170-174` |
| D6 | `CSharpTypeName` 套用不一致：hand-built `ConditionalModel`（`WhereIn`/translation FK/M2M delete/translatable search）皆省略，僅靠 PG 對 unknown literal 的 coercion。SQL Server/Oracle 或 SqlSugar 改參數化即壞 | MEDIUM | 同上 :261, :383, :315, :120 |
| D7 | 資料路徑重反射：per-call `MakeGenericMethod`（未快取）+ per-row `GetValue/SetValue`。§17.6「no per-request reflection」只對 metadata scan 成立，資料路徑反射密集（O(rows×fields)）| MEDIUM | `ItemService.Project` :657-665 |
| D8 | translation overlay `locale=null` 會載入整頁所有 locale 再記憶體 group（N×M 列/請求）| MEDIUM | `ItemService.cs:115` |
| D9 | 「多 DB」宣稱其實只有 PG（runtime）+ SQLite（test）。MySQL/SqlServer/Oracle 已 map 但未測，會踩 raw ORDER BY 子查詢與 literal coercion。ORDER BY 用 hand-assembled SQL（`RelationOrderExpr`/`TranslatableOrderExpr`，injection 已防但識別字引號/大小寫是 PG/SQLite 形狀）| MEDIUM | `SqlSugarItemRepository.cs:652-733` |
| D10 | 每次 update 有 3 次 SELECT（呼叫端 + repository 存在檢查 + 回傳新列）| LOW | `ItemService.cs:282` + repo :205,:214 |

### 資料層做得好的部分

關聯展開真正 N+1-safe（每關聯每頁一次批次查詢）；metadata 真為 startup-only 且以不可變性保證 thread-safe（scan-once、fail-fast 驗證）；whitelist 分層健全（filter 數/巢狀深度/limit 上限 + 每路徑對 metadata 驗證，to-many sort 正確拒絕，ORM 型別不外洩）；UUIDv7 PK 伺服器端產生；merge-update 保留伺服器管理欄位；`NullabilityInfoContext` 非 thread-safe 處理正確（per-column 新實例）。

---

## 🟠 三、後端分層與程式碼結構

### A1 — §2 依賴規則被違反（唯一一處）

`Struo.Api.csproj:18` 直接 `ProjectReference` 到 `samples/Struo.Sample.Blog`，`appsettings.json:7` 列它為 `ContentAssemblies`。CLAUDE.md §2 明寫「Framework code never references samples/*」。

**後果：** 用 StruoCMS 作 base template 的使用者繼承對 demo 內容的硬建置相依，必須改框架 `.csproj` 才能移除；也讓 Phase 6.9 convention discovery 半失效——所謂「設定驅動載入」其實靠這條硬引用把 DLL 帶進輸出目錄才成立，真正的 plugin（無 ProjectReference）目前無 assembly-probing/copy 策略。

**修法：** 另建 `samples/Struo.Sample.Host` 引用 `Struo.Api` + `Struo.Sample.Blog`，把 sample 引用與 ContentAssemblies 條目從框架 `Struo.Api` 移除。若刻意保留單一 host 為 §2 的 deliberate deviation，寫成 ADR 並更新 §2——目前程式碼與規則互相矛盾。

### A2 — `ItemService` 偏 god class

`ItemService.cs` 668 行、11 個注入依賴，集中權限、locale 驗證、查詢驗證委派、投影、translation overlay（含 per-locale 圖片解析）、關聯展開、M2M sync、JSON 反序列化 + system-field 剝除 + 必填驗證、RichText 淨化。未超過 800 行硬上限，但為最難測/難改的檔案。

**修法：** 拆為 read/write use-case service + `TranslationOverlay`、`ItemProjector`、`M2MSynchronizer`、`ItemDeserializer`，同時降低注入依賴數。

### A3 — production 組件內含 auth-bypass 型別

`AllowAllPermissionService.cs`（`CanRead/Write/Delete => true`）ship 在 production Infrastructure 組件內，雖未註冊 DI（實際綁 `RbacPermissionService`），但一個 `services.Replace` 就能重新啟用。**修法：** 移至測試專案作 test double。

### A4 — convention discovery 加固

`MetadataServiceCollectionExtensions.cs` 機制本身良好（讀 config、`Assembly.Load`、fail-fast、eager one-time scan into immutable singleton，符合 §6）。但：`a.GetTypes()` 對含無法解析型別的組件會拋 `ReflectionTypeLoadException`（無 try/partial-load 處理）；且 config 驅動載入目前靠 A1 的硬引用才成立。

### 分層做得好的部分

Domain 真正零外部套件（`AuditableEntity` 用 abstract Id 讓 `[SugarColumn]` 只出現在 Infrastructure 子類）；無跨層 using 洩漏；其餘 5 個專案依賴方向皆符合 §2；web-coupled auth 乾淨隔離在 `Api/Auth`（Infra 提供 web-free `StubCurrentUserAccessor`，Api 以 `services.Replace` 換入 `HttpContextCurrentUserAccessor`）；HTTP status 對應集中於 `Program.cs`；controller 薄；DTO/port 都在對的層；集中式套件管理含具移除條件的 CVE 抑制註記。`IHtmlSanitizer`/`IFileStorage`/`ICurrentUserAccessor`/`IPermissionService` 皆小而單一職責，無 god-interface。

> 註：`IItemRepository` 的簽章傳遞原始 CLR `Type` 與屬性名字串（reflection-shaped contract），雖不洩漏 SqlSugar 型別，但假設了 metadata/reflection adapter。屬 schema-driven 泛型設計的務實取捨，建議文件化為 intentional trade-off。

---

## 🟠 四、前端 SPA（7g+ 擴充性風險）

### F1 — 加一個欄位型別是 shotgun surgery（7g+ 核心風險）

dispatcher 是 `FieldInput.vue:23-62` 的 v-if 鏈 keyed by `fieldInputKind.ts` 字串 map，但 per-type 行為**同時**散落在 6 個檔案：`parseItemToForm`（反序列化+預設值）、`buildItemPayload`（序列化）、`validateItem`、`selectListColumns`（list 欄位允許清單）、`formatCell`（list 渲染）。要加 Json/KeyValue/Repeater/Files/multi-select 必須改 union、map、template、parse/serialize、兩個 list helper，**無編譯期強制覆蓋檢查**——正是「加一個 case 忘一處」的陷阱（memory 中 empty-Guid 500 跨 7c/7d/7e 反覆出現即此類）。

**修法（做 7g+ 之前）：** 引入 field-type registry，單一模組 keyed by interface：

```
fieldTypes[interface] = { component, parse, serialize, listColumn?, validate?, defaultValue }
```

`FieldInput` 變 `<component :is="fieldTypes[kind].component">`，parse/serialize/list helper 全部 iterate registry，靠 TS union exhaustiveness 強制覆蓋。

### F2 — 型別 quirk coercion 硬編且重複

`''→null` 的 empty-Guid 修正以字串比對出現在兩處（`buildItemPayload.ts:27`、`parseItemToForm.ts:16`），對型別系統不可見。多值 `Files`（`Guid[]`）與未來任何 Guid-scalar interface 都需同樣處理，忘一處就重現 Postgres 500。層級正確（在 parse/serialize 而非 component），問題是字串鍵且重複。

**建議：** 併入 F1 的 registry 之 `serialize`；且**根治應在後端邊界**（nullable Guid 綁定時把 `""` 視為 null），前端 coercion 保留為 defense-in-depth。

### F3 — i18n 所有 locale 的 TabPanel 同時掛載

PrimeVue Tabs 非 lazy（`ItemForm.vue:62-69`），N locale × M translatable field 同時掛載；翻譯型 richText 欄位 = 同時 instantiate N 個 TipTap/ProseMirror editor，多語系時記憶體/CPU 懸崖。**修法：** 只渲染 active locale 的 TabPanel（lazy tab 或 active 上 `v-if`），或至少只為可見 locale instantiate TipTap。

### F4 — 無 dirty-state 追蹤

`ItemFormView` 持 reactive model 但從不 diff。後果：update 重送所有有內容的 locale（覆蓋未編輯語系、無樂觀並發）；無 `beforeRouteLeave`/`beforeunload` 守衛，中途離開靜默丟失變更；無法做 per-tab dirty 指示。**修法：** 快照初始 model，submit 時 diff 只送變更的 locale/field，加未存變更守衛。

### 其他前端問題

- **F5** 無 route code-splitting（`router/index.ts` 靜態 import 全部 view，TipTap/ProseMirror 進 initial bundle → >500kB advisory）。修法：route lazy import + `manualChunks` 拆 `@tiptap/*`。
- **F6** `itemsApi.ts:22` `res.meta.total` 無 fallback，envelope 缺 meta 時拋 raw TypeError。應 `res.meta?.total ?? 0`。
- **F7** 多處 `as unknown as` 雙重轉型繞過邊界驗證，items 皆 `Record<string, unknown>`，無 Zod（違反專案「不信任外部資料」規則）。
- **F8** 媒體瀏覽邏輯在 `FilePicker.vue` 與 `RichTextInput.vue` 重複（+ MediaLibraryView 應為第三份），無共用 `filesApi.list()`/`useFileBrowser()` composable。
- **F9** RichText 圖片 src 硬編 `/api/files/{id}/content`：於獨立公開網站 origin 渲染時 `/api/...` 會解析到該站 → 破圖，除非 proxy。`data-file-id` 已保留（可 rewrite），但此 contract 未文件化/未建。
- **F10** a11y：`<label>` 無 `for`/`id` 關聯（E2E 需以 `.field` container 繞過）；schema 載入失敗顯示為「Collection not found」（`loadError` 未在 view 呈現）。

### 前端做得好的部分

parse/serialize/validate 純函式分離乾淨且各有單元測試；dispatcher 對未知 interface 優雅降級為 readonly（base template 前向相容好）；apiClient 薄而正確一致（集中 401 hook、統一 error 抽取、204 處理、FormData 分支、`unwrap:false` 逃生口）；coercion 在正確層級；schema + languages 在 shell 載入一次而非每 view；pure-logic 測試覆蓋佳 + E2E 覆蓋真實 auth→CRUD 流程；圖片 src base-independent（跨 origin admin 案例已處理）。

---

## 建議修復順序

### 立即（安全）

1. **H2** — `QueryValidator` 白名單排除 Hidden/credential 欄位（靜默、高影響、易修）
2. **H3** — `role`/`permission`/`userRole`/`user` 寫入限 super-admin
3. **H4** — Files API 變更路由走 RBAC/ownership
4. **H1** — `/api/auth/*` 加 rate limiting
5. 續 M1（CSRF）→ M2（token 過期/輪替）→ M3（SSO email 驗證/帳號區分）→ L1–L3

### production 就緒（資料層）

6. **D1** — aggregate transaction / unit-of-work
7. **D2** — 樂觀鎖版本欄位
8. **D3** — 正式 migration 工具（FluentMigrator/DbUp）+ baseline
9. **D4** — CI Testcontainers 真 PostgreSQL 把關 merge
10. 續 D5（offset 分頁）→ D6（`CSharpTypeName` 統一）→ D7（反射快取/compiled delegate）→ D8（overlay 上限）

### 架構整潔

11. **A1** — 修 sample 引用違規（另建 Sample.Host）
12. **A2** — 拆 `ItemService`
13. **A3** — `AllowAllPermissionService` 移至測試專案
14. **F1 + F3** — 前端 field-type registry + lazy i18n tabs（**做 7g+ 之前**）

---

## 附錄：關鍵檔案索引

| 主題 | 檔案 |
|---|---|
| 查詢驗證（H2/D5/D6） | `src/Struo.Application/Query/QueryValidator.cs` |
| Item 服務（H3/A2/D1/D2/D7/D8） | `src/Struo.Application/Query/ItemService.cs` |
| SqlSugar repository（D1/D5/D6/D9） | `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` |
| 檔案 API（H4/L2/L3） | `src/Struo.Api/Controllers/FilesController.cs`、`src/Struo.Infrastructure/Files/FileService.cs`、`LocalFileStorage.cs` |
| 認證/rate limit/CSRF（H1/M1） | `src/Struo.Api/Program.cs`、`src/Struo.Api/Auth/AuthWiring.cs` |
| Token（M2） | `src/Struo.Api/Controllers/UsersController.cs` |
| SSO（M3/L1） | `src/Struo.Application/Security/ExternalLoginService.cs`、`OidcOptions.cs`、`src/Struo.Api/Auth/OidcClaimsMapper.cs`、`AuthService.cs` |
| 分層違規（A1） | `src/Struo.Api/Struo.Api.csproj`、`appsettings.json` |
| discovery（A4） | `src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs` |
| migration（D3） | `src/Struo.Infrastructure/Persistence/DatabaseInitializer.cs` |
| 前端欄位系統（F1/F2） | `frontend/src/components/fields/FieldInput.vue`、`lib/fieldInputKind.ts`、`lib/buildItemPayload.ts`、`lib/parseItemToForm.ts` |
| 前端 i18n/表單（F3/F4） | `frontend/src/components/ItemForm.vue`、`views/ItemFormView.vue` |
| 前端 API/路由（F5/F6/F7） | `frontend/src/api/apiClient.ts`、`api/itemsApi.ts`、`router/index.ts` |
