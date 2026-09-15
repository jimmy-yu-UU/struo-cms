# 16. 認證與 SSO

一支請求證明自己是誰，有四種方式：密碼登入、工作階段 cookie、bearer 權杖，或是透過外部身分提供者
登入。呼叫端該挑 cookie 還是 bearer、CSRF 標頭什麼時候要帶，[第 12 章：REST 慣例
](12-rest-conventions.md) 已經講過，這一章談的是每一種憑證背後的機制——怎麼核發、存在哪裡、什麼
時候過期、怎麼撤銷；通過驗證之後能做什麼，留給[第 17 章：角色與權限](17-roles-and-permissions.md)。

## 第一個管理者

`users` 資料表第一次被建立、而且是空的那一刻，會播種第一個管理者帳號：啟用狀態，名稱固定是
`Administrator`，帳密取自 `Auth:BootstrapAdmin`。這個播種只做一次——之後改設定再重開，對既有
資料庫什麼都不會發生。

播種帳號是密碼原則唯一刻意繞過的地方：它把設定裡的密碼直接雜湊寫入，不經過下一節的長度檢查，設定
檔裡偏短的預設密碼因此依然能拿來第一次登入。手冊裡的示範都跑在剛建好的資料庫上，看到的就是這組
預設密碼——正式環境第一次登入之後應該立刻換掉它。

同一次播種也會建立 `admin`、`public` 兩個角色，並把第一個管理者加進 `admin`，細節留給
[第 17 章：角色與權限](17-roles-and-permissions.md)。

## 密碼

密碼雜湊只有一種實作：Argon2id，參數寫死在程式裡——時間成本 3、記憶體成本 65536（64 MiB）、平行度
1、混合定址、輸出 32 位元組——產生一個自我包含的 PHC 編碼字串，鹽值與參數都內嵌在雜湊本身，`user`
這個集合上並沒有另外一欄鹽值。

登入把送來的密碼跟儲存的雜湊做驗證，成功就直接在同一次請求裡簽發 cookie，沒有另外一道「拿密碼換
權杖」的手續。查無此帳號，或帳號沒有本機密碼（下面 OIDC 一節會提到這種帳號），伺服器仍然會跑一次
完整的 Argon2id 驗證去比對一個假的雜湊值，讓失敗看起來花一樣的時間，不讓呼叫端從回應快慢猜出這個
email 存不存在。

登入只分兩種失敗給呼叫端看：密碼驗證通過但帳號已停用，回 `401` `ACCOUNT_INACTIVE`；其餘情況——
密碼錯、帳號不存在——一律回同一種 `401` `UNAUTHORIZED` `Invalid credentials.`，兩者刻意不可分辨。
成功則回 `200` `{ id }`，沒有其他欄位。

密碼原則是 `Auth:Password` 底下的一組長度上下限，`POST /api/users` 與
`PUT /api/users/{id}/password` 這兩個寫入路徑套用同一套規則；只有下限會透過 `GET /api/config`
公開給前端，鍵名見[第 4 章：設定參考](04-configuration.md)。

改密碼走 `PUT /api/users/{id}/password`：呼叫端要嘛是超級管理員，要嘛是本人並附上
`currentPassword`——本人改密碼一定要驗證目前密碼，超級管理員代改則不用。目前密碼不對回 `400`
`INVALID_CURRENT_PASSWORD`；非管理員想改別人的密碼回 `403` `Admin role required.`；新密碼不合
原則回 `400`，附上欄位訊息。一個只透過 OIDC 登入、從沒設過本機密碼的帳號呼叫這個端點會被擋在最
前面：回 `400` `NO_LOCAL_PASSWORD`，因為根本沒有雜湊可以比對。

這個端點另外有自己的流量限制，依「動手的那個呼叫端」自己的 user id 分桶，不是依目標使用者或來源
IP——超級管理員連續幫多個使用者重設密碼，扣的是自己的額度，不會因為代改別人密碼而把對方鎖住。鍵
名同樣在[第 4 章：設定參考](04-configuration.md)的 `RateLimiting` 一節。

## 登入與工作階段 cookie

沒有帶 `Authorization: Bearer …` 標頭的請求一律走 cookie：名稱 `struo.session`，`HttpOnly`，
`SameSite` 預設 `Lax`；正式環境的安全政策固定是 `Always`，其他環境跟隨請求本身；只要設定了任何一
個 CORS 來源，安全政策與 `SameSite` 都會改成 `Always`／`None`——瀏覽器只在 `Secure` 的前提下接受
`SameSite=None`，代表跨來源部署雙邊都得跑 HTTPS。

cookie 本身只是一把不透明的鑰匙，背後真正的工作階段狀態存在一個分散式快取裡：設定了
`Redis:ConnectionString` 就用 Redis，沒設就退回行程內的快取，實際差別是重開一次行程就會遺失所有
工作階段，而且多個副本之間互不相通；Redis 兩者都撐得住。到期時間是同一個 8 小時滑動視窗，cookie 本
身、快取項目、下面提到的索引列都共用這一個常數，三者不能各自為政。鍵名見
[第 4 章：設定參考](04-configuration.md)的 `Redis` 一節。

每一次帶 cookie 的請求都會重新檢查帳號是否還存在、還是啟用狀態，一旦不是就拒絕這次請求並直接登
出——連帶把這把鑰匙背後的工作階段從快取裡刪掉，不只是擋這一次。快取本身沒有整表掃描的操作，失效
的索引列只會在該使用者下一次登入時被清掉，不是排程整表清理。

預設的驗證機制其實不是 cookie 本身，而是一個轉發用的 scheme：請求帶著
`Authorization: Bearer …` 就轉給 bearer 驗證，否則才轉給 cookie——這也是為什麼連讀取、
`/graphql`、檔案讀取這些沒有標明任何 scheme 的端點，也認得出一個 bearer 呼叫端。這條轉發規則只看
標頭在不在，壞掉或已撤銷的 token 一律降級成匿名，不會退回去試 cookie，即使這次請求同時帶著兩者。

如果呼叫端在還帶著一把有效工作階段 cookie 的情況下再登入一次，不會因此多開一個工作階段：cookie
處理器讀到這次請求本來就帶著一把工作階段鑰匙，就把同一把鑰匙原地續成新登入者的身分，而不是另外
存一份。呼叫端手上原本那把 cookie——不管是瀏覽器裡留著的，還是這次回應重新設定的——從此都以新使
用者的身分通過驗證，舊的工作階段沒有被撤銷，只是被蓋掉了。

這件事有一個容易漏掉的後果：續期只把到期時間往後推，索引裡那一列記的 user id 卻沒有跟著換——下一
節會說明撤銷完全靠這個索引，所以原地續期出來的工作階段，只會被前一個使用者的改密碼或刪除觸發撤
銷，不會被新使用者的觸發撤銷。給讀者的建議很直接：換帳號登入前先登出，不要讓同一個 cookie jar 同
時對應兩個帳號。

## 登出與撤銷

server-side 的工作階段狀態是撤銷能夠立即生效的前提：登出這個動作的全部內容，就是把 cookie
scheme 簽退——從快取裡刪掉那一把鑰匙的工作階段，並清掉瀏覽器端的 cookie。它只撤銷 cookie 這一
段：同一個帳號另外持有的 bearer 權杖完全不受影響，一支只靠 bearer 呼叫的請求打這個端點一樣拿到
`204`，它的 token 照樣活著——權杖要失效，只能靠下一節的撤銷端點，或核發一支新的蓋掉舊雜湊。

`user_sessions` 是一張鑰匙對使用者的索引表，存在的理由很直接：分散式快取本身沒有掃描或列舉的操
作，沒有它就答不出「這個使用者現在有哪些活著的工作階段」。它不是一個能透過項目 API 瀏覽或編輯的
集合，列被刪除時是直接刪掉，不是軟刪除。

密碼寫入成功之後會立刻讀這張索引，撤銷目標使用者當下所有的工作階段——撤銷本身失敗不會讓密碼變更
跟著回滾，而是用自己的錯誤代碼 `SESSION_REVOCATION_FAILED` 回報，因為呼叫端需要知道可能還有
工作階段活著。刪除一個使用者列（軟刪除或清除）一樣會觸發同一套撤銷，而且是在服務層做的，GraphQL
那邊的刪除 mutation 因此一樣蓋得到。

沒有帶任何憑證卻打到需要登入的端點，回應的信封跟一般失敗一樣：`401` `Authentication required.`，
或者帳號通過驗證但權限不足的 `403` `Forbidden.`——這一段在 MVC 動作真正執行之前，由 cookie 處理
器自己寫出來。

## Bearer token

bearer 權杖是替某一個使用者另外核發的長效憑證，只能由超級管理員透過
`POST /api/users/{id}/access-token` 核發，而且只在核發當下的回應裡出現這一次，之後只留得住雜
湊——原始值是 256 位元的隨機亂數，base64url 編碼並去掉補位；存起來的是它大寫十六進位的 SHA-256
摘要。

權杖不會自己過期，一路有效直到被撤銷（`DELETE /api/users/{id}/access-token`），或是被核發一支新
的蓋掉——蓋掉就是覆寫同一欄雜湊，舊的那一支立刻失效。每一次用 bearer 通過驗證的請求都會更新它最
後使用的時間戳，節流到每支權杖最多一分鐘寫一次，避免忙碌的整合把每次呼叫都變成一次寫入；帳號本
身被停用時，不管權杖有沒有效，驗證一樣失敗，而且是每一次請求都重新檢查這個旗標。

bearer 在每一個端點都算數，包括那些沒有標明任何 scheme 的讀取端點，不是只有寫入動作才認得它；一
支 bearer 呼叫端的權限，是它自己角色的授權加上 `public` 這層下限，不是匿名。CSRF 這個標頭也不要
求——沒有瀏覽器會替一支帶著 `Authorization` 標頭的跨站請求偷帶這個憑證，選擇規則與 CSRF 的完整說
明見[第 12 章：REST 慣例](12-rest-conventions.md)。

替 `editor@example.com` 核發一支權杖、拿它讀一次、撤銷、再讀一次：

```text
$ POST /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/access-token
{"success":true,"data":{"token":"MNkMqu0A4btO_EvAfMewVcpVH54SUpy0ww45rnQ--0o"}}
HTTP_STATUS:200

$ GET /api/items/article  (Authorization: Bearer <token>)
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":4,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}},{"id":"01a08f92-402f-7661-a0ba-08694e8391b6","version":0,"status":"published","publishedAt":"2026-06-01T00:00:00","heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.712477","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.712614","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Release notes","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}},{"id":"01a08f92-3a18-7c5d-99a3-5b14cd1279ea","version":7,"status":"published","publishedAt":"2026-03-01T00:00:00","heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:20.153539","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:47.343067","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Getting started","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null},"zh-TW":{"title":"開始使用","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":3,"limit":25,"offset":0}}
HTTP_STATUS:200

$ DELETE /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/access-token

HTTP_STATUS:204

$ GET /api/items/article  (Authorization: Bearer <revoked token>)
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Read not permitted."}}
HTTP_STATUS:401
```

這次示範沒有帶任何工作階段 cookie，所以最後一步看到的只是「失效的 token → 匿名 → 401」：壞掉的
bearer 標頭讓這支請求降級成匿名，而這個環境的 `article` 沒有對匿名開放讀取，授權規則見
[第 17 章：角色與權限](17-roles-and-permissions.md)。上一節提過的另一面——壞掉的 bearer 標頭優先
於任何同時帶著的 cookie，不會退回去試 cookie——是同一條轉發規則的推論，這裡的示範沒有帶 cookie，
並不能證明那一面。

## 登入速率限制

登入每一次匿名嘗試都要跑一次完整的 Argon2id 驗證，不管結果是什麼，放著不管，暴力猜密碼同時也是
一種耗用 CPU 的攻擊。有兩層各自獨立的防線，彼此不能互相取代。

依帳號的節流（`RateLimiting:LoginAccount`）預設就開著，依請求 body 裡的帳號分桶——鍵是 email 的
雜湊，不是明文——在真正驗證密碼之前就先檢查，擋下的請求完全不耗用 Argon2id 運算。這一層是固定視
窗，不是滑動的：視窗一開始算，期間內失敗再多次也不會把結束時間往後推，視窗過了，下一次失敗重新起
算。計數本身不是原子操作，兩個同時發生的失敗有可能只算成一次，能接受的代價是邊界上多讓幾次嘗試，
不是被繞過。不論帳號存不存在、密碼錯還是帳號被停用，都算一次失敗，而且擋下時一律回同一種
`429`，免得節流本身變成猜帳號存不存在的工具；登入成功會清空這個帳號的計數。

依用戶端 IP 的限流（`RateLimiting:Login`）則相反，預設是關的，只保護登入這一個端點，登出、
`me`、OIDC 挑戰都不受它限制。它預設關閉，是因為後台使用者常常共用同一個對外 IP，依 IP 分桶容易
連坐擋下整個辦公室；只有單一副本、直接對外、使用者彼此不共用對外 IP 的部署適合打開它，鍵名與細節
見[第 4 章：設定參考](04-configuration.md)的 `RateLimiting` 一節。

兩層被擋下都回 `429`，信封代碼一律是 `TOO_MANY_REQUESTS`，依帳號節流那一層自己標明
`Retry-After`；下面這段示範對同一個沒用過的 email 連續打了十次錯誤登入，第十一次觸發節流：

```text
$ POST /api/auth/login (-i, the attempt after the first 429)
body:
{"email":"$TW","password":"nope"}
HTTP/1.1 429 Too Many Requests
Content-Type: application/json; charset=utf-8
Date: Tue, 15 Sep 2026 02:25:50 GMT
Server: Kestrel
Retry-After: 897
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":false,"error":{"code":"TOO_MANY_REQUESTS","message":"Too many login attempts. Please try again later."}}
HTTP_STATUS:429
```

（`$TW` 是擷取腳本代入的那個沒用過的 email。）擋下的只有這一個帳號：同一次示範裡，對另一個帳號的
正確登入立刻就能通過，不受影響——分桶鍵是帳號，不是來源 IP。

## OIDC 外部登入

`Oidc:Enabled` 預設是 `false`，或者 `Oidc:Authority` 留空，外部登入這個驗證 scheme 根本不會被
註冊——`GET /api/auth/login/oidc` 因此回一般的 `404`，不是設定錯誤；`GET /api/config` 公開的
`oidcEnabled` 就是登入頁靠它決定要不要顯示外部登入按鈕。啟用時，`Authority`、`ClientId`、
`ClientSecret` 都要非空，啟動時就會驗證失敗，不會拖到第一次登入才發現；`ClientSecret` 在設定檔
裡沒有這一項，預期從環境變數或 user secrets 給。鍵名都在
[第 4 章：設定參考](04-configuration.md)的 `Oidc` 一節。

走的是帶 PKCE 的授權碼流程，提供者自己的 token 不會被存下來，claim 名稱用的是精簡格式，再另外呼
叫 userinfo 端點補齊——回呼路徑是 `Oidc:CallbackPath`，預設 `/signin-oidc`，要拿去登記成提供者
那邊的重新導向網址；要求的 scope 是 `Oidc:Scopes`，預設 `openid`、`email`、`profile`，留空或給
空陣列都會退回這組預設，不會變成完全不要求 scope。claim 會讀 `email`（缺了就退回較長的 email
claim 類型，再退回 `preferred_username`）、`name`（同樣有較長類型的備援）、`iss`、`tid`、
`email_verified`。

驗證通過的外部身分不會被直接拿來登入：它先被解析、比對或就地建立成一個本機使用者，再換成只帶著
那個使用者 id 的本機 cookie 身分——落進工作階段狀態的，是跟密碼登入完全一樣的那一套機制。解析依
序過五道關卡，任何一道沒過，登入就整個失敗：

- tenant 相符（`Oidc:AllowedTenantId`），大小寫不分；預設值是打不中任何 tenant 的佔位字串，替
  換之前會擋掉所有外部登入；
- email 已驗證（`Oidc:RequireEmailVerified`，預設不要求），claim 缺失或無法解析都算沒驗證；
- 身分裡有 email 可用——沒有就直接拒絕，不管其他設定，這是這道流程唯一能拿來比對本機帳號的鍵；
- email 網域在允許清單裡（`Oidc:AllowedEmailDomains`，預設空清單即不限制）；
- 比對到的本機帳號仍是啟用狀態——停用的帳號不會因為外部身分通過驗證就被重新啟用。

比對靠的是 email 相等，大小寫不分；比對不到就地建立一個新帳號，這正是「即時建立」的意思，不需要
事先由管理員手動開帳號。即時建立的帳號是啟用狀態、沒有任何角色、密碼欄位是空的——沒有角色代表除
了 `public` 這層下限之外什麼都沒有：外部登入解決的是身分，不是授權。它同樣沒有本機密碼，呼叫改密
碼的端點會被前面「密碼」一節提到的 `400` `NO_LOCAL_PASSWORD` 擋下。

email 相等本身是刻意接受的風險：沒有另外加上至少一道關卡，任何身分提供者裡 email 對得上的人理論
上都能接管一個既有的密碼帳號。正式環境的建議是明確設定單一 tenant、釘住 tenant id 或設網域允許清
單，並要求 email 已驗證，而不是依賴預設值。外部登入失敗時，回應是 `401` 加上
`{ "error": { "message": "External login failed." } }`（提供者本身回報使用者拒絕時是
`External login denied.`），這個形狀是手寫的，不是每個端點共用的那個標準信封。
`GET /api/auth/login/oidc` 接受 `returnUrl`，會先淨化成站內路徑才拿去挑戰，留空則退回
`Oidc:ReturnUrlDefault`。

## 接下來

憑證怎麼核發、存在哪裡、怎麼撤銷都講完了；通過驗證之後能做什麼，見
[第 17 章：角色與權限](17-roles-and-permissions.md)。
