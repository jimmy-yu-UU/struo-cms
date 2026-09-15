# 13. REST API 端點參考

這一章一個控制器一個表格，列出每個端點的方法、路徑與所需權限；共用的信封、狀態碼、認證與 CSRF
規則留給[第 12 章：REST API 慣例](12-rest-conventions.md)。

## 怎麼讀這章

下面每個表格的「權限」欄只會出現這幾種說法：

- **匿名** — 不檢查任何身分，也不檢查任何集合的授權。
- **登入** — 只要求用 cookie 或 bearer 通過認證，不檢查特定集合的授權。
- **讀／寫／刪** — 檢查呼叫端在該集合上的對應 RBAC 授權；寫入、刪除與還原動作另外一律要求先通過
  登入。
- **超級管理員** — 要求先登入，再加上呼叫端是超級管理員，不論委派的集合授權寫了什麼。
- **超級管理員或本人** — 呼叫端是超級管理員，或是路徑上那個使用者本人；本人不需要管理員身分，但
  要證明目前的密碼正確。

這些檢查之前，路由自己的型別限制會先擋下不合的請求：項目端點的 `{id}` 不限型別，其餘端點的 id
區段限定為 GUID，修訂號限定為整數；型別不符時路由整個不會比對到，回應是空 body 的普通 404，不是
這裡說的錯誤信封。集合名稱不分大小寫。

項目與檔案對 `purge` 的解讀不一樣：項目端點只認得 `?purge=true`（大小寫不拘），其餘拼法一律當成
false；檔案端點的 `purge` 是繫結的布林值，拼錯直接是 `VALIDATION` 400。

## Items `api/items/{collection}`

篩選、排序、投影、分頁的完整語意留給[第 10 章：查詢：過濾、排序、分頁](10-query-basics.md)與
[第 11 章：查詢：投影、深度展開、facet 與彙總](11-query-advanced.md)。

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/items/{collection}` | 讀 | 列出項目 |
| `POST api/items/{collection}/query` | 讀 | 同一種查詢，改用 JSON body |
| `GET api/items/{collection}/{id}` | 讀 | 讀取單一項目 |
| `POST api/items/{collection}` | 寫 | 建立一筆新項目 |
| `PUT api/items/{collection}/{id}` | 寫 | 更新一筆項目 |
| `DELETE api/items/{collection}/{id}` | 刪 | 依 `?purge=` 丟進垃圾桶或清除 |
| `POST api/items/{collection}/{id}/restore` | 刪 | 從垃圾桶復原，回 200 帶項目 |
| `GET api/items/{collection}/{id}/revisions` | 讀 | 列出版本紀錄 |
| `GET api/items/{collection}/{id}/revisions/{n}` | 讀 | 讀一筆版本快照 |
| `POST api/items/{collection}/{id}/revisions/{n}/revert` | 寫 | 還原到指定版本 |

要求 `deleted=` 非預設模式時，單筆讀取跟清單一樣需要刪除授權，不只是讀取授權；未知集合一律 404。
寫入或刪除 `permission`、`role`、`user`、`userRole` 這四個內建集合，以及任何被 fork 標成
`AdminOnly` 的 junction，還要求呼叫端是超級管理員，委派的集合授權不算數。

版本紀錄與垃圾桶的完整規則（`DELETE` 的等冪行為、`?purge=` 的細節、版本快照的欄位形狀）留給
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)；沒有版本紀錄的集合，讀取版本列表會回空
陣列，不是 404。

以下用 Blog 樣板的 `article`（有開版本紀錄）示範復原回 `200` 而不是 `204`；下面出現的 id 都是這
批測試資料本身的，讀者環境會不同：

```text
$ DELETE /api/items/article/01a08f92-4137-72e2-afeb-a0451167539c

HTTP_STATUS:204

$ POST /api/items/article/01a08f92-4137-72e2-afeb-a0451167539c/restore
{"success":true,"data":{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":2,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"}}
HTTP_STATUS:200
```

刪除回 204、沒有 body；復原回 200，`data` 是復原後的項目本身。

## Files `api/files`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `POST api/files` | 寫 | 上傳一個檔案，回 201 |
| `GET api/files/{id}` | 匿名 | 讀檔案中繼資料 |
| `GET api/files/{id}/content` | 匿名 | 讀內容，可加轉檔參數 |
| `DELETE api/files/{id}` | 刪 | 刪除，預設丟進垃圾桶 |
| `POST api/files/{id}/restore` | 刪 | 從垃圾桶復原，回 204 |

兩個讀取動作都不檢查任何授權：已發布的檔案任何人都讀得到，未發布的檔案則只有已登入且具備 `file`
寫入授權的呼叫端才讀得到，其餘一律回 404，不是 403——即使一個 fork 收回 `file` 的公開讀取授權，已
發布的檔案依然人人可讀。

上傳回 201，`Location` 指向剛建立的檔案，body 是 `{ id, fileName, contentType, size, width,
height, status, folderId }`；multipart 形狀本身不對會被直接擋下——缺一個叫 `file` 的分段回
`Missing 'file' part.`，`folderId` 不是合法 GUID 回 `Invalid 'folderId'.`，都是
`BAD_USER_INPUT`。

`GET api/files/{id}/content` 預設直接串流位元組，設定成 presigned 儲存時改成 302；帶 `width`、
`height`、`format`、`fit`、`quality` 這幾個轉檔參數，通常回轉檔後的位元組，但轉檔失敗會被接住，
退回原始檔案而不是讓請求整個失敗——完整的轉檔規則留給檔案那一章。刪除與復原都需要 `file` 的刪除
授權，兩者都回 204 或 404。

## Users `api/users`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `POST api/users` | 超級管理員 | 建立使用者，回 201 |
| `PUT api/users/{id}/password` | 超級管理員或本人 | 改密碼，本人需驗證目前密碼 |
| `POST api/users/{id}/access-token` | 超級管理員 | 核發存取權杖，只顯示一次 |
| `DELETE api/users/{id}/access-token` | 超級管理員 | 撤銷存取權杖 |
| `GET api/users/{id}/effective-permissions` | 超級管理員 | 預覽有效權限，支援 `?roles=` |

建立使用者回 `201`，`Location` 指向 `api/items/user/{id}`——那裡才是使用者列真正的讀取路由；
body 是 `{ id, email, name }`。空白 email、不符密碼規則、重複 email（`409 CONFLICT`）都會被擋
下。

改密碼是除了登入之外唯一有流量限制的端點，依呼叫端自己的 user id 分流；成功後會撤銷該使用者其他
的工作階段，撤銷失敗回報 `SESSION_REVOCATION_FAILED`，而不是悄悄吞掉，因為密碼本身已經改成功。核
發的權杖只顯示這一次，之後只存雜湊；撤銷回 204 或 404。

`effective-permissions` 的 `?roles=` 可以帶一串角色 id，預覽一個還沒儲存的角色組合；帶了但是空字
串，預覽的是公開角色的權限下限，跟完全不帶這個參數不一樣；格式錯誤或不存在的角色 id 都會被擋下，
不會悄悄少算。

使用者的列表、更新與刪除都走通用項目 API 的 `user` 集合——建立回應的 `Location` 就是指向那裡。

## Roles `api/roles`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/roles/{id}/permissions` | 超級管理員 | 讀該角色目前的授權列 |
| `PUT api/roles/{id}/permissions` | 超級管理員 | 整組覆寫該角色的授權 |

`GET` 依集合名稱排序回傳 `{ collection, canRead, canWrite, canDelete }` 陣列，角色不存在就是
404。`PUT` 是整組覆寫，不是逐筆合併——三個旗標都是 false 的那一列根本不會被存，回應也只會看到真
的還留著的列；body 如果不是 JSON 陣列、有重複集合、或點名不存在的集合，都會被擋下，集合名稱的大
小寫會被統一。角色本身是一般集合，`POST api/items/role` 就能建立一筆新角色。

## Languages `api/languages`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/languages` | 登入 | 列出已啟用的語言 |

回傳 `{ code, name, isDefault }` 陣列。

## Settings `api/settings`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `PUT api/settings/branding` | 超級管理員 | 更新品牌名稱與 logo |

body 是 `{ brandName, logoFileId? }`，回應是 `{ brandName, brandLogoUrl }`。空白或超過 100 字元
的品牌名稱、找不到或還沒發布的 logo 檔案，都會被擋下。存檔會立刻清掉設定快取，下一次
`GET api/config` 不必等滿 30 秒的 TTL 就能看到新值。這裡只有這一個 `PUT`，沒有對應的 `GET`——要
讀回來看 `GET api/config`，見下面 Config 一節。

## Schema `api/schema`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/schema` | 登入 | 列出所有集合的完整結構 |
| `GET api/schema/{collection}` | 登入 | 讀單一集合的結構，未知集合回 404 |

`Hidden` 欄位在兩個回應裡都不會出現。

## Auth `api/auth`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `POST api/auth/login` | 匿名 | 登入，設定工作階段 cookie |
| `POST api/auth/logout` | 登入 | 登出，回 204 |
| `GET api/auth/me` | 登入 | 讀目前登入者的身分與權限 |
| `GET api/auth/login/oidc` | 匿名 | 導向設定好的 OIDC 提供者 |

登入 body 是 `{ email, password }`，成功回 `200` 附 `{ id }` 並設定工作階段 cookie，失敗回
`401`。兩道獨立的防線都會擋下嘗試過於頻繁的呼叫：依帳號節流，預設就開著，在真的驗證密碼之前先
檢查；依呼叫端 IP 節流，預設是關的。兩者狀態碼、代碼、訊息都一樣，只有 `Retry-After` 不同，因為
兩個時間窗不一樣。

登入接著讀 `me` 的一次實際回應：

```text
$ POST /api/auth/login
body:
{"email":"admin@admin.com","password":"admin"}
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Fri, 11 Sep 2026 08:25:49 GMT
Server: Kestrel
Cache-Control: no-cache,no-store
Expires: Thu, 01 Jan 1970 00:00:00 GMT
Pragma: no-cache
Set-Cookie: struo.session=…; path=/; samesite=lax; httponly
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"}}
HTTP_STATUS:200

$ GET /api/auth/me
{"success":true,"data":{"id":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","email":"admin@admin.com","name":"Administrator","isSuperAdmin":true,"permissions":{}}}
HTTP_STATUS:200
```

`Set-Cookie` 的值很長，這裡省略；重點是標頭本身出現，帶著 `path=/`、`samesite=lax`、`httponly`。
`me` 對超級管理員回傳空的 `permissions`，因為每一項授權都已經隱含在這個身分裡。

`login/oidc` 在 OIDC 沒設定時回 404，有設定時回 302 導向，`?returnUrl=` 會先被淨化成站內路徑；
登出之後立刻讀 `me` 就是 `401`，不必等 cookie 過期。OIDC 回呼的細節留給認證那一章。

## Config `api/config`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/config` | 匿名 | 讀公開設定，快取 30 秒 |

這個端點允許匿名呼叫，回傳 `{ oidcEnabled, brandName, brandLogoUrl, passwordMinLength }`，伺服器
端快取 30 秒。已存的 logo 檔案如果後來被取消發布或刪除，這裡仍然退回設定檔裡的 logo，而不是讓匿
名的登入頁看到一個死的網址。

## Ping `api/ping`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/Ping` | 匿名 | 健康度探測，回 `{ status, service, utc }` |

路由樣板是 `api/[controller]`，寫出來是 `api/Ping`，實際比對時不分大小寫，`api/ping` 一樣命中。

## GraphQL、OpenAPI 與健康檢查

框架啟動時另外還有幾個路由，不屬於上面任何一個控制器；下表的「權限／閘道」欄改用兩種閘道說法，
不是上面清單那幾種：

| 路由 | 權限／閘道 | 用途 |
|---|---|---|
| `POST /graphql` | 匿名 | GraphQL 查詢與 mutation 入口 |
| `GET /graphql?sdl` | 依 `GraphQl:ExposeSchema` | 讀目前的 SDL 文字，預設僅 Development |
| `GET /health/live` | 匿名 | 純文字健康探測 |
| `GET /health/ready` | 匿名 | 純文字就緒探測 |
| `GET /openapi/v1.json` 與 `/scalar` | 僅非 Production | OpenAPI 文件與 Scalar 介面 |

兩個健康探測都回純文字（例如 `Healthy`），不是這裡的 JSON 信封。GraphQL 本身的文件見
[第 14 章：GraphQL API](14-graphql.md)；`/openapi/v1.json`、`/scalar` 這兩個非 Production 限定
路由的閘道規則，[第 3 章：快速開始](03-getting-started.md)與
[第 4 章：設定參考](04-configuration.md)已經講過，這裡只列路由。

## 接下來

端點都列完了，下一章換個角度：同一份查詢語意如何在 GraphQL 上表達——見
[第 14 章：GraphQL API](14-graphql.md)。
