# 11. 查詢：投影、深度展開、facet 與彙總

同一份清單查詢，可以少拿幾個欄位回來、把關聯多展開幾層、再順便算幾組摘要數字——這是這一章的三個主
題，都建在[第 10 章](10-query-basics.md)的查詢介面上。

## 欄位投影 `fields=`

`fields=` 只限制集合自己要投影哪些欄位；`id` 永遠會回來，不管有沒有列進 `fields=`。集合如果繼承
`AuditableEntity`，樂觀併發用的 `version` 也永遠會回來——只用 `IAuditable`、或完全沒繼承任何審計
基底的集合，任何回應都沒有 `version` 這個鍵。

`translations` 這份翻譯資料，以及任何 `deep` 展開出來的關聯，都是投影之後另外附加上去的，不管
`fields=` 有沒有點名它們，都照樣出現：

```text
$ GET /api/items/article?fields=id,status
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":4,"status":"draft","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}},{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}},{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":7,"status":"published","translations":{"en":{"title":"Getting started","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":3,"limit":25,"offset":0}}
HTTP_STATUS:200
```

只要求 `id` 跟 `status`，三篇文章卻都還帶著 `version` 跟完整的 `translations`——這兩者本來就不歸
`fields=` 管。後面的例子沿用同一批測試資料，id 是資料本身的，讀者環境會不同。

JSON 封裝形式是同一件事，鍵名是 `fields`，一個字串陣列，不是 `field` 也不是 `select`：
`{"fields":["id","status"]}`。

`fields=` 只投影集合自己的純量欄位，一條關聯路徑會被整個拒絕：

```text
$ GET /api/items/article?fields=category.name
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation paths are not supported in field selection: 'category.name'."}}
HTTP_STATUS:400
```

`category.name` 整條被拒絕，不是被忽略、也不是只投影 `category`。

`fields=` 只在清單與 `POST .../query` 這兩個讀取動作上生效。單筆讀取
`GET /api/items/{collection}/{id}` 只從查詢字串讀 `deep`，一律投影每個看得到的自有欄位，`fields=`
在那裡是被靜靜忽略的。

## 深度展開 `deep=`

從查詢語法的角度看，`deep` 只是模型上的另一個欄位，在篩選、排序、分頁都算完之後才處理——對象是已
經撈回來的那一頁父列，不是整張表。這代表 `deep` 底下的巢狀 `filter` 只會篩掉展開出來的關聯列，不
會讓父層那一頁跟著變小：`{"deep":{"tags":{"filter":{...}}}}` 讀起來像「只回傳有符合標籤的文
章」，其實不是——這個篩選是在父層那一頁已經固定之後才套用的。[第 8 章：關聯](08-relations.md)已
經說過整棵 `deep` 樹會在任何查詢真的執行之前驗證完，跟列數無關。

巢狀關聯自己的 `filter` 跟 `sort`，走的是同一個 `QueryValidator`，對照的是目標集合自己的中繼資料；巢
狀 `sort` 不能是帶點號的路徑，觸發的是另一個訊息——`Sort across relations is not supported for nested
lists: '<field>'.`——跟[第 10 章](10-query-basics.md)頂層 `sort=` 碰到多對多路徑時的 `Sort across
to-many relations is not supported: '<path>'.` 是兩回事，一個管巢狀清單自己的排序，一個管頂層排序。

junction 的 payload（`_junction` 鍵）[第 8 章：關聯](08-relations.md)已經介紹過，本章最後的完整
範例會再看到一次。

**深度上限。** `deep` 樹的深度上限跟篩選、排序共用同一個設定鍵：`Query:MaxRelationDepth`（預設
6），改法見[第 4 章：設定參考](04-configuration.md)。

這個上限含頭尾：`category` 加五層 `parent`，一共六層關聯跳，驗證會通過、請求正常執行；第七層才
會被擋下，訊息是 `Relation nesting too deep (depth {n}); the maximum is {max}.`。下面是踩到第
七層的請求：

```text
$ POST /api/items/article/query
body:
{"deep":{"category":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{"parent":{"deep":{}}}}}}}}}}}}}}}}
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation nesting too deep (depth 7); the maximum is 6."}}
HTTP_STATUS:400
```

`category` 加六層 `parent`，一共七個關聯跳，深度是 7，超過上限 6，所以被擋下；拿掉最內層的
`parent`、變回六層，就會是驗證通過、正常回應的請求。

## Facet 計數

`facets=` 在同一批經過篩選的結果上另外算摘要分佈，不會改變 `data` 或分頁；沒被要求就完全不會算，
連鍵本身都不會出現在 `meta` 裡，不是回傳一個 `null`。

查詢字串上是一份逗號清單，JSON 封裝裡是一個字串陣列；清單裡的重複路徑會被去掉重複、只留第一次出
現的那一份，既不會被拒絕，也不會被算兩次。判斷重複用的是精確比對（`StringComparer.Ordinal`），
不是欄位名稱那種不分大小寫的規則，所以 `status` 跟 `Status` 是兩條不同的路徑，都會被個別計算。

### 四種路徑形態

一個 facet 路徑只有四種形態；一條 facet 路徑最多只能跨一層關聯，也絕不能是量詞或 `_junction` 片
段：

| 形態 | 例 | 分組依據 |
|---|---|---|
| 自有純量欄位 | `facets=status` | 欄位本身 |
| 多對一外鍵 | `facets=categoryId` | 根列自己的外鍵欄位 |
| 純關聯名稱 | `facets=tags` | 目標 id |
| 一跳關聯＋葉欄位 | `facets=category.name` | 先依目標 id 分組，再換上葉欄位的值 |

純關聯名稱這種形態的目標 id，多對一取外鍵、一對多取子項自己的 id、多對多取 junction 的目標外鍵。

這些欄位介面可以拿來當 facet，自有欄位跟葉欄位共用同一份清單：

- 文字類：`Text`、`Textarea`、`Slug`、`Email`、`Url`、`Color`、`Phone`
- 選擇類：`Select`、`Radio`、`Boolean`、`Checkbox`
- 數值類：`Number`、`Slider`、`Rating`
- 時間與識別：`Date`、`DateTime`、`Time`、`Uuid`、`File`、`Image`

長文字（`RichText`／`Markdown`／`Code`）、多值介面（`MultiSelect`／`CheckboxGroup`／`Tags`／
`Repeater`）、結構化資料（`Json`／`KeyValue`／`Files`），以及 `Hidden`／`Divider`／`Password`
都不能拿來當 facet。一個隱藏欄位拿到的訊息，跟一個不存在的欄位完全一樣，所以 facet 路徑不能被拿
來探測一個隱藏欄位存不存在。

一跳關聯＋葉欄位如果葉欄位可翻譯，一律需要一個有效的查詢 locale——沒帶 `locale=` 就用站台預設
值，永遠有值可用。

### 剪枝：facet 為什麼會算到被篩掉的列

一個 facet 回答的是「如果改選這條路徑的每一個候選值，請求裡其餘的一切還會匹配多少
列？」，不是「目前這批結果裡有多少列帶這個值？」。

要拿到前者，每算一個 facet 之前都會先把同一個家族上的條件拿掉：

- 自有欄位 facet：拿掉這個欄位自己的條件。
- 多對一外鍵 facet：拿掉外鍵欄位本身的條件、任何以 `<relation>.` 開頭的路徑，以及對這個關聯的
  `_some`／`_none`。`categoryId` 跟 `category.name` 算同一個家族。

這個剪枝只認多對一自己的外鍵欄位當家族的根——一對多關聯宣告的外鍵是子項自己的欄位，一個根層篩選
剛好同名也不會被牽連。

一路拿掉下去，`filter` 有可能一條都不剩；被拿空的 `_and`／`_or` 群組會跟著塌縮，一路剪光時整個
請求就等於沒有 `filter`。

`search=` 也不會被剪枝，facet 剪的只是自己那份 `filter`，跟 `search` 是各自獨立的手續。

```text
$ GET /api/items/article?filter[categoryId][_eq]=01a08f92-3833-750e-bad3-6ee620f985c3&filter[status][_eq]=published&facets=status
{"success":true,"data":[...],"meta":{"total":2,"limit":25,"offset":0,"facets":{"status":[{"value":"published","count":2},{"value":"draft","count":1}]}}}
HTTP_STATUS:200
```

上面把 `data` 換成了 `...`——這裡的重點是 `meta`，兩篇文章的完整內容跟這一節無關。`data` 跟
`total` 只有兩篇已發佈的文章，跟 `filter` 說的一致——但 `facets.status` 照樣回報
`published: 2`、`draft: 1`，因為算 `status` 這個 facet 之前，`status` 自己的條件先被剪掉了，留
在 `categoryId=<Guides>` 這個分類底下的草稿一樣被算了進去。

驗收的辦法也一樣：對一個自有欄位 facet 而言，`{"value": v, "count": n}` 要等於把 facet 自己的條
件換成 `filter[<欄位>][_eq]=v` 之後，同一請求的 `meta.total`。

`count` 數的是相異的根列，不是關聯表上的原始列數。

to-many 的形態依目標 id 分組，用 `COUNT(DISTINCT ...)` 算：一個根列同時連到兩個共用某個值的目標
不會被算兩次。自有欄位跟外鍵不需要 `DISTINCT`，每個根列本來就只落在一組。

一跳關聯＋葉欄位是例外。它先做 id、計數查詢，再做一次葉值查詢，最後在記憶體裡把同值的分組合
併——一個根列同時連到兩個剛好共用同一個葉值的目標，這時會被算兩次。

`Query:MaxFacetValues`（預設 50）在第一段的目標 id 分組上就先截斷一次，合併之後又再截斷一次，
所以就算目標集合的相異葉值比上限少，合併後才會相等的兩個值仍然可能因為第一段截斷而漏掉其中一
個。

`deleted=` 只管根列本身：facet 只要碰到關聯的另一端，一律維持目標集合自己的軟刪除下限，不管外
層請求的 `deleted=` 是什麼；多對一外鍵這種形態是唯一的例外，因為它算的是根列自己的欄位，一個已
經被丟進垃圾桶的目標，id 照樣算得進去。

如果一個 fork 換掉了 `search=` 的候選 id 來源（這是擴充點一章的搜尋提供者話題），facet 跟彙總看
到的還是同一份、已經套上 `deleted=` 下限的候選集，不會因此洩漏一筆垃圾桶裡的資料列。

### NULL bucket、排序與數量上限

自有欄位跟多對一外鍵的 `NULL` 都有自己的 bucket，因為兩者分組用的欄位本來就是根列的一部分。純
關聯名稱這種形態永遠沒有「無關聯」的 bucket——建這種 bucket 需要一個反連接，實作裡沒有；
要問「沒有關聯的有幾筆」，`_none` 已經回答了。一跳關聯＋葉欄位的 `null` 是第三種、也是最容易
搞混的意義：目標 id 解析出來了，只是在這個 locale 下沒有翻譯列，不是「沒有目標」。

非翻譯的葉欄位還有第四種情況：如果目標列本身已經被硬刪除、或 id 對不到任何列，這個分組會被整個
丟掉，不會併進 `null` 這個 bucket。

每個 facet 都依 `count` 遞減、`value` 遞增排序，截到 `Query:MaxFacetValues`（預設 50），沒有
`otherCount` 這種餘量。

自有欄位、外鍵跟純關聯名稱這三種形態，排序跟截斷都在資料庫裡做完，`NULL` 剛好平手時排在哪裡由
資料庫決定。一跳關聯＋葉欄位是在記憶體裡把合併後的分組重新排序、重新截斷，`null` 一律排在最
後，不看資料庫引擎。

`Query:MaxFacets`（預設 10）限制一個請求能列出幾條 facet 路徑，`Query:MaxAggregates`（預設
10）限制彙總的 op、欄位組合總數，兩者都設在[第 4 章：設定參考](04-configuration.md)。

### 成本

一般的清單查詢會跑兩條 SQL——一次 `COUNT`、一次分頁 `SELECT`。facet 跟彙總不動這兩條，只是各自
再多跑幾條：

- 自有欄位、外鍵或純關聯名稱：各多跑一條。
- 一跳關聯＋葉欄位：多跑兩條；如果目標集合可軟刪除、葉欄位又可翻譯，要再多跑一條先查存活 id，
  一共三條。
- 純關聯名稱：如果目標集合可軟刪除，也多跑一條篩掉已丟進垃圾桶的目標；一對多不需要，因為它自
  己那條查詢本來就帶著軟刪除的篩選。

彙總依 10 個 op、欄位組合一批，批次大小是寫死的常數，不是 `Query:MaxAggregates`——預設上限剛好
是 10，所以一般請求只多跑一條，把上限調高才會多分幾批。

## 彙總 `aggregate[<op>]`

跟 facet 一樣，彙總只在被要求時才算，不改變 `data` 或分頁。查詢字串上每個 op 各一個鍵，值是逗號
分隔的欄位清單——`aggregate[sum]=price&aggregate[max]=price,rating`；JSON 封裝裡是一個物件，鍵
是 op、值是欄位名稱陣列。五個 op 各自允許的介面跟空結果如下：

| op | 允許的介面 | 空結果 |
|---|---|---|
| `count` | 任意自有欄位（含多對一外鍵） | `0` |
| `sum`／`avg` | `Number`、`Slider`、`Rating` | `null` |
| `min`／`max` | 上面三種再加 `Date`、`DateTime` | `null` |

`sum` 保留欄位自己的數字型別家族——整數會放大成更寬的型別、`decimal` 不變、`double`／`float`
變 `double`；`avg` 不管欄位型別，一律是 `double`。多對一外鍵只能用 `count`：它沒有
`[CmsField]`，驗證器退回 `Uuid` 介面，不落在數字或時間任何一邊，這是刻意的，因為一個外鍵求和或
求平均沒有意義。

彙總永遠不會被剪枝，不管同一個請求裡還要求了哪些 facet——它一律針對請求完整、未剪枝的 `filter`
執行：

```text
$ GET /api/items/article?aggregate[count]=publishedAt&aggregate[max]=publishedAt
{"success":true,"data":[...],"meta":{"total":3,"limit":25,"offset":0,"aggregate":{"count":{"publishedAt":2},"max":{"publishedAt":"2026-06-01T00:00:00"}}}}
HTTP_STATUS:200
```

`data` 同樣換成了 `...`——這裡的重點也是 `meta`。三篇文章裡只有兩篇有 `publishedAt`，`count`
回報 2；`max` 是那兩個時間裡比較晚的一個——三篇裡的草稿沒有 `publishedAt`，不影響這兩個結果。

## 錯誤

facet 跟彙總的驗證錯誤走 `BAD_USER_INPUT`／400，只有讀取授權被擋下的那一項例外——它跟
[第 10 章](10-query-basics.md)的關聯路徑一樣是 `FORBIDDEN`，匿名呼叫端則是 `UNAUTHORIZED`；信封的
完整形狀留給下一章。以下是各自的訊息跟觸發條件。

- 一個不存在的自有欄位，或一條解析不出來的單段路徑：`Unknown field 'bogusField' on collection
  'article'.`
- 超過一個關聯跳：`Facet paths support exactly one relation hop: 'category.parent.name'.`
- 介面不能拿來當 facet：`Field 'regions' on collection 'article' (MultiSelect) cannot be used
  as a facet.`
- 彙總 op 跟欄位介面不相容：`Aggregate 'sum' is not supported on field 'status' (Select).`
- 路徑裡有一段是空的：`Facet path must not be empty.`
- 一跳關聯＋葉欄位裡，關聯那一段解析不出來：`Unknown relation 'bogusRelation' on collection
  'article'.`——跟自有欄位解析不出來是不同的訊息。
- 路徑裡有一段是量詞或 `_junction`：`Facet paths cannot contain quantifiers or '_junction':
  '<path>'.`
- 超過上限：`Too many facets (max 10).`、`Too many aggregate fields (max 10).`
- 關聯那一段的目標集合讀不到：`Read not permitted on '<collection>'.`——這一項是 `FORBIDDEN`，匿名
  呼叫端則是 `UNAUTHORIZED`，不是 `BAD_USER_INPUT`；這個檢查先於路徑形態解析，一個讀不到的關聯底下
  有哪些欄位不會被反推出來。
- 彙總 op 名稱打錯：`Unknown aggregate op 'bogus'.`
- 封裝形式的型別檢查：`'facets' must be an array of strings.`、`'aggregate' must be an
  object.`、`'aggregate.<op>' must be an array of strings.`；查詢字串上多一種
  `Malformed aggregate key '<key>'.`

## 一個完整的例子

把 `filter`、`sort`、`limit`／`offset`、`fields`、`deep`、`facets`、`aggregate` 全部放進同一
個請求，各自的規則照樣成立，不會互相干擾：

```text
$ GET /api/items/article?filter[status][_eq]=published&sort=-publishedAt&limit=1&offset=0&fields=id,status&deep=category,tags&facets=status&aggregate[count]=publishedAt
{"success":true,"data":[{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","category":{"id":"01a08f92-3833-750e-bad3-6ee620f985c3","version":0,"name":"Guides","createdAt":"2026-09-11T08:25:19.670132","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:19.670271","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"},"tags":[{"id":"01a08f92-38f5-7f1b-a478-f8a1140f0b0b","version":0,"name":"howto","createdAt":"2026-09-11T08:25:19.861593","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:19.86169","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","_junction":{"note":null}},{"id":"01a08f92-396e-7bf7-a77c-149c9aa732b1","version":0,"name":"release","createdAt":"2026-09-11T08:25:19.98309","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:19.98318","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","_junction":{"note":"hero"}}],"translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":2,"limit":1,"offset":0,"facets":{"status":[{"value":"published","count":2},{"value":"draft","count":1}]},"aggregate":{"count":{"publishedAt":2}}}}
HTTP_STATUS:200
```

`meta` 一次帶齊這個請求的所有摘要：`total` 是 2，篩選後的總列數，不是這一頁的列數；`limit`／
`offset` 是夾完後生效的分頁值；`facets.status` 照樣回報 `published: 2`、`draft: 1`，沒被
`filter[status]` 自己的條件影響，因為算這個 facet 之前那個條件先被剪掉了；
`aggregate.count.publishedAt` 算的是完整、未剪枝的 `filter`。

`fields=id,status` 沒有擋掉 `category`、`tags` 這兩個展開出來的關聯，也沒有擋掉 `version` 跟
`translations`——這四者都不歸 `fields=` 管；`tags` 底下每一筆多出來的 `_junction`，裝的是連結
本身的備註，[第 8 章：關聯](08-relations.md)已經介紹過這個形狀。[第 10 章](10-query-basics.md)
的 `filter`、`sort`、`limit`、`offset` 跟這裡的 `fields`、`deep`、`facets`、`aggregate` 是同一
個查詢字串上的參數，可以照樣一起帶。

## 接下來

投影、展開、facet 與彙總都講完之後，下一章換個角度，把焦點放回 REST 這一層本身：回應信封長什麼
樣、狀態碼跟錯誤代碼怎麼對應、寫入怎麼做並發檢查，這是
[第 12 章：REST API 慣例](12-rest-conventions.md)的主題。
