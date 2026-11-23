# 14. GraphQL API

同一套 metadata 產生的 GraphQL schema，怎麼查、怎麼改，跟 REST 差在哪，是這一章的主題。

## Schema 從 metadata 產生

GraphQL 跟 REST 讀的是同一份啟動時就快取好的集合 metadata，是第二套完整型別化的 API；schema 沒有任
何一份手寫的 SDL。啟動邏輯逐集合產生一個物件型別、一個清單包裝、一個篩選輸入、一個建立輸入、一個更
新輸入，外加兩個 root query 欄位與四個 root mutation 欄位。junction collection 在 schema 裡是一般
的集合，跟其他集合一樣有自己的型別與 mutation：集合層級的 `Hidden` 只影響後台側邊欄，不影響 schema
產生。

命名完全是機械式的，一個 fork 可以照規則預測每個產生出來的名稱：型別用 PascalCase，欄位用
camelCase，清單欄位套一個簡單的英文複數規則——子音後的 `y` 變 `ies`；`s`／`x`／`z`／`ch`／`sh` 結尾
加 `es`；其餘加 `s`，`category` 因此變成 `categories`，不是 `categorys`。以 Blog 樣板的 `article`
為例：

| 產生什麼 | 名稱樣式 | 例子 |
|---|---|---|
| 物件型別 | `<X>` | `Article` |
| 清單包裝 | `<X>List` | `ArticleList` |
| 篩選輸入 | `<X>FilterInput` | `ArticleFilterInput` |
| 建立／更新輸入 | `<X>CreateInput` / `<X>UpdateInput` | `ArticleCreateInput` |
| junction 讀取型別 | `<X><Rel>Link` / `<X><Rel>Junction` | `ArticleTagsLink` |
| junction 寫入輸入 | `<X><Rel>LinkInput` | `ArticleTagsLinkInput` |
| 關聯篩選輸入 | `<X><Rel>RelationFilterInput` | `ArticleTagsRelationFilterInput` |
| 欄位名稱（junction 讀寫） | `<rel>Links` | `tagsLinks` |

每個產生出來的名稱都必須唯一，主機在啟動時就會檢查。集合的名稱若跟其他集合、或跟 schema 一律存在的
名稱撞在一起，啟動會中止，原因鏈裡有一個 `MetadataException`，訊息點名相關的集合與撞名的名稱。兩個集合的產生名稱
相遇就算撞名，例如 `article` 與 `articleList`（都會產生型別 `ArticleList`），或 `category` 與
`categories`（都會產生查詢欄位 `categories`）。集合若產生出內建名稱也算撞名，例如 `revision`（型別
`Revision`）、`query`、`mutation`、`string`、`tagItem`。產生名稱是逐字比對的（GraphQL 名稱區分大小
寫），而集合名稱本身就必須在忽略大小寫後彼此不同。改掉其中一個集合的名稱即可；`Name` 的規則見[第 5 章：定義集合](05-collections.md)。

物件型別固定帶 `id: ID!` 與 `version: Long`；每個非隱藏、沒被排除的自有欄位各一個欄位，每個關聯也
各一個，有翻譯 sidecar 的集合再多一個 `translations: [Translation!]`。`version` 在每個型別上都宣
告，但只有繼承稽核基底的集合才會真正解析出值，其餘集合這一欄一律是 `null`。

`File`／`Image` 欄位除了自己的 `ID` 純量，還多一個同伴解析欄位（`heroImageId` → `heroImage`；沒有
`Id` 結尾的話改成加 `File` 尾綴），`Files` 欄位多一個 `<name>Files: [File!]`。清單包裝固定四個欄
位：`items`、`total`、`facets`、`aggregate`。

其餘介面到 SDL 有一份固定對照：文字類對應 `String`，`Boolean`／`Checkbox` 對應 `Boolean`，`Date`／
`DateTime` 對應同名型別，`Time` 也對應 `String`，內容是以 `Z` 結尾的 UTC 時刻，`MultiSelect`、
`CheckboxGroup`、`Tags`、`Files`、`Repeater` 欄位對應陣列，`Json`／`KeyValue` 對應 `Any`，`File`／
`Image`／`Uuid` 對應 `ID`；`Number`／`Slider`／`Rating` 看的是實際 CLR 型別而不是介面本身，整數型對
應 `Int` 或 `Long`，其餘對應 `Float`。

`Tags` 對應成 `[TagItem!]`，`Repeater` 依子欄位另外產生一個型別 `<X><Field>Item`（沒有子欄位就整個
省略）；`Password`、`Hidden`、`Divider` 完全不出現在 schema 裡。

`Translation { locale: String!, fields: Any! }` 把翻譯的讀取形狀定成一份陣列，每筆一個 locale，不
是 REST 那種以 locale 當鍵的物件；寫入形狀留給本章「翻譯」一節。

巢狀物件的 `translations` 跟根層一樣：目標集合有 sidecar，展開出來的每一列都帶，列出那一列每個
有翻譯的 locale；`locale` 引數的嚴格篩選同樣適用。

## 端點與探索

單一端點是 `POST /graphql`，跟 REST 的一切一樣掛在同一個 web 應用裡。schema 揭露分兩條路，用同一個
開關 `GraphQl:ExposeSchema` 控制：內建的 introspection（`__schema`、`__type`）跟 HotChocolate 自
帶的 `GET /graphql?sdl`——後者是安全的 `GET`，回傳完整的 schema SDL 純文字，CSRF 中介層完全不理會
它。開關不設時預設只在 Development 環境開放；要在 Production 打開，改一個環境變數
`GraphQl__ExposeSchema=true` 就好，不用重新編譯。

schema 揭露跟查詢執行是兩件事：一個沒帶任何身分的 `POST /graphql` 查詢，在 Production 底下一樣正常
回資料。真正受影響的只是依賴 schema 的工具——codegen、schema 匯入、IDE 外掛——這些應該指向一個
Development 或 staging 環境。Nitro 這個內建的瀏覽器 IDE 不看這個開關，一律只在 Development 環境出
現。

| 路由 | Development | Production |
|---|---|---|
| introspection、`?sdl` | 開（預設） | 依 `GraphQl:ExposeSchema` |
| Nitro IDE | 開 | 一律關 |

認證規則跟 REST 共用：一個用 cookie 認證的 `/graphql` 請求，一樣要帶 `X-Struo-CSRF` 標頭，因為
GraphQL over HTTP 一律是 `POST`，這條規則對純讀取查詢跟對 mutation 一樣適用，見
[REST API 慣例那一章](12-rest-conventions.md)。bearer 權杖一樣能認證 `/graphql`，即使這條路由沒有
標明 `[Authorize]`，帶 bearer 的呼叫端會被當成自己的身分解析，也不需要 CSRF 標頭。

## 查詢：單筆、清單、引數

單筆欄位是 `{collection}(id: ID!, locale: String)`，找不到或已經軟刪除的 id 回 `null`，不是錯誤——
GraphQL 的 `null` 對應 REST 的 404，意圖一樣，形狀不同。清單欄位是複數名稱，帶九個引數：`filter`、
`sort: [String!]`、`limit: Int`、`offset: Int`、`search: String`、`locale: String`、
`deleted: DeletedFilter`、`facets: [String!]`、`aggregate: AggregateInput`；沒有 `fields` 也沒有
`deep`，投影跟關聯展開完全由客戶端自己的 selection set 決定。

`deleted` 是 SDL enum `EXCLUDE`／`ONLY`／`WITH`，套用同一個共用的列舉，`ONLY`／`WITH` 走一樣的刪
除授權門檻。

`facets` 與 `aggregate` 只有 root 清單欄位有，巢狀 to-many 清單沒有。回應裡的 `facets` 是
`[FacetResult!]!`——沒帶引數時是空陣列而不是省略，`aggregate` 是 `Any`，沒帶引數時是 `null`；
`FacetResult { field: String!, values: [FacetValue!]! }` 與
`FacetValue { value: Any, count: Int! }` 是兩個共用輸出型別，`AggregateInput` 依 REST 的彙總運算子
清單一個運算子一個 `[String!]` 欄位。

`Any` 這個純量保留原始的 JSON 種類，數字型 facet 值回來是數字而不是字串。`FacetValue.count` 跟清單
欄位的 `total` 都是不可為 null 的 `Int!`；只有 `count` 另外套了一層 checked cast，bucket 大到不合
理時會丟例外而不是悄悄溢位，`total` 則只是共用同一個 32 位元上限。

翻譯的欄位與 facet 值裡的 `Date`、`DateTime`、`Time`，文字跟欄位本身印出來的一樣。`Date` 是
`2026-03-04`；`DateTime` 與 `Time` 是以 `Z` 結尾的 UTC 時刻，例如
`2026-03-04T05:06:07.25Z`，有小數秒才印小數秒。彙總的 `min` 與 `max` 作用在 `Date` 或 `DateTime`
欄位時印法相同，`Time` 欄位不能做 `min`／`max`。對關聯集合的欄位做 facet，例如 `categories`
上的 `articles.publishedAt`，印法相同。null 值就是 `null`。版本紀錄的 `snapshot` 也是 `Any`，
它怎麼印日期時間見[第 9 章](09-revisions-and-trash.md)。

下面這個查詢示範單筆欄位、多對一關聯（不帶引數）跟巢狀 to-many 清單自己的 `sort`／`limit` 引數；後
面例子延用同一批測試資料，id 是資料本身的，讀者環境會不同：

```graphql
query { article(id: "01a2079c-7ee6-749b-95b0-b8a92cfca896") { id status version category { name } tags(sort: ["name"], limit: 1) { name } } }
```

實際回應：

```json
{"data":{"article":{"id":"01a2079c-7ee6-749b-95b0-b8a92cfca896","status":"published","version":3,"category":{"name":"Guides"},"tags":[{"name":"release"}]}}}
```

`article` 只帶 `id` 就找回同一筆項目；`category` 是多對一，直接是一個物件、沒有任何引數；`tags` 是多
對多，帶了自己的 `sort: ["name"], limit: 1`，只回一筆。`translations` 陣列的讀取形狀，
本章開頭「Schema 從 metadata 產生」一節已經說明過。清單欄位一次帶齊 `sort`、`limit`、`facets`、
`aggregate` 也一樣合法：

```graphql
query { articles(filter: { status: { eq: "published" } }, sort: ["-publishedAt"], limit: 1, facets: ["status"], aggregate: { count: ["publishedAt"] }) { total facets { field values { value count } } aggregate } }
```

回應顯示 `total` 是 2、`facets.status` 回報 `published` 兩篇與 `draft` 一篇；facet 與彙總的算法跟
[第 11 章](11-query-advanced.md)完全一樣。

## 篩選

篩選跟第 10 章是同一套語意——同一個驗證器、同一棵過濾樹、同一組深度與條件數上限，見
[查詢：過濾、排序、分頁](10-query-basics.md)——這裡只列拼法差異：

- 運算子 token：`_eq`→`eq`、`_neq`→`neq`、`_in`→`in`、`_nin`→`nin`、`_lt`→`lt`、`_lte`→`lte`、
  `_gt`→`gt`、`_gte`→`gte`、`_contains`→`contains`、`_starts_with`→`startsWith`、`_ends_with`→
  `endsWith`。
- `_null`／`_nnull` → 合成一個引數 `isNull: true`／`isNull: false`。
- `_some`／`_none` → `some`／`none`。
- `_junction.<欄位>` → 巢狀的 `junction: { <欄位>: … }`。
- `_and`／`_or` → `and`／`or`。
- 關聯的篩選輸入：帶可揭露 payload 的關聯，型別另有一個（見下）。

拼法之外，有兩個地方是真正的行為差異。第一個是 `isNull`：不同於 REST 的 `_null`／`_nnull`，這個引
數的值真的會被讀。第二個是哪些欄位介面可以拿來篩：REST 對哪個介面可以篩沒有限制，只看欄位名稱在不
在白名單裡；GraphQL 只替 `Text`、`Textarea`、`Slug`、`Email`、`Url`、`Color`、`Phone`、`Select`、
`Radio`、`Number`、`Slider`、`Rating`、`Boolean`、`Checkbox`、`Date`、`DateTime`、`Time`、`Uuid`
介面的欄位產生篩選欄位。`RichText`、`Markdown`、`Code`、`Json`、`KeyValue`、`MultiSelect`、
`CheckboxGroup`、`Tags`、`Files`、`File`、`Image`、`Repeater` 這些欄位在 GraphQL 完全沒有篩選
欄位（`Hidden`、`Divider`、`Password` 欄位則根本不在 GraphQL schema 裡）。

字串篩選帶有 `contains`、`startsWith`、`endsWith`，`createdBy` 這類 id 欄位也在內。這三個運算子只有
文字欄位接受；用在其他欄位，請求會失敗，`errors` 項目的 code 是 `BAD_USER_INPUT`，訊息點名欄位與運算
子。訊息用 REST 的拼法稱呼運算子（`_contains`、`_null`）；上面運算子 token 的清單給出各自的 GraphQL
拼法（`contains`、`isNull`）。第 10 章另外兩條規則在 GraphQL 碰不到：值是 `null` 的運算子在規則執行
前就被略過，單值運算子（例如 `eq`）收到清單則由 GraphQL schema 以它自己的訊息拒絕。

數字欄位的篩選輸入跟著它的型別走：`Int` 欄位（`int`）用 `IntFilter`，`Long` 欄位
（`long`，例如檔案大小）用 `LongFilter`，其他數字用 `FloatFilter`。各自吃自己的純量，所以 `Long` 的
運算元可以超過 2^31 − 1，`Int` 的不行。

多對一關聯在篩選輸入裡貢獻兩個欄位：自己的外鍵當 `IdFilter`（產生出來的純量篩選輸入型別之一，id 型
欄位專用），加上目標型別的巢狀篩選輸入；一對多或
多對多關聯則只貢獻巢狀篩選輸入這一個。跨關聯的巢狀篩選攤平成 REST 用的同一條帶點號路徑，兩邊收斂到
同一棵過濾樹。GraphQL 沒有自己的排序文法，`sort: [String!]` 收的是 REST `sort=` 同一套 `-field` 字
串。

`some`／`none` 是每個篩選輸入都帶的保留字：只在關聯自己的內層篩選裡才有意義，直接用在 root 或巢狀
清單自己的 `filter` 引數上會被拒絕（`'some' is only valid inside a relation filter.`），緊接著再巢
狀一層也被拒絕，要改成一層巢狀關聯篩選來表達；空的或非物件的值也被拒絕。巢狀 to-many 清單自己的
`filter` 引數型別是普通的目標篩選輸入，不是關聯專屬的那個，`some`／`none` 用在那裡一樣被拒絕，是
各自獨立的根。

多對多關聯如果 junction 帶了可揭露的 payload，父層篩選輸入上這個關聯的欄位型別會換成
`<Parent><Rel>RelationFilterInput`，多一個 `junction: <Parent><Rel>JunctionFilterInput` 欄位，跟目
標本身可篩的欄位、自己的 `and`／`or`／`some`／`none` 並列；沒有可篩欄位時，這整個 `junction` 欄位
跟對應的輸入型別一起省略。送出的 `junction` 篩選攤平成 REST 的 `_junction.<欄位>`。下面這個查詢示範
同一列量詞（`some` 配 `junction`）跟 `tagsLinks` 的讀取形狀：

<!-- capture: ch14-some-junction -->
```text
$ POST /graphql
body:
{"query": "query { articles(filter: { tags: { some: { name: { eq: \"howto\" }, junction: { note: { eq: \"hero\" } } } } }) { total items { id status tagsLinks { node { name } junction { note } } } } }"}
{"data":{"articles":{"total":1,"items":[{"id":"01a2079c-7ee6-749b-95b0-b8a92cfca896","status":"published","tagsLinks":[{"node":{"name":"howto"},"junction":{"note":"hero"}}]}]}}}
HTTP_STATUS:200
```

`total` 是 1，跟第 10 章同一個量詞查詢的結果一樣；`tagsLinks` 同時回出 `node`（目標本身）與
`junction`（連結上的 payload）。

## 巢狀 to-many 清單的引數

巢狀的 to-many 關聯欄位帶自己的四個引數：`filter`、`sort: [String!]`、`limit: Int`、
`offset: Int`，跟 root 清單欄位是分開算的一套，靠走訪客戶端自己的 selection 樹解析成跟 REST
`deep=` 一樣的巢狀展開規格。巢狀 `offset` 超過 `Query:MaxOffset`（預設 100000）會是點名該關聯與上限的
`BAD_USER_INPUT` 錯誤。多對一關聯則相反：一律是不帶任何引數的物件欄位，因為最多只解析到一列，
上一節看到的 `tags(sort: ["name"], limit: 1)` 就是這組引數的實際用法。

同一層選兩次同一個關聯（`tags` 與 `tagsLinks`，或一個別名重複），巢狀展開會合併，但只有第一次出現
的 `filter`／`sort`／`limit`／`offset` 才算數，不是各自獨立解析兩次。

## Mutation：create／update／delete

每個集合固定四個 mutation；開了版本紀錄的集合多一個 `revert<X>`。

每個 mutation 都需要已登入的呼叫端（session cookie 或 bearer token），不管 `public` 角色被授予什麼：
匿名 mutation 在任何權限檢查之前就拿到 `UNAUTHORIZED`，跟 REST 的寫入一樣。查詢仍然對匿名呼叫端開放，範圍
就是讀取授權。

| Mutation | 簽名 |
|---|---|
| `create<X>` | `(input: <X>CreateInput!, locale: String): <X>` |
| `update<X>` | `(id: ID!, input: <X>UpdateInput!, locale: String): <X>` |
| `delete<X>` | `(id: ID!, purge: Boolean): Boolean` |
| `restore<X>` | `(id: ID!): <X>` |
| `revert<X>` | `(id: ID!, revisionNumber: Int!, version: Long): <X>` |

`locale` 引數只出現在單筆查詢、清單查詢、`create`、`update` 上，`delete`／`restore`／`revert` 都沒有。
建立輸入帶可寫的自有欄位、多對一外鍵、多對多的 id 陣列，跟型別化的翻譯清單；更新輸入是同一份再加一個
`version: Long`。`version` 為 `null` 或沒帶時不做比對。GraphQL 的 `Long` 型別會在 resolver 執行前拒
絕不是 64 位元整數的值，什麼都不會寫入。以字面值寫在 `version` 輸入欄位或 `revert<X>` 引數上（`1.5`、
`"abc"`、`true`，或超出 64 位元範圍的整數），回 `INVALID_VERSION`。以變數傳入，或以字面值寫在其他任
何要 `Int`、`Long` 或 `Float` 的位置（`limit`、`offset`、`revisionNumber`、篩選的運算元、`in` 這類清
單運算元的每個元素），回 `BAD_USER_INPUT`，因為變數的錯誤沒有說明它餵給哪個欄位。訊息會說明這個值該
是什麼，也都不會記成伺服器錯誤。HTTP 狀態依 GraphQL 自己的規則，所以要看代碼，不要看狀態。

create 跟 update 把型別化的輸入轉回項目服務原本就吃的那份 JSON body，寫完之後重新讀一次那一列，讓
回傳的節點形狀跟一個查詢會產生的一模一樣；重讀被 RBAC 擋下時，退回原始的寫入結果，不會讓一次成功的
寫入被後續的讀取權限擋住，逼呼叫端誤判成失敗而重試。

HotChocolate 會把每個宣告出來的輸入欄位都補上 `null`，不管呼叫端有沒有送——這會讓一次局部更新變得
跟「其餘欄位全部清空」分不出來。因此兩個 resolver 都只把呼叫端真的送出的鍵交給服務層，巢狀輸入也一
樣。[REST API 慣例](12-rest-conventions.md)裡「沒送維持原值、明確送 null 一樣被擋」的規則在這裡完
全一樣。

多對多關聯在建立與更新輸入上是一個純 `[ID!]` 的目標 id 陣列，跟 REST 共用同一套同步邏輯。
`AdminOnly` 的寫入一樣要求超級管理員，被限制式關聯擋下的刪除一樣回 `CONFLICT`。

`delete`／`restore` 對一個未知 id 回 `false`／`null`，不是拋錯——跟查詢一樣，「id 不存在」是資料，
不是故障；已經在垃圾桶裡的項目再刪一次回的仍然是 `true`，刪除是等冪的，只有未知 id 才回 `false`，
而那一次不會再動 `version`，也不會再記一筆版本紀錄。`delete<X>(purge:)` 預設是 `false`，垃圾桶與清
除的規則跟 REST 一致。

GraphQL 沒有 `POST .../query`、`fields=`、`?purge=` 這些解析怪癖的對應，也沒有檔案端點：檔案上傳只
能走 REST。

### `<rel>Links`

一個多對多關聯只要 junction 宣告了至少一個可揭露的 payload 欄位，schema 就會額外產生一組讀寫都更完
整的欄位，跟純 id 的 `<rel>`／`[ID!]` 並存；沒有可揭露 payload 的關聯完全不會有這組東西。讀取端
`<rel>Links: [<Parent><Rel>Link!]` 跟 `<rel>: [<Target>!]` 並列，連結型別是
`{ node: <Target>!, junction: <Parent><Rel>Junction }`——讀不到 junction collection 時 `junction`
是 `null`，這也是這個欄位沒有標成不可為 null 的原因。

寫入端，建立與更新輸入上多一個 `<rel>Links: [<Parent><Rel>LinkInput!]`，連結輸入是
`{ id: ID!, ……可寫的 payload 欄位 }`。以 Blog 樣板的 `Article.tags` 為例（節錄自
`GET /graphql?sdl` 的實際回應）：

```graphql
type ArticleTagsJunction {
  note: String
}

type ArticleTagsLink {
  node: Tag!
  junction: ArticleTagsJunction
}

input ArticleTagsLinkInput {
  id: ID!
  note: String
}
```

`Article` 型別上對應多出來的兩個欄位：

```graphql
tags(filter: TagFilterInput, sort: [String!], limit: Int, offset: Int): [Tag!]
tagsLinks: [ArticleTagsLink!]
```

送出的 `<rel>Links` 會轉成 REST 的混合陣列寫入形狀；一次 mutation 如果同時送 `<rel>` 跟
`<rel>Links`，`<rel>Links` 整個贏——就算明確送 `<rel>Links: null`，也會蓋掉同時送出的 `<rel>` 陣
列，而不是放著不管。選 `<rel>Links` 一樣觸發跟選 `<rel>` 相同的展開，就算沒選 `node` 子欄位也一
樣；整組附加介面只要 payload 欄位全部隱藏或無法對應，就一起省略。下面是實際送出 `tagsLinks` 之後的
回應：

<!-- capture: ch14-update-tagslinks -->
```text
$ POST /graphql
body:
{"query": "mutation { updateArticle(id: \"01a2079c-7ee6-749b-95b0-b8a92cfca896\", input: { tagsLinks: [{ id: \"01a2079c-7eda-7db8-be61-b1683556f728\", note: \"editor pick\" }] }) { id tagsLinks { node { name } junction { note } } } }"}
{"data":{"updateArticle":{"id":"01a2079c-7ee6-749b-95b0-b8a92cfca896","tagsLinks":[{"node":{"name":"release"},"junction":{"note":"editor pick"}}]}}}
HTTP_STATUS:200
```

`tagsLinks` 整組取代這篇文章的標籤連結：回應只列出 `release`，備註是「editor pick」，送出的陣列
沒有提到的 `howto` 連結不在了。再送一次 mutation，同時給 `tags: ["<howto 的 id>"]` 跟
`tagsLinks: null`，結果是 `tagsLinks` 的 `null` 贏，連結維持不變、`tags` 陣列裡的內容根本沒被
套用。

## 翻譯

有翻譯 sidecar 的集合，建立跟更新輸入都多一個 `translations: [<X>TranslationInput!]`，每筆是
`{ locale: String!, fields: <X>TranslationFieldsInput }`——讀取端那份陣列形狀，原樣鏡射回寫入端。
resolver 把這份陣列轉回寫入路徑原本吃的、以 locale 當鍵的物件，轉換之前已經套用過「只留真的有送的
欄位」這層修剪；同一個 locale 出現兩次，mutation 以 `BAD_USER_INPUT` 失敗，與 REST 一致。可翻譯的自有
欄位不在建立與更新輸入裡出現，只能透過這個型別化的 `translations` 輸入去改；建立時一樣要求有一筆預設
locale 的翻譯。

更新時，已存的那一列是逐欄位合併：輸入有送的欄位會更新，沒送的保留原值，明確送 `null` 則清空。缺必填
翻譯時，錯誤的 `extensions.code` 是 `BAD_USER_INPUT`，並帶 `extensions.details`，是
`{ field, message, locale }` 的清單。

更新時，`fields: null` 的那筆會刪掉該 locale 的翻譯，規則跟 REST 的 `null` 相同
（[第 7 章](07-i18n.md)）：預設語言不能刪，沒有已存列的 locale 會略過。漏掉 `fields` 的那筆會被
拒絕。`revert{X}` 會移除快照裡沒有的 locale，跟 REST 一樣。

改一篇文章 `en` 翻譯的標題、其餘欄位不動，`updateArticle` 回傳的 `translations` 陣列裡只有 `title`
換了新值，`zh-TW` 那筆完全沒被動到。

## 版本紀錄

一個宣告 `Revisions = true` 的集合，額外多三個介面：
`{collection}Revisions(id: ID!): [Revision!]!`、
`{collection}Revision(id: ID!, revisionNumber: Int!): Revision`、
`revert{X}(id: ID!, revisionNumber: Int!, version: Long): X`。

共用的 `Revision` 型別是
`{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber, snapshot }`；列表欄位上
`snapshot` 一律是 `null`，單筆欄位才會填值，跟 REST 一致；`sourceRevisionNumber` 只有還原操作留下
的那筆紀錄才會填，其餘操作都是空的。

Blog 樣板的 `article` 開了這個選項：`articleRevisions` 回一份由新到舊的 metadata 清單，不含快照，
`articleRevision` 多要一筆版本編號、回傳連同結構化的 `snapshot`，`revertArticle` 套用該筆快照、回
傳還原後的項目本身。

## 錯誤形狀

匿名呼叫端送一個沒有讀取授權的查詢，會發生這一章讀者最容易意外的事：

<!-- capture: ch14-anon-users -->
```text
$ POST /graphql (no cookie)
body:
{"query":"{ users { total } }"}
HTTP/1.1 200 OK
Content-Type: application/graphql-response+json; charset=utf-8
Date: Mon, 23 Nov 2026 08:53:47 GMT
Server: Kestrel
Vary: Accept
X-Content-Type-Options: nosniff

{"errors":[{"message":"Read not permitted.","path":["users"],"extensions":{"code":"UNAUTHORIZED"}}],"data":null}
HTTP_STATUS:200
```

回應狀態是 `200`，不是 `401`；資料是 `null`，錯誤本身在 `errors` 陣列裡，`extensions.code` 是
`UNAUTHORIZED`。對 resolver 層級的錯誤，呼叫端得看每一則錯誤自己的 `extensions.code`，不能看狀態列。

`errors` 陣列裡的每一則錯誤還帶有 `extensions.traceId`，跟 REST 錯誤信封帶的是同一個請求 trace id（見
[第 12 章](12-rest-conventions.md)），本節的每一種錯誤都有。這裡的範例都省略了它。

GraphQL 的錯誤過濾器是 REST 例外處理器的對應版本，把 resolver 丟出的例外套進同一份對照表，蓋上一模
一樣的穩定代碼字串；resolver 丟出的例外，傳輸層的狀態永遠是 `200`，文件在 resolver 開始跑之前就
出錯的情形見下一段。`SEARCH_UNAVAILABLE` 也一樣——REST 對應到 503，這裡是 `200` 配這個代碼。沒有
對照表可查的例外會被遮罩成固定的通用訊息，伺服器端照樣完整記錄，搜尋提供者失敗的訊息也是同一種
遮罩。

還有一種失敗走另一條路。一個文件在任何 resolver 開始跑之前就出錯，不帶任何例外：文件指名了不存在
的欄位時，這則錯誤沒有 `extensions.code`；超過深度上限的文件同樣沒有；剖析不了的文件帶 `HC0011`，
超過 cost 上限的文件帶 `HC0047`，這兩個是 HotChocolate 自己的代碼。這些錯誤都沒有 `data` 鍵，在請
求沒帶 `Accept` 或帶 `*/*` 時回 HTTP `400`；帶 `Accept: application/json` 時，同一份錯誤以 `200` 回
應。上限與自我參照深度的細節在後面的「深度上限」一節。下面的請求沒帶 `Accept`：

<!-- capture: ch14-bogus-field-400 -->
```text
$ POST /graphql
body:
{"query": "query { bogusThing { id } }"}
HTTP/1.1 400 Bad Request
Content-Type: application/graphql-response+json; charset=utf-8
Date: Mon, 23 Nov 2026 08:53:47 GMT
Server: Kestrel
Vary: Accept
X-Content-Type-Options: nosniff

{"errors":[{"message":"The field `bogusThing` does not exist on the type `Query`.","locations":[{"line":1,"column":9}],"extensions":{"type":"Query","field":"bogusThing","responseName":"bogusThing","specifiedBy":"https://spec.graphql.org/September2025/#sec-Field-Selections"}}]}
HTTP_STATUS:400
```

錯誤訊息指名了不存在的欄位 `bogusThing`，但沒有 `extensions.code`——判斷一則 GraphQL 錯誤屬於哪一種，
看的是回應有沒有 `data` 鍵，不是狀態碼：resolver 丟出的錯誤與共用驗證器的錯誤，回應帶 `data`
（`null`，或某個成員是 `null` 的物件），代碼是[第 12 章](12-rest-conventions.md#錯誤代碼)列出的
穩定代碼，狀態碼是 `200`；文件在 resolver 開始跑之前就出錯時，回應沒有 `data` 鍵，錯誤沒有代碼，
或只帶 GraphQL 伺服器自己的代碼，狀態碼隨 `Accept` 而變；格式不對的請求是例外，見下一段：它帶
`BAD_USER_INPUT`，狀態碼是 `400`。

以 POST 送出 JSON body，而伺服器讀不成 GraphQL 請求時，沒帶 `Accept`、帶 `*/*` 或帶
`application/json` 都回 HTTP `400`，沒有 `data` 鍵，只有一則錯誤，`extensions.code` 是
`BAD_USER_INPUT`，訊息是 GraphQL 伺服器自己的。例如 body 不是 JSON，訊息是
`Invalid JSON document.`；沒有 `query` 或 `query` 為空，訊息是
`Request must contain either a query or a document id.`；`query`、`operationName`、`variables` 或
`extensions` 的型別不對，訊息像 `Invalid 'query' value. Expected string or null, but got Number.`。
這類請求不會寫 error 等級的記錄。

JSON 請求 body 超過 `Api:MaxJsonBodyBytes` 時，回 HTTP `413`，內容是一則 `extensions.code` 為
`PAYLOAD_TOO_LARGE` 的錯誤，形狀同樣是 `errors`，沒有 `data` 鍵。請求不論是宣告了長度還是串流送出，
結果都一樣，訊息是固定的 `Request body is too large.`，句子裡不帶任何上限。以串流送出、沒有宣告長度
的 body 因為其他原因讀不出來時，回該失敗的狀態 `400` 或 `408`，內容是一則 `BAD_USER_INPUT` 的錯誤，
訊息是 `The request body could not be read.`。

共用驗證器對 `sort` 引數與關聯路徑的訊息——未知欄位、未知關聯、跨多對多或一對多關聯的排序、超過
`Query:MaxRelationDepth` 的 `sort`／`filter` 路徑與選擇集，內容見[第 10 章](10-query-basics.md)與
[第 11 章](11-query-advanced.md)——在 GraphQL 上一律標成 `BAD_USER_INPUT`，走的是第一種、`200` 配
代碼的路。型別化的 `filter` 引數不走這條路：`filter` 裡寫了 schema 沒有的欄位，是上面那種沒有代碼
的 request 層級錯誤。`sort` 或 `filter` 路徑穿過呼叫端讀不到的關聯，是例外：錯誤代碼是
`FORBIDDEN`，同樣走 `200` 配代碼這條路（REST 回 `403`）；選取一個讀不到的關聯不是錯誤，那個欄位回
`null`。

## 深度上限

七個獨立的上限各自把關：

| 限制 | 數值 | 防的是什麼 |
|---|---|---|
| 執行深度（`GraphQl:MaxExecutionDepth`） | 預設 12 層 | 巢狀選擇集過深，introspection 欄位不算 |
| Cost（`GraphQl:MaxFieldCost`／`GraphQl:MaxTypeCost`） | 預設 150 | alias 放大：同一昂貴欄位配多別名重複算 |
| 欄位重複（`GraphQl:MaxFieldCycleDepth`） | 預設 3 次 | 沿著 `parent` 這類來回的關聯一路往下寫 |
| 關聯路徑深度（`Query:MaxRelationDepth`） | 預設 6 | 跨關聯篩選與巢狀展開，跟 REST 共用設定 |
| 排序欄位（`Query:MaxSortFields`） | 預設 5 | 過長的排序，跟 REST 共用設定 |
| 關聯節點（`Query:MaxDeepRelations`） | 預設 20 | 過寬的關聯展開，跟 REST 共用設定 |
| 每個關聯的列數（`Query:MaxExpandRows`） | 預設 1000 | 單次關聯抓取讀太多列，跟 REST 共用設定 |

超過執行深度上限的文件，在 resolver 開始跑之前就被拒絕：訊息是 `The GraphQL document has an
execution depth of N which exceeds the max allowed execution depth of 12.`，N 是文件實際的深度，12
是設定的 `GraphQl:MaxExecutionDepth`，`extensions` 帶 `detectedExecutionDepth` 與
`allowedExecutionDepth`，沒有 `extensions.code`。選取的每一層欄位都算一層，所以
`{ categories { items { name } } }` 是 3 層。

超過 cost 上限的文件同樣被拒絕：`extensions.code` 是 HotChocolate 的 `HC0047`，訊息是 `The maximum
allowed field cost was exceeded.`，`extensions` 帶 `fieldCost` 與 `maxFieldCost`。50 個別名各選一次
`articles { items { id } }`，成本是 550，上限是預設的 150。

欄位重複是另一條獨立的規則，不屬於執行深度上限。`GraphQl:MaxFieldCycleDepth` 設定同一個欄位（同一個
型別上的同一個欄位名）在一條路徑上最多出現幾次，再多一次就被拒絕；預設是 3，下面的例子都用預設值。
最簡單的情形是自我參照的 `parent`：在
`{ categories { items { parent { parent { parent { name } } } } } }` 這樣的路徑上，`parent` 最多巢狀
3 層。穿過兩個型別來回的路徑同樣受限，`Category.articles` 在
`{ categories { items { articles { category { articles { category { articles { id } } } } } } } }`
裡出現 3 次，可以通過驗證，再多一次就被拒絕。被拒絕的回應只有一則 `extensions.code` 為 `HC0087` 的
錯誤，訊息是 `Maximum allowed coordinate cycle depth was exceeded.`；一份文件同時超過執行深度上限與
這條規則時，兩則錯誤都在回應裡，沒有任何欄位重複超過上限、但深度超過上限的文件，回應只有深度那一則
錯誤。

這三種錯誤都沒有 `data` 鍵；沒帶 `Accept` 或帶 `*/*` 時回 HTTP `400`，帶 `Accept: application/json`
時回 `200`。

你選取的每個關聯欄位都算 deep 樹的一個節點，所以查詢選了很多關聯時，不必傳任何 `deep` 參數就可能撞到
`MaxDeepRelations`。

執行深度、cost 與欄位重複這三個上限設在 `GraphQl:` 底下（見[第 4 章](04-configuration.md)），而且
只能在設定裡設：在程式碼裡設定 `GraphQlOptions` 沒有作用。成本上限的預設值是只針對核心 schema
量出來的：最重的一般查詢成本是 33，50 個 alias 的放大是 550，150 落在兩者之間。fork 加了比較重的
查詢，就像 `GraphQlCostAnalysisTests` 對核心做的那樣，量出自己最重的正當查詢，把成本上限設在它的
上面一點。

## 加上自己的型別與欄位

fork 從 `Program.cs` 加進 schema。`ConfigureGraphQl` 交給你的是
HotChocolate 自己的 `IRequestExecutorBuilder`，所以你對它寫的是 HotChocolate 的 API，不是 StruoCMS 的。
它在核心 schema 註冊之後執行：

```csharp
using HotChocolate.Types;

public sealed record BuildInfo(string Version);

[ExtendObjectType("Query")]
public sealed class BuildInfoQuery
{
    public BuildInfo GetBuildInfo() => new("1.4.2");
}
```

在 `Program.cs` 透過 `AddStruoCms()` 的回呼註冊：

```csharp
builder.AddStruoCms(cms => cms.ConfigureGraphQl(graphQl => graphQl
    .AddTypeExtension<BuildInfoQuery>()));
```

之後 `{ buildInfo { version } }` 就和產生出來的欄位一起由 `/graphql` 提供。

你加在 `Mutation` 上的欄位，要自己保護。CMS 的登入檢查與權限是掛在它產生出來的欄位上，不是掛在
`Mutation` 型別上，所以 fork 自己的欄位，除非自己檢查，匿名呼叫端也能執行。你的動作也是在核心之後執行，
所以你在 builder 上改的設定（introspection、成本與深度規則、相依性注入的範圍）會取代核心的。CSRF 防護是
HTTP 中介層，照樣適用。要求已登入呼叫端的 mutation 長這樣：

```csharp
[ExtendObjectType("Mutation")]
public sealed class BuildInfoMutation
{
    public string RefreshBuildInfo([Service] IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? "refreshed"
            : throw new GraphQLException("Sign in to run this mutation.");
}
```

你的型別或根欄位，不能用 schema 已經有的名稱：集合產生的型別、`Revision` 這類共用型別，或 `articles` 這類
產生出來的根欄位。名稱衝突時啟動會失敗，訊息是 `GraphQL name collision`，並點出那個名稱；改掉自己的名稱即可。

## 接下來

上傳、下載與轉檔都留在 REST 這一側，檔案與媒體是下一個主題。
