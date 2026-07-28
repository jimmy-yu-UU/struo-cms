# StruoCMS Directus-Parity 核心強化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 補強 StruoCMS 核心，使其具備承接 ViitorSemi Directus 專案所需的關聯深度與圖片即時轉換能力，並以真實資料驗證查詢需求。

**Architecture:** 三條獨立工作：(P0) 用完即丟的驗證 spike，用真實 Directus 子集釘死查詢需求；(P1) 把已可配置的 `MaxRelationDepth` 預設由 5 提到 6 並驗證自我關聯遞迴；(P2) 在既有 `GET /api/files/{id}/content` 端點上加 NetVips 即時圖片轉換 + 快取。P1/P2 不依賴 spike，可與 P0 並行。

**Tech Stack:** .NET 10 / C# · SqlSugarCore · PostgreSQL(runtime) + SQLite(test) · ASP.NET Core Controllers · xunit + AwesomeAssertions · **NetVips (libvips)** 影像處理。

## Global Constraints

（以下為 spec 全域要求，每個 task 隱含適用；數值逐字照抄）
- **平台**：runtime = PostgreSQL；SQLite 僅 test/dev。DB 功能**必經 live Postgres 驗證**後才算完成（backend live verify port `:5221`）。
- **TDD**：每 task 先失敗測試 → 實作 → 通過 → commit。
- **NuGet 版本**：一律 `dotnet add package` 取得最新，**禁止手寫版本號**；版本集中於 `Directory.Packages.props`（`ManagePackageVersionsCentrally=true`）。
- **設定**：走 Options + appsettings（`Section__Key` env override），安全預設；prod Docker→K8s 免重編即可調。
- **核心零污染**：不引入 `samples/*` 或商業模型依賴；`FrameworkEntityTypes` 不因本輪新增內容表。
- **授權**：StruoCMS 維持 **MIT**；NetVips=MIT、libvips=LGPL-2.1 動態連結（見 P2 最終 task 的合規處理）。
- **輸出 JSON**：camelCase；REST 回應走既有 envelope `{success,data,error}`。

---

## 範圍與分卷說明

本計畫涵蓋 **P0 + P1 + P2**：
- **P0**（spike）為投資研究，交付物是 findings 文件，非正式產品碼（用完即丟，故不套嚴格 TDD 循環）。
- **P1、P2** 為正式核心變更，完整 TDD。

**P3（Aggregate/Count）與 P4（Query DSL 擴充）不在本卷** — 它們的具體形狀由 P0 findings 決定，現在寫成 TDD 任務會是臆測性 placeholder（違反本 skill）。P0 完成後另立一份 `2026-XX-XX-struocms-parity-query.md` 計畫。詳見文末「後續卷」。

參考 spec：`docs/superpowers/specs/2026-07-28-struocms-directus-parity-core-design.md`。

---

## Phase P0 — 驗證 Spike（用完即丟）

**交付物**：`docs/directus-migration/spike-findings.md`，回答 4 個問題並釘死 P3/P4 範圍與 P1 深度值。
**隔離**：來源 `web-directus-db`（容器 `postgresql-db-1`）**僅 SELECT**；目標為獨立 scratch DB；臨時碼置於 `spike/`（不進主 solution、不 commit 到 main，結束後刪或 park）。

### Task P0.1：建立 scratch 環境與最小內容模型

**Files:**
- Create: `spike/Struo.Spike.Content/`（臨時 content 專案，含最小實體）
- Create: `spike/README.md`（記錄 scratch DB 連線、如何跑、如何清除）

**Interfaces:**
- Produces: 一組最小 `[CmsCollection]` 實體，供 spike 探針查詢使用。

- [ ] **Step 1: 建 scratch DB**（獨立於來源）

```bash
docker exec -e PGPASSWORD=qaz@1234 postgresql-db-1 \
  psql -U postgres -c "CREATE DATABASE struo_spike;"
```

- [ ] **Step 2: 建最小實體**（uuid PK，對映 Directus 高風險結構）

於 `spike/Struo.Spike.Content/` 建立：
- `Category`：`Guid Id`、`string Slug`、自我 M2O `Guid? UpperId` + nav `Category? Upper`、`List<CategoryTranslation>`。
- `Property`：`Guid Id`、`string Code`、`string Type`、多值 `ShowIn`。
- `Product`：`Guid Id`、`string PartNumber`、M2O `Guid? CategoryId`、`List<ProductTranslation>`。
- `ProductProperty`（EAV）：`long Id`、M2O `Guid ProductId`、M2O `Guid PropertyId`、`string Value`。

依 `docs/guide/03-adding-a-collection.md` 的 `[CmsCollection]`/`[CmsField]`/`[Navigate]` 慣例撰寫；`Struo:ContentAssemblies` 指向本專案。

- [ ] **Step 3: 記錄環境到 `spike/README.md`**（連線字串、清除指令 `DROP DATABASE struo_spike;`）。

### Task P0.2：uuid-preserving 唯讀 ETL

**Files:**
- Create: `spike/Struo.Spike.Etl/Program.cs`（一次性 console）

- [ ] **Step 1: 寫 ETL**：用 SqlSugar 連 `web-directus-db`（**唯讀**）讀 `categories`/`properties`/`products`/`products_properties` + translations 子集（如前 50 products 及其關聯），寫入 scratch StruoCMS 實體，**保留主要實體 uuid**；`products_properties`/translations 的 integer key 重生。
- [ ] **Step 2: 執行並核對列數**（來源 vs scratch 抽樣一致）。

### Task P0.3：探針查詢 + findings

**Files:**
- Create: `docs/directus-migration/spike-findings.md`

- [ ] **Step 1: 跑 4 組探針**（用 StruoCMS REST `POST /api/items/{c}/query` 的 `deep` 與 GraphQL），逐一記錄「可行的查詢形狀」與「缺口」：
  1. 分類麵包屑：`Upper` 遞迴 5 層可否表達？查詢次數（N+1？）。
  2. EAV 參數化：「products 同時符合多個 `(Property.Code, Value/range)`」現有 DSL 能否表達、缺什麼。
  3. 列表計數 + 巢狀 to-many 是否需 `total`/`hasMore`。
  4. 各 translation 欄位型別是否全通過 metadata 掃描（GAP-12）。
- [ ] **Step 2: 寫 findings**：每組給結論 + 對 P1(深度值)/P3(count 形狀)/P4(是否需要、需要什麼) 的明確建議。
- [ ] **Step 3: 清除 scratch**（`DROP DATABASE struo_spike;`、刪/park `spike/`）。

**Acceptance:** findings 文件回答 4 問並釘死 P3/P4；scratch 資源清除。

---

## Phase P1 — 關聯查詢深度（GAP-2）

`MaxRelationDepth` 已是可綁定 Option（`StruoQueryOptions`）。本 phase：預設 5→6，並驗證深度邊界與自我關聯遞迴由該值正確驅動。

### Task P1.1：預設深度 5→6 + 深度邊界驗證

**Files:**
- Modify: `src/Struo.Application/Configuration/StruoQueryOptions.cs:19`
- Test: `tests/Struo.Tests/Query/QueryValidatorTests.cs`（新增 test + 一個自我關聯 FakeGraph）

**Interfaces:**
- Consumes: `QueryValidator.Validate(QueryModel, CollectionMetadata, StruoQueryOptions, IRelationshipGraph, IMetadataProvider)`（既有簽章）；`opts.MaxRelationDepth` 傳入 `RelationPath.Parse(...)`。
- Produces: `StruoQueryOptions.MaxRelationDepth` 預設值 = 6。

- [ ] **Step 1: 寫失敗測試**

在 `QueryValidatorTests.cs` 新增（自我關聯 graph 讓 `category.parent` 遞迴到 `category`）：

```csharp
    private sealed class SelfRefGraph : IRelationshipGraph
    {
        public IReadOnlyList<RelationMetadata> Relations(string c) => [];
        public IReadOnlyList<(string, string)> InboundRestrict(string c) => [];
        public RelationMetadata? Resolve(string collection, string rel) => (collection, rel) switch
        {
            ("category", "parent") => new RelationMetadata { Name = "parent", Label = "Parent",
                Kind = RelationKind.ManyToOne, TargetCollection = "category",
                Interface = RelationInterface.Dropdown, ForeignKey = "parentId" },
            _ => null
        };
    }

    private sealed class CategoryMeta : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => [];
        public CollectionMetadata? GetCollection(string name) => name == "category"
            ? new CollectionMetadata { Name = "category", Label = "Category", FieldGroups = [],
                Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }] }
            : null;
    }

    private static CollectionMetadata CategoryRoot() => new()
    {
        Name = "category", Label = "Category", FieldGroups = [],
        Fields = [new FieldMetadata { Name = "name", Label = "Name", Interface = FieldInterface.Text }]
    };

    [Fact]
    public void Default_max_relation_depth_is_six()
    {
        new StruoQueryOptions().MaxRelationDepth.Should().Be(6);
    }

    [Fact]
    public void Self_relation_path_within_max_depth_is_accepted()
    {
        // 6-level breadcrumb: parent.parent.parent.parent.parent.name
        var path = string.Concat(Enumerable.Repeat("parent.", 5)) + "name";
        var q = new QueryModel(null, new ComparisonFilter(path, QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, CategoryRoot(), new StruoQueryOptions(),
            new SelfRefGraph(), new CategoryMeta());
        act.Should().NotThrow();
    }

    [Fact]
    public void Self_relation_path_exceeding_max_depth_throws()
    {
        var opts = new StruoQueryOptions { MaxRelationDepth = 6 };
        var path = string.Concat(Enumerable.Repeat("parent.", 7)) + "name"; // 7 hops > 6
        var q = new QueryModel(null, new ComparisonFilter(path, QueryOperator.Eq, "x"), [], 0, 0, null);
        var act = () => QueryValidator.Validate(q, CategoryRoot(), opts,
            new SelfRefGraph(), new CategoryMeta());
        act.Should().Throw<QueryException>();
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~QueryValidatorTests.Default_max_relation_depth_is_six|FullyQualifiedName~Self_relation_path"`
Expected: FAIL（`Default_max_relation_depth_is_six` 斷言 6 但目前為 5；深度測試視 `RelationPath.Parse` 行為）。

> 註：`Self_relation_path_exceeding_max_depth_throws` 依賴 `RelationPath.Parse` 以 `opts.MaxRelationDepth` 為上限拋出。實作前先讀 `src/Struo.Application/Query/`（`RelationPath`）確認確切拋錯型別；本測試以 `QueryException` 寬鬆斷言，不綁死訊息字串。

- [ ] **Step 3: 改預設值**

`src/Struo.Application/Configuration/StruoQueryOptions.cs:19`：

```csharp
    [Range(1, int.MaxValue)]
    public int MaxRelationDepth { get; set; } = 6;
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~QueryValidatorTests"`
Expected: PASS（全部，含既有測試不退化）。

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Configuration/StruoQueryOptions.cs tests/Struo.Tests/Query/QueryValidatorTests.cs
git commit -m "feat(query): raise default MaxRelationDepth 5->6 for recursive breadcrumbs"
```

### Task P1.2：自我關聯遞迴展開 N+1 驗證（live PG）

**Files:**
- Test: `tests/Struo.Tests/Query/DeepNestingBatchingTests.cs`（擴充；沿用該檔既有 query-counting 模式）

**Interfaces:**
- Consumes: 既有 deep-nesting 展開引擎與該測試檔的 SQL 計數輔助。

- [ ] **Step 1: 讀既有檔**：`tests/Struo.Tests/Query/DeepNestingBatchingTests.cs` 與 `DeepNestingExpansionTests.cs`，理解其如何建構巢狀查詢、如何斷言查詢次數（batched、非指數）。
- [ ] **Step 2: 寫失敗測試**：新增一個自我關聯（或既有可遞迴的）collection，做 6 層遞迴展開，斷言查詢次數與層數成線性（沿用該檔計數輔助的既有寫法；比照既有 test 建 fixture）。
- [ ] **Step 3: 跑測試**（若引擎已支援即綠；若需修正批次展開則實作至綠）。

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~DeepNestingBatchingTests"`
Expected: PASS。

- [ ] **Step 4: Live PG 驗證**：於 live Postgres 跑 6 層 `Upper` 遞迴，確認結果正確且查詢次數受控（非 SQLite）。記錄證據。
- [ ] **Step 5: Commit**

```bash
git add tests/Struo.Tests/Query/DeepNestingBatchingTests.cs
git commit -m "test(query): verify self-relation 6-level expansion stays N+1-safe"
```

**Acceptance:** 預設深度 6；6 層自我關聯遞迴在 live PG 正確且查詢次數線性；既有查詢測試不退化。

---

## Phase P2 — 圖片即時轉換端點（GAP-1）

在既有 `FilesController.Download`（`GET /api/files/{id}/content`）加轉換參數，NetVips 處理 + 磁碟快取。

### Task P2.1：加入 NetVips 套件與轉換設定

**Files:**
- Modify: `Directory.Packages.props`（由 `dotnet add package` 自動寫入版本）
- Modify: `src/Struo.Infrastructure/Struo.Infrastructure.csproj`（加 `PackageReference`）
- Modify: `src/Struo.Application/Files/FileStorageOptions.cs`（新增 `ImageTransform` 巢狀 options）
- Test: `tests/Struo.Tests/Files/ImageTransformOptionsTests.cs`（新建）

**Interfaces:**
- Produces: `FileStorageOptions.ImageTransform`（`ImageTransformOptions`）：`bool Enabled`、`int MaxWidth`、`int MaxHeight`、`string[] AllowedFormats`、`int DefaultQuality`。

- [ ] **Step 1: 安裝 NetVips**（版本由套件管理器決定，勿手寫）

```bash
cd src/Struo.Infrastructure
dotnet add package NetVips
dotnet add package NetVips.Native
```

驗證 `Directory.Packages.props` 出現 `NetVips` / `NetVips.Native` 的 `PackageVersion`。

- [ ] **Step 2: 寫失敗測試**（`ImageTransformOptionsTests.cs`）

```csharp
using AwesomeAssertions;
using Struo.Application.Files;
using Xunit;

namespace Struo.Tests.Files;

public class ImageTransformOptionsTests
{
    [Fact]
    public void Defaults_are_safe()
    {
        var o = new FileStorageOptions().ImageTransform;
        o.Enabled.Should().BeTrue();
        o.MaxWidth.Should().Be(4096);
        o.MaxHeight.Should().Be(4096);
        o.DefaultQuality.Should().Be(82);
        o.AllowedFormats.Should().Contain(["webp", "jpeg", "png", "avif"]);
    }
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ImageTransformOptionsTests"`
Expected: FAIL（`ImageTransform` 尚不存在）。

- [ ] **Step 4: 加 options**（`FileStorageOptions.cs`，比照既有 `LocalOptions`/`S3Options` 巢狀風格）

```csharp
    public ImageTransformOptions ImageTransform { get; set; } = new();

    public sealed class ImageTransformOptions
    {
        public bool Enabled { get; set; } = true;
        public int MaxWidth { get; set; } = 4096;
        public int MaxHeight { get; set; } = 4096;
        public string[] AllowedFormats { get; set; } = ["webp", "jpeg", "png", "avif"];
        public int DefaultQuality { get; set; } = 82;
    }
```

- [ ] **Step 5: 跑測試確認通過** → Expected: PASS。
- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src/Struo.Infrastructure/Struo.Infrastructure.csproj \
        src/Struo.Application/Files/FileStorageOptions.cs tests/Struo.Tests/Files/ImageTransformOptionsTests.cs
git commit -m "feat(files): add NetVips dependency and image-transform options"
```

### Task P2.2：`IImageTransformer` + NetVips 實作

**Files:**
- Create: `src/Struo.Application/Files/IImageTransformer.cs`
- Create: `src/Struo.Infrastructure/Files/NetVipsImageTransformer.cs`
- Test: `tests/Struo.Tests/Files/NetVipsImageTransformerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public readonly record struct ImageTransformRequest(
      int? Width, int? Height, string? Format, string Fit, int Quality);
  public readonly record struct ImageTransformResult(byte[] Bytes, string ContentType, int Width, int Height);
  public interface IImageTransformer
  {
      // 輸入為「已驗證的儲存檔」bytes；輸出轉換後 bytes。format 為 allowlist 內值。
      ImageTransformResult Transform(ReadOnlySpan<byte> source, ImageTransformRequest req);
  }
  ```

- [ ] **Step 1: 寫失敗測試**（用 NetVips 產生輸入圖，避免嵌入二進位）

```csharp
using AwesomeAssertions;
using NetVips;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class NetVipsImageTransformerTests
{
    private static byte[] Png(int w, int h) =>
        Image.Black(w, h).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");

    [Fact]
    public void Resizes_to_requested_width_preserving_aspect()
    {
        var src = Png(200, 100);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 100, Height: null, Format: "png", Fit: "inside", Quality: 82));
        r.Width.Should().Be(100);
        r.Height.Should().Be(50);
        r.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void Converts_format_to_webp()
    {
        var src = Png(50, 50);
        var r = new NetVipsImageTransformer().Transform(src,
            new ImageTransformRequest(Width: 40, Height: null, Format: "webp", Fit: "inside", Quality: 70));
        r.ContentType.Should().Be("image/webp");
    }
}
```

- [ ] **Step 2: 跑測試確認失敗** → Expected: FAIL（型別不存在）。

> 執行環境需 libvips native asset（`NetVips.Native` 提供）。CI/容器須能載入；若測試主機缺 native，先於本機/含 native 的環境跑。

- [ ] **Step 3: 實作**（`IImageTransformer.cs` 放介面與 record；`NetVipsImageTransformer.cs` 實作）

```csharp
// NetVipsImageTransformer.cs
using NetVips;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

public sealed class NetVipsImageTransformer : IImageTransformer
{
    public ImageTransformResult Transform(ReadOnlySpan<byte> source, ImageTransformRequest req)
    {
        using var img = Image.NewFromBuffer(source.ToArray());
        var fit = req.Fit switch
        {
            "cover" => Enums.Size.Both,
            "contain" or "inside" => Enums.Size.Down,
            _ => Enums.Size.Down
        };
        using var outImg = (req.Width, req.Height) switch
        {
            (int w, null) => img.ThumbnailImage(w, size: fit),
            (int w, int h) => img.ThumbnailImage(w, height: h, size: fit),
            (null, int h) => img.ThumbnailImage(img.Width, height: h, size: fit),
            _ => img.Clone()
        };
        var (suffix, contentType) = (req.Format ?? "").ToLowerInvariant() switch
        {
            "webp" => (".webp", "image/webp"),
            "jpeg" or "jpg" => (".jpg", "image/jpeg"),
            "png" => (".png", "image/png"),
            "avif" => (".avif", "image/avif"),
            _ => (".webp", "image/webp")
        };
        var bytes = outImg.WriteToBuffer(suffix, new VOption { { "Q", req.Quality } });
        return new ImageTransformResult(bytes, contentType, outImg.Width, outImg.Height);
    }
}
```

> 實作前先確認 NetVips API 名稱（`ThumbnailImage` 多載、`WriteToBuffer` 的 `VOption` 質參數 `Q`）與已安裝版本一致；如簽章不同，依實際 API 調整但保持行為（resize + format + quality）。

- [ ] **Step 4: 跑測試確認通過** → Expected: PASS。
- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Files/IImageTransformer.cs \
        src/Struo.Infrastructure/Files/NetVipsImageTransformer.cs \
        tests/Struo.Tests/Files/NetVipsImageTransformerTests.cs
git commit -m "feat(files): NetVips image transformer (resize/format/quality)"
```

### Task P2.3：轉換結果快取 `IImageVariantCache`

**Files:**
- Create: `src/Struo.Application/Files/IImageVariantCache.cs`
- Create: `src/Struo.Infrastructure/Files/DiskImageVariantCache.cs`
- Test: `tests/Struo.Tests/Files/DiskImageVariantCacheTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IImageVariantCache
  {
      // key 由 (fileId, 正規化參數, 檔案版本) 衍生；命中回 bytes，未命中回 null。
      Task<byte[]?> TryGetAsync(string key, CancellationToken ct);
      Task SetAsync(string key, byte[] bytes, CancellationToken ct);
      string DeriveKey(Guid fileId, string fileVersion, ImageTransformRequest req);
  }
  ```

- [ ] **Step 1: 寫失敗測試**

```csharp
using AwesomeAssertions;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class DiskImageVariantCacheTests
{
    private static DiskImageVariantCache New() =>
        new(Path.Combine(Path.GetTempPath(), "struo-variant-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Set_then_get_roundtrips()
    {
        var c = New();
        var key = c.DeriveKey(Guid.NewGuid(), "v1",
            new ImageTransformRequest(100, null, "webp", "inside", 82));
        (await c.TryGetAsync(key, default)).Should().BeNull();
        await c.SetAsync(key, [1, 2, 3], default);
        (await c.TryGetAsync(key, default)).Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void Key_changes_with_params_and_version()
    {
        var c = New();
        var id = Guid.NewGuid();
        var a = c.DeriveKey(id, "v1", new ImageTransformRequest(100, null, "webp", "inside", 82));
        var b = c.DeriveKey(id, "v2", new ImageTransformRequest(100, null, "webp", "inside", 82));
        var d = c.DeriveKey(id, "v1", new ImageTransformRequest(200, null, "webp", "inside", 82));
        a.Should().NotBe(b);
        a.Should().NotBe(d);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗** → Expected: FAIL。
- [ ] **Step 3: 實作**（`DiskImageVariantCache`：key = SHA256(fileId|version|params) 之 hex；檔案存 `rootPath/<key前2字>/<key>`；`SetAsync` 原子寫入；防路徑穿越——key 只含 hex 字元）。
- [ ] **Step 4: 跑測試確認通過** → Expected: PASS。
- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Files/IImageVariantCache.cs \
        src/Struo.Infrastructure/Files/DiskImageVariantCache.cs \
        tests/Struo.Tests/Files/DiskImageVariantCacheTests.cs
git commit -m "feat(files): disk-backed image variant cache with versioned keys"
```

### Task P2.4：接進 `FilesController.Download` + DI 註冊

**Files:**
- Modify: `src/Struo.Api/Controllers/FilesController.cs:73-94`（`Download` action）
- Modify: DI 註冊處（`src/Struo.Api/Program.cs` 或既有 Files 註冊擴充 — 實作前 grep `AddSingleton<IFileStorage`/`IImageDimensionReader` 找到註冊點，於同處註冊 `IImageTransformer`/`IImageVariantCache`）
- Test: `tests/Struo.Tests/Files/ImageTransformEndpointTests.cs`

**Interfaces:**
- Consumes: `IImageTransformer`、`IImageVariantCache`（上兩 task）、既有 `FileService.GetAsync`、`IFileStorage.OpenReadAsync`、`IFileAccessPolicy`。
- 端點契約：`GET /api/files/{id}/content?width=&height=&format=&fit=&quality=`。無參數或非圖片 content-type → 維持既有直通行為（不變）。

- [ ] **Step 1: 寫失敗整合測試**（沿用 `FileDownloadTests` 的 `[Collection("ApiIntegration")]` + `ApiFactory` 模式）

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using NetVips;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Files;

[Collection("ApiIntegration")]
public class ImageTransformEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> UploadPng(System.Net.Http.HttpClient c, int w, int h)
    {
        var bytes = Image.Black(w, h).Cast(Enums.BandFormat.Uchar).WriteToBuffer(".png");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var mp = new MultipartFormDataContent { { content, "file", "img.png" } };
        var resp = await c.PostAsync("/api/files", mp);
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Width_param_returns_resized_image()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 200, 100);           // published by default
        var resp = await c.GetAsync($"/api/files/{id}/content?width=100&format=png");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var outBytes = await resp.Content.ReadAsByteArrayAsync();
        using var img = Image.NewFromBuffer(outBytes);
        img.Width.Should().Be(100);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task Width_over_max_is_clamped()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 200, 100);
        var resp = await c.GetAsync($"/api/files/{id}/content?width=999999&format=png");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var img = Image.NewFromBuffer(await resp.Content.ReadAsByteArrayAsync());
        img.Width.Should().BeLessThanOrEqualTo(4096);
    }

    [Fact]
    public async Task Nonimage_ignores_transform_params()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var txt = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("payload"));
        txt.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var mp = new MultipartFormDataContent { { txt, "file", "f.txt" } };
        var up = await c.PostAsync("/api/files", mp);
        var id = Root(await up.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString();
        var resp = await c.GetAsync($"/api/files/{id}/content?width=50");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Be("payload");   // 原檔直通
    }

    [Fact]
    public async Task Transform_preserves_published_anonymous_access()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await UploadPng(c, 60, 60);
        var anon = _factory.CreateClient();
        (await anon.GetAsync($"/api/files/{id}/content?width=30&format=webp")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test tests/Struo.Tests --filter "FullyQualifiedName~ImageTransformEndpointTests"`
Expected: FAIL（尚未實作轉換分支，`width` 被忽略、回原 200×100）。

- [ ] **Step 3: 註冊 DI**：於既有 Files 服務註冊處加

```csharp
services.AddSingleton<IImageTransformer, NetVipsImageTransformer>();
services.AddSingleton<IImageVariantCache>(_ =>
    new DiskImageVariantCache(imageVariantCacheRootFromOptions));
```

- [ ] **Step 4: 改 `Download` action**（在既有 published/授權檢查**之後**、串流之前插入轉換分支）

```csharp
    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Download(
        Guid id,
        [FromQuery] int? width, [FromQuery] int? height,
        [FromQuery] string? format, [FromQuery] string? fit, [FromQuery] int? quality,
        CancellationToken ct = default)
    {
        var row = await files.GetAsync(id, ct);
        if (row is null) return NotFound();
        if (row.Status != "published" && !await access.CanReadUnpublishedAsync(HttpContext, ct)) return NotFound();

        var it = options.ImageTransform;
        var wantsTransform = it.Enabled
            && (width is not null || height is not null || format is not null)
            && row.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        if (wantsTransform)
        {
            var fmt = (format ?? "").ToLowerInvariant();
            if (format is not null && !it.AllowedFormats.Contains(fmt))
                return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                    $"Unsupported format '{format}'.");

            var req = new ImageTransformRequest(
                Width: width is null ? null : Math.Clamp(width.Value, 1, it.MaxWidth),
                Height: height is null ? null : Math.Clamp(height.Value, 1, it.MaxHeight),
                Format: format is null ? null : fmt,
                Fit: string.IsNullOrEmpty(fit) ? "inside" : fit,
                Quality: Math.Clamp(quality ?? it.DefaultQuality, 1, 100));

            var version = (row.UpdatedAt ?? row.CreatedAt).Ticks.ToString();
            var key = variantCache.DeriveKey(id, version, req);
            var cached = await variantCache.TryGetAsync(key, ct);
            if (cached is not null)
                return File(cached, ContentTypeFor(req.Format));

            byte[] sourceBytes;
            await using (var src = await storage.OpenReadAsync(row.StorageKey, ct))
            using (var ms = new MemoryStream()) { await src.CopyToAsync(ms, ct); sourceBytes = ms.ToArray(); }

            ImageTransformResult result;
            try { result = transformer.Transform(sourceBytes, req); }
            catch { var s = await storage.OpenReadAsync(row.StorageKey, ct);
                    return File(s, row.ContentType, fileDownloadName: row.FileName); }  // 安全回退

            await variantCache.SetAsync(key, result.Bytes, ct);
            return File(result.Bytes, result.ContentType);
        }

        if (options.PresignedRedirect)
        {
            var presigned = await storage.GetPresignedUrlAsync(
                row.StorageKey, TimeSpan.FromSeconds(options.S3.PresignTtlSeconds), ct);
            if (presigned is not null) return Redirect(presigned);
        }
        var stream = await storage.OpenReadAsync(row.StorageKey, ct);
        return File(stream, row.ContentType, fileDownloadName: row.FileName);
    }
```

（constructor 加入 `IImageTransformer transformer, IImageVariantCache variantCache`；`ContentTypeFor` 為小工具或直接內聯 `req.Format` → MIME 映射。實作前確認 `row` 是否有 `UpdatedAt`/`CreatedAt`/`StorageKey`——若欄位名不同，grep `FileService.GetAsync` 回傳型別調整 `version`。）

- [ ] **Step 5: 跑測試確認通過** → Expected: PASS（4 tests）。
- [ ] **Step 6: 退化檢查**：`dotnet test tests/Struo.Tests --filter "FullyQualifiedName~FileDownloadTests"` → 既有行為不變。
- [ ] **Step 7: Live 驗證**：對真實 MinIO 圖片打 `?width=&format=webp`，確認尺寸/格式/快取命中（第二次更快 / 有 variant 檔）。
- [ ] **Step 8: Commit**

```bash
git add src/Struo.Api/Controllers/FilesController.cs src/Struo.Api/Program.cs \
        tests/Struo.Tests/Files/ImageTransformEndpointTests.cs
git commit -m "feat(files): on-the-fly image transform on content endpoint with cache"
```

### Task P2.5：appsettings 預設、docs 與 LGPL 合規

**Files:**
- Modify: `src/Struo.Api/appsettings.json`（`Struo:Files:ImageTransform` 預設；`Cache` 路徑）
- Create: `THIRD-PARTY-NOTICES.md`（repo 根）
- Modify: `docs/guide/`（新增圖片轉換端點用法一節）

- [ ] **Step 1: appsettings**：加

```json
"Struo": {
  "Files": {
    "ImageTransform": {
      "Enabled": true, "MaxWidth": 4096, "MaxHeight": 4096,
      "AllowedFormats": ["webp","jpeg","png","avif"], "DefaultQuality": 82,
      "CachePath": "App_Data/image-cache"
    }
  }
}
```

- [ ] **Step 2: `THIRD-PARTY-NOTICES.md`**：列 libvips 著作權 + LGPL-2.1 全文連結 + 上游源碼與版本；註明「經 NetVips 動態連結；StruoCMS 授權為 MIT，不受影響」。
- [ ] **Step 3: docs**：於 `docs/guide/` 記錄 `GET /api/files/{id}/content?width=&height=&format=&fit=&quality=` 用法、參數上限、快取與部署（native libvips asset 需求）。
- [ ] **Step 4: Commit**

```bash
git add src/Struo.Api/appsettings.json THIRD-PARTY-NOTICES.md docs/guide/
git commit -m "docs(files): image-transform config, usage, and LGPL/libvips notice"
```

**Acceptance:** 前端不改 client 即可經參數取得轉換圖 + 快取；dev/prod 皆可、不依賴外部 CDN；授權 parity（published 匿名、draft 404）；`THIRD-PARTY-NOTICES` 建立、StruoCMS 維持 MIT；libvips 安全註記（僅解碼已驗證之儲存檔）納入 docs。

---

## 後續卷（P3 / P4，本計畫不含）

P0 findings 完成後，另立 `docs/superpowers/plans/2026-XX-XX-struocms-parity-query.md`：
- **P3 Aggregate/Count（GAP-4）**：巢狀 to-many `total`/`hasMore`（REST + GraphQL）、必要時 count-only。
- **P4 查詢 DSL 擴充（GAP-5，條件性）**：僅實作 P0 證明必要者（巢狀布林群組 / EAV 多條件跨 to-many）；若現有 DSL 已足則記錄後跳過。

理由：兩者形狀由 spike 決定，先寫會是臆測性 placeholder。

---

## Self-Review

- **Spec coverage**：P0(GAP-2/3/4/5/12 驗證)✓、P1(GAP-2)✓、P2(GAP-1 + LGPL §7)✓；GAP-4/5 → 明確列為後續卷（spec §1 允許 P4 條件性、P3 spike 驅動）。GAP-6 已決策排除、GAP-7/8 延後——與 spec 非範圍一致。
- **Placeholder scan**：無 TBD/TODO。P3/P4 未寫成任務是**刻意**分卷（避免臆測），非 placeholder。三處「實作前先讀/grep 確認」是對未讀內部（`RelationPath`、DI 註冊點、`FileService.GetAsync` 回傳欄位、NetVips 確切 API）的誠實核對指示，非空泛占位。
- **Type consistency**：`ImageTransformRequest`/`ImageTransformResult`/`IImageTransformer`/`IImageVariantCache`/`FileStorageOptions.ImageTransform` 在 P2.1–P2.4 一致；`DeriveKey(Guid,string,ImageTransformRequest)` 於 P2.3 定義、P2.4 使用一致；`MaxRelationDepth=6` 於 P1.1 一致。
- **已知風險**：P1.2 與 P2.2/P2.4 部分測試依賴未讀內部（deep-nesting 引擎、NetVips 版本 API、`row` 欄位名）——每處已標「實作前確認」並以寬鬆斷言/回退降風險。

---

## Execution Handoff

計畫已存至 `docs/superpowers/plans/2026-07-28-struocms-directus-parity-core.md`。
- **P1、P2 可立即並行執行**；**P0 可與其並行**（獨立 scratch 環境）。
- 依你的偏好，執行階段用 **Sonnet subagent**（dev），審查用 Opus。
