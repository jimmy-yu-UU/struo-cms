# 16. 認證與 SSO

呼叫端要證明自己是誰，可以靠密碼登入、工作階段 cookie、bearer 權杖，或是透過外部身分提供者登入。
呼叫端該挑 cookie 還是 bearer、CSRF 標頭什麼時候要帶，見
[第 12 章：REST API 慣例](12-rest-conventions.md)；這一章談的是每一種憑證背後的機制，怎麼核發、
存在哪裡、什麼時候過期、怎麼撤銷。通過驗證之後能做什麼，見
[第 17 章：角色與權限](17-roles-and-permissions.md)。

## 第一個管理者

`struo_users` 資料表剛建好、裡面還是空的時候，會一併建立第一個管理者帳號：啟用狀態，名稱固定是
`Administrator`，帳密取自 `Auth:BootstrapAdmin`，密碼留空就改用隨機產生的。這件事只做一次，之後改設定再重開，對既有
資料庫什麼都不會發生。

範本不附 bootstrap 密碼。`Auth:BootstrapAdmin:Password` 留空時，seed 會隨機產生至少 24 個字元的密碼，並在
建立帳號的那次執行裡以 Warning 印出一次。自己設定的密碼會經過下一節的密碼原則，而且 `admin`（不分
大小寫）會被拒絕；設定的值還是弱值時，`migrate`、`migrate:preview` 與 `Database:MigrateOnStartup` 啟動都會拒絕，錯誤訊息點名
`Auth__BootstrapAdmin__Password`。這個值只用來植入帳號，之後可以移除。
示範裡登入用的是這組密碼的佔位符。第一次登入之後就把它換掉。

同一批種子資料也會建立 `admin`、`public` 兩個角色，並把第一個管理者加進 `admin`，細節留給
[第 17 章：角色與權限](17-roles-and-permissions.md)。

## 密碼

密碼雜湊只有一種實作：Argon2id，參數寫死在程式裡（時間成本 3、記憶體成本 65536 即 64 MiB、
平行度 1、混合定址、輸出 32 位元組），產生一個自我包含的 PHC 編碼字串；鹽值與參數都內嵌在雜湊
本身，`user` 這個集合上並沒有另外一欄鹽值。

登入把送來的密碼跟儲存的雜湊做驗證，成功就直接在同一次請求裡簽發 cookie，沒有另外一道「拿密碼換
權杖」的手續。查無此帳號，或帳號沒有本機密碼（下面 OIDC 一節會提到這種帳號），伺服器仍然會跑一次
完整的 Argon2id 驗證去比對一個假的雜湊值，讓失敗看起來花一樣的時間，不讓呼叫端從回應快慢猜出這個
email 存不存在。

登入只分兩種失敗給呼叫端看：密碼驗證通過但帳號已停用，回 `401` `ACCOUNT_INACTIVE`；其餘情況，
密碼錯、帳號不存在，一律回同一種 `401` `UNAUTHORIZED` `Invalid credentials.`，兩者刻意不可分辨。
成功則回 `200` `{ id }`，沒有其他欄位。

`POST /api/users` 建立使用者，只有超級管理員能呼叫：成功回 `201`，body 是
`{ id, email, name }`，`Location` 指向 `/api/items/user/{id}`，這是通用項目路由，因為沒有專屬
的 `GET /api/users/{id}`；通用的 `POST /api/items/user` 不會建立使用者（`400`），因為只有這個
端點會雜湊密碼並套用密碼原則；email 已經有人用則回 `409` `CONFLICT` `Email already in use.`。密碼要合乎的長度
上下限與鍵名，見[第 4 章：設定參考](04-configuration.md)的 `Auth` 一節。

後台從使用者清單的「新增」按鈕呼叫這個端點，只有超級管理員看得到。對話框要填 email、選填的顯示
名稱，以及輸入兩次的密碼；它不處理角色，建立完成後會開啟新使用者的頁面，角色在那裡指派。

改密碼走 `PUT /api/users/{id}/password`：呼叫端要嘛是超級管理員，要嘛是本人並附上
`currentPassword`，本人改密碼一定要驗證目前密碼，超級管理員代改則不用。目前密碼不對回 `400`
`INVALID_CURRENT_PASSWORD`；非管理員想改別人的密碼回 `403` `Admin role required.`；新密碼不合
原則回 `400`，附上欄位訊息。

一個只透過 OIDC 登入、從沒設過本機密碼的帳號，本人呼叫時會在驗證目前密碼之前被擋下，回 `400`
`NO_LOCAL_PASSWORD`；超級管理員仍然可以替這樣的帳號另外設一個本機密碼。整個身分相關的介面裡，
只有這個端點容許非超級管理員寫入。

後台 SPA 有兩個地方會呼叫這個端點：一般使用者從應用外殼的帳號選單改自己的密碼；超級管理員則在
使用者表單上看到重設動作——而一般角色通常不會被授予讀取 `user` 的權限，使用者表單並不是多數呼
叫端到得了的入口。

這個端點另外有自己的流量限制，依「動手的那個呼叫端」自己的 user id 分桶，不是依目標使用者或來源
IP：超級管理員連續幫多個使用者重設密碼，扣的是自己的額度，不會因為代改別人密碼而把對方鎖住。鍵名
同樣在[第 4 章：設定參考](04-configuration.md)的 `RateLimiting` 一節。

## 登入與工作階段 cookie

沒有帶 `Authorization: Bearer …` 標頭的請求一律走 cookie：名稱 `struo.session`，`HttpOnly`，
`SameSite` 預設 `Lax`；安全政策跟隨
`Auth:Cookie:RequireSecure`，留空時除了 Development 之外的環境都是 `Always`，Development 跟隨請求本身；只要設定了任何一
個 CORS 來源，安全政策與 `SameSite` 都會改成 `Always`／`None`，因為瀏覽器只在 `Secure` 的前提下
接受 `SameSite=None`，代表跨來源部署雙邊都得跑 HTTPS。

cookie 本身只是一把不透明的鑰匙——背後真正的工作階段狀態存在一個分散式快取裡：設定了
`Redis:ConnectionString` 就用 Redis，沒設就退回行程內的快取，實際差別是重開一次行程就會遺失所有
工作階段，而且多個副本之間互不相通；Redis 兩者都撐得住。到期時間是同一個 8 小時滑動視窗，cookie
本身、快取項目、下面提到的索引列都共用這一個常數，三者不能各自為政。鍵名見
[第 4 章：設定參考](04-configuration.md)的 `Redis` 一節。

每一次帶 cookie 的請求都會重新檢查帳號是否還存在、還是啟用狀態，一旦不是就拒絕這次請求並直接登
出，連帶把這把鑰匙背後的工作階段從快取裡刪掉，不只是擋這一次。這一步，以及一般要求登入卻沒帶憑
證時的 `401` `Authentication required.`／`403` `Forbidden.` 拒絕，都是 cookie 處理器在 MVC 動作
真正執行之前就寫出來的，動作本身不需要另外處理。

預設的驗證機制其實不是 cookie 本身，而是一個轉發用的 scheme：請求帶著
`Authorization: Bearer …` 就轉給 bearer 驗證，否則才轉給 cookie，這也是為什麼連讀取、
`/graphql`、檔案讀取這些沒有標明任何 scheme 的端點，也認得出一個 bearer 呼叫端。

在這些沒有標明 scheme 的端點上，壞掉或已撤銷的 token 一律降級成匿名，不會退回去試 cookie，即使
這次請求同時帶著兩者；要求登入的寫入端點則兩種 scheme 都會被驗證再合併，見
[第 12 章：REST API 慣例](12-rest-conventions.md)。

如果呼叫端在還帶著一把有效工作階段 cookie 的情況下再登入一次，不會因此多開一個工作階段：cookie
處理器讀到這次請求本來就帶著一把工作階段鑰匙，就把同一把鑰匙原地續成新登入者的身分，而不是另外
存一份。呼叫端手上原本那把 cookie（不管是瀏覽器裡留著的，還是這次回應重新設定的）從此都以新使
用者的身分通過驗證，舊的工作階段沒有被撤銷，只是被蓋掉了。

這件事續期時也會把索引列的歸屬換過來：那一列記的 user id 會跟著換成新使用者，不是繼續留著舊的。
下一節會說明，使用者層級的撤銷（改密碼、刪除帳號）靠這個索引，所以原地續期出來的工作階段，
會被新使用者的改密碼或刪除觸發撤銷，不會被前一個使用者的觸發撤銷。換帳號之前先登出，別讓同一個
cookie jar 同時對應兩個帳號。

## 登出與撤銷

工作階段狀態存在伺服器端，撤銷才能立即生效：登出這個動作的全部內容，就是把 cookie scheme 簽退，
從快取裡刪掉那一把鑰匙的工作階段，並清掉瀏覽器端的 cookie。它只撤銷 cookie 這一段：同一個帳號另
外持有的 bearer 權杖完全不受影響，一支只靠 bearer 呼叫的請求打這個端點一樣拿到 `204`，它的 token
照樣活著；權杖要失效，只能靠下一節的撤銷端點，或核發一支新的蓋掉舊雜湊。

`struo_user_sessions` 是一張鑰匙對使用者的索引表，存在的理由很直接：分散式快取本身沒有掃描或列舉的操
作，沒有它就答不出「這個使用者現在有哪些活著的工作階段」。它不是一個能透過項目 API 瀏覽或編輯的
集合，列被刪除時是直接刪掉，不是軟刪除；失效的索引列只會在該使用者下一次登入時被清掉，不是排程
整表清理。

密碼寫入成功之後會立刻讀這張索引，撤銷目標使用者當下所有的工作階段；撤銷本身失敗不會讓密碼變更
跟著回滾，而是用自己的錯誤代碼 `SESSION_REVOCATION_FAILED` 回報，因為呼叫端需要知道可能還有工作
階段活著。刪除一個使用者列（軟刪除或清除）一樣會觸發同一套撤銷，而且是在服務層做的，GraphQL 那
邊的刪除 mutation 因此一樣蓋得到。

管理介面的「登出」一律會回到登入頁，即使表單有未儲存的編輯也一樣。伺服器未能確認登出時，登入頁會顯示
提示；`401` 代表已經沒有工作階段可結束，視為成功。已登入時請求被 `401` 拒絕，管理介面會捨棄未儲存的
編輯，前往登入頁並顯示「登入已逾時」的提示，重新登入後回到原本那一頁。

## Bearer token

bearer 權杖是替某一個使用者另外核發的長效憑證，只能由超級管理員透過
`POST /api/users/{id}/access-token` 核發，而且只在核發當下的回應裡出現這一次，之後只留下雜湊。
原始值是 256 位元的亂數，base64url 編碼並去掉補位；存起來的是它大寫十六進位的 SHA-256 摘要。

權杖不會自己過期，一路有效直到被撤銷（`DELETE /api/users/{id}/access-token`），或是被核發一支新
的蓋掉；蓋掉就是覆寫同一欄雜湊，舊的那一支立刻失效。每一次用 bearer 通過驗證的請求都會更新它最
後使用的時間戳，節流到每支權杖最多一分鐘寫一次，避免忙碌的整合把每次呼叫都變成一次寫入；帳號本
身被停用時，不管權杖有沒有效，驗證一樣失敗，而且是每一次請求都重新檢查這個旗標。

每個使用者最多只有一支權杖。改密碼會撤銷該使用者的工作階段，但不會動權杖，所以改密碼並不會切斷靠
權杖驗證的整合；懷疑權杖外洩時，請直接撤銷或核發一支新的。最後使用時間只存在資料庫裡：API 與管理
介面都不會顯示，要找出久未使用的權杖，得直接從使用者資料讀取。

bearer 在每一個端點都算數，包括那些沒有標明任何 scheme 的讀取端點，不是只有寫入動作才認得它；一
支 bearer 呼叫端的權限是它自己角色的授權，不是匿名。bearer 呼叫不需要 CSRF 標頭；選擇規則與
CSRF 的完整說明見[第 12 章：REST API 慣例](12-rest-conventions.md)。

替 `editor@example.com` 核發一支權杖、拿它讀一次、撤銷、再讀一次：

```text
$ POST /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/access-token
{"success":true,"data":{"token":"Lg9y_o7xrCFg2nGIQzeOPPluSndpdNQ-Hr9YtfNW7Pc"}}
HTTP_STATUS:200

$ GET /api/items/article?limit=1  (Authorization: Bearer <token>, no cookie)
{"success":true,"data":[{"id":"01a08f92-4137-72e2-afeb-a0451167539c","version":4,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"createdAt":"2026-09-11T08:25:21.975394Z","createdBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","updatedAt":"2026-09-11T08:25:21.975508Z","updatedBy":"01a08f90-893a-7cdb-bb01-a1b46c5ed248","translations":{"en":{"title":"Draft piece","body":null,"seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null,"seoOgImage":null}}}],"meta":{"total":3,"limit":1,"offset":0}}
HTTP_STATUS:200

$ DELETE /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/access-token

HTTP_STATUS:204

$ GET /api/items/article?limit=1  (Authorization: Bearer <token>, no cookie)
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Read not permitted."}}
HTTP_STATUS:401
```

這次示範沒有帶任何工作階段 cookie，所以最後一步看到的只是「失效的 token → 匿名 → 401」：壞掉的
bearer 標頭讓這支請求降級成匿名，而這個環境的 `article` 沒有對匿名開放讀取，授權規則見
[第 17 章：角色與權限](17-roles-and-permissions.md)。

## 登入速率限制

每一次匿名的登入嘗試，不管結果如何，都要跑一次完整的 Argon2id 驗證；放著不管，暴力猜密碼同時也會
變成一種耗用 CPU 的攻擊。有三層各自獨立的防線，彼此不能互相取代。

依帳號的節流（`RateLimiting:LoginAccount`）預設就開著，依請求 body 裡的帳號分桶（鍵是 email 的
雜湊，不是明文）。這次嘗試在真正驗證密碼之前就先記上，同時發出的猜測不可能一起溜過上限，擋下的
請求也完全不耗用 Argon2id 運算。這一層是固定視窗，不是滑動的：視窗一開始算，期間內嘗試再多次也
不會把結束時間往後推；視窗過了，下一次嘗試重新起算。

不論帳號存不存在、密碼錯還是帳號被停用，都算一次嘗試，而且擋下時一律回同一種 `429`，免得節流本
身變成猜帳號存不存在的工具；登入成功會清空這個帳號的計數。

設了 `Redis:ConnectionString` 時，計數由所有副本共用，Redis 連不上時登入會回 5xx；沒有 Redis 時各
副本各自計數。

每一次嘗試都算數是有代價的：知道某個帳號 email 的人可以把它的額度用光，讓該帳號在視窗結束前無法
用密碼登入。這是刻意接受的取捨；SSO 登入不受影響。

另有一個行程層級的並行上限（`RateLimiting:LoginConcurrency`），限制同一時間有多少個登入在驗證密
碼：4 個同時跑、32 個排隊，其餘回同一種 `429` 並帶 `Retry-After`，不論來源為何，都能保護 CPU 與記
憶體不被大量並行的嘗試拖垮。

依呼叫端 IP 的限流（`RateLimiting:Login`）則相反，預設是關的，只保護登入這一個端點，登出、
`me`、OIDC 挑戰都不受它限制；預設關閉的原因、什麼時候適合打開，見
[第 4 章：設定參考](04-configuration.md)的 `RateLimiting` 一節。

每一層被擋下都回 `429`，信封代碼一律是 `TOO_MANY_REQUESTS`，也都帶 `Retry-After`，各層在呼叫端眼
裡因此無法分辨。下面這段示範對同一個沒用過的 email 連續打了十次錯誤登入，第十一次觸發節流；貼出
來的回應，是節流生效之後再送一次的樣子：

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

示範裡的 `$TW` 是一個從沒被用過的 email。擋下的只有這一個帳號：同一次示範裡，對另一個帳號的正確
登入立刻就能通過，不受影響。分桶鍵是帳號，不是來源 IP。

## OIDC 外部登入

每個外部身分提供者是 `Oidc:Providers:{id}` 底下的一筆。提供者停用時，不會註冊
認證 scheme。`GET /api/auth/login/oidc/{provider}` 對不存在或已停用的提供者回 `404`，所以沒有任何可用的提供者時，
任何 id 都回 `404`。啟用時要給哪些鍵、少了會怎麼樣，見
[第 4 章：設定參考](04-configuration.md)的 `Oidc` 一節。

匿名呼叫 `GET /api/config` 會拿到 `oidcProviders`，是 `{ id, displayName, icon }` 的陣列，順序就是按鈕
順序（`Order` 由小到大，相同時依 id）。登入頁在使用者輸入帳密之前，就依這份陣列為每個提供者畫一顆滿版
按鈕，文字是「使用 {displayName} 登入」。陣列為空就不顯示 SSO 按鈕。回應裡不會有任何密鑰。

走的是帶 PKCE 的授權碼流程，提供者自己的 token 不會被存下來，claim 名稱用的是精簡格式，再另外呼
叫 userinfo 端點補齊。每個提供者有自己的回呼路徑，預設 `/signin-oidc-{id}`，用 `CallbackPath` 可以改；
另外還有兩條由 id 推導的登出路徑：`/signout-callback-oidc-{id}` 與 `/signout-oidc-{id}`。啟用中的提供者
之間不能共用路徑。

登入預設一律顯示提供者的帳號挑選畫面（`Prompt` 為 `select_account`）；其他值與不送 `prompt` 的做法見
第 4 章。

### 按鈕圖示

`Icon` 是代號，不是網址。後台介面在 `frontend/src/lib/ssoIcons.ts` 把它對應到一個內嵌的 SVG 圖示。內建
的代號有 `microsoft` 與 `google`，比對不分大小寫。代號留空或不認得時，顯示通用的鑰匙圖示。

fork 想加自己的品牌圖示，就在該檔的 `ICONS` 表加一筆，鍵用小寫名稱，再把這個名稱設成提供者的
`Icon`，然後重新建置後台。`Icon` 不接受網址；每個圖示都是那張表裡的一個 SVG 元件。

### 設定 Microsoft Entra

用單一 tenant 的 authority，`https://login.microsoftonline.com/<tenant-id>/v2.0`，並把同一個 tenant id
填進 `AllowedTenantId`。在應用程式註冊裡新增一個 Web 類型的重新導向網址：管理介面的來源加上回呼路徑，
也就是 `https://cms.example.com/signin-oidc-microsoft`。如果把 `CallbackPath` 設成 `/signin-oidc`，
就改登記 `https://cms.example.com/signin-oidc`。

### 設定 Google

在 Google Cloud 主控台建立「網頁應用程式」類型的 OAuth 用戶端，並把
`https://cms.example.com/signin-oidc-google` 加進已授權的重新導向 URI。`Authority` 填
`https://accounts.google.com`，`ClientId` 填用戶端 ID，`ClientSecret` 用環境變數或 user-secrets 提供。
Google 沒有 tenant，所以 `AllowedTenantId` 留空。隨附的項目已打開 `RequireEmailVerified`，請保留；
想把登入限制在公司網域，再加上 `AllowedEmailDomains`。

### 其他 OpenID Connect 提供者

新增一筆，id 用符合 `[a-z0-9-]` 的任何名稱，填上提供者的 `Authority`、用戶端 ID 與密鑰，以及
`DisplayName`，再向提供者登記 `https://cms.example.com/signin-oidc-<id>` 當重新導向網址。帳號是靠 email
串起來的，所以提供者若可能簽出 email 未驗證的 token，就要打開 `RequireEmailVerified`，或用
`AllowedEmailDomains` 釘住網域；否則控制著某個相符但未驗證的 email 的人，就能接管既有帳號。

### 重新導向網址

要向提供者登記的重新導向網址，是使用者開啟管理介面的來源加上該提供者的回呼路徑：
`https://cms.example.com/signin-oidc-google`，Compose 堆疊則是 `http://localhost:8080/signin-oidc-google`。
純 `http` 的重新導向網址只在 `localhost` 可用：Entra 會拒絕其他 `http` 重新導向網址，而且 OIDC 的關聯
cookie 帶 `Secure`，所以其他來源都要用 `https`。要用登出回呼的話，同樣登記
`/signout-callback-oidc-{id}`。

在 `pnpm dev` 下開發時，登記 `http://localhost:5173/signin-oidc-{id}`；要用登出回呼的話，再登記
`http://localhost:5173/signout-callback-oidc-{id}`。Vite 開發伺服器會把這些路徑轉發給 API，並保留瀏覽器的
`Host`，API 就依你開啟開發伺服器用的主機組出重新導向網址。請開 `http://localhost:5173`，不要開
`http://127.0.0.1:5173`，才會跟登記的網址一致。

管理映像的 nginx 會把 `/signin-oidc-{id}`、`/signout-callback-oidc-{id}` 與 `/signout-oidc-{id}` 和
`/api/` 一樣代理給 API，所以提供者的重新導向會落回登入當初的那個來源；Vite 開發伺服器轉發的是同一組
路徑。自訂的 `CallbackPath` 只要在 `/api/` 底下，例如 `/api/signin-oidc-acme`，`/api/` 這條規則就涵蓋了，
nginx 不用改。其他自訂路徑要在 `frontend/nginx/default.conf.template` 另加一個 `location` 區塊，或在你
自己的代理加對應規則。

API 用它收到的請求的 scheme 與 host 組出重新導向網址。nginx 會連同埠號原樣帶過用戶端的 `Host`
標頭，所以 `http://localhost:<port>` 直接可用。對外的 `https` 來源則要 API 也看到 `https`：TLS 在
管理容器前面的代理終結時，要讓那個代理把 `X-Forwarded-Proto: https` 設成覆寫而不是附加，因為映像的
nginx 會把收到的值原樣帶給 API（沒收到才用它自己連線的 scheme）。接著在 API 啟用 `ForwardedHeaders`，並把
nginx 容器自己的位址或專用 network 列為已知代理；不要信任整段共用的 Docker network 範圍，也不要發布 API
的連接埠（第 20 章），否則 API 會忽略這個標頭，或被騙去採信偽造的值。

要求的 scope 是該提供者的 `Scopes`，預設 `openid`、`email`、`profile`，留空或給空
陣列都會退回這組預設，不會變成完全不要求 scope。讀取的 claim 有：

- `email`，缺了就退回較長的 email claim 類型，再退回 `preferred_username`；
- `name`，同樣有較長類型的備援；
- `iss`、`tid`、`email_verified`。

驗證通過的外部身分不會被直接拿來登入：它先被解析、比對或就地建立成一個本機使用者，再換成只帶著
那個使用者 id 的本機 cookie 身分，落進工作階段狀態的，是跟密碼登入完全一樣的那一套機制。解析會
依序檢查下面幾件事，任何一項沒過，登入就整個失敗：

- tenant 與該提供者的 `AllowedTenantId` 相符，大小寫不分；留空會略過這項檢查，隨附的 `microsoft`
  項目帶的佔位字串打不中任何 tenant，替換之前會擋掉經由它的所有外部登入；
- email 已驗證（該提供者的 `RequireEmailVerified`，預設不要求），claim 缺失或無法解析都算沒驗證；
- 身分裡有 email 可用，沒有就直接拒絕，不管其他設定，這是這道流程唯一能拿來比對本機帳號的鍵；
- email 網域在允許清單裡（該提供者的 `AllowedEmailDomains`，預設空清單即不限制）；
- 比對到的本機帳號仍是啟用狀態，停用的帳號不會因為外部身分通過驗證就被重新啟用。

比對靠的是 email 相等，大小寫不分；比對不到就地建立一個新帳號，這正是「即時建立」的意思，不需要
事先由管理員手動開帳號。即時建立的帳號是啟用狀態、沒有任何角色、密碼欄位是空的——沒有角色代表
除了 `public` 這層下限之外什麼都沒有：外部登入解決的是身分，不是授權，角色與這層下限見
[第 17 章：角色與權限](17-roles-and-permissions.md)。它同樣沒有本機密碼，改密碼的端點一樣回
`400` `NO_LOCAL_PASSWORD`。

email 相等本身是刻意接受的風險：沒有另外加上至少一道關卡，任何身分提供者裡 email 對得上的人理論
上都能接管一個既有的密碼帳號；正式環境該怎麼設定這幾道關卡，見
[第 4 章：設定參考](04-configuration.md)的 `Oidc` 一節。

有一種組合會在啟動時被拒絕：某個提供者已啟用，而且它的 `Authority` 的路徑含有 `common`、`organizations`、
`consumers` 或 consumers tenant 的 GUID 其中一段（Entra 的多租用戶端點，任何人都能自建 tenant 並送出任意 email），此時必須設定
該提供者的 `AllowedTenantId` 或 `AllowedEmailDomains`，否則主機無法啟動，錯誤訊息會點名這兩個鍵。
空白的值不算已設定。單一 tenant 的 authority，或提供者停用時，都不受影響。

外部登入失敗時，瀏覽器會被導向管理介面的登入頁 `/login?error=<code>`，登入頁顯示在地化的訊息，並把
這個參數從網址列移除。代碼是固定的一組，絕不帶例外訊息：

| 代碼 | 原因 |
|---|---|
| `external_login_failed` | 提供者或回呼失敗 |
| `external_login_denied` | 使用者在提供者那邊拒絕 |
| `external_login_NoEmail` | 身分沒有可用的 email |
| `external_login_EmailNotVerified` | email 未驗證，且該提供者設了 `RequireEmailVerified` |
| `external_login_DomainNotAllowed` | email 網域不在該提供者的 `AllowedEmailDomains` |
| `external_login_TenantNotAllowed` | tenant 不是該提供者的 `AllowedTenantId` |
| `external_login_Inactive` | 對應到的本機帳號已停用 |

其他任何值，登入頁一律顯示通用訊息。`GET /api/auth/login/oidc/{provider}` 接受 `returnUrl`，會先淨化成站內
路徑才拿去挑戰，留空則退回 `Oidc:ReturnUrlDefault`。

## 接下來

認證到這裡為止；通過驗證之後能做什麼，見
[第 17 章：角色與權限](17-roles-and-permissions.md)。
