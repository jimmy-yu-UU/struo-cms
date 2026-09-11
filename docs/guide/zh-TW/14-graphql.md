# 14. GraphQL API

同一套 metadata 產生的 GraphQL schema，怎麼查、怎麼改，跟 REST 差在哪，是這一章的主題。

## Schema 從 metadata 產生

GraphQL 跟 REST 讀的是同一份啟動時就快取好的集合 metadata，是第二套完整型別化的 API；schema 沒有任
何一份手寫的 SDL。啟動邏輯逐集合產生一個物件型別、一個清單包裝、一個篩選輸入、一個建立輸入、一個更
新輸入，外加兩個 root query 欄位與四個 root mutation 欄位。junction collection 在 schema 裡是一般
的集合，跟其他集合一樣有自己的型別與 mutation：`Hidden` 只影響後台側邊欄，不影響 schema 產生。

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

物件型別固定帶 `id: ID!` 與 `version: Long`；每個非隱藏、沒被排除的自有欄位各一個欄位，每個關聯也
各一個，有翻譯 sidecar 的集合再多一個 `translations: [Translation!]`。`version` 在每個型別上都宣
告，但只有繼承稽核基底的集合才會真正解析出值，其餘集合這一欄一律是 `null`。

`File`／`Image` 欄位除了自己的 `ID` 純量，還多一個同伴解析欄位（`heroImageId` → `heroImage`；沒有
`Id` 結尾的話改成加 `File` 尾綴），`Files` 欄位多一個 `<name>Files: [File!]`。清單包裝固定四個欄
位：`items`、`total`、`facets`、`aggregate`。

其餘介面到 SDL 有一份固定對照：文字類對應 `String`，`Boolean`／`Checkbox` 對應 `Boolean`，`Date`／
`DateTime` 對應同名型別，`Time` 也對應 `String`，多值欄位對應陣列，`Json`／`KeyValue` 對應 `Any`，
`File`／`Image`／`Uuid` 對應 `ID`；`Number`／`Slider`／`Rating` 看的是實際 CLR 型別而不是介面本
身，整數型對應 `Int` 或 `Long`，其餘對應 `Float`。

`Tags` 對應成 `[TagItem!]`，`Repeater` 依子欄位另外產生一個型別 `<X><Field>Item`（沒有子欄位就整個
省略）；`Password`、`Hidden`、`Divider` 完全不出現在 schema 裡。

`Translation { locale: String!, fields: Any! }` 把翻譯的讀取形狀定成一份陣列，每筆一個 locale，不
是 REST 那種以 locale 當鍵的物件；寫入形狀留給本章「翻譯」一節。

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

下面這個查詢示範單筆欄位、多對一關聯（不帶引數）跟巢狀 to-many 清單自己的 `sort`／`limit` 引數；後
面例子延用同一批測試資料，id 是資料本身的，讀者環境會不同：

```text
$ POST /graphql
body:
{"query": "query { article(id: \"01a08f92-3a18-7c5d-99a3-5b14cd1279ea\") { id status version translations { locale fields } category { name } tags(sort: [\"name\"], limit: 1) { name } } }"}
{"data":{"article":{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","status":"published","version":0,"translations":[{"locale":"en","fields":{"title":"Getting started","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}},{"locale":"zh-TW","fields":{"title":"開始使用","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}],"category":{"name":"Guides"},"tags":[{"name":"howto"}]}}}
HTTP_STATUS:200
```

`article` 只帶 `id` 就找回同一筆項目；`category` 是多對一，直接是一個物件、沒有任何引數；`tags` 是
多對多，帶了自己的 `sort: ["name"], limit: 1`，只回一筆。`translations` 就是前面說的那份陣列。清單
欄位一次帶齊 `sort`、`limit`、`facets`、`aggregate` 也一樣合法：

```graphql
query { articles(filter: { status: { eq: "published" } }, sort: ["-publishedAt"], limit: 1, facets: ["status"], aggregate: { count: ["publishedAt"] }) { total facets { field values { value count } } aggregate } }
```

回應顯示 `total` 是 2、`facets.status` 回報 `published` 兩篇與 `draft` 一篇；facet 與彙總的算法跟
第 11 章完全一樣，不受 `limit` 影響。

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
在白名單裡；GraphQL 只有純量介面能篩，`RichText`、`Markdown`、`Code`、`Json`、`KeyValue`、
`MultiSelect`、`CheckboxGroup`、`Tags`、`Files`、`File`、`Image`、`Repeater` 這些欄位在 GraphQL
完全沒有篩選欄位。

多對一關聯在篩選輸入裡貢獻兩個欄位：自己的外鍵當 `IdFilter`，加上目標型別的巢狀篩選輸入；一對多或
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
跟對應的輸入型別一起省略，攤平成 REST 的 `_junction.<欄位>`。下面這個查詢示範同一列量詞（`some` 配
`junction`）跟 `tagsLinks` 的讀取形狀：

```text
$ POST /graphql
body:
{"query": "query { articles(filter: { tags: { some: { name: { eq: \"howto\" }, junction: { note: { eq: \"hero\" } } } } }) { total items { id status tagsLinks { node { name } junction { note } } } } }"}
{"data":{"articles":{"total":1,"items":[{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","status":"published","tagsLinks":[{"node":{"name":"howto"},"junction":{"note":"hero"}}]}]}}}
HTTP_STATUS:200
```

`total` 是 1，跟第 10 章同一個量詞查詢的結果一樣；`tagsLinks` 同時回出 `node`（目標本身）與
`junction`（連結上的 payload）。

## 巢狀 to-many 清單的引數

巢狀的 to-many 關聯欄位帶自己的四個引數：`filter`、`sort: [String!]`、`limit: Int`、
`offset: Int`，跟 root 清單欄位是分開算的一套，靠走訪客戶端自己的 selection 樹解析成跟 REST
`deep=` 一樣的巢狀展開規格。多對一關聯則相反：一律是不帶任何引數的物件欄位，因為最多只解析到一列，
上一節看到的 `tags(sort: ["name"], limit: 1)` 就是這組引數的實際用法。

同一層選兩次同一個關聯（`tags` 與 `tagsLinks`，或一個別名重複），巢狀展開會合併，但只有第一次出現
的 `filter`／`sort`／`limit`／`offset` 才算數，不是各自獨立解析兩次。

## Mutation：create／update／delete

每個集合固定四個 mutation；開了版本紀錄的集合多一個 `revert<X>`。

| Mutation | 簽名 |
|---|---|
| `create<X>` | `(input: <X>CreateInput!, locale: String): <X>` |
| `update<X>` | `(id: ID!, input: <X>UpdateInput!, locale: String): <X>` |
| `delete<X>` | `(id: ID!, purge: Boolean): Boolean` |
| `restore<X>` | `(id: ID!): <X>` |
| `revert<X>` | `(id: ID!, revisionNumber: Int!): <X>` |

`locale` 引數只出現在單筆查詢、清單查詢、`create`、`update` 上，`delete`／`restore`／`revert` 都
沒有。建立輸入帶可寫的自有欄位、多對一外鍵、多對多的 id 陣列，跟型別化的翻譯清單；更新輸入是同一份
再加一個 `version: Long`。

create 跟 update 把型別化的輸入轉回項目服務原本就吃的那份 JSON body，寫完之後重新讀一次那一列，讓
回傳的節點形狀跟一個查詢會產生的一模一樣；重讀被 RBAC 擋下時，退回原始的寫入結果，不會讓一次成功的
寫入被後續的讀取權限擋住，逼呼叫端誤判成失敗而重試。

HotChocolate 會把每個宣告出來的輸入欄位都補上 `null`，不管呼叫端有沒有送——這會讓一次局部更新變得
跟「其餘欄位全部清空」分不出來。因此兩個 resolver 都只把呼叫端真的送出的鍵交給服務層，巢狀輸入也一
樣。[REST API 慣例](12-rest-conventions.md)裡「沒送維持原值、明確送 null 一樣被擋」的規則在這裡完
全一樣。

多對多關聯在建立與更新輸入上是一個純 `[ID!]` 的目標 id 陣列，跟 REST 共用同一套同步邏輯。
`AdminOnly` 的寫入一樣要求超級管理員，被限制式關聯擋下的刪除一樣回 `CONFLICT`；`delete`／`restore`
對一個未知 id 回 `false`／`null`，不是拋錯——跟查詢一樣，「id 不存在」是資料，不是故障；已經在垃圾
桶裡的項目再刪一次也是 `false`。`delete<X>(purge:)` 預設是 `false`，垃圾桶與清除的規則跟 REST 一
致。

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

```text
$ POST /graphql
body:
{"query": "mutation { updateArticle(id: \"01a08f92-3a18-7c5d-99a3-5b14cd1279ea\", input: { tagsLinks: [{ id: \"01a08f92-396e-7bf7-a77c-149c9aa732b1\", note: \"editor pick\" }] }) { id tagsLinks { node { name } junction { note } } } }"}
{"data":{"updateArticle":{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","tagsLinks":[{"node":{"name":"release"},"junction":{"note":"editor pick"}}]}}}
HTTP_STATUS:200
```

`release` 這個標籤被連結上，備註是「editor pick」。再送一次 mutation，同時給
`tags: ["<howto 的 id>"]` 跟 `tagsLinks: null`，結果是 `tagsLinks` 的 `null` 贏，連結維持不變、
`tags` 陣列裡的內容根本沒被套用。

## 翻譯

有翻譯 sidecar 的集合，建立跟更新輸入都多一個 `translations: [<X>TranslationInput!]`，每筆是
`{ locale: String!, fields: <X>TranslationFieldsInput! }`——讀取端那份陣列形狀，原樣鏡射回寫入端。
resolver 把這份陣列轉回寫入路徑原本吃的、以 locale 當鍵的物件，轉換之前已經套用過「只留真的有送的
欄位」這層修剪；同一個 locale 出現兩次，後面那筆贏。可翻譯的自有欄位不在建立與更新輸入裡出現，只能
透過這個型別化的 `translations` 輸入去改；建立時一樣要求有一筆預設 locale 的翻譯。

改一篇文章 `en` 翻譯的標題、其餘欄位不動，`updateArticle` 回傳的 `translations` 陣列裡只有 `title`
換了新值，`zh-TW` 那筆完全沒被動到。

## 版本紀錄

一個宣告 `Revisions = true` 的集合，額外多三個介面：
`{collection}Revisions(id: ID!): [Revision!]!`、
`{collection}Revision(id: ID!, revisionNumber: Int!): Revision`、
`revert{X}(id: ID!, revisionNumber: Int!): X`。

共用的 `Revision` 型別是
`{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber, snapshot }`；列表欄位上
`snapshot` 一律是 `null`，單筆欄位才會填值，跟 REST 一致；`sourceRevisionNumber` 只有還原操作留下
的那筆紀錄才會填，其餘操作都是空的。

Blog 樣板的 `article` 開了這個選項：`articleRevisions` 回一份由新到舊的 metadata 清單，不含快照，
`articleRevision` 多要一筆版本編號、回傳連同結構化的 `snapshot`，`revertArticle` 套用該筆快照、回
傳還原後的項目本身。

## 錯誤形狀

匿名呼叫端送一個沒有讀取授權的查詢，會發生這一章讀者最容易意外的事：

```text
$ POST /graphql (no cookie)
body:
{"query":"{ users { total } }"}
HTTP/1.1 200 OK
Content-Type: application/graphql-response+json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:38 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"errors":[{"message":"Read not permitted.","path":["users"],"extensions":{"code":"UNAUTHORIZED"}}],"data":null}
HTTP_STATUS:200
```

回應狀態是 `200`，不是 `401`；資料是 `null`，錯誤本身在 `errors` 陣列裡，`extensions.code` 是
`UNAUTHORIZED`。呼叫端得看每一則錯誤自己的 `extensions.code`，不能看狀態列——這是這一節最容易被誤判
的地方。

GraphQL 的錯誤過濾器是 REST 例外處理器的對應版本，把 resolver 丟出的例外套進同一份對照表，蓋上一模
一樣的穩定代碼字串，但傳輸層的狀態永遠留在 `200`。`SEARCH_UNAVAILABLE` 也一樣——REST 對應到 503，這
裡一樣是 `200` 配這個代碼。沒有對照表可查的例外會被遮罩成固定的通用訊息，伺服器端照樣完整記錄，搜
尋提供者失敗的訊息也是同一種遮罩。

還有一種失敗走另一條路。一個文件本身剖析不了、指名了不存在的欄位，或違反深度／cost 規則，在任何
resolver 開始跑之前就出錯，不帶任何例外，也就沒有 `extensions.code`，HotChocolate 直接回 HTTP
`400`：

```text
$ POST /graphql
body:
{"query": "query { bogusThing { id } }"}
HTTP/1.1 400 Bad Request
Content-Type: application/graphql-response+json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:38 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"errors":[{"message":"The field `bogusThing` does not exist on the type `Query`.","locations":[{"line":1,"column":9}],"extensions":{"type":"Query","field":"bogusThing","responseName":"bogusThing","specifiedBy":"https://spec.graphql.org/September2025/#sec-Field-Selections"}}]}
HTTP_STATUS:400
```

錯誤訊息指名了不存在的欄位 `bogusThing`，但沒有 `extensions.code`——這是判斷一則 GraphQL 錯誤該看狀
態碼還是該看 `extensions.code` 的分界：request 層級的結構性錯誤是 `400` 沒有代碼，resolver 執行時
丟出的例外是 `200` 配代碼。第 10 章與[第 11 章](11-query-advanced.md)列的每一句 REST 驗證訊息——未
知欄位、未知關聯、超出深度、讀不到的關聯——因為兩個協定共用同一個驗證器，在 GraphQL 上一律標成
`BAD_USER_INPUT`，走的是第一種、`200` 配代碼的路。

## 深度上限

三個獨立的上限各自把關：

| 限制 | 數值 | 防的是什麼 |
|---|---|---|
| 執行深度 | 12 層 | 巢狀選擇集過深，introspection 欄位不算 |
| Cost（`MaxFieldCost`／`MaxTypeCost`） | 150 | alias 放大：同一昂貴欄位配多別名重複算 |
| 關聯路徑深度（`Query:MaxRelationDepth`） | 預設 6 | 跨關聯篩選與巢狀展開，跟 REST 共用設定 |

## 接下來

上傳、下載與轉檔都留在 REST 這一側，檔案與媒體是下一個主題。
