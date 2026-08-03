# 4. 定義一個集合

本章示範下游 fork 如何新增自己的內容型別。這裡的一切都是新增性質:不會動到任何框架程式碼，只需要一個
新的 entity 類別 (位於你自己的專案中)。CodeFirst 會自動建立它的資料表，不論你執行哪一種後端、也不論
哪一個環境——一個全新的集合完全不需要任何 migration 腳本 (見下方「建立資料表」一節)。

## 中介資料驅動模型

一個**集合 (collection)** 就是一個帶有 `[CmsCollection]` attribute
(`Struo.Domain.Metadata.Attributes.CmsCollectionAttribute`) 的一般 C# 類別。啟動時，
`MetadataScanner.Scan` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) 會反射
(reflect) 掃描組件中的每一個型別 (見下方「內容專案存放於何處」)，並為每一個帶有 `[CmsCollection]`
attribute 的類別建立一筆 `CollectionMetadata` 記錄:它的標籤 (label)、icon、群組 (group)、預設顯示
欄位、`AdminOnly`/`Hidden`/`Revisions` 旗標、其 entity 是否實作 `ISoftDeletable`、它的欄位清單
(來自每個屬性的 `[CmsField]`)、它的欄位群組 (來自 `[CmsFieldGroup]`)、它的關聯 (來自 `[CmsRelation]`
+ SqlSugar 的 `[Navigate]`，第 7 章)，以及如果有的話，它的翻譯附屬資料表 (來自 `[CmsTranslations]`，
第 6 章)。

這一筆 `CollectionMetadata` 記錄就是其他每一層據以推導的單一來源:資料庫資料表 (透過 SqlSugar
CodeFirst 或一支 migration 腳本)、REST 端點及其查詢 DSL 介面 (第 8–9 章)、GraphQL schema
(第 10 章)，以及管理後台 SPA 的導覽、清單檢視與項目表單。沒有另外一個地方需要宣告 REST 路由、
GraphQL 型別或管理後台畫面——那一個帶 attribute 的 C# 類別就是完整的宣告。

這次掃描是即時 (eager) 執行、只發生一次，時機是在 DI 註冊時
(`MetadataServiceCollectionExtensions.AddStruoMetadata`);結果會被快取在一個單例 (singleton) 的
`IMetadataProvider` 中。集合中介資料完全不會逐請求重新掃描。

## 最小集合

最小、完整、可編譯的集合需要:一個繼承 `AuditableEntity` 的類別、一個帶有
`[SugarColumn(IsPrimaryKey = true)]` 的覆寫 `Id`、一個為其資料表命名的 `[SugarTable]`、一個為集合
命名的 `[CmsCollection]`，以及至少一個帶有 `[CmsField]` attribute 的屬性。這個範例位於一個佔位用的
`Acme.Content` 專案中——你自己 fork 的內容專案可以取任何名字:

```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Acme.Content;

[SugarTable("announcements")]
[CmsCollection("Announcement", Icon = "megaphone", Group = "Content", DefaultDisplayField = nameof(Title))]
public sealed class Announcement : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public override Guid Id { get; set; }

    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Title { get; set; } = string.Empty;

    [CmsField(Label = "Body", Interface = FieldInterface.Textarea, Sort = 2)]
    public string Body { get; set; } = string.Empty;

    [CmsField(Label = "Priority", Interface = FieldInterface.Select, Sort = 3)]
    [CmsOptions("low:Low", "medium:Medium", "high:High")]
    public string Priority { get; set; } = "medium";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Published At", Interface = FieldInterface.DateTime, Sort = 4)]
    public DateTime? PublishedAt { get; set; }
}
```

這個範例所依賴的幾件事，都是真實、可查驗的規則:

- `AuditableEntity` (`src/Struo.Domain/Auditing/AuditableEntity.cs`) 把 `Id` 宣告為
  `abstract`，特意讓遺漏主鍵成為**編譯期錯誤**，而不是執行期才會出現的意外——覆寫是必要的，而覆寫上的
  `[SugarColumn(IsPrimaryKey = true)]` 正是 SqlSugar 用來辨識該資料表主鍵的依據。`AuditableEntity`
  也提供 `CreatedAt`/`CreatedBy`/`UpdatedAt`/`UpdatedBy` (自動戳記);這四個屬性都不需要
  `[CmsField]`——掃描器會自動把它們加為唯讀的「系統」欄位 (見第 5 章)。`AuditableEntity` 另外還提供一個
  樂觀並行控制 (optimistic-concurrency) 用的 `Version` 欄位，這完全不是系統欄位——掃描器會忽略任何
  既沒有 `[CmsField]` 也不屬於四個稽核 (audit) 名稱之一的屬性，所以 `Version` 之所以會出現在 API
  回應中，純粹是因為 `ItemProjector` 在欄位迴圈之外，直接連同 `id` 一起輸出它。
- `[SugarTable("announcements")]` 明確為資料表命名，符合每一個框架與範例 entity 所採用的慣例
  (`src/Struo.Infrastructure/Files/MediaFolder.cs`、`samples/Struo.Sample.Blog/Article.cs` 等)——
  一個小寫、複數、snake_case 的資料表名稱。
- 一個既沒有 `[CmsField]` 也不符合稽核欄位命名慣例的屬性，掃描器會直接忽略它——如果需要，你可以在同一個
  entity 上保留一般、非 CMS 用途的屬性。
- 想要軟刪除或版本紀錄，取代 (或加上) 以上欄位?在類別上實作 `ISoftDeletable`，或在 `[CmsCollection]`
  上設定 `Revisions = true`——第 13 章會完整涵蓋這兩者。

## `[CmsCollection]` 選項

`CmsCollectionAttribute` (`src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs`) 恰好
宣告了以下這些屬性——這是完整清單，不是精選列表:

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Label` | `string` (建構函式引數，必填) | 集合的顯示標籤。 |
| `Icon` | `string?` | 顯示在管理後台側欄的 icon 名稱。 |
| `Group` | `string?` | 此集合歸屬的側欄導覽群組。 |
| `DefaultDisplayField` | `string?` | 用作項目顯示標題的欄位名稱 (例如在關聯選擇器、breadcrumb 中)。必須指向一個真實存在的欄位——否則掃描器會在啟動時擲出 `MetadataException`。 |
| `AdminOnly` | `bool` | 為 `true` 時，一般 CRUD 寫入操作 (新增/更新/刪除) 一律需要超級管理員身分，無論該集合有沒有任何逐集合 RBAC 授權——框架自身的身分/授權資料表就是用這個旗標，讓一個被委派的寫入授權無法被濫用來自我提權。讀取仍然遵循一般的 RBAC。 |
| `Hidden` | `bool` | 讓集合不出現在管理後台側欄/導覽中。它仍然可以完整透過 REST/GraphQL 與直接的管理後台 URL 存取——這僅僅是一個顯示旗標，不是一項存取規則。 |
| `Revisions` | `bool` | 讓集合選用啟用版本紀錄:每一次成功的新增/更新都會把一份完整的寫入後快照附加到框架的 `revisions` 資料表，且任何過去的版本都能被還原。見第 13 章。 |

還有兩個相關的旗標值得提一提，即使它們並非 attribute 屬性:**軟刪除 (soft delete)** 完全不是
`[CmsCollection]` 的選項——掃描器純粹依 entity 是否實作 `ISoftDeletable` 來推導
`CollectionMetadata.SoftDelete`，不需要任何 attribute (第 13 章)。而 **RBAC** 授權 (誰能讀取/寫入/
刪除某個集合) 是分開設定的，逐角色在管理後台 UI 或透過 API 設定——一個全新的集合在你加入授權之前，
完全沒有任何授權 (第 12 章)。

## `[CmsField]` 選項

`CmsFieldAttribute` (`src/Struo.Domain/Metadata/Attributes/CmsFieldAttribute.cs`) 套用在屬性上，
並宣告:

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Label` | `string?` | 顯示標籤;省略時會退回使用 CLR 屬性名稱。 |
| `Interface` | `FieldInterface` | 決定套用哪一種儲存/編輯器對應 (完整清單見第 5 章);預設為 `Text`。 |
| `Required` | `bool` | 對非可翻譯欄位，在寫入 (新增/更新) 時強制執行。 |
| `Searchable` | `bool` | 納入該集合的自由文字搜尋白名單 (第 8 章)。 |
| `Sortable` | `bool` | 允許做為查詢 DSL 的排序鍵。 |
| `Sort` | `int` | 決定欄位在管理後台表單中的順序，並間接影響集合清單的欄位順序。 |
| `ReadOnly` | `bool` | 讀取時會回傳其值;更新永遠無法把 client 提供的值搬移到它上面，新增時也會把它剔除——但有一個 CLR 型別上的但書——見第 5 章。 |
| `Hidden` | `bool` | 把欄位從 schema、GraphQL、項目投影 (projection) 以及查詢的篩選/搜尋/排序中移除——見第 5 章。 |
| `HelpText` | `string?` | 顯示在管理後台表單輸入欄位下方的說明文字。 |
| `Translatable` | `bool` | 欄位存放於逐 locale 的翻譯附屬資料表，而非父資料列上 (第 6 章)。 |
| `Group` | `string?` | 此欄位所屬的 `[CmsFieldGroup]` 名稱 (見下文)。 |
| `MaxLength` | `int` | CMS 層級的輸入長度上限;`0` 代表未設定。第 5 章涵蓋其確切的預設值規則。 |

`CmsFieldAttribute` 也宣告了一個 `Display` 屬性，但 `MetadataScanner.BuildField` 完全沒有讀取它，
`FieldMetadata` 上也沒有對應的屬性——它目前沒有任何可觀察到的效果;請不要依賴它。

## 欄位群組 (`[CmsFieldGroup]`)

`CmsFieldGroupAttribute` (`AllowMultiple = true`，套用於類別層級) 宣告一個具名的區段:`Name`
(建構函式引數)、`Label`，以及 `Sort`。欄位透過設定 `[CmsField(Group = "SameName")]` 來加入某個
群組——範例的 `Article` 集合宣告了兩個 (`[CmsFieldGroup("Content", ...)]`、
`[CmsFieldGroup("SEO", ...)]`):`Article` 自身的欄位使用 `Group = "Content"`，而它的翻譯附屬資料表
所繼承的 SEO 欄位 (`src/Struo.Domain/Seo/SeoTranslation.cs`) 則使用 `Group = "SEO"`。

群組會被記錄在中介資料中，並回傳給 API/GraphQL 呼叫端 (`CollectionMetadata.FieldGroups`、
`FieldMetadata.Group`)——但就出貨狀態而言，管理後台 SPA 的項目表單
(`frontend/src/components/ItemForm.vue`) 完全不會依群組分區表單:`splitFields.ts` 只把非系統欄位
分成 `shared` 與 `translatable` 兩個桶 (bucket) (各自依 `Sort` 排序)，而 `ItemForm.vue` 會把
`translatable` 桶渲染在逐 locale 的分頁 (tab) 中，再把 `shared` 桶以扁平清單的形式渲染在下方——兩者
都用不到 `Group`。目前宣告群組主要是為 API 消費端記錄結構，而非在視覺上把管理後台表單分區。

## 選項清單 (`[CmsOptions]`)

`CmsOptionsAttribute` 接受一個 `params string[]` 的選項清單，每一個項目要嘛是 `"value:label"`，
要嘛是單純的 `"value"` (標籤預設等同於值——範例的 `Article.Regions` 欄位除了帶標籤的項目之外，還有一個
單純的項目 `"amer"`)。如果某個項目的值那一半是空白，`MetadataScanner.ParseOptions` 會擲出
`MetadataException`。

`[CmsOptions]` 只在選項型的介面上有效——`Select`、`MultiSelect`、`Radio`、`CheckboxGroup` 或
`Tags`;把它附加在任何其他介面上，會在啟動時擲出 `MetadataException`。實務上，
`Select`/`Radio`/`MultiSelect`/`CheckboxGroup` 通常一定會宣告一個 (沒有固定選項清單，它們就沒有輸入
UI 可用)，而 `Tags` 通常不會設定，讓使用者能真正自由輸入——比較範例中的 `Article.Keywords` (未使用)
與有使用它的 `Article.Regions`/`Article.Audiences`。

## 內容專案存放於何處，以及探索機制如何運作

`AddStruoMetadata` (`src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs`)
恰好掃描三種來源的組件 (assembly):

1. **框架自身的組件** (`Struo.Infrastructure`)——一律會被附加，所以框架自身帶有 `[CmsCollection]`
   attribute 的型別 (`Language`、`File`、`MediaFolder`、`User`、`Role`、`Permission`、
   `UserRole`——第 1 章列出的十個框架 entity 型別中的七個;`FileTranslation`、`Revision` 與
   `SiteSettings` 是框架資料表，但不是集合) 一律會被發現。
2. **host 組件**（承載應用程式的 `Struo.Api`）——也就是 `Struo.Api` 本身
   (`typeof(Program).Assembly`，由 `Program.cs` 明確傳入)。
3. **`Struo:ContentAssemblies` 中列出的每一個組件**——在 `builder.Build()` 執行*之前*，就從
   `builder.Configuration` 讀取 (第 3 章完整涵蓋這種時機所帶來的影響)。每一個名稱都透過
   `Assembly.Load(new AssemblyName(name))` 解析;**任何無法載入的項目都會讓啟動失敗**，並擲出一個
   指名該項目的 `MetadataException`——絕不會被靜默略過。

要讓一個具名的組件被載入，它必須真的能被解析:要嘛被 host 專案參照 (在
`src/Struo.Api/Struo.Api.csproj` 中的一個 `ProjectReference`)，要嘛以其他方式，以一個可載入的 DLL
存在於 host 旁邊。出貨的 host 對任何內容專案都**沒有** `ProjectReference`，而
`Struo:ContentAssemblies` 出貨時是 `[]`——這正是為什麼一份全新的 checkout 會有零個內容集合
(第 1–2 章)。

要新增你自己的內容專案:建立一個參照 `Struo.Domain` (取得中介資料 attribute/enum) 的類別庫，並參照
足夠的 SqlSugar 以便宣告 `[SugarTable]`/`[SugarColumn]`;從 `Struo.Api.csproj` 為它加上一個
`ProjectReference`;把它的組件名稱加入 `Struo:ContentAssemblies`;然後重新啟動 API (這只是一次啟動期
掃描——第 3 章)。`samples/Struo.Sample.Blog` 正是這種模式、已經建好的一個**可拆卸示範**——第 16 章
會走過用同樣這兩個步驟選用啟用它，以及如何再乾淨地移除它。

## 建立資料表:CodeFirst 在每一個環境中都會自動處理

直接重新啟動 API 即可——資料表本身不需要任何設定。建表不受環境把關:只要某個 entity 型別的資料表尚不
存在，SqlSugar 的 CodeFirst 步驟就會建立它——你的內容集合自己的資料表、它的翻譯附屬資料表 (若有)，
以及它宣告的任何 M2M 關聯表 (`EntityTypeCollector.CollectForInitTables`——見
`src/Struo.Api/Program.cs`)——這一切都發生在其他任何動作之前，在任何一種已設定的五種後端上，開發環境
與正式環境皆然。因為一個全新集合的資料表尚不存在，這就是讓它成形所需要的唯一步驟:**一個新集合的
初始資料表，在任何環境中都不需要任何 migration 腳本。**

Migration 只有在之後才會派上用場——當這張資料表已經存有你需要保留的資料，而你需要變更它的形狀時:
例如改欄位名稱、收窄型別、對一張已有資料的資料表加上 `NOT NULL` 約束等等。`Database:MigrationsPath`
正是為了這種情況，讓 `MigrationRunner` 指向一個存放已審查 `*.sql` 腳本的目錄，而且這個 runner 現在
會在每一種後端上執行，不只 PostgreSQL。另外還有 `Database:AutoSyncSchema`，一個僅限 Development 的
選用開關，讓 CodeFirst 自動變更既有資料表——在真正的資料出現之前用來快速迭代 schema 很方便，但一旦
有了真正的資料就很危險。第 15 章完整涵蓋這兩套機制，包括 `AutoSyncSchema` 的九項危險情境清單，以及
如何撰寫具可攜性的 migration。

## 新增一個集合的端到端檢查清單

1. 建立 (或重複使用) 一個內容類別庫專案;參照 `Struo.Domain` 取得 attribute/enum，並參照足夠的
   SqlSugar (直接參照，或透過 `Struo.Infrastructure` 間接取得) 以便使用
   `[SugarTable]`/`[SugarColumn]`。
2. 撰寫 entity:繼承 `AuditableEntity`、以 `[SugarColumn(IsPrimaryKey = true)]` 覆寫 `Id`、加上
   `[SugarTable("your_table_name")]` 與 `[CmsCollection("Your Label", ...)]`。
3. 為每一個 API/管理後台表單應該公開的屬性加上 `[CmsField]`，並在任何
   `Select`/`Radio`/`MultiSelect`/`CheckboxGroup` 欄位上加上 `[CmsOptions]`。
4. 如果這個集合需要回收桶/還原或版本歷史，就實作 `ISoftDeletable` 並/或設定 `Revisions = true`
   (第 13 章)。
5. 如果這個集合會參照另一個集合，就用 `[CmsRelation]` + SqlSugar 的 `[Navigate]` 加上關聯
   (第 7 章)。
6. 從 `src/Struo.Api/Struo.Api.csproj` 為你的內容專案加上一個 `ProjectReference`，並把它的組件
   名稱加入 `Struo:ContentAssemblies`。
7. 重新啟動 API。CodeFirst 會自動建立資料表——在每一個環境中都會，不只開發環境——確認管理後台 SPA
   的側欄現在顯示一個「Content」導覽群組，且其中有你的集合 (對照第 2 章「尚無集合時」的狀態)。
8. 把這個集合的 RBAC 讀取/寫入/刪除權限授予需要的角色 (第 12 章)——一個全新的集合還沒有任何授權，
   所以在你這麼做之前，只有超級管理員能使用它。
9. 針對正式環境部署，資料表本身不需要任何額外動作——CodeFirst 在那裡也會自動建立它。只有當你之後要
   變更一張已經存有你需要保留之資料的資料表的形狀時，才需要一支 migration (第 15 章)。

## 接下來該去哪

- 第 5 章 [欄位型別與介面](05-field-types.md)，取得本章 `[CmsField]` 範例所依據的完整
  `FieldInterface` 參考。
- 第 6 章 [國際化](06-internationalization.md)，涵蓋 `Translatable` 欄位與 `[CmsTranslations]`。
- 第 7 章 [關聯](07-relations.md)，涵蓋 `[CmsRelation]`。
- 第 12 章 [認證、SSO 與 RBAC](12-auth-and-rbac.md)，用來授予新集合的權限。
- 第 13 章 [版本紀錄與軟刪除](13-revisions-and-soft-delete.md)，完整涵蓋 `Revisions` 與
  `ISoftDeletable`。
- 第 15 章 [部署、維運與測試](15-deployment-operations-testing.md)，涵蓋撰寫與套用正式環境
  migration。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，看看這整份檢查清單已經為 Blog 範例做過一遍是什麼
  樣子。
