# 2026-07-15 稽核修復 Batch 2 — 後端/資料 MEDIUM(細部計畫)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development。母計畫:`2026-07-15-audit-remediation.md`(Global Constraints 全數適用)。審核報告:`docs/architecture-audit-2026-07-15.md`。

**範圍:** DB-6 → DB-4 → DB-5 → CS-3 → CS-4(=ARC-2) → ARC-3,共 6 tasks + 批次 gate。
**基線:** 後端 683 綠 / 前端 270 綠(本批不動前端)。每 task 結束不得低於基線。
**模型分工(user 2026-07-15 指示,覆蓋母計畫預設):** 實作 subagent = **opus**;審核/確認 subagent = **fable**。

## 已拍板決策(user 2026-07-15)

- **DB-6:** 輕量 runner + `schema_migrations` 追蹤表(非純文件規範)。

## 本批共通事實(確認 agent 2026-07-15 查核,post-Batch-1)

- `db/migrations/` 現有 9 檔,混兩套編號:`0001__widen_content_bearing_text_columns.sql`、`0002__retroactive_add_version_columns.sql` 與 `001-…`~`007-hot-path-indexes.sql`。無任何 runner / `schema_migrations`。
- `ix_revisions_item`(`006-revisions-table.sql:41`)為**非 unique**;`revisions` 物理欄名 = 全小寫 `collectionname, itemid, revisionnumber`(非 snake_case)。migration 008 尚不存在。
- CodeFirst unique 的既有機制 = `[SugarColumn(UniqueGroupNameList = …)]`(`User.cs`/`Role.cs`/`UserRole.cs`/`Permission.cs` 已用);全 repo 零 `[SugarIndex]` 使用。SqlSugarCore 5.1.4.215。
- InitTables 雙重 dev-gate:`Program.cs:98-105` + `DatabaseInitializer.cs:8-21`(非 dev 直接 throw)。
- `SqlSugarItemRepository.cs` 缺 ct 僅剩兩處:`GetByIdGenericAsync`(`:253` `InSingleAsync(id)`,private 無 ct 參數,`MethodInfo` pin `[object, DeletedFilter]`)與 `CreateGenericAsync`(`:305` `ExecuteReturnEntityAsync()` 無 ct)。`FileService.cs` 另有 `:58 ExecuteReturnEntityAsync()`、`:62/:66 InSingleAsync(id)`。`IItemRepository` 介面全數已收 ct。Seeder 為 startup 一次性,不在範圍。
- `EntityDescriptor` = `record(Type EntityType, IReadOnlyDictionary<string,string> FieldToProperty, string IdProperty)`(`IEntityRegistry.cs:3-6`),singleton registry,startup 掃描(`MetadataScanner.ScanDescriptors:99-139`)。熱路徑反射:`ItemService.Project`(`:1151` id、`:1169` per-row×per-field)、`ReadProp`(`:310-315` IgnoreCase,呼叫點含 relation expansion `:234/:238`、repeater `:1109/:1118`)、`RevisionSnapshotBuilder`(`:41/:53/:67/:94` + `ReadProp :112-114`)。
- REST `StruoExceptionHandler.Map` 已是 `public static (int, ErrorBody) Map(Exception, bool authenticated, ILogger?)`(`:26-40`);GraphQL `StruoErrorFilter.OnError`(`:16-41`)硬編字串重複同表,**漂移確認**:unauthenticated `PermissionDeniedException` REST→401/UNAUTHORIZED、GraphQL→一律 FORBIDDEN。viewing-deleted gate 重複:`ItemsController.cs:59-60` vs `CollectionResolvers.cs:64-65`(同字串)。例外全在 **Struo.Domain**;`ErrorCodes`/`Envelope` 在 **Struo.Api/Http**。
- 測試慣例:unit-store fixture = `SqliteTestDatabase` + `SqlSugarClientFactory.Create` + `InitTables`(見 `RevisionStoreTests.cs:14-35`);整合 = `Support/ApiFactory`(collection fixture `"ApiIntegration"`,env=Development,SQLite temp file);GraphQL 整合 = 打 `/graphql` 過真 pipeline。

---

## Task 1: DB-6 — migration 編號統一 + 輕量 runner + schema_migrations 追蹤表

**Files:**
- Rename: `db/migrations/*`(git mv,統一單一 3 位數 `NNN-description.sql` 方案)
- Create: `src/Struo.Infrastructure/Persistence/MigrationRunner.cs`
- Modify: `src/Struo.Infrastructure/Persistence/DatabaseOptions.cs`(+ `MigrationsPath`,string?,預設 null=停用)
- Modify: `src/Struo.Api/Program.cs`(startup 接線)
- Create: `tests/Struo.Tests/Persistence/MigrationRunnerTests.cs`
- Modify: `db/migrations/README.md`(若無則建:編號規範 + fresh-env 程序)

**Interfaces(Produces):**
- `static Task<IReadOnlyList<string>> MigrationRunner.ApplyAsync(ISqlSugarClient db, string migrationsDirectory, ILogger? logger, CancellationToken ct)` — 回傳本次實際套用之檔名清單。
- 語意:僅 `DbType == PostgreSQL` 時執行(SQLite 測試/其他 DB 直接 no-op 並 log);自建 `schema_migrations (filename text PRIMARY KEY, appliedat timestamptz NOT NULL)`(**timestamptz** — DB-7 慣例,新表即遵循);讀目錄 `*.sql` 依 ordinal 檔名排序;跳過已記錄檔名;每檔一交易(執行 SQL + insert 追蹤列同交易,失敗 rollback 且中止後續);raw SQL 執行屬 Global Constraints 允許之 migration-檔類。
- 接線:`Program.cs` 於現有 dev 區塊 InitTables **之後**、seeder 之前呼叫(`MigrationsPath` 有值且 PG 才生效);非 dev 環境亦允許呼叫(reviewed scripts 於 prod 套用即為 runner 的存在意義)— 置於 dev 區塊外、以 `MigrationsPath` 配置驅動。`appsettings.json` 加註解樣例(值留空)。
- 編號統一:以 `git log --follow` 判定 `0001__`/`0002__` 兩檔的實際時序,將全部 9 檔重排為單一連號 `001-…` 系列(既有 001-007 內容相依順序不得破壞:005 soft-delete 欄位先於 007 partial index;006 revisions 先於未來 unique);全檔冪等,重命名後於既有 live DB 重放安全。追蹤表為新表,無舊紀錄相容問題。

**Steps:**
- [ ] Step 1(RED): `MigrationRunnerTests` — (a) SQLite 上 no-op 且不建表;(b) 排序/跳過語意用假目錄 + 純函式面測(檔名排序、已套用過濾邏輯抽 internal static 純函式測);(c) 失敗中止:第 N 檔壞 SQL → 前 N-1 檔已記錄、第 N 檔未記錄(此條 PG-only 行為,SQLite 可測純函式部分,交易語意留 live gate)
- [ ] Step 2: 實作 runner + options + Program.cs 接線
- [ ] Step 3: 檔案重命名(git mv)+ 各檔 header 註解更新 + README 規範
- [ ] Step 4: 全量回歸 ≥683
- [ ] Step 5: Commit `feat(db): migration runner with schema_migrations tracking + unified numbering (DB-6)`

## Task 2: DB-4(=CS-6) — revision number UNIQUE backstop

**Files:**
- Create: `db/migrations/<next>-revisions-unique-number.sql`(Task 1 重排後的下一號)
- Modify: `src/Struo.Infrastructure/Revisions/Revision.cs`(unique 屬性)
- Test: `tests/Struo.Tests/Revisions/RevisionStoreTests.cs`(+ 併發/重複 no. 測試)

**Interfaces(Produces):**
- Migration:先去重(重複 `(collectionname,itemid,revisionnumber)` 保留 `createdat` 最早、tie-break `id` 最小者,其餘刪除),再 `DROP INDEX IF EXISTS ix_revisions_item;` + `CREATE UNIQUE INDEX IF NOT EXISTS ux_revisions_item_no ON revisions (collectionname, itemid, revisionnumber);`(原非 unique index 為其前綴,直接以 unique 版取代)。
- CodeFirst 收斂(本項只做 revisions 這一索引;其餘見 Task 3):`Revision` 的三欄加 `[SugarColumn(UniqueGroupNameList = new[]{"ux_revisions_item_no"})]`(沿用 Identity 實體既有機制)。**風險點:** 既有 dev PG DB 若已有重複列,InitTables 更新 unique 會炸 — dev 啟動順序為 InitTables → runner,實作者須驗證:InitTables 對既有表是否嘗試補建 unique;若會,改為 runner 先行(Task 1 接線調整)或於 migration 註明先手動套用。以測試/現地驗證擇定,並記錄於 migration header。
- 敗方語意:併發重複 no. → unique violation → 該寫入交易 rollback(revision capture 與本體更新同交易,整體失敗即正確結果,不需 retry 邏輯 — YAGNI)。

**Steps:**
- [ ] Step 1(RED): SQLite fixture(InitTables 產 unique 後)direct insert 兩筆同 `(collection,item,no.)` → 第二筆丟 DB 例外;`CaptureAsync` 正常遞增不受影響
- [ ] Step 2: 實作 entity 屬性 + migration 檔
- [ ] Step 3: 全量回歸 ≥683(revisions 既有測試全綠)
- [ ] Step 4: Commit `fix(db): UNIQUE(collectionname,itemid,revisionnumber) backstop for revision numbers (DB-4, CS-6)`

## Task 3: DB-5 — CodeFirst/migration schema 收斂 + dev fail-fast 守衛

**Files:**
- Modify: `samples/Struo.Sample.Blog/*.cs`、`src/Struo.Infrastructure/{Identity,Files,Localization}/*.cs` 相關實體(`[SugarIndex]` 屬性,對映 007 的一般 btree 索引)
- Create: `src/Struo.Infrastructure/Persistence/SchemaGuard.cs`
- Modify: `src/Struo.Api/Program.cs`(dev 啟動於 InitTables+runner 後呼叫)
- Test: `tests/Struo.Tests/Persistence/SchemaGuardTests.cs`(+ InitTables 索引產出斷言)

**Interfaces(Produces):**
- `[SugarIndex("ix_<table>_<cols>", nameof(Prop), OrderByType.Asc)]` 逐一對映 `007-hot-path-indexes.sql` 中可表達者(M2O FK、junction 雙向、translation `(fk,locale)` 複合、identity 熱路徑);**partial index(`WHERE deletedat IS NULL`)無法以屬性表達 → 維持 migration-only**,於 007 header 註明此不對稱為既知。索引名與 007 完全一致(冪等重疊安全)。實作者先以最小 spike 確認 5.1.4.215 的 `[SugarIndex]` 在 InitTables 下對 PG/SQLite 均生效(不生效則回報 BLOCKED,勿硬上)。
- `static Task SchemaGuard.AssertCriticalConstraintsAsync(ISqlSugarClient db, CancellationToken ct)` — 斷言關鍵約束存在(至少:`revisions` 的 unique 索引;PG 查 `pg_indexes`,SQLite 查 `sqlite_master`),缺失即 throw(fail-fast on 分歧)。僅 dev 啟動呼叫(接於 InitTables/runner 之後)。

**Steps:**
- [ ] Step 1(RED): SQLite InitTables 後斷言 `sqlite_master` 含對映索引(現況無 → FAIL);SchemaGuard 對缺 unique 的 DB throw、對完整 DB 通過
- [ ] Step 2: spike + 屬性鋪設 + SchemaGuard 實作 + Program.cs 接線
- [ ] Step 3: 全量回歸 ≥683
- [ ] Step 4: Commit `feat(db): CodeFirst index parity via SugarIndex + dev SchemaGuard fail-fast (DB-5)`

## Task 4: CS-3 — CancellationToken 一致轉發

**Files:**
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`(`GetByIdGenericAsync` 加 ct 參數:`InSingleAsync(id)` → `q.In(id).FirstAsync(ct)`,同步更新 `GetByIdGenericAsyncDef` MethodInfo pin 之簽名陣列與 invoke 引數;`CreateGenericAsync` 加 ct:`ExecuteReturnEntityAsync()` → 帶 ct 之對應 API,同步更新 `CreateGenericAsyncDef`)
- Modify: `src/Struo.Infrastructure/Files/FileService.cs`(`:58` `ExecuteReturnEntityAsync()`、`:62/:66` `InSingleAsync(id)` 同法補 ct)
- Test: 既有 repository/FileService 測試 + 新 ct 轉發測試

**Interfaces:**
- `IItemRepository` 介面不變(已全收 ct)。SqlSugar 若無 `ExecuteReturnEntityAsync(ct)` 過載,以等效帶-ct API 達成(如 `ExecuteCommandAsync(ct)` + 讀回,或該 API 的 ct 過載 — 實作者以 5.1.4.215 實際 API 為準,不得手寫版本假設)。`BeginTranAsync/CommitTranAsync/RollbackTranAsync` 無 ct 過載即不動(於 `InTransactionAsync` 註解記錄)。
- 驗收含 grep 稽核:`src/` 內所有 ORM async 呼叫(排除 startup seeder 與無 ct 過載之 tran API)皆帶 ct。

**Steps:**
- [ ] Step 1(RED): 已取消之 ct 傳入 `GetByIdAsync`/`CreateAsync`/`FileService.GetAsync` → `OperationCanceledException`(現況:查詢照跑 → FAIL)
- [ ] Step 2: 實作三處 + MethodInfo pin 更新
- [ ] Step 3: 全量回歸 ≥683 + grep 稽核輸出附於 report
- [ ] Step 4: Commit `fix: forward CancellationToken through by-id read and create DB paths (CS-3)`

## Task 5: CS-4(=ARC-2) — descriptor 快取 property accessor,熱路徑去反射

**Files:**
- Modify: `src/Struo.Application/Metadata/IEntityRegistry.cs`(`EntityDescriptor` + `IReadOnlyDictionary<string, PropertyInfo> Properties`,key=CLR 屬性名,`OrdinalIgnoreCase`)
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`(`ScanDescriptors` 建 Properties;`SqlSugarItemRepository.BuildTranslationDescriptor:787-804` 同步補齊)
- Modify: `src/Struo.Application/Query/ItemService.cs`(`Project:1145-1188` 的 `:1151/:1169` 改查 `d.Properties`;`ReadProp:310-315` 改用 process-wide `ConcurrentDictionary<(Type, string), PropertyInfo?>` 快取 — relation-target/repeater child 不一定有 descriptor 在手,型別鍵快取為零行為改變的最小解)
- Modify: `src/Struo.Application/Query/RevisionSnapshotBuilder.cs`(`:41/:53/:67/:94` 改查 descriptor Properties;`ReadProp:112-114` 共用同一型別鍵快取)
- Test: `tests/Struo.Tests/Metadata/`(descriptor Properties 斷言)+ 既有投影/revision 測試全綠即行為不變證明

**Interfaces:**
- **零行為改變**:大小寫語意維持現狀(`Project` 現走精確 CLR 名、`ReadProp` 現 IgnoreCase — 快取後語意逐點相同)。`EntityDescriptor` 為 record,新增成員需同步所有建構點(scanner、translation descriptor、測試 fixture)。
- 驗收:grep `ItemService.cs`/`RevisionSnapshotBuilder.cs` 之 per-row/per-field 路徑(`Project`/`ReadProp`/snapshot 欄位迴圈)無裸 `GetProperty(`;寫入路徑(per-write 迴圈,ARC-1/Batch 5 範圍)與 GraphQL `StruoTypeModule` 不在本 task 範圍。

**Steps:**
- [ ] Step 1(RED): descriptor Properties 存在性/IgnoreCase 測試(現況無此成員 → 編譯期 RED 即失敗測試)
- [ ] Step 2: scanner + descriptor 實作
- [ ] Step 3: 呼叫端改線(Project/ReadProp/RevisionSnapshotBuilder)
- [ ] Step 4: 全量回歸 ≥683(投影/i18n/repeater/revision 全綠 = 行為不變)+ grep 證據
- [ ] Step 5: Commit `perf: cache property accessors on EntityDescriptor — no per-row reflection in projection hot path (CS-4, ARC-2)`

## Task 6: ARC-3 — 例外→error-code 映射單一真相

**Files:**
- Create: `src/Struo.Api/Http/DomainErrorMap.cs`(pure static;兩消費者皆在 Api,層次合法)
- Modify: `src/Struo.Api/Http/StruoExceptionHandler.cs`(`Map` 改委派 DomainErrorMap,疊 HTTP status)
- Modify: `src/Struo.Api/GraphQl/StruoErrorFilter.cs`(改委派,疊 `.WithCode`;硬編字串全刪)
- Create: `src/Struo.Application/Query/DeletedAccessGuard.cs`(viewing-deleted gate 單一實作;`DeletedFilter`/`IPermissionService` 皆 Application 可及)
- Modify: `src/Struo.Api/Controllers/ItemsController.cs:59-60`、`src/Struo.Api/GraphQl/CollectionResolvers.cs:64-65`(改呼叫 guard)
- Test: 對照測試(單元:同一例外 → DomainErrorMap 單一結果;整合:REST 與 GraphQL 對 unauthenticated PermissionDenied 皆回 `UNAUTHORIZED`)

**Interfaces(Produces):**
- `static (string Code, string Message) DomainErrorMap.Map(Exception ex, bool authenticated)` — 回 `ErrorCodes` 常數;未映射型別回 `(Internal, <masked>)`(遮蔽語意維持在 handler 的 LogAndMask,Map 不 log)。
- `static void DeletedAccessGuard.EnsureCanViewDeleted(IPermissionService permissions, string collection, DeletedFilter deleted)` — 非 Exclude 且無 delete 權限 → 丟 `PermissionDeniedException`(訊息維持現字串)。
- **漂移修正(REST 語意為準):** GraphQL 對 unauthenticated `PermissionDeniedException` 改回 `UNAUTHORIZED` — `StruoErrorFilter` 需取得 authenticated(注入 `IHttpContextAccessor`,無 HttpContext 時視為 authenticated=false 的保守值?否 — 無 context 即非 HTTP 情境,依 `ClaimsPrincipal` 缺席視為 unauthenticated;實作者以整合測試釘死兩情境)。GraphQL error payload 僅疊 code,不含 HTTP status(GraphQL 200 語意不變)。
- REST status 推導:code→status 對映併入 DomainErrorMap 或 handler 內 switch(單處);`ErrorCodes.ForStatus` 既有方向不動。

**Steps:**
- [ ] Step 1(RED): 單元 — `DomainErrorMap.Map` 全型別表(5 例外 × authenticated 二值);整合 — GraphQL 匿名打權限保護查詢 → error code `UNAUTHORIZED`(現況 FORBIDDEN → FAIL)+ REST 同情境對照
- [ ] Step 2: DomainErrorMap + 雙 handler 委派 + DeletedAccessGuard 改線
- [ ] Step 3: 全量回歸 ≥683(`StruoExceptionHandlerTests`/`StruoErrorFilterTests`/`ErrorEnvelopeEndpointTests` 全綠)
- [ ] Step 4: Commit `refactor: single-source exception-to-error-code map for REST and GraphQL — fix UNAUTHORIZED drift (ARC-3)`

---

## Batch 2 Gate(全批完成後)

- [ ] `dotnet test` 全綠(≥683 + 新增);前端不動(270 基線不驗證亦不得碰)
- [ ] **Live gate(真 PG,`ASPNETCORE_URLS=:5080`):**
  1. DB-6:fresh 追蹤表 → runner 套用全部 migrations → `SELECT * FROM schema_migrations` 齊全;重跑 no-op
  2. DB-4:併發雙更新非 auditable revisioned item → 一方失敗、`SELECT … GROUP BY … HAVING count(*)>1` 零重複
  3. DB-5:InitTables 後 `pg_indexes` 含屬性產出之索引 + SchemaGuard 通過
  4. CS-3:抽查(單元已足,live 不特測)
  5. ARC-3:REST vs GraphQL 匿名/有權/無權三情境 code 對照一致
- [ ] 更新 `docs/architecture-audit-2026-07-15.md`:DB-4/5/6、CS-3、CS-4(+CS-6/ARC-2 交叉標)、ARC-3 標 ✅FIXED(附 commit)
- [ ] Merge to main
