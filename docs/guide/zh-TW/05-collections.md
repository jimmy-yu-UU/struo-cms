# 5. 定義集合

想幫 StruoCMS 加一個新的內容型別，這一章示範一個 C# 類別要寫成什麼樣子，才能同時變成資料表、
REST／GraphQL 端點與後台表單。新增集合不會動到任何框架程式碼，只在你自己的內容專案裡加一個類
別；它的資料表也不用寫 migration 腳本——CodeFirst 會在任何後端、任何環境自動建出缺少的資料表。

## 最小集合

集合是一個掛 `[CmsCollection]`（`Struo.Domain.Metadata.Attributes` 底下）的類別，一個類別只
能掛一次。最小可編譯的集合只需要三樣東西：一個 `[SugarTable]`，一個帶
`[SugarColumn(IsPrimaryKey = true)]` 的屬性，以及至少一個 `[CmsField]`。繼承 `AuditableEntity`
是推薦寫法，不是必要條件——框架自帶的 `Language` 直接實作 `IAuditable`，搭配一個 `long` 自增主
鍵。範例的 `ArticleTag` 完全沒有基底類別。

下面的 `Announcement` 放在一個叫 `Acme.Content` 的內容專案裡，名字取什麼都行（專案要參照什麼
見本章後面）。這個範例比最低要求多了幾樣常用設定。

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

重啟 API 之後，這個類別就變成一張 `announcements` 資料表、側欄 Content 群組下的一個
Announcement、一組 REST 與 GraphQL 端點，以及一張有四個輸入的後台表單。剛宣告好的集合還沒有
任何 RBAC 授權，所以一開始只有 super-admin 看得到它。

`AuditableEntity` 把 `Id` 宣告成 `abstract`，忘記覆寫是編譯期錯誤；覆寫上要帶
`[SugarColumn(IsPrimaryKey = true)]`，因為繼承來的 `Id` 上沒有 attribute，SqlSugar 認不出它
是主鍵。它同時供應 `CreatedAt`／`CreatedBy`／`UpdatedAt`／`UpdatedBy`，四個都不用加
`[CmsField]`——掃描器自動把它們變成系統欄位。

`AuditableEntity` 還有一個樂觀鎖定用的 `Version`，不用加 `[CmsField]`。項目回應裡的 `version`
只有繼承 `AuditableEntity` 的集合才有，`Language`、`ArticleTag` 這類集合沒有。

沒有 `[CmsField]`、也不叫那四個稽核名稱的屬性，掃描器直接略過。可以在同一個 entity 上放你自
己要用的一般屬性。新項目的主鍵由框架自己指派，是 version 7 GUID，不用自己產生。資料表名稱統
一是小寫、複數、snake_case，由 `[SugarTable]` 明寫。欄位名稱則照 SqlSugar 對 CLR 屬性名稱的小
寫轉換。

可為 null 的屬性（`DateTime?`、`Guid?`、`int?`、`string?`）會自動對到可為 null 的欄位，
CodeFirst 自己判斷，不需要再加 `[SugarColumn(IsNullable = true)]`；要反過來強制不可為 null，
才需要明寫 `[SugarColumn(IsNullable = false)]`。

## `[CmsCollection]` 選項

`[CmsCollection]` 可以設的成員：

| 成員 | 型別 | 預設 | 作用 |
|---|---|---|---|
| `Label` | `string` | 建構子參數 | 側欄與頁面標題 |
| `Icon` | `string?` | `null` | 側欄圖示，名稱見下 |
| `Group` | `string?` | `null` | 側欄導覽分組 |
| `DefaultDisplayField` | `string?` | `null` | 項目顯示標題 |
| `AdminOnly` | `bool` | `false` | 一般 CRUD 寫入一律要求 super-admin |
| `Hidden` | `bool` | `false` | 只從後台側欄移除；REST、GraphQL、權限與 schema 不受影響 |
| `Revisions` | `bool` | `false` | 啟用版本紀錄 |

`DefaultDisplayField` 名稱要對得上某個欄位，比對時看 camelCase 之後的名字，對不上就在啟動時
丟出 `MetadataException`。

`AdminOnly = true` 只管一般 CRUD 的寫入，不論該集合的 RBAC 授予了什麼都要求 super-admin；讀取
仍照一般 RBAC 走。

軟刪除和版本紀錄是兩個各自獨立的開關：軟刪除讓集合實作 `ISoftDeletable`，版本紀錄在
`[CmsCollection]` 上設 `Revisions = true`。

### 側欄圖示怎麼填

`Icon` 的值會拿去查前端固定對照表 `frontend/src/lib/icons.ts` 裡的 `ICON_MAP`；能用的名稱
就是這張表目前有的鍵，例如 `article`、`tag`、`folder`、`megaphone`、`image`、`file`、
`user`、`table`。

查不到的名稱、或整個沒填，畫面一律退回通用的檔案圖示，側欄不會因此壞掉。想用表裡沒有的圖
示，得先在 `ICON_MAP` 加一個新的鍵——這是前端要改的地方，屬於管理後台 SPA 客製化的範圍。

## `[CmsField]` 選項

`[CmsField]` 的每個設定都用具名參數寫：

| 成員 | 型別 | 預設 | 作用 |
|---|---|---|---|
| `Label` | `string?` | `null` | 欄位標籤，未設定時用 PascalCase 屬性名 |
| `Interface` | `FieldInterface` | `Text` | 決定欄位型別、GraphQL 型別、後台編輯器 |
| `Display` | `string?` | `null` | 沒有效果 |
| `Required` | `bool` | `false` | 必填檢查（見下文） |
| `Searchable` | `bool` | `false` | 加入全文搜尋白名單 |
| `Sortable` | `bool` | `false` | 後台清單表頭可點排序 |
| `Sort` | `int` | `0` | 表單與清單裡的欄位順序 |
| `ReadOnly` | `bool` | `false` | 唯讀保護，並停用後台輸入 |
| `Hidden` | `bool` | `false` | 從 schema／API 隱藏，寫入不受影響 |
| `HelpText` | `string?` | `null` | 後台表單輸入下方的說明文字 |
| `Translatable` | `bool` | `false` | 併入翻譯 sidecar |
| `Group` | `string?` | `null` | 所屬的 `[CmsFieldGroup]` |
| `MaxLength` | `int` | `0`（未設定） | 輸入長度上限（以 UTF-16 code unit 計） |

- `Interface` 沒有任何依 CLR 型別推斷的邏輯——一個 `int` 上掛 `[CmsField]` 卻不寫
  `Interface`，就是一個 `Text` 欄位，不是 `Number`。
- `Sort` 決定它在後台表單與清單欄位裡的順序——依 `Sort` 由小到大排，同值再依宣告順序排，四
  個稽核欄位固定排在 `Sort = 1000`，宣告了更大 `Sort` 值的欄位才會排在它們後面。
- `MaxLength` 是 CMS 層的輸入長度上限，跟資料庫欄位寬度無關。

`Required` 在更新時驗證的是合併後的 entity，不是請求本文：省略某個欄位會沿用資料庫既有值並
通過檢查，明確傳入 `null` 或空白字串（含只有空格）才會失敗；建立時則一定要帶這個欄位。對一個
不可為 null 的 `Guid` 欄位，`Guid.Empty` 也算缺漏，而且這種欄位本來就傳不了 `null`。

`Hidden = true` 的欄位不會進搜尋白名單，即使設了 `Searchable`。`Sortable` 決定後台清單的表頭
能不能點著排序；查詢語法的 `sort` 不看這個旗標，任何已知欄位都排得動。

欄位對外的鍵名是 CLR 屬性名稱的 camelCase 形式；退回 PascalCase 屬性名時，不會自動加空格或
改大小寫。

### 名稱限制

欄位和關聯的名稱不能用這五個詞，前面加不加底線都不行（`and` 和 `_and` 都算），比對不分大小
寫——這些字查詢語法自己要用：

- `and`
- `or`
- `some`
- `none`
- `junction`

用到保留詞，啟動時就會被 `MetadataException` 擋下來。

## 欄位群組與選項清單

`[CmsFieldGroup]` 掛在類別上，同一個類別可以掛多次，一次宣告一個群組。成員是 `Name`（建構子
參數）、`Label`、`Sort`。欄位用 `[CmsField(Group = "SameName")]` 加入群組。範例的 `Article`
宣告了 `Content`、`SEO` 兩個群組：自己的欄位用 `Group = "Content"`，翻譯 sidecar 繼承的三個
SEO 欄位用 `Group = "SEO"`。

群組會回傳給 REST 與 GraphQL 呼叫端，但預設的後台表單不照群組分區——它只把非系統欄位分成共
用和可翻譯兩組，各自依 `Sort` 排序，可翻譯的那組放進各語言分頁裡。

`CmsOptionsAttribute` 接受 `params string[]`，每一項是 `"value:label"` 或單純的 `"value"`
（標籤退回用值本身）；切割只看**第一個**冒號。空白的選項值會讓掃描失敗。`[CmsOptions]` 只能
掛在下列五種介面上，掛在別的介面上一樣是啟動失敗：

- `Select`
- `MultiSelect`
- `Radio`
- `CheckboxGroup`
- `Tags`

實務上前四種幾乎都要宣告選項清單，沒有選項就沒有值可選；`Tags` 通常留自由輸入。範例
`Article.Keywords` 是 `Tags`，沒有宣告選項；`Article.Regions`、`Article.Audiences` 都有。

## 內容專案放哪、怎麼被找到

掃描一律涵蓋 API 主機專案本身與框架自己的組件，不需要任何設定——內容類別可以直接寫在主機
專案裡。想放進一個獨立的類別庫也行，主機專案要有指到它的 `ProjectReference`，而且要把組件
名稱列進 `Struo:ContentAssemblies`——兩者缺一不可，單純把 DLL 放在旁邊不會被載入。放主機專
案裡還是獨立類別庫，選哪一種是你的事。解析不到列出的組件就啟動失敗，訊息會點名那個項目
（見[第 4 章：設定參考](04-configuration.md)）。

內容專案是一個普通的類別庫，最低限度只需要參照 `Struo.Domain`（取得 attribute 與列舉）與
`SqlSugarCore` 套件（讓 `[SugarTable]`／`[SugarColumn]` 能用）。掃描只在啟動時跑一次，加了新
的內容專案要重啟才會生效；就算某個組件裡有型別載入失敗，掃描也只是拿到成功載入的那些型別，
不會整個中止。

## 建立資料表

CodeFirst 會建出集合自己的資料表、它的翻譯 sidecar 資料表，以及它宣告的任何多對多 junction
資料表，在五種後端、Development 與 Production 都一樣。框架自己的資料表和你的集合一起建，缺
哪張就建哪張。建表只會處理還不存在的資料表——已經存在的資料表完全不會被動到。

要改動一張已經有資料的資料表，走的是 migration：`Database:MigrationsPath` 指到你自己準備、
經過審查的 SQL 腳本；`Database:AutoSyncSchema` 只在 Development 有用，可以讓 CodeFirst 直接
改既有資料表；其他環境設了也不會生效。這兩個鍵的完整語意與預設值見
[第 4 章：設定參考](04-configuration.md)。

## 檢查清單

新增一個集合，依序做這些事：

1. 決定內容類別放哪：直接放進 API 主機專案，或放進主機參照的類別庫。
2. 在 entity 上掛 `[SugarTable]`、`[CmsCollection]`，並有一個掛了
   `[SugarColumn(IsPrimaryKey = true)]` 的主鍵。
3. 幫每個要曝露的屬性加 `[CmsField]`，需要選項清單的介面加上 `[CmsOptions]`。
4. 需要的話實作 `ISoftDeletable`，或設 `Revisions = true`。
5. 有關聯的話加 `[CmsRelation]` 和 `[Navigate]`（見[第 8 章：關聯](08-relations.md)）。
6. 加專案參照，並把項目加進 `Struo:ContentAssemblies`（獨立類別庫才需要）。
7. 重啟行程，讓掃描抓到新集合。
8. 授予 RBAC 權限，不然只有 super-admin 能用它。

## 接下來

集合宣告好之後，下一步是把每個欄位的介面選對——這是
[第 6 章：欄位型別與編輯介面](06-field-types.md)的主題。
