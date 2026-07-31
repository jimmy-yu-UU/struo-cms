# 8. 查詢 DSL

每一個集合 (collection) 的讀取層面——REST 的清單/取得、GraphQL 的清單/單筆，以及關聯的 `deep`
展開——都是由同一個共用模型 `QueryModel` (`src/Struo.Domain/Query/QueryModel.cs`) 驅動的，它由
`QueryParser` (`src/Struo.Application/Query/QueryParser.cs`) 建構，並在真正到達資料庫之前，由
`QueryValidator` (`src/Struo.Application/Query/QueryValidator.cs`) 做白名單驗證。

## 兩種請求形式

同一個查詢模型可以透過兩種方式抵達:

- **`GET /api/items/{collection}`**——查詢字串形式:`filter[field][op]=value` 配對 (可重複
  出現;隱含以 AND 連接)，再加上 `sort=`、`limit=`、`offset=`、`search=`、`fields=`、`deep=`、
  `locale=`、`deleted=`。
- **`POST /api/items/{collection}/query`**——一個 JSON 信封，帶有與上面相同的欄位，以正規
  JSON 形式呈現:`{ "filter": {...}, "sort": [...], "limit": n, "offset": n, "fields": [...],
  "deep": {...}, "search": "..." }`。要使用 `_and`/`_or` 邏輯組合，或是一個 `deep` 關聯自己
  巢狀的 `filter`/`sort`/`limit`/`offset`，這是唯一的方式——查詢字串形式的 `deep=`，只接受一份
  單純、以逗號分隔的關聯名稱清單來展開，沒有任何進一步的選項。

兩者都是唯讀的，並且都會經過與任何其他讀取相同的 RBAC 讀取檢查 (`ItemsController` 這兩個
action 上都沒有 `[Authorize]`，`ItemService` 對一次查詢也只會檢查 `CanRead`)——儘管動詞是
`POST`，`POST /query` 並不需要*寫入*授權。但 `CsrfProtectionMiddleware`
(`src/Struo.Api/Auth/CsrfProtectionMiddleware.cs`) 會以相同的方式，對每一個非安全 HTTP 方法
(`POST`/`PUT`/`DELETE`/……，`GET`/`HEAD`/`OPTIONS`/`TRACE` 則豁免) 做把關，不論是讀取還是寫入
——**但只有在請求依附於 session cookie 上時才會如此**:一個以 `Bearer` 驗證的請求可豁免 (沒有
瀏覽器環境憑證可供偽造)，完全沒有帶 session cookie 的請求也一樣 (也還沒有東西可以被攻擊)。一個
以 cookie 驗證、卻沒有 `X-Struo-CSRF` 標頭的 `POST /query`，會在抵達 controller 之前就被拒絕:

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" -b cookies.txt -d '{}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}
```

## 一個查詢的剖析

| 組成部分 | 查詢字串 | JSON 信封 | 意義 |
|---|---|---|---|
| 篩選 (Filter) | `filter[field][op]=value` (可重複) | `"filter": { ... }` | 欄位條件，預設以 AND 連接;運算子與邏輯組合見下文。 |
| 排序 (Sort) | `sort=field,-field2` | `"sort": ["field", "-field2"]` | 逗號分隔清單;開頭加上 `-` 代表降冪。 |
| 分頁 (Pagination) | `limit=`、`offset=` | `"limit"`、`"offset"` | 不存在任何 `page` 參數——分頁純粹以 offset 為基礎 (見下文)。 |
| 欄位投影 (Fields) | `fields=a,b,c` | `"fields": ["a","b","c"]` | 限制投影出哪些*自有*欄位 (關聯與 `translations` 不受影響——見下文)。 |
| 關聯展開 (Deep) | `deep=rel1,rel2` | `"deep": { "rel1": {...} }` | 關聯展開;第 7 章完整涵蓋。 |
| 搜尋 (Search) | `search=text` | `"search": "text"` | 自由文字 `LIKE`，以 OR 連接跨越每一個 `Searchable` 欄位 (第 4 章)。 |
| 軟刪除 (Soft-delete) | `deleted=exclude\|only\|with` | *(僅限查詢字串——`GET`/`POST query` 都是從 URL 讀取它)* | 見下文。 |
| 語言 (Locale) | `locale=code` | *(僅限查詢字串，同上)* | 可翻譯欄位篩選/排序/讀取時使用的有效查詢語言 (第 6 章)。 |

## 完整的運算子表

`QueryOperator` (`src/Struo.Domain/Query/QueryOperator.cs`) 恰好有這 13 個值，每一個都對應
剛好一個查詢字串 token 與一個 JSON 信封鍵值 (`QueryParser` 的 `Operators` 對應表)。下方每一個
範例，都是對框架自身的 `file` 集合執行的 (三個已上傳的檔案:`alpha-report.txt`，20 位元組，
位於一個資料夾中;`beta-notes.txt`，23 位元組，位於同一個資料夾中;`gamma-draft.txt`，35
位元組，未歸入任何資料夾):

| `QueryOperator` | Token | 範例 | 結果 |
|---|---|---|---|
| `Eq` | `_eq` | `filter[fileName][_eq]=alpha-report.txt` | 只符合 `alpha-report.txt`。 |
| `Neq` | `_neq` | `filter[fileName][_neq]=alpha-report.txt` | 符合 `beta-notes.txt` 與 `gamma-draft.txt`。 |
| `In` | `_in` | `filter[fileName][_in]=alpha-report.txt,gamma-draft.txt` | 符合這兩個指名的檔案。 |
| `Nin` | `_nin` | `filter[fileName][_nin]=alpha-report.txt,gamma-draft.txt` | 只符合 `beta-notes.txt`。 |
| `Lt` | `_lt` | `filter[size][_lt]=23` | 符合那個 20 位元組的檔案。 |
| `Lte` | `_lte` | `filter[size][_lte]=23` | 符合那兩個 20 與 23 位元組的檔案。 |
| `Gt` | `_gt` | `filter[size][_gt]=23` | 符合那個 35 位元組的檔案。 |
| `Gte` | `_gte` | `filter[size][_gte]=23` | 符合那兩個 23 與 35 位元組的檔案。 |
| `Null` | `_null` | `filter[folderId][_null]=true` | 符合那個未歸入任何資料夾的檔案。 |
| `NNull` | `_nnull` | `filter[folderId][_nnull]=true` | 符合那兩個已歸入資料夾的檔案。 |
| `Contains` | `_contains` | `filter[fileName][_contains]=report` | 符合 `alpha-report.txt` (SQL `LIKE '%report%'`)。 |
| `StartsWith` | `_starts_with` | `filter[fileName][_starts_with]=beta` | 符合 `beta-notes.txt` (`LIKE 'beta%'`)。 |
| `EndsWith` | `_ends_with` | `filter[fileName][_ends_with]=draft.txt` | 符合 `gamma-draft.txt` (`LIKE '%draft.txt'`)。 |

以上每一個都是真實執行過的:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5BfileName%5D%5B_eq%5D=alpha-report.txt"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}],"meta":{"total":1,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bsize%5D%5B_lt%5D=23"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt","size":20, ...}],"meta":{"total":1,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5BfolderId%5D%5B_null%5D=true"
{"success":true,"data":[{"id":"...","fileName":"gamma-draft.txt","folderId":null, ...}],"meta":{"total":1,"limit":25,"offset":0}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5BfileName%5D%5B_ends_with%5D=draft.txt"
{"success":true,"data":[{"id":"...","fileName":"gamma-draft.txt", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

`_null`/`_nnull` 會渲染成一個單純的 `IS [NOT] NULL`——不會有任何比較值被送到資料庫——這是刻意
如此設計的:一個 `IS NULL OR = ''` 風格的檢查，在 PostgreSQL 上對一個非文字欄位會擲出型別錯誤。
每一個其他會拿來與一個型別化欄位比較的運算子 (`Guid`、數值、`DateTime`……)，都會透過
`ConditionalModel.CSharpTypeName` 做轉型，所以舉例來說，一個 `Guid` 外鍵欄位比較時是
`uuid = uuid`，而不是 `uuid = text` (這會被 PostgreSQL 直接拒絕)。

## 邏輯組合 (`_and`/`_or`)

不同的查詢字串 `filter[...]` 鍵值，已經隱含以 AND 連接——包括同一個欄位在*不同*運算子下的兩個
條件 (`filter[size][_gt]=20&filter[size][_lt]=35`，見下方)。查詢字串形式完全無法表達的是 OR，
或是同一個欄位在*相同*運算子下的兩個條件 (`filter[size][_gt]` 作為一個字典鍵值只能出現一次)
——這兩種情況都需要 JSON 信封的 `_and`/`_or`。這裡只支援**一層**巢狀——一個 `LogicalFilter`
若出現在另一個 `LogicalFilter` 之下，就會被直接拒絕，所以 `_or` 裡面包 `_and` (或反過來) 是
行不通的，即使只巢狀一層也一樣:

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"_or":[{"fileName":{"_eq":"alpha-report.txt"}},{"fileName":{"_eq":"gamma-draft.txt"}}]}}'
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...},{"id":"...","fileName":"gamma-draft.txt", ...}],"meta":{"total":2,"limit":25,"offset":0}}
```

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"_and":[{"_and":[{"fileName":{"_eq":"x"}}]}]}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Nested logical groups are not supported; use a single level of _and/_or over field conditions."}}
```

對獨立欄位條件做一個扁平的 `_and`，在查詢字串形式下完全不需要任何 `_and` 鍵值——重複
`filter[...]` 本身就已經是隱含的 AND:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bsize%5D%5B_gt%5D=20&filter%5Bsize%5D%5B_lt%5D=35"
{"success":true,"data":[{"id":"...","fileName":"beta-notes.txt","size":23, ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

每一次查詢的篩選條件總數也有一個硬性上限——`StruoQueryOptions.MaxFilterConditions`，預設為
**50**——計算的是走訪過程中拜訪到的每一個葉節點 `ComparisonFilter`，不論它們巢狀在
`_and`/`_or` 底下多少層;超過這個上限，會在任何查詢執行之前擲出 `"Too many filter conditions
(max 50)."`。

還有第二個上限，它限制的是不同的東西:不是查詢的形狀，而是回答這個查詢**中間**可以花多少工。
帶點號的 (跨關聯) 篩選，以及對可翻譯欄位的搜尋，兩者的實作方式都是先把條件解析成一組根 id，
再改寫成本集合自身的 `id IN (...)`——這趟走訪見第 7 章。`StruoQueryOptions.MaxResolvedFilterIds`
(預設 **5000**) 為那趟解析的每一步設下上限:葉節點查詢、每一次回走的跳躍，以及可翻譯搜尋的
聯集。它之所以存在，是因為上面那些上限限制的是**結果頁**，而不是這個中間集合——少了它，一個
刻意放寬的條件 (`?filter[category.name][_contains]=a`、`?search=a`) 就要付出 O(表大小) 的記憶體
外加一句巨大的 SQL，而且任何持有讀取授權的呼叫者都能觸發，包括在 `public` 授予讀取之處的匿名
呼叫者。

超過上限屬於用戶端錯誤，而不是截斷:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Bcategory.name%5D%5B_contains%5D=a"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Resolving 'category.name' matched too many rows (7412, limit 5000). Narrow the filter or search term, or raise Query:MaxResolvedFilterIds."}}
```

改成截斷的話，會默默丟掉符合條件的資料列並回傳靜靜出錯的結果，所以這個查詢是被拒絕的。如果
某個 fork 的正當篩選會解析出更大的集合，就把 `Query:MaxResolvedFilterIds` 調高——代價是記憶體
加上 SQL 語句大小，每個 uuid 大約 40 個位元組的語句文字。

## 排序

`sort=` 是一份逗號分隔的欄位名稱清單，每一個都可以選擇性地加上 `-` 前綴代表降冪，並依給定的
順序套用 (多鍵排序):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=-size,fileName"
{"success":true,"data":[{"id":"...","fileName":"gamma-draft.txt","size":35, ...},{"id":"...","fileName":"beta-notes.txt","size":23, ...},{"id":"...","fileName":"alpha-report.txt","size":20, ...}],"meta":{"total":3,"limit":25,"offset":0}}
```

一個排序鍵也可以是一個帶點號的關聯路徑，但僅限於每一跳都是 many-to-one 的情況——第 7 章完整
涵蓋這一點 (以及它對 to-many 的拒絕)。

## 分頁與 `meta`

沒有 `page` 頁碼這種東西——分頁只有 `limit`/`offset`。`QueryValidator.Validate` 會把未設定或
非正數的 `limit` 夾在 `StruoQueryOptions.DefaultLimit` (25)，把任何更大的值夾在
`StruoQueryOptions.MaxLimit` (100);`offset` 則以 0 為下限。每一個清單回應都帶有一個 `meta`
物件，內含*有效* (夾限後) 的 `limit`/`offset`，以及符合篩選條件的資料列總數 (分頁之前的總數):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=fileName&limit=2&offset=1"
{"success":true,"data":[{"fileName":"beta-notes.txt", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":3,"limit":2,"offset":1}}
```

## 欄位投影

`fields=` 限制投影出集合的哪些**自有**欄位——`id` 與樂觀並行控制用的 `version`，無論如何都
一律會被納入，而一個集合的 `translations` 對應表 (第 6 章)，以及任何 `deep` 展開的關聯，都與
`fields=` 完全無關 (它們是在 `ItemProjector` 的自有欄位選取執行之後，由各自獨立的階段附加上去
的):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?fields=id,fileName&sort=fileName"
{"success":true,"data":[
  {"id":"...","version":1,"fileName":"alpha-report.txt","translations":{"en":{"title":"alpha-report","alt":null},"zh-TW":{"title":"alpha 報告","alt":"Alpha 報告圖示"}}},
  {"id":"...","version":2,"fileName":"beta-notes.txt","translations":{"en":{"title":"beta-notes","alt":null}}},
  {"id":"...","version":0,"fileName":"gamma-draft.txt","translations":{"en":{"title":"gamma-draft","alt":null}}}
],"meta":{"total":3,"limit":25,"offset":0}}
```

注意 `contentType`/`size`/……都不見了 (沒有被請求)，但 `translations` 仍然存在，即使它並沒有
被列在 `fields=` 中。

## 關聯展開

第 7 章完整涵蓋;從查詢 DSL 的角度來看，`deep=` 只是另一個 `QueryModel` 欄位，會在篩選/排序/
分頁之後，針對已經取回的那一頁父資料列做解析:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deep=folder&filter%5BfileName%5D%5B_eq%5D=alpha-report.txt"
{"success":true,"data":[{"fileName":"alpha-report.txt", ..., "folder":{"id":"...","name":"Guides", ...}}], ...}
```

## 軟刪除篩選 (`deleted=`)

`DeletedFilter` (`src/Struo.Domain/Query/DeletedFilter.cs`) 有三個值——`Exclude` (預設)、
`Only`、`With`——從兩個 `GET` 端點上的 `?deleted=exclude|only|with` 讀取。要求預設值以外的
任何值，都需要對該集合有刪除權限 (`DeletedAccessGuard`，在 `ItemsController` 自身強制執行，
因為 `ItemService.QueryAsync`/`GetAsync` 對查詢一律只檢查讀取權限)。已對 `file` (實作了
`ISoftDeletable`) 做即時環境驗證，做法是把一列移入回收桶，再重新查詢全部三種模式:

```
$ curl -s -X DELETE http://localhost:5221/api/items/file/<beta-id> -H "X-Struo-CSRF: 1" -b cookies.txt
# 204 No Content

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=fileName"
{"success":true,"data":[{"fileName":"alpha-report.txt", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":2, ...}}   # exclude (default): trashed row hidden

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=only"
{"success":true,"data":[{"fileName":"beta-notes.txt", ...}],"meta":{"total":1, ...}}   # only: exclusively the trashed row

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=with&sort=fileName"
{"success":true,"data":[{"fileName":"alpha-report.txt", ...},{"fileName":"beta-notes.txt", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":3, ...}}   # with: all three
```

一個無效的值會被直接拒絕:`"Query parameter 'deleted' must be exclude|only|with."`

## 驗證:白名單、未知路徑，與深度上限

每一個被 `filter`/`sort`/`fields=` 項目指名的自有欄位名稱，都會對照集合自身非隱藏的
`[CmsField]`，再加上它宣告的 many-to-one 外鍵做檢查 (所以你可以用例如 `folderId` 做篩選/
排序，即使它本身不帶任何 `[CmsField]`)——除此之外沒有任何東西是可觸及的，而且一個 `Hidden`
欄位，甚至會被刻意排除在這份許可清單之外 (第 5 章)，因為一個外形像憑證的隱藏欄位，如果仍然
可被篩選，就會讓 `meta.total` 變成一個逐字元的萃取神諭。一個未知的自有欄位，不論它看起來像是
打錯字還是刻意的探測，都會以相同的方式被拒絕:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bbogus%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogus' on collection 'file'."}}
```

一個帶點號的關聯路徑，會逐跳對照實際的關聯圖做解析與白名單驗證 (第 7 章):一個無法解析的關聯
片段、終端集合上一個無法解析的葉欄位，以及一個超過 6 跳深度上限的路徑，三者各自會以自己精確的
訊息被拒絕——這三種情況都已在第 7 章即時驗證過。`fields=` 額外完全不允許關聯路徑
(`allowRelation: false`)——投影一律只會選取集合自身的純量欄位，絕不會選取一個巢狀關聯的欄位。

## 端對端完整範例

把好幾個部分組合進同一個請求中——一個比較篩選、降冪排序、分頁，以及欄位投影，全部濃縮進一個
`GET` 裡:

```
$ curl -s -b cookies.txt \
    "http://localhost:5221/api/items/file?filter%5Bsize%5D%5B_gte%5D=20&sort=-size&limit=2&offset=0&fields=id,fileName,size"
{"success":true,"data":[
  {"id":"...","version":0,"fileName":"gamma-draft.txt","size":35,"translations":{"en":{"title":"gamma-draft","alt":null}}},
  {"id":"...","version":2,"fileName":"beta-notes.txt","size":23,"translations":{"en":{"title":"beta-notes","alt":null}}}
],"meta":{"total":3,"limit":2,"offset":0}}
```

## 接下來該去哪

- 第 6 章 [國際化](06-internationalization.md)，說明 `?locale=`，以及一個可翻譯欄位如何在
  有效查詢語言下被篩選/排序。
- 第 7 章 [關聯](07-relations.md)，完整詳述帶點號路徑篩選、關聯路徑排序、`deep` 展開，以及
  深度上限。
- 第 9 章 [REST API](09-rest-api.md)，取得回應信封格式、錯誤代碼目錄，以及本章
  寫入/`POST query` 範例所用到的 `X-Struo-CSRF` 標頭。
- 第 10 章 [GraphQL API](10-graphql-api.md)，說明同一套篩選/排序/分頁功能，如何改以型別化的
  GraphQL 引數表達，而非查詢字串/JSON 信封慣例。
