# 12. REST API 慣例

每個 REST 端點共用同一套回應信封、狀態碼、錯誤代碼、認證方式與寫入規則，是這一章的主題；下一章逐一列
出端點本身。

## 回應信封

每一個 JSON 回應，不管成功或失敗，都會被包成同一種形狀。成功時是：

```json
{ "success": true, "data": … }
```

失敗時是：

```json
{ "success": false, "error": { "code": "…", "message": "…", "details": … } }
```

`details` 只在錯誤代碼是 `VALIDATION` 時才會出現，其餘代碼一律不帶這個鍵。`meta` 也一樣，只在清單這
種分頁結果上才會出現，單筆讀取、建立與更新都完全省略這個鍵，而不是送一個 `null`。有 `meta` 時，除
了分頁資訊，還可能多兩個鍵：`facets` 與 `aggregate`，一樣是請求要求了才出現，兩者互不影響。

三種來源都會走到同一種信封：一般的控制器回應、框架自己攔下來的例外，以及少數在驗證或授權階段就提早
結束的請求（例如少了 CSRF 標頭）。不管哪一種送出回應，形狀都完全一致，呼叫端不需要分辨來源。

控制器回應如果內容本身已經是這個信封形狀，會被原樣放行；分頁結果會被包成帶 `meta` 的成功信封；
`201` 建立會被重新包成帶信封 body 的 `201`，`Location` 標頭照樣送出；`204` 一律原樣放行，完全沒有
body。

其餘的錯誤回應會依狀態碼配上一句預設訊息——`400` 是 `Bad request.`，`401` 是 `Authentication
required.`，`403` 是 `Forbidden.`，`404` 是 `Resource not found.`，`409` 是 `Conflict.`，其他狀
態碼一律是 `An error occurred.`；控制器如果自己附了訊息文字，那句話會取代預設訊息。

一個 `201` 建立，帶完整回應標頭：

```text
$ POST /api/items/tag
body:
{"name":"launch"}
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:38 GMT
Server: Kestrel
Location: /api/items/tag/01a08f92-8470-718c-888b-1714ffaef71e
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"01a08f92-8470-718c-888b-1714ffaef71e","version":0,"name":"launch","createdAt":"2026-09-11T08:25:39.185162Z","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:39.1853366Z","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"}}
HTTP_STATUS:201
```

範例的 id 都是這批測試資料本身的，讀者環境會不同。留意 `Location` 指向剛建立的那一筆，而
`id`／`version`／`createdAt` 這些欄位都是伺服器自己配的，呼叫端一個都沒送。

未攔到的例外會在伺服器端記錄下來，回給呼叫端的一律是固定的遮罩訊息 `An internal error occurred.`，
絕不洩漏例外本身的內容；這件事，連同認證階段本身失敗的情況，也走同一套遮罩與同一種信封。

GraphQL 是唯一的例外，拼法見[第 14 章：GraphQL API](14-graphql.md)：`/graphql` 的回應是原生的
GraphQL 形狀（`data`／`errors`），完全不套這個信封，除非是像少了 CSRF 標頭這種請求根本到不了
GraphQL 就先被擋下的情況——那種回應仍然是這裡的信封形狀。

## 狀態碼

大部分讀取與寫入回 `200`，body 帶著結果。`201` 只出現在三個端點：建立項目、建立使用者、上傳檔案，三
者都帶一個相對的 `Location`，指向剛建立那一筆的標準讀取路由。`204` 用在六個地方：項目刪除、檔案
刪除、檔案復原、登出、改密碼、撤銷存取權杖，一律沒有 body。

項目從垃圾桶復原是唯一的例外：`POST /api/items/{collection}/{id}/restore` 回 `200`，body 是復原後
的項目，而不是跟其他刪除／復原一樣的 `204`；檔案的復原則確實是 `204`，兩個介面在這裡並不一致。其餘
狀態碼跟下面的錯誤代碼一一對應。

## 錯誤代碼

下面是全部的錯誤代碼，REST 與 GraphQL 共用同一份，同一個字串在兩種協定上代表同一件事：

| 代碼 | HTTP | 何時 |
|---|---|---|
| `UNAUTHORIZED` | 401 | 沒有或無效的憑證；未認證時的權限拒絕 |
| `FORBIDDEN` | 403 | 已認證但沒有權限；缺少 CSRF 標頭 |
| `NOT_FOUND` | 404 | 未知的集合、找不到的檔案，或其他一般 404 |
| `CONFLICT` | 409 | 關聯限制式擋下的刪除、使用者 email 重複，或其他一般 409 |
| `VERSION_CONFLICT` | 409 | 樂觀並行的 `version` 對不上 |
| `BAD_USER_INPUT` | 400 | 查詢參數或寫入內容有誤 |
| `VALIDATION` | 400 | 模型繫結或資料註記驗證失敗 |
| `INTERNAL_SERVER_ERROR` | 500 | 未歸類的例外，訊息一律遮罩 |
| `TOO_MANY_REQUESTS` | 429 | 登入或改密碼觸發流量限制 |
| `PAYLOAD_TOO_LARGE` | 413 | 上傳的實際位元組超過設定上限 |
| `INVALID_CURRENT_PASSWORD` | 400 | 自助改密碼時目前密碼錯誤 |
| `NO_LOCAL_PASSWORD` | 400 | 帳號是外部 OIDC 建的，沒有本地密碼 |
| `ACCOUNT_INACTIVE` | 401 | 密碼正確，但帳號已被停用 |
| `SESSION_REVOCATION_FAILED` | 500 | 密碼變更或刪除使用者後，撤銷工作階段失敗 |
| `SEARCH_UNAVAILABLE` | 503 | 已註冊的搜尋提供者自己回應失敗 |

- `TOO_MANY_REQUESTS` 由三個地方直接寫出，不經過上面這套錯誤對照：登入依帳號的節流、登入依呼叫端
  IP 位址的節流，以及改密碼的節流。
- `SESSION_REVOCATION_FAILED` 代表密碼變更或使用者刪除本身已經成功，只是後續要撤銷該使用者其他
  工作階段時失敗，所以有些工作階段可能還活著；它沒有自己的狀態碼，因此落到 500，但代碼與訊息仍然
  是專屬的。
- `SEARCH_UNAVAILABLE` 的訊息固定是 `Search is temporarily unavailable.`，提供者自己回報的原因
  只會寫進伺服器端的紀錄。

`PAYLOAD_TOO_LARGE` 與 `SEARCH_UNAVAILABLE` 在一般主機上不容易觸發，前者需要真的傳超過上限的位元
組，後者需要一個會主動失敗的搜尋提供者；`INTERNAL_SERVER_ERROR` 只出現在真正未處理的錯誤上。

## 驗證錯誤的 `details`

`details` 只在 `VALIDATION` 代碼上出現，是模型繫結或資料註記驗證失敗時才有的東西，發生在動作本身
執行之前。陣列裡一個項目對應一項驗證失敗，不是一個欄位一項——同一個欄位若同時違反兩項檢查，會產生
兩筆共用同一個 `field` 的項目。ASP.NET Core 沒給訊息時，退回固定的 `Invalid value.`。

```text
$ POST /api/users
body:
{"password":"whatever123"}
{
  "success": false,
  "error": {
    "code": "VALIDATION",
    "message": "One or more validation errors occurred.",
    "details": [
      {
        "field": "Email",
        "message": "The Email field is required."
      }
    ]
  }
}
HTTP_STATUS:400
```

少了 `email` 這個必填欄位，在動作執行之前就被擋下，`details` 裡剛好一筆，指名 `Email`。

應用層自己判斷出來的錯誤是另一回事，一律是 `BAD_USER_INPUT`，沒有 `details` 這個鍵，訊息本身就是
全部資訊：

```text
$ POST /api/users
body:
{"email":"nobody@example.com","password":"short"}
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Password must be at least 8 characters."}}
HTTP_STATUS:400
```

同一個端點，這次 `email` 有給，密碼太短是應用層的規則，所以換成 `BAD_USER_INPUT`，而不是
`VALIDATION`。

一筆更新可以不重送某個必填欄位，維持原值；但一旦明確送了 `null` 或空白，一樣會擋下：合併之後的整
筆項目仍然要通過必填檢查。必填的清單或 map 欄位也是同樣道理——不送就維持原值，送了（就算是空陣列
或空物件）就要通過完整驗證；建立沒有這個顧慮，因為沒有既有列可以合併。

可翻譯欄位的必填檢查是另一條路，跟著翻譯內容逐 locale 檢查，不受這條規則影響。更新時，長度上限等
欄位層級檢查先於這條合併必填檢查跑，兩者都違反時回報先跑到的那一項，跟建立（先檢查必填）可能不同。

一個能指名欄位的錯誤長這樣：`Field 'weight' has an invalid value.`；一個剖析階段就出錯、指不出是
哪個欄位的錯誤，退回泛用的 `Request body could not be parsed.`。頂層 body 如果根本不是一個 JSON
物件，也會被直接擋下，回報 `Request body must be a JSON object.`。

## 認證：cookie 或 bearer

呼叫端可以用兩種憑證：登入端點設下的工作階段 cookie，或是替某個使用者另外申請的 bearer 權杖，帶在
`Authorization: Bearer <token>` 標頭裡。決定用哪一種的規則很簡單：`Authorization` 標頭只要以
`Bearer ` 開頭，就整個當成 bearer 身分處理；沒有這個標頭，才看 cookie。這個判斷只看標頭本身，完全
不會退回去試 cookie。

但這條規則只在不要求登入的端點上算數，也就是讀取、`/graphql`、`GET /api/files/{id}`
與 `/content`：呼叫端在這些端點上同時帶了兩種憑證，也只認 bearer 這一種，權杖失效或格式不對，就直
接降級成匿名，而不是改用 cookie 身分。這是刻意的——寧可讓請求掉成匿名被擋下，也不要靜悄悄換一個身
分通過。

要求登入的寫入端點兩種都收，cookie 或 bearer 皆可，兩者都會被驗證再合併成呼叫端身分；
大部分讀取端點不強制要求任一種憑證，因為讀取只看這個集合的讀取授權。SSO 與密碼規則的完整細節留給
認證那一章。

替目前這個帳號申請一個權杖、用它讀一次、再撤銷它：

```text
$ POST /api/users/01a08f90-893a-7cdb-bb01-a1b46c5ed248/access-token
{"success":true,"data":{"token":"LbUeNnhuvBYeEYt9uFU1izO6eyQDmUAL57hIxchHMvw"}}
HTTP_STATUS:200

$ GET /api/items/article?limit=1  Authorization: Bearer <token>
{"success":true,"data":[{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":2,"limit":1,"offset":0}}
HTTP_STATUS:200

$ DELETE /api/users/01a08f90-893a-7cdb-bb01-a1b46c5ed248/access-token

HTTP_STATUS:204

$ GET /api/items/article?limit=1  Authorization: Bearer <revoked token>
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Read not permitted."}}
HTTP_STATUS:401
```

權杖只在申請當下顯示這一次，之後只存雜湊；用它讀取，`data` 的形狀跟用 cookie 讀取完全一樣。撤銷
之後，同一個權杖立刻失效，不是等到下一次請求才生效。

## CSRF：`X-Struo-CSRF`

用 cookie 這種帶憑證的方式呼叫時，每一個非安全方法（也就是 `GET`／`HEAD`／`OPTIONS`／`TRACE` 以外
的方法）都得帶上 `X-Struo-CSRF` 這個標頭；伺服器只檢查有沒有這個標頭，值本身完全不看，任何字串都
算數。

這樣就足以防禦 CSRF，靠的是瀏覽器本身的規則，不是標頭的值：跨站頁面沒辦法對一次帶憑證的請求加上
自訂標頭，除非目標網站的 CORS 政策已經放行那個來源，而 StruoCMS 的 CORS 預設關閉、只認白名單裡
的來源。fork 若把 CORS 放寬到未知來源，這道防線也會跟著鬆動。

這個檢查只在請求真的靠 cookie 認證時才啟動：用 bearer 身分呼叫、或者請求根本沒帶工作階段
cookie（例如登入本身那一次），都不受這條規則約束，而且 bearer 的豁免會先判斷，就算請求同時帶著
cookie 也一樣豁免。

這道檢查涵蓋每一個非安全方法的路徑，不只是 `/api/*`：`POST /graphql` 也是非安全方法，一個用 cookie
認證的 GraphQL 請求，查詢或 mutation 都一樣要帶這個標頭。少了它，回應是：

```text
$ POST /api/items/article/query (no CSRF header)
body:
{}
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}
HTTP_STATUS:403
```

即使 `POST .../query` 本身只是一次讀取，一樣要帶這個標頭——CSRF 的判斷只看方法，不看這次呼叫是不是
真的會寫入任何東西。

## 樂觀並行：`version`

只有繼承自稽核基底的集合，列上才會有 `version` 這個整數；只實作最基本稽核介面的集合沒有這一欄，
細節見[第 11 章](11-query-advanced.md)。有 `version` 的集合，讀取跟列表回應都會帶著它，更新的
body 也可以把讀到的值原樣送回；伺服器拿這個值去比對目前存好的那一列，一致才會真的寫入，不一致就
整個拒絕：

```text
$ PUT /api/items/article/01a08f92-3a18-7c5d-99a3-5b14cd1279ea
body:
{"status":"published","version":0}
{"success":false,"error":{"code":"VERSION_CONFLICT","message":"The record was modified by someone else since you loaded it. Reload and try again."}}
HTTP_STATUS:409
```

呼叫端手上的 `version` 已經落後於目前這一列真正的值，寫入被拒；這句訊息就是用戶端規劃「重新讀一次
再試」這條路的依據。body 完全不帶 `version` 也可以，這時就退回原本讀到的值，等於放棄這層保護，但
仍然相容舊的呼叫端。還原一筆快照時，`version` 會先被拿掉才套用，避免因快照裡的舊版本號而白白
衝突（見[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)）。`version` 本身也不能透過建立或更新
的 body 直接指定——它只會被當成並行控制的比對值，從不被寫進那一欄。

## `POST`／`PUT` 的 body 能設定什麼

建立與更新只會依照這個集合宣告過的可寫欄位與多對一外鍵來繫結 body，其餘欄位一律被靜靜丟掉，不會回
報錯誤——`id`、`version`、系統管理欄位都是這樣，建立不能自己選 id，更新也不能。唯讀與系統欄位另外
會在繫結後被清成空值，所以 body 就算故意用同樣的鍵名夾帶內容，也進不了那些欄位。

`translations` 與多對多關聯的鍵不在這個繫結範圍內，但仍然生效——伺服器會另外從原始 body 讀出它們，
在同一筆交易裡套用，細節見多對多的寫入形狀。`Json` 型別的欄位是從 body 的原始 JSON 文字直接取值，
可以放進任何 JSON 形狀；沒有翻譯的富文字欄位在驗證之前就先做過消毒，長度上限量的是消毒後的 HTML，
存進去的也是。

更新只會覆蓋 body 裡真的出現過的鍵，不管是自己的欄位還是多對一外鍵，判斷鍵是否出現時不分大小寫；
一個局部的 `PUT` 因此不會不小心清掉伺服器自己在管、呼叫端根本沒送的欄位。以下幾種常見的 body 錯誤
都是實際跑出來的訊息：

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'publishedAt' has an invalid value."}}
HTTP_STATUS:400
```

送 `{"publishedAt":""}` 得到上面這個欄位層級的錯誤；送一個不是物件的頂層 body（例如 `[]`），得
到前面那句 `Request body must be a JSON object.`；送 `{"name":null}` 到一個必填欄位，得到 `Field
'name' is required.`。

`file` 集合要走專屬的上傳端點，對它送 `POST /api/items/file` 一律被拒絕，不管 body 寫什麼：

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Files cannot be created through the generic items API. Upload one with POST /api/files instead."}}
HTTP_STATUS:400
```

上傳細節留給檔案那一章；讀取、更新、刪除 `file` 集合則跟其他集合完全一樣，走這裡的通用規則。

### many-to-many 的寫入形狀

純 id 或帶 payload 的物件、混合陣列的去重規則、物件贏過純 id、junction 自己的寫入授權，
[第 8 章：關聯](08-relations.md)都講過；這一節只看它寫在一次 `PUT` body 上的樣子。

以下是實際的寫入與回應，`tags` 的陣列裡一個純 id、一個帶 payload 的物件：

```text
$ PUT /api/items/article/01a08f92-3a18-7c5d-99a3-5b14cd1279ea
body:
{"tags":["01a08f92-38f5-7f1b-a478-f8a1140f0b0b",{"id":"01a08f92-396e-7bf7-a77c-149c9aa732b1","note":"editor pick"}]}
{"success":true,"data":{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":2,"status":"draft", …}}
HTTP_STATUS:200
```

`data` 的其餘欄位省略，跟本章其他更新回應一樣的形狀。第一個目標只被連結，第二個目標的 `note`
被換成 `editor pick`。

payload 欄位驗證失敗時，訊息會標明是哪個關聯、哪個目標，方便一次改好幾筆連結的表單定位問題：

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Relation 'tags', target '01a08f92-396e-7bf7-a77c-149c9aa732b1': Field 'note' exceeds maximum length 200."}}
HTTP_STATUS:400
```

訊息點名關聯與那一筆目標的 id，前端因此知道錯誤該標在哪一列。物件裡未宣告為這個關聯 payload 的
鍵，還有 junction 自己的外鍵與結構欄位，一律被忽略。

一個物件元素送到沒有宣告 payload 的關聯，或陣列裡出現布林、陣列、`null`、非整數這些不合法的形狀，
一律得到同一句泛用訊息：

```text
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"One or more ids in 'tags' are not valid."}}
HTTP_STATUS:400
```

訊息不分辨是哪一個元素出錯，只說這個關聯的 id 有問題。物件元素如果送到有宣告 payload 的關聯，卻
沒帶 `id` 這個鍵，則是另一句訊息：`Each object in '<relation>' must carry an 'id'.`，這一關一樣
會在任何 payload 值被綁定之前就先擋下。

每一個目標 id 都必須真的存在，不存在會得到 `One or more ids in '<relation>' do not exist in
'<target>'.`；還原是唯一的例外，它連已經被丟進垃圾桶的目標也認，因為快照擷取當下那個目標可能還沒
被丟——[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)已經說過這個道理。

## 接下來

每個端點共用的規則都講完了，下一步是逐一列出每個控制器實際有哪些端點——見
[第 13 章：REST API 端點參考](13-rest-endpoints.md)。

