# 8. 關聯

一個集合怎麼參照另一個集合、參照到的資料要怎麼讀出來，以及目標被刪除時該連動什麼，是這一章的主題。

## 三種關聯

每個關聯宣告在一個 navigation 屬性上，要同時掛兩個 attribute：SqlSugar 的 `[Navigate]` 決定 join 的
形狀，`[CmsRelation]` 負責其餘 CMS 的設定。兩個都要有，缺一個就會被掃描器整個跳過，不是錯誤。
`RelationKind` 只有三個值——`ManyToOne`、`OneToMany`、`ManyToMany`——掃描器依屬性的形狀自己推斷：
純量或可為 `null` 的純量參照是多對一；清單屬性預設是一對多，只有當 `[Navigate]` 的第一個參數是一個型
別時才算多對多。

### 多對一——`File.Folder`

框架與範例裡的多對一一律用 `NavigateType.OneToOne`，一對多一律用 `NavigateType.OneToMany`；但真正決
定種類的不是這個常數——掃描器只看屬性是不是清單，以及 `[Navigate]` 的第一個參數是不是一個型別。框架
自己的多對一例子是 `File.Folder`：

```csharp
[SugarColumn(IsNullable = true)]
public Guid? FolderId { get; set; }

[Navigate(NavigateType.OneToOne, nameof(FolderId))]
[CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
[SugarColumn(IsIgnore = true)]
public MediaFolder? Folder { get; set; }
```

`MediaFolder.Parent` 宣告一模一樣的樣式，用來模擬資料夾的樹狀結構。

多對一一定要能解出外鍵，目標集合也一定要真的存在，否則啟動失敗：`M2O relation
'{collection}.{relation}' has no foreign key.`、`Relation '{collection}.{relation}' targets unknown
collection '{target}'.`

自我參照的多對一（例如 `MediaFolder.Parent`）在 metadata 上會多一個 `SelfReferencing` 旗標，寫入時另
外有一道循環守衛：沿著新父層的祖先鏈往上走，即使某個祖先已經在垃圾桶裡也算數，會形成循環就拒絕更新，
建立不受影響。這道守衛自己的走訪上限是 64 層，純粹是防呆。這道守衛跟查詢語法的關聯深度上限無關。

### 一對多——範例的 `Category.Articles`／`Category.Children`

一對多的現成例子在範例裡——`Category.Children` 與 `Category.Articles`；同樣的宣告方式套用在自己的集
合上一樣成立：

```csharp
[Navigate(NavigateType.OneToMany, nameof(ParentId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Category> Children { get; set; } = [];

[Navigate(NavigateType.OneToMany, nameof(Article.CategoryId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
[SugarColumn(IsIgnore = true)]
public List<Article> Articles { get; set; } = [];
```

一對多本身不帶外鍵：metadata 的 `ForeignKey` 是從子項那一端 `[Navigate]` 參數讀出來的反向外鍵屬性名
稱，查詢層靠這個名字知道要用目標集合的哪一欄回頭篩選。

### 多對多——`User.Roles`

框架自己的多對多是 `User.Roles`，透過 `UserRole` 做 junction：

```csharp
[Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
[CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Role> Roles { get; set; } = [];
```

寫入時多對多接受的是目標 id 的純陣列，鍵是關聯自己的 camelCase 名稱；陣列裡每個 id 都會先確認存在於
目標集合，不存在就整批拒絕：`One or more ids in '{relation}' do not exist in '{target}'.`

同步邏輯是跟現有 junction 列做差異比對：目標消失的列刪掉，新目標的列新增，留下來的目標則保留原本的
junction 主鍵，不會整批砍掉重建。三種情況各自的處理是：

- 關聯鍵沒有出現在本文裡：整個跳過
- 鍵存在但值不是陣列：也跳過
- 顯式送 `[]`：會刪光那個關聯的每一筆 junction 列

多對一的外鍵寫入時完全不檢查存在與否，只有多對多的目標 id 會檢查。這一層 schema 也沒有資料庫層級的外
鍵限制：`OnDelete` 跟每一項參照檢查都在應用層做，自己的 ETL 或直接操作資料庫都繞得過，也可能留下懸空
的子列。關聯在 metadata 上的標籤一律是 PascalCase 的 CLR 屬性名稱，沒有任何 attribute 成員可以
覆寫它。

## `[CmsRelation]` 選項

`[CmsRelation]` 的每個設定都是具名參數，沒有建構子。前四個決定怎麼顯示、怎麼挑目標：

| 成員 | CLR 型別 | 預設值 | 效果 |
|---|---|---|---|
| `Interface` | `RelationInterface` | `Dropdown` | 決定後台用哪個 picker |
| `DisplayTemplate` | `string?` | `null` | `{欄位}` 樣板，解析不出來退回 id |
| `PickerQuery` | `string?` | `null` | 存進 metadata，後台目前沒有讀取 |
| `SortField` | `string?` | `null` | 僅多對多；junction 的排序欄 |

後四個決定寫入與刪除時的行為：

| 成員 | CLR 型別 | 預設值 | 效果 |
|---|---|---|---|
| `OnDelete` | `OnDelete` | `Restrict` | 僅多對一 |
| `Editable` | `bool` | `true` | 後台 picker 是否接受輸入 |
| `DisplayColumns` | `string?` | `null` | 沒有效果 |
| `MaxDepth` | `int` | `1` | 沒有效果 |

`DisplayColumns` 與 `MaxDepth` 都不會被讀進 `RelationMetadata`，也到不了 `/api/schema`。沒有逐關聯覆
寫深度上限的欄位，唯一生效的上限是全域的 `Query:MaxRelationDepth`，見「用 `deep` 讀取關聯資料」
一節。

## junction entity 與 payload

junction 是一個普通的 SqlSugar entity，不必是 `[CmsCollection]`；是不是多對多看擁有端的
`[Navigate(typeof(Junction), ...)]`，junction 型別本身不帶 `[CmsRelation]`。

每一個 junction 型別都必須宣告剛好一個 `[SugarColumn(IsPrimaryKey = true)]` 屬性——同步邏輯靠這個主
鍵更新 junction 列，複合鍵或缺主鍵都會在啟動時失敗：`Junction type '{type}' (used by
'{owner}.{relation}') must declare exactly one [SugarColumn(IsPrimaryKey = true)] property; the M2M
sync updates junction rows by primary key.`

junction 上兩個外鍵的唯一性不會自動推導：`UserRole` 自己額外宣告了一組唯一索引，範例的 `ArticleTag`
沒有——沒寫就沒有資料庫層級的重複保護。

當 junction 型別同時掛 `[CmsCollection]`，它就成為一個 junction collection，也就是一個可讀寫的
collection：除了兩個外鍵、`SortField` 指到的欄位，以及任何 `IsSystem`／`ReadOnly` 欄位之外，它自己的
`[CmsField]` 全部算進這個關聯的 payload——屬於這條連結本身、不屬於任何一端的資料。這份清單只在一個
地方算出來；同一份 payload 清單在寫入、`_junction` 投影、版本快照與 GraphQL links 上都是一致的。

junction collection 的兩個外鍵都必須宣告成可寫入的 `[CmsField]`，否則啟動失敗；要求的是可寫入，不是
特定介面——`UserRole` 兩個都用 `Text`，範例的 `ArticleTag` 用 `Uuid`：`Junction collection
'{junction}' (used by '{owner}.{relation}') must declare its foreign keys '{fkA}' and '{fkB}' as
writable [CmsField]s (e.g. Interface = FieldInterface.Uuid); otherwise items created through the API
store empty keys.`

跟集合層的 `Hidden`（見[第 5 章：定義集合](05-collections.md)）一樣，只讓它從後台側邊欄消失：它仍然
完整出現在 `/api/schema`、RBAC 權限表與產生的 GraphQL schema 裡。`UserRole` 另外掛了
`AdminOnly = true`，範例的 `ArticleTag` 沒有，兩者誰能寫入因此不同。關聯的 schema 項目會點名
junction collection、它的 payload 欄位名稱與排序欄位，這是呼叫端發現「還需要 junction collection 自
己的寫入授權」的方式。

範例的 `ArticleTag` 就是這樣一個 junction collection：

```csharp
[CmsCollection("Article tag", Icon = "link", Group = "Content", Hidden = true)]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Article", Interface = FieldInterface.Uuid, Required = true, Sort = 1)]
    public Guid ArticleId { get; set; }

    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid, Required = true, Sort = 2)]
    public Guid TagId { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Note", Interface = FieldInterface.Text, MaxLength = 200, Sort = 3)]
    public string? Note { get; set; }

    [CmsField(Label = "Sort", Interface = FieldInterface.Number, Sort = 4)]
    public int Sort { get; set; }
}
```

這四個欄位裡只有 `Note` 算 payload：兩個外鍵是連結本身，`Sort` 是 `Article.Tags` 的 `SortField`。
`ArticleTag` 另外掛了 `Hidden = true`。payload 欄位的 `MaxLength` 錯誤會帶關聯與目標前綴：`Relation
'tags', target '<id>': Field 'note' exceeds maximum length 200.`

GraphQL 另外提供 `<rel>Links: [<Parent><Rel>Link!]`，讀的是同一份 payload，每一筆是 `{ node,
junction }`，原本的 `<rel>` 欄位不受影響。

寫入的陣列裡每個元素可以是純 id（只連結，不動 payload），也可以是物件 `{ id, ...payload }`（連結並合
併指名的 payload 欄位），其他形狀一律拒絕。同一個 id 出現不只一次時，物件贏過純 id，較晚的物件贏過較
早的物件，排序依第一次出現的位置。junction 的排序欄會被改寫成該元素在合併後清單裡的索引，陣列順序就
是排序。

junction payload 從不檢查 `Required`：元素沒帶的欄位維持原值。junction 的寫入授權——junction
collection 本身的寫入授權，加上 `AdminOnly` 時額外要求的超級管理員——在任何 payload 值被綁定之前就
先檢查，沒有授權的呼叫端會拿到 403，不會漏到 payload 自己的欄位規則產生 400。

## `OnDelete` 的每個值

`OnDelete` 決定刪除目標時要連動什麼。丟進垃圾桶與清除（帶 `?purge` 的永久刪除）是兩個不同的時機，
下表的行為都以清除為準。`OnDelete` 只對多對一關聯生效，宣告在一對多或多對多上會被忽略。

| 值 | 行為 |
|---|---|
| `Restrict`（預設） | 仍有參照時拒絕刪除，回 `409 CONFLICT` |
| `SetNull` | 清除前把每一列參照它的外鍵設成 `null` |
| `Cascade` | 遞迴清掉每一列參照它的資料 |

`Restrict` 的訊息點名集合：`Cannot delete '{collection}/{id}': referenced by
'{sourceCollection}'.`，同一道守衛在單純丟進垃圾桶時也會跑，清除的每一層遞迴也會跑。這道檢查不鎖
列，兩個交易之間仍可能競速出一列新的參照。

junction entity 自己宣告的多對一（例如指向第三個集合）也算一般的多對一：預設 `Restrict`，所以只要還
有 junction 列參照那個目標，就刪不掉它。

`SetNull` 只在清除時發生，單純丟進垃圾桶不會動到參照它的列；範例的 `Article.Category` 與
`Category.Parent` 都宣告 `OnDelete.SetNull`。

`Cascade` 連帶清掉的還有那些列各自的 junction、翻譯與版本紀錄，循環參照不會讓它無限遞迴。參照它的
列讀取時繞過軟刪除篩選，已經在垃圾桶裡的參照者一樣會被清掉，不會被跳過。
框架與範例都沒有任何關聯宣告 `Cascade`。

## 用 `deep` 讀取關聯資料

展開關聯的參數只有一個：`deep`。展開是逐關聯、逐頁批次做的——每個關聯一次額外查詢，不是逐列各查一次，
也刻意不用 ORM 自己的 eager-include：多對一一次查詢，一對多一次，多對多兩次（junction 列一次、
目標一次）。

查詢字串形式是單層、只列關聯名稱：`?deep=folder,tags`，用逗號分隔；每個關聯的欄位挑選、巢狀篩選、排
序、`limit`／`offset`，以及更深一層的巢狀，只存在於查詢端點的 JSON 封裝形式裡。對多對一關聯帶篩選、
排序、`limit` 或 `offset` 是錯誤，不是靜靜忽略：`filter/sort/limit/offset are only supported on
to-many relations; '{relation}' on '{collection}' is many-to-one.`

巢狀的 `limit` 會被夾到 `Query:MaxLimit`；沒給巢狀 `limit` 時，每個父列的這個關聯會回傳全部列——展
開一個沒給上限的一對多或多對多關聯，是沒有界限的。

逐父列的排序與分頁是在已經取回的資料上、於記憶體裡做的，這正是批次抓取不會有 N+1 問題的原因；`null`
排最前，比不出大小的值視為相等以維持穩定順序。沒有巢狀排序時，一對多維持抓取順序，多對多維持
junction 順序——關聯沒有宣告 `SortField`、或該欄位不是數字時，會退化成插入順序，而不是失敗。

整棵 `deep` 樹在任何查詢真的執行之前就會先驗證完，跟列數無關，就算頁面是空的，深度超過或關聯名稱不存
在一樣會失敗：`Relation nesting too deep (depth {n}); the maximum is {max}.`、`Unknown relation
'{name}' on '{collection}'.` 巢狀 `limit`／`offset` 給負值會被拒絕，巢狀排序如果帶點號路徑（跨關聯）
也會被拒絕。關聯路徑的深度上限由 `Query:MaxRelationDepth` 控制，預設 6，設定方式見
[第 4 章：設定參考](04-configuration.md)。

`deep` 跟帶點號的篩選／排序路徑都需要沿路每個集合的讀取授權，但沒有授權時的下場不同：`deep` 靜靜略過
讀不到的關聯，帶點號的篩選或排序路徑則直接拒絕：`Read not permitted on '{target}'.`，而且這個拒絕在
路徑真的被解析之前就發生，不會被拿來反推一個讀不到的集合有哪些欄位。修剪先於驗證：一個讀不到的關聯上
的巢狀篩選或排序根本不會拿去對集合的 metadata 驗證；如果修剪把每個要求的關聯都拿掉，請求照樣成功，只
是沒有展開，也沒有錯誤。

一對多展開長這樣，`articles` 是一個陣列，元素是完整的項目投影：

```text
$ GET /api/items/category/01a08c68-5681-7e60-ade2-3f277347cc19?deep=articles
{"success":true,"data":{"id":"01a08c68-5681-7e60-ade2-3f277347cc19","version":0,"name":"Tutorials","createdAt":"2026-09-10T17:40:43.266118","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.266208","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","articles":[{"id":"01a08c68-57d6-7f25-80e7-c42a2a1702a4","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-10T17:40:43.607052","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.607133","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858"}]}}
HTTP_STATUS:200
```

當 `deep` 展開一個 junction 帶 payload 的多對多關聯，每一列展開出來的目標都會多一個 `_junction`
物件，裝著那一列非 `Hidden` 的 payload 值，鍵是欄位的 camelCase API 名稱；junction 不帶 payload
的關聯完全不會有這個鍵。

呼叫端沒有 junction collection 的讀取授權時，`_junction` 是整個省略，不是送 `null`，跟 `deep` 對讀不
到的關聯的作法一致。`_junction` 是一個固定的保留鍵，不是從 junction collection 自己的名稱推出來的，
也只有透過 `deep` 才碰得到。

帶 payload 的多對多展開長這樣，`tags` 的每一筆多一個 `_junction`：

```text
$ GET /api/items/article/01a08c68-57d6-7f25-80e7-c42a2a1702a4?deep=tags
{"success":true,"data":{"id":"01a08c68-57d6-7f25-80e7-c42a2a1702a4","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-10T17:40:43.607052","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.607133","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","tags":[{"id":"01a08c68-56ed-7736-9b4a-bdd351d6b95b","version":0,"name":"howto","createdAt":"2026-09-10T17:40:43.374315","createdBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","updatedAt":"2026-09-10T17:40:43.374443","updatedBy":"019f1794-82d6-70b2-8e07-e7ecdf37b858","_junction":{"note":"primary tag"}}],"translations":{"en":{"title":"Getting started with StruoCMS","body":"<p>Run the API and the admin SPA.</p>","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用 StruoCMS","body":"<p>先把 API 與後台跑起來。</p>","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}}
HTTP_STATUS:200
```

## 關聯的篩選（概要）

篩選欄位路徑帶一個點號，就是一條關聯路徑，整個條件會被推進巢狀子查詢，不會先撈出 id 集合，所以多帶幾
個關聯條件不會讓查詢次數變多。路徑裡點到不存在的關聯，跟點到不存在的欄位一樣被拒絕：`Unknown
relation '{name}' on '{collection}' in path '{path}'.`

兩條帶點號的條件可以分別由不同的關聯列滿足；要求它們落在同一列，要用「至少一個」／「一個都沒有」的量
詞式寫法。junction 自己的 payload 也有另一種篩選寫法，而排序跨關聯只在整條路徑都是多對一時才成立。完
整的篩選與排序語法留給查詢語法與進階查詢兩章。

## 後台的關聯 picker 與 junction 列表編輯器

`RelationInterface` 只有四個值：`Dropdown`、`TagSelect`、`TreeSelect`、`RelatedList`，後台每一個都有
對應的輸入元件。

- `Dropdown`：單值下拉選單，對目標集合做去抖動的搜尋。
- `TreeSelect`：單值的樹狀選擇器，樹是從一個自我參照的多對一關聯建出來的。
- `RelatedList`：唯讀、分頁、延遲載入的目標集合清單，依反向外鍵篩選；每一列點進去是該列自己的編輯頁
  ，只有父列存過之後才會出現。
- `TagSelect`：依 junction 是否帶可見 payload 或 `SortField` 分成兩種樣子，見下段。

`TagSelect` 在關聯的 junction 沒有可見 payload、也沒有 `SortField` 時，是單純的多值標籤選擇器；只要
兩者有一個成立，就換成逐列的連結編輯器。逐列編輯器裡，對每個已選目標各顯示一列，內容是：

- 那個連結可見的 payload 欄位（用跟項目表單相同的欄位型別 registry 渲染）
- 一個移除按鈕
- 只有宣告 `SortField` 才會出現的排序控制

底下的下拉框只負責新增與移除成員。

授權分三級：

- 沒有 junction 的讀取授權：列還是會出現，但不會顯示任何 payload 欄位——伺服器本來就省略了
  `_junction`，沒有值可以顯示。
- 有讀取但沒有寫入授權（或 junction 是 `AdminOnly` 而呼叫端不是超級管理員）：payload 欄位唯讀，儲存
  只送出裸 id，只有成員與（如果有 `SortField`）順序會被更新。
- 除此之外：payload 才是可編輯的。

父層表單自己的停用狀態贏過以上全部。

伺服器端的 payload 驗證錯誤會落在表單最上層的錯誤訊息；必填與長度上限則在送出前由前端先擋一次。數字
或布林的 payload 欄位留白時存的是 `null`，不是空字串，這種 junction 欄位要嘛宣告成可為 `null`，要嘛
標成 `Required`——表單會在送出前擋下空白的必填欄位。隱藏的 junction payload 欄位在後台完全不會出現
可編輯的版本，因為 API 本來就不會把它送進 `_junction`。

## 接下來

關聯讀寫都清楚之後，下一步是版本怎麼保留、軟刪除的東西怎麼找回來——這是
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)的主題。
