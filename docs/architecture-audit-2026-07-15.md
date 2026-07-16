# StruoCMS 架構稽核報告

> **日期：** 2026-07-15（Phase 9c revisions 完成後）
> **範圍：** 全專案 — 後端分層/設計、C# 程式碼品質、安全、資料層/ORM、前端 SPA
> **方法：** 五路平行深度審查（architecture / csharp / security / database / frontend 子代理，Opus），所有 HIGH 發現已於程式碼層交叉驗證（file:line）
> **基線：** 後端 655 測試綠、前端 267 測試綠
> **重點：** 前次稽核（`architecture-audit-2026-07-06.md`）已修復項目不重列；已「接受/延後」項目（H1 rate limiting、M3 SSO email-merge、D7/D10 效能、D8 單語系、A2 大重構）不重列。加重審查 2026-07-06 之後新增且**未經稽核**的 Phase 7g+/8（GraphQL）/9a（envelope）/9b（soft-delete）/9c（revisions）。
>
> ---
>
> ## 🔧 修復進度（分批執行中，計畫 `docs/superpowers/plans/2026-07-15-audit-remediation.md`）
>
> **Batch 1 — 全部 7 HIGH：✅ 完成、真 PG live-gate 通過（22/22）、後端 683 綠 / 前端 270 綠。**
> - ✅ **SEC-1** 關聯路徑 hidden leaf 排除 — `50f1442`（live：hidden-leaf 400 訊息與 unknown-field 同型，無 oracle）
> - ✅ **CS-2** 畸形 id → 400 — `49cb300`（live：`BAD_USER_INPUT`，無例外型別洩漏）
> - ✅ **CS-1** SqlSugarScope 併發 thread-safety — `612c787`（live：50 併發 GraphQL 多 root 全 200 無 error）
> - ✅ **SEC-2** revision snapshot 對外過濾 hidden — `d851b75` + 測試強化 `9fe7e3d`（live：snapshot 無 hidden、revert 仍還原 hidden）
> - ✅ **DB-1 / DB-2** 交易式 purge 完整性（SetNull/Cascade/孤兒清理）— `02711e4` + 介面預設拋例外 `d187124`（live：purge tag/category/article → junction/translation/revision 全清、SetNull 無 PG 42804）
> - ✅ **DB-3** 熱路徑索引 migration 007 — `e6d55ce`（live：11 索引套用 + 冪等重跑 + `pg_indexes` 斷言）
> - ✅ **FE-1** CollectionListView 深連結載入 schema — `0d4cd61`（live：Playwright 硬導航 `/collections/article` 表格有資料）
>
> **Batch 2 — 6 MED（DB-4=CS-6 / DB-5 / DB-6 / CS-3 / CS-4=ARC-2 / ARC-3）：✅ 完成、真 PG live-gate 通過（runner／unique／parity）、後端 732 綠。**
> - ✅ **DB-4（= CS-6）** revision no. DB 唯一約束 backstop — `3d87618`（migration 010 dedupe-first 後 `UNIQUE(collectionname,itemid,revisionnumber)` + `Revision.UniqueGroupNameList`；live：重複 revision 三元組 insert 遭 23505 擋）
> - ✅ **DB-5** CodeFirst（InitTables）與 migration schema 收斂 — `662097d`（9 個 plain 熱路徑索引補 `[SugarIndex]` 使 InitTables 亦產生 + dev `SchemaGuard` fail-fast；partial index 仍 migration-only 為文件化不對稱；live：InitTables 產出索引 + 重啟冪等）
> - ✅ **DB-6** migration runner／排序／追蹤 + 命名統一 — `925fe12`（統一 001-010 編號 + `MigrationRunner` + `schema_migrations`（timestamptz）；live：全套用／no-op 重跑／壞 SQL abort 回滾／Production-path 皆驗證）
> - ✅ **CS-3** by-id read 與 create DB 路徑轉發 `CancellationToken` — `9f8971b` + `8a58abe` + `55d607f`（by-id read/create + `FileService` 轉發 ct；identity-PK 分支修 review 抓出的 Language `Id=0` 回歸並補回歸測試；identity 路徑 pre-flight 取消）
> - ✅ **CS-4（= ARC-2）** 投影熱路徑快取 property accessor — `ba4ccbd`（`EntityDescriptor.Properties`（OrdinalIgnoreCase，每實例建一次）+ `PropertyAccessorCache`；零行為改變；寫入路徑延至 Batch 5／ARC-1）
> - ✅ **ARC-3** REST／GraphQL error-code 映射單一真相 — `09808aa` + `0ed2865`（`DomainErrorMap` 單源 + `StatusFor`；GraphQL 未認證 `PermissionDenied` 改回 `UNAUTHORIZED`（漂移修復，對齊 REST 語意）；`DeletedAccessGuard` 去重；live：3 情境 parity 驗證）
>
> **Batch 3 — 5 前端 MED（FE-2～FE-6）：✅ 完成、live gate 通過（真 PG + Playwright；新 e2e specs 6/6）、前端 309 綠 + build 綠、後端未動（732）。**
> - ✅ **FE-2** 清單 stale-response 競態 — `f9f6412`（`lib/latestWins.ts` 遞增 token；`loadItems` 全部 state 寫入含 finally guard；AbortController 依計畫 YAGNI 不做）
> - ✅ **FE-6** Relation/Files picker debounce + 排序 — `f9f6412`（`lib/debounce.ts` 300ms（含 cancel、unmount 清理）+ loadOptions latest-wins；live：連打 8 鍵 → 1 個 debounced search 請求（network log））
> - ✅ **FE-3** 伺服器端 `error.details` 映回欄位錯誤 — `7c3dfd6`（`lib/applyServerErrors.ts` case-insensitive → meta 正名；leftover 退 banner；CMS `BAD_USER_INPUT` message-only 維持 banner 為既知界線；details 目前僅 model-binding 產（`Program.cs` `InvalidModelStateResponseFactory`），items API 不經之 — 單元證據為準）
> - ✅ **FE-4** 樂觀鎖 409 恢復路徑 — `684617a` + `817568a`（**含 fact-check 新發現前置 bug：`setModel` 丟棄 version → UI 從未 echo、樂觀鎖實際失效**，一併修復；409 CONFLICT → 重抓只更新 `model.version` 不覆蓋使用者編輯 + conflict banner + Reload latest（套用衝突當下快取副本，過期會再 409 自恢復）；live e2e：雙寫者 409 → reload/再存兩路徑 + version 單調）
> - ✅ **FE-5** dirty-state 離開守衛 — `8a8e9d5` + `817568a`（`lib/formDirty.ts` snapshot 排除 version（409 恢復不擾動 dirty）+ `onBeforeRouteLeave` async confirm + `beforeunload`；live e2e：dirty reject/accept、untouched（含 TipTap）無誤報、存檔後不問、Esc dismiss 後守衛仍作用）
> - e2e：`conflict.spec.ts` + `unsaved-guard.spec.ts` 新增（`3827d41`）。final-review MUST-FIX（init 重置 conflict/latestFromServer）於 `817568a`。
> - ⚠️ **live gate 新發現（本批範圍外，待列管）：** (a) **空白 optional DateTime 序列化為 `""` → Article UI 更新 400** — ✅ **已修（Batch 3b hotfix `f97da84`）**：`date`/`time`/`dateTime` serialize 空值改送 `null`（API 實證 null→200；後端不動）；(b) RelatedList 同 route-record params-only 導航繞過 leave guard，且被未 key 的 `<router-view>`（AppShell）遮蔽 — 成對列入下輪稽核；(c) 既有 e2e `items.spec`/`relations.spec` 對 RichText Body 的 textarea 假設過期 — ✅ **已修（Batch 3b `d9fa0de`）**：TipTap-aware 重寫 + `conflict.spec` 換回 article（Published At 留空，同時 live 實證 (a) 修復達 409 而非 400）；e2e 全套 **12/12** 綠、前端 312 綠 + build 綠（Batch 3b merged）。
>
> **Batch 4 — LOW（SEC-4～6 / CS-5/7/8 / DB-7/9/10 / ARC-4～6 / FE-7～12）＋ DB-8 ＋ Batch 3 carry-over（NAV-1 / API-1）：✅ 完成、真 PG + MinIO live-gate 通過（2026-07-16）、後端 773 綠 / 前端 339 綠 + build、e2e 13/13 live。**
> live gate（2026-07-16）：SEC-4 cost 400 `HC0047`、SEC-5 draft-file RBAC 404、SEC-6 白名單 + MinIO content-type/disposition、DB-8 version+revisions（psql 驗證）、DB-9 dedup、DB-10 唯一 23505 + migration 011 冪等、ARC-5 startup fail-fast、API-1 雙協定、NAV-1 e2e。
> - ✅ **SEC-4** GraphQL cost analyzer（alias/batch 放大緩解）— `3079cd1`
> - ✅ **SEC-5** 未發布檔案讀取 gate `CanRead(file)`（零權限者不再可讀 draft 檔）— `3079cd1`
> - ✅ **SEC-6** 上傳白名單合理預設 + S3/MinIO 物件 content-type + presigned download disposition — `3079cd1`
> - ⏸️ **SEC-3 — DEFERRED（使用者決策 2026-07-16）**：`appsettings.Development.json` 為 gitignored／僅本機，使用者判定無洩露風險；本輪不做輪換／遷移。
> - ✅ **CS-5** `RevertAsync` 內層 `JsonDocument` pool buffer 洩漏（`using` 內層 snapshot doc）— `d38cc0a`
> - ✅ **CS-7** `LanguageProvider` 快取併發競態（`Lazy<>` 快取）— `d38cc0a`
> - ✅ **CS-8** `FileService.DeleteAsync` 改用 nesting-safe helper（非 raw BeginTran）— `d38cc0a`
> - ✅ **DB-7** 新欄位 `timestamptz` 慣例文件化 — `2b85cbb`
> - ✅ **DB-8** trash/restore 繞過樂觀鎖與 revision 歷史 — `e02eed6` + `bf8b181`（決策：trash/restore 遞增 version 並記 delete/restore revision；冪等守衛：已 trash 時跳過 version 遞增/revision）
> - ✅ **DB-9** M2M 重複 id 誤判 + revert 對已 trash M2M target 容錯 — `2b85cbb`
> - ✅ **DB-10** translation `(fk,locale)` 唯一約束（migration 011 + CodeFirst）— `2b85cbb`
> - ✅ **ARC-4** `SqlSugarItemRepository` ORDER-BY 子查詢抽 `OrderByExpressionBuilder` — `178fbc9`（僅抽取、零行為改變；`MakeGenericMethod` delegate-cache 延至 Batch 5）
> - ✅ **ARC-5** Options 啟動 fail-fast（`ValidateDataAnnotations` + `ValidateOnStart`：Database/Query/Oidc/Files）— `e7ec131`
> - ✅ **ARC-6** `ItemsController` 改依 `IItemUseCases` 接縫（不再直依具體 `ItemService`）— `f8b1a8b`
> - ✅ **FE-7** 搜尋 debounce timer 於 unmount/切換 collection 清除 — `9a09774`
> - ✅ **FE-8 / FE-9** MediaLibrary 刪除確認 + 權限 gate；刪除死 scaffold（`HelloWorld.vue`）— `37ad267`
> - ✅ **FE-10 / FE-11 / FE-12** RichText link protocol guard；apiClient list meta guard + `errBody` rename；RelationPicker 選中 label 併入 options — `2862e05`
> - ⚠️ **Batch 4 carry-over（Batch 3 live 發現）：** (a) **NAV-1** — RelatedList 同 route-record params-only 導航繞過 leave guard + 未 key 的 `<router-view>`（AppShell）—— ✅ **已修 `27cf662`**（keyed router-view + route-update dirty guard，同一 record 導航重載表單並尊重未存編輯；一併解 Esc-dismiss 後 leave guard resolve）；(b) **API-1** — VERSION_CONFLICT 從 CONFLICT 拆出（REST + GraphQL + 前端）—— ✅ **已修 `0807bee`（後端雙協定拆分）+ `112c5f0`（前端衝突恢復僅 key 於 `VERSION_CONFLICT` + e2e settle/churn 強化）**。
>
> **下輪稽核候選（Batch 4 新發現 / 既有，列 backlog）：**
> - `FilesController.CanReadUnpublishedAsync` 重複 middleware 的權限解析（7-dep ctor）。
> - Bearer principal 無可解析 `NameIdentifier` 時落到 public floor（今日不可觸發；補明確 null guard）。
> - `Program.cs` top-level catch 吞掉 startup 例外 → process exit 0（既有；orchestrator 看到乾淨結束）。
> - `AddStruoInfrastructure`/`AddStruoData`/`AddStruoFiles` 忽略其 `IConfiguration` 參數（`BindConfiguration` 走 DI）。
> - Presigned S3 URL 以 https scheme 發出而 MinIO endpoint 為 http（既有環境怪癖，live gate 觀察到；簽章僅 host，故 scheme 置換仍可用）。
> - `MigrationRunner` 為 config-driven（`Database:MigrationsPath`）而 dev config 未設 → migration 011 於 gate 手動套用；須決定 dev 是否應設該路徑。
>
> **Batch 5（ARC-1 god-class 重構 + ARC-4 delegate-cache 尾巴 + CS-4/ARC-2 寫入路徑 accessor 快取）— ✅ 完成（2026-07-16）：**
> - ✅ **ARC-1** `ItemService` 1251→**399 行**純編排層 — 分四步全綠 commit（`1244250` ItemDeserializer + per-interface `IFieldValidator` registry（Tags/OptionMultiValue/KeyValue/Files/Repeater，phase 順序與例外訊息逐字保留）+ `SyncTranslationsAsync` Required/MaxLength 去重入 `FieldValueRules`；`8efd3de` ItemProjector；`eef1891` TranslationOverlay + DeepExpansionCoordinator；`f70feb3` ItemWriteSideSync + ItemPurgePipeline）。ctor 簽章凍結（16 個測試建構點零改動）、協作類別以欄位初始化自建（ARC-4 前例）；全批 `git diff -- tests/` 為空。
> - ✅ **CS-4/ARC-2 寫入路徑** accessor 快取 — `8efd3de`（Create/Update id 讀取、field-overlay、FK-overlay 換 `EntityDescriptor.Properties`（OrdinalIgnoreCase，同一 PropertyInfo）；ItemDeserializer 內同步換用）。
> - ✅ **ARC-4 尾巴** `MakeGenericMethod` delegate-cache — `0ad2328`（**17** 個 dispatch 站點（原估 ~14）全轉 cached open-instance delegate，per-dispatcher `ConcurrentDictionary<Type,Func<…>>`；helper 皆 async 故例外面完全一致；ctor 不變）。**OrderByExpressionBuilder ctor 注入改判 YAGNI 不做**（無 Batch-5 直接消費者，rewire 只會攪動 18 個測試建構點；DI scoped 註冊 + 自建維持現狀，決策記於 batch5 計畫）。
> - Gate：後端 773 全綠（測試零改動）+ 真 PG live 回歸 15/15（登入/CRUD/兩則 400 驗證訊息逐字/i18n overlay/M2M+deep/CAS+`VERSION_CONFLICT`/revisions list+revert/trash 204/`?deleted=only`/restore/purge）。

---

## 總結論

核心架構**依然紮實**：Clean Architecture 分層零違規、Domain 型別純淨、metadata 為 startup-only 不可變快取、soft-delete 單點全域 filter、revisions 設計乾淨（capture 在寫入 transaction 內）、REST/GraphQL 寫入路徑 RBAC 對稱、RichText 於 revert 也重新淨化。前次稽核的所有安全修復（H2–H4/M1/M2/L1–L3/D1/D2/D5/D6）經覆核**仍然成立**。

但新增的 Phase 8/9 表面積帶進了數個**真實缺陷**，且暴露出兩類系統性問題：

1. **H2「隱藏欄位」保證有兩個漏洞** — 關聯路徑 filter/sort（SEC-1）與 revision snapshot（SEC-2）都繞過了 hidden-field 排除，在合理的 host 設定下可重新打開盲抽 / 直接洩露憑證的攻擊面。
2. **資料完整性外圍幾乎不存在** — 沒有任何 FK constraint、沒有 index（除 revisions 一個）、`OnDelete.SetNull/Cascade` 宣告了卻從未實作、purge 會產生孤兒列。ORM 不會自動產生這些，migration 也沒補。

共 **7 個 HIGH / 12 個 MEDIUM / 21 個 LOW**。多數 HIGH 都是「預設 sample 尚不可觸發、但作為可重用框架模板一旦 host 正常擴充即成真」的潛在缺陷 —— 正因為 StruoCMS 定位是 base template，這些必須修。

### 跨面向重複發現（同一根因）
- **每列反射**：ARC-2 = CS-4 —— `EntityDescriptor` 只快取欄位「名稱」不快取 `PropertyInfo`，每筆列每欄位做 `Type.GetProperty`，違反 §17.6「無 per-request reflection」。
- **revision number 無唯一約束**：CS-6 = DB-4 —— `MAX+1` 產生，靠樂觀鎖「順帶」序列化，但非 `AuditableEntity` 的 revisioned collection 無 CAS，可產生重複 revision no.。
- **隱藏欄位保證破口**：SEC-1（關聯路徑）與 SEC-2（snapshot）都源自「H2 排除只做在自身欄位路徑」。

---

## 建議修復順序（優先序）

| 序 | 項目 | 面向 | 嚴重 | 一句話 |
|---|---|---|---|---|
| 1 | **SEC-1** | 安全 | HIGH | `RelationPath.Parse` leaf 未排除 hidden → 關聯路徑重開 H2 oracle。**一行修復** |
| 2 | **CS-2** | C# | HIGH | 畸形 id → `Guid.Parse` 丟未映射例外 → 500（應 400/404），且洗 error log |
| 3 | **CS-1** | C# | HIGH | GraphQL Request-scope + `new SqlSugarClient`（非 thread-safe）→ 併發 resolver 撞同一連線 |
| 4 | **SEC-2** | 安全 | HIGH/MED | revision snapshot（REST+GraphQL）洩露 hidden 欄位明文給任何有 read 權限者 |
| 5 | **DB-1 / DB-2** | 資料 | HIGH | purge 產生孤兒（junction/translation/revision）；`SetNull`/`Cascade` 宣告未實作 |
| 6 | **DB-3** | 資料 | HIGH | FK/翻譯鍵/soft-delete flag 全無 index → 熱路徑 seq-scan |
| 7 | **FE-1** | 前端 | HIGH | `CollectionListView` 深連結/重新整理時 schema 未載入 → 清單永久空白 |
| 8 | **DB-4 = CS-6 / DB-5** | 資料 | MED | revision no. 無唯一約束可重複；InitTables 與 migration schema 分歧 |
| 9 | **FE-2/FE-4/FE-5** | 前端 | MED | 清單 stale-response 競態；409 無恢復路徑；無未存編輯離開守衛（資料遺失） |
| 10 | **ARC-2 = CS-4** | 架構/C# | MED | per-request 反射（快取 PropertyInfo/compiled getter） |
| 11 | **ARC-1 / ARC-3** | 架構 | MED | `ItemService` 981 行 god class；REST/GraphQL error-code 映射重複已漂移 |
| — | 其餘 LOW | 各 | LOW | 見下清單，可批次處理或列 backlog |

---

## 🔴 安全（SEC）

### SEC-1 — HIGH — 關聯路徑 filter/sort 未排除 hidden 欄位（跨關聯重開 H2 盲抽 oracle）
**已於程式碼確認。** `QueryValidator.cs:25` 自身欄位白名單正確用 `Where(f => !f.Hidden)`（H2 修復）；但 `RelationPath.cs:64-66` 對點狀關聯路徑的 leaf 欄位驗證只做 `terminal.Fields.Any(...)`，**沒有 `!f.Hidden`**。`RelationFilterResolver.cs:89-105` 接著用該 leaf 建 `ComparisonFilter` 打到 terminal collection，`ConditionalModelTranslator.Column` 會解析出 hidden 欄位真實 DB 欄位（hidden 欄位仍在 `FieldToProperty` map），predicate 於是對 hidden 欄位執行。sort 亦然（`?sort=<rel>.<hidden>` 產生 ORDER-BY 相關子查詢）。

**攻擊情境：** 對任何可讀、且有 `[CmsRelation]` 指向帶 hidden 欄位之 collection，`GET /api/items/<coll>?filter[<rel>.<hidden>][_startsWith]=a` 的 `meta.total` 變化即為逐字元盲抽 oracle —— 與 H2 完全同型但改走關聯。框架內建 `User.Password`/`User.AccessToken` 為 hidden；host 只要加一個 `author`/`owner` M2O 指向 user 即可洩 Argon2id hash。sample 目前 `Article→Category(M2O)`、`Article→Tag(M2M)` 若任一標 Hidden 欄位，`article`（常為 public-read）即成未認證 oracle。**GraphQL 不受影響**（nested filter input 已排除 hidden，`CollectionSchemaBuilder.cs:216`）。

**修復（一行）：** `RelationPath.Parse` leaf 檢查改為 `terminal.Fields.Any(f => !f.Hidden && string.Equals(f.Name, leaf, ...))`，同時堵住 filter 與 sort 兩個變體。

### SEC-2 — HIGH/MEDIUM — revision snapshot（REST + GraphQL）洩露 hidden 欄位給任何有 read 權限者
`RevisionSnapshotBuilder.cs:44-56` 建 snapshot 時只跳過 `IsSystem` 與 `Translatable`，**沒有 `if (field.Hidden) continue;`**（對比讀取投影 `ItemService.cs:1072` 有跳過 hidden）→ hidden 欄位值原文序列化進 snapshot。`GetRevisionAsync`（`ItemService.cs:741-748`）只 gate `CanRead(collection)` 就回傳原始 snapshot，經 REST `GET /api/items/{coll}/{id}/revisions/{n}` 與 GraphQL `xRevision` 暴露。

**攻擊情境：** 任何啟用 `Revisions=true` 且有 hidden 欄位的 collection，僅需 read 權限（若 public-read 則匿名）即可**直接讀出** hidden 欄位明文（比 SEC-1 更嚴重，直接洩露而非 oracle）。sample 只有 `Article` 有 revisions 且無 hidden 欄位，故預設不可觸發 —— 潛在模板缺陷。

**修復：** 區分「revert 內部用 snapshot」（需完整）與「對外回傳 snapshot」（需過濾 hidden）。`GetRevisionAsync`/`xRevision`/REST revision-get 過濾 hidden，`RevertAsync` 內部仍用完整 snapshot（revert 本身已安全，見下）。

### SEC-3 — LOW — working-tree config 內有明文第三方憑證
`appsettings.Development.json` 含真實 Postgres 密碼、真實 **Azure AD OIDC ClientSecret**、bootstrap admin 密碼、MinIO 憑證。已確認該檔 gitignored 且從未提交（tracked `appsettings.json` 為 `REPLACE_ME`）——**無 repo 洩露**。但明文躺在磁碟上；若被備份/分享/機器被入侵，OIDC client secret 可對 tenant 發 token。**修復：** 移到 .NET user-secrets / 環境變數；輪換已明文存在過的 Azure secret 與 DB/admin 密碼。

### SEC-4 — LOW — GraphQL 無 cost/complexity 與 alias/batch 限制（僅 max-depth 12）
`GraphQlServiceCollectionExtensions.cs:48` 只有 `AddMaxExecutionDepthRule(12)`，無 cost analyzer、無 alias 上限、batching 為預設。搭配已接受缺席的 rate limiting（H1），單一請求可用 alias 重複昂貴的 list/relation-expansion 欄位放大 DB 負載。**修復：** 啟用 `AddCostAnalyzer` / 限制每請求 operation 與 alias 數（與日後 H1 一起做）。

### SEC-5 — LOW — 未發布檔案內容任何已認證者可讀（無 per-collection RBAC）
`FilesController.cs:61-74,94-100`：`published` 檔匿名可讀；非 published 檔只需「已認證」（cookie/bearer），**不檢查 `CanRead("file")`**。零權限的 JIT-provisioned SSO 使用者可抓任何非 published 檔。由 v4 GUID key 不可枚舉緩解，且上傳預設 published。**修復：** 非 published 檔讀取改 gate `CanRead(FileCollection)`。

### SEC-6 — LOW（資訊性）— 上傳白名單預設空；S3/MinIO 物件未設 content-type
`appsettings.json` `AllowedContentTypes: []`（allow-all）；`S3FileStorage.SaveAsync` 未設 `ContentType`、presigned GET 未設 `ResponseContentDisposition`。實際 XSS 風險低（API 路徑強制 `Content-Disposition: attachment`，presigned 走獨立 origin）。**修復：** 出貨合理預設白名單；presigned 設物件 content-type + download disposition。

**安全 — 已核可無虞：** REST/GraphQL 寫入/刪除 RBAC 對稱；AdminOnly 阻擋自我提權；寫入剝除 ReadOnly/Hidden/IsSystem；soft-delete `?deleted` gate 雙協定要求 `CanDelete`；**revert 不會重注入未淨化 HTML**（重跑 Deserialize/SyncTranslations 再淨化，append-only，剝除 version）；自身欄位 H2 排除仍成立；SQL injection 面（ConditionalModel 參數化，唯一字串插值 ORDER-BY 子查詢的 locale 經 charset 驗證 + 單引號跳脫）；cookie flags（HttpOnly/Secure/SameSite）+ Redis 可撤銷 ticket；token 256-bit CSPRNG + SHA-256 存放，密碼 Argon2id；CSRF 自訂 header 全域先於 endpoint（涵蓋 `POST /graphql`）；introspection 非 Development 關閉；路徑穿越守衛 + OIDC returnUrl open-redirect 守衛。

---

## 🟠 C# 程式碼品質（CS）

### CS-1 — HIGH — GraphQL 於併發 resolver 間共用非 thread-safe SqlSugar client
**已確認。** GraphQL query+mutation root 明確 pin `DependencyInjectionScope.Request`（`GraphQlServiceCollectionExtensions.cs:56-60`，為共用 per-request 權限快照）；`ISqlSugarClient` 為 `AddScoped`；factory 回傳 `new SqlSugarClient(config)`（`SqlSugarClientFactory.cs:118`）—— **非** thread-safe 的 `SqlSugarScope`。HotChocolate 併發執行 sibling 欄位，兩個並行 root resolver（如 `{ articles{} tags{} }`）解析到同一 scoped `ItemService` → 同一 client → 交錯 ADO 操作。**失敗情境：** 選兩個以上 collection root 欄位時，間歇「connection already open」/交錯 reader / 被遮蔽的 500。測試皆單 root 欄位故未觸發。**修復：** factory 改回 `SqlSugarScope`（thread-safe 且維持 Request scope），或關閉 parallel resolver，或把權限快照移出 scoped provider 改用 Resolver scope。

### CS-2 — HIGH — 畸形 id 回 500（被遮蔽）而非 400/404，遍及所有 by-id 端點
**已確認。** `ConvertId`（`SqlSugarItemRepository.cs:790-808`）的 `Guid` 分支 `Guid.Parse(id)` 在 try/catch **之前**，畸形 guid 丟原始 `FormatException`；另一分支 catch 丟 `ArgumentException`。二者皆未在 `StruoExceptionHandler.Map` 映射 → `LogAndMask` → HTTP 500 + error-level log。遍及 `GetByIdAsync/UpdateAsync/DeleteAsync/RestoreAsync`（含 revert）。Application 端的孿生 `IdParsing.ParseTo`（`IdParsing.cs:15-22`）已正確包住兩分支丟 `QueryException`(400)—— 修復現成。**修復：** `Guid.Parse` 分支包進 try/catch 丟 `QueryException`（或直接重用 `IdParsing.ParseTo`）→ 畸形 400、合法但不存在維持 404，並止住 error log 洪水。

### CS-3 — MEDIUM — id-read 與 create DB 路徑丟失 CancellationToken
`GetByIdAsync`/`GetByIdGenericAsync`（`SqlSugarItemRepository.cs:224-238`）用 `InSingleAsync(id)` 無 ct；`CreateGenericAsync`（`278-289`）`ExecuteReturnEntityAsync()` 無 ct。其餘方法皆有轉發 —— 屬不一致。client 斷線無法取消這些查詢。**修復：** `GetByIdGenericAsync` 加 `ct` 參數改 `q.In(id).FirstAsync(ct)` 並轉發。

### CS-4 — MEDIUM — 投影熱路徑 per-row/per-field `PropertyInfo` 反射（= ARC-2）
`Project`（`ItemService.cs:1053-1096`，每列呼叫一次）內對每欄位做 `d.EntityType.GetProperty(prop)?.GetValue(entity)`；`ReadProp`（`310-315`）另加 IgnoreCase binding 掃描。100 列 × 20 欄 ≈ 2,000 次反射查找/請求，違反 §17.6。**修復：** 於 startup 在 `EntityDescriptor`/registry 快取 `PropertyInfo`（或 compiled getter）按名查找。

### CS-5 — LOW — `RevertAsync` 洩漏內層 `JsonDocument` 的 pool buffer
`ItemService.cs:716-717`：內層 `JsonDocument.Parse(rec.Snapshot)` 從未 dispose，其 `ArrayPool` buffer 不歸還（外層有 `using`）—— 與 `StripKeys` 註解意圖相反。每次 revert 洩一個 buffer。**修復：** `using var src = JsonDocument.Parse(rec.Snapshot);` 再 `StripKeys(src.RootElement, ...)`。

### CS-6 — LOW — revision number 無 DB 唯一約束 backstop（= DB-4）
`SqlSugarRevisionStore.cs:13-28` `MAX+1` 後 insert，靠寫入 transaction 內 row lock 序列化 —— 但無 `(CollectionName,ItemId,RevisionNumber)` 唯一 index。若序列化假設被弱化（未來非交易 capture 路徑，或 experimental backend），可靜默產生重複 revision no.。**修復：** migration/InitTables 加唯一 index。

### CS-7 — LOW — `LanguageProvider` 快取於併發 resolver 下有競態（複合 CS-1）
`LanguageProvider.cs:9-12` `_cache ??= ...` 於 CS-1（Request scope + 並行 resolver）下兩執行緒可同時進 `Load()` 撞 `db`。**修復：** 解 CS-1；或 `Lazy<>`/lock。

### CS-8 — LOW — `FileService.DeleteAsync` 用手動交易而非 nesting-safe helper
`FileService.cs:70-88` 用 raw `BeginTran/CommitTran` 而非 repository 的 `InTransactionAsync`。目前不在外層交易內故無 live bug，但日後被組進更大 unit-of-work 會提早 commit/rollback 外層。

**C# — 已核可無虞：** `EnvelopeResultFilter` 冪等、正確放行 204/FileResult/redirect/challenge、`SuppressMapClientErrors=true` 到位；`StruoExceptionHandler`/`StruoErrorFilter` 未映射例外遮蔽 + log；soft-delete filter 互動正確；樂觀鎖 CAS typed 參數；revision capture 於同一寫入交易內、revert 剝 version 且 append-only；**先前 disposed-JsonElement bug class 已避免**（Json 欄位自 raw text 重新解析、`ItemsController.Revision` 於 using 前 clone root）；GraphQL input coercion（`SentFieldsOnly`/`FoldTranslations`）正確且不可變；`InTransactionAsync` nesting guard；無 `async void`/`.Result`/`.Wait()`/吞例外 catch。

---

## 🟡 資料層 / ORM（DB）

### DB-1 — HIGH — purge（硬刪）產生孤兒 junction/translation/revision
`ItemService.DeleteAsync`（`ItemService.cs:635-671`）purge → `DeleteGenericAsync`（`SqlSugarItemRepository.cs:345-346`）`Deleteable<T>().In(id)` 只刪父列。**全 schema 無任何 FK constraint**（migration 與 InitTables 皆不產生），無級聯。**失敗情境：** purge `Article` → 其 `article_translations`/`article_tags`/`revisions` 全成永久孤兒；purge `Tag` 留下懸空 `article_tags.tag_id`。除儲存膨脹外，`revisions` 內留有已刪項目的**完整、無 RBAC** 快照資料。**修復：** purge 時包交易，依 M2M descriptor 刪 junction、依 FK 刪 translation、依 collection+itemId 刪 revisions（files 為共用媒體，刻意保留）；或加 `ON DELETE CASCADE` FK。

### DB-2 — HIGH — `OnDelete.SetNull`/`Cascade` 宣告了卻從未實作
只有 `Restrict` 有接（`RelationshipGraph.cs:62-67` 僅為 Restrict 建 inbound index；`ItemService.DeleteAsync:641-663` 只查 `InboundRestrict`）。enum 有三值（`OnDelete.cs:3`），sample 實際用 `SetNull`（`Article.Category`、`Category.Parent`）。**失敗情境：** purge 被文章參照的 `Category`，合約說 `SetNull` 但 `Article.CategoryId` 保留懸空 GUID；`Cascade` 靜默 no-op。soft-delete 於讀取遮蔽（全域 filter 隱藏 target 使 expansion 回 null），故只在硬 purge 時爆。**修復：** 實作 inbound `SetNull`（交易內 null 掉來源 FK）與 `Cascade`（遞迴刪來源）；未做前於文件標記兩者未實作。

### DB-3 — HIGH — FK、翻譯查找鍵、soft-delete flag 全無 index
全 schema 唯一 index 是 `ix_revisions_item`（`006:41`）。無任何 index attribute。seq-scan 熱路徑：`article_translations(articleid,locale)`（overlay/translatable-sort 相關子查詢/translatable search，後者 O(rows × 掃表)）、`article_tags(articleid)/(tagid)`（M2M expansion/sync）、`articles.categoryid`（O2M/Restrict）、`deletedat`（每次 list/get 帶 `WHERE deletedat IS NULL`）。**修復：** migration 為每個 M2O FK、junction FK、translation `(fk,locale)` 建 btree index；soft-deletable 表用 partial index `WHERE deletedat IS NULL`。

### DB-4 — MEDIUM — 非 `AuditableEntity` 的 revisioned collection 併發下 revision no. 可重複（= CS-6）
`CaptureAsync` `MAX+1`，index 非唯一（`006:41`）。序列化只來自 `UpdateGenericAsync` 的樂觀鎖 row lock，**僅當 entity 為 `AuditableEntity`**（`SqlSugarItemRepository.cs:313-330`）；非 `AuditableEntity` 落到無 CAS 的 plain update 路徑。**失敗情境：** 兩併發更新同一非 auditable revisioned item，各自讀 `MAX=5` 皆 insert 6，無唯一約束擋 → 兩筆 revision 6，`GetAsync(...6)` 回任意一筆，revert 非決定性。**修復：** index 改 `UNIQUE(collectionname,itemid,revisionnumber)`，敗方 insert 失敗回滾其寫入交易（正確結果）。

### DB-5 — MEDIUM — CodeFirst（InitTables）與 migration schema 分歧
`InitTables` 建表/欄位但無 index/唯一/check（無 index attribute）；所有 index/約束只在手寫 SQL migration。故 InitTables 建的 dev/test DB **沒有** `ix_revisions_item`，migration 建的 prod 有。若 DB-4 唯一約束只走 migration，dev 靜默不強制 → 遮蔽正要防的競態。**修復：** 讓兩者收斂（宣告 SqlSugar index attribute 使 InitTables 也產生，或 dev 也跑 SQL migration）；Development 啟動加 schema-diff/assert。

### DB-6 — MEDIUM — 無 migration runner/排序/追蹤，命名不一致
`db/migrations/` 混兩套編號（`0001__` vs `001-`）無跨套字典序，無 `schema_migrations` 追蹤表與 runner（手動套用）。各腳本冪等（`IF EXISTS`/`IF NOT EXISTS`）為佳，但無套用狀態紀錄與順序強制。**修復：** 統一零填充編號 + 輕量 runner + 追蹤表（或明文規定 fresh env 先 CodeFirst 再依序 migration）。

### DB-7 — LOW — 新欄位用 `timestamp`（無時區）
`005`（`deletedat timestamp`）、`006`（`createdat timestamp`）沿用既有慣例，值為 `DateTime.UtcNow`；無時區、跨時區讀取有歧義。**修復：** 新欄位統一 `timestamptz`（既有欄位不在範圍）。

### DB-8 — LOW — restore/soft-delete 繞過樂觀鎖與 revision 歷史
`RestoreGenericAsync`/`SoftDeleteGenericAsync`（`SqlSugarItemRepository.cs:361-414`）只按 id 更新 `deletedat`/`deletedby`，無 version 檢查/遞增，且不 capture revision。→ client 無法偵測發生過 trash/restore，歷史時間線無此紀錄。**修復：** 若 trash 轉換應入歷史，於 soft-delete/restore 遞增 version 並記 delete/restore revision。

### DB-9 — LOW — M2M 重複 id 誤判；revert 對已 trash 的 M2M target 失敗
`SyncM2MAsync`（`ItemService.cs:618-621`）以 `found.Count != targetIds.Count` 驗證，body `tags:[t1,t1]` → found=1 vs count=2 誤報「id 不存在」。另 revert 時對 M2M id 走 `QueryWhereInAsync` 套 soft-delete filter，已 trash 的 target 使 revert 失敗（snapshot 當時合法）。**修復：** 比對前去重 `targetIds`；revert 驗證時清 soft-delete filter（或文件化不對稱）。

### DB-10 — LOW — translation `(fk,locale)` 無唯一約束
`SyncTranslationsGenericAsync`（`SqlSugarItemRepository.cs:762-767`）delete-by-`(fk,locale)` 再 insert，無唯一 index；`OverlayTranslationsAsync`（`ItemService.cs:155`）last-wins 遮蔽重複。非 auditable translatable collection 併發（無 CAS，見 DB-4）可產生同 `(articleid,locale)` 兩列，讀取變順序相依。**修復：** migration + CodeFirst 加 `UNIQUE(articleid,locale)`。

**資料層 — 已核可無虞：** revision capture 共用寫入交易（nested M2M/translation join `db.Ado.Transaction`）；`AuditableEntity` 樂觀鎖 CAS typed 參數涵蓋 REST/GraphQL update + revert；soft-delete 全域 filter 涵蓋 list/get/count/三種 relation expansion，junction/translation/Revision 不實作 `ISoftDeletable` 故不誤篩；`?deleted=with|only` 正確 `ClearFilter` + typed IsNot-null；PG 型別轉換危害於新路徑已守（`CSharpTypeName` Guid→uuid/long→bigint、`ConditionalType.In`、typed-null `SetColumns(it=>new T{})`）；JSON 欄位 `IsJson + DataType=text`（避 varchar(1) 截斷），`Revision.Snapshot` text；nested-list push-down N+1-safe；migration 冪等、無破壞性 DROP、`0002` version backfill `DEFAULT 0` 避 23502。

---

## 🔵 架構 / 設計（ARC）

> 無 CRITICAL/HIGH —— 結構連貫、最新 phase 整合乾淨。以下為真實 MEDIUM/LOW 可維護性與原則遵循問題。

### ARC-1 — MEDIUM — `ItemService` 981 行 god class，`Deserialize` 單方法 ~250 行
`ItemService.cs:20-1097`，一個 sealed class 吃 14 個建構相依，涵蓋 query/get/投影/deep-expansion 編排+驗證/translation overlay+sync/per-locale 圖片解析/M2M sync/create/update/delete/restore/revert/revision capture/RichText 淨化/寫入 body 反序列化+per-interface 驗證。`Deserialize`（`:798-1051`）單方法跑八段 per-field-interface 驗證迴圈。此為全 codebase 最高 churn 檔（7g+/8b/9b/9c 都動），違反自身「函式 <50 行 / 檔 <800」。parent-field 與 `SyncTranslationsAsync`（`:459-548`）per-locale 已重複 Required/MaxLength 邏輯。**修復：** 抽 write pipeline —— `ItemDeserializer` + per-interface `IFieldValidator` 策略（registry keyed by `FieldInterface`，對映前端 `lib/fieldTypes/`）；讀取面抽 `ItemProjector`/`TranslationOverlay`/`DeepExpansionCoordinator`。

### ARC-2 — MEDIUM — per-request 反射：descriptor 只快取名稱不快取 accessor（= CS-4，違 §17.6）
`EntityDescriptor` 只有 `FieldToProperty`（camel→CLR 名）與 `IdProperty`，無 `PropertyInfo`（`IEntityRegistry.cs:3-6`）。metadata *模型*有快取，但屬性 accessor 解析沒有 —— descriptor 停在最後一步。**修復：** `MetadataScanner.ScanDescriptors`（`MetadataScanner.cs:99-139`）另建 `IReadOnlyDictionary<string,PropertyInfo>`（理想上 compiled `Func`/`Action` getter/setter）存於 `EntityDescriptor`，呼叫端改用快取 delegate。零行為改變。

### ARC-3 — MEDIUM — 例外→error-code 映射於 REST 與 GraphQL 重複且已漂移
REST 走 `StruoExceptionHandler.Map` 用 `ErrorCodes` 常數（`StruoExceptionHandler.cs:26-40`）；GraphQL 於 `StruoErrorFilter.OnError` 用硬編字串重實作同一 switch（`StruoErrorFilter.cs:19-41`）。**已不同步**：REST 對未認證的 `PermissionDeniedException` 回 `UNAUTHORIZED`，GraphQL 一律 `FORBIDDEN`。同型重複亦見於「viewing deleted 需 delete 權限」gate（`ItemsController.cs:56-62` vs `CollectionResolvers.cs:64-65`）。**修復：** 抽共用純函式 `DomainErrorMap.Map(Exception)→(code,message)` 回 `ErrorCodes` 常數，REST 疊 HTTP status、GraphQL 疊 `.WithCode`。

### ARC-4 — LOW — `SqlSugarItemRepository`（832 行）混泛型 dispatch 與手寫 ORDER-BY 子查詢
`SqlSugarItemRepository.cs`：14 個快取 `MethodInfo` + per-call `MakeGenericMethod().Invoke()`（型別抹除 dispatch）與 raw SQL 字串建構（`RelationOrderExpr:901-940`、`TranslatableOrderExpr:859-889`）兩種不同關注點同居一檔。vendor-SQL 面已是接受項 D9 不重列，僅低內聚為新發現。**修復：** SQL-text builder 抽 `OrderByExpressionBuilder`；考慮快取 open-generic delegate table 取代重複 `MakeGenericMethod().Invoke()`。

### ARC-5 — LOW — Options 綁定但啟動不驗證（無 fail-fast）
`DatabaseOptions`/`StruoQueryOptions`/`OidcOptions`/`FileStorageOptions` 皆 `Configure` 但全 src 無 `ValidateOnStart`/`ValidateDataAnnotations`。缺失/空連線字串在首次 DB/檔存取才爆（困惑的首請求 500）而非開機失敗。**修復：** 核心 options 加 `.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`，連線字串加 `[Required]`。

### ARC-6 — LOW — 抽象不對稱：GraphQL 有可 fake 接縫，REST controller 直依具體 981 行 `ItemService`
GraphQL resolver 依 `IGraphQlDataSource`（`GraphQlDataSource.cs:14-25`）；REST `ItemsController` 直注具體 `ItemService`（`ItemsController.cs:14`）。controller 測試被迫穿過完整具體服務（14 相依）。**修復：** `ItemsController` 改依接縫（可 rename `IItemUseCases` 移至 Application），或明確接受耦合。低優先。

**架構 — 已核可無虞：** 專案參照嚴守 §2（Domain 零套件；sample 標 DEV-ONLY）；Domain 型別純淨（無 SqlSugar/HTTP/HotChocolate 型別）；metadata startup-only 掃描進不可變 singleton；GraphQL 為真薄 adapter（接縫刻意留 Api 不洩入 Application）；schema 於 boot 建一次、query+mutation pin Request scope；soft-delete 單點全域 filter；revisions 設計乾淨（非 CmsCollection/非 ISoftDeletable、capture 於寫入交易、per-item no. 受樂觀鎖保護、RBAC-bypass 為刻意文件化限制）；`ICurrentUserAccessor` singleton over `IHttpContextAccessor` 無 captive dependency；envelope pipeline 排序正確（`UseExceptionHandler` 上游於 CSRF/permission、`SuppressMapClientErrors=true`）；`SchemaTypeMapper` 單一真相來源、`SchemaService` 剝 hidden；`MetadataScanner` 於 startup fail-fast（重複名/缺翻譯慣例/非法 translatable/未知 DefaultDisplayField）。

---

## 🟢 前端 SPA（FE）

### FE-1 — HIGH — `CollectionListView` 未載入 schema store，深連結/重新整理時清單永久空白
`CollectionListView.vue:63-89,159-168`：`loadItems()` 在 `meta.value` falsy 時 early-return，`meta`（`:29`）讀 `schemaStore.get(name)`。但此 view **從不呼叫 `schema.load()`**（只有 `AppShell.onMounted` 與 `ItemFormView.init` 有）。硬重新整理/書籤深連結到 `/collections/:name` 時，子 view `onMounted(loadItems)` 早於父的 async `schema.load()` resolve，`meta` undefined、`loadItems` bail，且 **`meta` 無 watcher 重試**（只有 `watch(name)`）。schema 到達後 `v-if="!meta"` 翻成表格但 `loadItems` 不再被呼叫 → 表格恆空（"No records"）。對比 `ItemFormView` 有 `await schema.load()` 故 edit 深連結正常 —— 不對稱即線索。unit test 因直接 seed `schema.collections` 遮蔽此 bug。**修復：** `loadItems` 頂 `await schema.load()`（store 靠 `loaded` flag 去重），或加 `watch(meta, () => loadItems())`。

### FE-2 — MEDIUM — 清單 stale-response 競態（無請求排序/取消）
`CollectionListView.vue:63-89,91-128,159-166`：`loadItems` 由 page/sort/search(debounce 300ms)/mode/`watch(name)` 觸發，回應無條件套用（`rows.value = res.data`），無 request-id guard 或 `AbortController`。較慢的舊請求晚於新請求 resolve 會覆蓋狀態（快速切 Article→Tag、或打 "a" 再 "ab"）。**修復：** 遞增 request token，忽略非最新回應；理想上 abort in-flight。

### FE-3 — MEDIUM — 伺服器端驗證 `error.details` 被靜默丟棄
`ItemFormView.vue:76-92`、`ItemForm.vue:47,76`：`ApiError` 帶 `code` 與 `details: ValidationDetail[]`，但 form 只讀 `e.code==='NOT_FOUND'`，其餘全塌成 `serverError.value = e.message`。欄位級 `errors[…]` 只由 client 端 `validateItem` 填。伺服器端獨有驗證（重複 slug、唯一性、商規）回的 `details` 從不映回 `errors[field]`。**修復：** `ApiError` 有 `details` 時先依 field name 併入 `errors` 再退回 banner。

### FE-4 — MEDIUM — 樂觀鎖 409 無恢復路徑
`ItemFormView.vue:82-91`、`buildItemPayload.ts:56-58`：`version` 正確 echo，伺服器可回 409 —— 但 catch 只設 `serverError`，從不重抓刷新 `model.version`。使用者卡住：重送同 stale version 再 409，除整頁 reload 無 in-app 和解方式。**修復：** conflict code 時重抓 item（或提供「reload latest」）更新 version 並警告有遠端變更。

### FE-5 — MEDIUM — 未存編輯資料遺失（無 dirty-state 守衛）
`ItemFormView.vue:110-112`、`ItemForm.vue`：無 `onBeforeRouteLeave`/`beforeunload`。Cancel、導覽選單點擊、瀏覽器 back 靜默丟棄跨共用欄位/所有 locale tab/relation 選擇的編輯。長 rich-text/repeater 表單為真實資料遺失。**修復：** 追蹤 dirty flag（model vs loaded baseline），離開/unload 前確認。

### FE-6 — MEDIUM — Relation/Files picker：未 debounce、未排序的 filter 搜尋
`RelationPicker.vue:95,127,141`、`FilesField.vue:107`：`@filter`→`search=e.value`→`watch(search,loadOptions)` 每次擊鍵發請求，無 debounce、無排序 guard，`options.value=res.data` 可被 stale 回應覆蓋（同 FE-2 類）並轟炸 API。**修復：** debounce + latest-wins token（共用 FE-2 helper）。

### FE-7 — LOW — 搜尋 debounce timer 未於 unmount/切換 collection 清除
`CollectionListView.vue:104-112,159-166`：`searchTimer` 未於 `onUnmounted`/`watch(name)` 清除，pending timer 可於卸載後 fire 或以重置 `search=''` 打剛切換的 collection。今日無害但潛在。**修復：** `onUnmounted` 與 `watch(name)` 頂 `clearTimeout`。

### FE-8 — LOW — `MediaLibraryView` 刪除無確認且無 client 端權限 gate
`MediaLibraryView.vue:31-38,54-59`：`onDelete` 直接 `filesApi.remove` 無 `confirm.require`（與 `deleteAction.ts` 到處的守衛流程不一致）；Delete/Edit 按鈕不論 `canDelete`/`canWrite` 恆顯示（伺服器仍擋但誘發 403）。一次誤點永久刪檔。**修復：** 走確認對話框、按鈕 gate `canDelete('file')`。

### FE-9 — LOW — 死 scaffold 元件含不安全外部連結
`HelloWorld.vue:37-82`：Vite starter 遺留、無處 import；多個 `<a target="_blank">` 無 `rel="noopener noreferrer"`（reverse-tabnabbing）。**修復：** 刪檔。

### FE-10 — LOW — RichText 連結插入 client 端接受任意 URL
`RichTextInput.vue:110-117`：`setLink` 把 raw `window.prompt` 值直入 `setLink({href:url})`。TipTap `protocols:['http','https','mailto']` 白名單 + 伺服器端淨化為真防線，且 SPA 無 `v-html` 渲染路徑故無 in-app XSS —— 但 `javascript:` href 仍可被存，取決於公開 renderer 是否遵守淨化。**修復：** `setLink` 前 client 端驗證 protocol（縱深防禦）。

### FE-11 — LOW — apiClient 變數遮蔽 + list envelope 假設
`apiClient.ts:86`、`itemsApi.ts:26-27`：`let body` 遮蔽 request 參數 `body`（不 bug 但混淆）；`itemsApi.list` 做 `res.meta.total` 無守衛，若 list 端點無 `meta` 丟原始 `TypeError`。**修復：** 改名 `errBody`；`res.meta?.total ?? res.data.length`。

### FE-12 — LOW — 選中的 relation 值若不在當前選項頁則渲染為 raw id
`RelationPicker.vue:57-70,131-143`：`ensureSelectedLabels` 填 `labelById`，但 `Select`/`MultiSelect` 由 `options`（上限 25 列/被搜尋過濾）顯示 label。選中項不在已載入 `options` 時顯示 id 而非 label（`labelById` 算了卻未接進顯示）。**修復：** 併選中 id 的 label 列進 `options`。

**前端 — 已核可無虞：** envelope 處理正確（依 `success` 分支、`getRaw` 回完整 envelope 供 list meta、非 list 解 `.data`）；**XSS 面零 `v-html`/`innerHTML` sink**（RichText 經 TipTap 編輯、從不 raw 渲染）；boundary 型別安全無 `any`；field-type registry 為 total `Record` 且未知 interface 退回 `readonlyDef`；store 不可變（action 換新狀態、元件 emit 新陣列）；version echo 正確；**locale tab 切換不丟已輸入值**（`v-model` 綁持久 reactive、`v-if` 只卸載輸入）；CSRF presence-only header 正確加於非安全方法；401 中央 `onUnauthorized` 清 user + 導 login；schema/language store catch 並曝 `loadError` + 重試；上傳 `Promise.all` 隔離 per-file 錯誤。

---

## 附錄：本次未重列的既有決策
- **已接受風險（前次稽核，仍成立）：** H1（rate limiting 缺席，與 SEC-4 相關）、M3（SSO email-merge，部署須 pin tenant/domain）、D7/D10（純效能）、D8（單語系輸出取捨）、A2（大重構）。
- SEC-3 為憑證處理風險（非 repo 洩露，檔案 gitignored 從未提交）。
