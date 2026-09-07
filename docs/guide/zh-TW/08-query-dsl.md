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
`POST`，`POST /query` 並不需要*寫入*授權。不過在以 cookie 驗證時，它確實需要 `X-Struo-CSRF` 標頭:
CSRF 規則看的是 HTTP **方法**，而不是讀取或寫入，所以一個唯讀的 `POST` 與一次 mutation 受到完全相同
的把關(完整規則與理由見第 9 章)。這一點最常讓人絆倒，所以在此實測一次:

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
| 搜尋 (Search) | `search=text` | `"search": "text"` | 自由文字 `LIKE`，以 OR 連接跨越每一個 `Searchable` 欄位 (第 4 章);在同一個請求上與 `filter` 以 AND 組合——一列必須同時滿足 filter*且*符合搜尋詞，而非兩者擇一。當一個 fork 註冊了 `ISearchProvider` 且它回應了這個請求時，一組候選 id 會直接取代 `LIKE` 搜尋——見下方的[搜尋提供者（Search providers）](#搜尋提供者-search-providers)。 |
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
`_and`/`_or` 底下多少層，也包含 `_some`/`_none` 量詞自身內層 filter 裡的每一個葉節點 (下文)
——一個量詞的內層 filter 不會有自己獨立的額度;超過這個上限，會在任何查詢執行之前擲出
`"Too many filter conditions (max 50)."`。

帶點號的 (跨關聯) 篩選，以及對可翻譯欄位的搜尋，兩者都是把條件下推成一個巢狀 SQL 子查詢來回答
的——確切形狀見第 7 章——而不是先在記憶體中解析出一組中間 id 集合。正因如此，這裡沒有一個類似
上方 `MaxFilterConditions` 的上限:一個跨關聯條件的代價恰好是一道子查詢，不論它可能比對到多少
資料列，而不是與某個中間集合大小成正比的記憶體——已經沒有中間集合這種東西可供上限約束了。

## 關聯量詞:`_some`/`_none`

第 7 章完整涵蓋語意 (各自存在 vs. 同一列、`_junction`、many-to-one 的情況、NULL 安全與軟刪除
規則);這一節是兩種請求形式的文法參考，外加保留字詞與錯誤目錄。

**保留字詞。** `_some`、`_none`、`_junction` (查詢字串／JSON envelope 拼法) 與 `some`、`none`、
`junction` (GraphQL 拼法)，都跟 `_and`/`_or` 一樣被保留——不可以用這些名稱替任何欄位或關聯命名
(`FilterReservedTokens.All`，`src/Struo.Application/Query/FilterReservedTokens.cs`);一個違反此
規則的集合，會在啟動時就快速失敗，早於任何請求被服務之前。

**JSON envelope (完整形式)。** 一個欄位物件的鍵是 `_some` 或 `_none`，其值是一個以關聯目標
集合為根的完整 `filter` 物件——遞迴地與任何頂層 `filter` 同一套文法，所以它本身可以再包含
帶點號路徑、巢狀的 `_some`/`_none`、`_junction`，以及一層 `_and`/`_or`:

```json
{"filter":{"tags":{"_some":{"name":{"_eq":"Guide"},"_junction.note":{"_contains":"hero"}}}}}
{"filter":{"tags":{"_none":{"name":{"_eq":"internal"}}}}}
```

一個欄位物件可以同時帶有 `_some` 與 `_none`，作為兩個獨立述詞 (AND 連接)，但不能在同一個欄位
物件中，把任一個跟一個單純的純量運算子 (`_eq`、`_contains`、……) 混用——一個關聯路徑本身沒有
任何純量運算子:

```
$ curl -s -X POST http://localhost:5221/api/items/article/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"tags":{"_some":{"name":{"_eq":"Guide-u3doc0905"}},"_eq":"someval"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'tags' mixes a relation quantifier with scalar operators; a relation path has no scalar operators."}}
```

**查詢字串 (折疊的簡寫形式)。** `filter[<prefix>._some.<inner path>][<op>]=<value>` (或
`_none`) 在一般查詢字串剖析之後，會被折疊 (`RelationQuantifierFolder.Fold`，
`src/Struo.Application/Query/RelationQuantifierFolder.cs`) 進同一棵 `_some`/`_none` 樹:每一個
路徑中含有 `_some`/`_none` 片段的條件，都依 (該片段*之前*的路徑前綴、該量詞) 分組，同一組裡的
每一個條件都會合併成一個述詞，以 AND 連接——一個巢狀量詞 (`a._some.b._none.c`) 會在內層那一組
上再次遞迴折疊。**在查詢字串上，一次請求對每一組 (前綴、量詞) 只能表達一組**——兩個前綴與量詞
相同的條件，不論分散在多少個 `filter[...]` 鍵值裡，永遠會落進同一組;查詢字串沒有辦法表達針對
同一個關聯的兩個*獨立*同一列述詞 (例如「某個 tag 名為 `a`，或者一個*不同的* tag 是紅色」，放在
`_or` 之下)——那需要改用 JSON envelope，它的 `_some` 值是你明確自行建構的一個單一 filter 物件。

一個量詞片段之後必須至少再接一個路徑片段 (它所量化的條件)——它不能是路徑的最後一個片段:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5Btags._some%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"'tags._some': '_some' must be followed by a condition on the related collection."}}
```

兩個量詞片段也不能緊接在一起 (`a._some._none.b`)——請改用 envelope 形式的內層 filter 表達一個
巢狀的 `_some`/`_none`。

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

## 搜尋提供者（Search providers）

`search=` (上文) 預設由內建的 `LIKE` 掃描回應。一個 fork 可以改為註冊 `ISearchProvider`
(`src/Struo.Application/Search/ISearchProvider.cs`)，把自己的搜尋引擎 (Meilisearch、
Elasticsearch、PostgreSQL 全文搜尋……) 插進同一個請求——這正是本節要說明的刻意擴充接縫;第 1 章的
「什麼可以換、什麼不能換」把它與 `IFileStorage` 並列。預設的註冊 `NullSearchProvider` 從不處理任何
搜尋，所以在沒有 fork 提供 provider 的情況下，上面的 `LIKE` 路徑會完全維持原樣不受影響。

### 契約

`ItemService.QueryAsync` 只在 `search=` 非空白，且僅限清單請求 (絕不用於單筆 `GET`) 時，向已註冊的
`ISearchProvider` 詢問一次，帶著一個 `SearchRequest`
(`src/Struo.Application/Search/SearchRequest.cs`):

| 欄位 | 意義 |
|---|---|
| `Collection` | 集合的標準 camelCase 名稱 (`CollectionMetadata.Name`，例如 `article`)——不論 REST 路由區段本身是用什麼大小寫 (`/api/items/Article` 也能不分大小寫地解析)，provider 看到的永遠是同一個標準形式。 |
| `Term` | `search=` 的值，逐字保留——不修剪、不轉小寫。 |
| `Locale` | 有效查詢語言 (明確的 `locale=`，或站台預設值)。 |
| `SearchableFields` | Core 的 `Searchable && !Hidden` 欄位名稱——僅供參考;provider 可以索引完全不同的欄位。 |

Provider 以一個 `SearchOutcome` (`src/Struo.Application/Search/SearchOutcome.cs`) 回應，二選一:

- **`SearchOutcome.NotHandled`**——core 內建的 `LIKE` 搜尋會照常執行，就跟沒有註冊 provider 時完全
  一樣。
- **`SearchOutcome.Candidates(ids)`**——給定的根 id (以字串形式) 會變成一個 `id IN (...)` 條件，
  直接取代 `LIKE` 搜尋;搜尋詞本身不會再被參照。**空**候選清單是一次已處理、零命中的搜尋 (`id IS
  NULL`)，絕不會回退到 `LIKE`——一個確實沒找到任何東西的 provider 仍必須回傳 `Candidates([])`，
  而不是 `NotHandled`。

### 如何與請求的其餘部分組合

- **與 `filter` 是 AND，不是取代它。** 候選條件與 `filter` 都會套用——一列必須同時滿足 filter
  *且*是其中一個候選 id，而非兩者擇一。
- **與 `deleted=`、權限、`Hidden` 各自獨立。** Core 的讀取權限檢查是集合層級，不是逐列的
  (`ItemService.QueryAsync` 在向 provider 詢問之前就已經跑過 `CanRead`，若失敗早已擲出
  `PermissionDeniedException`)，所以沒有任何逐列 RBAC 是候選 id 可以繞過的;`Hidden` 欄位的處理
  同樣是逐欄位的，與哪些列符合資格無關。唯一會在下游過濾候選列的是軟刪除模式:一個目前落在請求的
  `deleted=` 模式之外的候選 id (例如預設 `exclude` 下一列已被移入回收桶) 會跟任何其他資料列一樣
  被排除。
- **清單、每一個 facet，以及彙總，都共用同一組候選集** (見下方「Facets 與彙總」)——
  `ItemService.QueryAsync` 只解析候選一次，並把同一個 `QueryModel` 貫穿分頁查詢、每一個 facet，
  以及彙總，所以三者回報的都是完全相同的資料列。
- **候選的順序不影響結果順序。** 候選只會縮小*哪些*列符合資格;`sort=` (或它的缺席) 仍然完全照舊
  決定列的順序。這個版本並不會保留 provider 自己的相關性排序——保留它是一個可能的後續項目，不是
  這個版本提供的東西。

### id 信任邊界與上限

Provider 回傳的 id 是一個**信任邊界**，不是使用者輸入:`SearchCandidateResolver`
(`src/Struo.Application/Search/SearchCandidateResolver.cs`) 會在每一個 id 進入 SQL 之前，先把它
解析為集合主鍵的 CLR 型別，因為 `FilterTranslator` 會把最終的 `id IN (...)` 渲染成型別化的字面值。
只支援 `Guid` 或整數型別的主鍵 (`long`/`int`/`short`);其他任何主鍵型別都會被拒絕。候選數量同時也受
`Query:MaxSearchCandidates` (第 3 章，預設 **1000**) 上限約束。一個無法解析的 id，以及一個超過上限
的數量，都是 **provider 違反契約，而非使用者的錯誤**——它們會擲出 `InvalidOperationException`
(→ `INTERNAL_SERVER_ERROR`/500)，絕不是 `QueryException` (→ `BAD_USER_INPUT`/400)，因為呼叫端什麼
都沒做錯;是 fork 的 provider 有問題。`StruoExceptionHandler.Map` 會在伺服器端以 `Error` 等級記錄每
一個 `INTERNAL_SERVER_ERROR` 情況，所以一個行為異常的 provider 的違規，對維運者而言並不是無聲的，
即使客戶端看到的只是遮蔽過的通用訊息。同一個 provider 回應中重複的 id 會被靜默去重。

### 當 provider 本身無法回應時

一個完全無法回應的 provider (連線被拒、逾時、索引缺失) 應該擲出 `SearchUnavailableException`
(`src/Struo.Domain/Query/SearchUnavailableException.cs`)，而不是回傳 `NotHandled`——這樣請求就會以
`SEARCH_UNAVAILABLE`/503 (第 9 章) 失敗，而不是無聲降級成呼叫端可能沒有預期到的 `LIKE` 掃描。一個
想優雅降級到 `LIKE` 的 provider，可以在內部自行攔截它自己的例外並改回傳 `NotHandled`；兩者都是合法
的選擇，這道接縫並不強制其中一種。

### 註冊一個 provider

`AddStruoData()` 以 `TryAddScoped` 註冊 `NullSearchProvider`，所以一個 fork 只需要在呼叫
`AddStruoData()` *之後*加入自己的註冊 (在它*之前*註冊也一樣有效，因為 `TryAddScoped` 只有在已經有
東西被註冊時才會退讓):

```csharp
public sealed class StaticSearchProvider : ISearchProvider
{
    public Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        if (request.Collection != "article") return Task.FromResult(SearchOutcome.NotHandled);
        IReadOnlyList<string> ids = MyIndex.Lookup(request.Term, request.Locale); // your engine call
        return Task.FromResult(SearchOutcome.Candidates(ids));
    }
}
// Program.cs, after AddStruoData():
builder.Services.AddScoped<ISearchProvider, StaticSearchProvider>();
```

除非 provider 真的是無狀態的，否則請以 scoped (或 transient) 生命週期註冊:一個以 singleton
註冊、卻捕捉了 scoped 相依 (`DbContext`、`ISqlSugarClient` 或類似物件) 的 provider 是一個
captive dependency——它要嘛在啟動時因 `ValidateScopes` 而擲出例外，要嘛更糟，在整個應用程式的
生命週期中，都默默重複使用第一個請求的 scoped 實例。

### Core 不做什麼

讓 fork 的搜尋索引與寫入 (新增/更新/刪除) 保持同步——也就是寫入側索引同步——刻意**不是** core 的
一部分:索引策略 (同步、排入佇列、批次) 取決於 fork 選擇的搜尋引擎，所以現階段這是 fork 自己的責任；
一個寫入側同步 hook 是一個可能的未來項目，不是這個版本提供的東西。

## Facets 與彙總

另外還有兩個僅限清單使用的參數，會針對*整個*已篩選的結果集 (而不只是目前這一頁) 計算摘要資料:
`facets=` (分面計數，每個請求路徑各一份 value/count 的分佈) 與 `aggregate[<op>]=` (`sum`/`avg`/`min`/`max`/
`count`，僅限自有欄位)。兩者都不會改變 `data` 或分頁——只會在 `meta` (REST) 或清單包裝物件
(GraphQL，第 10 章) 上多加鍵值/欄位——而且除非被要求，否則兩者都完全不會被計算:一個一般請求的
SQL 與回應完全不受影響 (`QueryValidator`/`ItemService` 在值為 `null` 時直接短路)。

```
?facets=status,categoryId,tags,category.name
?aggregate[sum]=price&aggregate[max]=price,rating&aggregate[count]=publishedAt
```

- `facets`——一份 facet 路徑的逗號清單 (查詢字串)，或同樣字串組成的 JSON 陣列 (信封)。每一條
  路徑都是下面四種形態之一。
- `aggregate[<op>]`——每個 op (`count`/`sum`/`min`/`max`/`avg`) 各一個查詢字串鍵，值是自有欄位的
  逗號清單;信封形式是 `"aggregate": {"sum": ["price"], "max": [...]}`，每個 op 各一個陣列。未知的
  op 會被拒絕:`"Unknown aggregate op 'x'."`。

兩者都能與本章其餘的一切組合——`filter`、`search`、`sort`、`limit`/`offset`、`deep`、`locale`、
`deleted`——並由同一個為 `filter`/`sort`/`fields` 做白名單驗證的 `QueryValidator`
(`ValidateFacets`/`ValidateAggregate`，`src/Struo.Application/Query/QueryValidator.cs`) 驗證，所以
一個未知的路徑、一個無法作為 facet 的欄位，或一個不相容的彙總 op，都會在任何 facet／彙總
SQL 執行之前就以 `BAD_USER_INPUT` 讓請求失敗——與一個未知的 filter 欄位完全相同。不過 `deleted=`
(基本語意見下文「軟刪除篩選」) 只會與**根**集合自己的資料列組合:一個 to-many facet 的關聯／
junction 端查詢，永遠不會解除軟刪除篩選，不論外層請求的 `deleted=` 模式為何——舉例來說，一個
`deleted=with` 請求的 `tags` facet，仍然會排除一個已被移入回收桶的標籤，即使文章資料列本身包含了
已回收的項目。

### 四種 facet 路徑形態

`FacetPathResolver.Resolve` (`src/Struo.Application/Query/FacetPath.cs`) 只接受下面這幾種形態——
絕不超過一個關聯跳，也絕不能是量詞或 `_junction` 片段:

| 形態 | 範例 | 依什麼分組 | `value` |
|---|---|---|---|
| 自有純量欄位 | `status` | 根欄位本身 | 欄位自身的值;`null` 有自己的 bucket |
| Many-to-one 外鍵 | `categoryId` | 根外鍵欄位 | 目標 id 的字串形式;`null` 有自己的 bucket |
| 關聯名稱 (任何種類) | `category`、`tags`、`articles` | 目標 id (M2O:根外鍵;O2M:子項自己的 id;M2M:junction 的目標外鍵) | 目標 id 的字串形式;**沒有**「無關聯」的 bucket |
| 一個關聯跳 + 一個葉欄位 | `category.name`、`tags.name`、`articles.title` | 同樣依目標 id 分組，再把葉值換入 | 葉欄位的值;一個可翻譯的葉欄位會使用查詢有效語言下的 sidecar 資料列，一個在該語言沒有翻譯列的 id 會得到一個 `null` bucket |

一個可作為 facet 的欄位，其 interface 必須是下列之一:`Text`、`Textarea`、`Slug`、`Email`、`Url`、
`Color`、`Phone`、`Select`、`Radio`、`Number`、`Slider`、`Rating`、`Boolean`、`Checkbox`、`Date`、
`DateTime`、`Time`、`Uuid`、`File`、`Image` (`FacetPathResolver.Facetable`) ——自有欄位與葉欄位皆然。
長文字 (`RichText`/`Markdown`/`Code`)、每一種多值 interface (`MultiSelect`/`CheckboxGroup`/`Tags`/
`Repeater`)、結構化資料 (`Json`/`KeyValue`/`Files`)，以及 `Hidden`/`Divider`/`Password`，全部都會被
拒絕——一個 `Hidden` 欄位會得到跟一個無法解析的名稱完全相同的 `"Unknown field"` 訊息，所以 facet 路徑
永遠無法被用來探測一個隱藏欄位是否存在。

### 自排除 (disjunctive) 計數與剪枝規則

一個 facet 回答的是「如果我改選這條路徑的每一個候選值，我請求裡其餘的一切還會匹配多少列？」——
而不是「我*目前*結果集裡有多少列帶這個值？」。要得到前者，`FacetFilterPruner.Prune`
(`src/Struo.Application/Query/FacetFilterPruner.cs`，一個對已驗證的 `FilterNode` 樹做運算的
純函式) 會針對每一個 facet，先移除同一個欄位/關聯*家族*上的每一個條件，才開始計數:一個自有
欄位對自己的條件;一個 many-to-one 外鍵對外鍵欄位本身*以及*任何以 `<relation>.` 開頭的帶點號路徑
*以及*一個針對該關聯的 `_some`/`_none` 量詞——`categoryId` 與 `category.name` 屬於同一個家族，會
一起被剪掉。`search` 永遠不會被剪;**`aggregate` 永遠不會被剪**——它一律針對請求完整、未剪枝的
filter 執行。一個被剪到空的邏輯群組會塌縮 (0 個子節點 → 整個節點消失，1 個倖存子節點 → 該群組被它
取代)，所以一個完全以分類為範圍的 filter，在對 `category`/`categoryId`/`category.*` 做 facet 時，
可能會整個剪成沒有任何 filter。

實際對照一個小型 fixture (一個分類「Facet Demo」底下三篇文章——兩篇 `published`、一篇 `draft`，
兩篇共用一個標籤):只篩選出草稿，facet 仍然回報兩種狀態都存在，因為 `status` 條件在 `status`
facet 被計數之前就已經被移除:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5BcategoryId%5D%5B_eq%5D=<category-id>&filter%5Bstatus%5D%5B_eq%5D=draft&facets=status"
{"success":true,"data":[{"id":"...","status":"draft", ...}],"meta":{"total":1,"limit":25,"offset":0,"facets":{"status":[{"value":"published","count":2},{"value":"draft","count":1}]}}}
```

`data`/`total` 是唯一符合的那篇草稿，跟 `filter` 說的一致——但 `facets.status` 顯示
`published: 2` 與 `draft: 1`，這正是把 `status` 條件輪流換成每個候選值、同時保留 `categoryId`
不變會得到的結果。這同時也是驗收 oracle:對一個自有欄位 facet 而言，`{"value": v, "count": n}`
必須等於把同一請求的 facet 自身條件換成 `filter[field][_eq]=v` 之後的 `meta.total`——上面已經
驗證過 (`filter[status][_eq]=published` 在同一個分類上，獨立回傳的正是 `"total":2`)。

對*被剪枝*的家族本身做 facet，正好展示了「剪到什麼都不剩」的情況:在
`filter[categoryId][_eq]=<category-id>` 之外一併請求 `category.name`，會在計數 `category.name`
之前把該 filter 完全移除，所以這個 facet 會涵蓋資料庫裡的每一個分類，而不只是清單原本篩選到的
那一個——實際對照這個 host 共用的開發 fixture (包含其他驗證流程留下、彼此無關的分類，因為被剪掉
的 filter 已經沒有任何東西可以拿來限縮範圍):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?filter%5BcategoryId%5D%5B_eq%5D=<category-id>&facets=status,tags,category.name&aggregate%5Bcount%5D=publishedAt&aggregate%5Bmax%5D=publishedAt"
{"success":true,"data":[ ... 3 articles ... ],"meta":{"total":3,"limit":25,"offset":0,
  "facets":{
    "status":[{"value":"published","count":2},{"value":"draft","count":1}],
    "tags":[{"value":"<tag-id>","count":2}],
    "category.name":[{"value":"Facet Demo","count":3},{"value":"GateCat A 1783050271","count":3},{"value":"catA-LG8c3b","count":3},{"value":"catC-LG8c3b","count":3},{"value":"E2E Guard Save b21784859754","count":2},{"value":"E2E Guard Save e2e saved","count":2},{"value":"Cat9c-1343377572","count":1},{"value":"Cat9c-1885194788","count":1},{"value":"GateCat B 1783050752","count":1},{"value":"GateCat B 1783051807","count":1},{"value":"catB-LG8c3b","count":1},{"value":"cjkDiagCat","count":1},{"value":"diagCat-r3","count":1}]
  },
  "aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-09-03T00:00:00"}}
}}
```

`status` 與 `tags` 仍然維持在該分類的範圍內 (它們的家族——一個自有欄位，以及一個與
`category`/`categoryId` 無關的關聯——不受 `category.name` 自己家族被剪枝的影響)，但
`category.name` 涵蓋了這個 host 上的每一個分類。`aggregate` 則完全無視剪枝:`publishedAt` 上的
`count`/`max` 是針對*原始* `categoryId` filter 計算的，只匹配這個分類裡兩篇已發布的文章，不論
同一個請求裡還一併要求了哪些 facet。

JSON 信封形式 (`POST /api/items/article/query`) 與上面同一個請求的查詢字串形式，逐位元組完全等價:

```
$ curl -s -X POST http://localhost:5221/api/items/article/query -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"filter":{"categoryId":{"_eq":"<category-id>"}},"facets":["status","tags","category.name"],"aggregate":{"count":["publishedAt"],"max":["publishedAt"]}}'
# identical response to the query-string request above
```

一個可翻譯的葉欄位，對每一個在有效語言沒有翻譯列的 id，會退回一個 `null` bucket——實際對照這個
分類，對它的 `articles.title` (一個一跳的 O2M 葉，落在翻譯 sidecar 上) 做 facet，三篇文章裡只有
一篇帶有 `zh-TW` 翻譯:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Bname%5D%5B_eq%5D=Facet%20Demo&facets=articles.title"
{"success":true,"data":[{"id":"...","name":"Facet Demo", ...}],"meta":{"total":1,"limit":25,"offset":0,"facets":{"articles.title":[{"value":"Facet Demo Article One","count":1},{"value":"Facet Demo Article Three Draft","count":1},{"value":"Facet Demo Article Two","count":1}]}}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/category?filter%5Bname%5D%5B_eq%5D=Facet%20Demo&facets=articles.title&locale=zh-TW"
{"success":true,"data":[{"id":"...","name":"Facet Demo", ...}],"meta":{"total":1,"limit":25,"offset":0,"facets":{"articles.title":[{"value":null,"count":2},{"value":"Facet Demo Article Two zh","count":1}]}}}
```

在預設語言下三個標題都會出現 (每篇文章都有一個 `en` 資料列);在 `zh-TW` 下只有那篇已翻譯的文章
保留自己的值，另外兩篇——沒有 `zh-TW` 資料列——會塌縮成同一個 `{"value": null, "count": 2}` bucket。

### Count 的定義與一項已知限制

一個 facet 的 `count` 永遠是符合該值的**相異根資料列**數量，絕不是相關資料表上的原始列數。對一個
to-many 關聯 (`tags`) 或一個 to-many 葉 (`articles.title`) 而言，底層 SQL 是依**目標 id** 分組——
即使是葉形態 (上一節「一跳 + 葉」那一列) 也絕不會直接依葉值分組——並使用
`COUNT(DISTINCT <指向根的欄位>)` (`SqlFunc.AggregateDistinctCount`) 而不是單純的 `COUNT(*)`——
否則一個同時連到兩個共用同一個值的目標的根資料列會被算兩次。一個自有欄位或 many-to-one 外鍵的
facet 完全不需要 `DISTINCT`:每一列根資料列本來就只會貢獻到一個群組。

**已知限制**，這是一跳加葉這種形態固有的:它先在一次查詢裡依目標 *id* 把根資料列分組，接著才把
葉值換進去、在記憶體中合併相同值的群組 (第二次查詢) ——這是刻意的設計，如此才不需要任何 join
機制，而且一個可翻譯葉欄位的 sidecar 查找，也剛好落在同一個形狀裡。這代表一個同時連到**兩個
恰好共用同一個葉值**的不同目標的根資料列 (例如一篇文章上兩個都叫做 `"Guide"` 的標籤)，在那個
共用值上會被算兩次——那兩個以目標 id 分組的群組 (各自在自己的目標 id 底下都已經是正確的相異根
計數) 會一起塌縮進合併後的葉值 bucket，兩者的 count 會被直接加總，而不是在合併之後把根 id 的
聯集重新去重。這是一項刻意的取捨，不是缺陷:要修正它，就需要在合併過程中攜帶根 id 集合，而不是
只帶 count，如此一來就再也塞不進兩次查詢裡了。

兩步形態帶來的第二個、與此相關的後果:`MaxFacetValues` (下一節) 是在葉值合併**之前**，先對第一次
查詢的**目標 id** bucket 套用上限——而不是對最終、合併後的葉值 bucket。因此，即使目標集合裡相異
葉值的總數少於 `MaxFacetValues`，一個葉值仍然可能從回應中消失，只要第一道上限保留下來的、
高 count 的目標 id，恰好沒有涵蓋到帶著那個葉值的 id。

### NULL bucket

一個自有欄位或 many-to-one 外鍵的 `NULL` 值會得到自己的 bucket (`{"value": null, "count": n}`) ——
自有欄位與外鍵 facet 是依一個真正屬於根資料列一部分的欄位分組，所以 `NULL` 是一個真實、可計數的
群組。一個裸關聯名稱的 facet (`category`、`tags`、`articles`) 絕不會有「無關聯」的 bucket:要計算
「有多少根資料列*沒有*相關資料列」需要一個目前實作沒有建構的反向 join，而實務上一個想要這個數字的
呼叫端已經有 `_none` 可用 (上文第 7/8 章)。一個可翻譯葉欄位的 `null` bucket (前一節) 是第三種、
不同的情況:它代表「目標 id 解析成功，但在這個語言下沒有翻譯列」，而不是「沒有目標」。

### 排序與數值上限

每一個 facet 都會依 count **降冪**排序，再以 value **升冪**作為決勝，並截斷到
`StruoQueryOptions.MaxFacetValues` (預設 50) ——沒有 `otherCount` 餘量。對自有欄位、外鍵，以及
裸關聯名稱這三種形態而言，這個排序與截斷是在資料庫層完成的 (`OrderBy` + `Take`，
`FacetQueries.Group`)，而 `NULL` bucket 在 count 完全打平時落在哪個位置，取決於資料庫引擎——因為
不論 SQLite 或 PostgreSQL，都沒有被明確要求 `NULLS FIRST`/`NULLS LAST`:SQLite 預設的
`ORDER BY value ASC` 把 `NULL` 排在最前面，PostgreSQL 的預設則把它排在最後面。

一跳加葉這個形態不同:它以目標 id 分組的 bucket，跟上面一樣在資料庫層排序/截斷，但接下來的葉值
合併 (前一節) 會對*合併後*的 bucket 在記憶體中重新排序與重新截斷 (`SwapLeafValues`，
`FacetQueries.Leaf.cs`) ——一樣是 count 降冪，再用一個自訂的 `LeafValueComparer` 做 value 的決勝，
它會無條件把 `null` 排在**最後**，不論在哪個引擎上，而不是像另外三種形態那樣，依賴資料庫自身
NULL 排序的預設值。

### 這項功能要付出多少次查詢

一份單純的清單是 2 條陳述式 (`COUNT` + 該頁的 `SELECT`)，這項功能不會改變這一點。每一個請求的
facet 會多加恰好 1 條 (自有欄位、外鍵，或裸關聯名稱) 或 2 條 (一跳加葉的 facet——id/count 查詢
再加上葉值查找);`aggregate` 每 10 個請求的 op/欄位 slot 多加 1 條陳述式，以固定常數
`AggregateRow.SlotCount = 10` (`src/Struo.Infrastructure/Query/FacetRow.cs`，`AggregateQueries` 的
`slots.Chunk(AggregateRow.SlotCount)`) 分批——**不是**依 `Query:MaxAggregates`，後者只限制一個請求
總共可以指名多少個 op/欄位 slot (`ValidateAggregate`)，跟分批大小無關。在預設的 `Query:MaxAggregates`
(10) 之下，每一個合法請求的彙總欄位都恰好塞得進那唯一的一個 10-slot 分批，所以永遠只花一條彙總
陳述式;但若某個 fork 把 `Query:MaxAggregates` 調高超過 10，每多請求 10 個欄位就會多花一條彙總
陳述式，因為分批用的常數本身並不會跟著調整。`StruoQueryOptions.MaxFacets` (預設 10) 限制了 facet
的數量，所以在預設值下，單一請求最糟的情況是 `2 + 2·MaxFacets + 1`——更一般地說，是
`2 + 2·MaxFacets + ⌈彙總欄位數 / 10⌉`。一個已註冊的 `ISearchProvider` 回應候選路徑，並不會改變這個
計數:provider 呼叫只發生一次，在資料庫之外、查詢執行之前——它只是把同樣這些陳述式裡的 `LIKE`
群組換成一個 `id IN (...)` 條件，不會多加任何查詢。

### 彙總 op

| Op | 適用對象 | 結果 |
|---|---|---|
| `count` | 任何可見的自有欄位，包含 many-to-one 外鍵——計算非 `NULL` 的資料列數 | 整數 (空集合為 `0`，絕不是 `null`) |
| `sum`、`avg` | `Number`、`Slider`、`Rating` | `sum`:欄位自身的數值型別 (整數欄位 → 較寬的整數、`decimal` → `decimal`、`double`/`float` → `double`);`avg`:一律是 `double`，不論欄位本身的型別為何 |
| `min`、`max` | 上面三種 interface，再加上 `Date`、`DateTime` | 欄位自身的型別 |

除了 `count` 之外的每一個 op，在符合條件的集合為空時都會回傳 `null`，而不是 `0`/`NaN`。實際對照
fixture 分類裡兩篇已發布文章的 `publishedAt` 上的 `count`/`max` (第三篇還是草稿，所以它的
`publishedAt` 是 `null`，不計入):

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { articles(filter: { categoryId: { eq: \"<category-id>\" } }, facets: [\"status\", \"tags\"], aggregate: { count: [\"publishedAt\"], max: [\"publishedAt\"] }) { total facets { field values { value count } } aggregate } }"}'
{"data":{"articles":{"total":3,"facets":[{"field":"status","values":[{"value":"published","count":2},{"value":"draft","count":1}]},{"field":"tags","values":[{"value":"<tag-id>","count":2}]}],"aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-09-03T00:00:00"}}}}}
```

GraphQL 的 `aggregate` 欄位型別是 `Any`——第 10 章有完整的共用型別表面
(`AggregateInput`/`FacetResult`/`FacetValue`) 與它自己的實際輸出。

### 錯誤

四個實際範例——一個未知的 facet 欄位、一條超過一個關聯跳的 facet 路徑、一個無法作為 facet 的
interface (`Article.regions` 是 `MultiSelect`)，以及一個與欄位 interface 不相容的彙總 op
(對一個 `Select` 的 `Article.status` 做 `sum`):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?facets=bogusField"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogusField' on collection 'article'."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?facets=category.parent.name"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Facet paths support exactly one relation hop: 'category.parent.name'."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?facets=regions"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'regions' on collection 'article' (MultiSelect) cannot be used as a facet."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article?aggregate%5Bsum%5D=status"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Aggregate 'sum' is not supported on field 'status' (Select)."}}
```

還有兩個 `FacetPathResolver.Resolve` 的訊息補完整個目錄，都是 `BAD_USER_INPUT`:一個空的路徑片段
(`facets=` 逗號清單裡的一個空項目，或信封陣列裡的一個空字串) 是 `"Facet path must not be empty."`;
一個一跳加葉路徑，其*關聯*片段 (不是葉) 無法解析——例如 `facets=bogusRelation.name`——則是
`"Unknown relation 'bogusRelation' on collection 'article'."`，這與一個無法解析的自有欄位、或
單一片段路徑得到的單純 `"Unknown field"` 訊息不同。

一個超過 `MaxFacets` 的 facet 數量，或一個超過 `MaxAggregates` 的彙總欄位數量，會以相同的方式被拒絕
(`"Too many facets (max 10)."` / `"Too many aggregate fields (max 10)."`)，且發生在任何
facet／彙總 SQL 執行之前;一個重複的 facet 路徑會被靜默去重，而不是被拒絕或計算兩次。一個關聯跳
指向呼叫端無權讀取的集合的 facet，會以權限優先失敗，就跟一條帶點號的 filter 路徑一樣:
`FORBIDDEN`/`UNAUTHORIZED` (`"Read not permitted on '<collection>'."`)，會在路徑的形狀甚至還沒被
解析之前就先檢查——所以一個不可讀關聯的欄位名稱，一樣無法透過 facet 驗證錯誤被探測，就跟透過 filter
驗證錯誤一樣不可行 (見下文「驗證」)。

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
在同一個 to-many 關聯路徑上組合多個單純 (未加量詞) 的條件，是各自存在（each-exists），而非
同一列——上文的 `_some`/`_none` 才是把一個關聯路徑的條件綁定到同一列相關資料列的量詞——完整的
語意對照表與即時驗證實錄，見
[第 7 章](07-relations.md#各自存在-each-exists-vs-同一列-帶點號路徑-vs-some-none)。

讀取授權不會跨越關聯跳躍。一個帶點號的路徑所經過的每一個集合，都需要它自己的讀取權限，因此一個
只被授予 `article`、未被授予 `user` 的角色，無法透過 `author.email` 觸及 user 的資料列:該請求會被
拒絕 (`FORBIDDEN`;匿名呼叫者則是 `UNAUTHORIZED`)。選擇拒絕而非忽略該條件是刻意的——忽略它會回傳
與送出的 filter 不相符的資料列，而且即使一列都不回傳，`meta.total` 對一個不可讀的集合而言仍是一個
逐字元的抽取 oracle。`deep=` 套用同一條規則，但失敗模式相反:目標集合不可讀的關聯會**從回應中被
略過**，而不是讓整個請求失敗，所以一個窄授權的角色仍然拿得到它的項目，只是少了那個巢狀物件。一個
可翻譯的 Image/File 欄位也走同一份授權——沒有 `file` 的讀取權時，巢狀物件會是 `null`，而原始的
`<name>Id` 仍然拿得到。

權限檢查是在路徑的葉節點被解析**之前**執行的，因此一個你無權讀取的集合，它的欄位名稱無法透過驗證
錯誤訊息被探測:你無法區分該集合上一個真實存在的欄位與一個你捏造的欄位，因為兩者都會停在那一跳。
多對一的外鍵本身 (`categoryId`) 則仍可被 filter——它是你本來就有權讀取的那一列上的一個欄位，暴露的
是一個不透明的 id，而不是目標集合裡的任何內容。

**對於開放匿名讀取的部署，這是一個破壞性變更。** 如果 `Rbac:PublicReadCollections` 列出的某個集合，
其公開的 filter 或 `deep=` 會跨進第二個集合，那麼第二個集合現在也需要自己的項目，否則這些請求會開始
失敗 (filter) 或回傳時少了巢狀物件 (`deep=`)。

`facets=`/`aggregate[<op>]=` 也會經過同一個 `QueryValidator`:`ValidateAggregate` 會用跟
`CheckField` 一模一樣的自有欄位加外鍵允許清單 (`Known`) 去檢查每一個欄位，而 `ValidateFacets`
會以權限優先拒絕一個不可讀的 facet 目標——它自己的單一片段版本 `DenyUnreadableFacetTarget`，也就是
上文 `DenyUnreadableHops` 的 facet 路徑對應版本——在完全解析路徑形狀之前就先做這個檢查，所以一個
未知/隱藏的 facet 欄位，以及一個不可讀的關聯目標，失敗的方式都跟一個未知的 filter 欄位、或一條
不可讀的帶點號 filter 路徑完全相同。

一個已註冊的 `ISearchProvider` 所回傳的候選 id，完全跳過 `QueryValidator`——它們不是使用者輸入，
所以沒有白名單需要比對。取而代之的是 `SearchCandidateResolver` (上一節) 會把每一個 id 解析為集合
主鍵的 CLR 型別，解析失敗會是 `InvalidOperationException`/500，而不是 `QueryException`/400:是
fork 的 provider 有問題，不是呼叫端。

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
