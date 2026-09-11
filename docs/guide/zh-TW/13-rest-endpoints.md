# 13. REST API 端點參考

這一章把框架十個端點群組的三十二個動作，加上框架另外掛的五個路由，整理成一眼看完方法、路徑與所需權
限的表格；共用的信封、狀態碼、認證與 CSRF 規則留給[第 12 章](12-rest-conventions.md)。

## 怎麼讀這章

下面每個表格的「權限」欄用四種說法之一：

- **匿名** — 不檢查任何身分，也不檢查任何集合的授權。
- **登入** — 只要求用 cookie 或 bearer 通過認證，不檢查特定集合的授權。
- **讀・寫・刪** — 檢查呼叫端在該集合上的對應 RBAC 授權；寫入、刪除與還原動作另外一律要求先通過
  登入，兩層檢查都要過，不是其中一層就夠。
- **超級管理員** — 除了上面那一項，還要求呼叫端是超級管理員，不論委派的集合授權寫了什麼——寫入
  內建的 `AdminOnly` 集合（`permission`、`role`、`user`、`userRole`，加上任何帶 payload 的
  junction）都適用這一條。

路由本身的型別限制在這些檢查之前就先擋下：項目端點的 `{id}` 不限型別，其餘端點的 id 區段與修訂號都
限定型別；型別不符時路由整個不會比對到，回應是空 body 的普通 404，不是這裡說的錯誤信封。集合名稱本
身是不分大小寫比對的。

兩個常被弄反的地方：項目的復原 `POST .../restore` 回 `200` 並帶著復原後的項目，檔案的復原
`POST /api/files/{id}/restore` 回 `204`，兩個介面並不一致；`?purge=true` 是項目端點唯一認得的拼
法，`?purge=1`、`?purge=yes` 都會被當成 false，只丟進垃圾桶——檔案端點的 `purge` 是繫結的布林值，
拼錯直接是 `VALIDATION` 400。

下面出現的 id，都是這批測試資料本身的，讀者環境會不同。

## Items `api/items/{collection}`

下表以 `…` 代表 `api/items/{collection}`。篩選、排序、投影、分頁的完整語意留給
[第 10 章](10-query-basics.md)與[第 11 章](11-query-advanced.md)；版本紀錄與垃圾桶的完整規則留給
[第 9 章](09-revisions-and-trash.md)。

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET …` | 讀 | 列出項目 |
| `POST …/query` | 讀 | 同一種查詢，改用 JSON 本文 |
| `GET …/{id}` | 讀 | 讀取單一項目 |
| `POST …` | 寫 | 建立一筆新項目 |
| `PUT …/{id}` | 寫 | 更新一筆項目 |
| `DELETE …/{id}` | 刪 | 依 `?purge=` 丟進垃圾桶或清除 |
| `POST …/{id}/restore` | 刪 | 從垃圾桶復原，回 200 帶項目 |
| `GET …/{id}/revisions` | 讀 | 列出版本紀錄 |
| `GET …/{id}/revisions/{n}` | 讀 | 讀一筆版本快照 |
| `POST …/{id}/revisions/{n}/revert` | 寫 | 還原到指定版本 |

五個讀取動作都不要求登入，只檢查集合本身的讀取或刪除授權；要求 `deleted=` 非預設模式時，單筆讀取
跟清單一樣需要刪除授權，不只是讀取授權。四個寫入動作與還原都要求先登入（cookie 或 bearer），再疊
上對應的集合授權。`DELETE` 是等冪的：已經在垃圾桶裡的項目仍然回 204，只有未知 id 才是 404；沒有軟
刪除層的集合不論 `?purge=` 給了什麼都直接清除。未知集合一律 404，例如 `GET /api/items/nope` 回
`{"code":"NOT_FOUND","message":"Unknown collection 'nope'."}`。

`GET …/{id}/revisions` 回傳 `{ revisionNumber, operation, createdAt, createdBy,
sourceRevisionNumber }` 陣列，不含快照；沒有版本紀錄的集合回空陣列，不是 404。`GET
…/{id}/revisions/{n}` 多一個 `snapshot` 欄位，是結構化 JSON，隱藏欄位會被遮蔽；不存在的版本號或
沒有版本紀錄的集合回 404。以下用 Blog 樣板的 `article`（有開版本紀錄）示範復原的實際回應：

```text
$ DELETE /api/items/article/01a08f92-4137-72e2-afeb-a0451167539c

HTTP_STATUS:204

$ POST /api/items/article/01a08f92-4137-72e2-afeb-a0451167539c/restore
{"success":true,"data":{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":2,"status":"draft", …}}
HTTP_STATUS:200
```

刪除回 204、沒有 body；復原回 200，`data` 是復原後的項目本身，其餘欄位省略。

## Files `api/files`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `POST api/files` | 寫 | 上傳一個檔案，回 201 |
| `GET api/files/{id}` | 讀 | 讀檔案中繼資料 |
| `GET api/files/{id}/content` | 讀 | 讀內容，可加轉檔參數 |
| `DELETE api/files/{id}` | 刪 | 刪除，預設丟進垃圾桶 |
| `POST api/files/{id}/restore` | 刪 | 從垃圾桶復原，回 204 |

上傳、刪除、復原都要求先登入；兩個讀取動作都不要求，因為已發布的檔案本來就可能屬於公開讀的集合。
檔案的權限檢查是獨立的一條路徑，不經過通用的項目服務，因為檔案的寫入走檔案儲存管線，跟一般集合不
一樣；未發布的檔案對沒有 `file` 寫入授權的呼叫端一律回 404，不是 403。

上傳回 201，`Location` 指向剛建立的檔案，本文是 `{ id, fileName, contentType, size, width,
height, status, folderId }`；multipart 形狀本身不對會被直接擋下——缺一個叫 `file` 的分段回
`Missing 'file' part.`，`folderId` 不是合法 GUID 回 `Invalid 'folderId'.`，都是
`BAD_USER_INPUT`。

`GET .../content` 預設直接串流位元組，設定成 presigned 儲存時改成 302；帶轉檔參數（`width`、
`height`、`format`、`fit`、`quality`）通常回轉檔後的位元組，但轉檔失敗會被接住，退回原始檔案而不
是讓請求整個失敗——完整的轉檔規則留給檔案那一章。刪除與復原都需要 `file` 的刪除授權，兩者都回
204 或 404。

## Users `api/users`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `POST api/users` | 超級管理員 | 建立使用者，回 201 |
| `PUT api/users/{id}/password` | 超級管理員或本人 | 改密碼，本人需驗證目前密碼 |
| `POST api/users/{id}/access-token` | 超級管理員 | 核發存取權杖，只顯示一次 |
| `DELETE api/users/{id}/access-token` | 超級管理員 | 撤銷存取權杖 |
| `GET api/users/{id}/effective-permissions` | 超級管理員 | 預覽有效權限，支援 `?roles=` |

這裡的五個動作都要求先登入（cookie 或 bearer）；其中四個另外要求超級管理員，唯一的例外是改自己
的密碼，那裡只需要證明目前密碼正確，不需要管理員身分。

建立使用者回 `201`，`Location` 指向 `/api/items/user/{id}`——那裡才是使用者列真正的讀取路由；本文
是 `{ id, email, name }`。空白 email、不符密碼規則、重複 email（`409 CONFLICT`）都會被擋下。

改密碼是除了登入之外唯一有流量限制的端點，依呼叫端自己的 user id 分流；成功後會撤銷該使用者其他
的工作階段，撤銷失敗回報 `SESSION_REVOCATION_FAILED`，而不是悄悄吞掉，因為密碼本身已經改成功。核
發的權杖只顯示這一次，之後只存雜湊；撤銷回 204 或 404。

`effective-permissions` 的 `?roles=` 可以帶一串角色 id，預覽一個還沒儲存的角色組合；帶了但是空字
串，預覽的是公開角色的權限下限，跟完全不帶這個參數不一樣；格式錯誤或不存在的角色 id 都會被擋下，
不會悄悄少算。

這裡沒有 `GET /api/users`，使用者的列表、更新、刪除都走通用項目 API 的 `user` 集合——這也是為什
麼建立回應的 `Location` 指向那裡。

## Roles `api/roles`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/roles/{id}/permissions` | 超級管理員 | 讀該角色目前的授權列 |
| `PUT api/roles/{id}/permissions` | 超級管理員 | 整組覆寫該角色的授權 |

兩個動作都要求 cookie 或 bearer，並且都要求超級管理員。`GET` 依集合名稱排序回傳 `{ collection,
canRead, canWrite, canDelete }` 陣列，角色不存在就是 404。`PUT` 是整組覆寫，不是逐筆合併——三個旗
標都是 false 的那一列根本不會被存，回應也只會看到真的還留著的列；本文如果不是 JSON 陣列、有重複集
合、或點名不存在的集合，都會被擋下，集合名稱的大小寫會被統一。角色本身是一般集合，`POST
/api/items/role` 就能建立一筆新角色。

## Languages `api/languages`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/languages` | 登入 | 列出已啟用的語言 |

只要求登入，不檢查特定集合權限；回傳 `{ code, name, isDefault }` 陣列。

## Settings `api/settings`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `PUT api/settings/branding` | 超級管理員 | 更新品牌名稱與 logo |

本文是 `{ brandName, logoFileId? }`，回應是 `{ brandName, brandLogoUrl }`。空白或超過 100 字元的
品牌名稱、找不到或還沒發布的 logo 檔案，都會被擋下。存檔會立刻清掉設定快取，下一次
`GET /api/config` 不必等滿 30 秒的 TTL 就能看到新值。這裡只有這一個 `PUT`，沒有對應的 `GET`——要
讀回來看 `GET /api/config`，見下面 Config 一節。

## Schema `api/schema`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/schema` | 登入 | 列出所有集合的完整結構 |
| `GET api/schema/{collection}` | 登入 | 讀單一集合的結構，未知集合回 404 |

兩個動作都只要求登入，不檢查特定集合權限——任何已認證的呼叫端都能讀到完整的 schema。`Hidden` 欄位
在兩個回應裡都不會出現。

## Auth `api/auth`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `POST api/auth/login` | 匿名 | 登入，設定工作階段 cookie |
| `POST api/auth/logout` | 登入 | 登出，回 204 |
| `GET api/auth/me` | 登入 | 讀目前登入者的身分與權限 |
| `GET api/auth/login/oidc` | 匿名 | 導向設定好的 OIDC 提供者 |

`login` 與 `login/oidc` 允許匿名呼叫，`logout` 與 `me` 都要求 cookie 或 bearer。登入本文是
`{ email, password }`，成功回 `200` 附 `{ id }` 並設定工作階段 cookie，失敗回 `401`。兩道獨立的
防線都會擋下嘗試過於頻繁的呼叫：依帳號節流，預設就開著，在真的驗證密碼之前先檢查；依呼叫端 IP
節流，預設是關的。兩者狀態碼、代碼、訊息都一樣，只有 `Retry-After` 不同，因為兩個時間窗不一樣。

登入接著讀 `me` 的一次實際回應：

```text
$ POST /api/auth/login
body:
{"email":"admin@admin.com","password":"admin"}
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Set-Cookie: struo.session=…; path=/; samesite=lax; httponly
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"01a08f90-893a-7cdb-bb01-a1b46c5ed248"}}
HTTP_STATUS:200

$ GET /api/auth/me
{"success":true,"data":{"id":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","email":"admin@admin.com","name":"Administrator","isSuperAdmin":true,"permissions":{}}}
HTTP_STATUS:200
```

`Set-Cookie` 的值很長，這裡省略；重點是這個標頭本身出現，而且帶著 `path=/`、`samesite=lax`、
`httponly` 三個屬性。`me` 對超級管理員回傳空的 `permissions` 物件，因為每一項授權都已經隱含在超級
管理員身分裡。登出之後立刻讀 `me` 會得到 `401`，不必等 cookie 過期。`login/oidc` 在 OIDC 沒有設定
時回 404；有設定時回 302 導向，`?returnUrl=` 會先被淨化成站內路徑。OIDC 回呼本身不是這裡的動作，
細節留給認證那一章。

## Config `api/config`

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/config` | 匿名 | 讀公開設定，快取 30 秒 |

唯一明確允許匿名呼叫的端點，回傳 `{ oidcEnabled, brandName, brandLogoUrl, passwordMinLength }`，
伺服器端快取 30 秒。已存的 logo 檔案如果後來被取消發布或刪除，這裡仍然退回設定檔裡的 logo，而不是
讓匿名的登入頁看到一個死的網址。

## Ping `api/ping` 與 Health

| 方法與路徑 | 權限 | 用途 |
|---|---|---|
| `GET api/Ping` | 匿名 | 健康度探測，回 `{ status, service, utc }` |

路由樣板是 `api/[controller]`，寫出來是 `api/Ping`，實際比對時不分大小寫，`api/ping` 一樣命中。

以下五個路由不屬於上面任何一個端點群組，是框架啟動時直接掛上去的，不算進上面的三十二個動作裡：

| 路由 | 權限／閘道 | 用途 |
|---|---|---|
| `POST /graphql` | 匿名 | GraphQL 查詢與 mutation 入口 |
| `GET /graphql?sdl` | 依 `GraphQl:ExposeSchema` | 讀目前的 SDL 文字，預設僅 Development |
| `GET /health/live` | 匿名 | 純文字健康探測 |
| `GET /health/ready` | 匿名 | 純文字就緒探測 |
| `GET /openapi/v1.json` 與 `/scalar` | 僅非 Production | OpenAPI 文件與 Scalar 介面 |

兩個健康探測都回純文字（例如 `Healthy`），不是這裡的 JSON 信封。GraphQL 本身的文件見
[第 14 章](14-graphql.md)；`/openapi/v1.json`、`/scalar` 這兩個非 Production 限定路由的閘道規則，
[第 3 章](03-getting-started.md)與[第 4 章](04-configuration.md)已經講過，這裡只列路由。

## 接下來

端點都列完了，下一章換個角度：同一份查詢語意如何在 GraphQL 上表達——見
[第 14 章：GraphQL API](14-graphql.md)。
