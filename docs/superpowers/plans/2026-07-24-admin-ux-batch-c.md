# Admin UX Batch C Implementation Plan — 媒體資料夾 (#5) + 檔名帶入 Title (#7) + 移除完整編輯連結 (#6 剩餘)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 媒體庫獲得巢狀資料夾(純系統整理、與實體儲存解耦)、上傳自動帶入去副檔名檔名為預設語系 Title、移除 MediaDetailDialog 指向 System>>File 的完整編輯連結。

**Architecture:** 新核心實體 `MediaFolder`(自引用樹,走現成通用 `/api/items/mediafolder`,不新開 controller)+ `File.FolderId` 可空 FK。刪除守衛靠框架**現成**的 `OnDelete.Restrict` 機制(`ItemPurgePipeline.CheckRestrictAsync` → `RelationConflictException` → 409 `CONFLICT`),零新守衛程式碼、只需宣告 relation。循環守衛為新的 metadata 驅動 `SelfReferenceCycleGuard`(吃現成的 `RelationMetadata.SelfReferencing` 旗標,sample `Category` 同步受惠)。前端媒體庫採 Drive 式資料夾卡片 + 麵包屑。

**Tech Stack:** .NET 10 / SqlSugarCore / PostgreSQL(SQLite tests)· Vue 3 + PrimeVue(TreeSelect)+ vue-i18n · xUnit / Vitest / Playwright。

**Spec:** `docs/superpowers/specs/2026-07-24-admin-ux-batch-c-design.md`

## Global Constraints

- 全 DB 存取走 SqlSugar ORM;禁止 vendor SQL(migration `.sql` 檔除外)。
- Domain 專案不得引用外部套件(`virtual` 修飾與屬性 override 不在此限)。
- 框架程式碼(`src/Struo.*`)不得引用 `samples/*`。
- Migration 冪等 + forward-only;新時間欄位一律 `timestamptz`(DB-7);dev InitTables(entity CodeFirst)與 migration schema 必須一致(DB-16 parity)。
- 前端 i18n:en/zh-TW key 集合必須相同(`locales.test.ts` 強制);所有新 UI 字串走 i18n。
- Outbound JSON camelCase;REST 錯誤 envelope `{success:false,error:{code,message}}`。
- TDD:每個行為先寫失敗測試;commit 遵循 conventional commits(無 attribution)。
- 前端 gate:`pnpm test` + `pnpm build`(vue-tsc);後端 gate:`dotnet test`。
- 後端埠:live 驗證用 :5221(launch profile,`dotnet run`);不要動 vite proxy。
- 不做:#12 File 軟刪除、多選批次移動、資料夾層級權限、搜尋限定資料夾。
- **計畫內程式碼以「精讀過的現檔」為準寫成;若鄰近程式碼與計畫片段有出入,以現檔模式為準並在 commit message 註明。**

## 既有事實(實作者必讀,免重查)

- `OnDelete` enum = `{ Restrict, Cascade, SetNull }`;`CmsRelationAttribute.OnDelete` **預設 Restrict**(`src/Struo.Domain/Metadata/Attributes/CmsRelationAttribute.cs:13`)。
- `ItemPurgePipeline.CheckRestrictAsync`(`src/Struo.Application/Query/Write/ItemPurgePipeline.cs:27`)已對 inbound Restrict 關聯丟 `RelationConflictException`;`DomainErrorMap` 映射為 409 + `CONFLICT`(`src/Struo.Api/Http/DomainErrorMap.cs:32,48`)。`QueryException` → 400 `BAD_USER_INPUT`。
- `ItemService.UpdateCoreAsync` 已 overlay 客戶端送來的 ManyToOne relation FK(`src/Struo.Application/Query/ItemService.cs:180-189`)→ `folderId` 經 items update 可寫,免改。
- `QueryValidator` 自動放行 ManyToOne FK 過濾(filter[folderId][_eq]);`_null`/`_nnull` 運算子已存在(`src/Struo.Application/Query/QueryParser.cs:16`,不使用 FieldValue)。
- `RelationMetadata.SelfReferencing` 已由 MetadataScanner 填好(`MetadataScanner.cs:443`:`target == type`)。
- `ILanguageProvider.DefaultCode()` 永不為 null(fallback 鏈 → `"en"`,`LanguageProvider.cs:20-21`)。
- `PermissionMatrix.vue:29`:Role 權限矩陣**已包含 hidden collections** → `mediafolder` 自動出現,免改 RBAC UI。
- timestamptz + InitTables parity 先例:`SiteSettings.UpdatedAt` 用 `[SugarColumn(ColumnDataType = "timestamptz")]`(SQLite 照收字面型別,InitTables 不受影響)。
- 前端 `FilterSpec = Record<string, { op: string; value: string }>`(`buildListQuery.ts:1`);PrimeVue TreeSelect 單選綁 `Record<key, true>` 物件(範例 `RelationPicker.vue:113-118,136-144`);`buildRelationTree(rows, parentKey, excludeId?)` 現成(`lib/buildRelationTree.ts`)。
- `apiClient.postForm` 已存在(`filesApi.upload` 在用);`ApiError` 有 `status`/`code`。
- 後端測試 fixture 模式:抄 `tests/Struo.Tests/Files/FileCollectionTests.cs` 與 `tests/Struo.Tests/Query/ItemServiceFilesFieldTests.cs` 開頭的建構方式(SQLite in-memory + InitTables + 手工組 service)。**計畫中的測試碼是行為規格;fixture 建構細節以這兩檔現行模式為準。**

---

### Task 1: 後端 — MediaFolder 實體 + File.FolderId + 註冊 + migration 002

**Files:**
- Create: `src/Struo.Infrastructure/Files/MediaFolder.cs`
- Modify: `src/Struo.Domain/Auditing/AuditableEntity.cs`(CreatedAt/UpdatedAt 加 `virtual`)
- Modify: `src/Struo.Infrastructure/Files/File.cs`(FolderId + Folder nav + SugarIndex)
- Modify: `src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs`(加 MediaFolder)
- Create: `db/migrations/002-media-folders.sql`
- Test: `tests/Struo.Tests/Files/MediaFolderTests.cs`(新檔)

**Interfaces:**
- Consumes: 現成框架(AuditableEntity / CmsCollection / ItemPurgePipeline Restrict)。
- Produces: collection 路由 `mediafolder`(欄位 `name`、`parentId`;relation `parent`)、`file.folderId`(可寫 FK、可過濾)。後續全部任務依賴這兩個形狀。

- [ ] **Step 1: 寫失敗測試**(fixture 建構抄 `FileCollectionTests.cs` 現行模式;下列為行為規格)

```csharp
// tests/Struo.Tests/Files/MediaFolderTests.cs — 測試意圖(方法名 = 規格):
// 1. Mediafolder_collection_is_registered_and_hidden
//    metadata.GetCollection("mediafolder") 非 null;Hidden == true;Group == "System"。
// 2. Create_list_update_folder_roundtrip
//    ItemService.CreateAsync("mediafolder", {name:"A"}) → QueryAsync 查得到;
//    CreateAsync({name:"B", parentId:<A.id>}) → GetAsync(B).parentId == A.id;
//    UpdateAsync(B, {name:"B2"}) → name 更新。
// 3. Files_filter_by_folderId_eq_and_null
//    建資料夾 A、兩個 File 列(直接 db.Insertable,一個 FolderId=A、一個 null);
//    QueryAsync("file", filter folderId _eq A) 只回資料夾內那筆;
//    filter folderId _null 只回未分類那筆。
// 4. Delete_folder_with_files_is_rejected_409
//    資料夾 A + File.FolderId=A → ItemService.DeleteAsync("mediafolder", A)
//    → 丟 RelationConflictException。
// 5. Delete_folder_with_subfolder_is_rejected_409
//    A ← B(B.parentId=A)→ DeleteAsync(A) 丟 RelationConflictException。
// 6. Delete_empty_folder_succeeds
//    清空後 DeleteAsync(A) == true;GetAsync(A) == null。
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter "FullyQualifiedName~MediaFolderTests"`
Expected: FAIL(mediafolder collection 不存在 → GetCollection 回 null / 編譯錯誤)

- [ ] **Step 3: 實作**

`src/Struo.Domain/Auditing/AuditableEntity.cs` — 兩行改動(讓子類可掛 SugarColumn override;既有實體零行為變化):

```csharp
public virtual DateTime CreatedAt { get; set; }
// (CreatedBy 不變)
public virtual DateTime UpdatedAt { get; set; }
```

`src/Struo.Infrastructure/Files/MediaFolder.cs`(新檔):

```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Framework-owned media-library folder (#5): pure in-system organisation, decoupled from
/// physical storage keys. Self-referencing tree via <see cref="ParentId"/>. Hidden — the media
/// library is its only UI; CRUD goes through the generic items API (route <c>mediafolder</c>).
/// Deleting a folder that still contains files or subfolders is rejected by the framework's
/// OnDelete.Restrict guard (ItemPurgePipeline.CheckRestrictAsync) — declared on the inbound
/// relations (File.Folder, MediaFolder.Parent), no bespoke guard code.
/// </summary>
[SugarTable("media_folders")]
[SugarIndex("ix_media_folders_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("MediaFolder", Group = "System", DefaultDisplayField = nameof(Name), Hidden = true)]
public sealed class MediaFolder : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    // DB-7: new framework table → timestamptz (the baseline AuditableEntity tables predate the
    // rule and stay `timestamp`; convention binds new schema only). Same literal-type mechanism
    // as SiteSettings.UpdatedAt, so InitTables emits the identical column type (DB-16 parity).
    [SugarColumn(ColumnDataType = "timestamptz")] public override DateTime CreatedAt { get; set; }
    [SugarColumn(ColumnDataType = "timestamptz")] public override DateTime UpdatedAt { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public Guid? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public MediaFolder? Parent { get; set; }
}
```

`src/Struo.Infrastructure/Files/File.cs` — 類別上加 `[SugarIndex("ix_files_folderid", nameof(FolderId), OrderByType.Asc)]`,`Status` 之後、`Translations` 之前插入:

```csharp
    // #5 media folders: nullable organisational FK — NOT ReadOnly (moving a file = items update;
    // UpdateCoreAsync's M2O-FK overlay handles it). OnDelete.Restrict on the nav relation means a
    // folder still containing files cannot be deleted (framework guard, 409 CONFLICT).
    [SugarColumn(IsNullable = true)]
    public Guid? FolderId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(FolderId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public MediaFolder? Folder { get; set; }
```

`FrameworkEntityTypes.cs`:`All` 陣列加 `typeof(MediaFolder)`(放 `File`/`FileTranslation` 旁)。

`db/migrations/002-media-folders.sql`(新檔;欄位型別/風格對齊 `001-core-baseline.sql` 的寫法,唯時間欄位依 DB-7 用 timestamptz):

```sql
-- 002-media-folders.sql
-- 2026-07-24 · Batch C (#5 media folders) · media_folders table + files.folderid organisational FK.
-- Idempotent, forward-only. DB-7: temporal columns of NEW tables are timestamptz.
CREATE TABLE IF NOT EXISTS media_folders (
    id uuid NOT NULL PRIMARY KEY,
    name character varying(255) NOT NULL,
    parentid uuid NULL,
    createdat timestamptz NOT NULL,
    createdby uuid NULL,
    updatedat timestamptz NOT NULL,
    updatedby uuid NULL,
    version bigint NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_media_folders_parentid ON media_folders (parentid);

ALTER TABLE files ADD COLUMN IF NOT EXISTS folderid uuid NULL;
CREATE INDEX IF NOT EXISTS ix_files_folderid ON files (folderid);
```

(NOT NULL/型別如與 InitTables 輸出不合,以 live PG parity 驗證結果修 SQL,不動 entity。)

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test`(整套,確認無回歸——特別是 metadata/graph/GraphQL schema 相關套件)
Expected: 全 PASS

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Domain/Auditing/AuditableEntity.cs src/Struo.Infrastructure/Files/MediaFolder.cs src/Struo.Infrastructure/Files/File.cs src/Struo.Infrastructure/Metadata/FrameworkEntityTypes.cs db/migrations/002-media-folders.sql tests/Struo.Tests/Files/MediaFolderTests.cs
git commit -m "feat(media): MediaFolder core entity + File.FolderId with Restrict delete guard (#5)"
```

---

### Task 2: 後端 — 自引用循環守衛(metadata 驅動)

**Files:**
- Create: `src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs`
- Modify: `src/Struo.Application/Query/ItemService.cs`(欄位初始化 + UpdateCoreAsync 一行呼叫)
- Test: `tests/Struo.Tests/Query/SelfReferenceCycleGuardTests.cs`(新檔)

**Interfaces:**
- Consumes: `RelationMetadata.SelfReferencing`/`Kind`/`ForeignKey`、`IItemRepository.GetByIdAsync`、`IEntityRegistry`(`d.Properties` 大小寫不敏感,同 `ItemService.cs:186` 用法)。
- Produces: `SelfReferenceCycleGuard.EnsureNoCycleAsync(string collection, CollectionMetadata meta, object entityAfterOverlay, CancellationToken ct)` — UpdateCoreAsync 在 FK overlay 後、交易前呼叫;循環 → `QueryException`(400)。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Struo.Tests/Query/SelfReferenceCycleGuardTests.cs — 行為規格:
// 1. Setting_parent_to_self_is_rejected
//    mediafolder A → UpdateAsync(A, {parentId:A}) 丟 QueryException(訊息含 "cycle")。
// 2. Setting_parent_to_own_descendant_is_rejected
//    A ← B ← C(parentId 鏈)→ UpdateAsync(A, {parentId:C}) 丟 QueryException。
// 3. Reparenting_to_sibling_branch_succeeds
//    A ← B、A ← C → UpdateAsync(C, {parentId:B}) 成功,C.parentId == B。
// 4. Clearing_parent_succeeds
//    UpdateAsync(B, {parentId:null}) 成功。
// 5. Category_sample_collection_is_also_guarded(證明 metadata 驅動,非 mediafolder 特例)
//    category X → UpdateAsync(X, {parentId:X}) 丟 QueryException。
//    (fixture 需註冊 sample Category entity——測試專案已引用 samples;抄現有含 Category 的測試建構。)
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter "FullyQualifiedName~SelfReferenceCycleGuardTests"`
Expected: FAIL(1/2/5 未丟例外)

- [ ] **Step 3: 實作**

`src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs`(新檔):

```csharp
// src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Rejects a self-referencing ManyToOne update that would create a parent cycle (A→B→…→A).
/// Metadata-driven via RelationMetadata.SelfReferencing, so every tree collection (mediafolder,
/// sample Category) is covered — previously only the SPA's excludeId pruning guarded this, which
/// a direct API caller bypasses. Create is exempt: a fresh server-generated id cannot appear in
/// any existing ancestor chain. Walks the incoming parent's ancestor chain; a dangling parent id
/// ends the walk (FK existence is not this guard's concern — DB-14 app-only stance). MaxDepth is
/// a defensive stop against pre-existing corrupt data looping forever.
/// </summary>
public sealed class SelfReferenceCycleGuard(IItemRepository repository, IEntityRegistry registry)
{
    private const int MaxDepth = 64;

    public async Task EnsureNoCycleAsync(
        string collection, CollectionMetadata meta, object entityAfterOverlay, CancellationToken ct)
    {
        foreach (var rel in meta.Relations)
        {
            if (!rel.SelfReferencing || rel.Kind != RelationKind.ManyToOne || rel.ForeignKey is null) continue;
            var d = registry.Get(collection);
            if (d is null) return;
            var fkProp = d.Properties.GetValueOrDefault(rel.ForeignKey);
            if (fkProp is null) continue;
            var selfId = d.Properties.GetValueOrDefault(d.IdProperty)?.GetValue(entityAfterOverlay)?.ToString();
            if (selfId is null) continue;

            var parentId = fkProp.GetValue(entityAfterOverlay)?.ToString();
            var depth = 0;
            while (parentId is not null)
            {
                if (string.Equals(parentId, selfId, StringComparison.OrdinalIgnoreCase))
                    throw new QueryException(
                        $"'{rel.ForeignKey}' would create a cycle in '{collection}'.");
                if (++depth > MaxDepth)
                    throw new QueryException(
                        $"'{rel.ForeignKey}' ancestor chain exceeds {MaxDepth} levels in '{collection}'.");
                var parent = await repository.GetByIdAsync(collection, parentId, ct: ct);
                if (parent is null) break;
                parentId = fkProp.GetValue(parent)?.ToString();
            }
        }
    }
}
```

`ItemService.cs` 接線 — 欄位區(`purge` 旁)加:

```csharp
    private readonly SelfReferenceCycleGuard cycleGuard = new(repository, registry);
```

`UpdateCoreAsync` 內、version 讀取(`if (existing is Struo.Domain.Auditing.AuditableEntity ex)`)之前加:

```csharp
        // Self-referencing tree collections: reject a parentId that points at the item itself or
        // one of its descendants (cycle). Runs after the FK overlay so it sees the incoming value.
        await cycleGuard.EnsureNoCycleAsync(collection, meta, existing, ct);
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test`
Expected: 全 PASS(含既有 Category/relations 測試無回歸)

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs src/Struo.Application/Query/ItemService.cs tests/Struo.Tests/Query/SelfReferenceCycleGuardTests.cs
git commit -m "feat(items): metadata-driven self-reference cycle guard on update (mediafolder + Category)"
```

---

### Task 3: 後端 — 上傳進資料夾 + 檔名帶入 Title (#7)

**Files:**
- Modify: `src/Struo.Api/Controllers/FilesController.cs`(Upload 讀 `folderId` form 欄位;回應加 `folderId`)
- Modify: `src/Struo.Infrastructure/Files/FileService.cs`(ctor 加 `ILanguageProvider`;UploadAsync/SaveAsync 簽章加 `Guid? folderId`;種 Title)
- Test: `tests/Struo.Tests/Files/FileUploadFolderAndTitleTests.cs`(新檔)

**Interfaces:**
- Consumes: Task 1 的 `MediaFolder`/`File.FolderId`;`ILanguageProvider.DefaultCode()`(永不 null);`IItemRepository.InTransactionAsync`(FileService 已注入 repository)。
- Produces: `POST /api/files` 接受可選 multipart 欄位 `folderId`(非法 Guid / 不存在 → 400 `BAD_USER_INPUT`);201 回應含 `folderId`;上傳後 `file_translations` 有預設語系 Title 列。`FileService.UploadAsync(Stream, string, string, long, Guid? folderId = null, CancellationToken ct = default)`。

- [ ] **Step 1: 寫失敗測試**

```csharp
// tests/Struo.Tests/Files/FileUploadFolderAndTitleTests.cs — 行為規格
// (fixture 抄 FileUploadTests.cs 現行建構;FileService ctor 多一個 ILanguageProvider):
// 1. Upload_seeds_default_locale_title_without_extension
//    UploadAsync(..., "photo-01.jpg", "image/jpeg", ...) →
//    db.Queryable<FileTranslation>().Where(FileId==id) 恰一列,
//    Locale == languages.DefaultCode(),Title == "photo-01",Alt == null。
// 2. Upload_dotfile_falls_back_to_full_name
//    ".gitignore"(text/plain)→ Title == ".gitignore"(GetFileNameWithoutExtension 產物為空 → 用全名)。
// 3. Upload_with_valid_folderId_sets_folder
//    先插 MediaFolder A → UploadAsync(..., folderId: A.Id) → File.FolderId == A.Id。
// 4. Upload_with_unknown_folderId_throws_QueryException
//    UploadAsync(..., folderId: Guid.NewGuid()) 丟 QueryException;且無 files/file_translations 殘留列。
// 5. Upload_without_folderId_leaves_folder_null(FolderId == null;Title 照種。)
// 6. Upload_title_longer_than_255_is_truncated(檔名 300 字元 → Title.Length == 255。)
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test --filter "FullyQualifiedName~FileUploadFolderAndTitleTests"`
Expected: FAIL(UploadAsync 無此 overload / 無 translation 列)

- [ ] **Step 3: 實作**

`FileService.cs`:ctor 加 `ILanguageProvider languages`(參數列最後、`IItemRepository repository` 之後)+ `using Struo.Application.Localization;`。

`UploadAsync` 簽章:`(Stream content, string fileName, string contentType, long length, Guid? folderId = null, CancellationToken ct = default)`;結尾改 `return await SaveAsync(buffer, fileName, contentType, dims, folderId, ct);`。

`SaveAsync` 改為:

```csharp
    private async Task<File> SaveAsync(
        Stream buffer, string fileName, string contentType, (int Width, int Height)? dims,
        Guid? folderId, CancellationToken ct)
    {
        if (folderId is { } fid)
        {
            // App-only existence check (DB-14 accepted stance: no DB FK). A folder deleted between
            // this check and the insert is the same narrow race every FK-less write here has.
            var folder = await db.Queryable<MediaFolder>().In(fid).FirstAsync(ct);
            if (folder is null) throw new QueryException($"Folder '{fid}' does not exist.");
        }

        var key = StorageKey.Create(fileName);
        await storage.SaveAsync(key, buffer, contentType, ct);

        var entity = new File
        {
            Id = Guid.NewGuid(),
            StorageKey = key,
            FileName = fileName,
            ContentType = contentType,
            Size = buffer.Length,          // (既有 SEC-15 註解保留)
            Width = dims?.Width,
            Height = dims?.Height,
            Status = "published",
            FolderId = folderId,
        };

        // #7: seed the default-locale Title from the filename (extension stripped) so an upload is
        // immediately human-readable everywhere. Dotfiles ("." prefix strips to empty) fall back to
        // the full name; clamp to the column width (varchar 255). Same transaction as the file row —
        // a seed failure must not leave a title-less file (repository join-if-active, CS-8).
        var title = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(title)) title = fileName;
        if (title.Length > 255) title = title[..255];
        var translation = new FileTranslation
        {
            FileId = entity.Id, Locale = languages.DefaultCode(), Title = title,
        };

        await repository.InTransactionAsync(async () =>
        {
            await db.Insertable(entity).ExecuteCommandAsync(ct);
            await db.Insertable(translation).ExecuteCommandAsync(ct);
        }, ct);
        return entity;
    }
```

(原 `// ExecuteReturnEntityAsync has no CancellationToken overload...` 註解保留在 Insertable 上方。)

`FilesController.cs` Upload — `if (file is null) ...` 之後、`await using var stream` 之前:

```csharp
        Guid? folderId = null;
        var folderRaw = form["folderId"].ToString();
        if (!string.IsNullOrEmpty(folderRaw))
        {
            if (!Guid.TryParse(folderRaw, out var parsedFolder))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput, "Invalid 'folderId'.");
            folderId = parsedFolder;
        }
```

呼叫改 `files.UploadAsync(stream, file.FileName, file.ContentType, file.Length, folderId, ct)`;201 匿名物件加 `folderId = created.FolderId`(Get 動作的回應物件同步加 `folderId = row.FolderId`,維持兩端 payload 一致)。

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test`
Expected: 全 PASS(既有 FileUpload/FileService 測試因 ctor 變更需同步補 `ILanguageProvider` 參數——用測試現有的語言 fixture 或最小 stub)

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Api/Controllers/FilesController.cs src/Struo.Infrastructure/Files/FileService.cs tests/Struo.Tests/Files/FileUploadFolderAndTitleTests.cs tests/Struo.Tests/Files
git commit -m "feat(files): upload into folder + seed default-locale Title from filename (#5/#7)"
```

---

### Task 4: 前端 — lib/api 層(folder 查詢輔助 + upload folderId)

**Files:**
- Modify: `frontend/src/api/filesApi.ts`(upload 加 `folderId?`;FileMeta 加 `folderId`)
- Modify: `frontend/src/lib/mediaQuery.ts`(`mediaFolderFilter`)
- Create: `frontend/src/lib/folderTree.ts`
- Test: `frontend/src/lib/mediaQuery.test.ts`(擴充)、`frontend/src/lib/folderTree.test.ts`(新)、`frontend/src/api/filesApi.test.ts`(擴充)

**Interfaces:**
- Produces(Task 5/6/7 依賴,簽章如下):
  - `filesApi.upload(file: File, folderId?: string | null): Promise<FileMeta>`(有值才 append form 欄位)
  - `mediaFolderFilter(folderId: string | null): FilterSpec` — 有 id → `{ folderId: { op: '_eq', value: id } }`;null → `{ folderId: { op: '_null', value: 'true' } }`(後端 `_null` 不看 value)
  - `type FolderRow = { id: string; name: string; parentId: string | null; version?: number }`
  - `toFolderRows(data: Record<string, unknown>[]): FolderRow[]`
  - `childFolders(folders: FolderRow[], parentId: string | null): FolderRow[]`
  - `folderPath(folders: FolderRow[], id: string | null): FolderRow[]`(麵包屑祖先鏈 root→…→id;循環安全)

- [ ] **Step 1: 寫失敗測試**

```ts
// frontend/src/lib/folderTree.test.ts
import { describe, it, expect } from 'vitest'
import { toFolderRows, childFolders, folderPath, type FolderRow } from './folderTree'

const rows: FolderRow[] = [
  { id: 'a', name: 'A', parentId: null },
  { id: 'b', name: 'B', parentId: 'a' },
  { id: 'c', name: 'C', parentId: 'b' },
  { id: 'x', name: 'X', parentId: null },
]

describe('toFolderRows', () => {
  it('maps items rows and defaults missing parentId to null', () => {
    expect(toFolderRows([{ id: 'a', name: 'A' }, { id: 'b', name: 'B', parentId: 'a', version: 3 }]))
      .toEqual([
        { id: 'a', name: 'A', parentId: null, version: undefined },
        { id: 'b', name: 'B', parentId: 'a', version: 3 },
      ])
  })
})

describe('childFolders', () => {
  it('returns root folders for null and children for an id', () => {
    expect(childFolders(rows, null).map((f) => f.id)).toEqual(['a', 'x'])
    expect(childFolders(rows, 'a').map((f) => f.id)).toEqual(['b'])
  })
})

describe('folderPath', () => {
  it('returns the ancestor chain root-first', () => {
    expect(folderPath(rows, 'c').map((f) => f.id)).toEqual(['a', 'b', 'c'])
  })
  it('returns [] for null and is cycle-safe', () => {
    expect(folderPath(rows, null)).toEqual([])
    const cyclic: FolderRow[] = [
      { id: 'p', name: 'P', parentId: 'q' }, { id: 'q', name: 'Q', parentId: 'p' }]
    expect(folderPath(cyclic, 'p').map((f) => f.id)).toEqual(['q', 'p'])
  })
})
```

```ts
// mediaQuery.test.ts 追加:
// mediaFolderFilter('abc') → { folderId: { op: '_eq', value: 'abc' } }
// mediaFolderFilter(null)  → { folderId: { op: '_null', value: 'true' } }
// filesApi.test.ts 追加:upload(file, 'fid') 後 FormData 含 folderId='fid';
// upload(file) / upload(file, null) 不含 folderId 欄位。
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd frontend; pnpm test -- --run src/lib/folderTree.test.ts src/lib/mediaQuery.test.ts src/api/filesApi.test.ts`
Expected: FAIL(模組不存在 / 函式未定義)

- [ ] **Step 3: 實作**

`frontend/src/lib/folderTree.ts`(新檔):

```ts
export type FolderRow = { id: string; name: string; parentId: string | null; version?: number }

export function toFolderRows(data: Record<string, unknown>[]): FolderRow[] {
  return data.map((r) => ({
    id: String(r.id ?? ''),
    name: typeof r.name === 'string' ? r.name : '',
    parentId: typeof r.parentId === 'string' ? r.parentId : null,
    version: typeof r.version === 'number' ? r.version : undefined,
  }))
}

export function childFolders(folders: FolderRow[], parentId: string | null): FolderRow[] {
  return folders.filter((f) => f.parentId === parentId)
}

/** Breadcrumb ancestor chain, root first, ending at `id`. Cycle-safe (stops on revisit). */
export function folderPath(folders: FolderRow[], id: string | null): FolderRow[] {
  if (!id) return []
  const byId = new Map(folders.map((f) => [f.id, f]))
  const path: FolderRow[] = []
  const seen = new Set<string>()
  let cur = byId.get(id)
  while (cur && !seen.has(cur.id)) {
    seen.add(cur.id)
    path.unshift(cur)
    cur = cur.parentId ? byId.get(cur.parentId) : undefined
  }
  return path
}
```

`mediaQuery.ts` 追加:

```ts
// folderId is a declared ManyToOne relation FK, auto-allowed by QueryValidator. `_null` ignores
// FieldValue server-side; 'true' is a placeholder to satisfy FilterSpec's string shape.
export function mediaFolderFilter(folderId: string | null): FilterSpec {
  return folderId
    ? { folderId: { op: '_eq', value: folderId } }
    : { folderId: { op: '_null', value: 'true' } }
}
```

`filesApi.ts`:`FileMeta` 加 `folderId: string | null`;upload 改:

```ts
  async upload(file: File, folderId?: string | null): Promise<FileMeta> {
    const form = new FormData()
    form.append('file', file)
    if (folderId) form.append('folderId', folderId)
    return apiClient.postForm<FileMeta>('/files', form)
  },
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd frontend; pnpm test -- --run; pnpm build`
Expected: 全 PASS + vue-tsc 過

- [ ] **Step 5: Commit**

```bash
git add frontend/src/lib/folderTree.ts frontend/src/lib/folderTree.test.ts frontend/src/lib/mediaQuery.ts frontend/src/lib/mediaQuery.test.ts frontend/src/api/filesApi.ts frontend/src/api/filesApi.test.ts
git commit -m "feat(media-fe): folder query helpers + upload folderId param"
```

---

### Task 5: 前端 — 媒體庫 Drive 式資料夾導航 + 資料夾 CRUD + 資料夾內上傳

**Files:**
- Create: `frontend/src/components/media/MediaFolderCards.vue`
- Create: `frontend/src/components/media/MediaFolderNameDialog.vue`
- Modify: `frontend/src/views/MediaLibraryView.vue`
- Modify: `frontend/src/components/media/MediaUploadDialog.vue` + `MediaUploadDropzone.vue`(傳遞 `folderId`)
- Modify: `frontend/src/locales/en.ts`、`frontend/src/locales/zh-TW.ts`(media ns 新 key)
- Test: `MediaFolderCards.test.ts`、`MediaFolderNameDialog.test.ts`(新)、`MediaLibraryView.test.ts`、`MediaUploadDropzone.test.ts`(擴充)

**Interfaces:**
- Consumes: Task 4 全部輸出;`itemsApi.list/create/update/remove('mediafolder', …)`;`ApiError`(409 + code `CONFLICT` = 資料夾非空)。
- Produces:
  - `MediaFolderCards` props `{ folders: FolderRow[]; canManage: boolean }`,emits `open(id: string)`、`rename(folder: FolderRow)`、`remove(folder: FolderRow)`
  - `MediaFolderNameDialog` props `{ visible: boolean; header: string; initialName?: string }`,emits `update:visible(boolean)`、`submit(name: string)`
  - `MediaUploadDialog`/`MediaUploadDropzone` 新增 prop `folderId?: string | null` → 傳給 `filesApi.upload(file, folderId)`

- [ ] **Step 1: 寫失敗測試**(mount 模式抄各自現有 `.test.ts`;i18n/PrimeVue 測試裝置沿用)

```ts
// MediaFolderCards.test.ts:
// - 渲染 folders 為卡片(每張含 name);點卡片 emit open(id)。
// - canManage=true 時有 rename/delete 圖示鈕,@click.stop 不觸發 open;emit rename/remove 帶該 folder。
// - canManage=false 無管理鈕。
// MediaFolderNameDialog.test.ts:
// - visible=true 顯示輸入框,initialName 預填;輸入後按確認 emit submit('新名') 且 emit update:visible(false)。
// - 空白名稱時確認鈕 disabled。
// MediaLibraryView.test.ts 追加(mock itemsApi.list 依 collection 分流:'mediafolder' 回資料夾、'file' 回檔案):
// - 初載:root 顯示 parentId=null 的資料夾卡片;itemsApi.list('file') 收到 filter 含 folderId _null。
// - enterFolder('a') 後:list('file') filter 含 folderId _eq 'a';麵包屑顯示 A;卡片變 A 的子資料夾。
// - 搜尋非空:不渲染資料夾卡片;list('file') filter 不含 folderId 鍵(全域搜尋)。
// - onCreateFolder('N'):itemsApi.create('mediafolder', { name:'N', parentId: currentFolderId })。
// - 刪除資料夾收到 ApiError(409,'CONFLICT') → toast 顯示 media.folderNotEmpty(mock useToast)。
// - 型別過濾與 folder 過濾疊加:type='image' 且在資料夾 a → filter 同時含 contentType 與 folderId。
// MediaUploadDropzone.test.ts 追加:
// - prop folderId='fid' 時 uploadFiles 呼叫 filesApi.upload(file, 'fid')。
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd frontend; pnpm test -- --run src/components/media src/views/MediaLibraryView.test.ts`
Expected: FAIL(元件不存在 / prop 未接)

- [ ] **Step 3: 實作**

`MediaFolderCards.vue`(新檔):

```vue
<script setup lang="ts">
import Button from 'primevue/button'
import type { FolderRow } from '../../lib/folderTree'

defineProps<{ folders: FolderRow[]; canManage: boolean }>()
const emit = defineEmits<{
  (e: 'open', id: string): void
  (e: 'rename', folder: FolderRow): void
  (e: 'remove', folder: FolderRow): void
}>()
</script>

<template>
  <div v-if="folders.length" class="folder-grid">
    <div v-for="f in folders" :key="f.id" class="folder-card" role="button" tabindex="0"
         @click="emit('open', f.id)" @keydown.enter="emit('open', f.id)">
      <i class="pi pi-folder folder-card__icon" aria-hidden="true" />
      <span class="folder-card__name">{{ f.name }}</span>
      <span v-if="canManage" class="folder-card__actions">
        <Button icon="pi pi-pencil" text size="small" :aria-label="$t('media.folderRename')"
                @click.stop="emit('rename', f)" />
        <Button icon="pi pi-trash" text size="small" severity="danger" :aria-label="$t('media.folderDelete')"
                @click.stop="emit('remove', f)" />
      </span>
    </div>
  </div>
</template>

<style scoped>
.folder-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; margin-bottom: 16px; }
.folder-card {
  display: flex; align-items: center; gap: 10px; padding: 10px 12px; cursor: pointer;
  border: 1px solid var(--border); border-radius: var(--radius-lg, 12px); background: var(--surface);
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.folder-card:hover { border-color: var(--accent); box-shadow: var(--shadow-1); }
.folder-card__icon { color: var(--accent); font-size: 1.1rem; }
.folder-card__name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
.folder-card__actions { display: flex; }
</style>
```

(卡片用 `div role="button"` 而非 `<button>`:內含管理用 `<Button>`,巢狀原生按鈕是無效 HTML。)

`MediaFolderNameDialog.vue`(新檔):

```vue
<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'

const props = defineProps<{ visible: boolean; header: string; initialName?: string }>()
const emit = defineEmits<{ (e: 'update:visible', v: boolean): void; (e: 'submit', name: string): void }>()

const name = ref('')
watch(() => props.visible, (v) => { if (v) name.value = props.initialName ?? '' })
const valid = computed(() => name.value.trim().length > 0)

function onSubmit(): void {
  if (!valid.value) return
  emit('submit', name.value.trim())
  emit('update:visible', false)
}
</script>

<template>
  <Dialog :visible="visible" modal :header="header" :style="{ width: 'min(90vw, 420px)' }"
          @update:visible="emit('update:visible', $event)">
    <label class="folder-name-field">
      <span>{{ $t('media.folderName') }}</span>
      <InputText v-model="name" autofocus @keydown.enter="onSubmit" />
    </label>
    <template #footer>
      <Button :label="$t('media.folderConfirm')" :disabled="!valid" @click="onSubmit" />
    </template>
  </Dialog>
</template>

<style scoped>
.folder-name-field { display: grid; gap: 4px; }
.folder-name-field > span { font-size: .8rem; color: var(--muted); font-weight: 500; }
</style>
```

`MediaUploadDropzone.vue`:加 `const props = defineProps<{ folderId?: string | null }>()`,`filesApi.upload(file)` → `filesApi.upload(file, props.folderId)`。
`MediaUploadDialog.vue`:props 加 `folderId?: string | null`,`<MediaUploadDropzone :folder-id="folderId" @done="emit('done')" />`。

`MediaLibraryView.vue` — script 增改(邏輯全貌;既有函式除標示處外**逐字保留**):

```ts
// 新增 import:
import MediaFolderCards from '../components/media/MediaFolderCards.vue'
import MediaFolderNameDialog from '../components/media/MediaFolderNameDialog.vue'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import ConfirmDialog from 'primevue/confirmdialog'
import { ApiError } from '../api/apiClient'
import { mediaFolderFilter } from '../lib/mediaQuery'    // 併入既有該檔 import
import { toFolderRows, childFolders, folderPath, type FolderRow } from '../lib/folderTree'

const toast = useToast()
const confirm = useConfirm()

// 資料夾狀態
const folders = ref<FolderRow[]>([])
const currentFolderId = ref<string | null>(null)
const createOpen = ref(false)
const renameTarget = ref<FolderRow | null>(null)

const canManageFolders = computed(() => auth.canWrite('mediafolder'))
const canDeleteFolders = computed(() => auth.canDelete('mediafolder'))
const searchActive = computed(() => search.value.trim().length > 0)
const visibleFolders = computed(() =>
  searchActive.value ? [] : childFolders(folders.value, currentFolderId.value))
const breadcrumb = computed(() => folderPath(folders.value, currentFolderId.value))

async function loadFolders(): Promise<void> {
  try {
    const res = await itemsApi.list('mediafolder', { page: 0, rows: 500, sort: 'name' })
    folders.value = toFolderRows(res.data)
  } catch {
    folders.value = []   // 資料夾載入失敗 → 退化為扁平列表,不阻擋檔案瀏覽
    toast.add({ severity: 'warn', summary: t('media.folderLoadFailed'), life: 3500 })
  }
}

// load():filter 行改為(其餘逐字保留)
//   filter: { ...(mediaTypeFilter(type.value) ?? {}),
//             ...(searchActive.value ? {} : mediaFolderFilter(currentFolderId.value)) },

function enterFolder(id: string): void { currentFolderId.value = id; page.value = 0; load() }
function goToBreadcrumb(id: string | null): void { currentFolderId.value = id; page.value = 0; load() }

async function onCreateFolder(name: string): Promise<void> {
  try {
    await itemsApi.create('mediafolder', { name, parentId: currentFolderId.value })
    await loadFolders()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
  }
}

async function onRenameFolder(name: string): Promise<void> {
  const target = renameTarget.value
  renameTarget.value = null
  if (!target) return
  try {
    await itemsApi.update('mediafolder', target.id,
      target.version !== undefined ? { name, version: target.version } : { name })
    await loadFolders()
  } catch (e) {
    toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
  }
}

function onRemoveFolder(folder: FolderRow): void {
  confirm.require({
    header: t('media.folderDelete'),
    message: t('media.folderDeleteConfirm', { name: folder.name }),
    acceptProps: { severity: 'danger' },
    accept: async () => {
      try {
        await itemsApi.remove('mediafolder', folder.id)
        if (currentFolderId.value === folder.id) currentFolderId.value = folder.parentId
        await loadFolders()
        await load()
      } catch (e) {
        if (e instanceof ApiError && e.status === 409)
          toast.add({ severity: 'warn', summary: t('media.folderNotEmpty'), life: 4000 })
        else
          toast.add({ severity: 'error', summary: e instanceof Error ? e.message : t('media.folderSaveFailed'), life: 3500 })
      }
    },
  })
}

// onMounted 改:onMounted(() => { loadFolders(); load() })
// defineExpose 追加:folders, currentFolderId, visibleFolders, breadcrumb, enterFolder,
//   goToBreadcrumb, onCreateFolder, onRenameFolder, onRemoveFolder, createOpen, renameTarget
```

template 增改:

```html
<!-- PageHeader #actions 內、Upload 鈕之前 -->
<Button v-if="canManageFolders" :label="t('media.folderNew')" icon="pi pi-folder-plus"
        severity="secondary" outlined @click="createOpen = true" />

<!-- ListToolbar 之後、error <p> 之前:麵包屑(在資料夾內或有資料夾時顯示) -->
<nav v-if="!searchActive && (breadcrumb.length || folders.length)" class="media-crumb" :aria-label="t('media.title')">
  <button type="button" class="media-crumb__link" @click="goToBreadcrumb(null)">{{ t('media.breadcrumbRoot') }}</button>
  <template v-for="c in breadcrumb" :key="c.id">
    <i class="pi pi-angle-right media-crumb__sep" aria-hidden="true" />
    <button v-if="c.id !== currentFolderId" type="button" class="media-crumb__link" @click="goToBreadcrumb(c.id)">{{ c.name }}</button>
    <span v-else class="media-crumb__current">{{ c.name }}</span>
  </template>
</nav>

<!-- MediaGrid/MediaFileList 之前 -->
<MediaFolderCards :folders="visibleFolders" :can-manage="canManageFolders || canDeleteFolders"
                  @open="enterFolder" @rename="renameTarget = $event" @remove="onRemoveFolder" />

<!-- 檔案空狀態行改為:資料夾卡片也為空才顯示 media.empty -->
<p v-if="!loading && !files.length && !visibleFolders.length" class="empty">{{ t('media.empty') }}</p>

<!-- 既有 MediaUploadDialog 加 :folder-id -->
<MediaUploadDialog v-model:visible="uploadOpen" :folder-id="searchActive ? null : currentFolderId" @done="reload" />

<!-- section 尾端 -->
<MediaFolderNameDialog v-model:visible="createOpen" :header="t('media.folderNew')" @submit="onCreateFolder" />
<MediaFolderNameDialog :visible="renameTarget !== null" :header="t('media.folderRename')"
                       :initial-name="renameTarget?.name" @update:visible="(v: boolean) => { if (!v) renameTarget = null }"
                       @submit="onRenameFolder" />
<ConfirmDialog group="media-folder" />
```

(注意 FE-R7 教訓:view 已有 MediaDetailDialog 內嵌 `<ConfirmDialog>`(無 group)——新增的 confirm.require 要帶 `group: 'media-folder'` 並用 `<ConfirmDialog group="media-folder" />`,避免雙對話框。上面 `confirm.require({...})` 需加 `group: 'media-folder'`。)

style 追加:

```css
.media-crumb { display: flex; align-items: center; gap: 4px; margin: 0 0 12px; flex-wrap: wrap; }
.media-crumb__link { border: 0; background: none; padding: 2px 4px; cursor: pointer; color: var(--accent); font: inherit; border-radius: var(--radius, 8px); }
.media-crumb__link:hover { text-decoration: underline; }
.media-crumb__sep { color: var(--muted); font-size: .75rem; }
.media-crumb__current { color: var(--fg); font-weight: 600; padding: 2px 4px; }
```

i18n(en / zh-TW 對應加入 media ns;zh-TW 譯文如右):

```ts
// en.ts                                   // zh-TW.ts
folderNew: 'New folder',                   // '新增資料夾'
folderName: 'Folder name',                 // '資料夾名稱'
folderConfirm: 'OK',                       // '確定'
folderRename: 'Rename folder',             // '重新命名資料夾'
folderDelete: 'Delete folder',             // '刪除資料夾'
folderDeleteConfirm: 'Delete folder "{name}"? Files are not deleted — a non-empty folder cannot be removed.',
                                           // '確定刪除資料夾「{name}」?檔案不會被刪除——非空資料夾無法刪除。'
folderNotEmpty: 'Folder is not empty — move its files and subfolders out first.',
                                           // '資料夾不是空的——請先移出其中的檔案與子資料夾。'
folderLoadFailed: 'Failed to load folders',// '資料夾載入失敗'
folderSaveFailed: 'Folder operation failed',// '資料夾操作失敗'
breadcrumbRoot: 'Media Library',           // '媒體庫'
folderAll: 'All files',                    // '全部檔案'
folderUncategorized: 'Uncategorized',      // '未分類'
folderField: 'Folder',                     // '資料夾'
```

(`folderAll`/`folderUncategorized`/`folderField` 供 Task 6/7 用,一次加齊避免三次動 locale 檔。)

- [ ] **Step 4: 跑測試確認通過**

Run: `cd frontend; pnpm test -- --run; pnpm build`
Expected: 全 PASS(含 locales.test.ts key 對齊)+ vue-tsc 過

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/MediaFolderCards.vue frontend/src/components/media/MediaFolderCards.test.ts frontend/src/components/media/MediaFolderNameDialog.vue frontend/src/components/media/MediaFolderNameDialog.test.ts frontend/src/views/MediaLibraryView.vue frontend/src/views/MediaLibraryView.test.ts frontend/src/components/media/MediaUploadDialog.vue frontend/src/components/media/MediaUploadDropzone.vue frontend/src/components/media/MediaUploadDropzone.test.ts frontend/src/locales/en.ts frontend/src/locales/zh-TW.ts
git commit -m "feat(media-fe): Drive-style folder navigation, folder CRUD, upload into current folder (#5)"
```

---

### Task 6: 前端 — MediaDetailDialog 資料夾欄位 + 移除完整編輯連結 (#6)

**Files:**
- Modify: `frontend/src/components/media/MediaDetailDialog.vue`
- Modify: `frontend/src/locales/en.ts`、`frontend/src/locales/zh-TW.ts`(移除 `media.openInEditor`)
- Test: `frontend/src/components/media/MediaDetailDialog.test.ts`(擴充+調整)

**Interfaces:**
- Consumes: Task 4 的 `toFolderRows`;`buildRelationTree`(`lib/buildRelationTree.ts`);後端 UpdateCoreAsync FK overlay(payload 帶 `folderId` 鍵即寫入,null = 移到未分類)。
- Produces: 詳情對話框可改檔案所屬資料夾(隨 Save 儲存);`onOpenEditor`/完整編輯按鈕/`media.openInEditor` key 不復存在。

- [ ] **Step 1: 寫失敗測試**

```ts
// MediaDetailDialog.test.ts:
// 1. 移除連結:mount 後 footer 不含「openInEditor」文案/pi-external-link 鈕;
//    元件不再 import useRouter(以快照/文本斷言按鈕不存在即可)。
// 2. 載入時 itemsApi.list('mediafolder', …) 被呼叫,TreeSelect 顯示(mock 回傳兩層資料夾)。
// 3. 初值:item.folderId='b' → treeValue 選中 b;item.folderId=null → 選中「未分類」節點。
// 4. onSave:選了資料夾 b → itemsApi.update('file', id, payload) 的 payload.folderId === 'b';
//    選「未分類」→ payload.folderId === null。
// (既有 409/翻譯測試不動,僅補 list('mediafolder') 的 mock 分流。)
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd frontend; pnpm test -- --run src/components/media/MediaDetailDialog.test.ts`
Expected: FAIL(按鈕仍在 / folderId 未入 payload)

- [ ] **Step 3: 實作**

移除:`useRouter` import 與 `const router = useRouter()`、`onOpenEditor` 函式、footer 的 openInEditor `<Button>`、en/zh-TW 的 `media.openInEditor` key。

新增(script):

```ts
import TreeSelect from 'primevue/treeselect'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import { toFolderRows, type FolderRow } from '../../lib/folderTree'

const UNFILED = '__unfiled'
const folders = ref<FolderRow[]>([])
const folderId = ref<string | null>(null)

const folderNodes = computed<TreeNode[]>(() => [
  { key: UNFILED, label: t('media.folderUncategorized'), data: UNFILED, children: [] },
  ...buildRelationTree(folders.value.map((f) => ({ id: f.id, label: f.name, parentId: f.parentId })), 'parentId'),
])
// TreeSelect single-selection binds { [key]: true } (RelationPicker 同款映射)
const folderValue = computed(() => ({ [folderId.value ?? UNFILED]: true }))
function onFolderChange(selection: Record<string, boolean>): void {
  const key = Object.keys(selection)[0]
  folderId.value = !key || key === UNFILED ? null : key
}

// load() 內(itemsApi.get 之後)追加:
//   folderId.value = typeof item.folderId === 'string' ? item.folderId : null
// 並與 schema/lang 平行載入資料夾(失敗不阻擋詳情):
//   try { folders.value = toFolderRows((await itemsApi.list('mediafolder', { page: 0, rows: 500, sort: 'name' })).data) }
//   catch { folders.value = [] }

// onSave() 內 buildItemPayload 之後、update 之前追加:
//   const payload = { ...buildItemPayload(fileMeta.value, model.value, locales.value, 'update'), folderId: folderId.value }
```

template(Alt 欄位之後):

```html
<label class="md-field">
  <span>{{ $t('media.folderField') }}</span>
  <TreeSelect :model-value="folderValue" :options="folderNodes" selection-mode="single"
              :disabled="!canWrite || loading" @update:model-value="onFolderChange" />
</label>
```

`defineExpose` 追加 `folderId, onFolderChange`。

- [ ] **Step 4: 跑測試確認通過**

Run: `cd frontend; pnpm test -- --run; pnpm build`
Expected: 全 PASS(locales key 測試確認 openInEditor 兩邊都移掉)

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/media/MediaDetailDialog.vue frontend/src/components/media/MediaDetailDialog.test.ts frontend/src/locales/en.ts frontend/src/locales/zh-TW.ts
git commit -m "feat(media-fe): folder field in detail dialog; drop System>>File full-editor link (#6)"
```

---

### Task 7: 前端 — FilePicker 資料夾過濾

**Files:**
- Modify: `frontend/src/components/fields/FilePicker.vue`
- Test: `frontend/src/components/fields/FilePicker.test.ts`(擴充)

**Interfaces:**
- Consumes: Task 4 的 `mediaFolderFilter`/`toFolderRows`;`buildRelationTree`;i18n `media.folderAll`/`media.folderUncategorized`(Task 5 已加)。
- Produces: 選檔 dialog 內資料夾 TreeSelect 過濾(預設「全部檔案」= 現行為不變)。

- [ ] **Step 1: 寫失敗測試**

```ts
// FilePicker.test.ts 追加(mock itemsApi.list 依 collection 分流):
// 1. openDialog 後預設 folderSel='__all':list('file') 的 filter 為 undefined(行為不變)。
// 2. 選資料夾 'a':list('file') filter == { folderId: { op:'_eq', value:'a' } }。
// 3. 選「未分類」:filter == { folderId: { op:'_null', value:'true' } }。
// 4. 資料夾載入失敗:仍可瀏覽檔案(TreeSelect 隱藏,files 照載)。
// 5. 搜尋與資料夾過濾疊加:search='x' 且選 'a' → list 收到 search 'x' 且 filter 含 folderId。
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd frontend; pnpm test -- --run src/components/fields/FilePicker.test.ts`
Expected: FAIL

- [ ] **Step 3: 實作**(script 追加;`loadOptions` 只改 filter 一處)

```ts
import TreeSelect from 'primevue/treeselect'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import { mediaFolderFilter } from '../../lib/mediaQuery'
import { toFolderRows, type FolderRow } from '../../lib/folderTree'

const ALL = '__all'
const UNFILED = '__unfiled'
const folders = ref<FolderRow[]>([])
const folderSel = ref<string>(ALL)

const folderNodes = computed<TreeNode[]>(() => [
  { key: ALL, label: t('media.folderAll'), data: ALL, children: [] },
  { key: UNFILED, label: t('media.folderUncategorized'), data: UNFILED, children: [] },
  ...buildRelationTree(folders.value.map((f) => ({ id: f.id, label: f.name, parentId: f.parentId })), 'parentId'),
])
const folderValue = computed(() => ({ [folderSel.value]: true }))
function onFolderChange(selection: Record<string, boolean>): void {
  folderSel.value = Object.keys(selection)[0] ?? ALL
  void loadOptions()
}
function pickerFolderFilter(): ReturnType<typeof mediaFolderFilter> | undefined {
  if (folderSel.value === ALL) return undefined
  return folderSel.value === UNFILED ? mediaFolderFilter(null) : mediaFolderFilter(folderSel.value)
}

async function loadFolders(): Promise<void> {
  try { folders.value = toFolderRows((await itemsApi.list('mediafolder', { page: 0, rows: 500, sort: 'name' })).data) }
  catch { folders.value = [] }  // 過濾器退化隱藏;選檔功能不受影響
}

// loadOptions():list('file', {...}) 增加 filter: pickerFolderFilter()
// openDialog():dialogOpen 之後 await Promise.all([loadFolders(), loadOptions()])
// defineExpose 追加:folderSel, onFolderChange, folders
```

template(搜尋框上方):

```html
<TreeSelect v-if="folders.length" class="file-picker__folder" :model-value="folderValue"
            :options="folderNodes" selection-mode="single" @update:model-value="onFolderChange" />
```

style 追加:`.file-picker__folder { display: block; margin: 8px 0 0; width: 100%; }`

- [ ] **Step 4: 跑測試確認通過**

Run: `cd frontend; pnpm test -- --run; pnpm build`
Expected: 全 PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/fields/FilePicker.vue frontend/src/components/fields/FilePicker.test.ts
git commit -m "feat(media-fe): folder filter in FilePicker dialog"
```

---

### Task 8: E2E + 文件收尾 + live 驗證

**Files:**
- Modify: `frontend/e2e/media.spec.ts`(資料夾流程)
- Modify: `docs/admin-ux-issues-backlog.md`(#5/#6/#7 狀態)

**Interfaces:**
- Consumes: 全部前置任務;live 環境事實:後端 `:5221` 既有實例(或 `dotnet run` 起,勿用 :5080)、dev admin `admin@admin.com`、限流 `SEC-7 Enabled=false`、Playwright `--workers=1`、vite `--host 127.0.0.1`、唯一 `E2E_STAMP`、`E2E_API` 指向 :5221。

- [ ] **Step 1: 擴充 e2e spec**(選擇器/登入 fixture 沿用該檔既有寫法;名稱帶 `E2E_STAMP` 免撞)

```ts
// media.spec.ts 追加一個 test.describe('media folders (Batch C)'):
// 1. 建資料夾:媒體庫 → 新增資料夾 → 命名 `C-${STAMP}` → 卡片出現。
// 2. 進資料夾上傳:點卡片 → 麵包屑「媒體庫 / C-…」→ 上傳 fixture 圖檔 photo-01.png →
//    檔案出現在資料夾內;開詳情 → Title 欄位值 === 'photo-01'(#7 驗證)。
// 3. 詳情無完整編輯連結:詳情 dialog 內不存在 pi-external-link 按鈕(#6 驗證)。
// 4. 移動檔案:詳情 → 資料夾 TreeSelect 改「未分類」→ Save → 資料夾內列表不再有該檔;
//    回根層(麵包屑)看得到。
// 5. 刪除守衛:再上傳一檔進 C-… → 對卡片按刪除 → confirm → toast「資料夾不是空的…」且卡片仍在。
// 6. 清空後刪除:把檔案移出(或刪除檔案)→ 刪資料夾 → 卡片消失。
```

- [ ] **Step 2: 全套 gate(依序,全綠才算)**

```bash
dotnet test                                    # 後端全綠
cd frontend; pnpm test -- --run; pnpm build            # vitest + vue-tsc
# live(PG + MinIO;後端 :5221;限流關閉):
$env:E2E_API='http://localhost:5221'; $env:E2E_STAMP=(Get-Date -Format 'MMddHHmmss')
pnpm exec playwright test --workers=1                  # 既有 17 + 新 folder specs 全過
```

- [ ] **Step 3: live 手動 smoke**(plugin_playwright MCP):建資料夾→上傳(Title 帶入)→移動→麵包屑→非空刪除被拒 toast→FilePicker 資料夾過濾(隨便開一個含 File 欄位的表單)→Role 矩陣看得到 mediafolder 列。**live PG 上跑一次 prod-mode migration 驗證**:`002-media-folders.sql` 在乾淨 PG 由 MigrationRunner 套用成功且 `/health/ready` 200(比照 001 rebaseline 的驗法);再以 InitTables dev 庫對照 `media_folders`/`files.folderid` schema 零差異(DB-16 parity)。

- [ ] **Step 4: 更新 backlog 狀態表**

`docs/admin-ux-issues-backlog.md`:#5 → ✅ 完成 Batch C;#6 → ✅ 完成(Batch A 死圖 + Batch B Hidden + Batch C 連結移除);#7 → ✅ 完成 Batch C;「剩餘工作規劃順序」只留 #12。

- [ ] **Step 5: Commit**

```bash
git add frontend/e2e/media.spec.ts docs/admin-ux-issues-backlog.md
git commit -m "test(e2e): media folder flows + Title autofill; docs: mark #5/#6/#7 done (Batch C)"
```

---

## 驗收門檻(整批)

1. `dotnet test` 全綠(SQLite)。
2. `pnpm test -- --run` + `pnpm build` 全綠。
3. Live PG+MinIO:migration 002 套用成功、InitTables parity 零差異、e2e 既有+新 spec 全過、手動 smoke 過。
4. 複核(Fable subagent)per-task + 整批 whole-branch review 無 FAIL。
