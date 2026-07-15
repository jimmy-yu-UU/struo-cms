# 2026-07-15 架構稽核修復計畫（全 40 項，分批）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修復 `docs/architecture-audit-2026-07-15.md` 全部 7 HIGH / 12 MED / 21 LOW 發現，分 5 批，每批獨立驗證（backend + frontend 測試綠 + 真 PG live gate）並 commit。

**Architecture:** 依審核報告優先序分批：Batch 1 = 全部 HIGH（安全/正確性/資料完整性/前端阻斷）；Batch 2 = 後端/資料 MED；Batch 3 = 前端 MED；Batch 4 = LOW 批次；Batch 5 = ARC-1 ItemService 重構（純行為不變、最後做）。每項先寫失敗測試（TDD），批次結束跑 live gate。

**Tech Stack:** .NET 10 / SqlSugarCore / PostgreSQL（SQLite tests-only）/ Vue 3 + PrimeVue / vitest + Playwright。

## 已拍板決策（2026-07-15，user 確認）

1. **範圍：** 全部 40 項，分批進行。
2. **DB-1/DB-2 參照完整性：** 應用層實作（purge 交易內依 metadata 做 SetNull/Cascade/孤兒清理），不加 DB FK constraint。
3. **CS-1：** factory 改 `SqlSugarScope`（thread-safe，維持 Request scope）。
4. **ARC-1：** 本輪最後一批（Batch 5）進行。

## Global Constraints

- 全 DB 存取走 SqlSugar ORM，零 vendor SQL（§17.4）；SQL fragment 僅允許既有已接受樣式（`.Where($"{col} = @p", …)`、ORDER-BY 子查詢、migration 檔）。
- 分層：Domain→無、Application→Domain、Infrastructure→App+Domain、Api→App+Infra（§2）。
- TDD：每項先寫失敗測試；SQLite 綠 ≠ 完成，**每批結束必跑真 PG live gate**（memory: db-verify-live-postgres）。
- typed-NULL 陷阱：PG 42804 — 對 nullable 欄位 SET NULL 不可用 untyped null 參數（9b live-gate 教訓）。
- 前端 gate = `pnpm test` **且** `pnpm build`（vue-tsc 會檢查測試檔，9a-fe 教訓）。
- Live gate：cookie-auth 寫入需 `X-Struo-CSRF` header（presence-only）；PS 讀 `.data.*`；`"$id?"` 內插會壞（用字串串接）；dev 伺服器 `ASPNETCORE_URLS=:5080`、bootstrap admin `admin@admin.com`。
- 套件版本不得手寫，一律 `dotnet add package` / `pnpm add` 產生（§17.5）。本計畫預期**零新套件**。
- Commit 格式：`<type>: <description>`（conventional commits，attribution 已全域停用）。
- 基線：後端 655 綠、前端 267 綠。每 task 結束不得低於基線。

## 批次總覽

| 批 | 內容 | 項目 |
|---|---|---|
| 1 | 全部 HIGH（本文件含完整 task 細節） | SEC-1, CS-2, CS-1, SEC-2, DB-1+DB-2, DB-3, FE-1 |
| 2 | 後端/資料 MED | DB-4(=CS-6), DB-5, DB-6, CS-3, CS-4(=ARC-2), ARC-3 |
| 3 | 前端 MED | FE-2, FE-3, FE-4, FE-5, FE-6 |
| 4 | LOW 批次 | SEC-3~6, CS-5, CS-7, CS-8, DB-7~10, ARC-4~6, FE-7~12 |
| 5 | ARC-1 ItemService 重構 | ItemDeserializer + IFieldValidator registry + 讀取面拆分 |

Batch 2–5 的細部 task 計畫於各批開始時 just-in-time 撰寫（同 writing-plans skill，附於本文件末尾之批次條目為範圍與驗收基準）。**執行順序內含依賴：Batch 1 Task 5（purge 完整性）依賴 Task 2（CS-2 的 id 解析）先完成。**

## 待 user 決策（到該批時再問）

- **DB-8（Batch 4）：** trash/restore 是否應遞增 version 並記入 revision 歷史？（產品行為決策）
- **SEC-3（Batch 4）：** Azure OIDC ClientSecret / DB / admin 密碼**輪換**需 user 自行操作；程式側只做 user-secrets 遷移指引。
- **DB-6（Batch 2）：** migration runner 形式（輕量 runner + 追蹤表 vs 純文件規範）。
- **CS-7（Batch 4）：** CS-1 修完後重新評估是否仍需 `Lazy<>`。

---

# Batch 1 — HIGH（7 項，7 tasks）

## Task 1: SEC-1 — 關聯路徑 leaf 排除 hidden 欄位

**Files:**
- Modify: `src/Struo.Application/Query/RelationPath.cs:64-66`
- Test: 既有 RelationPath / QueryValidator 測試檔（`tests/Struo.Tests/Query/` 下，glob `*RelationPath*`/`*QueryValidator*` 定位；沿用其現有 fixture 慣例，target collection 需含一個 `Hidden = true` 欄位的 metadata）

**Interfaces:**
- Consumes: `RelationPath.Parse(rootCollection, path, graph, metadata, maxDepth)`（現有簽名不變）
- Produces: 對 `<rel>.<hiddenField>` 路徑丟 `QueryException`（filter 與 sort 共用此入口，一次堵兩個變體）

- [ ] **Step 1: 寫失敗測試** — 在既有 RelationPath 測試檔加（fixture 命名依現有測試調整）：

```csharp
[Fact]
public void Parse_rejects_hidden_leaf_field_on_relation_path()
{
    // target collection 的 metadata 含 Hidden 欄位（比照 H2 自身欄位測試的 fixture）
    var ex = Assert.Throws<QueryException>(() =>
        RelationPath.Parse("article", "category.secret", _graph, _metadata, maxDepth: 3));
    Assert.Contains("Unknown field 'secret'", ex.Message); // 對外訊息與「不存在」同型，不洩露 hidden 欄位存在性
}

[Fact]
public void Parse_still_accepts_visible_leaf_field_on_relation_path()
{
    var path = RelationPath.Parse("article", "category.name", _graph, _metadata, maxDepth: 3);
    Assert.Equal("name", path.LeafField);
}
```

- [ ] **Step 2: 跑測試確認 FAIL**（hidden leaf 目前被接受）
  `dotnet test --filter "FullyQualifiedName~RelationPath" ` → 新測試 1 FAIL
- [ ] **Step 3: 一行修復** — `RelationPath.cs:66`：

```csharp
var leafKnown =
    string.Equals(leaf, "id", StringComparison.OrdinalIgnoreCase) ||
    terminal.Fields.Any(f => !f.Hidden && string.Equals(f.Name, leaf, StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 4: 跑測試 PASS + 全量回歸** `dotnet test` → 基線 655 + 新增全綠
- [ ] **Step 5: Commit** `fix(security): exclude hidden fields from relation-path filter/sort leaf (SEC-1)`

## Task 2: CS-2 — 畸形 id 回 400 而非 500

**Files:**
- Modify: `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs:790-808`（`ConvertId`）
- Test: repository 測試 + WebApplicationFactory 整合測試（envelope：`GET /api/items/article/not-a-guid` → 400，`error.code` 為既有 `ErrorCodes` 之 validation/query 值，非 500）

**Interfaces:**
- Consumes: `IdParsing.ParseTo(string id, Type targetType)`（Application 層現成，Infra→App 參照合法）— 兩分支皆包 try/catch 丟 `QueryException`
- Produces: `ConvertId` 對任何無法解析的 id 丟 `QueryException`（`StruoExceptionHandler.Map` 已映射 → 400）；合法但不存在維持 404

- [ ] **Step 1: 失敗測試（單元）** — 畸形 guid 經 `GetByIdAsync`/`DeleteAsync` 丟 `QueryException`（現況：`FormatException`）
- [ ] **Step 2: 失敗測試（整合）** — WebApplicationFactory 打 `GET /api/items/article/not-a-guid`，斷言 `400` + envelope `success:false` + `error.message` 不含例外型別名（9a 教訓：連 message 一起斷言）
- [ ] **Step 3: 實作** — `ConvertId` 全體改委派：

```csharp
private static object ConvertId(string id, EntityDescriptor d)
{
    var idType = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
    return Struo.Application.Query.IdParsing.ParseTo(id, idType); // 統一丟 QueryException(400)
}
```

（確認 `IdParsing` 已涵蓋 Guid/IConvertible 與 Nullable unwrap — 已讀碼確認涵蓋。原 `ArgumentException` 分支一併消失。）
- [ ] **Step 4: 全量回歸**（涵蓋 GetById/Update/Delete/Restore/revert by-id 路徑既有測試不得變紅）
- [ ] **Step 5: Commit** `fix: malformed item id returns 400 QueryException instead of masked 500 (CS-2)`

## Task 3: CS-1 — factory 改 SqlSugarScope（GraphQL 併發 thread-safety）

**Files:**
- Modify: `src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs:118`
- Test: factory 單元測試（回傳型別）；併發 smoke（整合）；live gate 併發 GraphQL

**Interfaces:**
- Produces: `SqlSugarClientFactory.Create(...)` 回傳 `SqlSugarScope`（實作 `ISqlSugarClient`，thread-safe，內部 AsyncLocal per-context）；DI 註冊（AddScoped）與呼叫端零改動

- [ ] **Step 1: 失敗測試** — `Assert.IsType<SqlSugarScope>(SqlSugarClientFactory.Create(opts, accessor))`
- [ ] **Step 2: 實作** — `var client = new SqlSugarClient(config);` → `var client = new SqlSugarScope(config);`。確認後續 `client.QueryFilter.AddTableFilter<ISoftDeletable>(...)` 與 `AuditAop.Register(client, currentUser)` 在 `SqlSugarScope` 上等效生效（SqlSugarScope 轉發至 ScopedContext；若 AOP/QueryFilter 掛載語意不同，改於 config 的 `ConfigureExternalServices`/`AopEvents` 掛載 — 實作者須以測試證明 soft-delete filter 與 audit AOP 仍生效，這是本 task 的主要風險點）
- [ ] **Step 3: 併發整合測試** — WebApplicationFactory（SQLite）POST `/graphql` body `{ articles { items { id } } tags { items { id } } faqItems { items { id } } }` 併發 20 次，斷言全部 200 且無 error（現況間歇炸「connection already open」；SQLite 上可能不穩定重現 — 若無法穩定 FAIL，允許以 live PG gate 為 RED 證據，測試保留為回歸 smoke）
- [ ] **Step 4: 全量回歸**（重點：soft-delete 測試、audit 欄位測試、revisions 交易測試全綠 — SqlSugarScope 交易語意差異就會在這裡爆）
- [ ] **Step 5: Commit** `fix: thread-safe SqlSugarScope for concurrent GraphQL resolvers (CS-1)`

## Task 4: SEC-2 — 對外 revision snapshot 過濾 hidden 欄位

**Files:**
- Create: `src/Struo.Application/Query/RevisionSnapshotRedactor.cs`（純函式：snapshot JSON + metadata → 移除 hidden top-level key 與 `translations.{locale}` 內 hidden translatable key 後之 JSON）
- Modify: `src/Struo.Application/Query/ItemService.cs:741-748`（`GetRevisionAsync` 回傳前過濾；`RevertAsync`:710 **不變**，仍用完整 snapshot）；更新 `GetRevisionAsync`/`RevisionSnapshotBuilder` 的 doc comment（RBAC-bypass 說明改為「僅限 revert 內部」）
- Test: `tests/Struo.Tests/Query/` revisions 測試檔 + REST/GraphQL 整合測試

**Interfaces:**
- Produces: `static string RevisionSnapshotRedactor.RedactHidden(string snapshotJson, CollectionMetadata meta)` — 回新 JSON 字串，不 mutate 輸入；`JsonDocument` 全程 `using`（CS-5 同類 pool-leak 禁止）
- Consumes: `CollectionMetadata.Fields`（`Hidden`/`Translatable` flag）

- [ ] **Step 1: 失敗測試（單元）** — 含 hidden 欄位 fixture 的 snapshot 經 `GetRevisionAsync` 回傳後不含該 key；`translations` 內 hidden translatable 欄位亦被移除；非 hidden 欄位原樣保留
- [ ] **Step 2: 失敗測試（revert 完整性）** — revert 後 hidden 欄位值**仍被還原**（證明 `RevertAsync` 未被誤過濾）
- [ ] **Step 3: 實作 Redactor + 接線** — `GetRevisionAsync` 末行改：

```csharp
var rec = await revisions.GetAsync(collection, id, revisionNumber, ct);
if (rec is null) return null;
return rec with { Snapshot = RevisionSnapshotRedactor.RedactHidden(rec.Snapshot, meta) };
```

（`RevisionRecord` 若非 record/不可 with，回傳新實例 — 不 mutate。REST `ItemsController.Revision` 與 GraphQL `xRevision` 都經 `GetRevisionAsync`，單點修復；實作者須以整合測試證明兩協定都乾淨。）
- [ ] **Step 4: 全量回歸**（既有 revisions 21 條 live 情境的單元對應全綠）
- [ ] **Step 5: Commit** `fix(security): redact hidden fields from externally returned revision snapshots (SEC-2)`

## Task 5: DB-1 + DB-2 — purge 參照完整性（應用層，交易內）

**Files:**
- Modify: `src/Struo.Infrastructure/Metadata/RelationshipGraph.cs`（比照 `_inboundRestrict` 增建 `_inboundSetNull`/`_inboundCascade` index；新增「inbound M2M junction」查詢：所有 M2M descriptor 中 `TargetCollection == X` 者）
- Modify: `src/Struo.Application/Metadata/IRelationshipGraph.cs`（+ `InboundSetNull(target)` / `InboundCascade(target)`；junction 面走 `IM2MDescriptorSource` 既有/擴充介面）
- Modify: `src/Struo.Application/Query/ItemService.cs:635-671`（`DeleteAsync` purge 分支重寫為交易內 pipeline）
- Modify: `src/Struo.Application/Query/IItemRepository.cs` + `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs`（+ 新方法，見下）
- Modify: `src/Struo.Application/Revisions/IRevisionStore.cs` + `src/Struo.Infrastructure/Revisions/SqlSugarRevisionStore.cs`（+ `DeleteForItemAsync`）
- Test: 新 `tests/Struo.Tests/Query/PurgeIntegrityTests.cs`

**Interfaces（Produces）:**
- `IItemRepository`:
  - `Task SetForeignKeyNullAsync(string sourceCollection, string foreignKeyProperty, object typedId, CancellationToken ct)` — 實作用 `db.Updateable<T>().SetColumns("<fkcol> = NULL").Where($"{fkcol} = @__fk", new { __fk = typedId })`：SET 端用 **SQL 字面 NULL**（無參數 → 無 42804 typed-null 問題），Where 端參數化，欄名一律出自 `EntityMaintenance.GetDbColumnName`（非使用者輸入）
  - `Task DeleteByPropertyAsync(Type entityType, string property, object value, CancellationToken ct)` — junction/translation 列清理（junction 非 collection，走 Type）
- `IRevisionStore.DeleteForItemAsync(string collection, string itemId, CancellationToken ct)`
- `DeleteAsync` purge 語意（全部包在 `repository.InTransactionAsync`，nesting-safe helper）：
  1. **Restrict**（現有邏輯，移入交易）→ 丟 `RelationConflictException`
  2. **SetNull**：每個 inbound SetNull `(source, fk)` → `SetForeignKeyNullAsync`
  3. **Cascade**：每個 inbound Cascade `(source, fk)` → 查 referencing ids → 對每筆**遞迴走同一 purge core**（自身 junction/translation/revision 也清），`HashSet<(string,string)>` visited 防環
  4. **own M2M junction**（parent 側）+ **inbound M2M junction**（target 側）→ `DeleteByPropertyAsync`
  5. **translations**：`meta.Translation` 存在時 `DeleteByPropertyAsync(tm.TranslationEntityType, tm.ForeignKeyProperty, typedId)`
  6. **revisions**：`meta.Revisions` 時 `DeleteForItemAsync`
  7. 父列 `DeleteGenericAsync`
- **files 刻意不動**（共用媒體，審核明載）；soft-delete（非 purge）分支行為不變

- [ ] **Step 1: 失敗測試組**（SQLite，逐條）：
  - purge Article → `article_translations`/`article_tags`/`revisions` 該 item 列全清
  - purge Tag → `article_tags.tagid` 指向列全清（inbound junction）
  - purge 被引用的 Category（`Article.Category` 為 SetNull）→ `articles.categoryid` 變 NULL、article 本體保留
  - Cascade：測試專用 entity 對（O2M/M2O `OnDelete.Cascade`）→ purge parent 連 child（含 child 的 translations/revisions）全刪；環狀 Cascade 不無限迴圈
  - Restrict 仍丟 `RelationConflictException` 且交易 rollback（無半套刪除）
  - 途中任一步丟例外 → 全部 rollback（父列仍在、junction 仍在）
- [ ] **Step 2: RelationshipGraph 擴充 + 測試**（inbound index 三分法 Restrict/SetNull/Cascade、target 側 M2M 查詢）
- [ ] **Step 3: repository 新方法 + 測試**（含 PG 42804 迴避的字面 NULL 實作）
- [ ] **Step 4: `DeleteAsync` purge pipeline 實作**（遞迴 core 抽 private method，visited set 傳遞）
- [ ] **Step 5: 全量回歸 + Commit** `feat(data): transactional purge integrity — SetNull/Cascade + orphan cleanup for junction/translation/revision (DB-1, DB-2)`

## Task 6: DB-3 — FK / 翻譯鍵 / soft-delete 索引 migration

**Files:**
- Create: `db/migrations/007-hot-path-indexes.sql`
- Test: 無單元測試（純 DDL）；驗收 = live PG 套用後 `pg_indexes` 斷言 + 冪等重跑

**Interfaces:**
- Produces: 冪等（`IF NOT EXISTS`）btree 索引；命名 `ix_<table>_<cols>`；欄名 lowercase-unquoted（SqlSugar 慣例，見 006 註解）

- [ ] **Step 1: 以 metadata 核對欄名** — 讀 `samples/Struo.Sample.Blog/*.cs` 與 `src/Struo.Infrastructure/{Identity,Files,Localization}/*.cs` 確認每個 FK/locale/deletedat 實體屬性 → 物理欄名（lowercase）。**下方 SQL 內任何欄名與實體不符者以實體為準修正。**
- [ ] **Step 2: 撰寫 migration**：

```sql
-- db/migrations/007-hot-path-indexes.sql
-- Audit 2026-07-15 DB-3: M2O FKs / junction FKs / translation (fk,locale) / soft-delete flag.
-- Idempotent; lowercase unquoted identifiers (SqlSugar convention, see 006 header).

-- M2O foreign keys
CREATE INDEX IF NOT EXISTS ix_articles_categoryid        ON articles (categoryid);
CREATE INDEX IF NOT EXISTS ix_categories_parentid        ON categories (parentid);

-- M2M junction (both directions)
CREATE INDEX IF NOT EXISTS ix_article_tags_articleid     ON article_tags (articleid);
CREATE INDEX IF NOT EXISTS ix_article_tags_tagid         ON article_tags (tagid);

-- Translation lookup keys (overlay / translatable sort subquery / translatable search)
CREATE INDEX IF NOT EXISTS ix_article_translations_fk_locale ON article_translations (articleid, locale);
CREATE INDEX IF NOT EXISTS ix_file_translations_fk_locale    ON file_translations (fileid, locale);

-- Identity hot paths
CREATE INDEX IF NOT EXISTS ix_user_roles_userid          ON user_roles (userid);
CREATE INDEX IF NOT EXISTS ix_user_roles_roleid          ON user_roles (roleid);
CREATE INDEX IF NOT EXISTS ix_permissions_roleid         ON permissions (roleid);

-- Soft-delete floor: every list/get carries WHERE deletedat IS NULL — partial index per soft table
CREATE INDEX IF NOT EXISTS ix_articles_live   ON articles (id)   WHERE deletedat IS NULL;
CREATE INDEX IF NOT EXISTS ix_categories_live ON categories (id) WHERE deletedat IS NULL;
```

（soft-deletable 表以 grep `ISoftDeletable` 的實體清單為準增減；`users`/`files` 等若含 `deletedat` 一併加。）
- [ ] **Step 3: live PG 套用 + 驗證** — `psql -f`，再查 `SELECT indexname FROM pg_indexes WHERE indexname LIKE 'ix_%'` 全數存在；重跑第二次無錯（冪等）
- [ ] **Step 4: Commit** `perf(db): hot-path indexes for FKs, translation keys, soft-delete flag (DB-3)`

## Task 7: FE-1 — CollectionListView 深連結載入 schema

**Files:**
- Modify: `frontend/src/views/CollectionListView.vue:63-64`
- Test: `frontend/src/views/__tests__/`（既有 CollectionListView 測試檔）— 新測試**不得**直接 seed `schema.collections`（正是遮蔽 bug 的原因），改 stub schema API

**Interfaces:**
- Consumes: `useSchemaStore().load()`（既有，`loaded` flag 去重，重複呼叫 no-op）

- [ ] **Step 1: 失敗測試** — mock schemaApi 延遲 resolve，mount view（不 pre-seed store），`await flushPromises()` 後斷言 `itemsApi.list` 被呼叫且表格有列（現況：`loadItems` early-return、永不重試 → FAIL）
- [ ] **Step 2: 實作** — `loadItems` 開頭：

```ts
async function loadItems(): Promise<void> {
  await schema.load()          // dedup 由 store 的 loaded flag 處理；深連結/硬重整時等 schema 到位
  if (!meta.value || !canRead.value) return
  ...
}
```

- [ ] **Step 3: `pnpm test` + `pnpm build` 全綠**（vue-tsc 含測試檔）
- [ ] **Step 4: Commit** `fix(frontend): load schema store before list fetch — deep-link/refresh no longer renders empty (FE-1)`

## Batch 1 Gate（全批完成後）

- [ ] `dotnet test` 全綠（≥655 + 新增）；`pnpm test` + `pnpm build` 全綠（≥267 + 新增）
- [ ] **Live gate（真 PG，`ASPNETCORE_URLS=:5080`）：**
  1. SEC-1：`filter[category.<hidden>][_startsWith]` → 400（400 訊息不洩 hidden 存在性）；`?sort=category.<hidden>` → 400
  2. CS-2：`GET /api/items/article/zzz` → 400 envelope；error log 無 stack
  3. CS-1：併發 GraphQL 多 root 欄位 ×50 → 全 200 無 connection error
  4. SEC-2：`GET .../revisions/{n}` snapshot 無 hidden key；revert 後 hidden 值還原
  5. DB-1/2：live purge article/tag/category 各一輪，psql 斷言無孤兒、SetNull 生效
  6. DB-3：`pg_indexes` 斷言 + 任跑一個 list EXPLAIN 用上 index（抽查即可）
  7. FE-1：Playwright 硬導航 `/collections/article`（`pnpm dev --host 127.0.0.1`，Vite IPv4）→ 表格有資料
- [ ] 更新 `docs/architecture-audit-2026-07-15.md` 各項標 ✅FIXED（附 commit hash）
- [ ] Merge to main

---

# Batch 2 — 後端/資料 MEDIUM（批次開始時出細部 plan）

| 項 | 範圍 | 修法要點 | 驗收 |
|---|---|---|---|
| DB-4(=CS-6) | `db/migrations/`、`SqlSugarRevisionStore` | `ix_revisions_item` 改 `UNIQUE(collectionname,itemid,revisionnumber)`（migration 008：先去重再 CREATE UNIQUE）；敗方 insert 失敗 → 寫入交易 rollback 即正確 | 併發雙更新非 auditable revisioned entity → 一方失敗、無重複 no.（live PG） |
| DB-5 | `Revision.cs` 等 entity attribute、`InitTables` | SqlSugar index attribute 讓 CodeFirst 也產 index/unique；Development 啟動 schema-diff assert | InitTables 後 SQLite/PG 皆有 unique；dev 啟動 fail-fast on 分歧 |
| DB-6 | `db/migrations/` | 統一零填充編號 + 輕量 runner + `schema_migrations` 追蹤表（**形式先問 user**） | fresh env 一鍵套用、狀態可查 |
| CS-3 | `SqlSugarItemRepository.cs:224-238,278-289` | `GetByIdGenericAsync` 加 ct（`q.In(id).FirstAsync(ct)`）、`ExecuteReturnEntityAsync` 轉發 ct | 全 by-id/create 路徑 ct 一致轉發（grep 稽核） |
| CS-4(=ARC-2) | `MetadataScanner.ScanDescriptors`、`EntityDescriptor`、`ItemService.Project/ReadProp`、`RevisionSnapshotBuilder` | descriptor 加 `IReadOnlyDictionary<string,PropertyInfo>`（或 compiled getter），呼叫端全改查快取；零行為改變 | 行為不變測試綠 + grep 熱路徑無 `GetProperty(` |
| ARC-3 | `StruoExceptionHandler.cs`、`StruoErrorFilter.cs`、`ItemsController.cs:56-62`、`CollectionResolvers.cs:64-65` | 抽 `DomainErrorMap.Map(Exception)→(code,message)` 單一真相；REST 疊 status、GraphQL 疊 `.WithCode`；修正已漂移的 UNAUTHORIZED/FORBIDDEN 不一致（REST 語意為準） | 對照測試：同一例外雙協定同 code |

Gate：`dotnet test` 全綠 + live PG（DB-4 併發、ARC-3 雙協定 code 對照）+ commit per 項 + 標 ✅。

# Batch 3 — 前端 MEDIUM（批次開始時出細部 plan）

| 項 | 範圍 | 修法要點 |
|---|---|---|
| FE-2 | `CollectionListView.vue` | 共用 `lib/latestWins.ts`（遞增 token，非最新回應丟棄；理想 AbortController） |
| FE-6 | `RelationPicker.vue`、`FilesField.vue` | 同一 helper + 300ms debounce（與 FE-2 一起做） |
| FE-3 | `ItemFormView.vue`、`ItemForm.vue` | `ApiError.details` 依 field name 併入 `errors[field]`，剩餘退 banner |
| FE-4 | `ItemFormView.vue` | 409 → 重抓 item 更新 `model.version` + 警示遠端已變更（「reload latest」動作） |
| FE-5 | `ItemFormView.vue`、`ItemForm.vue` | dirty flag（model vs loaded baseline 深比較）+ `onBeforeRouteLeave` + `beforeunload` 確認 |

Gate：`pnpm test` + `pnpm build` + Playwright（FE-4 409 流程、FE-5 離開守衛）+ commit per 項 + 標 ✅。

# Batch 4 — LOW 批次（批次開始時出細部 plan；先問 DB-8 / SEC-3 / CS-7 決策）

分四組各一 commit：
- **安全組：** SEC-3（user-secrets 遷移 + 輪換指引文件；**輪換為 user 操作**）、SEC-4（GraphQL cost analyzer / alias 上限）、SEC-5（非 published 檔 gate `CanRead(file)`）、SEC-6（預設 content-type 白名單 + presigned disposition）
- **C# 組：** CS-5（`using var src = JsonDocument.Parse(rec.Snapshot)`）、CS-7（視 CS-1 結果）、CS-8（`FileService.DeleteAsync` 改 `InTransactionAsync`）
- **資料組：** DB-7（新欄位 timestamptz 慣例文件化）、DB-8（**依 user 決策**）、DB-9（M2M targetIds 去重 + revert 時清 soft-delete filter）、DB-10（translation `(fk,locale)` UNIQUE，migration + CodeFirst）
- **架構/前端組：** ARC-4（`OrderByExpressionBuilder` 抽檔 + open-generic delegate cache）、ARC-5（options `ValidateOnStart` + `[Required]`）、ARC-6（`ItemsController` 依 `IItemUseCases` 接縫）、FE-7~12（timer 清理、MediaLibrary confirm+gate、刪 HelloWorld.vue、setLink protocol 驗證、errBody 改名+meta 守衛、RelationPicker 選中 label 併入 options）

Gate：雙端測試全綠 + live 抽查（SEC-5 檔案權限、DB-10 unique）+ 標 ✅。

# Batch 5 — ARC-1 ItemService 重構（最後；批次開始時出細部 plan）

前置：Batch 1–4 全綠 merge 後、9d hooks 之前。純行為不變重構，分四步各自全綠 commit：
1. 抽 `ItemDeserializer` + per-interface `IFieldValidator` 策略 registry（keyed by `FieldInterface`，對映前端 `lib/fieldTypes/`；消除 `SyncTranslationsAsync` 重複的 Required/MaxLength 邏輯）
2. 抽 `ItemProjector`（含 CS-4 已快取之 accessor）
3. 抽 `TranslationOverlay` / `DeepExpansionCoordinator`
4. `ItemService` 收斂為編排層（目標 <400 行）；`ItemsController`/GraphQL adapter 接線不變

Gate：**零行為變更** — 全量測試不改一行而全綠 + live gate 快速回歸（CRUD/i18n/relations/revisions 各一輪）+ 標 ✅。

---

## 執行方式

依既有專案慣例：**subagent-driven development**（dev subagent 用 Sonnet、review 用 Opus — memory: workflow-model-and-cost-prefs）。每 task：實作 subagent → code review（csharp-reviewer / typescript-reviewer）→ 修 CRITICAL/HIGH → commit。安全相關 task（SEC-*）加 security-reviewer pass。
