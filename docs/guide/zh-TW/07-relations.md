# 7. 關聯

一個關聯，把某個集合 (collection) 的資料列連結到另一個集合的資料列。每一個關聯都是透過一個
navigation 屬性上的一對 attribute 來宣告的:SqlSugar 自己的 `[Navigate]` (join 是哪一邊/哪一種
形狀) 與框架的 `[CmsRelation]` (所有 CMS 專屬的部分——picker UI、顯示方式、cascade 行為)。
`MetadataScanner.ScanRelations` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) 要求一個
屬性必須**同時**帶有這兩個 attribute，才會被辨識為一個關聯;只有其中一個時會被忽略。

## 支援的種類

`RelationKind` (`src/Struo.Domain/Metadata/Enums/RelationKind.cs`) 恰好有三個值:`ManyToOne`、
`OneToMany`、`ManyToMany`。掃描器會依屬性自身的形狀，推斷適用哪一種——一個純量 (或可為 null 的
純量) 參照永遠是 `ManyToOne`;一個 `List<T>`/可列舉屬性則是 `OneToMany`，除非 `[Navigate]` 的
建構函式指名了一個 junction 型別，此時才是 `ManyToMany`。

### Many-to-one——`File.Folder`

框架自身的 `File` 集合宣告了一個真正、真實存在的 many-to-one 關聯，指向 `MediaFolder`:

```csharp
// src/Struo.Infrastructure/Files/File.cs (excerpt)
[SugarColumn(IsNullable = true)]
public Guid? FolderId { get; set; }

[Navigate(NavigateType.OneToOne, nameof(FolderId))]
[CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
[SugarColumn(IsIgnore = true)]
public MediaFolder? Folder { get; set; }
```

(SqlSugar 自己的 `NavigateType.OneToOne` 常數，同時被 many-to-one 與 one-to-many 兩種
navigation 屬性使用——決定究竟是 `ManyToOne` 還是 `OneToMany` 的是掃描器本身，而不是
`NavigateType` 的值，依據的是這個屬性是純量還是清單。) 外鍵 (`folderId`) 是擁有方上一個一般的、
可為 null 的 `Guid` 欄位;`[CmsRelation]` 的 `DisplayTemplate = "{Name}"`，說明了在 picker 與
breadcrumb 中要為一個已解析的目標顯示什麼。`MediaFolder` 自身在下一層宣告了完全相同的模式，以
自我參照的方式來塑模一棵資料夾樹:

```csharp
// src/Struo.Infrastructure/Files/MediaFolder.cs (excerpt)
[Navigate(NavigateType.OneToOne, nameof(ParentId))]
[CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
[SugarColumn(IsIgnore = true)]
public MediaFolder? Parent { get; set; }
```

此處 `RelationMetadata.SelfReferencing` 是 `true` (目標型別與宣告型別相同)。一個自我參照的
many-to-one，在**寫入**時還會受到額外的把關:`SelfReferenceCycleGuard`
(`src/Struo.Application/Query/Write/SelfReferenceCycleGuard.cs`) 會走訪傳入的父層自身的祖先鏈
(略過軟刪除過濾器，因為一個已在回收桶中的祖先，其外鍵仍然算數)，並拒絕任何會閉合成一個循環的
更新:

```
$ curl -s -X PUT http://localhost:5221/api/items/mediafolder/<docs-id> \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"name":"Docs","parentId":"<guides-id>"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'parentId' would create a cycle in 'mediafolder'."}}
```

(此處的 `<guides-id>` 原本就已經是 `<docs-id>` 的子項，所以把 `Docs` 重新掛到 `Guides` 底下，
會閉合成一個兩節點的迴圈。) 這個守衛自己內建了一個 64 層的走訪上限，純粹是為了防禦既存的損毀
資料——這與本章稍後涵蓋的查詢 DSL 關聯路徑深度上限，是兩個互不相關的數字。

### One-to-many——範例的 `Category.Articles`/`Category.Children`

目前沒有任何框架集合宣告 one-to-many 關聯——所有七個本身就是 `[CmsCollection]` 的框架集合，都只
使用 many-to-one 與 many-to-many。`Struo.Sample.Blog` 示範專案 (第 16 章) 完整展示了這個模式，
而同樣的宣告方式，在你自己 fork 的集合中也能一模一樣地運作:

```csharp
// samples/Struo.Sample.Blog/Category.cs (excerpt)
[Navigate(NavigateType.OneToMany, nameof(ParentId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Category> Children { get; set; } = [];

[Navigate(NavigateType.OneToMany, nameof(Article.CategoryId))]
[CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
[SugarColumn(IsIgnore = true)]
public List<Article> Articles { get; set; } = [];
```

一個 one-to-many 在它自己宣告的那一側完全不帶外鍵——`RelationMetadata.ForeignKey` 改為**反向**
外鍵屬性名稱，讀取自子項 (目標) 那一側 `[Navigate]` 的引數 (`Articles` 對應到 `categoryId`，
`Children` 對應到 `parentId`)，這樣查詢層才知道要以目標集合的哪一欄來過濾。已用一組真實的
`Category`/`Article` 資料驗證 (範例暫時選用啟用，僅供這項檢查使用):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/category/<engineering-id>?deep=articles"
{"success":true,"data":{"id":"<engineering-id>","name":"Engineering", ...,
  "articles":[{"id":"<article-id>","status":"published", ...}]}}
```

### Many-to-many——`User.Roles`

框架自身的使用者↔角色指派，是一個真正、真實存在的 many-to-many 關聯，透過 `UserRole` entity
做 junction:

```csharp
// src/Struo.Infrastructure/Identity/User.cs (excerpt)
[Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
[CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Role> Roles { get; set; } = [];
```

`[Navigate]` 的第一個引數是一個 `Type` (而不是 `NavigateType`)，正是這一點告訴掃描器這是
`ManyToMany` 而不是 `OneToMany`，即使兩者都宣告在一個 `List<T>` 屬性上。寫入端接受一個以關聯
自身 camelCase 名稱為鍵值的純目標 id 陣列——`"roles": ["<role-id>", ...]`——而
`ItemWriteSideSync.SyncM2MAsync` 會先驗證每一個 id 都存在於目標集合中，才把陣列交給
`ManyToManySync`，由它把陣列與該父項現有的 junction 資料列做差異比對:不再存在的目標，其資料列
會被刪除;新增的目標，其資料列會被插入;維持不變的目標，其資料列則保留自己的主鍵，不會被砍掉
重建。一個未知的 id 會被拒絕為 `"One or more ids in '{relation}' do not exist in '{target}'."`，
而一個重複的 id 會被靜默去重 (junction 本身是一個 set)。當 junction 本身也帶有 payload 時，
寫入端有更豐富的寫入形狀，見下一節。

## `[CmsRelation]` 屬性

`CmsRelationAttribute` (`src/Struo.Domain/Metadata/Attributes/CmsRelationAttribute.cs`) 宣告了:

| 屬性 | 型別 | 說明 |
|---|---|---|
| `Interface` | `RelationInterface` | 決定套用哪一種管理後台 SPA 的 picker/顯示元件 (見下文);預設為 `Dropdown`。 |
| `DisplayTemplate` | `string?` | 一個 `{FieldName}` 風格的樣板，用來把一個已解析的目標資料列渲染成標籤 (picker、breadcrumb、`RelatedList` 資料列)。 |
| `PickerQuery` | `string?` | 會被收錄進中介資料，但目前在管理後台 SPA 的 picker 元件中完全沒有被讀取——出貨的 `RelationPicker`/`RelatedList`，一律不加篩選地查詢目標集合 (除了使用者輸入的 `search=` 詞彙之外)，`RelatedList` 則再加上父項自身的反向外鍵過濾。 |
| `SortField` | `string?` | 僅適用於 many-to-many:用來排序目標資料列的 junction 欄位 (`RelationExpander.JunctionSortKey`);若未設定或非數值，則退回插入順序。 |
| `OnDelete` | `OnDelete` | 當一個 many-to-one 的*目標*被刪除時的 cascade 行為 (見下文);預設為 `Restrict`。 |
| `Editable` | `bool` | 決定管理後台項目表單中，此關聯的 picker 是否接受輸入;預設為 `true`。 |
| `DisplayColumns` | `string?` | 宣告在 attribute 上，但完全沒有被讀進 `RelationMetadata` 或 `src/` 中任何其他地方——目前沒有任何可觀察到的效果 (與第 4 章對 `[CmsField(Display = ...)]` 提出的同一種未使用屬性但書;請不要依賴它)。 |
| `MaxDepth` | `int` (預設 `1`) | 同樣有宣告，但從未被讀進 `RelationMetadata`——目前沒有任何可觀察到的效果。請不要把它與本章稍後涵蓋、查詢 DSL 那個各自獨立、確實會被強制執行的深度上限 6 混為一談。 |

## Many-to-many 的 junction entity

一個 junction 就是一個一般的 SqlSugar entity——不見得非得是 `[CmsCollection]` 不可。一個完全沒有
`[CmsCollection]` attribute 的純粹 junction，運作方式一如既往，本節所述完全不適用於它。不論哪一種
做法，掃描器解析 junction 形狀時，需要的都是*擁有方*集合清單屬性上的
`[Navigate(typeof(JunctionType), parentFkName, targetFkName)]`——junction 型別本身不帶任何
`[CmsRelation]`。

每一種 junction 型別——不論帶不帶 payload——都必須宣告恰好一個 `[SugarColumn(IsPrimaryKey =
true)]` 屬性:many-to-many 同步機制會依這個主鍵去更新既有的 junction 資料列，所以複合主鍵或完全
沒有主鍵的 junction 會在啟動階段就以 `MetadataException` 快速失敗，而不是等到第一次寫入才出錯。

### Junction payload

當一個 junction 型別*同時*也帶有 `[CmsCollection]` 時，它就成了本手冊稱之為 **junction
collection** 的東西，而它除了兩個外鍵、該關聯的 `SortField` (見上文)，以及任何 `IsSystem`/
`ReadOnly` 欄位之外的所有 `[CmsField]`，就是這個關聯的 **payload**:屬於這條連結本身、而不屬於
任何一端的資料 (例如為什麼要連結這兩列的
備註、有別於排序的顯示權重、核准時間戳)。`MetadataScanner.ResolveJunctionPayloadFields`
(`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) 會在 `ScanTypes` 收尾階段算出
`RelationMetadata.JunctionPayloadFields`——這些 payload 欄位的 camelCase 名稱，含 hidden 欄位
在內。該關聯自己的 `RelationMetadata.SortField` 則是更早，在依
`[CmsRelation(SortField = ...)]` (見上文) 建置這個關聯本身時就已經設定好的。`RelationshipGraph.JunctionPayloadOf`
(`src/Struo.Infrastructure/Metadata/RelationshipGraph.cs`) 不會重新推導這份清單，只把
`MetadataScanner` 已經算好的名稱解析成 CLR 屬性;寫入端的混合陣列繫結器 (REST，第 9 章)、
`_junction` 讀取投影 (見下文)、修訂版本 (第 13 章)，以及 GraphQL (第 10 章) 都是透過
`JunctionPayloadOf` 做這一步解析。

一個 junction collection 的兩個外鍵**必須**宣告成可寫入的 `[CmsField]` (例如
`Interface = FieldInterface.Uuid`)——框架自身的 `UserRole`
(`src/Struo.Infrastructure/Identity/UserRole.cs`) 也是這麼做的，只是它除了兩個外鍵之外沒有宣告
任何欄位，所以它不帶 payload。`MetadataScanner.ValidateJunctionCollections`
(`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) 會在任一外鍵不可寫入時，以
`MetadataException` 讓啟動失敗，因為否則通用 CRUD API 就有辦法建立出外鍵為空的 junction 資料
列;訊息的第一段子句是 `"Junction collection '{junction}' (used by '{owner}.{relation}') must
declare its foreign keys '{fkA}' and '{fkB}' as writable [CmsField]s (e.g. Interface =
FieldInterface.Uuid)"`。

`Hidden = true` 用在 junction collection 上 (`UserRole`，以及範例的 `ArticleTag`，見下文) 只會
讓它不出現在管理後台側欄中——在其他所有地方，它仍是一個完全可定址的集合:`GET /api/schema`、RBAC
權限矩陣，以及產生出來的 GraphQL schema，都會像對待任何非隱藏集合一樣把它包含進去。
`RelationMetadata.JunctionCollection` (`/api/schema` 關聯項目中的 `junctionCollection`) 會指名
它，讓客戶端知道要對哪個集合另外申請寫入授權，才能寫入 junction payload (REST，第 9 章)。

同一個關聯項目裡也帶著 `junctionPayloadFields` 與 `sortField` 這兩個 key;出貨的管理後台 SPA
就是靠它們——把 `junctionPayloadFields` 篩到各欄位自己 `Hidden` 為否的那些——判斷一個
`TagSelect` 關聯是否帶有*可見*的 payload，或是有 `sortField`，因而要不要改用列表編輯器，而不是
普通的標籤 picker (見下方「管理後台 picker」)。

**給 fork 的但書**:如果你在一個 junction entity 上直接加上自己的
`[Navigate]`/`[CmsRelation]` picker 關聯 (例如從該 junction 到某個第三方集合的 many-to-one，
比方說「由誰新增」)，這個關聯會像任何其他 many-to-one 一樣，登記進 inbound-restrict 索引，因為
`OnDelete` 預設就是 `Restrict`:只要還有 junction 資料列參照著第三方集合的那一列，刪除它就會被
擋下，除非你在這個 picker 關聯上宣告 `OnDelete = OnDelete.Cascade`。

範例的 `ArticleTag` (`samples/Struo.Sample.Blog/ArticleTag.cs`，第 16 章) 就是隨附出貨的
junction collection 範例:`[CmsCollection("Article tag", Hidden = true)]`，兩個外鍵都宣告成
可寫入的 `Uuid` 欄位，一個 `Note` 文字欄位作為它的 payload，以及一個 `Sort` 數字欄位，接到
`Article.Tags` 的 `[CmsRelation(SortField = nameof(ArticleTag.Sort))]`。

## 逐個值的 `OnDelete` 語意

`OnDelete` (`src/Struo.Domain/Metadata/Enums/OnDelete.cs`) 有三個值——`Restrict`、`Cascade`、
`SetNull`——只適用於一個 many-to-one 關聯的**目標**那一側 (外鍵所指向的集合)，決定當目標那一側
的資料列被永久刪除 (清除) 時會發生什麼事。`ItemPurgePipeline`
(`src/Struo.Application/Query/Write/ItemPurgePipeline.cs`) 就是這三個值全部被強制執行的地方:

- **`Restrict`** (attribute 的預設值)——如果任何地方仍有資料列透過該外鍵參照目標，清除操作
  (以及共用同一個守衛的一般軟刪除/回收桶操作) 就會被拒絕，擲出 `RelationConflictException`
  (HTTP 409):`"Cannot delete '{collection}/{id}': referenced by '{sourceCollection}'."`。已透過
  即時環境驗證，刪除一個仍持有 `File` 的 `MediaFolder` (`File.Folder` 是 `OnDelete.Restrict`;
  `MediaFolder` 本身不可軟刪除，所以它的 `DELETE` 一律嘗試進行清除):

  ```
  $ curl -s -X DELETE http://localhost:5221/api/items/mediafolder/<guides-id> -H "X-Struo-CSRF: 1" -b cookies.txt
  {"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediafolder/<guides-id>': referenced by 'file'."}}
  ```

- **`SetNull`**——在目標被清除之前，把每一筆傳入資料列的外鍵都設為 `null`。範例的
  `Article.Category` 與 `Category.Parent` 使用這個值。這個分支只會在**清除**期間執行，絕不會在
  軟刪除/回收桶期間執行——回收桶操作一律只執行上方的 `Restrict` 檢查，所以一個
  `SetNull`/`Cascade` 關聯的參照資料列，在對目標做一般回收桶操作時不會被動到。
- **`Cascade`**——每一筆傳入的參照資料列也會被遞迴清除 (透過同一條管線，所以*它自己*的
  junction/翻譯附屬資料表/版本紀錄也會一併清理，並用一個 `visited` set 防止循環的 cascade
  圖形無窮迴圈)。目前沒有任何出貨的框架或範例集合實際宣告 `OnDelete.Cascade`——它確實存在，
  並由測試套件演練過，但這個程式庫中每一個真實的關聯都使用 `Restrict` 或 `SetNull`。

## 用 `deep` 讀取關聯資料

`deep` 與下方帶點號的 filter/sort 路徑，都需要對路徑經過的**每一個**集合具備讀取授權，而不只是被
查詢的那一個。`deep` 會略過你無權讀取的關聯;帶點號的 filter 或 sort 路徑則會直接被拒絕。完整規則
以及它對開放匿名讀取部署的遷移影響，見第 8 章 [查詢 DSL](08-query-dsl.md) 的驗證一節。

`deep` 會要求在讀取時，把一個關聯展開到它的父資料列上，由 `RelationExpander`
(`src/Struo.Infrastructure/Query/RelationExpander.cs`) 逐關聯、逐頁批次處理 (每個關聯只發一次
後續查詢，不是每一列都發一次——不會有 N+1 問題)。單純的查詢字串形式，只需要指名要展開哪些關聯:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?deep=folder"
{"success":true,"data":{"id":"<id>","fileName":"alpha-report.txt", ...,
  "folder":{"id":"<folder-id>","name":"Guides", ...}}}
```

JSON 信封形式 (`POST /api/items/{collection}/query`，第 8 章) 則額外接受逐關聯設定:一份欄位
白名單、一個巢狀的 `filter` (對*目標*集合解析)、一個自有欄位 `sort`，以及 `limit`/`offset`
——這些都是在記憶體中，逐父項套用到該關聯已經取回的資料列上——再加上一個巢狀的 `deep` 以做多層
展開。一個 many-to-one 關聯會忽略 filter/sort/limit/offset (最多只有一筆目標資料列);它們只
適用於 one-to-many 與 many-to-many。

### 帶 payload 的 many-to-many 上的 `_junction`

當 `deep` 展開一個 junction 帶有 payload 的 many-to-many 關聯時 (見上文)，`RelationExpander`
會在每一筆展開出來的目標資料列上附加一個 `_junction` 物件，裡面是該資料列非 `Hidden` 的 payload
欄位值——一個 `Hidden` payload 欄位，會像一個 `Hidden` 自有欄位不會出現在目標資料列本身一樣，
被排除在 `_junction` 之外。一個 junction 不帶 payload 的關聯完全不會有 `_junction` 這個鍵;而當
呼叫端不具備對該 junction collection 的讀取授權時，`_junction` 會被整個省略 (而不是回傳
`null`)——這與 `deep` 對呼叫端無權讀取的關聯一貫採取的「省略、不失敗」立場一致。以 deep 展開
範例的 `Article.Tags` (第 16 章)，其形狀為 (示意用——`sort` 本身被排除在 `_junction` 之外，因為
它是該關聯的 `SortField`，已經反映在陣列順序中，不算 payload):

```json
{
  "id": "<article-id>",
  "tags": [
    { "id": "<tag-id>", "name": "Guide", "_junction": { "note": "editor pick" } }
  ]
}
```

## 深度上限 6

每一個帶點號的關聯路徑——不論出現在篩選、排序鍵，或巢狀的 `deep` 中——都會由 `RelationPath.Parse`
(`src/Struo.Application/Query/RelationPath.cs`) 解析，並對照 `StruoQueryOptions.MaxRelationDepth`
(預設值為 **6**) 做檢查。這個計數算的是關聯的*跳數 (hop)*，不計入最終的葉節點欄位:
`folder.name` 是 1 跳，`folder.parent.name` 是 2 跳，依此類推。跳數超過上限的路徑，會在任何
查詢執行之前就被拒絕——這純粹是一項中介資料圖形檢查，與這條鏈上實際存在多少筆真實資料列無關:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bfolder.parent.parent.parent.parent.parent.parent.name%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation path 'folder.parent.parent.parent.parent.parent.parent.name' exceeds the maximum depth of 6."}}
```

(`folder` 加上六個 `parent` 跳，總共 7 跳，比上限多 1。)

## 跨帶點號路徑的關聯篩選

一個包含 `.` 的篩選欄位路徑，會被當作一個關聯路徑。`FilterTranslator`
(`src/Struo.Infrastructure/Query/FilterTranslator.cs`、`FilterTranslator.Subquery.cs`) 不會分兩步
解析它——先取得相符的關聯 id，再把條件改寫成 `id IN (<字面 id>)`——而是把整個條件下推成**一個
巢狀 SQL 子查詢**，依關聯種類而形狀不同，但一律以同樣三種形式收尾 (`<col> IN (<sql>)` /
`<col> NOT IN (<sql>)` / `(<col> IS NULL OR <col> NOT IN (<sql>))`)，包裝成單一
`ConditionalModel`。一次清單查詢永遠恰好是兩道 SQL 敘述——一道 `COUNT`、一道 `SELECT`——不論它帶了
多少個關聯條件，也永遠不會有任何東西被具現化成一個記憶體內的 id 集合:

- **many-to-one**:`<declaring>.<fk> IN (SELECT id FROM <target> WHERE …)`
- **one-to-many**:`<declaring>.id IN (SELECT <reverseFk> FROM <target> WHERE … AND <reverseFk> IS
  NOT NULL)`
- **many-to-many**:`<declaring>.id IN (SELECT <parentFk> FROM <junction> WHERE […] AND <targetFk>
  IN (SELECT id FROM <target> WHERE …))`

每一跳都把下一跳巢狀包在自己裡面，所以一條多跳路徑 (`category.parent.name`) 是一條子查詢鏈，
而不是每一跳各發一次查詢。三種關聯種類全都支援，已用範例的 `article`/`category`/`tag` 即時驗證過
(如上，暫時選用啟用——`Engineering-u3doc0905` 是一個 category，`Guide-u3doc0905` 是一個 tag):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Bcategory.name%5D%5B_eq%5D=Engineering-u3doc0905"    # many-to-one
{"success":true,"data":[{"id":"...","translations":{"en":{"title":"Article B u3doc0905", ...}}}, {"id":"...","translations":{"en":{"title":"Article A u3doc0905", ...}}}],"meta":{"total":2,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags.name%5D%5B_eq%5D=Guide-u3doc0905"              # many-to-many
{"success":true,"data":[{"id":"...","translations":{"en":{"title":"Article B u3doc0905", ...}}}, {"id":"...","translations":{"en":{"title":"Article A u3doc0905", ...}}}],"meta":{"total":2,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Bname%5D%5B_contains%5D=u3doc0905&filter%5Barticles.status%5D%5B_eq%5D=published"   # one-to-many
{"success":true,"data":[{"id":"...","name":"Engineering-u3doc0905", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

每一個都恰好回傳了預期的資料列。路徑中一個未知的關聯名稱，會以與一個未知葉欄位相同的方式被拒絕
(第 8 章涵蓋完整的驗證全貌):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bbogus.name%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown relation 'bogus' on 'article' in path 'bogus.name'."}}
```

### 各自存在（each-exists）vs. 同一列:帶點號路徑 vs. `_some`/`_none`

**一條單純的帶點號路徑是「各自存在（each-exists）」，而非同一列。** 關聯路徑上的每一個
`ComparisonFilter` 都會被翻譯成自己獨立的子查詢;`LogicalFilter` 只會遞迴處理其子節點，因此沒有
任何機制會記錄「哪一列滿足了哪一個條件」。同一個 to-many 關聯路徑上的兩個條件，可以分別由*不同*
的關聯資料列滿足，而父資料列依然算相符。`_some` 與 `_none` 是修正這一點的兩個關聯**量詞
(quantifier)**:`_some` 的內層 filter 會被翻譯成對目標集合的**單一**子查詢，所以內層的每一個
條件都必須由*同一列*關聯資料列滿足;`_none` 是同一個子查詢，只是取反 (`NOT IN`，many-to-one 則是
`IS NULL OR NOT IN`)。

| 寫法 | 語意 |
|---|---|
| `filter[tags.name][_eq]=a&filter[tags.color][_eq]=red` | 各自存在:某個 tag 名為 `a`，**且**某個 tag 是紅色——可能是兩個不同的 tag。 |
| `filter[tags._some.name][_eq]=a&filter[tags._some.color][_eq]=red` | 同一列:**同一個** tag 既名為 `a` 又是紅色。 |
| `filter[tags._none.name][_eq]=a` | 沒有任何 tag 名為 `a`——包括完全沒有 tag 的文章。 |
| `filter[category._none.name][_eq]=x` | category 不叫 `x`，或者根本沒有 category。 |
| `filter[tags._some._junction.note][_contains]=hero` | 某個 article↔tag 連結自己的 `note` 包含 "hero"。 |

已針對一份帶有兩篇已標籤文章的 fixture 即時驗證——`A` 有一個 tag (`Guide-u3doc0905`，junction
`note: "hero"`)，`B` 有兩個 tag (`Guide-u3doc0905` 的 `note: "plain"`，以及 `Misc-u3doc0905` 的
`note: "hero"`)，`C` 完全沒有 tag。帶點號 (各自存在) 的寫法同時比對到 `A` 與 `B`——`B` 的 `Guide`
tag 單獨滿足了 name 條件，它*不同的* `Misc` tag 單獨滿足了 junction-note 條件:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags.name%5D%5B_eq%5D=Guide-u3doc0905&filter%5Btags._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":2,"limit":25,"offset":0}}
```

在 `_some` 之下，完全相同的兩個條件只比對到 `A`，因為它單一的 tag 資料列同時滿足了兩者:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags._some.name%5D%5B_eq%5D=Guide-u3doc0905&filter%5Btags._some._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<a-id>", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

`_none` 比對到那篇沒有 tag 的文章 (關聯量詞用在 many-to-one 上同樣合法——見下文——而 `_none` 用在
一個空的 to-many 關聯上為真，不是錯誤):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags._none.name%5D%5B_eq%5D=Guide-u3doc0905"
{"success":true,"data":[{"id":"<c-id>","translations":{"en":{"title":"Article C u3doc0905", ...}}}],"meta":{"total":1,"limit":25,"offset":0}}
```

**`_some`/`_none` 用在 many-to-one 關聯上同樣合法，不只限於 to-many。** 用在 to-one 上時，
`_some` 等同於對相同內層 filter 的一條帶點號路徑;`_none` 則代表「沒有相符的目標，或外鍵為
null」——這確實有用 (「沒有 category，或者不叫 Archive」)，已針對 `C` (完全沒有 category) 連同
`A`/`B` (有 category，但不叫 `Archive-u3doc0905`) 驗證過:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Bcategory._none.name%5D%5B_eq%5D=Archive-u3doc0905"
{"success":true,"data":[{"id":"<c-id>", ...},{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":3,"limit":25,"offset":0}}
```

最後這個結果依賴一條 NULL 安全規則:一句單純的 SQL `<fk> NOT IN (SELECT …)`，只要它的子查詢
曾經回傳過一個 NULL，結果就會整個變空，而*外層*資料列上一個可為 null 的外鍵——`C` 的
`categoryId`——用普通的 `NOT IN` 也有相同的失效模式。因此 many-to-one 上的 `_none` 會產生
`(<fk> IS NULL OR <fk> NOT IN (<sql>))`，而*內層*的 `IS NOT NULL` 防護只會套用在真的可能是
NULL 的欄位上，並非一律套用:one-to-many 這一跳的 `<reverseFk>` 投影 (上面那條要點) 永遠會加上
這道防護，不論是不是量詞，因為不論翻譯的是哪一種述詞，該欄位本身就是可為 null 的。many-to-one 這
一跳自己的 `target.id` 投影，只有在翻譯 `_none` 述詞時才會以同樣的方式加上防護——一條單純的帶點號
路徑或用在 many-to-one 上的 `_some`，都會略過這道防護，因為主鍵永遠不會是 NULL，也就沒有這道
防護需要保護的對象。many-to-many 這一跳自己內層的 `target.id` 投影 (餵給它 junction 子查詢
`<targetFk> IN (…)` 那一半)，永遠不會加上防護，`_none` 也一樣，理由相同;只有它*外層*的
`junction.<parentFk>` 投影，才會像 many-to-one 這一跳的 `target.id` 投影一樣，得到那道只在
`_none` 時才有的防護。

**子查詢內的軟刪除，永遠不會隨 `?deleted=` 放寬。** 一個關聯子查詢的 `Where(...)`，不論*外層*
請求自己的 `?deleted=only|with` 為何，建構方式都一樣——目標集合上的軟刪除過濾器，永遠不會為它
清除。一列已進回收桶的關聯資料列，永遠無法滿足一個帶點號路徑或量詞條件，即使呼叫端當下正在檢視
父項的回收桶也一樣。

### `_junction`:篩選 junction 自身的 payload

對一個 junction 帶有 payload 的 many-to-many 關聯而言 (上文)，`_junction.<field>` 是一個偽片段，
篩選的是*連結本身*的欄位，而非任一端——帶點號時 (各自獨立看待每一個 article↔tag 連結)，或是在
`_some`/`_none` 之內 (該述詞其餘部分所比對到的同一個連結):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":2,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btitle%5D%5B_contains%5D=u3doc0905&filter%5Btags._some.name%5D%5B_eq%5D=Misc-u3doc0905&filter%5Btags._some._junction.note%5D%5B_eq%5D=hero"
{"success":true,"data":[{"id":"<b-id>", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

`_junction` 必須緊接在一個帶有[可揭露 payload](07-relations.md#junction-payload) 的 many-to-many
關聯之後，而且後面必須恰好接一個非 `Hidden` 的 payload 欄位名稱，不能再有更多跳點;它不計入上方
提到的深度上限 6。它還需要在 *junction* 集合上取得自己的讀取授權——這是與關聯目標集合上讀取授權
分開的另一項檢查。用在 many-to-one 上時，因為根本沒有 junction 集合，會被拒絕為
`'_junction' is only valid after a many-to-many relation with a junction collection.`:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bcategory._junction.note%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'category._junction.note': '_junction' is only valid after a many-to-many relation with a junction collection."}}
```

**把 `_some`/`_none` 與其他普通條件組合，跟任何其他 filter 完全一樣**——包括在 `_or` 之內混用一個
純量條件與一個關聯述詞，或是把一個關聯量詞跟一個可翻譯自有欄位葉節點 (`title`) 組合——後者本身
也是透過完全相同的機制下推的:

```
$ curl -s -X POST http://localhost:5221/api/items/article/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"_or":[{"categoryId":{"_eq":"<engineering-category-id>"}},{"tags":{"_some":{"name":{"_eq":"Misc-u3doc0905"}}}}]}}'
{"success":true,"data":[{"id":"<b-id>", ...},{"id":"<a-id>", ...}],"meta":{"total":2,"limit":25,"offset":0}}
```

`_some`/`_none` 可以任意遞迴:內層 filter 是一個完整的、以目標集合為根的 `filter` 物件，本身可以
再包含帶點號路徑、更進一步的 `_some`/`_none` (巢狀量詞)，以及一層 `_and`/`_or`——完整文法、
JSON envelope 寫法，以及查詢字串折疊規則，見第 8 章。

**跨關聯路徑的排序**，範圍比篩選更窄:只有全程都是 many-to-one 的路徑才可排序
(`RelationPath.IsSortable`)，因為一個 to-many 跳點，並沒有單一、明確定義的順序可以拿來排序父
資料列:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?sort=category.name"
{"success":true, ...}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?sort=tags.name"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Sort across to-many relations is not supported: 'tags.name'."}}
```

## 管理後台 picker

`RelationInterface` (`src/Struo.Domain/Metadata/Enums/RelationInterface.cs`) 有四個值，而出貨的
管理後台 SPA 把每一個都接上了真正的輸入元件
(`frontend/src/lib/relationInputKind.ts`):

| `RelationInterface` | 管理後台元件 | 行為 |
|---|---|---|
| `Dropdown` | `RelationPicker` (vendored `ui/combobox`) | 單值 picker;在使用者輸入時，對目標集合做去抖動 (debounce) 的 `search=`。 |
| `TagSelect` | `RelationPicker` (vendored `ui/combobox`) | 沒有*可見*的 junction payload、也沒有 `SortField`:適用於 many-to-many 關聯的多值標籤 picker，搜尋行為與 `Dropdown` 相同。 |
| `TagSelect` | `JunctionLinksEditor` | 有可見的 junction payload 和/或 `SortField`:每個已選取的 target 各佔一列，列內是該連結可見的 junction payload 欄位 (與 `ItemForm` 相同的欄位型別註冊表) 與一個移除鈕，再加上——只有在關聯宣告了 `SortField` 時——排序用的上下箭頭。下方的 combobox 只負責增減成員。 |
| `TreeSelect` | `RelationPicker` (`form/TreeSelect`) | 建立在一棵樹狀結構上的單值 picker，該樹由一個自我參照 many-to-one 的目標資料列建構而成。 |
| `RelatedList` | `RelatedList` (vendored `data/DataTable`) | 目標集合的唯讀、分頁、延遲載入清單，依關聯的反向外鍵過濾;點擊一列會導向該列自己的項目編輯頁面。只有在父項已經儲存之後才會顯示 (一個全新、尚未儲存的父項，還沒有 id 可以拿來過濾)。 |

**授權 (Grants)**:沒有 junction 讀取授權——列仍然會顯示，但完全不顯示任何 payload 欄位 (伺服器回應
裡沒有 `_junction`，沒有東西可信地拿來畫)。有讀取但沒有寫入授權 (或者 junction collection 是
`AdminOnly` 而呼叫者不是 super admin)——payload 欄位會唯讀顯示，儲存時只送出裸 id:只更新成員，以及
在關聯有 `SortField` 時的順序。伺服器端回傳的 payload 驗證錯誤，會落到表單最上方的錯誤 banner
裡;客戶端自己則會在送出前先擋掉 `required`／`maxLength`。這套判斷邏輯位於
`frontend/src/lib/junctionLinks.ts` (`canReadJunction`、`canWriteJunction`)。數值或布林 payload 欄位若留空，
會被存成 `null`，而非空字串，所以 fork 應該把這類 junction 欄位宣告為可為 null，或者標記為
`Required`——表單會在送出前先擋掉空的 `Required` 欄位。

`relationInputKind.ts` 仍然保留一個無條件的 fallback，用於它的對應表不認得的介面:一個單純的
<span v-pre>`<span class="readonly-relation">{{ relation.label }} (read-only)</span>`</span>，而不是一個輸入元素，
所以 `Editable` 沒有任何東西可以套用上去。出貨的 enum 裡沒有任何值會走到那裡:schema 契約 gate
(`schema/interfaces.json`，詳見 `schema/README.md`) 會在某個 `RelationInterface` 成員缺少對應表
條目時讓 `pnpm test` 失敗，所以這個 fallback 是「因結構而不可達」，不是靠慣例維持。它只在你新增成員卻略過
前端那一半時才有意義——而那正是 gate 不允許的事。早期版本曾出貨三個沒有對應的成員
(`FilePicker`、`ImagePicker`、`FilesPicker`)，它們確實會落到這個 fallback;後來已移除，因為檔案
參照是以 `FieldInterface` 的 `File`/`Image`/`Files` 建模，而非以關聯建模。

每一個 picker，都會透過 `resolveDisplayLabel`，從 `[CmsRelation(DisplayTemplate = ...)]` 解析出
目標資料列的顯示標籤——這是對目標自身已投影欄位做的一個單純 `{FieldName}` 代換，如果樣板 (或它
參照的欄位) 無法解析，就退回使用原始 id。

## 接下來該去哪

- 第 4 章 [定義一個集合](04-defining-a-collection.md)，涵蓋 `[CmsField]`，以及一個集合自身的
  欄位如何餵給 `DisplayTemplate`。
- 第 8 章 [查詢 DSL](08-query-dsl.md)，涵蓋 `deep` 與帶點號路徑所在的完整 `filter`/`sort`
  語法，包括每一個運算子與確切的驗證錯誤結構。
- 第 12 章 [認證、SSO 與 RBAC](12-auth-and-rbac.md)，說明 `User.Roles` 如何用來解析有效權限。
- 第 13 章 [版本紀錄與軟刪除](13-revisions-and-soft-delete.md)，說明一次清除操作的
  `Cascade`/`SetNull` 掃描，如何與一個集合自身的版本歷史及軟刪除狀態互動。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，取得本章 one-to-many 與 many-to-many 範例所
  依據的完整 `Category`/`Article`/`Tag` 關聯圖。
