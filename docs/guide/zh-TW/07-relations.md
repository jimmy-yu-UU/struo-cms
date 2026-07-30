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
`ItemWriteSideSync.SyncM2MAsync` 會先驗證每一個 id 都存在於目標集合中，才替換該父項的
junction 資料列 (是完整替換，不是差異式的 patch):一個未知的 id 會被拒絕為 `"One or more ids
in '{relation}' do not exist in '{target}'."`，而一個重複的 id 會被靜默去重 (junction 本身是
一個 set)。

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

一個 junction 就是一個一般的 SqlSugar entity——不見得非得是 `[CmsCollection]` 不可。框架自身的
`UserRole` (`src/Struo.Infrastructure/Identity/UserRole.cs`) 剛好*同時*也是它自己的
`[CmsCollection]` (`Hidden = true`，所以它從來不會顯示在側欄中;`AdminOnly = true`，所以對它的
一般寫入，無論有沒有任何逐集合授權，都需要超級管理員身分);它自己的文件註解只說明它「塑模了
使用者↔角色這個 many-to-many」，並未進一步說明為什麼它同時也要是一個集合。一個 junction entity
完全不需要同時是一個集合:範例的 `ArticleTag` (`samples/Struo.Sample.Blog/ArticleTag.cs`) 就是
一個沒有 `[CmsCollection]` attribute 的純粹 junction。不論哪一種做法，掃描器解析 junction 形狀
時，需要的是*擁有方*集合清單屬性上的
`[Navigate(typeof(JunctionType), parentFkName, targetFkName)]`——junction 型別本身不帶任何
`[CmsRelation]`。

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

一個包含 `.` 的篩選欄位路徑，會被當作一個關聯路徑，並由 `RelationFilterResolver`
(`src/Struo.Infrastructure/Query/RelationFilterResolver.cs`) 解析:它會從葉節點走到根節點，
逐跳收集目標 id，並把原始條件改寫成一個對*根*集合的單純 `id _in [...]` (若沒有任何東西相符，
則改寫成一個恆為假的 `id _null`)——這樣一來，查詢管線的其餘部分就完全不需要為關聯路徑做任何
特殊處理。三種關聯種類全都支援作為跳點，已用範例的 `article`/`category` 即時驗證過 (如上，
暫時選用啟用):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bcategory.name%5D%5B_eq%5D=Engineering"    # many-to-one
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags.name%5D%5B_eq%5D=Guide"              # many-to-many
$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Barticles.status%5D%5B_eq%5D=published"   # one-to-many
```

每一個都恰好回傳了預期的資料列。路徑中一個未知的關聯名稱，會以與一個未知葉欄位相同的方式被拒絕
(第 8 章涵蓋完整的驗證全貌):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bbogus.name%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown relation 'bogus' on 'article' in path 'bogus.name'."}}
```

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
| `Dropdown` | `RelationPicker` (PrimeVue `Select`) | 單值 picker;在使用者輸入時，對目標集合做去抖動 (debounce) 的 `search=`。 |
| `TagSelect` | `RelationPicker` (PrimeVue `MultiSelect`) | 適用於 many-to-many 關聯的多值 picker，搜尋行為相同。 |
| `TreeSelect` | `RelationPicker` (PrimeVue `TreeSelect`) | 建立在一棵樹狀結構上的單值 picker，該樹由一個自我參照 many-to-one 的目標資料列建構而成。 |
| `RelatedList` | `RelatedList` (PrimeVue `DataTable`) | 目標集合的唯讀、分頁、延遲載入清單，依關聯的反向外鍵過濾;點擊一列會導向該列自己的項目編輯頁面。只有在父項已經儲存之後才會顯示 (一個全新、尚未儲存的父項，還沒有 id 可以拿來過濾)。 |

`relationInputKind.ts` 仍然保留一個無條件的 fallback，用於它的對應表不認得的介面:一個單純的
`<span class="readonly-relation">{{ relation.label }} (read-only)</span>`，而不是一個輸入元素，
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
