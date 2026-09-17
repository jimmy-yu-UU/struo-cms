# 23. 範例專案導覽

這一章介紹 `samples/Struo.Sample.Blog`——它把[第 5 章：定義集合](05-collections.md)到
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)教的每個機制各示範成一個看得到、跑得
起來的範例，怎麼開啟它、逐檔該看什麼、以及怎麼把它整個拿掉，都在下面。

## 這個範例展示什麼

`samples/Struo.Sample.Blog/` 是隨模板一起放在儲存庫裡的一個小型部落格範例，把一個完整集合、
一張翻譯附屬表、一個帶 payload 的多對多 junction、一棵自我參照的樹狀集合，以及一個 Repeater
子型別，各示範一次：

- `Article` ——完整集合，涵蓋多種欄位介面、兩個關聯、版本紀錄與軟刪除、兩個欄位群組。
- `ArticleTranslation` ——`Article` 的翻譯附屬表。
- `Tag` 與 `ArticleTag` ——多對多關聯，junction 本身帶 payload。
- `Category` ——自我參照的樹狀集合。
- `FaqItem` ——純 POCO，做 `Repeater` 的子型別。

`Struo.Sample.Blog.csproj` 是一個普通類別庫，參照 `Struo.Domain`（拿到 `[Cms*]` attribute）與
`Struo.Infrastructure`（SqlSugar 透過遞移相依帶進來）——這是第 5 章集合檢查清單允許的其中一種
寫法。

核心不會參照它：`src/Struo.Api.csproj` 沒有指到它的 `ProjectReference`，
`Struo:ContentAssemblies` 預設是空陣列。真正參照它的只有測試專案 `tests/Struo.Tests`，而且不
只一處——後面的移除清單會列出實際涉及的檔案。

## 開啟範例

打開範例需要兩處編輯，缺一不可。第一處，在 `src/Struo.Api/Struo.Api.csproj` 加一筆專案參照：

```xml
<ProjectReference Include="..\..\samples\Struo.Sample.Blog\Struo.Sample.Blog.csproj" />
```

第二處，在 `src/Struo.Api/appsettings.Development.json.example`（非 Development 環境可以改設
`appsettings.json` 或用 `Struo__ContentAssemblies__0`）取消這段註解：

```json
  // Uncomment to enable the Blog sample (see docs/guide/en/16-sample-walkthrough.md).
  // The Struo.Sample.Blog project reference must be added to Struo.Api.csproj as well.
  "Struo": {
    // "ContentAssemblies": [ "Struo.Sample.Blog" ],
```

`Struo:ContentAssemblies` 在 host build 之前就讀過一次，所以只改設定檔不會生效，一定要重新啟
動行程，改完的設定才算數。

兩處都做完、重啟之後，CodeFirst 會像建立任何其他集合的資料表一樣，建出 `articles`、
`article_translations`、`tags`、`article_tags`、`categories` 五張表。`GET /api/schema` 這時
會列出十一個集合——範例加的四個（`article`、`articleTag`、`category`、`tag`）緊接在七個框架集
合旁邊；`articleTag` 雖然在後台側欄裡被 `Hidden` 藏起來，`/api/schema` 仍然會列出它。

開著範例的這段期間，`dotnet test` 有一項測試會維持紅燈：
`TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` 直接讀
`Struo.Api.csproj`，斷言裡面不該出現指到 `samples` 的 `ProjectReference`——這正是第一步剛加
上去的那一行。要恢復綠燈，就是下面移除清單的第一步。

## 逐檔導覽

### `Article.cs`

`Article`（`AuditableEntity, ISoftDeletable`）是範例的主角：

```csharp
[SugarTable("articles")]
[SugarIndex("ix_articles_categoryid", nameof(CategoryId), OrderByType.Asc)]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status), Revisions = true)]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : AuditableEntity, ISoftDeletable
```

它實作 `ISoftDeletable` 並把 `Revisions` 設成 `true`，所以同時擁有垃圾桶與版本紀錄，見
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)。

自己的欄位涵蓋了大半個介面總覽：`Select`（`Status`）、`DateTime`（`PublishedAt`）、`Image`
（`HeroImageId`）、`MultiSelect`（`Regions`）、`CheckboxGroup`（`Audiences`）、`Tags`
（`Keywords`）、`Json`（`Attributes`）、`KeyValue`（`Meta`）、`Files`（`Gallery`）、
`Repeater`（`Faqs`，子型別是下面的 `FaqItem`），還有一個 `Text` 搭 `Hidden` 的
`InternalNote`。逐一介面的行為見[第 6 章：欄位型別與編輯介面](06-field-types.md)。`Status`
是一個 `draft`／`published` 的 `Select`，程式碼預設 `draft`，也是集合的 `DefaultDisplayField`，
但它沒有宣告 `Required`——範例裡真正必填的欄位是 `ArticleTranslation.Title`、`Tag.Name`、
`Category.Name`、`FaqItem.Question`，以及 `ArticleTag` 的兩個外鍵。

兩個關聯：`Category`（多對一 `Dropdown`，`OnDelete = SetNull`）與 `Tags`（透過 `ArticleTag`
junction 的多對多 `TagSelect`，用 `[CmsRelation(SortField = nameof(ArticleTag.Sort))]` 排
序，表單上能就地編輯每個標籤的 `Note` 跟順序）；兩種關聯介面的運作方式見
[第 8 章：關聯](08-relations.md)。標題與內文不在 `Article` 上——它們放在下面的翻譯附屬表。

### `ArticleTranslation.cs`

```csharp
[SugarTable("article_translations")]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
```

`ArticleTranslation` 是 `Article` 的翻譯附屬表，由 `Article` 上的
`[CmsTranslations(typeof(ArticleTranslation))]` 指過來；`ArticleId`／`Locale` 這一對欄位的
複合唯一索引不是靠屬性宣告，而是由翻譯附屬表的 metadata 自動推出來的。它必填、可搜尋的
`Title` 掛在 `Content` 群組，`Body` 是 `RichText`，另外有一個 `Hidden` 的 `InternalSlug`。翻
譯附屬表的運作方式見[第 7 章：多語內容](07-i18n.md)。

繼承 `SeoTranslation` 讓它免費拿到 `SeoTitle`／`SeoMetaDescription`／`SeoOgImageId`，這三個欄
位掛在 `Article` 宣告的 `SEO` 群組底下。

### `Tag.cs` 與 `ArticleTag.cs`

`Tag`（`[SugarTable("tags")]`）是範例裡最簡單的集合：一個必填、可搜尋的 `Name`。
`Article.Tags` 透過 `[Navigate(typeof(ArticleTag), ...)]` 走的 join 表 `ArticleTag`
（`[SugarTable("article_tags")]`）自己也掛了 `[CmsCollection(..., Hidden = true)]`，所以它
是一個可讀寫的 junction collection，帶著兩個外鍵之外的 payload：一個 `Note` 文字欄位，和兼做
`Article.Tags` 排序欄的 `Sort`。junction collection 這個機制的完整說明在
[第 8 章：關聯](08-relations.md)，這裡不重複。

`ArticleTag` 的兩個外鍵（`ArticleId`／`TagId`）宣告了 `Required = true`，直接對 junction
collection（`articleTag`）寫入時這條規則真的會擋：拒絕 `null`、空字串，也拒絕
`Guid.Empty`。`Required` 管不到的是透過 `Article.Tags` 這個關聯陣列寫入 junction payload 的
路徑——第 8 章說過，那條路徑從不檢查 `Required`。兩個外鍵必須宣告成可寫入的 `[CmsField]`，這
是另一件事：`MetadataScanner.ValidateJunctionCollections` 在啟動時就會檢查，檔案自己的註解
說明了原因。

如果是對著範例出現之前就存在的資料庫開啟，`note`／`sort` 這兩欄要靠 Development 的
`Database:AutoSyncSchema=true`，或是 fork 自己寫的 migration 才會出現在既有的表——
`db/migrations` 只收核心的腳本，從來不含範例集合的結構。

`ArticleTag` 沒有唯一索引擋 `(ArticleId, TagId)` 這一對重複，直接對 `articleTag` 寫入可以造
出重複的配對；下一次儲存擁有它的 `Article` 會把它修好（留下 PK 最小的一筆，其餘刪除並記一筆
警告），見[第 8 章：關聯](08-relations.md)的 `ManyToManySync`。

### `Category.cs`

```csharp
[SugarTable("categories")]
[SugarIndex("ix_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : AuditableEntity, ISoftDeletable
```

`Category`（`AuditableEntity, ISoftDeletable`）自我參照成一棵樹：`ParentId` 搭一個
`TreeSelect` 的 `Parent` 關聯（`OnDelete = SetNull`），和反過來走同一個外鍵的 `Children`
（`RelatedList`）；另外還有一個 `Articles`，是反過來走 `Article.CategoryId` 的
`RelatedList`。自己的欄位只有必填、可搜尋的 `Name`。三種關聯介面的行為見
[第 8 章：關聯](08-relations.md)。

### `FaqItem.cs`

沒有 `[SugarTable]`，也沒有 `[CmsCollection]`——`FaqItem` 是純 POCO，它的 `[CmsField]` 只在
`Article.Faqs` 這個 `Repeater` 欄位裡當子型別的形狀用：一個必填的 `Text`（`Question`）、一個
`Textarea`（`Answer`），和一個有兩個選項的 `Select`（`Category`）。

```csharp
public sealed class FaqItem
{
    [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
    public string Question { get; set; } = "";

    [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
    public string Answer { get; set; } = "";

    [CmsField(Label = "Category", Interface = FieldInterface.Select)]
    [CmsOptions("general:General", "billing:Billing")]
    public string? Category { get; set; }
}
```

## 執行範例的端到端測試

`frontend/e2e/sample/` 底下有八個規格檔（`collections`、`items`、`relations`、`revisions`、
`trash`、`conflict`、`unsaved-guard`、`not-found`），`frontend/playwright.config.ts` 把它們
接成 `sample` 這個 Playwright 專案，執行指令是 `pnpm e2e:sample`。

跑這組測試，除了 `pnpm e2e` 本身就要的東西（執行中的 API、可連線的資料庫、已種好的
bootstrap 管理員，見[第 22 章：測試與 CI](22-testing.md)）之外，還要把範例加進
`Struo:ContentAssemblies`；完整的先決條件與逐規格的種子資料備註在 `frontend/e2e/README.md`。

`frontend/e2e/README.md` 額外提到 `relations.spec.ts` 需要先有一筆 `category`、一筆 `tag`
資料——範例本身不附種子資料。

## 完全移除範例

只刪掉 `samples/Struo.Sample.Blog/` 目錄還不夠——後端測試套件把範例的集合當固定物在用，範圍
遠超過這個目錄本身，清乾淨要照下面十步做：

1. 還原兩處開啟編輯：從 `Struo.Api.csproj` 移除指到 `Struo.Sample.Blog.csproj` 的
   `ProjectReference`；把 `appsettings.Development.json.example` 裡的
   `Struo:ContentAssemblies` 註解跟著移除，不然這份範例設定檔會一直指向一個已經不存在的
   範例。

2. 刪掉整個 `samples/Struo.Sample.Blog/` 目錄；從 `StruoCMS.slnx` 移除
   `<Folder Name="/samples/">` 那個區塊；從 `tests/Struo.Tests/Struo.Tests.csproj` 移除指
   到它的 `ProjectReference`。

3. 刪掉 `tests/Struo.Tests/Support/ContentAssemblyEnvBootstrap.cs`——這是在任何測試跑之
   前，就把 `Struo__ContentAssemblies__0=Struo.Sample.Blog` 設進行程環境變數的
   `[ModuleInitializer]`；少了它，`ApiFactory` 起的測試主機看不到範例的集合。

4. 用下面這道指令找出直接 `using` 範例型別的測試檔，逐一刪除：

   ```
   grep -rl "using Struo.Sample.Blog;" tests/Struo.Tests
   ```

   今天會找到 36 個檔案，分布在 `Query`、`Metadata`、`Persistence`、`Revisions`、`Search`、
   `Changes`、`Health` 七個子目錄下。

5. 這一步做完，`dotnet build` 會過——`src/Struo.*` 底下從來沒有程式碼參照過範例——但
   `dotnet test` 還不會，原因是接下來兩步。

6. 修 `tests/Struo.Tests/Metadata/ConventionMetadataDiscoveryTests.cs`：四個測試裡有兩個
   用字串指名 `"Struo.Sample.Blog"` 並斷言它能載入成功，把
   `Configured_content_assembly_is_scanned` 與 `Registers_entity_type_collector` 刪掉，或
   改成指向自己 fork 的組件；另外兩個測試不涉及範例，留著。

7. 刪掉每一個沒有 `using` 範例命名空間、卻直接經 REST 或 GraphQL 操作
   `article`／`category`／`tag` 的測試檔——它們全靠第 3 步那個 module initializer 撐著，編
   譯期抓不到，要跑一次 `dotnet test`，看哪些測試回 `404`／`KeyNotFoundException` 再逐一處
   理，散布在 `Api/`、`Query/`、`GraphQl/`、`Localization/`、`Identity/` 底下。

8. 順手清掉四處提到範例的過期註解：`tests/Struo.Tests/Support/ApiFactory.cs` 與
   `tests/Struo.Tests/Api/CorsAndCookieTests.cs` 各引用了已經不存在的
   `ContentAssemblyEnvBootstrap.cs`；`tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs` 的
   註解提到範例的路徑，`GraphQlExecutionTests.cs` 的註解提到
   `Struo.Sample.Blog.FaqItem` 這個型別——都不影響編譯，留著只是過期。

9. 前端這邊：刪掉 `frontend/e2e/sample/`（8 個檔案）；從 `frontend/playwright.config.ts`
   移除 `"sample"` 專案，以及 `core` 專案不再需要的
   `testIgnore: '**/e2e/sample/**'`；從 `frontend/package.json` 移除 `"e2e:sample"`、
   `"e2e:all"`，只留 `"e2e"`；修剪 `frontend/e2e/README.md` 裡談範例的段落。

10. 清掉任何跑過範例的資料庫裡留下的五張表——CodeFirst 建的表不會自動消失，而且從來不是
    `db/migrations` 底下任何一支腳本管的：

    ```
    DROP TABLE IF EXISTS article_tags, article_translations, articles, categories, tags CASCADE;
    ```

移除完，依序跑這三道指令，確認乾淨：

```
dotnet build
dotnet test
cd frontend && pnpm test && pnpm build && pnpm e2e
```

## 接下來

範例導覽到這裡。想動手做一個你自己的集合，回頭看
[第 5 章：定義集合](05-collections.md)；才剛開始摸這個專案的話，從
[第 3 章：快速開始](03-getting-started.md)開始。
