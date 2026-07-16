# 2026-07-16 稽核修復 Batch 4 — LOW 批次 + Batch 3 帶入項(細部計畫)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development。母計畫:`2026-07-15-audit-remediation.md`(Global Constraints 全數適用)。審核報告:`docs/architecture-audit-2026-07-15.md`。

**範圍:** 後端 8 tasks(CS-5/7/8、SEC-4/5/6、DB-8、DB-9+10+7、ARC-5、ARC-4、ARC-6、API-1-BE)+ 前端 5 tasks(FE-7+fold-ins、FE-8+9、FE-10/11/12+(d)(e)、NAV-1+(c)、API-1-FE+e2e minors)+ 批次 gate。
**基線:** 後端 **732** 綠 / 前端 **312** 綠 + `pnpm build` 綠 / e2e 12/12。每 task 結束不得低於基線。
**模型分工(user 2026-07-16 指示):** 實作 subagent = **opus**;審核/確認 subagent = **fable**。SEC-* task 加 security-reviewer pass。
**分支:** `audit-batch4`(自 main 開)。每 task 一 commit。

## 已拍板決策(user 2026-07-16)

1. **DB-8:** trash/restore **遞增 version + 記入 revision 歷史**(operation = `delete` / `restore`)。
2. **SEC-3:** **跳過**(Development.json 未進 git、僅本地開發,user 判定無疑慮)— 審核報告標 DEFERRED (user decision)。
3. **CS-7:** 加 `Lazy<>` 防護。
4. **NAV-1 + API-1:** 都納入本批。

## 本批共通事實(fable 確認 agents 2026-07-16 查核,post-Batch-3;行號以此為準,audit 報告行號已 stale)

### 後端
- `ItemService.cs` = 1189 行;`SqlSugarItemRepository.cs` = 1066 行。整合測試 fixture = `tests/Struo.Tests/Support/ApiFactory.cs`(WebApplicationFactory、SQLite temp-file、`CreateAuthenticatedClientAsync`/`CreateEditorClientAsync(readCollections, writeCollections)`/`CreateRolelessClientAsync`;`[Collection("ApiIntegration")]`)。GraphQL 測試 `PostAsJsonAsync("/graphql", new { query })`。
- **SEC-4:** `GraphQlServiceCollectionExtensions.cs`:48 只有 `AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)`;`HotChocolate.CostAnalysis` 16.4.0 **已是 transitive 依賴**(經 `HotChocolate.AspNetCore` 16.4.0 → `HotChocolate` meta)— `AddCostAnalyzer()` / `ModifyCostOptions(Action<CostOptions>)` 可直接用,**零新套件**。`CostOptions` 有 `MaxFieldCost/MaxTypeCost/EnforceCostLimits/ApplyCostDefaults/DefaultResolverCost/Filtering/Sorting`。**`MaxAllowedNodeBatchSize` 與 v13 Complexity API 在 16.4.0 不存在**;無專用 alias 上限 rule — cost analysis 即是 alias 放大的對策(alias 疊加 field cost)。
- **SEC-5:** `FilesController.cs`:25 `private const string FileCollection = "file";`(Upload/Delete 已用);`Get`:53 / `Download`:66 皆為 `if (row.Status != "published" && !await IsAuthenticatedAsync()) return NotFound();` — 缺 `permissions.CanRead(FileCollection)`。ctor:18–19 已注入 `IPermissionService permissions`。`IsAuthenticatedAsync()`:94–100。File entity `Status` 預設 `"draft"`。
- **SEC-6:** `appsettings.json`:17 `"AllowedContentTypes": []`(= allow-all);`FileStorageOptions` 在 **`src/Struo.Application/Files/FileStorageOptions.cs`**(SectionName `"Struo:Files"`,自訂 `Validate()`:26–43);`S3FileStorage.cs` `SaveAsync(string key, Stream content, CancellationToken ct)`:30–34 **不設 ContentType**;`GetPresignedUrlAsync(string key, TimeSpan ttl, ct)`:47–51 **不設 ResponseHeaderOverrides**。上傳驗證在 `src/Struo.Infrastructure/Files/FileService.cs` `UploadAsync`:21–26(size + contentType 白名單)+ :32–38 magic-byte(`FileSignatureValidator.IsConsistent`)。下載 302 redirect 用 `options.S3.PresignTtlSeconds`(`FilesController.Download`:68–70)。
- **CS-5:** 只剩**內層** `JsonDocument.Parse(rec.Snapshot)` 洩漏 — `ItemService.RevertAsync`:798–800:`using var doc = JsonDocument.Parse(StripKeys(JsonDocument.Parse(rec.Snapshot).RootElement, …))`。外層已 using。`Deserialize`:910 有正確範本。
- **CS-7:** `src/Struo.Infrastructure/Localization/LanguageProvider.cs`(24 行):`private IReadOnlyList<LanguageInfo>? _cache;`(:9)、`Load() => _cache ??= db.Queryable<Language>()…`(:11–13)、`Invalidate() => _cache = null`(:23)。DI = Scoped(`DataServiceCollectionExtensions.cs`:27)。
- **CS-8:** `FileService` 在 **Infrastructure**,直接吃 `ISqlSugarClient db`(:13),`DeleteAsync`:69–99 raw `BeginTranAsync/CommitTranAsync/RollbackTranAsync`(:79/:89/:91–95),之後 best-effort `storage.DeleteAsync`(:97)。nesting-safe helper = `IItemRepository.InTransactionAsync(Func<Task>, ct)`(`IItemRepository.cs`:35;實作 `SqlSugarItemRepository.cs`:257–281 join-if-active)。
- **DB-7:** migrations 已renumber `001`–`010`,**下一號 = 011**(README:18–19)。timestamptz 慣例已寫在 `MigrationRunner.cs`:136–142 註解 + `schema_migrations.appliedat timestamptz`,但**無 docs 檔記載**;README 有提及(:93)。
- **DB-8:** `SoftDeleteGenericAsync<T>`:496–519 / `RestoreGenericAsync<T>`:534–549 — `Updateable<T>().SetColumns(it => new T { DeletedAt = …, DeletedBy = … }).Where($"{idColumn} = @__sdId", …)`,**無 version bump、無 revision、無交易包裹**。CAS 範本 = `UpdateGenericAsync<T>`:343–366(`AuditableEntity` 分支,`ConcurrencyConflictException` :360)。ItemService:`DeleteAsync`:640–662(soft 分支 :646–654,`CanDelete` gate :643)、`RestoreAsync`:761–777(gate :765,`GetByIdAsync(…, DeletedFilter.With)` → `repository.RestoreAsync` → 重讀 Exclude → Project)。revision capture 範本 = `UpdateCoreAsync`:402–414(`InTransactionAsync` 內 `if (meta.Revisions) { snapshotBuilder.BuildAsync(collection, updated!, ct); revisions.CaptureAsync(collection, id, operation, snapshot, ct); }`)。`IRevisionStore.CaptureAsync(string collection, string itemId, string operation, string snapshotJson, ct)`。GraphQL 走 `IGraphQlDataSource` → 同一 ItemService,單點修復。
- **DB-9:** `SyncM2MAsync`:594–629,驗證 :611–618(`found.Count != targetIds.Count`,**無去重**);`QueryWhereInAsync` → `WhereInGenericAsync<T>`:571–590 **套 soft-delete filter**(factory :145 註冊 `AddTableFilter<ISoftDeletable>`)。**繞 filter 方法已存在:`QueryWhereInWithDeletedAsync`**(repo :452–481,`ClearFilter<ISoftDeletable>()` :478;介面 default `IItemRepository.cs`:162–165)— 目前只有 `PurgeCoreAsync`(ItemService:720)在用。revert 路徑:`RevertAsync` → `UpdateCoreAsync(collection, id, root, "revert", ct)` → `SyncM2MAsync`。
- **DB-10:** `SyncTranslationsGenericAsync<T>`:842–903(per-locale delete-then-insert,`InTransactionAsync` :897–902)。現有 index 皆非 unique `[SugarIndex]`:`ArticleTranslation.cs`:10(`ix_article_translations_fk_locale`,FK=`ArticleId` Guid、`Locale` string,PK `long Id` identity)、`FileTranslation.cs`:14(同型,FK=`FileId`)。**unique 範本 = `Revision.cs`:25–30 `[SugarColumn(UniqueGroupNameList = ["ux_revisions_item_no"])]`** + `010-revisions-unique-number.sql`(先去重再 CREATE UNIQUE)。`SchemaGuard.AssertCriticalConstraintsAsync(ISqlSugarClient, ct)` 以「uniqueness + 欄位覆蓋」偵測(PG `pg_indexes` / SQLite `sqlite_master`),Development-only(Program.cs:129–132)。README:53–81 記載 010 的 InitTables 順序陷阱與「接受冗餘 dev index」先例。
- **ARC-4:** `BuildOrderBy(sort, d, collection, queryLocale)`:952–976;`TranslatableOrderExpr(collection, fieldName, tm, queryLocale, parentDesc)`:983–1013;`RelationOrderExpr(rootCollection, path)`:1025–1064(用 `RelationPath.Parse`/`registry`/`graph`/`metadata`/`options.MaxRelationDepth`/`db.EntityMaintenance`)。17 個快取 `MethodInfo` + 17 個 `MakeGenericMethod` dispatch(既接受樣式)。抽取先例:static(`ConditionalModelTranslator`/`IdCoercion`)與 DI scoped(`RelationExpander`/`RelationFilterResolver`,註冊於 `DataServiceCollectionExtensions.cs`:24–25)皆有。
- **ARC-5:** 四個 options 全部只有 `Configure<>` 無 startup 驗證:`DatabaseOptions`(`"Database"`,`ConnectionString` 預設 "";綁於 `ServiceCollectionExtensions.AddStruoInfrastructure`:17)、`StruoQueryOptions`(`"Query"`;`DataServiceCollectionExtensions`:19–20 另有 `AddSingleton(sp => IOptions<>.Value)`)、`OidcOptions`(`"Oidc"`;`OidcWiring.cs`:17)、`FileStorageOptions`(`"Struo:Files"`;`FileStorageServiceCollectionExtensions`:16–22 的 singleton factory 於**首次 resolve** 呼叫 `Validate()`,非 ValidateOnStart)。`Microsoft.Extensions.Options.DataAnnotations` 在 shared framework,零新套件。
- **ARC-6:** `ItemsController(ItemService items, IPermissionService permissions)`(:14);呼叫面 = ItemService 全部 9 個 public 方法:`QueryAsync(string, QueryModel, string?, DeletedFilter, ct)`、`GetAsync(string, string, DeepSpec?, string?, DeletedFilter, ct)`、`CreateAsync(string, JsonElement, ct)`、`UpdateAsync(string, string, JsonElement, ct)`、`DeleteAsync(string, string, bool, ct)`、`RestoreAsync(string, string, ct)`、`ListRevisionsAsync(string, string, ct)`、`GetRevisionAsync(string, string, long, ct)`、`RevertAsync(string, string, long, ct)`。`PagedResult` record 在 `ItemService.cs`:17–18。GraphQL 已有接縫 `IGraphQlDataSource`(`GraphQlDataSource.cs`:14–25)。`ItemService` 註冊 scoped(`DataServiceCollectionExtensions.cs`:31)。
- **API-1(後端):** `DomainErrorMap.cs` `Map`:25–34,:31 = `RelationConflictException or ConcurrencyConflictException => (ErrorCodes.Conflict, exception.Message)`;`StatusFor`:41–50(Conflict→409)。`ErrorCodes.cs` 常數:UNAUTHORIZED/FORBIDDEN/NOT_FOUND/CONFLICT/BAD_USER_INPUT/VALIDATION/INTERNAL_SERVER_ERROR + `ForStatus(int)`。`ConcurrencyConflictException` 唯一 thrower = repo `UpdateGenericAsync`:360;`RelationConflictException` thrower = `ItemService.CheckRestrictAsync`:682。**duplicate-email 409 是 `UsersController.Create`:39–40 inline `ApiResults.Fail(409, ErrorCodes.Conflict, …)`,不經 DomainErrorMap** — 分碼只需動 DomainErrorMap/ErrorCodes。
- **測試 fixtures:** `PurgeIntegrityTests.cs` 有測試專用 entity `CascadeNode`(`test_cascade_nodes`,`Revisions = true`、`ISoftDeletable`、self-FK Cascade、sidecar `CascadeNodeTranslation`)與 `RestrictRef` — DB-8/9 測試可重用其 harness 模式(真 ItemService over Blog + 測試 entities)。sample:Article = `AuditableEntity, ISoftDeletable, Revisions=true`(有 Hidden `InternalNote`);Category = soft-deletable 無 revisions;Tag = 皆無。

### 前端
- **FE-7:** `CollectionListView.vue` `loadItems`:67–100 — **token 在 :68(early-return :71 之前)取號** → bail 已 bump token,可孤兒化 in-flight load 的 spinner;手寫 `searchTimer` debounce :115–123;`watch(name)`:170–177(重置 page/sort/search/mode,**不清 searchTimer**);**無 `onUnmounted`**。`lib/debounce.ts`:`debounce<A extends unknown[]>(fn, ms): ((...args: A) => void) & { cancel(): void }`;`lib/latestWins.ts`:`createLatestWins(): { next(): number; isCurrent(token): boolean }`。defineExpose:181–182(含 `onSearchInput/loadItems/rows/total/loading/mode/setMode/…`)。
- **FE-8:** `MediaLibraryView.vue` `onDelete`:31–38 直接 `filesApi.remove(id)`(`DELETE /files/{id}`,非 itemsApi)**無 confirm、無權限 gate**;Edit/Delete 按鈕 :56–57 恆顯示;`onEdit` → `router.push({ name: 'collection-item', params: { name: 'file', id } })`。權限 API = `authStore` getters `canRead/canWrite/canDelete(collection)`(:15–20);collection 名 = `'file'`。`deleteAction.ts`:`deleteConfirm('hard')` = 'Confirm delete' / 'Delete this item? This cannot be undone.'。`confirm.require` 現用於 `CollectionListView.vue`(:158/:163)與 `ItemFormView.vue`(:169/:197)。
- **FE-9:** `HelloWorld.vue`(96 行)零引用;其專屬死資產:`src/assets/vite.svg`、`src/assets/hero.png`、`src/assets/vue.svg`、`public/icons.svg`(僅它引用)。**`src/style.css` 也是孤兒(從未被 import)** — 但 media/file 元件的 `var(--border)`/`var(--accent)` 等引用其變數且無 fallback(現況本來就沒載入,刪除為零行為變更)。`App.vue` = 純 `<router-view/>`。
- **FE-10:** `RichTextInput.vue` `setLink`:110–117(`window.prompt` :113,raw url 直入 `setLink({href:url})`);**Link.configure `protocols: ['http','https','mailto'], autolink: false` 已存在(:85)** — 剩 prompt 路徑的 client 端驗證。TipTap v3.27.1。
- **FE-11:** `apiClient.ts`:86 `let body: ApiErrorBody | undefined` 遮蔽 :69 參數 `body?: unknown`;`itemsApi.ts` `list`:20–28 `total: res.meta.total` 無守衛(`ListEnvelope = { data; meta: { total } }` :17)。
- **FE-12:** `RelationPicker.vue`(156 行):`options`/`labelById` refs :29–30;`loadOptions`:45–62(latest-wins guarded);`ensureSelectedLabels`:64–77 填 `labelById` 但 **Select/MultiSelect 只由 `options` 渲染 label**(:128–153,`option-label="label" option-value="id"`);TreeSelect 走 `treeNodes`(不在此 bug 範圍)。debounce+cancel 已有(:102–109)。測試 4 條(含 debounce),**無 latest-wins race 測試**。
- **fold-in (c):** `ItemFormView.vue` `guardLeave`:192–204 — `confirm.require({header, message, accept: resolve(true), reject: resolve(false)})`,**Esc/X 關閉不 resolve**。PrimeVue 4.5.5 `ConfirmationOptions.onHide?: () => void` **存在**(typings 確認)。
- **fold-in (d):** `.conflict-banner` scoped style `ItemFormView.vue`:255–264(`#f0ad4e` :261、`#fff8ec` :262);**repo 無任何 `var(--p-*)` 先例**(現存樣式用 v3-era var + hex fallback);PrimeVue 4 Aura preset(`main.ts`:15)runtime 有 emit `--p-*` tokens — 本批建立慣例:`var(--p-…, <hex fallback>)`。
- **fold-in (e):** join 在 **`ItemFormView.vue`:127** `leftover.join(' ')`(非 lib);`splitServerErrors(details, knownFields): { fieldErrors; leftover }`。
- **NAV-1:** `RelatedList.vue`:61–63 `router.push({ name: 'collection-item', params: { name: targetCollection, id } })`(row-click :82)— 同 route-record params-only 導航;**`AppShell.vue` 在 `src/layouts/`**(audit 路徑 stale),:29 `<router-view />` 未 key;`ItemFormView.init()`:72–98 已含 re-entrant 重置(conflict/latestFromServer :79–80)但**全 src 無任何 route-param watch / `onBeforeRouteUpdate`** → 重置碼現不可達;`onBeforeRouteLeave` 對 params-only 導航不觸發 → dirty guard 同被繞過。Routes:`collection-list` / `collection-create` / `collection-item` 共用 ItemFormView(create/item)。vue-router **5.1.0**。
- **API-1(前端):** `ItemFormView.vue`:114 `if (e instanceof ApiError && e.status === 409 && e.code === 'CONFLICT')` → `recoverFromConflict()`(:137–154)。src 內 `'CONFLICT'` 僅此處 + 測試 4 條。**delete 衝突(RelationConflict)全域無處理**(generic message)— 本批僅改分碼,不動 delete UX。
- **e2e:** 8 specs(auth/collections/not-found/items/relations/trash/unsaved-guard/conflict);count-settle idiom = `trash.spec`:47–53(`toHaveCount(1)`);`openByTitle` = `items.spec`:62–67(**無 settle**);relations `pickFirstFromPicker`:82–104(`toPass` 20s);conflict.spec 用 article + `page.request` out-of-band PUT(`E2E_API ?? http://localhost:5080`,`X-Struo-CSRF: '1'`)。

---

## Task 1: CS-5 + CS-7 + CS-8 — C# 衛生組

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs`(RevertAsync :798–800)
- Modify: `src/Struo.Infrastructure/Localization/LanguageProvider.cs`
- Modify: `src/Struo.Infrastructure/Files/FileService.cs`(DeleteAsync :69–99;ctor + DI 呼叫端)
- Test: `tests/Struo.Tests/Localization/`、`tests/Struo.Tests/Files/` 既有測試檔就近加

**Interfaces(Produces):**
- CS-5:`RevertAsync` 內層 parse 改兩行 — `using var src = JsonDocument.Parse(rec.Snapshot); using var doc = JsonDocument.Parse(StripKeys(src.RootElement, …));`(比照 `Deserialize`:910 範本)。行為零變更。
- CS-7:`LanguageProvider` 改 `Lazy<IReadOnlyList<LanguageInfo>>`(`LazyThreadSafetyMode.ExecutionAndPublication`);`Invalidate()` 換新 `Lazy` 實例(不 mutate 舊值 — immutability 規則);`Enabled()/DefaultCode()/IsEnabled()` 語意不變。
- CS-8:`FileService` ctor 增 `IItemRepository repository`(Infra→App 參照合法,兩者皆 scoped),`DeleteAsync` 的 delete 段改 `await repository.InTransactionAsync(async () => { …Deleteable<FileTranslation>…; …Deleteable<File>…; }, ct);`,`storage.DeleteAsync` 維持交易外 best-effort。手動 BeginTran/Commit/Rollback 全移除。

**Steps:**
- [ ] Step 1(RED):CS-7 — 併發測試:同一 scoped instance 上 `Parallel.ForEachAsync` 呼叫 `Enabled()`,以可觀測 fake/計數 db 呼叫斷言 `Load` 底層查詢至多執行一次(現況 `??=` 可 >1 → FAIL 或 flaky-red;若 SQLite 上無法穩定 FAIL,以 Lazy 型別/單次載入單元斷言為 RED 證據)。CS-8 — 測試:`DeleteAsync` 在外層 `InTransactionAsync` 內被呼叫時不提早 commit(nested 呼叫後外層 rollback → file 列仍在;現況 raw CommitTran 會提早落盤 → FAIL)。CS-5 — 無可行為斷言(pool 內部),以 code-review gate 為準,不寫偽測試。
- [ ] Step 2:實作三項
- [ ] Step 3:`dotnet test` 全綠(≥732 + 新增)
- [ ] Step 4:Commit `fix: dispose inner revert snapshot doc, Lazy language cache, nesting-safe file delete txn (CS-5, CS-7, CS-8)`

## Task 2: SEC-4 + SEC-5 + SEC-6 — 安全組

**Files:**
- Modify: `src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`(:48 附近)
- Modify: `src/Struo.Api/Controllers/FilesController.cs`(:53、:66)
- Modify: `src/Struo.Api/appsettings.json`(:17)
- Modify: `src/Struo.Application/Files/IFileStorage.cs` + `src/Struo.Infrastructure/Files/S3FileStorage.cs` + `LocalFileStorage`(SaveAsync 簽名)+ `FileService.UploadAsync` 呼叫端
- Test: `tests/Struo.Tests/GraphQl/`、`tests/Struo.Tests/Files/`(整合)

**Interfaces(Produces):**
- SEC-4:builder 鏈加 `.AddCostAnalyzer()`,`ModifyCostOptions(o => { o.MaxFieldCost = …; o.MaxTypeCost = …; o.EnforceCostLimits = true; })` — 初值採 16.4.0 預設(MaxFieldCost/MaxTypeCost 各 1000 等級),**以全量既有 GraphQL 測試綠為約束校準**:若預設值使既有合法查詢(多 root、depth≤12、nested list)被拒,調高至最小可過值並在程式碼註解記載依據。alias 放大由 cost 自然涵蓋(每個 alias 各計 cost),不另尋 alias rule(16.4.0 無此 API — 事實查核確認)。
- SEC-5:`Get`/`Download` 的 gate 改 `if (row.Status != "published" && (!await IsAuthenticatedAsync() || !permissions.CanRead(FileCollection))) return NotFound();` — **保留 authenticated 前置條件**(若 `file` 被列入 `Rbac:PublicReadCollections`,匿名 CanRead=true,單靠 CanRead 會比現況更弱);404 不洩存在性。
- SEC-6:(a) `appsettings.json` `AllowedContentTypes` 出貨預設白名單:`["image/jpeg","image/png","image/gif","image/webp","image/avif","image/svg+xml","application/pdf","text/plain","video/mp4","audio/mpeg"]`(svg 可留 — API 路徑強制 attachment、presigned 走獨立 origin,audit 已認定);(b) `IFileStorage.SaveAsync` 簽名加 `string contentType`(呼叫端 = `FileService.UploadAsync` 傳入已驗證之 contentType;`LocalFileStorage` 忽略之);`S3FileStorage.SaveAsync` 於 `PutObjectRequest` 設 `ContentType`;(c) `GetPresignedUrlAsync` 設 `ResponseHeaderOverrides = { ContentDisposition = "attachment" }`(ContentType 由 (b) 的物件屬性帶)。
- 既有測試若上傳非白名單 type(如 `application/octet-stream`):**改測試所用 type 為白名單值**,除非該測試本意是驗 allow-all(改斷言為 403/400 拒絕)— 逐一判讀,不齊頭砍。

**Steps:**
- [ ] Step 1(RED):SEC-5 — 整合:seed 一個 `Status="draft"` 檔;`CreateRolelessClientAsync()`(已認證、零權限)GET content/download → 斷言 404(現況 200 → FAIL);`CreateEditorClientAsync(readCollections: ["file"])` → 200;匿名 → 404。SEC-4 — 整合:50-alias 疊 `articles { items { id } }` 的查詢 → 斷言 errors 非空且 data null(現況全執行 → FAIL);既有正常查詢仍 200 無 error。SEC-6 — 單元:`FileService.UploadAsync` 收非白名單 type 丟既有拒絕例外(換上預設白名單後現有 allow-all 行為變化);S3 request 組裝以可注入之 `IAmazonS3` fake 斷言 `PutObjectRequest.ContentType` 與 presigned `ResponseHeaderOverrides`(若 S3FileStorage 直接 new client 而不可注入,允許重構為注入 `IAmazonS3` — 現有 DI 已有先例則沿用;否則以 live gate MinIO 驗證為準並記載)。
- [ ] Step 2:實作三項
- [ ] Step 3:`dotnet test` 全綠;**security-reviewer subagent pass**(SEC 組專屬)
- [ ] Step 4:Commit `fix(security): GraphQL cost analyzer, unpublished file RBAC gate, upload whitelist + S3 content-type/disposition (SEC-4, SEC-5, SEC-6)`

## Task 3: DB-8 — trash/restore 遞增 version + 記 revision

**Files:**
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`(`SoftDeleteGenericAsync`:496–519、`RestoreGenericAsync`:534–549)
- Modify: `src/Struo.Application/Query/ItemService.cs`(`DeleteAsync` soft 分支 :646–654、`RestoreAsync`:761–777)
- Test: 新 `tests/Struo.Tests/Query/TrashRevisionTests.cs`(重用 `PurgeIntegrityTests` harness 模式)

**Interfaces(Produces):**
- Repository:`SoftDeleteGenericAsync`/`RestoreGenericAsync` 對 `AuditableEntity` 子型別**同一 UPDATE 內遞增 version** — 於既有 `SetColumns(it => new T { … })` 之外,`typeof(AuditableEntity).IsAssignableFrom(typeof(T))` 時鏈加 version 遞增(優先 typed expression;若 SqlSugar 對 `it.Version + 1` 的 object-init 形不支援,允許 `SetColumns("<versioncol> = <versioncol> + 1")` SQL 字面 — 欄名出自 `EntityMaintenance.GetDbColumnName`,無參數無 42804 風險,屬既接受樣式)。非 AuditableEntity 的 ISoftDeletable:行為不變(無 version 可遞增)。**不做 CAS**(client 不送 version 於 delete/restore;決策 = 只遞增)。
- ItemService:soft-delete 分支與 `RestoreAsync` 皆包 `repository.InTransactionAsync`,內部:執行 soft-delete/restore → `if (meta.Revisions)` 以 `DeletedFilter.With` 重讀 entity → `snapshotBuilder.BuildAsync` → `revisions.CaptureAsync(collection, id, "delete" | "restore", snapshot, ct)`(比照 `UpdateCoreAsync`:402–414 範本;operation 字串新增兩值,`RevisionInfo.Operation` 為自由字串無需 schema 變更)。
- REST/GraphQL 零介面變更(單點 ItemService)。

**Steps:**
- [ ] Step 1(RED):(a) soft-delete Article → 重讀(With)斷言 `Version` +1、revisions 多一筆 `Operation=="delete"` 且 snapshot 含刪除當下欄位值;(b) restore → `Version` 再 +1、一筆 `Operation=="restore"`;(c) Category(soft、無 revisions)→ version +1、revisions 零筆;(d) revision 內途中丟例外 → rollback(deletedat 仍 null、無 revision 半套)— 以 fake IRevisionStore 丟例外驗證;(e) 既有 trash/restore 測試全綠(list/get 過濾語意不變)。現況 (a)(b) FAIL。
- [ ] Step 2:repository 實作 → Step 3:ItemService 交易+capture 實作
- [ ] Step 4:`dotnet test` 全綠(≥732 + 新增)
- [ ] Step 5:Commit `feat(data): trash/restore bumps version and records delete/restore revisions (DB-8)`

## Task 4: DB-9 + DB-10 + DB-7 — 資料完整性組

**Files:**
- Modify: `src/Struo.Application/Query/ItemService.cs`(`SyncM2MAsync`:594–629 與其呼叫鏈)
- Modify: `samples/Struo.Sample.Blog/ArticleTranslation.cs`、`src/Struo.Infrastructure/Files/FileTranslation.cs`(unique 屬性)
- Create: `db/migrations/011-translation-unique-locale.sql`
- Modify: `src/Struo.Infrastructure/Persistence/SchemaGuard.cs`(擴充斷言)
- Modify: `db/migrations/README.md`(DB-7 慣例段)
- Test: `tests/Struo.Tests/Query/`(M2M/revert)、`tests/Struo.Tests/Persistence/`(SchemaGuard/unique)

**Interfaces(Produces):**
- DB-9(去重):`SyncM2MAsync` 於驗證前 `targetIds = targetIds.Distinct().ToList()`(id 為 typed 值,default comparer 足夠)— 之後 junction insert 亦以去重後清單為準(`tags:[t1,t1]` = `tags:[t1]` 語意)。
- DB-9(revert):`SyncM2MAsync` 增參數 `bool includeDeleted`(由 `UpdateCoreAsync` 以 `operation == "revert"` 決定傳入);驗證查詢 `includeDeleted ? repository.QueryWhereInWithDeletedAsync(…) : repository.QueryWhereInAsync(…)`(繞 filter 方法已存在,零 repo 變更)。語意:revert 允許引用已 trash 的 target(snapshot 當時合法);一般寫入維持拒絕。
- DB-10:兩個 translation entity 的 FK+Locale 欄位加 `[SugarColumn(UniqueGroupNameList = ["ux_article_translations_fk_locale"])]` / `["ux_file_translations_fk_locale"]`(比照 `Revision.cs`:25–30;既有非 unique `[SugarIndex]` **保留** — README 已接受冗餘 dev index 先例,避免 CodeFirst/migration 分歧)。migration `011`:每表先去重(同 `(fk,locale)` 留 `MAX(id)`)再 `CREATE UNIQUE INDEX IF NOT EXISTS ux_…`(比照 `010` 樣式,冪等)。`SchemaGuard` 擴充:斷言兩表存在覆蓋 `(fk,locale)` 的 unique index(沿用「uniqueness + 欄位覆蓋、不比名字」偵測法)。
- DB-7:`db/migrations/README.md` 增「Timestamp convention」小節:新欄位/新表一律 `timestamptz`(引 `schema_migrations.appliedat` 先例與 MigrationRunner 註解);既有 `timestamp` 欄位不回填改型。純文件。

**Steps:**
- [ ] Step 1(RED):(a) body `tags:[t1,t1]` 更新 Article → 現況 400「do not exist」→ 期望成功且 junction 恰一列;(b) trash 一個 Tag → revert Article 至含該 tag 的 snapshot → 現況 400 → 期望成功且 junction 還原;(c) 一般更新引用已 trash tag 仍 400(不對稱守住);(d) SQLite InitTables 後對 translation 表插入重複 `(fk,locale)` → 斷言 unique violation(現況成功 → FAIL);(e) SchemaGuard 對缺 unique 的庫 fail-fast(單元)。
- [ ] Step 2:DB-9 實作 → Step 3:DB-10 attribute + migration + SchemaGuard → Step 4:README DB-7 段
- [ ] Step 5:`dotnet test` 全綠
- [ ] Step 6:Commit `fix(data): M2M id dedup + revert tolerates trashed targets; translation (fk,locale) UNIQUE; timestamptz convention doc (DB-9, DB-10, DB-7)`

## Task 5: ARC-5 — Options ValidateOnStart

**Files:**
- Modify: `src/Struo.Application/Configuration/DatabaseOptions.cs`、`StruoQueryOptions.cs`、`src/Struo.Application/Security/OidcOptions.cs`(annotations)
- Modify: `src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`:17、`DataServiceCollectionExtensions.cs`:19–20、`src/Struo.Api/Auth/OidcWiring.cs`:17、`src/Struo.Application/Files/FileStorageServiceCollectionExtensions.cs`:16–22
- Test: 新 `tests/Struo.Tests/DependencyInjection/OptionsValidationTests.cs`

**Interfaces(Produces):**
- `DatabaseOptions.ConnectionString` 加 `[Required(AllowEmptyStrings = false)]`;`StruoQueryOptions` 四個 int 加 `[Range(1, int.MaxValue)]`。`System.ComponentModel.DataAnnotations` 為 BCL,Application 層合法(§2 不破)。
- 四處綁定改 `services.AddOptions<T>().BindConfiguration("<Section>").ValidateDataAnnotations().ValidateOnStart()`;`StruoQueryOptions` 既有 `AddSingleton(sp => IOptions<>.Value)` 保留;`FileStorageOptions` 以 `.Validate(o => …)` 或小型 `IValidateOptions<FileStorageOptions>` 包既有 `Validate()`(保留其訊息);`OidcOptions` 加條件驗證:`Enabled == false` 恆過,`Enabled == true` 需 `Authority/ClientId/ClientSecret` 非空(`.Validate(o => !o.Enabled || (…), "Oidc enabled requires Authority, ClientId, ClientSecret")`)。
- 風險點:`ValidateOnStart` 使缺配置的 host **開機即死** — `ApiFactory` 已供 SQLite 連線字串與 local files 配置,既有整合測試應綠;若有測試僅部分配置,補齊該測試的 in-memory config 而非放寬驗證。

**Steps:**
- [ ] Step 1(RED):`OptionsValidationTests` — 以 `WebApplicationFactory` + 移除 `Database:ConnectionString` 的 config 啟動 → 斷言 host 啟動丟 `OptionsValidationException`(現況:啟動成功、首次 DB 存取才炸 → FAIL);`Query:MaxLimit=0` → 同斷言;`Oidc:Enabled=true` 無 ClientId → 同斷言;完整配置 → 啟動成功。
- [ ] Step 2:實作 → Step 3:`dotnet test` 全綠
- [ ] Step 4:Commit `feat: options fail-fast — ValidateDataAnnotations + ValidateOnStart for Database/Query/Oidc/Files (ARC-5)`

## Task 6: ARC-4 — OrderByExpressionBuilder 抽檔(純重構)

**Files:**
- Create: `src/Struo.Infrastructure/Query/OrderByExpressionBuilder.cs`
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`(移除 :952–1064 三方法,改委派)
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`(註冊)

**Interfaces(Produces):**
- `OrderByExpressionBuilder`:scoped DI 服務(比照 `RelationExpander` 先例),ctor 吃 repository 現用之相依(`IEntityRegistry`/`IRelationshipGraph`/metadata provider/`StruoQueryOptions`/`ISqlSugarClient`(EntityMaintenance 用)— 以現方法實際引用為準);public `string? BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d, string collection, string? queryLocale)` 簽名不變,`TranslatableOrderExpr`/`RelationOrderExpr` 為其 private。SQL 文字**逐字搬移**(含 locale quote-doubling :1011),零行為變更。
- `MakeGenericMethod` dispatch(17 處)**本批不動** — open-generic delegate cache 屬效能最佳化且 Batch 5 將重構讀寫面,YAGNI,於 commit message 記載 deferred。
- 驗收 = 既有全量測試**不改一行**而綠(排序相關測試即回歸網)。

**Steps:**
- [ ] Step 1:抽檔 + 接線(無新測試 — 純搬移;若 builder 相依需要,允許建構子單元 smoke)
- [ ] Step 2:`dotnet test` 全綠(732+,零測試變更)
- [ ] Step 3:Commit `refactor: extract OrderByExpressionBuilder from SqlSugarItemRepository — zero behavior change (ARC-4; delegate-cache deferred to Batch 5)`

## Task 7: ARC-6 — ItemsController 依 IItemUseCases 接縫

**Files:**
- Create: `src/Struo.Application/Query/IItemUseCases.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs`(實作介面,一行)
- Modify: `src/Struo.Api/Controllers/ItemsController.cs`(:14 ctor 型別)
- Modify: `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs`(:31 附近註冊)
- Test: `tests/Struo.Tests/Api/` 加 controller 級單元(fake IItemUseCases)smoke

**Interfaces(Produces):**
- `public interface IItemUseCases` — 9 方法,簽名**逐字**取自 ItemService public surface(共通事實 ARC-6 條列);`ItemService : IItemUseCases`。註冊:`services.AddScoped<IItemUseCases>(sp => sp.GetRequiredService<ItemService>())`(同 scope 同實例)。`IGraphQlDataSource` 不動(Api 層刻意接縫,audit 已核可)。
- 零行為變更;新增之 controller 單元測試證明接縫可 fake(如:fake 回固定 PagedResult → controller 200 envelope)。

**Steps:**
- [ ] Step 1(RED):controller 單元 — `new ItemsController(fakeUseCases, fakePermissions)` 呼叫 list → 斷言委派與回傳(現況 ctor 吃具體型別 → 編譯即 FAIL,RED = 編譯錯誤即可)
- [ ] Step 2:介面 + 接線 → Step 3:`dotnet test` 全綠
- [ ] Step 4:Commit `refactor: ItemsController depends on IItemUseCases seam (ARC-6)`

## Task 8: API-1(後端)— VERSION_CONFLICT 分碼

**Files:**
- Modify: `src/Struo.Api/Http/ErrorCodes.cs`(+ `VersionConflict = "VERSION_CONFLICT"`)
- Modify: `src/Struo.Api/Http/DomainErrorMap.cs`(:31 拆兩行;`StatusFor` + VersionConflict→409)
- Test: 既有 DomainErrorMap 單元 + REST/GraphQL 雙協定整合對照

**Interfaces(Produces):**
- `ConcurrencyConflictException => (ErrorCodes.VersionConflict, msg)`(409);`RelationConflictException => (ErrorCodes.Conflict, msg)`(409 不變)。`ErrorCodes.ForStatus(409)` **維持回 `Conflict`**(泛用反向映射;`UsersController` duplicate-email inline 409 不受影響 — 事實查核確認其不經 DomainErrorMap)。
- GraphQL `StruoErrorFilter` 走同一 `DomainErrorMap` → 自動同步,零額外變更;測試須雙協定對照斷言(ARC-3 精神)。
- **前端跟進在 Task 12b(API-1-FE)** — 後端先行,live gate 前必須兩者皆併。

**Steps:**
- [ ] Step 1(RED):(a) 單元:`DomainErrorMap.Map(new ConcurrencyConflictException(…))` → `VERSION_CONFLICT`(現況 CONFLICT → FAIL);`RelationConflictException` → `CONFLICT`;(b) 整合:REST stale-version update → 409 + `error.code=="VERSION_CONFLICT"`;GraphQL 同例外 → error `extensions.code=="VERSION_CONFLICT"`;delete restrict → 雙協定 `CONFLICT`。
- [ ] Step 2:實作 → Step 3:`dotnet test` 全綠
- [ ] Step 4:Commit `feat(api): split optimistic-lock conflicts to VERSION_CONFLICT across REST and GraphQL (API-1)`

---

## Task 9: FE-7 + fold-ins (a)(b) — 清單 timer/spinner 衛生

**Files:**
- Modify: `frontend/src/views/CollectionListView.vue`
- Modify: `frontend/src/views/CollectionListView.test.ts`

**Interfaces(Produces):**
- (b) `searchTimer` 手寫 debounce(:115–123)收斂到 `lib/debounce.ts`:`const debouncedSearch = debounce(() => { page.value = 0; loadItems() }, 300)`;`onSearchInput` = `search.value = value; debouncedSearch()`。
- (FE-7 本體)`onUnmounted(() => debouncedSearch.cancel())`;`watch(name)` handler 頂部 `debouncedSearch.cancel()`(pending 搜尋不得打剛切換的 collection)。
- (a) `loadItems` 的 early-return(:71)改為 `if (!meta.value || !canRead.value) { if (listLoad.isCurrent(token)) loading.value = false; return }` — bail 的呼叫已 bump token,須同時終結被其孤兒化的 spinner。token 取號位置(:68)不動(保持「最後呼叫勝」語意)。
- 測試補強(Batch 3 帶入):spinner-preservation interleaving — 請求 A in-flight 時發請求 B,A 先 resolve:斷言 `loading` 仍 true(為 B 保留)、rows 未被 A 污染;B resolve 後 loading=false、rows=B。

**Steps:**
- [ ] Step 1(RED):fake timers — (i) unmount 後 advance 300ms → `itemsApi.list` 不被呼叫(現況 timer 照 fire → FAIL);(ii) 打字後立即切 `name` → pending 搜尋不觸發、只有 watch(name) 的一次 load;(iii) spinner interleaving 測試(現況 (a) 未修 → bail 路徑 loading 殘留斷言 FAIL);(iv) 既有 debounce 行為(300ms 合併)維持。
- [ ] Step 2:實作 → Step 3:`pnpm test` + `pnpm build` 全綠(≥312 + 新增)
- [ ] Step 4:Commit `fix(frontend): list search debounce via shared helper with unmount/switch cancel + bail clears orphaned spinner (FE-7)`

## Task 10: FE-8 + FE-9 — MediaLibrary 守衛 + 死碼清除

**Files:**
- Modify: `frontend/src/views/MediaLibraryView.vue` + `MediaLibraryView.test.ts`
- Delete: `frontend/src/components/HelloWorld.vue`、`frontend/src/assets/vite.svg`、`frontend/src/assets/hero.png`、`frontend/src/assets/vue.svg`、`frontend/public/icons.svg`、`frontend/src/style.css`

**Interfaces(Produces):**
- FE-8:`onDelete` 改 `confirm.require({ ...deleteConfirm('hard'), accept: async () => { await filesApi.remove(id); await load() } })`(檔案刪除 = 永久,`deleteConfirm('hard')` 文案吻合;比照 `CollectionListView`:158 樣式,含錯誤處理維持現有 catch);Delete 按鈕 `v-if="canDelete"`、Edit 按鈕 `v-if="canWrite"`,`const auth = useAuthStore(); const canDelete = computed(() => auth.canDelete('file'))`(canWrite 同)。若 view 需要 `ConfirmDialog` 元件註冊,比照既有 confirm 使用 view 的做法(查 AppShell/main.ts 全域註冊即沿用)。
- FE-9:六檔全刪 — `HelloWorld.vue` 零引用(fact-check grep 確認);`style.css` 從未被 import(**零行為變更** — 其變數今日本來就沒生效;media 元件殘留的 `var(--border)` 等 dangling 引用為既存 no-op,不在本批範圍,於 commit message 記載)。刪後 `pnpm build`(vue-tsc + vite)綠即證明無引用。

**Steps:**
- [ ] Step 1(RED):MediaLibrary 測試 — `onDelete` 呼叫時 `confirmRequire` 被呼叫且 accept 前 `filesApi.remove` 不執行(現況直接刪 → FAIL);roleless user store(`canDelete('file')=false`)→ Delete 按鈕不渲染;有權限 → 渲染。
- [ ] Step 2:實作 + 刪檔 → Step 3:`pnpm test` + `pnpm build` 全綠
- [ ] Step 4:Commit `fix(frontend): media delete confirm + permission-gated actions; remove dead scaffold (FE-8, FE-9)`

## Task 11: FE-10 + FE-11 + FE-12 + fold-ins (d)(e) — 前端小項組

**Files:**
- Create: `frontend/src/lib/linkUrl.ts` + `frontend/src/lib/linkUrl.test.ts`
- Modify: `frontend/src/components/fields/RichTextInput.vue`(setLink :110–117)
- Modify: `frontend/src/api/apiClient.ts`(:86)、`frontend/src/api/itemsApi.ts`(:17、:27)
- Modify: `frontend/src/components/fields/RelationPicker.vue` + `RelationPicker.test.ts`
- Modify: `frontend/src/views/ItemFormView.vue`(:127 join、:255–264 style)

**Interfaces(Produces):**
- FE-10:`linkUrl.ts` — `export function isAllowedLinkUrl(url: string): boolean`(trim 後 `/^(https?:|mailto:)/i`;空字串 false)。`setLink`:prompt 後、既有空字串 unset 分支後,`if (!isAllowedLinkUrl(url)) return`(靜默拒絕即可 — 縱深防禦,TipTap protocols + 伺服器淨化為真防線)。
- FE-11:`apiClient.ts`:86 `let body` → `let errBody`(含後續引用);`itemsApi.ts`:`ListEnvelope.meta` 改 optional(`meta?: { total: number }`),`total: res.meta?.total ?? res.data.length`。
- FE-12:`RelationPicker` 增 computed `displayOptions` — `options` ∪ 已選但缺席的 id(`{ id, label: labelById[id] ?? id, raw: {} }`,選中 id 來自 modelValue,multiple 時攤平);`Select`/`MultiSelect` 的 `:options` 改 `displayOptions`(TreeSelect 不動)。**測試補強(帶入):**picker 元件級 latest-wins race — 兩個可控 promise,舊回應後 resolve,斷言 `options` = 新回應。
- (d):`.conflict-banner` `border: 1px solid var(--p-amber-400, #f0ad4e); background: var(--p-amber-50, #fff8ec)`(建立 `var(--p-*, fallback)` 慣例 — repo 首例,commit message 記載)。
- (e):`ItemFormView.vue`:127 `leftover.join(' ')` → `leftover.join('; ')`。

**Steps:**
- [ ] Step 1(RED):`isAllowedLinkUrl` 純函式(http/https/mailto/大小寫/`javascript:`/空);RichText:mock prompt 回 `javascript:alert(1)` → `setLink` 不被呼叫(現況直入 → FAIL);itemsApi:mock `getRaw` 回無 meta envelope → `total === data.length`(現況 TypeError → FAIL);RelationPicker:preselected id 不在 options → `displayOptions` 含該 id 且 label 來自 `labelById`(現況渲染 raw id → FAIL);race 測試;applyServerErrors view 測試斷言 banner 以 `'; '` 相接。
- [ ] Step 2:實作 → Step 3:`pnpm test` + `pnpm build` 全綠
- [ ] Step 4:Commit `fix(frontend): link protocol guard, list meta guard + errBody rename, picker selected-label merge, theme-token banner, banner join (FE-10, FE-11, FE-12)`

## Task 12a: NAV-1 + fold-in (c) — 同 record 導航對 + Esc 守衛

**Files:**
- Modify: `frontend/src/layouts/AppShell.vue`(:29)
- Modify: `frontend/src/views/ItemFormView.vue`(guardLeave :192–204 + `onBeforeRouteUpdate`)
- Modify: `frontend/src/views/ItemFormView.test.ts`;新 e2e 情境(`frontend/e2e/` 既有 spec 擴充或新 spec)

**Interfaces(Produces):**
- AppShell:`<router-view :key="route.path" />`(`const route = useRoute()`)— params-only 導航(RelatedList row-click、create→edit)強制重掛載 → `init()` 重跑、表單不再殘留舊資料。`path` 不含 query(list 的 page/sort 為元件內 state,不受影響;`CollectionListView` 既有 `watch(name)` 變冗餘但無害,**保留不動**以最小 diff)。
- ItemFormView:`onBeforeRouteUpdate(async (to, from) => { if (to.params.id !== from.params.id || to.params.name !== from.params.name) return guardLeave(); return true })` — dirty guard 接手 params-only 路徑(leave guard 對此不觸發,fact-check 確認)。
- (c):`guardLeave` 的 `confirm.require` 增 `onHide: () => resolve(false)`(PrimeVue 4.5.5 typings 確認存在;accept 後 onHide 再 fire 屬 promise 二次 resolve no-op,無需 settled flag)。
- **行為變更聲明:** RelatedList 導航從「靜默重用舊表單」變「守衛詢問(dirty 時)→ 重掛載載入新 item」。

**Steps:**
- [ ] Step 1(RED):(i) unit — `vi.mock('vue-router')` 增 `onBeforeRouteUpdate` export 捕捉 guard:params 變更 + dirty → `confirmRequire` 被呼叫,reject → guard 回 false;clean → 直接 true;(ii) unit — guardLeave 的 require options 含 `onHide`,呼叫 onHide → promise resolve false(現況懸掛 → 以 timeout race 斷言 FAIL);(iii) e2e — 編輯 article(dirty)→ RelatedList 點 tag row → confirm 出現;accept 後 URL 變更且表單顯示 tag 的資料(現況:表單殘留 article 內容 → FAIL)。
- [ ] Step 2:實作 → Step 3:`pnpm test` + `pnpm build` + 受影響 e2e 綠
- [ ] Step 4:Commit `fix(frontend): keyed router-view + route-update dirty guard — same-record nav reloads form and respects unsaved changes; Esc resolves leave guard (NAV-1)`

## Task 12b: API-1(前端)+ e2e minors(fold-in (f))

**依賴:Task 8(後端 VERSION_CONFLICT)已併。**

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue`(:114)+ `ItemFormView.test.ts`(4 條 CONFLICT mocks)
- Modify: `frontend/e2e/items.spec.ts`、`frontend/e2e/relations.spec.ts`(minors)

**Interfaces(Produces):**
- `:114` 條件改 `e.code === 'VERSION_CONFLICT'`(**不留 `'CONFLICT'` fallback** — duplicate-email 等泛用 409 不得誤觸 conflict-recovery banner,正是 API-1 動機);unit mocks 全改 `VERSION_CONFLICT`。新測試:`code:'CONFLICT'`(泛用 409)→ 走 `serverError` banner、**不**觸發 `recoverFromConflict`。`conflict.spec.ts` e2e 斷言 UI 文案不變,免改(live gate 驗證)。
- e2e minors:(i) `items.spec`/`relations.spec` 最終 row-gone 斷言借用 `trash.spec`:47–53 count-settle idiom;(ii) `openByTitle`(items.spec:62–67)補 form-load settle(URL 斷言後加 title input `toHaveValue(title)`);(iii) relations `pickFirstFromPicker` 重開 churn:保留 `toPass` 但把 overlay 重開條件收斂(依現碼實況微調,目標 = 降 flake,零語意變更)。
- 測試補強(帶入):`reloadLatest` 後 `guardLeave` 不再詢問(re-baseline);delete-accept 後導航不觸發 guard。

**Steps:**
- [ ] Step 1(RED):unit — mock 丟 `ApiError(409, …, 'VERSION_CONFLICT')` → recovery 觸發(改條件前 mock 用 VERSION_CONFLICT 會 FAIL);`'CONFLICT'` → 只 banner;re-baseline 兩測試(現況若未涵蓋 → FAIL)。
- [ ] Step 2:實作 + e2e minors → Step 3:`pnpm test` + `pnpm build` 全綠
- [ ] Step 4:Commit `fix(frontend): conflict recovery keys on VERSION_CONFLICT only + e2e settle/churn hardening (API-1)`

---

## Batch 4 Gate(全批完成後)

- [ ] `dotnet test` 全綠(≥732 + 新增);`pnpm test` + `pnpm build` 全綠(≥312 + 新增);git diff 確認無計畫外變更
- [ ] **Live gate(真 PG,`ASPNETCORE_URLS=:5080`,bootstrap admin `admin@admin.com`;前端 `pnpm dev --host 127.0.0.1`):**
  1. SEC-4:50-alias GraphQL 轟炸 → error(cost);正常多 root 查詢 → 200
  2. SEC-5:draft 檔以零權限 SSO/roleless user 讀 → 404;有 file read 權限 → 200;匿名 → 404
  3. SEC-6:上傳白名單外 type → 400/403;MinIO 物件 `Content-Type` 正確;presigned URL response header 帶 `attachment` disposition(curl -I 驗證)
  4. DB-8:trash → restore 一輪,psql 斷言 version +2、revisions 兩筆(delete/restore);UI trash/restore 正常
  5. DB-9:`tags:[t1,t1]` PUT → 200 單 junction;trash tag 後 revert → 200
  6. DB-10:`pg_indexes` 斷言兩個 `ux_*_fk_locale`;psql 手動插重複 `(fk,locale)` → 23505;migration 011 重跑冪等
  7. ARC-5:清掉 ConnectionString 起 server → 開機即死(訊息含 options 驗證);還原後正常
  8. API-1:REST stale version PUT → `VERSION_CONFLICT`;GraphQL 同;users duplicate email → `CONFLICT`(不觸發前端 recovery banner — UI 抽查)
  9. NAV-1:Playwright/手動 — article 表單 dirty → RelatedList 點 tag → 守衛 → accept → tag 表單正確載入;Esc 關守衛 → 留原頁且後續導航正常
  10. e2e 全量:既有 + 新 spec 全綠(目標 ≥ 13)
- [ ] 更新 `docs/architecture-audit-2026-07-15.md`:SEC-4~6、CS-5/7/8、DB-7~10、ARC-4~6、FE-7~12 標 ✅FIXED(附 commit);SEC-3 標 DEFERRED(user decision);NAV-1/API-1 補錄為 Batch 3 帶入項已修
- [ ] Merge `audit-batch4` to main
