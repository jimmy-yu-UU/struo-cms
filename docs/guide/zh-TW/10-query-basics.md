# 10. 查詢：過濾、排序、分頁

清單端點的查詢字串怎麼寫、篩選與排序怎麼組合、分頁怎麼算，是這一章的主題；REST 與 GraphQL 共用同一
套語意。

## 查詢怎麼寫

清單端點有兩種等價的請求形狀，解析成同一份查詢，在 SQL 組出來之前都先過同一份欄位白名單。

**查詢字串。** `GET /api/items/{collection}` 讀 bracket 語法的重複鍵：
`filter[<欄位或關聯路徑>][<運算子>]=<值>`，其餘參數的完整清單見下一節「一個查詢的組成」。一次過濾、
一個遞減排序、外加分頁：

```text
$ GET /api/items/article?filter[status][_eq]=published&sort=-publishedAt&limit=1&offset=0
{"success":true,"data":[{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","publishedAt":"2026-06-01T00:00:00","heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.712477","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.712614","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":2,"limit":1,"offset":0}}
HTTP_STATUS:200
```

只有一篇已發佈的文章排在前面被回傳；`meta.total` 是 2，代表符合篩選的總列數，`limit`／`offset` 則
是夾完後真正生效的分頁值。後面範例延用同一批測試資料，id 是資料本身的，讀者環境會不同。

**JSON 封裝。** `POST /api/items/{collection}/query` 收下同一個模型當 JSON body，鍵名跟查詢字串的
參數一一對應。這是唯一能寫 `_and`／`_or`、也是唯一能替一個 `deep` 關聯自己的
`fields`／`filter`／`sort`／`limit`／`offset`，或再巢狀 `deep` 的方式。上面那個查詢寫成 JSON body 是
同一件事：

```json
{
  "filter": { "status": { "_eq": "published" } },
  "sort": ["-publishedAt"],
  "limit": 1,
  "offset": 0
}
```

本章接下來的例子都用查詢字串形式。GraphQL 用同一套語意做同一件事，只是引數拼法不同——運算子從
`_eq` 換成 `eq`，量詞從 `_some` 換成 `some`——拼法留給[GraphQL 一章](14-graphql.md)。

**兩者都是讀。** 兩個動作都不需要登入，只要求這個集合的讀取授權；`POST .../query` 雖然用 POST，也
不需要寫入授權。

重複的查詢字串鍵會先被逗號接起來才解析：`filter[status][_eq]=a&filter[status][_eq]=b` 會變成單一個
值 `a,b`，不是兩個條件，也不會報錯。

## 一個查詢的組成

一次查詢由這幾組參數組成：

- **`filter`**——依欄位或關聯路徑篩選列，本章其餘部分的主題。
- **`search`**——集合可搜尋、非隱藏欄位的 `LIKE` OR 群組，跟 `filter` 是 AND 關係；沒有可搜尋欄位
  的集合直接忽略、不報錯，可翻譯欄位透過翻譯 sidecar 用當次查詢實際使用的 locale 一起比對。
- **`sort`**——依一或多個欄位排序，`limit`／`offset`——分頁，分別見下方「排序」與「分頁與 `meta`」。
- **`fields`**——只留下集合自己的欄位，留給[進階查詢的一章](11-query-advanced.md)。
- **`deep`**——展開關聯，同樣留給[進階查詢的一章](11-query-advanced.md)；第 8 章已經先講過批次展開
  的機制，見[第 8 章：關聯](08-relations.md)。
- **`facets`／`aggregate`**——在同一批過濾結果上算摘要統計，留給[進階查詢的一章](11-query-advanced.md)。

## 運算子

每一種運算子在查詢字串與 JSON 裡是同一個 token，在 GraphQL 裡另有拼法：

| REST | 意義 |
|---|---|
| `_eq` | 等於 |
| `_neq` | 不等於 |
| `_in` | 在清單中 |
| `_nin` | 不在清單中 |
| `_lt` | 小於 |
| `_lte` | 小於等於 |
| `_gt` | 大於 |
| `_gte` | 大於等於 |
| `_null` | 為 null |
| `_nnull` | 不為 null |
| `_contains` | 包含子字串 |
| `_starts_with` | 以子字串開頭 |
| `_ends_with` | 以子字串結尾 |

**比對區分大小寫。** `_EQ` 或 `_startsWith` 會被拒絕，回 `Unknown operator '<token>'.`；欄位名稱則
相反，到處都不分大小寫。

**接受值的形狀。** `_in`／`_nin` 在查詢字串上是逗號清單、在 JSON 裡是陣列，兩者都併成同一份 SQL `IN`
清單；`_contains`／`_starts_with`／`_ends_with` 分別轉成 `LIKE '%v%'`、`LIKE 'v%'`、`LIKE '%v'`。

**`_null`／`_nnull` 不看值。** 這兩個運算子只轉成單純的 `IS NULL`／`IS NOT NULL`，值本身完全不會被
讀，所以 `filter[publishedAt][_null]=false` 跟 `=true` 是同一個請求，不會反過來變成「不是
null」：

```text
$ GET /api/items/article?filter[publishedAt][_null]=true
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":1,"limit":25,"offset":0}}
HTTP_STATUS:200
```

把 `_null]=true` 換成 `_null]=false`，回傳的是同一列，`meta.total` 一樣是 1。

**型別轉換。** 除了 `_null`／`_nnull` 以外，每個運算子的值都會轉成欄位真正的 CLR 型別再比對——
`Guid` 外鍵用 `uuid = uuid` 比，不是文字比對；轉換表涵蓋 `Guid`、`int`、`long`、`short`、`bool`、
`DateTime`、`DateTimeOffset`、`decimal`、`double`、`float`，`string` 與未涵蓋的型別維持文字比較。
值一律用不隨地區變動的格式送出，主機的預設文化特性也在啟動時固定成 invariant，兩邊才不會對同一個
小數或日期讀出不同結果；運算子與欄位介面則互不相干，對一個 `Text` 欄位下 `_gt` 一樣會被接受，當成
文字比較。

## `_and` 與 `_or`

在查詢字串上，不同的 `filter[...]` 鍵之間預設是 AND，即使是同一個欄位配不同運算子也一樣。查詢字串
完全無法表達 OR，同一個欄位配同一個運算子也只能出現一次鍵，這兩種情況都得改用 JSON 封裝的
`_and`／`_or`，這樣寫：

```json
{"filter":{"_or":[{"status":{"_eq":"draft"}},{"categoryId":{"_eq":"01a08f92-3833-750e-bad3-6ee620f985c3"}}]}}
```

POST 到 `/api/items/article/query`；三篇文章都符合——草稿那一篇滿足第一個分支，三篇的分類都是
`Guides`，滿足第二個分支——`meta.total` 是 3。

**只能巢狀一層。** `_and`／`_or` 底下的子項如果自己又是一個邏輯群組，會被拒絕：
`Nested logical groups are not supported; use a single level of _and/_or over field conditions.`
這個一層的限制是分別計算的：`_some`／`_none` 的內層篩選是一個新的起點，本身還能再有自己的一層
`_and`／`_or`。

**條件數量上限。** `Query:MaxFilterConditions`（預設 50）計算的是整個請求裡所有葉節點比較——含每
個 `_and`／`_or` 分支，也含每個量詞內層篩選——超過就先被拒絕：
`Too many filter conditions (max 50).` 跨關聯的條件沒有類似的上限，因為一個關聯條件只多一層巢狀子
查詢，不需要像撈一份 id 集合那樣限制大小。

**其他形狀錯誤。** 一個空的 `filter` 物件是錯誤（`Empty filter object.`），不帶 `filter` 鍵則是合法
的無操作；一個欄位的值必須是運算子物件，`{"status":"draft"}` 會被拒絕成
`Filter for field 'status' must be an object of operators.`，`{"status":{}}` 則是
`Filter for field 'status' has no operator.`；`_and`／`_or` 的值必須是陣列（`'_or' must be an
array.`），查詢字串上沒分成欄位與運算子兩段的鍵是 `Malformed filter key 'filter[status]'.`。

## 關聯路徑：逐段成立 vs `_some`／`_none` 同一列

[第 8 章：關聯](08-relations.md)「關聯的篩選（概要）」一節已經說過帶點號的路徑會被推進巢狀子查
詢；這裡把「逐段成立」跟「同一列」的差別講清楚，也把 `_junction` 這個量詞式篩選補完。

**逐段成立。** 一條帶點號的路徑，兩個條件可以各自被不同的關聯列滿足，比方說
`filter[tags.name][_eq]=howto&filter[tags._junction.note][_eq]=hero` 回傳「Release notes」與
「Getting started」兩篇——只要文章有任何一個標籤叫 `howto`、以及任何一個標籤的連結備註是 `hero` 就
算數，不要求同一個標籤兩個條件都成立。

**`_some`／`_none` 綁定同一列。** 量詞的內層篩選會整個轉成單一個子查詢，所以量詞底下的每個條件都要
被同一個關聯列滿足；`_none` 是那個子查詢取反，一篇完全沒有相關列的文章也滿足 `_none`。把上面的路
徑換成量詞：

```text
$ GET /api/items/article?filter[tags._some.name][_eq]=howto&filter[tags._some._junction.note][_eq]=hero
{"success":true,"data":[{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":0,"status":"published","publishedAt":"2026-03-01T00:00:00","heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:20.153539","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:20.153611","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Getting started","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":1,"limit":25,"offset":0}}
HTTP_STATUS:200
```

只剩「Getting started」：它的 `howto` 標籤本身備註就是 `hero`；「Release notes」雖然也有 `howto`
標籤，但備註是 `hero` 的是另一個（`release`）標籤，不是同一個標籤連結。`_some`／`_none` 對多對一關
聯一樣合法：`_some` 等於同一個內層篩選的逐點路徑，`_none` 則是「沒有符合的目標，或外鍵本身是
null」兩種情況都算。

**保留字與寫法。** `_and`、`_or`、`_some`、`_none`、`_junction`，加上 GraphQL 拼法拿掉底線的同一
組，都不能拿來當欄位或關聯名稱，比對不分大小寫；違反的話在啟動時就會失敗，不必等到有請求進來。

JSON 封裝裡量詞是欄位物件裡的一個鍵——`{"tags":{"_some":{...}}}`——值是完整的一份篩選，根是關聯的
目標集合，遞迴套用同一套文法：可以有帶點號的路徑、再一層 `_some`／`_none`、`_junction.<欄位>`，還有
一層 `_and`／`_or`。一個欄位物件也可以同時放 `_some` 與 `_none`，兩者是各自獨立、AND 起來的條件。

查詢字串上量詞是路徑裡的一個片段——`filter[tags._some.name][_eq]=howto`——同一個（前綴、量詞）底下
的條件會先被分組再折成一個量詞，前綴比對不分大小寫。同一個請求裡每個（前綴、量詞）只能有一組，兩個
各自獨立的同列條件要用 JSON 封裝表達，例如
`{"filter":{"tags":{"_some":{"name":{"_eq":"howto"},"_junction.note":{"_contains":"hero"}}}}}`。

量詞的形狀錯誤各自有自己的訊息：

- 值必須是非空物件：`'tags._some' must be a non-empty filter object.`
- 量詞不能跟純量運算子混在同一個欄位物件裡：`'tags' mixes a relation quantifier with scalar
  operators; a relation path has no scalar operators.`
- 量詞前面一定要有關聯名稱、後面一定要接條件：
  `'tags._some': '_some' must be followed by a condition on the related collection.`、
  `'_some.name': '_some' must follow a relation name.`

**`_junction`。** `_junction.<欄位>` 篩的是連結本身的 payload，不是任一端的資料，可以逐點使用，也
可以放進 `_some`／`_none` 裡。它必須緊跟在一個帶 junction collection 的多對多關聯之後，後面只能接
一個非隱藏的 payload 欄位、不能再接下一段；用在多對一上會被拒絕：
`'category._junction.note': '_junction' is only valid after a many-to-many relation with a
junction collection.`

`_junction` 需要呼叫端對 junction collection 本身的讀取授權，這個檢查在欄位名稱被解析之前就先做，
讀不到的 junction 不會被拿來反推有哪些欄位。

它不算進關聯深度上限，因為只是一個附加片段；`_junction` 條件也不能跟目標集合的條件在同一個量詞裡用
OR 接起來。

**軟刪除的界線。** 關聯子查詢一律套用目標集合自己的軟刪除過濾，與外層的 `deleted=` 無關，見
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)。

## 可翻譯欄位怎麼被篩選

上面每一個運算子在可翻譯欄位上都能用：篩選會被改寫成一條對翻譯 sidecar 的子查詢，在這次查詢實際使
用的 locale 下比對，不用特別帶 `locale=`——沒給的話用站台預設值。
`GET /api/items/article?filter[title][_contains]=start` 不帶 `locale=` 時用站台預設（`en`）比對，
回傳標題含 `start` 的那一篇，`meta.total` 為 1；換成中文標題並帶
`GET /api/items/article?filter[title][_contains]=%E9%96%8B%E5%A7%8B&locale=zh-TW` 明白指定 locale，
回傳的是同一篇文章，但 `translations` 只剩 `zh-TW` 一筆，`meta.total` 仍是 1。

**`locale=` 的驗證。** `locale=` 先過字元白名單，不合規則的碼會被拒絕：
`Locale '<code>' contains invalid characters. Codes must match [A-Za-z0-9_-]{1,35}.`；通過字元
檢查後還要在已啟用的語言清單裡，否則是 `Unknown or disabled locale '<code>'.`。沒有 `locale=` 時，
這次查詢實際使用的 locale 一律算成站台預設值，即使根集合自己沒有可翻譯欄位，也會為了關聯路徑上可
能碰到的可翻譯集合而算出這個值。

## 排序

`sort=` 是逗號清單，每個欄位可以加上 `-` 前綴代表遞減，套用順序就是列出的順序。

排序鍵可以是一條點號路徑，但每一段都必須是多對一，只要碰到一段是多對多或一對多就被拒絕：

```text
$ GET /api/items/article?sort=tags.name
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Sort across to-many relations is not supported: 'tags.name'."}}
HTTP_STATUS:400
```

`sort=category.name` 這種多對一路徑則正常回應 200。

`NULL` 排在哪裡交給資料庫決定，程式不會送 `NULLS FIRST`／`NULLS LAST`：SQLite 遞增排序時 `NULL`
排最前，PostgreSQL 排最後。

**預設順序。** 沒有 `sort=` 時，順序是 `createdAt DESC, id ASC`，沒有 `CreatedAt` 的集合退化成單獨
的 `id ASC`；這是刻意的——PostgreSQL 的堆積順序在更新後不穩定，不這樣做的話一筆被編輯過的列會憑空
跳位置。主鍵一律被加在每個客戶端排序的最後一段當遞增的平手判定，除非呼叫端已經自己排 `id`，這樣一
來就算排序欄不是唯一值，翻頁也不會重複或漏掉列。

排序一個可翻譯欄位會對翻譯 sidecar 開一條 locale-scoped 的關聯子查詢；某個 locale 沒有翻譯列的那一
列排序值當成 `NULL`。排序鍵用的白名單跟篩選欄位是同一份，細節見本章最後一節。

## 分頁與 `meta`

分頁只有 `limit`／`offset` 兩個參數，沒有 `page`。`limit` 沒給或不是正數會被夾到
`Query:DefaultLimit`（預設 25），超過上限的會夾到 `Query:MaxLimit`（預設 100）；`offset` 最低是 0。
`limit=abc` 這種非數字值不會報錯，直接當成沒給處理，一樣被夾到預設值，比方說
`GET /api/items/article?limit=abc` 回應的 `meta` 是 `{"total":3,"limit":25,"offset":0}`——`limit`
落回預設的 25，不是拒絕請求。

每個清單回應都帶一個 `meta` 物件，裝著夾完之後真正生效的 `limit`／`offset`，以及 `total`——套用同
一個 `deleted=` 模式篩完之後、分頁之前的列數，不是整張表的列數。分頁沒有游標或 keyset 形式，也沒有
`Link` 標頭，深分頁就是純粹的 `offset`。

## `deleted=`

`deleted=` 一律從網址讀，在清單、`POST .../query` 與單筆讀取上都一樣。三個可接受值、預設值與權限
規則見[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)「`deleted=` 過濾器與權限」一節；這道
檢查寫在控制器，不在服務層，直接呼叫 `ItemService.QueryAsync`／`GetAsync` 不會自動套用。

## 哪些欄位能篩、能排，走錯會怎樣

一個欄位能不能拿來篩選、排序或投影，看的是同一份白名單：集合自己非隱藏的 `[CmsField]`，加上宣告出
來的多對一外鍵——`categoryId` 因此可以篩，即使它自己沒有 `[CmsField]`；`id` 永遠合法，即使不在欄位
清單裡。

`Sortable` 不影響這份白名單：`[CmsField]` 上的 `Sortable` 只決定後台清單畫面的欄位表頭能不能點來排
序，跟 `sort=` 這裡的白名單是兩件事。隱藏欄位刻意被排除在白名單之外：一個看起來像密碼的隱藏欄位如
果還能拿來篩，`meta.total` 就會變成一個逐字元試出內容的側錄工具。一個不存在的自己欄位不管像不像打
錯字都用同一種方式拒絕：

```text
$ GET /api/items/article?filter[bogus][_eq]=x
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogus' on collection 'article'."}}
HTTP_STATUS:400
```

一條帶點號的路徑逐段驗證，對應到關聯圖上：不存在的關聯、不存在的葉欄位、超出深度上限三種情況各自
有自己的訊息——`Unknown relation '<rel>' on '<collection>' in path '<path>'.`、
`Unknown field '<leaf>' on collection '<collection>' in path '<path>'.`、
`Relation path '<path>' exceeds the maximum depth of <max>.`。第一種確實會發生：

```text
$ GET /api/items/article?filter[bogus.name][_eq]=x
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown relation 'bogus' on 'article' in path 'bogus.name'."}}
HTTP_STATUS:400
```

路徑終點的葉欄位驗證，用的跟根欄位一樣的可見性規則：非隱藏欄位加 `id`。深度預算是共用的：一個
`_some`／`_none` 量詞已經用掉的段數，會從它內層路徑剩下的預算裡扣除，但錯誤訊息報的是設定的上限本
身，不是扣完剩下的數字。

**讀取授權不會沿路徑傳遞。** 一條點號路徑經過的每個集合都要有自己的讀取授權，沒有的話整個請求被拒
絕——`FORBIDDEN`，匿名呼叫端則是 `UNAUTHORIZED`——不是悄悄拿掉那個條件：拿掉會讓呼叫端拿到不符合
篩選的資料，而且就算回傳零筆，`meta.total` 本身在讀不到的集合上一樣能被拿來試探。授權檢查在葉欄位
被解析之前就先做，讀不到的集合上真正存在的欄位跟編造出來的欄位，拿到的是同一種拒絕。

多對一外鍵欄位本身維持可篩：它是呼叫端本來就能讀到的那一列上的一欄，而且只暴露一個不透明的 id，不
會洩漏目標集合的任何資料。

`Rbac:PublicReadCollections` 裡的集合，如果它的公開篩選或 `deep=` 會碰到另一個集合，那個集合也要
一併列進去，否則會撞上上面的授權拒絕。

錯誤信封的完整形狀留給[REST API 慣例那一章](12-rest-conventions.md)。

## 接下來

篩選、排序、分頁都清楚之後，下一步是怎麼投影欄位、展開關聯、算 facet 與彙總，這是
[進階查詢一章](11-query-advanced.md)的主題。
