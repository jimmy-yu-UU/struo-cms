# 5. 定義集合

想幫 StruoCMS 加一個新的內容型別，這一章示範一個 C# 類別要寫成什麼樣子，才能同時變成資料表、
REST／GraphQL 端點與後台表單。新增集合不會動到任何框架程式碼，只在你自己的內容專案裡加一個類
別；它的資料表也不用寫 migration 腳本——CodeFirst 會在任何後端、任何環境自動建出缺少的資料表。

## 最小集合

集合是一個掛 `[CmsCollection]`（`Struo.Domain.Metadata.Attributes` 底下）的類別，這個
attribute 只能掛在類別上一次。最小可編譯的集合只需要三樣東西：一個 `[SugarTable]`，一個帶
`[SugarColumn(IsPrimaryKey = true)]` 的屬性，以及至少一個 `[CmsField]`。繼承 `AuditableEntity`
是推薦寫法，不是必要條件——框架自帶的 `Language` 直接實作 `IAuditable`，搭配一個 `long` 自增主
鍵；範例的 `ArticleTag` 完全沒有基底類別。

下面的 `Announcement` 走推薦寫法，放在一個叫 `Acme.Content` 的內容專案裡；你自己的內容專案可以
取任何名字，只要參照 `Struo.Domain` 取得 attribute 與列舉，並自己加上 `SqlSugarCore` 套件參照
讓 `[SugarTable]`／`[SugarColumn]` 能解析——`Struo.Domain` 本身不帶任何套件參照。

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

    [CmsField(Label = "Published At", Interface = FieldInterface.DateTime, Sort = 4)]
    public DateTime? PublishedAt { get; set; }
}
```

`AuditableEntity` 把 `Id` 宣告成 `abstract`，忘記覆寫是編譯期錯誤；覆寫上要帶
`[SugarColumn(IsPrimaryKey = true)]`，因為 SqlSugar 認不出沒有屬性的繼承 `Id`。它同時供應
`CreatedAt`／`CreatedBy`／`UpdatedAt`／`UpdatedBy`，四個都不用加 `[CmsField]`——掃描器自動把
它們變成系統欄位。

`AuditableEntity` 另外還有一個樂觀鎖定用的 `Version` 欄位，掃描器完全不理它；它會出現在項目回應
裡，是因為 `ItemProjector` 直接在欄位迴圈外面寫出 `id` 與 `version`，而且只有繼承
`AuditableEntity` 的集合才有這個 `version` 鍵——像 `Language`、`ArticleTag` 這類集合的項目回應
裡就沒有它。

沒有 `[CmsField]`、也不叫那四個稽核名稱的屬性，掃描器直接略過；可以在同一個 entity 上放你自己
要用的一般屬性。新項目的主鍵由框架自己指派，是一個 version 7 的 GUID，不用自己產生。資料表名稱
統一是小寫、複數、snake_case，由 `[SugarTable]` 明寫；欄位名稱則照 SqlSugar 對 CLR 屬性名稱的
小寫轉換。

可為 null 的屬性（`DateTime?`、`Guid?`、`int?`，以及可為 null 的 `string?`）會自動對到可為 null
的欄位，CodeFirst 自己判斷，不需要再加 `[SugarColumn(IsNullable = true)]`；要反過來強制不可為
null，才需要明寫 `[SugarColumn(IsNullable = false)]`。

軟刪除與修訂記錄是兩個各自獨立的選用開關：讓集合實作 `ISoftDeletable`，或是在
`[CmsCollection]` 上設 `Revisions = true`，互不相關，可以只開一個，也可以都開。

欄位或關聯的名稱不能是以下五個詞，帶底線或不帶底線都算，比對不分大小寫，一共十個保留篩選詞：

- `and`
- `or`
- `some`
- `none`
- `junction`

取到保留詞，啟動時就會被 `MetadataException` 擋下來。

## `[CmsCollection]` 選項

`CmsCollectionAttribute` 一共七個成員：

| 屬性 | 型別 | 預設 | 作用 |
|---|---|---|---|
| `Label` | `string` | 建構子參數 | 側欄與頁面標題 |
| `Icon` | `string?` | `null` | 側欄圖示 |
| `Group` | `string?` | `null` | 側欄導覽分組 |
| `DefaultDisplayField` | `string?` | `null` | 項目顯示標題，符合條件時也是清單第一欄 |
| `AdminOnly` | `bool` | `false` | 一般 CRUD 寫入一律要求 super-admin |
| `Hidden` | `bool` | `false` | 從側欄隱藏 |
| `Revisions` | `bool` | `false` | 啟用修訂記錄 |

`DefaultDisplayField` 要指到一個真實欄位：掃描器把它 camelCase 化後對照已建好的欄位名稱，對不
上就在啟動時丟出 `MetadataException`。`AdminOnly = true` 只管一般 CRUD 的寫入，不論該集合的
RBAC 授予了什麼都要求 super-admin；讀取仍照一般 RBAC 走。`Hidden = true` 只影響側欄——REST、
GraphQL、`GET /api/schema` 與 RBAC 權限矩陣裡的那一列都不受影響，純粹是顯示層的旗標。
`Revisions = true` 是修訂記錄的完整開關：每次成功寫入都會在共用的 `revisions` 資料表補一筆快
照，任何一筆過去的快照都能還原（見[第 9 章](09-revisions-and-trash.md)）。

軟刪除不是這裡的選項：`CollectionMetadata.SoftDelete` 純粹看 entity 有沒有實作
`ISoftDeletable` 推導出來，不是 `[CmsCollection]` 的成員。一個全新的集合預設沒有任何 RBAC 授
權，在你自己加授權之前，只有 super-admin 能用它。

## `[CmsField]` 選項

`CmsFieldAttribute` 一共十三個成員，沒有建構子，全部用具名參數設定：

| 屬性 | 型別 | 預設 | 作用 |
|---|---|---|---|
| `Label` | `string?` | `null`→PascalCase 屬性名 | 欄位標籤 |
| `Interface` | `FieldInterface` | `Text`（明寫） | 決定欄位型別、GraphQL 型別、後台編輯器 |
| `Display` | `string?` | `null` | 沒有效果，完全不會被讀取 |
| `Required` | `bool` | `false` | 必填檢查（見下文） |
| `Searchable` | `bool` | `false` | 加入全文搜尋白名單 |
| `Sortable` | `bool` | `false` | 允許查詢 DSL 拿來排序 |
| `Sort` | `int` | `0` | 表單與清單裡的欄位順序 |
| `ReadOnly` | `bool` | `false` | 唯讀保護，並停用後台輸入 |
| `Hidden` | `bool` | `false` | 從 schema／API 隱藏，寫入不受影響 |
| `HelpText` | `string?` | `null` | 後台表單輸入下方的說明文字 |
| `Translatable` | `bool` | `false` | 併入翻譯 sidecar |
| `Group` | `string?` | `null` | 所屬的 `[CmsFieldGroup]` |
| `MaxLength` | `int` | `0`（未設定） | 輸入長度上限（見下文） |

`Interface` 沒有任何依 CLR 型別推斷的邏輯——一個 `int` 上掛 `[CmsField]` 卻不寫 `Interface`，
就是一個 `Text` 欄位，連帶套用 255 字元的後台上限；唯一依型別分支的地方是掃描器替四個稽核欄位
自動建立系統欄位時。

`Required` 在更新時驗證的是合併後的 entity，不是請求本文：省略某個欄位會沿用資料庫既有值並通
過檢查，明確傳入 `null` 或空字串才會失敗；建立時則一定要帶這個欄位。對一個不可為 null 的
`Guid` 欄位，`Guid.Empty` 也算缺漏，而且這種欄位本來就傳不了 `null`。

`Searchable` 把欄位加進集合的全文搜尋白名單；`Sortable` 讓它可以當查詢 DSL 的排序鍵；`Sort` 決
定它在後台表單與清單欄位裡的順序——依 `Sort` 由小到大排，同值再依宣告順序排，四個稽核欄位固定
排在 `Sort = 1000`，宣告了更大 `Sort` 值的欄位才會排在它們後面。

`HelpText` 顯示在後台表單輸入下方；`Group` 原樣寫入 metadata，不會被 camelCase 化，也不檢查是
否對應到已宣告的群組；`MaxLength` 是 CMS 層的輸入長度上限，跟資料庫欄位寬度無關，`0` 代表未
設定。`Display` 雖然存在，但沒有任何地方讀它，寫了也不會有效果。

欄位對外的鍵名是 CLR 屬性名稱的 camelCase 形式；省略 `Label` 時，標籤退回的是 **PascalCase**
的屬性名稱，不是人性化過的字串。

## 欄位群組與選項清單

`CmsFieldGroupAttribute` 掛在類別上，而且是整組六個 attribute 裡唯一可以重複掛的一個，成員是
`Name`（建構子參數）、`Label`、`Sort`。欄位用 `[CmsField(Group = "SameName")]` 加入群組。範例
的 `Article` 宣告了 `Content`、`SEO` 兩個群組：自己的欄位用 `Group = "Content"`，翻譯 sidecar
繼承的三個 SEO 欄位用 `Group = "SEO"`。

群組會回傳給 REST 與 GraphQL 呼叫端，但出廠的後台表單不照群組分區——它只把非系統欄位分成共用
與可翻譯兩桶，各自依 `Sort` 排序，可翻譯的那桶放進各語言分頁裡。宣告群組是替 API 呼叫端記錄結
構，不是用來切分表單。

`CmsOptionsAttribute` 接受 `params string[]`，每一項是 `"value:label"` 或單純的 `"value"`
（標籤退回用值本身）；切割只看**第一個**冒號，空白標籤一樣退回用值本身。空白的選項值會讓掃描
失敗。`[CmsOptions]` 只能掛在下列五種介面上，掛在別的介面上一樣是啟動失敗：

- `Select`
- `MultiSelect`
- `Radio`
- `CheckboxGroup`
- `Tags`

實務上前四種幾乎都要宣告選項清單，沒有選項就沒有值可選；`Tags` 通常留自由輸入。範例
`Article.Keywords`（`Tags`）沒有宣告選項，`Article.Regions`、`Article.Audiences` 都有；
`Article.Regions` 裡單純的 `"amer"` 項目，就是標籤退回用值本身的例子。

## 內容專案放哪、怎麼被找到

掃描涵蓋三個來源：框架自己的組件（永遠內含，七個框架集合就是這樣被找到的）、`Program.cs` 傳入
的主機組件，以及 `Struo:ContentAssemblies` 裡列出的每一個組件。清單裡的每個名稱都用
`Assembly.Load` 解析，解析不到就在啟動時丟出點名該項目的 `MetadataException`，不會被靜靜跳
過。要能解析到，通常代表 API 主機專案得有一個指到它的 `ProjectReference`——單純把 DLL 放在旁
邊不會被載入，專案參照跟設定項目兩者都要有。

內容專案是一個普通的類別庫，最低限度只需要參照 `Struo.Domain`（取得 attribute 與列舉）與
`SqlSugarCore` 套件（讓 `[SugarTable]`／`[SugarColumn]` 能用）。掃描只在啟動時跑一次，加了新
的內容專案要重啟才會生效；就算某個組件裡有型別載入失敗，掃描也只是拿到成功載入的那些型別，不
會整個中止。組件清單本身的讀取時機與完整的鍵語意見[第 4 章](04-configuration.md)。

## 建立資料表

CodeFirst 會建出集合自己的資料表、它的翻譯 sidecar 資料表，以及它宣告的任何多對多 junction 資
料表，在五種後端、Development 與 Production 都一樣；交給建表流程的型別集合，是所有掃描到的集
合的 entity／sidecar／junction 型別，加上十一個框架 entity 型別的聯集。建表本身不是靠開關做到
非破壞性，而是靠只把資料表還不存在的型別交出去——已經存在的資料表完全不會被動到。

要改動一張已經有資料的資料表，走的是 migration：`Database:MigrationsPath` 指到你自己準備、經
過審查的 SQL 腳本；`Database:AutoSyncSchema` 是只在 Development 生效的選項，讓 CodeFirst 直接
改動既有資料表的結構，其他環境會被忽略並記一筆警告。這兩個鍵的完整語意與預設值見
[第 4 章](04-configuration.md)。

## 檢查清單

新增一個集合，依序做這些事：

1. 內容程式庫放在 `src/Struo.*` 之外。
2. entity 帶 `[SugarTable]`、`[CmsCollection]`，並有一個帶屬性的主鍵。
3. 每個要曝露的屬性都加 `[CmsField]`，需要選項清單的介面加上 `[CmsOptions]`。
4. 需要的話實作 `ISoftDeletable`，或設 `Revisions = true`。
5. 有關聯的話加 `[CmsRelation]` + `[Navigate]`（見[第 8 章](08-relations.md)）。
6. 專案參照要有，`Struo:ContentAssemblies` 的項目也要有——兩者缺一不可。
7. 重啟行程，讓掃描抓到新集合。
8. 授予 RBAC 權限，不然只有 super-admin 能用它。

## 接下來

集合宣告好之後，下一步是把每個欄位的介面選對——這是[第 6 章](06-field-types.md)的主題。
