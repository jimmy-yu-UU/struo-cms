# 12. 認證、SSO 與 RBAC

每一個請求，要嘛帶著 session cookie，要嘛帶著 bearer access token，要嘛兩者都沒有——第 9 章已經記載了
這兩種機制在傳輸層級的運作細節、`X-Struo-CSRF` 標頭，以及 `AuthController`/`UsersController`/
`RolesController` 的端點表。本章涵蓋的是這些傳輸層合約背後的子系統：密碼如何被雜湊與驗證、session 如何
被儲存與撤銷、外部身分提供者如何被接入並以 JIT 方式即時佈建本機帳號，以及 RBAC 資料模型如何決定一個已
通過驗證(或匿名)的呼叫端，在請求通過驗證之後實際能做什麼。

## 密碼驗證 (Argon2id)

`Argon2idPasswordHasher`(`src/Struo.Infrastructure/Identity/Argon2idPasswordHasher.cs`)是唯一出貨的
`IPasswordHasher`。它會透過 `Isopoh.Cryptography.Argon2`，以固定參數 `timeCost: 3`、
`memoryCost: 65536`(64 MiB)、`parallelism: 1`、`type: Argon2Type.HybridAddressing`、
`hashLength: 32` 產生一個自我包含的 PHC 編碼字串——salt 與參數都內嵌在雜湊值本身之中，因此 `User` 上並
不存在獨立的 salt 欄位。已針對執行中的資料庫進行即時驗證(而非只從原始碼讀取)：

```
$ docker exec struo-postgres psql -U struo -d struo -c \
    "select email, left(password,30) as pw_prefix from users limit 2;"
       email        |           pw_prefix
--------------------+--------------------------------
 editor@example.com | $argon2id$v=19$m=65536,t=3,p=1
 admin@admin.com    | $argon2id$v=19$m=65536,t=3,p=1
```

`AuthController.Login`(`src/Struo.Api/Controllers/AuthController.cs`)會呼叫
`IAuthService.AuthenticateAsync`，它透過 `Argon2idPasswordHasher.Verify` 將送交的密碼與所儲存的雜湊值
進行比對；驗證成功後，會直接以 `Cookie` 機制簽入一個 `ClaimsPrincipal`——密碼驗證與 cookie 核發發生在
同一個請求之中，並不存在獨立的「以密碼交換 token」這道步驟。

不過有兩種失敗結果被刻意合併成同一個代碼：密碼錯誤，以及對一個根本不存在的電子郵件嘗試登入，兩者都會
解析成 `UNAUTHORIZED`(第 9 章)，因為把它們區分開來會打開一個帳號列舉的攻擊面。
`AuthService.AuthenticateAsync` 會*先*驗證密碼雜湊、*之後*才檢查 `IsActive`，所以一個已停用的帳號，
只有在呼叫端已經證明自己輸入了正確密碼之後，才會抵達它自己專屬的代碼(`ACCOUNT_INACTIVE`)——揭露這個
代碼，不會洩漏呼叫端尚未證明過的任何資訊。

## Session cookie 與分散式 ticket 存放

**cookie** 機制(`AuthSchemes.Cookie`，常數為 `"Cookies"`)是預設的 `AuthSchemes.Adaptive` policy
scheme 在每一個未帶 `Authorization: Bearer` 標頭的請求上自動轉發的對象——無論是否標示 `[Authorize]`：
`AuthWiring.AddStruoAuth`(`src/Struo.Api/Auth/AuthWiring.cs`)真正註冊為預設驗證機制
的是 `Adaptive` 本身，而非直接是 `Cookie`(`services.AddAuthentication(AuthSchemes.Adaptive)`)；轉發
規則與帶 bearer 標頭的情形，見下方 Bearer token 一節。它的 cookie 名稱為 `struo.session`
(`AuthSchemes.SessionCookieName`)，具備 `HttpOnly`、`SameSite=Lax`、8 小時滑動到期時間。
`SecurePolicy` 在 `Production` 下為 `Always`，其他情況則為 `SameAsRequest`(本機開發/測試用的 host
(即執行本章範例的 `Struo.Api` 執行個體)是以純 HTTP 執行的，`Always` 會讓 cookie 被悄悄地不再送回)。
若 CORS 設定了任何允許的來源(`CorsWiring.HasConfiguredOrigins`)，cookie 選項會改為重新設定成
`SameSite=None` + `SecurePolicy=Always`——跨來源 cookie 需要 `SameSite=None`，而瀏覽器只有在同時具備
`Secure` 時才會認可它。

Ticket 存放——也就是 cookie 那組不透明金鑰背後真正的 session 狀態——是 `DistributedCacheTicketStore`
(`src/Struo.Api/Auth/DistributedCacheTicketStore.cs`)，一個以 `IDistributedCache` 為基礎的
`ITicketStore`：**當設定了 `Redis:ConnectionString` 時使用 StackExchange.Redis**，**否則使用記憶體內
的分散式快取**(`AuthWiring.AddStruoAuth` 中依 `Redis:ConnectionString` 決定的分支，
`AuthWiring.cs`)。兩個分支都採用相同的 8 小時滑動到期時間。實務上的差異(第 3
章已明白說明)在於：記憶體內備援方案會在行程重新啟動時遺失每一個 session——對於快速的本機執行來說沒問題，
但不適合任何存活較久或多執行個體的情境——而 Redis 則會在重新啟動後保留 session，並在各複本之間共用。使用
伺服器端的 ticket 存放，而非把 claim 直接編碼進 cookie 本身，正是讓「立即撤銷」得以實現的原因：
`AuthController.Logout`(`SignOutAsync`)會把該 ticket 從存放區中移除，因此一個已登出的 cookie 會立即
失效，而不是只依照自己的排程逐漸過期。

## Bearer token

**bearer** 機制(`AuthSchemes.Bearer`，常數為 `"Bearer"`)由 `BearerTokenAuthenticationHandler`
(`src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs`)針對一個雜湊化的 token 存放區進行驗證——
一個 token 只會透過 `POST /api/users/{id}/access-token`(僅限 super-admin，第 9 章)鑄造一次，僅在那
一次回應中顯示(`AccessTokenHasher.Generate`；只有雜湊值會被持久化)，而且不會自行過期(沒有 TTL——
一個 token 會一直有效，直到透過 `DELETE /api/users/{id}/access-token` 明確撤銷，或藉由產生一個新的
token 來輪替，這會覆寫既有的雜湊值)。每一個以 bearer 驗證的請求都會更新 `AccessTokenLastUsedAt`，並
節流為每個 token 每分鐘最多一次，這樣一個繁忙的整合端就不會把每一次呼叫都變成一次寫入。

**Bearer 在每一個端點上都有效，包含那些沒有指名任何機制的端點。**
`ItemsController`/`FilesController`/`UsersController`/`RolesController` 等控制器的寫入 action，都明確
標示了兩種機制 (`[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]`)。而那些完全不帶
`[Authorize]` attribute 的 action——`ItemsController` 的讀取，以及 `/graphql` (第 10 章)——則由預設驗證
機制 `AuthSchemes.Adaptive` (`src/Struo.Api/Auth/AuthWiring.cs`) 涵蓋:只要請求帶有
`Authorization: Bearer …`，它就會轉發給 `Bearer`。因此一個純 bearer 的呼叫端在讀取這些端點時，會被解析
為**它自己**，連同它自己角色的授權與下方的 `public` 底線聯集，與一個 cookie session 完全相同——而不是
被當成匿名者。已即時驗證：一個 bearer 請求不需要 `X-Struo-CSRF` 標頭就能成功(見下方)，而對同一個端點
發出的 cookie 請求若沒有這個標頭則會被拒絕：

```
$ curl -s -i -X PUT http://localhost:5221/api/items/file/5b4de227-0997-4bb1-b1e7-9565b3cffce6 \
    -H "Authorization: Bearer <token>" \
    -H "Content-Type: application/json" -d '{"status":"published"}'
HTTP/1.1 200 OK
{"success":true,"data":{"id":"5b4de227-0997-4bb1-b1e7-9565b3cffce6","version":5, ...}}
```
(沒有 `X-Struo-CSRF` 標頭、沒有 session cookie，但請求仍然成功——原因見下方。)

## CSRF 標頭規則

`CsrfProtectionMiddleware`(`src/Struo.Api/Auth/CsrfProtectionMiddleware.cs`)要求每一個非安全 HTTP
方法都必須帶有 `X-Struo-CSRF` 標頭(**只檢查是否存在**)——但**僅限於請求是搭乘 session cookie 而來
時**。以 `Bearer` 驗證的請求可豁免(因為不存在瀏覽器周邊憑證可供跨站頁面搭便車)，完全不帶 session
cookie 的請求同樣豁免。這正是為什麼上面那個 bearer `PUT` 不需要 CSRF 標頭，而對等的 cookie 驗證呼叫
若沒有這個標頭就會回傳 403——第 8 章與第 9 章都已即時展示過這個確切的拒絕情形；本章只是就上述兩種驗證
機制重申這條規則，而不重述機制本身(完整的 OWASP 理由與 middleware 的 `RequiresCsrfHeader` 邏輯，請見
其中任一章)。

## 登入速率限制

`POST /api/auth/login` 由一個應用程式內建的固定視窗限制器守護，依用戶端 IP 分區——
`RateLimiting:Login`(`LoginRateLimitOptions`，
`src/Struo.Application/Configuration/LoginRateLimitOptions.cs`)：`Enabled`(預設 `true`)、
`PermitLimit`(預設 `5`)、`WindowSeconds`(預設 `60`)。每一次匿名登入嘗試都會耗用完整的 Argon2id
CPU 運算，無論結果為何，因此一次不受限的暴力破解嘗試同時也是一個 CPU 耗盡型的 DoS 攻擊媒介——這個限制器
的存在正是為了界限這個風險，且只套用在 `AuthController` 自身端點之中的登入這個 action 上(登出／me／
OIDC challenge 則刻意不受限制)。不過它並不是這個應用程式裡唯一的速率限制器：另一個獨立設定的限制器
改守護 `PUT /api/users/{id}/password`，依呼叫端自己已驗證的使用者 id 分區，而不是依 client IP——它的
設定見第 3 章，兩個限制器共用的 `429` 回應形狀見第 9 章。第 9 章也展示了登入限制器自身視窗耗盡後產生
的即時 `429` 回應(`Retry-After: 60`，錯誤代碼 `TOO_MANY_REQUESTS`)；本章不會再次觸發它。

**何時停用它：** 只有在多複本部署(例如 Kubernetes)中，且改由 ingress/edge/WAF 這一層強制執行逐 IP
速率限制時，才將 `Enabled` 設為 `false`——那一層看得到真正的用戶端 IP，且位於每個 pod 之前，而這個限制
器的狀態是位於記憶體內、逐 pod 各自獨立的，因此無法在各複本之間強制一個真正的全域上限(第 3 章)。若在
一個自身不做速率限制的負載平衡器背後，仍保留這個限制器為啟用狀態，會導致每個 pod 各自低估攻擊次數，卻
無法真正保護整體部署——這個旗標的存在，就是為了讓維運者能夠有意識地做出這個取捨，而不是讓預設值在任一種
拓樸下都悄悄做錯事。

## OIDC／外部登入

`Oidc:Enabled`(預設 `false`)掌控整個外部登入機制的註冊——`OidcWiring.AddStruoOidc`
(`src/Struo.Api/Auth/OidcWiring.cs`)在呼叫 `AddOpenIdConnect` 之前，只要 `Enabled` 為
`false` 或 `Authority` 為空，就會提早回傳——停用時，完全不會加入任何 OIDC `AuthenticationScheme`，
`GET /api/auth/login/oidc` 會直接回傳普通的 `404`，而不會嘗試發起 challenge(已即時驗證，此 host
依預設停用了 OIDC)：

```
$ curl -s -i http://localhost:5221/api/auth/login/oidc
HTTP/1.1 404 Not Found
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}

$ curl -s http://localhost:5221/api/config
{"success":true,"data":{"oidcEnabled":false,"brandName":"StruoCMS Docs Demo","brandLogoUrl":null,"passwordMinLength":8}}
```

啟用時，`Oidc:Authority`/`ClientId`/`ClientSecret` 在啟動時全部為必填(`ValidateOnStart`)；handler
使用 authorization-code + PKCE，保留簡短的 JWT claim 名稱(`MapInboundClaims = false`，因此
`OidcClaimsMapper` 直接讀取 `email`/`name`/`iss`/`tid`/`email_verified`，而不是它們對應的冗長 ASP.NET
Core claim-type 名稱)，並從 userinfo 端點取得額外的 claim。在 `OidcWiring.AddStruoOidc` 裡的 `OnTokenValidated` handler
(`OidcWiring.cs`)中，這個外部 principal 會被對應成一個 `ExternalIdentity`，並交給
`IExternalLoginService.ResolveOrProvisionAsync`(`ExternalLoginService`，
`src/Struo.Application/Security/ExternalLoginService.cs`)——**並非**直接簽入：OIDC principal 會被丟棄，
換成一個本機的 `Cookie` 機制身分，只攜帶已解析出的使用者 id，而這才是真正被寫入上方 Redis 支援之 ticket
存放區的內容。因此一個正式環境部署最終會擁有與密碼登入完全相同的 session 機制，無論使用者實際上是透過
何種方式驗證的。

**以電子郵件為基礎的 JIT 佈建：** 解析程序以外部身分的電子郵件為錨點，在儲存層**以不分大小寫**的方式與
現有本機使用者比對。若沒有任何本機使用者相符，就會當場建立一個(`store.CreateExternalUserAsync`)——
這正是「JIT」(just-in-time，即時)在此處的意義：第一次以外部身分登入時，不需要另一個由管理員主導的
佈建步驟。有三道防護，各自獨立地在電子郵件比對執行之前被檢查，位於
`ExternalLoginService.ResolveOrProvisionAsync` 內部；但只有其中兩道**預設為寬鬆**——租戶鎖定出貨時
是**失敗封閉 (fail closed)**的：

| 防護 | 設定鍵 | 預設值 | 設定後的效果 |
|---|---|---|---|
| 租戶鎖定 | `Oidc:AllowedTenantId` | 出貨時為不會匹配任何東西的預留值 `REPLACE_TENANT_ID`(`appsettings.json`)——失敗封閉，拒絕每一個真實 tenant 直到被換掉為止 | 除非 token 的 `tid` claim 完全相符，否則拒絕(`TenantNotAllowed`)。 |
| 電子郵件已驗證 | `Oidc:RequireEmailVerified` | `false` | 除非 token 的 `email_verified` claim 為 `true`，否則拒絕(`EmailNotVerified`)。 |
| 網域允許清單 | `Oidc:AllowedEmailDomains` | `[]`(不受限) | 除非電子郵件的網域在清單之中，否則拒絕(`DomainNotAllowed`)。 |

原始碼本身明確記載這是一項**可接受的風險**，而非疏漏
(`OidcOptions.RequireEmailVerified`/`AllowedTenantId`/`AllowedEmailDomains`，
`src/Struo.Application/Security/OidcOptions.cs`)：因為連結是以電子郵件相等性為依據，一個在未
鎖定上述任一道防護的情況下啟用 OIDC 的部署，可能會讓任何身分提供者中出現相符電子郵件的身分接管一個密碼
帳號。正式環境的 OIDC 部署預期會明確加以限制——單一租戶的 `Authority`，加上 `AllowedTenantId` 和／或
`AllowedEmailDomains`，並將 `RequireEmailVerified` 設為 `true`——而不是仰賴那組讓本機開發保持零摩擦
的零設定預設值。

**`public` 對每一個呼叫端而言都是一道底線：**`SqlSugarRolePermissionStore.LoadForUserAsync`
(`src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs`)會把 `public` 角色本身的
授權，聯集進**每一個**呼叫端的有效權限之中——匿名者、無角色者，以及持有角色者皆然——而不僅僅是在一個
使用者的角色集合回傳為空時才當作備援。一個呼叫端自己的角色只能*增加* `public` 本已授予的東西，永遠
不能減少：這個模型沒有拒絕語意——`PermissionResolver.Resolve` 只會用 `OR` 把每一個角色的
讀取/寫入/刪除授權摺疊在一起，沒有其他運算——所以即使在這次聯集出現之前，一個角色也永遠不可能有意義地
縮小這道底線。這一點之所以重要，是因為在這次聯集出現之前，一個已登入使用者的授權*只*來自他們自己被指派
的角色：因此一個持有角色的使用者，只要 `public` 持有一項他們的角色恰好沒有重複的授權，就可能讀到*比*
一個匿名訪客*更少*的內容——已經登入，反而更糟。把 `public` 聯集進每一個結果之中，修正了這個不對稱：
一個剛以 JIT 方式佈建的使用者、一個完全沒有 `UserRole` 資料列的使用者，以及一個持有完整角色集合的
使用者，全部都至少能看到 `public` 所授予的內容，絕不會更少。

## 使用者、角色、權限：資料模型

四個 framework 集合構成了 RBAC，全部都是 `[CmsCollection(..., AdminOnly = true)]`
(`src/Struo.Infrastructure/Identity/*.cs`)：

| 集合 | 資料表 | 主要欄位 | 備註 |
|---|---|---|---|
| `user` | `users` | `email`(唯一)、`password`(Argon2id 雜湊，`Hidden`+`ReadOnly`)、`name`、`isActive`、`accessToken`(bearer token 的 SHA-256，`Hidden`+`ReadOnly`) | `Roles` 是一個透過 `userRole` junction 與 `role` 建立的多對多 `TagSelect`——在 User 表單上以標籤選取角色名稱的方式編輯，而不是手動建立 junction 資料列。 |
| `role` | `roles` | `name`(唯一)、`isSuperAdmin`、`description` | `isSuperAdmin = true` 會讓每一項權限檢查直接短路為全部允許(見下方的 `EffectivePermissions`)。 |
| `permission` | `permissions` | `roleId`、`collection`、`canRead`、`canWrite`、`canDelete` | 在 `(roleId, collection)` 上唯一；`Hidden`(沒有專屬的管理後台畫面——只能透過下方的角色權限矩陣編輯)。 |
| `userRole` | `user_roles` | `userId`、`roleId` | 在 `(userId, roleId)` 上唯一；`Hidden`，純粹的 junction。 |

`permission` 與 `userRole` 除了 `Hidden` 之外還都帶有 `AdminOnly`——一個集合可以只是 `Hidden`(側邊欄
沒有項目)而不是 `AdminOnly`，反之亦然；這裡兩者皆是，因為這兩個集合純粹是 RBAC 機制的內部構件，若讓任
何持有一般逐集合寫入授權的人都能碰觸它們，正是下方 `AdminOnly` 所要防止的那種自我提權。

種子資料只在第一次啟動時建立，且具備冪等性(`RbacSeeder.SeedAsync`，
`src/Struo.Infrastructure/Identity/RbacSeeder.cs`)，只在 `roles` 資料表被建立時才會被呼叫：它會建立
`admin`(`isSuperAdmin = true`)與 `public` 兩個角色，將啟動用管理員(`Auth:BootstrapAdmin:Email`)
指派給 `admin`，並針對 `Rbac:PublicReadCollections` 中的每一項授予 `public` 讀取權——這正是第 3 章
已詳細記載的同一個「只在第一次啟動時生效」但書(編輯這個設定鍵並重新啟動，並**不會**對既有資料庫回溯授
予任何東西——授權只能直接針對一個現存的資料庫進行，這正是下一節要展示的內容)。

## 逐集合讀取／寫入／刪除授權

`EffectivePermissions`(`src/Struo.Application/Security/EffectivePermissions.cs`)是每個請求解析後
的快照：`IsSuperAdmin` 會讓每一項 `CanRead`/`CanWrite`/`CanDelete` 檢查無條件短路為 `true`；否則每一
項檢查都會在一個由 `PermissionResolver.Resolve` 從呼叫端持有的每一個角色摺疊而成的讀取／寫入／刪除三
元組中查找該集合——只要**任一個**持有的角色給予授權就足夠(跨角色之間是 `OR`，不是 `AND`)，計算一次
後便快取在該請求範圍內的 `ICurrentPermissions` 上(`PermissionResolutionMiddleware`)。若沒有該集合的
項目，則三者一律直接拒絕——這是安全的預設值。

角色權限矩陣(`PUT /api/roles/{id}/permissions`，第 9 章)會在單一交易中**完整替換**一個角色的整組授
權(先全部刪除再全部插入)；一整列皆為 `false` 的資料會被視為不存在，而非被儲存下來。已即時驗證：先授予
`public` 對 `file` 的讀取權，再撤回，展示這項授權會立即針對執行中的資料庫生效，不需要重新啟動：

```
$ curl -s -X PUT http://localhost:5221/api/roles/<public-role-id>/permissions \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '[{"collection":"file","canRead":true,"canWrite":false,"canDelete":false}]'
{"success":true,"data":[{"collection":"file","canRead":true,"canWrite":false,"canDelete":false}]}

$ curl -s -i "http://localhost:5221/api/items/file?sort=fileName&limit=1"
HTTP/1.1 200 OK
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}],"meta":{"total":4,"limit":1,"offset":0}}

$ curl -s -X PUT http://localhost:5221/api/roles/<public-role-id>/permissions \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[]'
{"success":true,"data":[]}

$ curl -s -i "http://localhost:5221/api/items/file?limit=1"
HTTP/1.1 401 Unauthorized
```

(授予之前的匿名請求，以及撤回之後再一次的匿名請求，都正確地回傳了 `401 UNAUTHORIZED`——「需要驗證」
——因為 `file` 在這個 host 上並沒有其他任何公開授權。)

## `AdminOnly` 集合與 super-admin

`CmsCollectionAttribute.AdminOnly`
(`src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs`)將一個集合的**寫入**動作
(透過一般 CRUD 路徑進行的建立／更新／刪除／還原／回復)標示為無論任何委派的逐集合授權為何，一律需要
super-admin——上述四個身分／授權集合是唯一設定它的集合。`ItemService.RequireSuperAdminForAdminOnly`
(`src/Struo.Application/Query/ItemService.cs`)在失敗時會拋出 `PermissionDeniedException`，
訊息為 `"Writes to '{collection}' require a super-admin."`——但**一般**的逐集合權限檢查會在這五個呼叫點
的每一個之中**先**執行，而且五個呼叫點用的並非同一個檢查：`CreateAsync`、`UpdateCoreAsync` 與
`RevertAsync` 檢查 `CanWrite`，失敗時回傳 `"Write not permitted."`；`DeleteAsync`
與 `RestoreAsync` 則檢查 `CanDelete`，失敗時回傳 `"Delete not permitted."`——
`RequireSuperAdminForAdminOnly` 緊接在這五個檢查之後執行，所以一個對某個 `AdminOnly` 集合完全**沒有**一般授權的
呼叫端，看到的是通用的逐動詞訊息，而 AdminOnly 專屬的訊息只會出現在一個確實持有相關逐集合授權、但並非
super-admin 的呼叫端身上。以下針對寫入情境分別即時驗證了這兩者，刻意將每一項檢查獨立出來：

```
# editor@example.com has NO grant on 'role' at all:
$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"description":"hacked"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Write not permitted."}}

# public role (editor's floor) temporarily granted write on 'role', still not super-admin:
$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"description":"hacked"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Writes to 'role' require a super-admin."}}
```

`RolesController` 直接在它自己的每一個 action 上強制執行相同的 super-admin 要求
(`RequireAdmin()`，在每個 action 中最先被檢查——`GetPermissions` 與 `PutPermissions`，
`src/Struo.Api/Controllers/RolesController.cs`)，完全不經過
`ItemService`——以下從完全不同的程式碼路徑即時驗證了相同的拒絕形狀：

```
$ curl -s -i -X PUT http://localhost:5221/api/roles/<id>/permissions -H "X-Struo-CSRF: 1" \
    -b editor-cookies.txt -d '[]'
{"success":false,"error":{"code":"FORBIDDEN","message":"Admin role required."}}
```

**`UsersController` 對每一個 action 都做了相同的事，只有一個刻意設計的例外：**
`PUT /api/users/{id}/password` 只有在呼叫端要變更**別人**的密碼時才會呼叫 `RequireAdmin()`
(`ChangePassword`，`src/Struo.Api/Controllers/UsersController.cs`)——一個非管理員的已驗證使用
者可以透過提供 `currentPassword` 來變更**自己**的密碼，該值會在寫入之前先與所儲存的雜湊值比對，
於 `ChangePassword` 的自助式分支之中。這是整個身分／RBAC 表面中唯一一條自助式寫入路徑；其他每一個 `UsersController`/
`RolesController` action(建立使用者、核發／撤銷 access token、有效權限預覽、角色權限矩陣)都無條件
需要 super-admin，沒有任何自助式例外。以下針對 `editor@example.com`(沒有任何管理授權)變更自己密碼
的情境進行了即時驗證：一個錯誤的 `currentPassword` 會被拒絕為 `400`，代碼是
`INVALID_CURRENT_PASSWORD`——這是「知識證明」而非管理員關卡，但也刻意**不是** `401`/`UNAUTHORIZED`，
因為呼叫端本來就持有一個有效的 session；SPA 的全域 401 處理器只要看到 `401` 就會清除 session，所以
沿用那個代碼在這裡會讓呼叫端因為一個單純的打字錯誤而被登出——而正確的 `currentPassword` 則會成功：

```
$ curl -s -i -X PUT http://localhost:5221/api/users/<self-id>/password -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"newPassword":"tempPassword3","currentPassword":"wrongpass"}'
HTTP/1.1 400 Bad Request
{"success":false,"error":{"code":"INVALID_CURRENT_PASSWORD","message":"Current password is incorrect."}}

$ curl -s -i -X PUT http://localhost:5221/api/users/<self-id>/password -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"newPassword":"tempPassword3","currentPassword":"editorpass1"}'
HTTP/1.1 204 No Content
```

一個建立在「每一個 `UsersController` action 都需要 super-admin」之上的威脅模型，恰恰在這唯一一個端點
上是錯的——這件事值得明白說出來，而不是留成一個隱含的例外。

`AdminOnly` 集合的讀取**沒有**受到特別限制——它們與其他任何集合一樣，都經過相同的逐集合 `CanRead`
授權；只有寫入才帶有 super-admin 要求。

## 公開讀取授權 (`Rbac:PublicReadCollections`)

已於上方在種子資料一節中提過；此處重述，是因為這是本章 RBAC 模型最直接掌控的唯一一個設定鍵。它**只**
在 `roles` 資料表第一次被建立時被讀取(第 3 章)——之後再變更並重新啟動，對既有資料庫沒有回溯效果。要
針對一個現存的資料庫授予公開讀取權，唯一的方法是直接透過管理後台中的角色權限矩陣(或其底層的
`PUT /api/roles/{id}/permissions` 端點，如上方所示)。

## Hidden 欄位：從 RBAC 的角度看

第 5 章記載了 `[CmsField(Hidden = true)]` 在 schema、投影、GraphQL 與查詢 DSL 各處的作用。有兩項後果
在這裡特別重要。

**讀取側——查詢 DSL 的排除是一項安全性質，不只是整潔。** 一個 `Hidden` 欄位完全被排除在
filter/sort/`fields=` 白名單之外(第 8 章)，指名它會被拒絕為未知欄位。若沒有這道排除，一個外形像憑證的
`Hidden` 欄位仍然可被過濾，`meta.total` 就會把它變成一個逐字元擷取的探測工具——「值不會被投影」這件事
救不了你。

**寫入側——`Hidden` 不是寫入防護，出貨的 schema 也沒有把它當成寫入防護。** 沒有任何機制會因為一個欄位
是 `Hidden` 就剝除它;真正剝除它的是 `ItemDeserializer.Deserialize`
(`src/Struo.Application/Query/Write/ItemDeserializer.cs`)內針對 `IsSystem`／`ReadOnly` 的迴圈。出貨的
兩個 `Hidden` 欄位(`User.Password`、`User.AccessToken`)*同時*也被宣告為 `ReadOnly`，那才是實際捨棄
客戶端所提供之值的原因。你自己的特權欄位請比照辦理——單靠 `Hidden`，任何猜到欄位名稱的呼叫端都仍然
寫得進去。已即時驗證：一次試圖覆寫某使用者密碼的一般 `PUT` 會成功(請求本身不會被拒絕——該欄位只是被
丟棄)，而且可以證明所儲存的雜湊值並未改變：

```
$ curl -s -X PUT http://localhost:5221/api/items/user/<editor-id> -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"email":"editor@example.com","password":"IGNORED-VALUE"}'
{"success":true,"data":{"id":"...","version":2,"email":"editor@example.com", ...}}   # no "password" key in the response — Hidden, never projected

$ docker exec struo-postgres psql -U struo -d struo -t -c "select password from users where email='editor@example.com';"
 $argon2id$v=19$m=65536,t=3,p=1$8OQS79u9b0SQAywU5GXKFQ$ZZlbXg1/mIGKjRZlveHSs4jSGyhJ0GKP5z8UibRPOWs
```

(上面的雜湊值在寫入前後完全相同——送出的 `"IGNORED-VALUE"` 從未抵達資料庫。變更密碼唯一受支援的方式
是 `PUT /api/users/{id}/password`——無論是身為 super-admin 變更別人的密碼，還是身為帳號本人提供正確的
`currentPassword`(見上方的自助式例外)；第 9 章記載了這個端點本身。)

管理後台實際上把這個端點放在兩個入口，不是一個:每一位已登入的使用者，都能透過 app shell 右上角的
帳號選單走自助式變更;super-admin 則是從使用者表單上的動作走重設路徑。自助式變更之所以放在 shell
選單而不是使用者表單上，正是因為 `User` 是 `AdminOnly`(見上方)——一般使用者根本無法開啟那個表單，
若把自助式變更放進表單裡，等於讓除了 super-admin 以外的每一種角色都失去變更自己密碼的途徑。
`AdminOnly` 管控的是寫入，不是表單對其擁有者的可見性;真正讓「僅限管理員」這一半成立的，是重設動作
本身的守衛。

## 管理後台中的有效權限預覽

`GET /api/users/{id}/effective-permissions`(僅限 super-admin，第 9 章)的存在，正是為了讓管理後台的
使用者編輯表單能夠在尚未儲存之前，先顯示某個角色選擇「會」授予什麼。它重複使用了一個真正的請求所經過的
完全相同一組解析元件(`IRolePermissionStore` + `PermissionResolver`)，因此這個預覽本質上就與實際會被
強制執行的內容完全一致——而不是另外維護的一份近似值。這也包括上方的 `public` 底線：`LoadForRolesAsync`
會把同一份 `public` 授權，聯集進一個假設性的角色集合之中，就像 `LoadForUserAsync` 把它聯集進一個真正
呼叫端所儲存的角色一樣，所以這個預覽絕不可能與請求管線實際會解析出的結果不一致——預覽一個空的或尚未
儲存的角色選擇，仍然至少會顯示那道底線，絕不會是一個人為造成的空結果。以下三種不同的請求形狀，全都針對
一個無角色的 `editor@example.com` 進行了即時驗證：

```
# absent `roles=` -> the user's actually-STORED roles, unioned with the public floor (editor holds
# none, so this is just the floor itself, currently empty)
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions"
{"success":true,"data":{"isSuperAdmin":false,"permissions":{}}}

# `roles=` present but EMPTY -> hypothetical preview of an empty role set, unioned with the same
# public floor
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions?roles="
{"success":true,"data":{"isSuperAdmin":false,"permissions":{}}}

# hypothetical: "what if this user were assigned the admin role?" -- an UNSAVED selection
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions?roles=<admin-role-id>"
{"success":true,"data":{"isSuperAdmin":true,"permissions":{}}}

# an unknown role id in the hypothetical set is rejected outright, not silently dropped
$ curl -s -b cookies.txt "http://localhost:5221/api/users/<editor-id>/effective-permissions?roles=00000000-0000-0000-0000-000000000000"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown role ids: 00000000-0000-0000-0000-000000000000"}}
```

**缺席**的 `roles=` 參數，與一個存在但**為空**的參數，兩者之間的區別是刻意設計、也是刻意實作出來的，
透過 `UsersController.GetEffectivePermissions` 內的 `Request.Query.TryGetValue` 檢查
(`UsersController.cs`)：ASP.NET Core 對一個純字串參數的預設模型繫結，
會把兩者都摺疊成 `null`，因此該控制器改為直接讀取 `Request.Query`，正是為了讓「預覽已儲存的角色」與
「預覽一個無角色的選擇」維持成可以區分的請求形狀。`isSuperAdmin: true` 搭配一個空的 `permissions` 映射
(上方第三個範例)是 `EffectivePermissions` 對於 super-admin 所記載的形狀——每一項授權都是隱含的，因此
永遠不會填入任何逐集合的映射；一個真正的 super-admin session 下，`GET /api/auth/me` 回傳的也是相同的
形狀(第 9 章)。

## 接下來該去哪

- 第 8 章 [查詢 DSL](08-query-dsl.md) 與第 9 章 [REST API](09-rest-api.md)，涵蓋 `X-Struo-CSRF`
  機制本身、完整的 cookie/bearer 端點表，以及預設的 `Adaptive` 驗證機制如何在每一個端點上——包括
  讀取——都以相同方式解析一個純 bearer 的呼叫端。
- 第 3 章 [設定參考](03-configuration-reference.md)，涵蓋本章提及的每一個設定鍵——
  `Auth:BootstrapAdmin`、`Rbac:PublicReadCollections`、`RateLimiting:Login`、`Redis`、`Oidc`——的
  完整內容，包括它們「只在第一次啟動時生效」的但書。
- 第 11 章 [檔案、媒體與圖片轉換](11-files-and-media.md)，涵蓋 `IFileAccessPolicy`——RBAC 在一般
  `ItemService` 路徑之外被強制執行的唯一場合。
- 第 13 章 [版本紀錄與軟刪除](13-revisions-and-soft-delete.md)，涵蓋 `DeletedAccessGuard`——唯一比
  普通 `CanRead` 更嚴格的讀取端權限檢查。
