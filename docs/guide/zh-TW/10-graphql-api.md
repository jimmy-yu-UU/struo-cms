# 10. GraphQL API

StruoCMS 在 REST 之外，還公開了第二個完全型別化的 API 介面:單一一個 GraphQL 端點，其整份 schema
都是從 REST 與管理後台 SPA 早已共用的同一份啟動時快取的集合 (collection) 中介資料產生出來的
——整個程式碼庫中沒有任何一份手寫的 SDL 檔案。本章涵蓋這份產生過程如何運作、如何探索這個即時
schema、它所產生的 query/filter/mutation 介面，以及它的錯誤形狀與限制。Filter/sort/分頁的*語意*
(運算子的意義、關聯路徑的深度上限、`deleted`) 是第 8 章的主題;本章涵蓋的是這同一套 DSL，如何改以
型別化的 GraphQL 引數表達，而不是查詢字串/JSON 信封慣例。

## Schema 產生:沒有手寫的 SDL

整份 schema 都是在啟動時，由 `StruoTypeModule` (`src/Struo.Api/GraphQl/StruoTypeModule.cs`)——一個
註冊在 `GraphQlServiceCollectionExtensions.AddStruoGraphQl`
(`src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`) 中的 HotChocolate `ITypeModule`
——建構出來的。它的 `CreateTypesAsync` 讀取 `IMetadataProvider.GetCollections()`——這正是
`SchemaController` 的 `GET /api/schema` 與 `QueryValidator` 的白名單 (第 8 章) 早已讀取的同一份
中介資料——並且逐集合地由 `CollectionSchemaBuilder`
(`src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`) 產生出:

- 一個物件型別 (例如 `File`)，永遠帶有 `id`/`version`，每一個非 `Hidden`、未被排除的自有欄位各
  一個欄位，每一個關聯各一個欄位，以及——當該集合有翻譯附屬資料表時——一個
  `translations: [Translation!]` 欄位。一個 `File`/`Image` 介面的自有欄位，除了自己的
  `ID`/`[ID!]` 純量之外，還會額外取得一個配套的 resolver 欄位——單一個 `File`/`Image` 欄位對應
  `<去除結尾 Id 後的名稱>: File`，一個 `Files` 欄位則對應 `<name>Files: [File!]`——它會透過
  `FileFieldResolvers` 的 DataLoader，把整個回應中所有被參照到的 id 批次成一次查詢 (沒有 N+1
  問題)。七個即時上線的框架集合，沒有任何一個以這三種介面之一宣告自有欄位，所以這個配套欄位行為
  在這個 host (即執行本章範例的 `Struo.Api` 執行個體) 上無法觸及，但一個 fork 若加入例如
  `[CmsField(Interface = FieldInterface.Image)] public Guid? Cover { get; set; }`，就能免費取得
  一個 `cover: ID` 加上一個 `coverFile: File`;
- 一個清單包裝型別 (`FileList { items: [File!]!, total: Int! }`);
- 一個篩選輸入型別 (`FileFilterInput`)，帶有 `and`/`or`/`id`，再加上每一個可篩選的自有欄位與
  每一個關聯各一個運算子輸入欄位 (跨關聯篩選，見下文);
- 一個建立輸入型別 (`FileCreateInput`) 與一個更新輸入型別 (`FileUpdateInput`，額外帶有樂觀
  並行控制用的 `version: Long`);
- 兩個根 `Query` 欄位 (`file(id, locale)`、`files(filter, sort, limit, offset, search, locale,
  deleted)`) 與四個根 `Mutation` 欄位 (`createFile`、`updateFile`、`deleteFile`、`restoreFile`)
  ——再加上 `fileRevisions`/`fileRevision` 這兩個 query 欄位，以及一個 `revertFile` mutation，但
  **只有**在該集合宣告 `Revisions = true` 時才會出現 (七個框架集合沒有任何一個如此——見下方的
  「版本紀錄」)。

`SchemaTypeMapper` (`src/Struo.Api/GraphQl/SchemaTypeMapper.cs`) 是 `FieldInterface` → SDL 型別
對應的唯一來源 (例如 `RichText`/`Markdown`/`Text` → `String`，`Number` → `Int`/`Long`/`Float`
視 CLR 數值型別而定，`Files` → `[ID!]`，`Json`/`KeyValue` → `Any` 純量)。`Password`、`Hidden`
與 `Divider` 會被完全排除——一個 `Password` 欄位絕不會出現在 GraphQL schema 中，與第 5 章的
`SchemaTypeMapper.Excluded` 集合一致。命名完全是機械式的 (`Pascal`/`Camel`/一個有文件說明的簡單
英文複數化規則)，所以舉例來說 `mediaFolder` 會變成 `mediaFolder`/`mediaFolders`/`MediaFolder`/
`MediaFolderFilterInput`，而 `category` 會變成 `categories` (而不是 `categorys`)——下方每一個
名稱，都是從即時 schema 讀出的，不是用猜的。

## 端點與探索方式

單一端點是 `POST /graphql`，掛載於 `MapStruoGraphQl`
(`src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs`)。有兩件事是依環境而定的，兩者都
讀自 `GraphQlServiceCollectionExtensions`:

- **Introspection**——`.DisableIntrospection(!env.IsDevelopment())`:introspection 查詢
  (`__schema`、`__type`、……) 在 Development 中可以運作，在其他環境下則會被拒絕。這個 host 正
  執行在 Development 環境下，所以下方的 schema 細節，都是透過標準的 introspection 查詢直接從即時
  伺服器讀出來的，不是從 type-module 原始碼轉錄而來。
- **Nitro IDE** (HotChocolate 內建的瀏覽器內 GraphQL 瀏覽工具)——
  `options.Tool.Enable = app.Environment.IsDevelopment()`:規則相同，瀏覽器工具只在 Development
  中提供。

**還有第三條揭露路由，而且它不是你會猜到的那一條。** `GET /graphql?sdl` 是 HotChocolate 內建在同一個
`/graphql` 路徑上的路由，會以純 SDL 文字的形式提供完整 schema——匿名即可，不需 session cookie，也不需
`X-Struo-CSRF` 標頭 (它是安全的 `GET`，`CsrfProtectionMiddleware` 根本不會考慮它)。關鍵在於
`.DisableIntrospection(...)` **管不到它**:那道把關管的是 introspection *查詢* (一般 GraphQL 請求中的
`__schema`/`__type` 選取)，完全沒有提到這條查詢字串路由，而 `MapGraphQL("/graphql")` 本身也不帶任何
環境把關。

這個模板較早的版本只把關了前者。針對一個真正的 Production 模式執行個體實測，當時出貨的樣子是:
introspection 查詢確實被拒絕 (`HC0046`)、瀏覽器 IDE 確實消失 (`404`)——而 `?sdl` 仍然把完整 schema
提供給匿名呼叫端。這個教訓比那個缺陷本身更長壽，所以記錄在此:**「introspection 已停用」與「schema
讀不到」並不是同一個主張。**

**現在兩條揭露路由由同一個旗標把關。** `GraphQl:ExposeSchema` (第 3 章) 在
`GraphQlServiceCollectionExtensions.ResolveExposeSchema` 中解析一次，同時餵給
`.DisableIntrospection(!exposeSchema)` 以及 HotChocolate 的
`GraphQLServerOptions.EnableSchemaRequests`——後者才是真正管控 `?sdl` 路由的那個選項。未設定 (預設)
代表僅限 Development，所以對本機安裝來說沒有任何改變。針對 Production 模式與 Development 模式的
執行個體重新實測:

| 路由 | Development | Production (預設) |
|---|---|---|
| `GET /graphql?sdl` | `200`，完整 SDL | **`404`，空 body** |
| `POST /graphql` introspection 查詢 | `200` | `400` `HC0046` |
| `POST /graphql` 一般查詢 | `200`，有資料 | **`200`，有資料** |
| Nitro 瀏覽器 IDE (`GET /graphql`) | 啟用 | `404` |

**關掉揭露不等於關掉執行。** `?sdl` 與 introspection 屬於 schema *發現*，而用戶端是透過
`POST /graphql` 帶查詢語句來執行的——一個已經知道自己要發什麼查詢的用戶端，執行期根本不會去讀
schema。第三列就是你用 GraphQL 前端接資料時真正該關心的那一列:它完全不受影響。
`SchemaExposureGateTests` 明確斷言了這一點，所以日後有人收緊揭露時，不會默默弄壞接資料的路徑。

關掉它真正會影響的是依賴 schema 的**工具鏈**——codegen、Postman/Insomnia 匯入 schema、Apollo
Sandbox、IDE 外掛。請把這些指向 Development 或 staging 執行個體，那裡兩條路由都是開著的。

如果你是刻意要公開一個 public GraphQL API、希望正式環境也能讀取 schema，只要改設定就行，不需要
重新建置:

```
GraphQl__ExposeSchema=true
```

這會**同時**重新啟用 introspection 與 `?sdl`——這是刻意的:「schema 要不要公開」是一個決定，不是
兩個。Nitro 瀏覽器 IDE 不受這個設定影響，一律僅限 Development——對外提供一個瀏覽器 IDE，是比提供
SDL 文字大得多的決定。在 reverse proxy/ingress 上封鎖這條路由仍然是合理的雙重保險，但它已經不是
唯一的選項了。

一個以 cookie 驗證的 `/graphql` 請求，需要與 REST 寫入相同的 `X-Struo-CSRF` 標頭 (第 9 章)。由於這條
規則看的是 HTTP 方法，而 GraphQL-over-HTTP 永遠使用 `POST`，因此它對一個唯讀的 *query* 與對一個
mutation 完全同等適用:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -b cookies.txt -d '{"query":"query { languages { total } }"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"query":"query { languages { items { code name isDefault } total } }"}'
{"data":{"languages":{"items":[{"code":"zh-TW","name":"繁體中文","isDefault":false},{"code":"en","name":"English","isDefault":true}],"total":2}}}
```

**一個 bearer 權杖確實能驗證 `/graphql`。** `MapGraphQL` 本身沒有帶任何 `[Authorize]`
attribute，但驗證這件事本身，已經不再仰賴是否存在這樣一個 attribute:預設的驗證機制是
`AuthSchemes.Adaptive` (`src/Struo.Api/Auth/AuthWiring.cs`)，這是一個轉發用的 policy scheme，
只要請求的 `Authorization` 標頭以 `Bearer ` 開頭，就會轉發給 `Bearer` handler，否則轉發給
`Cookie`——在每一個端點上都是如此，`/graphql` 也不例外。因此一個純 bearer 的客戶端，會被解析為
**它自己**，連同它自己角色的授權 (與 `public` 底線聯集，第 12 章)，與一個 cookie session 完全
相同。只要請求的 `Authorization` 標頭以 `Bearer ` 開頭，`CsrfProtectionMiddleware` 就會把它從上方的
`X-Struo-CSRF` 要求中豁免 (第 9 章的 CSRF 段落)——這項檢查會在 middleware 檢視 session cookie
之前就先執行並回傳，所以即使該請求恰好也帶有一個 session cookie，這項豁免依然成立。因此一次純粹由
bearer 權杖驅動的 `/graphql` 呼叫，既不需要 session cookie，也不需要 CSRF 標頭。

本章中的每一個 GraphQL 範例，仍然是在一個帶有 CSRF 標頭的 cookie session 下執行的，但這只是因為
一個以瀏覽器為基礎的 GraphQL 客戶端 (Nitro IDE、一個 SPA) 天生就是這個樣子——不是因為一個純
bearer 的客戶端無法驅動 `/graphql`。它可以，而且與一個 cookie session 完全平等，包括下方的每一個
mutation。以下是證明，不是空口斷言——一個只被授予 `mediaFolder` 讀取權的角色所核發的 bearer
權杖，不帶 cookie、也不帶 CSRF 標頭，驅動一次 `mediaFolders` 查詢:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "Authorization: Bearer <token>" -d '{"query":"{ mediaFolders { items { id } } }"}'
{"data":{"mediaFolders":{"items":[{"id":"019fac90-2300-78da-8a3c-f281dac532e0"},{"id":"019fac8f-fb2b-77ae-a152-f25fddf54ef8"}]}}}
```

### 讀取即時 schema

對執行中的伺服器做一次限定於根型別的 introspection 查詢，可以確認接上這個 host 的正好就是這七個
集合，完全沒有 (未選用啟用的) 範例混入其中:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"{ __schema { queryType { fields { name } } mutationType { fields { name } } } }"}'
{"data":{"__schema":{"queryType":{"fields":[{"name":"_service"},{"name":"language"},{"name":"languages"},{"name":"permission"},{"name":"permissions"},{"name":"role"},{"name":"roles"},{"name":"user"},{"name":"users"},{"name":"userRole"},{"name":"userRoles"},{"name":"file"},{"name":"files"},{"name":"mediaFolder"},{"name":"mediaFolders"}]},"mutationType":{"fields":[{"name":"_service"},{"name":"createLanguage"},{"name":"updateLanguage"},{"name":"deleteLanguage"},{"name":"restoreLanguage"},{"name":"createPermission"},{"name":"updatePermission"},{"name":"deletePermission"},{"name":"restorePermission"},{"name":"createRole"},{"name":"updateRole"},{"name":"deleteRole"},{"name":"restoreRole"},{"name":"createUser"},{"name":"updateUser"},{"name":"deleteUser"},{"name":"restoreUser"},{"name":"createUserRole"},{"name":"updateUserRole"},{"name":"deleteUserRole"},{"name":"restoreUserRole"},{"name":"createFile"},{"name":"updateFile"},{"name":"deleteFile"},{"name":"restoreFile"},{"name":"createMediaFolder"},{"name":"updateMediaFolder"},{"name":"deleteMediaFolder"},{"name":"restoreMediaFolder"}]}}}}
```

`Query` 帶有 14 個由集合衍生出的欄位 (再加上 `_service` 錨點)——`language`/`languages`、
`permission`/`permissions`、`role`/`roles`、`user`/`users`、`userRole`/`userRoles`、
`file`/`files`、`mediaFolder`/`mediaFolders`——而 `Mutation` 帶有 28 個 (再加上它自己的
`_service` 錨點):同樣這七個集合各自的 `create`/`update`/`delete`/`restore`。任何地方都沒有出現
`revert*` mutation，也沒有 `*Revisions`/`*Revision` 這種 query 欄位 (見下方的「版本紀錄」)。

## Query:單筆項目、清單、引數

每一個集合都恰好會得到兩個根 query 欄位 (`CollectionResolvers.SingleField`/`ListField`，
`src/Struo.Api/GraphQl/CollectionResolvers.cs`):

- **`{collection}(id: ID!, locale: String)`**——單一個項目，或者對一個未知/預設下已軟刪除的
  id 回傳 `null` (沒有錯誤——GraphQL 的 null，意圖上與 REST 的 `404` 一致，但形狀不同)。
- **`{collection}s(filter, sort: [String!], limit: Int, offset: Int, search: String, locale: String,
  deleted: DeletedFilter, facets: [String!], aggregate: AggregateInput)`**——一頁資料，以
  `{ items: [X!]!, total: Int!, facets: [FacetResult!]!, aggregate: Any }` 的形式回傳。
  `deleted` 是 SDL 列舉 `EXCLUDE`/`ONLY`/`WITH`，直接繫結到與 REST 相同的
  `Struo.Domain.Query.DeletedFilter`;要求 `ONLY`/`WITH` 會受到與 REST 的 `ItemsController`
  (第 8 章) 完全相同的 `DeletedAccessGuard.EnsureCanViewDeleted` 檢查把關——需要對該集合有刪除
  權限。`facets`/`aggregate` 就是 REST 的 `facets=`/`aggregate[<op>]=` 所開放的同一項功能
  (第 8 章「Facets 與彙總」有完整的語法、語意與錯誤目錄);回應上的 `facets`/`aggregate`
  在本節後段涵蓋。只有這兩個根層級清單欄位帶這兩個引數——一個巢狀 to-many 清單欄位
  (下文的 `article.tags(filter: …)`) 沒有。

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { user(id: \"019fa8b2-4d09-7155-b641-2c3e2519233b\") { id email name roles { id name } } }"}'
{"data":{"user":{"id":"019fa8b2-4d09-7155-b641-2c3e2519233b","email":"admin@admin.com","name":"Administrator","roles":[{"id":"019fa8b2-4edb-702d-80fc-27ea33c18b6a","name":"admin"}]}}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { files(filter: { size: { gte: 20 } }, sort: [\"-size\"], limit: 2, offset: 0) { items { id fileName size } total } }"}'
{"data":{"files":{"items":[{"id":"...","fileName":"gamma-draft.txt","size":35},{"id":"...","fileName":"beta-notes.txt","size":23}],"total":3}}}
```

清單欄位回應上的 `facets`/`aggregate`——`facets: [FacetResult!]!` (省略 `facets` 引數時為空陣列)
與 `aggregate: Any` (省略 `aggregate` 引數時為 `null`)——共用 `SharedFacetTypes.Build`
(`src/Struo.Api/GraphQl/SharedFacetTypes.cs`) 為每個集合只建構一次的兩個輸出型別:
`FacetResult { field: String!, values: [FacetValue!]! }` 與 `FacetValue { value: Any, count: Int! }`，
對應 domain 層的 `FacetResult`/`FacetBucket` record。引數型別 `AggregateInput` 每個彙總 op
(`count`/`sum`/`min`/`max`/`avg`) 各有一個 `[String!]` 欄位，由 `QueryParser.AggregateOps`
(REST 的 `aggregate[<op>]=` 鍵所用的同一組 op 集合) 建構而成。實際輸出，對照第 8 章相同的
fixture (一個分類底下三篇文章——兩篇 `published`、一篇 `draft`，兩篇共用一個標籤):

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { articles(filter: { categoryId: { eq: \"<category-id>\" } }, facets: [\"status\", \"tags\"], aggregate: { count: [\"publishedAt\"], max: [\"publishedAt\"] }) { total facets { field values { value count } } aggregate } }"}'
{"data":{"articles":{"total":3,"facets":[{"field":"status","values":[{"value":"published","count":2},{"value":"draft","count":1}]},{"field":"tags","values":[{"value":"<tag-id>","count":2}]}],"aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-09-03T00:00:00"}}}}}
```

`FacetValue.value` 的 `Any` 型別會保留原本的 JSON 種類——一個數值 facet (分面計數) 的 bucket 值回來時是
GraphQL 數字，不會被字串化——`aggregate` 的 `Any` 也是同樣道理，讓 `count` 的整數與 `max` 的
`DateTime` 字串保持各自的型別，而不是強制轉成單一 scalar 型別。

一列已軟刪除的資料，其單筆項目欄位會解析為 `null` (不是錯誤)，而 `deleted: ONLY` 則會讓它出現在
清單欄位中:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { file(id: \"<id>\") { id fileName } }"}'
{"data":{"file":null}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { files(deleted: ONLY) { items { id fileName } total } }"}'
{"data":{"files":{"items":[{"id":"...","fileName":"gamma-draft.txt"}],"total":1}}}
```

## 篩選，包括跨關聯帶點號路徑

`FileFilterInput` 帶有 `and`/`or` (各為 `[FileFilterInput!]`，恰好一層——與 REST 的 JSON 信封
`_and`/`_or` (第 8 章) 有相同的限制)、`some`/`none` (各為一個單純的 `FileFilterInput`——即下文
涵蓋的關聯量詞;在這個根層級沒有意義，用在這裡會被拒絕)、`id: IdFilter`、每一個可篩選自有欄位
各一個運算子輸入欄位
(`StringFilter`/`IntFilter`/`FloatFilter`/`DateTimeFilter`/`BooleanFilter`/`IdFilter`——在
`SharedFilterTypes.cs` 中只建構一次，並在每一個集合中重複使用)，以及——跨關聯的部分——一個關聯
自身的外鍵欄位，以 `IdFilter` 的形式呈現 (與 REST 的外鍵篩選白名單 (第 8 章) 對等)，**再加上**
一個對該關聯目標型別的巢狀篩選輸入。`FilterInputTranslator`
(`src/Struo.Api/GraphQl/FilterInputTranslator.cs`) 會把一個巢狀關聯篩選，攤平成與第 8 章的
`FilterTranslator` 下推成子查詢相同的帶點號 `FieldPath` (`"folder.name"`)——
GraphQL 與 REST 的跨關聯路徑，會在抵達查詢驗證器之前，就先收斂成完全相同的 `FilterNode` 樹:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { users(filter: { roles: { name: { eq: \"Editor\" } } }) { items { id email roles { name } } total } }"}'
{"data":{"users":{"items":[{"id":"...","email":"editor@example.com","roles":[{"name":"Editor"}]}],"total":1}}}
```

每一個運算子輸入上的 `isNull: Boolean`，會渲染成與第 8 章記載的 `_null`/`_nnull` 完全相同的
單純 `IS [NOT] NULL`——不會有任何比較值被送到資料庫。

### 關聯量詞:`some`/`none`，以及透過 `<Parent><Rel>RelationFilterInput` 表達的 `_junction`

每一個 `<T>FilterInput` 額外都帶有 `some: <T>FilterInput` 與 `none: <T>FilterInput`——即第 7/8
章為 REST 涵蓋的同一組關聯量詞，以與 `and`/`or` 相同的方式保留。它們只有在**巢狀於某個關聯自己
的 filter 字典之內**時才有意義;若用在任何 `filter` 引數的根層級 (自有欄位或某個巢狀清單自己的
`filter` 引數)，就會被拒絕:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { article(id: \"<id>\") { id tags(filter: { some: { name: { eq: \"Guide\" } } }) { id } } }"}'
{"errors":[{"message":"'some' is only valid inside a relation filter.","path":["article"],"extensions":{"code":"BAD_USER_INPUT"}}],"data":{"article":null}}
```

(最後這則查詢裡的 `tags(filter: ...)`，是一個 to-many *巢狀清單*引數，由下文同一套 `deep` 展開
機制解析——它自己 `filter` 引數的型別，是單純、未經改動的 `TagFilterInput`，而不是接下來要
描述的關聯專屬輸入;那裡的 `some`/`none` 是在該引數自己的根層級被求值的，正好就是上面錯誤訊息
所指名的「根層級、沒有關聯情境」那種情況。)

對一個 junction 帶有可揭露 payload 的 many-to-many 關聯而言 (第 7 章的 junction payload 與
`_junction` 讀取投影)，父型別上該關聯的 filter 欄位**不是**單純的 `<Target>FilterInput`——而是
一個關聯專屬的 `<Parent><Rel>RelationFilterInput`，除了目標自身的可篩選欄位與它自己的
`and`/`or`/`some`/`none` 之外，還多加了一個 `junction: <Parent><Rel>JunctionFilterInput` 欄位
(每一個可揭露 payload 欄位各一個運算子輸入欄位)。範例的 `Article.tags` (payload:`note`) 就是
隨附出貨的例子——introspection 確認了型別名稱
(`ArticleTagsRelationFilterInput`/`ArticleTagsJunctionFilterInput`，依循
`SchemaTypeMapper.RelationFilterInputName`/`JunctionFilterInputName`)，而 `some` 加上
`junction` 可以在同一個查詢中組合，完全就像 REST 的 `_some`/`_junction`:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { articles(filter: { tags: { some: { name: { eq: \"Guide-u3doc0905\" }, junction: { note: { eq: \"hero\" } } } } }) { items { id } total } }"}'
{"data":{"articles":{"items":[{"id":"<a-id>"}],"total":1}}}
```

一個 junction 不帶可揭露 payload 的關聯，仍維持單純、共用的 `<Target>FilterInput`——這個關聯
專屬的改名，是本功能之下唯一會改變的 GraphQL 型別名稱:一個帶 payload 關聯的 filter 欄位，原本
帶有單純、共用的 `<Target>FilterInput` 型別 (`Article.tags` 就是 `TagFilterInput`)，現在改為帶
關聯專屬的 `ArticleTagsRelationFilterInput`——一個把該欄位型別明確宣告成 GraphQL 變數 (而不是
讓查詢就地內嵌它) 的客戶端，必須更新它。

## 巢狀 to-many 清單與它們的引數

一個物件型別上的 to-many 關聯 (`OneToMany`/`ManyToMany`) 欄位，會帶有自己的
`filter`/`sort`/`limit`/`offset` 引數，由 `CollectionResolvers.BuildDeep` 解析——它會把客戶端的
選取樹走訪成一個巢狀的 `DeepSpec`——這正是第 7 章 `deep=` 所使用的同一套關聯展開機制，只是改由
GraphQL 的選取來驅動，而不是一份查詢字串清單。`User.roles` (框架自身唯一即時上線的
many-to-many，透過 `userRole` junction 塑模的 `user` ↔ `role`) 示範了這一點:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { user(id: \"<editor-id>\") { id email roles(sort: [\"name\"], limit: 1) { id name } } }"}'
{"data":{"user":{"id":"...","email":"editor@example.com","roles":[{"id":"...","name":"Editor"}]}}}
```

一個 many-to-one 關聯 (例如 `File.folder`、`MediaFolder.parent`) 則維持是一個完全不帶任何
引數的單純物件欄位——`CollectionSchemaBuilder` 只會把 `filter`/`sort`/`limit`/`offset` 附加在
`OneToMany`/`ManyToMany` 欄位上，因為一個 M2O 那一側最多只會解析出一列資料。

與上方兩個根層級清單欄位不同，一個巢狀 to-many 欄位的引數清單止步於
`filter`/`sort`/`limit`/`offset`——`facets`/`aggregate` 在 v1 只限於根層級清單欄位
(`AddRelationField`，`src/Struo.Api/GraphQl/CollectionSchemaBuilder.cs`，永遠不會把它們加到一個
關聯欄位上)，所以 `article.tags(facets: [...])` 並不是差一個 schema 錯誤就能動——它根本就不在
產生出來的 schema 裡。

## Mutation:create、update、delete——型別化輸入與部分更新語意

每一個集合都會得到 `create{X}(input: XCreateInput!, locale: String): X`、
`update{X}(id: ID!, input: XUpdateInput!, locale: String): X`、
`delete{X}(id: ID!, purge: Boolean): Boolean`，以及 `restore{X}(id: ID!): X`
(`MutationResolvers`，`src/Struo.Api/GraphQl/MutationResolvers.cs`)。`create`/`update` 會把
型別化的輸入轉換回 `ItemService.CreateAsync`/`UpdateAsync` 早已接受的同一個 `JsonElement`
(`MutationInputMapper.ToJsonElement`)，然後在寫入之後**重新讀取**該資料列，讓回傳的節點具備與一次
query 會產生的相同形狀 (關聯/翻譯都可解析)——若這次重新讀取被 RBAC 拒絕，會退回使用原始的寫入
結果，而不是呈現為一個錯誤，所以一次成功的寫入，絕不會因為後續讀取的權限失敗而被隱藏起來。

**部分更新語意帶有一個真實的微妙之處。** HotChocolate 強制轉型後的輸入字典，會替*每一個*宣告過的
輸入欄位回填 `null`，只要客戶端沒有送出它——如果不做任何處理，這會讓一次部分 `updateX` 與「明確地
把我沒提到的每一項都設為 null」無法區分。Resolver 用 `SentFieldsOnly` 防範這一點:它會從請求自身
的引數字面量，重新推導出*實際送出*的鍵值集合 (並遞迴進入巢狀輸入——例如一個 Repeater/Translation
子物件——所以一個被省略的巢狀欄位同樣不會被回填)，然後才把修剪過的字典交給 `ItemService`，由它
自身的 `bodyKeys` 合併邏輯 (第 9 章) 只把這些鍵值疊加到既有的資料列上。**第 9 章記載的 `Required`
欄位但書，在這裡同樣適用**:`ItemDeserializer` 仍然會對照修剪過、但仍是剛剛解析出的請求本文，
驗證每一個 `Required` 欄位，所以每一次 `updateX` 都必須重新送出一個 `Required` 欄位，否則這次
mutation 會以 `BAD_USER_INPUT` 失敗——這不是 REST 與 GraphQL 之間的差異，而是同一條共用的寫入
路徑:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { createRole(input: { name: \"Reviewer\", description: \"Docs demo role\" }) { id name description } }"}'
{"data":{"createRole":{"id":"...","name":"Reviewer","description":"Docs demo role"}}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { updateRole(id: \"<id>\", input: { description: \"Updated via GraphQL\" }) { id name description } }"}'
{"errors":[{"message":"Field 'name' is required.","path":["updateRole"],"extensions":{"code":"BAD_USER_INPUT"}}],"data":{"updateRole":null}}

$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { deleteRole(id: \"<id>\") }"}'
{"data":{"deleteRole":true}}
```

一個 many-to-many 關聯，會在建立/更新輸入上，以一個單純的目標 id 陣列 `[ID!]` 的形式寫入
(`UserCreateInput`/`UserUpdateInput` 上的 `roles`)，從頭到尾共用 REST 底層的 M2M 同步機制
(第 9 章):這個陣列會與目前已連結的資料做差異比對，而不是整批刪除再重新插入，因此一個維持連結
狀態的目標，其 junction 資料列會保留自己的主鍵:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { updateUser(id: \"<id>\", input: { email: \"editor@example.com\", name: \"Editor Person\", roles: [\"<role-id>\"] }) { id email name isActive roles { id name } } }"}'
{"data":{"updateUser":{"id":"...","email":"editor@example.com","name":"Editor Person","isActive":true,"roles":[{"id":"...","name":"Editor"}]}}}
```

### `<rel>Links`:讀寫 junction payload

對於 junction 至少宣告了一個可曝光 payload 欄位 (非 `Hidden`、可對應到純量——第 7 章) 的
many-to-many 關聯，`CollectionSchemaBuilder` 會額外產生一套與上方純陣列 `<rel>`/`[ID!]` 並行、
更豐富的介面;junction 沒有可曝光 payload 的關聯 (例如 `User.Roles`) 完全不會有這一套東西——
只有純陣列介面。讀取端，`<rel>Links: [<Parent><Rel>Link!]` 會與 `<rel>: [<Target>!]` 並列，其中
`<Parent><Rel>Link = { node: <Target>!, junction: <Parent><Rel>Junction }`——`node` 就是
`<rel>` 會回傳的同一筆目標資料列，`junction` 則是該關聯非 `Hidden` payload 欄位組成的物件，若
呼叫端無法讀取該 junction collection 則為 `null`。寫入端，建立/更新輸入會在既有的
`<rel>: [ID!]` 之外，額外多出 `<rel>Links: [<Parent><Rel>LinkInput!]`，其中
`<Parent><Rel>LinkInput = { id: ID!, <可寫入的 payload 欄位...> }`。在查詢中選取 `<rel>Links`
會像 `<rel>` 一樣驅動同一套深度展開;在 mutation 中送出 `<rel>Links`，底層會直接折疊進 REST 的
混合陣列寫入形狀 (第 9 章)——`MutationResolvers.FoldLinks` 會把它改寫進 `<rel>` 這個鍵，之後
才送到 `ItemService`。若一次 mutation 同時送出 `<rel>` 與 `<rel>Links`，`<rel>Links` 會直接
勝出，包括明確送出 `<rel>Links: null` 的情況——它會捨棄同時送出的 `<rel>` 陣列，而不是放著
不管。

範例的 `Article.Tags` (第 16 章) 就是隨附出貨的範例——它的 junction `ArticleTag` 曝光了
`note` 作為 payload，因此產生出來的 schema，會在 `Article` 上額外多出 `tagsLinks:
[ArticleTagsLink!]`，其中 `ArticleTagsLink { node: Tag!, junction: ArticleTagsJunction }` 而
`ArticleTagsJunction { note: String }`;而在 `ArticleCreateInput`/`ArticleUpdateInput` 上，則
多出 `tagsLinks: [ArticleTagsLinkInput!]`，其中
`ArticleTagsLinkInput = { id: ID!, note: String }`。形狀如下 (示意用——schema 型別/欄位命名
方式，對應 `SchemaTypeMapper` 的 `<Parent><Rel>Link`/`<Parent><Rel>Junction`/
`<Parent><Rel>LinkInput` 型別命名，以及 `<rel>Links` 欄位命名):

```graphql
type ArticleTagsJunction { note: String }
type ArticleTagsLink { node: Tag!, junction: ArticleTagsJunction }
input ArticleTagsLinkInput { id: ID!, note: String }

# 在 Article 上: tags: [Tag!]  (不變)  +  tagsLinks: [ArticleTagsLink!]
# 在 ArticleUpdateInput 上: tags: [ID!]  (不變)  +  tagsLinks: [ArticleTagsLinkInput!]
```

```json
{ "tagsLinks": [{ "id": "<tag-id>", "note": "editor pick" }] }
```

`AdminOnly` 集合的寫入 (`permission`/`role`/`user`/`userRole`)，在這裡與在 REST 中一樣，都需要
超級管理員身分——`ItemService.RequireSuperAdminForAdminOnly` 是兩種協定共同呼叫的同一個檢查:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b editor-cookies.txt \
    -d '{"query":"mutation { updateRole(id: \"<id>\", input: { name: \"Editor\", description: \"hack\" }) { id } }"}'
{"errors":[{"message":"Writes to 'role' require a super-admin.","path":["updateRole"],"extensions":{"code":"FORBIDDEN"}}],"data":{"updateRole":null}}
```

一次因 `OnDelete.Restrict` (第 7 章) 而被擋下的刪除，會呈現出與 REST 回傳完全相同的
`CONFLICT` 代碼:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { deleteMediaFolder(id: \"<guides-id>\") }"}'
{"errors":[{"message":"Cannot delete 'mediaFolder/<guides-id>': referenced by 'file'.","path":["deleteMediaFolder"],"extensions":{"code":"CONFLICT"}}],"data":{"deleteMediaFolder":null}}
```

對一個未知的 id，`delete`/`restore` 會回傳與 REST 的 `404` 對等的 `null`/`false`，而不是一個
GraphQL 錯誤——這與 query 欄位所採取的「未知 id 是資料，不是故障」立場一致。

## Mutation 中的翻譯

一個帶有翻譯附屬資料表的集合，會在建立與更新輸入上都得到一個
`translations: [XTranslationInput!]` 欄位，其中
`XTranslationInput = { locale: String!, fields: XTranslationFieldsInput! }`——這是讀取端
`[Translation!]` 形狀鏡射回來的輸入版本。`MutationResolvers.FoldTranslations` 會把這份清單，
轉換成 `ItemService.SyncTranslationsAsync` 所預期、以 locale 為鍵值的物件
(`{ "<locale>": { <field>: <value> } }`)，轉換發生在 `SentFieldsOnly` 已經把每一筆項目修剪到只
剩客戶端送出的子欄位之後——清單中重複的 locale 以最後一筆為準，而且 create 仍然會強制要求存在
一筆預設語言的翻譯 (第 6 章)，與 REST 相同:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"mutation { updateFile(id: \"<id>\", input: { translations: [ { locale: \"en\", fields: { title: \"Gamma Draft (GraphQL)\", alt: \"edited via GraphQL\" } } ] }) { id fileName translations { locale fields } } }"}'
{"data":{"updateFile":{"id":"...","fileName":"gamma-draft.txt","translations":[{"locale":"en","fields":{"title":"Gamma Draft (GraphQL)","alt":"edited via GraphQL"}}]}}}
```

## 透過 GraphQL 使用版本紀錄

當一個集合宣告 `Revisions = true` 時，`StruoTypeModule` 會加入兩個 query 欄位
(`{collection}Revisions(id: ID!): [Revision!]!`、
`{collection}Revision(id: ID!, revisionNumber: Int!): Revision`) 與一個 mutation
(`revert{X}(id: ID!, revisionNumber: Int!): X`)——`RevisionResolvers.cs`。共用的 `Revision`
型別是 `{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber, snapshot }`;清單欄位的每一筆項目都
回傳 `snapshot: null` (只有中介資料，與 REST 的清單端點一致)，而單筆欄位則會填入它，解析成
`Any` 純量。**七個即時上線的框架集合，沒有任何一個宣告 `Revisions = true`**，所以目前這個 host
的 schema 中完全不存在這些欄位——直接對照上方 introspection 出來的 `Query`/`Mutation` 欄位清單
即可確認 (沒有 `*Revisions`、沒有 `*Revision`、沒有 `revert*`)。一個把某個集合選用啟用
`[CmsCollection(Revisions = true)]` 的 fork，可以免費取得這整套介面，完全不需要寫任何
GraphQL 層的程式碼;REST 相同的版本紀錄端點 (第 9 章) 是目前唯一可以即時演練這個行為的地方，而
且在那裡同樣也只能演練它「沒有設定版本紀錄」的空/`404` 形式。

## 錯誤形狀 (`StruoErrorFilter`)

`StruoErrorFilter` (`src/Struo.Api/GraphQl/StruoErrorFilter.cs`) 是 REST 的
`StruoExceptionHandler` 在 GraphQL 這一側的孿生對照:它會透過相同的 `DomainErrorMap`，對應一個
**resolver** 例外，並把結果的 `extensions.code`，蓋上與 REST 完全相同的穩定代碼字串
(`UNAUTHORIZED`、`FORBIDDEN`、`NOT_FOUND`、`CONFLICT`、`VERSION_CONFLICT`、`BAD_USER_INPUT`、
`PAYLOAD_TOO_LARGE`、`SEARCH_UNAVAILABLE`、`SESSION_REVOCATION_FAILED`、`INTERNAL_SERVER_ERROR`——
`DomainErrorMap.Map`
完全不區分 REST 與 GraphQL 呼叫端，所以每一個被對應到的例外型別，包括 `PayloadTooLargeException` 在內，無論是哪一種協定的
resolver/action 擲出的，都會蓋上相同的代碼)。這種情況——一個例外，是在 resolver 針對一個語法/結構
上合法的請求真正執行時擲出的——會讓傳輸層的 HTTP 狀態維持在 `200`;呼叫端應該逐一檢視每個錯誤的
`extensions.code`，而不是依賴狀態列。`SEARCH_UNAVAILABLE` (第 8 章的
[搜尋提供者（Search providers）](08-query-dsl.md#搜尋提供者-search-providers)) 也不例外:GraphQL
仍然以 `200` 回應，並在 `extensions.code` 中帶上代碼，即使 REST 把相同的 `SearchUnavailableException`
對應成 HTTP `503`。下面的實錄以一個權限錯誤示範這種「`200` 加 `extensions.code`」的形狀:

```
$ curl -s -i -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -d '{"query":"query { users { total } }"}'
HTTP/1.1 200 OK
{"errors":[{"message":"Read not permitted.","path":["users"],"extensions":{"code":"UNAUTHORIZED"}}],"data":null}
```

這裡一個未被對應到的例外，會被遮蔽成通用訊息，並在伺服器端記錄下來，與 REST 的做法完全相同。
一個請求層級的錯誤，若完全**沒有**附帶任何 `Exception`——文件無法剖析、指名了一個不存在的欄位，
或是 (見下方的「深度上限」) 在任何 resolver 執行之前，就違反了執行深度/成本規則——永遠不會抵達
`StruoErrorFilter` 的例外分支，不會帶有任何 `code` 擴充欄位，而 HotChocolate 自身的
ASP.NET Core 整合會以 HTTP `400` 而不是 `200` 來回應它:

```
$ curl -s -i -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { bogusThing { id } }"}'
HTTP/1.1 400 Bad Request
{"errors":[{"message":"The field `bogusThing` does not exist on the type `Query`.","locations":[{"line":1,"column":9}],"extensions":{"type":"Query","field":"bogusThing","responseName":"bogusThing","specifiedBy":"https://spec.graphql.org/September2025/#sec-Field-Selections"}}]}
```

## 深度上限

有三個各自獨立的上限適用，全部都設定在 `AddStruoGraphQl` 中:

- **執行深度**——`.AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)`:一個選取樹
  巢狀超過 12 層的請求，會在任何 resolver 執行之前就被拒絕，introspection 豁免在外。
- **成本分析**——`.AddCostAnalyzer()`，`MaxFieldCost = 150.0` / `MaxTypeCost = 150.0`:這是
  別名 (alias) 放大攻擊的防禦機制。檢視 HotChocolate 16.6.0 的 validation 規則集後(直接對照
  `HotChocolate.Validation` 16.6.0 組件確認)，仍沒有專屬的別名/操作數規則，所以這道防線由成本分析
  承擔——每一個帶別名的選取都會累積自己的欄位成本，所以在多個別名下重複一個昂貴的清單欄位，
  成本會大致成比例增加，並以相同方式被拒絕。這個數字是對照專案自身的 GraphQL 測試套件校準出來的:
  最重的合法 query 測得 `fieldCost = 33`;一次 50 別名的放大攻擊測得 `fieldCost = 550`;`150`
  剛好落在兩者之間。
- **關聯路徑深度**——第 8 章對一條帶點號關聯路徑的 6 跳上限，在這裡原封不動地適用，因為
  `FilterInputTranslator` 的跨關聯篩選與 `BuildDeep` 的巢狀清單展開，兩者最終都會流入與 REST
  相同的那個 `QueryValidator`。

一個把框架自身唯一的自我參照關聯 (`MediaFolder.parent`) 巢狀超過執行深度上限的 query，會同時
被深度規則錯誤，以及 (因為走訪本身仍會先執行到一部分) 一個 HotChocolate 內建的循環守衛錯誤所拒絕
——像這樣的一個請求層級拒絕，同樣屬於 `HTTP 400` 的情況，與上方未知欄位的範例相同:

```
$ curl -s -i -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"query { mediaFolder(id: \"019fac8f-fb2b-77ae-a152-f25fddf54ef8\") { id parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { parent { id } } } } } } } } } } } } } } }"}'
HTTP/1.1 400 Bad Request
{"errors":[{"message":"The GraphQL document has an execution depth of 15 which exceeds the max allowed execution depth of 12.","locations":[{"line":1,"column":1}],"extensions":{"allowedExecutionDepth":12,"detectedExecutionDepth":15}},{"message":"Maximum allowed coordinate cycle depth was exceeded.","locations":[{"line":1,"column":97}],"path":["mediaFolder","parent","parent","parent"],"extensions":{"code":"HC0087"}}]}
```

## 接下來該去哪

- 第 8 章 [查詢 DSL](08-query-dsl.md)，涵蓋篩選運算子、`deleted`/`locale` 語意，以及本章的引數
  最終會編譯成的那個 6 跳關聯路徑深度上限。
- 第 7 章 [關聯](07-relations.md)，涵蓋 `OnDelete.Restrict`、many-to-many junction，以及自我
  參照樹狀結構 (上方的 `MediaFolder.parent` 範例)。
- 第 9 章 [REST API](09-rest-api.md)，涵蓋本章與 REST 逐字共用的回應信封、錯誤代碼目錄，以及
  `X-Struo-CSRF`/bearer 驗證細節。
- 第 13 章 [版本紀錄與軟刪除](13-revisions-and-soft-delete.md)，涵蓋 `[CmsCollection(Revisions
  = true)]`，以及一旦選用啟用之後，一個即時上線、有版本紀錄的集合，其 GraphQL 介面會是什麼樣子。
