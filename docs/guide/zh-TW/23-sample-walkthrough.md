# 23. 範例專案導覽

這一章介紹 `samples/Struo.Sample.Blog`——它把[第 5 章：定義集合](05-collections.md)到
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)教的每個機制各示範成一個看得到、跑得
起來的範例，怎麼開啟它、逐檔該看什麼、怎麼把它整個拿掉，都在下面。

## 這個範例展示什麼

`samples/Struo.Sample.Blog/` 是隨模板一起放在儲存庫裡的一個小型部落格範例，把一個完整集合、
一張翻譯 sidecar、一個帶 payload 的多對多 junction、一棵自我參照的樹狀集合，以及一個
`Repeater` 子型別，各示範一次：

- `Article`：完整集合，涵蓋多種欄位介面、兩個關聯、版本紀錄與軟刪除、兩個欄位群組。
- `ArticleTranslation`：`Article` 的翻譯 sidecar。
- `Tag` 與 `ArticleTag`：多對多關聯，junction 本身帶 payload。
- `Category`：自我參照的樹狀集合。
- `FaqItem`：純 POCO，做 `Repeater` 的子型別。

`Struo.Sample.Blog.csproj` 是一個普通類別庫，參照 `Struo.Domain`（拿到 `[CmsCollection]`／
`[CmsField]` 這些 attribute），並且兩條路都走：自己直接引用 `SqlSugarCore` 套件，也參照
`Struo.Infrastructure`（SqlSugar 因此也會遞移帶進來）——[第 5 章](05-collections.md)說的最低限度
就是前者。

參照它的只有測試專案 `tests/Struo.Tests`，而且用得很深，〈完全移除範例〉列出全部要動的
檔案。`src/Struo.Api/Struo.Api.csproj` 沒有指到它的 `ProjectReference`，
`Struo:ContentAssemblies` 預設是空陣列。

## 開啟範例

開啟範例需要兩處編輯，缺一不可。第一處，在 `src/Struo.Api/Struo.Api.csproj` 加一筆專案參照：

```xml
<ProjectReference Include="..\..\samples\Struo.Sample.Blog\Struo.Sample.Blog.csproj" />
```

第二處，在你自己的 `src/Struo.Api/appsettings.Development.json` 裡把 `"ContentAssemblies"` 那一行
的註解取消——這份檔案是[第 3 章：快速開始](03-getting-started.md)讓你從範例複製出來、不會提交的
副本。要找的區塊就是範例檔裡的這一段（非 Development 環境把陣列放進 `appsettings.json`，或用
`Struo__ContentAssemblies__0`）：

```json
  // Uncomment to enable the Blog sample (see docs/guide/en/23-sample-walkthrough.md).
  // The Struo.Sample.Blog project reference must be added to Struo.Api.csproj as well.
  "Struo": {
    // "ContentAssemblies": [ "Struo.Sample.Blog" ],
```

`Struo:ContentAssemblies` 在服務註冊階段就讀掉了，所以只改設定檔不會生效，一定要重新啟動行
程，改完的設定才算數。

只是想看一看，之後把這兩處編輯還原回去就好，範例的程式碼留著不影響任何東西；要從 fork
裡徹底拿掉，見〈完全移除範例〉。

兩處都做完、重啟之後，CodeFirst 會像建立任何其他集合的資料表一樣，建出 `articles`、
`article_translations`、`tags`、`article_tags`、`categories` 五張表。`GET /api/schema` 這時
會列出十一個集合：範例加的四個集合（`article`、`articleTag`、`category`、`tag`）會跟七個框架
集合一起出現；`ArticleTranslation` 是 sidecar，不是獨立集合。`articleTag` 雖然在後台側欄裡被
`Hidden` 藏起來，`/api/schema` 仍然會列出它。

開著範例的這段期間，`dotnet test` 有一項測試會維持紅燈：
`TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` 直接讀
`Struo.Api.csproj`，斷言裡面不該出現指到 `samples` 的 `ProjectReference`——這正是第一步剛加
上去的那一行。要恢復綠燈，就是下面移除清單的第一步。

## 逐檔導覽

六個 `.cs` 檔，從主角開始看。

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
[第 9 章](09-revisions-and-trash.md)。

自己的欄位涵蓋了第 6 章介面總覽裡的大半介面：`Select`（`Status`）、`DateTime`（`PublishedAt`）、
`Image`（`HeroImageId`）、`MultiSelect`（`Regions`）、`CheckboxGroup`（`Audiences`）、
`Tags`（`Keywords`）、`Json`（`Attributes`）、`KeyValue`（`Meta`）、`Files`（`Gallery`）、
`Repeater`（`Faqs`，子型別是下面的 `FaqItem`），還有一個 `Text` 搭 `Hidden` 的
`InternalNote`。

逐一介面的行為見[第 6 章：欄位型別與編輯介面](06-field-types.md)。

`Status` 是一個 `draft`／`published` 的 `Select`，程式碼預設 `draft`，也是集合的
`DefaultDisplayField`，但它沒有宣告 `Required`。範例裡真正必填的欄位是
`ArticleTranslation.Title`、`Tag.Name`、`Category.Name`、`FaqItem.Question`，以及
`ArticleTag` 的兩個外鍵。

兩個關聯：`Category`（多對一 `Dropdown`，`OnDelete = SetNull`）與 `Tags`（透過 `ArticleTag`
junction 的多對多 `TagSelect`，用 `[CmsRelation(SortField = nameof(ArticleTag.Sort))]` 排
序，表單上能就地編輯每個標籤的 `Note` 跟順序）；兩種關聯介面的運作方式見
[第 8 章：關聯](08-relations.md)。標題與內文不在 `Article` 上，而是放在下面的翻譯 sidecar。

### `ArticleTranslation.cs`

```csharp
[SugarTable("article_translations")]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
```

`ArticleTranslation` 是 `Article` 的翻譯 sidecar，由 `Article` 上的
`[CmsTranslations(typeof(ArticleTranslation))]` 指過來；`ArticleId`／`Locale` 的複合唯一索引
不用自己宣告。它必填、可搜尋的 `Title` 掛在 `Content` 群組，`Body` 是 `RichText`，另外有一個
`Hidden` 的 `InternalSlug`。翻譯 sidecar 的運作方式見[第 7 章：多語內容](07-i18n.md)。

繼承 `SeoTranslation` 讓它免費拿到 `SeoTitle`／`SeoMetaDescription`／`SeoOgImageId`，這三個欄
位掛在 `Article` 宣告的 `SEO` 群組底下。

### `Tag.cs` 與 `ArticleTag.cs`

`Tag`（`[SugarTable("tags")]`）是範例裡最簡單的集合：一個必填、可搜尋的 `Name`。
`Article.Tags` 透過 `[Navigate(typeof(ArticleTag), ...)]` 走的 junction
`ArticleTag`（`[SugarTable("article_tags")]`）自己也掛了 `[CmsCollection(..., Hidden = true)]`，
所以它是一個可讀寫的 junction collection：兩個外鍵之外還有兩欄，一個 `Note` 文字欄位（這條
連結自己的 payload），和兼做 `Article.Tags` 排序欄的 `Sort`。

junction collection 這個機制的完整說明在[第 8 章](08-relations.md)。

`ArticleTag` 的兩個外鍵（`ArticleId`／`TagId`）宣告了 `Required = true`，但透過
`Article.Tags` 這個關聯陣列寫入 payload 時這條規則不生效；兩條寫入路徑的差別見
[第 8 章](08-relations.md)。

如果是對著一個 `article_tags` 已經建好、但還沒有 `note`／`sort` 兩欄的舊資料庫開啟，這兩欄要
靠 Development 的 `Database:AutoSyncSchema=true`（見
[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)），或是自己寫一支 migration 才會
補上——`db/migrations` 收的是 fork 自己寫的腳本，從來不含範例集合的結構。

`ArticleTag` 沒有唯一索引擋 `(ArticleId, TagId)` 這一對重複，直接對 `articleTag` 寫入可以造
出重複的配對；下一次儲存擁有它的 `Article` 會把它修好（留下 PK 最小的一筆，其餘刪除並記一筆
警告），見[第 8 章](08-relations.md)。

### `Category.cs`

```csharp
[SugarTable("categories")]
[SugarIndex("ix_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : AuditableEntity, ISoftDeletable
```

`Category`（`AuditableEntity, ISoftDeletable`）自我參照成一棵樹：`ParentId` 搭一個
`TreeSelect` 的 `Parent` 關聯（`OnDelete = SetNull`），和反過來走同一個外鍵的
`Children`（`RelatedList`）；另外還有一個 `Articles`，是反過來走 `Article.CategoryId`
的 `RelatedList`。自己的欄位只有必填、可搜尋的 `Name`。三種關聯介面的行為見
[第 8 章](08-relations.md)。

### `FaqItem.cs`

`FaqItem` 是純 POCO，沒有 `[SugarTable]`，也沒有 `[CmsCollection]`：它的 `[CmsField]`
只在 `Article.Faqs` 這個 `Repeater` 欄位裡當子型別的形狀用，一個必填的
`Text`（`Question`）、一個 `Textarea`（`Answer`），和一個有兩個選項的 `Select`（`Category`）。

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

`frontend/e2e/sample/` 底下有 8 個 spec 檔（`collections`、`items`、`relations`、
`revisions`、`trash`、`conflict`、`unsaved-guard`、`not-found`）、15 個測試，
`frontend/playwright.config.ts` 把它們接成 `sample` 這個 Playwright 專案，執行指令是
`pnpm e2e:sample`。

跑這組測試，除了 `pnpm e2e` 本身就要的東西（執行中的 API、可連線的資料庫、已種好的
bootstrap 管理員，見[第 22 章：測試與 CI](22-testing.md)）之外，還要把範例加進
`Struo:ContentAssemblies`；完整的先決條件與逐規格的種子資料備註在 `frontend/e2e/README.md`，
其中 `relations.spec.ts` 需要先有一筆 `category`、一筆 `tag` 資料——範例本身不附種子資料。

## 完全移除範例

清乾淨的範圍比 `samples/Struo.Sample.Blog/` 這個目錄大：後端測試套件拿範例的集合當
fixture，照下面的清單逐步走完。

1. 還原兩處開啟編輯：從 `Struo.Api.csproj` 移除指到 `Struo.Sample.Blog.csproj` 的
   `ProjectReference`；把 `appsettings.Development.json` 裡的 `"ContentAssemblies"` 那一行改回註解
   或刪掉。接著把追蹤中的 `appsettings.Development.json.example` 裡那一行、連同 `"Struo"` 上面兩行
   指向它的註解一起移除，否則這份範例設定檔會一直指向不存在的範例。

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

   這道指令會列出 36 個檔案；分布見[第 22 章](22-testing.md)。

5. 跑一次 `dotnet build` 確認編譯已經乾淨——`src/Struo.*` 底下沒有程式碼參照範例。
   `dotnet test` 這時還是紅的，接下來兩步處理它。

6. 修 `tests/Struo.Tests/Metadata/ConventionMetadataDiscoveryTests.cs`：四個測試裡有兩個用字串
   把 `"Struo.Sample.Blog"` 設成要掃描的組件，刪掉 `Configured_content_assembly_is_scanned`
   與 `Registers_entity_type_collector`，或改指向自己 fork 的組件；另外兩個不涉及範例，留著。

7. 刪掉沒有 `using` 範例命名空間、卻經 REST／GraphQL 操作 `article`／`category`／`tag` 的測試檔：
   全靠第 3 步的 module initializer，編譯期抓不到，跑一次 `dotnet test` 看哪些回 `404`／
   `KeyNotFoundException`，分布在 `Api/`、`Query/`、`GraphQl/`、`Localization/`、`Identity/`。

8. 順手清掉四處提到範例的過期註解：`ApiFactory.cs` 與 `CorsAndCookieTests.cs` 引用已經刪
   掉的 `ContentAssemblyEnvBootstrap.cs`，`FakeMetadataFixtures.cs` 與
   `GraphQlExecutionTests.cs` 的註解還指著範例的路徑與型別。

9. 前端這邊：刪掉 `frontend/e2e/sample/`（8 個 spec 檔）；`frontend/playwright.config.ts` 移
   除 `"sample"` 專案與 `core` 不再需要的 `testIgnore: '**/e2e/sample/**'`；`frontend/package.json`
   移除 `"e2e:sample"`、`"e2e:all"`，只留 `"e2e"`；修剪 `frontend/e2e/README.md` 談範例的段落。

10. 清掉任何跑過範例的資料庫裡留下的五張表——CodeFirst 建的表不會跟著程式碼一起消失：

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
