# 16. 範例走查

本章涵蓋 `samples/Struo.Sample.Blog/`，一個隨這個儲存庫出貨、外形像部落格的小型示範。它不是 core
能力——它存在純粹是為了展示，一旦第 4 章的檢查清單每一個步驟都真正被執行過之後，會產生出什麼：一個
帶有欄位與群組的完整集合 (collection)、一個翻譯附屬資料表、一個透過 junction 資料表建立的多對多關聯、
一棵自我參照的樹狀結構，以及一個 Repeater 子欄位型別。本章會選用啟用它、逐一走查每一個檔案、執行它
專屬的端到端套件，然後再次把它完全移除——撰寫本章時，這兩半都已針對這份檢出程式碼逐字執行過。

## 這個範例的用途

`samples/Struo.Sample.Blog/Struo.Sample.Blog.csproj` 是一個單純的類別庫，參照了 `Struo.Domain`
(取得 `[Cms*]` attribute)與 `Struo.Infrastructure`(間接取得 SqlSugar)——這是第 4 章檢查清單針對
內容專案的 SqlSugar 相依性所允許的兩種變體中的第二種，另一種則是直接參照 `SqlSugarCore` 套件。它宣告
了三個真正的集合——`Article`、`Tag`、`Category`——外加 `ArticleTranslation`(`Article` 的翻譯附屬
資料表，本身並非一個集合)、一個純粹的 `ArticleTag` 多對多 junction(同樣沒有標示
`[CmsCollection]`)，以及 `FaqItem`，一個被用作 Repeater 子欄位型別的單純 POCO。

`src/Struo.*` 底下沒有任何東西參照它。出貨的 `src/Struo.Api/Struo.Api.csproj` 沒有任何
`ProjectReference` 指向它，出貨的 `Struo:ContentAssemblies` 也是 `[]`——這在第 2 章「尚無集合時」的
走查中已經確認過。這個範例在出貨的樹狀結構中恰好被接上一個地方：`tests/Struo.Tests`，它的後端測試
套件使用這些相同的集合作為具體的固定物 (fixture)，來演練一般性的框架行為(RBAC、版本紀錄、軟刪除、
關聯、i18n、查詢 DSL)。正是這種耦合，讓移除這個範例比刪除一個目錄要複雜得多——下方的「完全移除範例」
一節會完整涵蓋這件事，並已針對這份檢出程式碼進行端到端驗證。

## 選用啟用它

兩項編輯，兩者都是必要的，且順序如下：

**1. 新增 `ProjectReference`**到 `src/Struo.Api/Struo.Api.csproj`：

```xml
<ItemGroup>
  <ProjectReference Include="..\Struo.Application\Struo.Application.csproj" />
  <ProjectReference Include="..\Struo.Infrastructure\Struo.Infrastructure.csproj" />
  <ProjectReference Include="..\..\samples\Struo.Sample.Blog\Struo.Sample.Blog.csproj" />
</ItemGroup>
```

**2. 把組件名稱加入 `Struo:ContentAssemblies`。** 在 `appsettings.Development.json`(依第 2 章所述，
從 `appsettings.Development.json.example` 複製而來)中，這一行已經以註解的形式存在——把它取消
註解：

```json
"Struo": {
  "ContentAssemblies": [ "Struo.Sample.Blog" ],
  ...
}
```

(一個非 Development 環境，會把相同的陣列加進 `appsettings.json`，或透過
`Struo__ContentAssemblies__0=Struo.Sample.Blog` 覆寫它。)

這兩項編輯必須一起完成：這個組件必須是*可被解析的*(`ProjectReference`)，也必須是*被命名的*
(`ContentAssemblies`)——原因見第 4 章。`Struo:ContentAssemblies` 只會在 host 建置之前被讀取一次
(第 3 章)，因此這項變更只會在**下一次**重新啟動時生效；一個正在執行的執行個體必須被停止並重新啟動，
而不只是在它底下把設定檔編輯掉。

完成這兩項編輯後重新啟動，dev `InitTables` 就會建立這個範例的資料表(`articles`、
`article_translations`、`tags`、`article_tags`、`categories`)，方式與它建立任何其他內容集合的
資料表完全相同。已針對這份檢出程式碼即時驗證：完成這兩項編輯並重新啟動之後，`GET /api/schema`
(以啟動用管理員身分)會在七個框架集合之外(第 4 章)，一併列出 `article`、`category` 與 `tag`，
而在此之前一刻，它還只列出框架集合。

以這種方式選用啟用範例時，一個出貨的後端測試會刻意轉紅：
`tests/Struo.Tests/Template/TemplateInvariantsTests.cs` 的
`Host_project_has_no_project_reference_into_samples` 會直接讀取 `Struo.Api.csproj`，並斷言沒有
任何 `ProjectReference` 那一行提到 `samples`——而這正是上面步驟 1 剛剛新增的東西，所以
`dotnet test` 會回報這一項失敗，直到「完全移除範例」步驟 1 的還原動作把 csproj 放回去為止。

## 導覽走查

下方每個檔案都是直接對照所引用的原始碼閱讀而來的。

### `Article.cs`——一個完整的集合

```csharp
[SugarTable("articles")]
[SugarIndex("ix_articles_categoryid", nameof(CategoryId), OrderByType.Asc)]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status), Revisions = true)]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : AuditableEntity, ISoftDeletable
```

這是這個範例的核心，也是第 4 章欄位群組一節回頭指向的地方。它實作了 `ISoftDeletable`
(`DeletedAt`/`DeletedBy`)並設定 `Revisions = true`，因此它同時具備回收桶與版本歷史(第 13
章)。它宣告了兩個欄位群組，`Content` 與 `SEO`——它自己的欄位全部使用 `Group = "Content"`；`SEO`
群組則被透過其翻譯附屬資料表繼承而來的欄位使用(見下方)。除了 `Status`(一個必填的 `Select`，選項
為 `draft`/`published`，也是該集合的 `DefaultDisplayField`)之外，它在同一個地方走過了幾乎每一種
欄位介面：`PublishedAt`(`DateTime`)、`HeroImageId`(`Image`)、`Regions`(`MultiSelect`，其中
一個選項——`"amer"`——刻意不加標籤，讓它回退顯示原始值)、`Audiences`(`CheckboxGroup`)、
`Keywords`(`Tags`，沒有固定的 `[CmsOptions]` 清單——第 4 章提到這是一個真正自由格式 `Tags` 欄位
常見的模式)、`Attributes`(`Json`)、`Meta`(`KeyValue`)、`Gallery`(一個多檔案的 `Files`
欄位)、`Faqs`(`Repeater`，型別為 `FaqItem`——見下方)，以及 `InternalNote`，一個被排除在
schema、GraphQL 與項目投影之外的 `Hidden` 文字欄位。它還宣告了兩個關聯：`Category`(一個多對一的
`Dropdown`，`OnDelete = SetNull`)與 `Tags`(一個透過 `ArticleTag` junction 建立的多對多
`TagSelect`，藉由 `[Navigate(typeof(ArticleTag), ...)]`)。標題與內文**不在**這裡——它們位於翻譯
附屬資料表上，接下來就會談到。

### `ArticleTranslation.cs`——翻譯附屬資料表

```csharp
[SugarTable("article_translations")]
[SugarIndex("ix_article_translations_fk_locale", nameof(ArticleId), OrderByType.Asc, nameof(Locale), OrderByType.Asc)]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
```

透過 `[CmsTranslations(typeof(ArticleTranslation))]`(第 6 章)從 `Article` 參照。它在一個複合
`UNIQUE` 限制之下攜帶逐語言的 `ArticleId`/`Locale` 配對(每篇文章每個語言各一列翻譯資料)，還有
必填、可搜尋的 `Title`(`Group = "Content"`)、一個 `RichText` 的 `Body`，以及第二個 `Hidden`
欄位 `InternalSlug`，與 `Article.InternalNote` 相呼應。透過繼承 `SeoTranslation`，它還無償取得了
`SeoTitle`/`SeoMetaDescription`/`SeoOgImageId`，歸檔在 `Article` 上宣告的 `SEO` 群組之下——這正是
第 4 章欄位群組一節所指向的機制。

### `Tag.cs` + `ArticleTag.cs`——透過 junction 建立的多對多關聯

`Tag`(`[SugarTable("tags")]`)是這個範例中最單純的一個真正集合：只有一個必填、可搜尋的 `Name`。
`ArticleTag`(`[SugarTable("article_tags")]`)則完全**不是**一個集合——沒有 `[CmsCollection]`，
只有 `Id`/`ArticleId`/`TagId`，外加各自 FK 欄位上的一個次要索引。它存在純粹是作為 `Article.Tags`
的 `[Navigate(typeof(ArticleTag), ...)]` 關聯所走過的那張連接表，讓 `Article` 擁有一個多對多的
`TagSelect` 欄位，而不需要自己專屬的 junction-table UI(第 7 章)。

### `Category.cs`——帶有自我參照樹狀結構的多對一關聯

```csharp
[SugarTable("categories")]
[SugarIndex("ix_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : AuditableEntity, ISoftDeletable
```

`Category` 是可軟刪除且自我參照的：`ParentId` 加上一個 `Parent` 關聯(`TreeSelect`，
`OnDelete = SetNull`)，以及一個反向的 `Children` 關聯(`RelatedList`)，沿著同一個 FK 走向另一個
方向。它還額外宣告了 `Articles`，一個沿著 `Article.CategoryId` 反向走的 `RelatedList`——也就是
`Article` 針對 `Category` 宣告的那個多對一關聯，從 `Category` 這一側看過去的樣子。

### `FaqItem.cs`——一個最小化、非集合的型別

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

沒有 `[SugarTable]`，也沒有 `[CmsCollection]`——`FaqItem` 是一個單純的 POCO，其標示了
`[CmsField]` 的屬性只會被當作 `Article.Faqs` 的 `Repeater` 介面的子欄位形狀來讀取。它是 Repeater
項目型別最小化的示範：一個必填的 `Text`、一個普通的 `Textarea`，以及一個帶有兩個選項的選用
`Select`。

## 執行範例的 E2E 套件

`frontend/e2e/sample/` 存放了八個 spec 檔案(`collections`、`items`、`relations`、`revisions`、
`trash`、`conflict`、`unsaved-guard`、`not-found`)，端到端演練這個範例的集合：對 `article` 的
瀏覽與 CRUD、關聯編輯及其 `RelatedList` 那一側、版本歷史 drawer 與還原、軟刪除/還原/清除、樂觀並行
控制的衝突橫幅，以及未儲存變更的導覽守衛。`frontend/playwright.config.ts` 把它們接上為 `sample`
這個專案，以下列方式執行：

```bash
pnpm e2e:sample
```

這需要 `pnpm e2e`(第 15 章)所需要的一切——一個正在執行的 API、一個可連線的資料庫、已建立種子資料
的啟動用管理員，以及停用或放寬到足以應付這個套件多次登入的登入速率限制器——**外加**如上所述、已選用
啟用進 `Struo:ContentAssemblies` 的 Blog 範例，因為這些 spec 每一個都依賴
`article`/`category`/`tag` 這些集合的存在。`frontend/e2e/README.md` 記載了完整的先決條件清單，
以及逐 spec 的種子資料備註；這裡的內容不會改變它們。

已針對這份選用啟用範例的檢出程式碼即時確認：`pnpm e2e:sample` 會發現全部八個 spec 檔案(13 個
測試)，並針對正在執行的 API 驅動它們——包括對 `article` 完整的建立/編輯/刪除、帶有 `RelatedList`
導覽的關聯編輯，以及過期版本的衝突橫幅——透過一個真正已登入的 session，針對上方導覽過的那些集合進
行。`pnpm e2e:all` 會在單一次呼叫中，一起執行這個專案與 `core` 專案(第 15 章)。

## 完全移除範例

光是刪除 `samples/Struo.Sample.Blog/` 本身，並不足以留下一個乾淨的建置：這個儲存庫自己的後端測試
套件，把這個範例的集合當作固定物 (fixture) 使用的範圍，遠遠超出這個示範目錄本身——既有直接匯入範例
型別的做法，也有完全不匯入任何東西、直接透過 REST/GraphQL 演練 `article`/`category`/`tag` 的做法。
下方完整的檢查清單已針對這份檢出程式碼端到端執行過；這裡的每一個步驟都是達成一次綠燈建置與綠燈測試
執行所必要的——沒有一個是選擇性的。

1. **還原那兩項選用啟用的編輯**(若已進行)：把 `<ProjectReference>` 到
   `Struo.Sample.Blog.csproj` 從 `src/Struo.Api/Struo.Api.csproj` 中移除，並把
   `"Struo.Sample.Blog"` 這個項目從你自己 `appsettings.Development.json` 中的
   `Struo:ContentAssemblies` 移除(或重新加上註解)。同時也要把受版控的
   `src/Struo.Api/appsettings.Development.json.example` 中對應、仍帶有註解的區塊移除
   (`// Uncomment to enable the Blog sample …` 這則註解，以及那行加了註解的
   `"ContentAssemblies": [ "Struo.Sample.Blog" ]`)——否則出貨的範例檔會持續把每一位未來的讀者
   指向一個已經不存在的範例。
2. **完全刪除 `samples/Struo.Sample.Blog/`**。
3. **從 `StruoCMS.slnx` 移除它的項目**——整個 `<Folder Name="/samples/">` 區塊。
4. **移除 `<ProjectReference>`**——把指向 `Struo.Sample.Blog.csproj` 的那一項，從
   `tests/Struo.Tests/Struo.Tests.csproj` 中移除。
5. **刪除 `tests/Struo.Tests/Support/ContentAssemblyEnvBootstrap.cs`。** 這正是把這個範例接進
   套件中每一個測試 host 的檔案：一個 `[ModuleInitializer]`，會在任何測試執行之前，於整個行程範圍
   內設定環境變數 `Struo__ContentAssemblies__0=Struo.Sample.Blog`——這是為了因應
   `Struo:ContentAssemblies` 會在 `WebApplicationFactory` 能夠注入它自己的設定覆寫之前就被讀取，
   所做的權宜之計(它自己的文件註解完整解釋了這件事)。沒有這個檔案，任何以 `ApiFactory` 為基礎的
   測試 host，都完全不會擁有這個範例的集合。
6. **刪除每一個直接匯入這個範例型別的測試檔案**(`using Struo.Sample.Blog;`)。撰寫本文時，這是
   26 個檔案：`tests/Struo.Tests/Query/` 底下 17 個、`Metadata/` 底下 3 個、`Persistence/` 底下
   3 個、`Revisions/` 底下 2 個、`Health/` 底下 1 個。用以下指令找出目前的清單：

   ```bash
   grep -rl "using Struo.Sample.Blog;" tests/Struo.Tests
   ```

   一旦這些檔案都不見了，`dotnet build` 就會成功(`src/Struo.*` 底下從未參照過這個範例)，但
   `dotnet test` 還不會——接下來兩個步驟正是原因所在。
7. **修正 `tests/Struo.Tests/Metadata/ConventionMetadataDiscoveryTests.cs`。** 它的四個測試中
   有兩個，把 `"Struo.Sample.Blog"` **以名稱**(一個純字串，而非匯入)設定成一項
   `Struo:ContentAssemblies` 項目，並斷言它會成功載入——刪除
   `Configured_content_assembly_is_scanned` 與 `Registers_entity_type_collector`，或針對你的
   fork 實際出貨的一個組件重寫它們。另外兩個測試(空設定與無法載入組件的案例)並未參照這個範例，
   不受影響。
8. **刪除每一個完全不匯入範例命名空間、卻透過 REST 或 GraphQL 演練 `article`/`category`/`tag`
   集合的測試檔案。** 這些檔案完全仰賴步驟 5 的 module initializer，因此編譯期不會抓到它們——
   會發現它們的是 `dotnet test`，每一個集合形狀的測試都會出現一次 `HTTP 404`/`KeyNotFoundException`
   失敗，散布在 `Api/`、`Query/`、`GraphQl/`、`Localization/` 與 `Identity/` 之中。沒有任何東西
   事先把這些檔案標記為與範例耦合，而且這個集合會隨套件成長而漂移，因此沒有一個靜態指令可以取代
   直接執行它：在步驟 1–7 之後執行 `dotnet test`，刪除每一次失敗所在的檔案，重複直到綠燈為止——
   這個檢查清單實際上就是用這個迴圈驗證過的，而不是一份固定清單。在處理這些檔案時，也順手整理少數
   幾則指向你剛剛刪除之物的註解——`tests/Struo.Tests/Support/ApiFactory.cs` 與
   `tests/Struo.Tests/Api/CorsAndCookieTests.cs` 各有一則註解，引用了現已不存在的
   `ContentAssemblyEnvBootstrap.cs`，而 `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs` 與
   `GraphQl/GraphQlExecutionTests.cs` 各有一則註解，引用了一個 `samples/Struo.Sample.Blog/`
   路徑。這些都不會讓建置失敗，但會是過時的內容。
9. **在前端方面：** 刪除 `frontend/e2e/sample/`(8 個 spec 檔案)；從
   `frontend/playwright.config.ts` 移除 `"sample"` 這個專案項目(以及它的 `core` 專案現在已不
   需要的 `testIgnore: '**/e2e/sample/**'`，因為已經沒有東西需要忽略)；從
   `frontend/package.json` 移除 `"e2e:sample"` 與 `"e2e:all"` 這兩個 script，只留下 `"e2e"`
   作為唯一的端到端 script；並從 `frontend/e2e/README.md` 中清除與範例套件相關的內容——它開頭套件
   清單中的 `sample` 項目、`pnpm e2e:sample` 那項先決條件(Blog 範例選用啟用與 `E2E_STAMP`)，
   以及其「Further reading」一節中指向第 16 章的連結。
10. **從任何執行過它的資料庫中，卸除這個範例的資料表**——dev `InitTables` 建立了它們，而沒有任何
    東西會自動卸除它們：`articles`、`article_translations`、`tags`、`article_tags`、
    `categories`。它們從來不是 `db/migrations/001-core-baseline.sql` 或任何其他受追蹤 migration
    (第 15 章)的一部分，因此不需要撰寫任何 migration 來移除它們——針對你的開發資料庫直接執行
    一次 `DROP TABLE IF EXISTS article_tags, article_translations, articles, categories, tags
    CASCADE;` 就足夠了。

**驗證，依此順序執行：**

```bash
dotnet build
dotnet test
cd frontend && pnpm test && pnpm build && pnpm e2e
```

已針對這份檢出程式碼確認：在完成上面每一個步驟之後，`dotnet build` 會成功且沒有任何警告，而
`dotnet test`、`pnpm test`，以及 `pnpm build`(`vue-tsc -b && vite build`)全部都完全通過——兩個
套件中剩下的每一個測試都通過，不再有那些已刪除檔案曾經造成的任何失敗。`pnpm e2e` 需要一個正在執行
的 API 與資料庫，完全如第 15 章所述——這項需求，以及 core 套件的行為，都不受移除範例影響。

## 接下來該去哪

- 第 4 章 [定義一個集合](04-defining-a-collection.md)，這個範例正是該章檢查清單的一個實作範例。
- 第 13 章 [版本紀錄與軟刪除](13-revisions-and-soft-delete.md)，涵蓋 `Article` 的 `Revisions`
  旗標，以及兩個集合的 `ISoftDeletable` 實作完整內容。
- 第 15 章 [部署、維運與測試](15-deployment-operations-testing.md)，涵蓋 `core` E2E 專案、
  後端/前端測試層，以及 CI 會與不會執行的內容。
