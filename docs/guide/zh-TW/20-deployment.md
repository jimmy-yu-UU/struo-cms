# 20. 部署

把 API 專案（`Struo.Api`）與後台 SPA 部署到正式環境，要弄清楚哪些設定送錯只是被忽略、哪些會讓
啟動直接失敗；後半段接著談兩個容器映像各自打包了什麼、各自把什麼留給操作者自己做。

## 正式環境檢查清單

以下每一項都是一個容易送錯、後果卻不明顯的設定；鍵名的完整語義見
[第 4 章：設定參考](04-configuration.md)，這裡只講部署時的後果。

### 新版 API 上線前先跑 `migrate`

`Database:MigrateOnStartup` 預設是 `false`：資料庫還有待套用的 migration，API 就拒絕啟動（見下方
〈啟動時會快速失敗的事〉）。部署流程因此要在新版 API 啟動之前跑一次 `migrate`，可以是一次性的
job、init container，或 `docker run --rm … <映像> migrate`。也可以設
`Database__MigrateOnStartup=true`，讓 API 自己在開始服務前套用。

好幾個副本都設了 `MigrateOnStartup` 時，它們在 migration 鎖上排隊，只有一個在套用；等超過
`Database:MigrationLockTimeoutSeconds` 的副本啟動失敗。SQLite 與 Oracle 都不拿鎖，要確定只有一
處在跑 `migrate`。

Migration 只往前走，沒有回滾命令，跑之前先備份資料庫。正式環境啟動時不做結構檢查，`migrate` 之後
自己跑一次 `migrate:check`。命令與鎖的細節見[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)。

### 第一次 `migrate` 時指定或記下 bootstrap 密碼

`Auth:BootstrapAdmin:Password` 在核心的 seed migration 第一次執行時用來植入帳號，之後再怎麼改這個
值，都不會影響已經存在的帳號。`migrate` 作為獨立的 job 執行時，`Auth__BootstrapAdmin__*` 要設在
那個 job 的環境裡，API 自己的環境跟植入無關。

範本不附 bootstrap 密碼。留空的話，seed 會隨機產生至少 24 個字元的密碼，在 `migrate` 的輸出（由 API
自己跑 migration 時則是 API 的 log）以 Warning 印出一次；請當場複製，之後不會再印。自己指定的密碼
在任何環境都必須符合 `Auth:Password`，而且不能是 `admin`。只要設定的是弱值，`migrate`、`migrate:preview` 與 `Database:MigrateOnStartup`
啟動都會拒絕（資料庫已經植入過也一樣），錯誤訊息點名 `Auth__BootstrapAdmin__Password`；帳號建立之後就把
它移除。

### 登入速率限制：帳號層開、IP 層看拓樸

帳號層的節流（`RateLimiting:LoginAccount`）預設開著，能單獨擋下針對單一帳號的暴力嘗試；IP 層
的節流（`RateLimiting:Login`）預設關閉，因為它依 `Connection.RemoteIpAddress` 分桶：反向代理
後面（沒有透過 `ForwardedHeaders` 信任的代理）每個人看起來都是代理自己的位址，而共用一條對外 NAT
的辦公室即使代理設定正確，也一樣擠在同一個桶裡。兩層各自的行為見[第 16 章：認證與 SSO](16-authentication.md)〈登入速率限制〉一節。

### 反向代理後面要啟用 `ForwardedHeaders`

除非 `ForwardedHeaders:Enabled` 是 `true`，API 一律忽略 `X-Forwarded-For`、`X-Forwarded-Proto`、
`X-Forwarded-Host`。在反向代理後面，`Connection.RemoteIpAddress` 會停在代理自己的位址，TLS 在代理
終結時請求的 scheme 也會停在 `http`。上面的登入 IP 限流、任何依賴用戶端位址的行為都會被打亂，OIDC
還會組出提供者會拒絕的 `http` 重新導向網址。

設 `ForwardedHeaders__Enabled=true`，並把代理列在 `ForwardedHeaders__KnownProxies__0`（位址）或
`ForwardedHeaders__KnownNetworks__0`（CIDR 範圍）。只有來自這些來源的
請求才受信任；旗標打開卻兩個清單都空，或有格式錯誤的項目，主機會拒絕啟動並點名鍵。API 前面沒有
代理時就維持關閉。

API 前面是管理映像時，API 的代理就是那個 nginx 容器：列出該容器自己的位址，或把兩者放在專用的 network
再列出那個 network 的範圍。不要信任整段共用的 Docker network 範圍：它包含 bridge 閘道，而打到 API 已發布
連接埠的請求會經由這個閘道進來，等於能偽造 `X-Forwarded-*`。API 躲在 nginx 後面時，根本不要發布它的連接埠。

TLS 終結在那個容器前面的另一個代理時，中間有兩跳，API 只採信其中一跳。它看到的用戶端位址因此是外層
代理的，每個用戶端的每 IP 登入限流都落進同一個桶；scheme 與 host 仍會正確送達。外層代理必須把
`X-Forwarded-Proto: https` 設成覆寫而不是附加，因為映像的 nginx 會把收到的值原樣帶過去，沒收到才用自己
連線的 scheme。

### `Redis:ConnectionString`：多副本或要撐過重啟就必設

留空時，工作階段狀態（cookie 對應的 ticket、`RateLimiting:LoginAccount` 的計數）全部退回行程
內的快取。只有一個副本、也不在乎重啟就丟掉所有工作階段，留空沒關係；有一個以上的副本，或需要
工作階段撐過行程重啟，就得設定它。不設，每次重啟都逼所有使用者重新登入，多副本則各自有各自
的一份，同一個使用者打到不同副本會看起來像沒登入過。設了 Redis，登入嘗試計數是所有副本共用的
Redis 原子計數器，Redis 連不上時登入會回 5xx。沒設 Redis 時，每個行程各自計數，並在驗證密碼之前
於帳號層級的鎖內把每次嘗試算進去，所以單一副本不會少算，但副本之間看不到彼此的嘗試。

### Cookie 只在 HTTPS 下送回

Development 之外的環境，認證 cookie 只有透過 HTTPS 的請求才會被送回（`Auth:Cookie:RequireSecure`
可覆寫，只有前面沒有 TLS 的主機才該設成 `false`）；沒有先終結 TLS 就把流量送進
來，登入回應看起來正常（帶著 `Set-Cookie`），但瀏覽器往後不會再帶著這個 cookie，呼叫端等於卡
在「登入了卻又要登入」的迴圈。安全政策怎麼判斷、CORS 開了之後多出哪些規則，見
[第 16 章](16-authentication.md)。

唯一的例外是 `http://localhost`：瀏覽器把它當成安全來源，照樣送回 cookie。本章最後的本機
Compose 環境就是靠這點，不架 TLS 也能登入。

### Scalar／OpenAPI 在 Development 之外關閉

`GET /scalar` 與 `/openapi/v1.json` 只在 Development 註冊（除非 `OpenApi:Enabled` 為 `true`），Production 啟動時這兩個路由
完全不存在。

相鄰的 GraphQL schema 揭露路由另外由 `GraphQl:ExposeSchema` 控制，預設值 `null`
同樣只在 Development 開放，機制見[第 14 章：GraphQL API](14-graphql.md)。

### OIDC 三個檢查要釘死

`Oidc:AllowedTenantId` 的預設值是一個打不中任何 tenant 的佔位字串，換成真正的 tenant 之前，
外部登入對任何人都會失敗。

換上真正的 tenant 之後，`Oidc:RequireEmailVerified`（預設不要求）
與 `Oidc:AllowedEmailDomains`（預設空清單即不限制）都是預設放行的檢查，三個都該在正式環境釘
死，否則只剩下 email 相等這一道關卡。檢查順序見[第 16 章](16-authentication.md)。

### 安全回應標頭：API 只送 nosniff

API 專案自己只送一個安全回應標頭：`X-Content-Type-Options: nosniff`，連錯誤回應、CORS 預檢
與裸 404 都涵蓋；`Strict-Transport-Security`、`X-Frame-Options`／CSP 的 `frame-ancestors`、
`Referrer-Policy` 都不是它的責任，交給反向代理處理——`frontend/nginx/default.conf.template`
是一份可用的參考實作，不是非用 nginx 不可的規定。

## 啟動時會快速失敗的事

`Program.cs` 在最上層把整段啟動包在一個 `try`／`catch`／`finally` 裡：任何一個環節丟出例外，
都會被 `Log.Fatal` 記下，並把 `Environment.ExitCode` 設成 `1` 才結束程序。沒有這一步，行程
會以代碼 `0` 結束，讓編排工具誤以為啟動成功，既不重啟也不告警。

`Database`、`Struo:Files`、`Oidc`、`Query`、`Auth:Password` 這五組設定都綁了
`ValidateOnStart`：缺 `Database:ConnectionString`、`Oidc:Enabled=true` 卻沒給 `ClientId`，
或 `Query:MaxLimit` 超出範圍，都會在應用程式真正開始接受請求之前，就丟出
`OptionsValidationException` 讓啟動失敗。

資料庫還有待套用的 migration、`MigrateOnStartup` 又是 `false`，是同一套快速失敗機制的另一個例
子。下面是 API 映像對一個空的 SQLite 資料庫啟動：

```
[10:35:35 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 2 migration(s) pending, the lowest is 202610080000; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

行程隨後以代碼 `1` 結束，後面的堆疊追蹤此處省略；訊息本身點名了要跑的命令與設定鍵。

另一件跟設定無關、但同樣在啟動最上層就決定好的事：程序一開始就把整個行程的預設文化特性釘死成
invariant，操作者不需要另外釘 `LANG` 或 `DOTNET_SYSTEM_GLOBALIZATION_*`。SqlSugar 重新解析
查詢篩選器渲染出來的字面值時，用的是同一個文化特性，兩者本來就得一致。

## 日誌

Serilog 設定全部在 `Serilog:*` 底下：一個 Console sink，加一個寫入 `logs/struo-.log` 的 File
sink，按日期滾動、允許同時被多個行程共享讀寫，檔名長成 `struo-YYYYMMDD.log`，一天一份。

設定在啟動時建立一次，`Serilog:*` 改了要重開行程才生效，`appsettings.json` 的檔案監看不會讓
它們生效；`UseSerilog` 同時讀 `IConfiguration` 與 DI 容器，透過 DI 註冊的 enricher 也會被撿
起來一併生效。

## 兩個容器映像

### 建置

兩份 Dockerfile 各自產生一個獨立映像：根目錄的 `Dockerfile` 建 API，`frontend/Dockerfile` 建
後台 SPA。根目錄的 `docker-compose.yml` 用這兩份 Dockerfile 起一整套開發與測試環境，不是正式
環境的部署範本，見本章最後的「本機的 Compose 環境」。本機建置跟 CI 的 `docker` job 跑的是同樣
兩道指令，只是本機習慣打 `:local` 標籤，CI 打的是 `:ci`：

```
docker build --tag struo-api:local .
docker build --tag struo-admin:local frontend
```

API 映像要讀進整個儲存庫，不只是 `src/Struo.Api/`：一個 fork 的內容專案是透過
`ProjectReference` 參照進來的，建置因此需要那個參照能碰到的每一個專案，加上根目錄的
`Directory.Build.props`／`Directory.Packages.props`。根目錄的 `.dockerignore` 把
`frontend/`、`docs/` 跟一般的建置產物擋在外面。

### API 映像

監聽 `8080`，以非 root 使用者 `app` 執行。映像啟動時不套用 migration：進入點是
`dotnet Struo.Api.dll`，所以 `docker run --rm … struo-api:local migrate` 就是對資料庫跑
`migrate`；不然就設 `Database__MigrateOnStartup=true`。

`app` 擁有三個目錄：`/app/App_Data`（本機檔案後端的上傳內容與圖片轉檔快取）、`/app/logs`
（Serilog 的 File sink）跟 `/home/app/.aspnet/DataProtection-Keys`（金鑰圈，見下面
「DataProtection 金鑰」）。具名 volume 掛在其中任何一個上，一開始就能寫。

只有 `/app/App_Data` 另外宣告成 `VOLUME`，就算 `docker run` 完全不帶 `--mount`／`-v`，這裡也
會拿到一個匿名 volume。

`HEALTHCHECK` 每 30 秒打一次 `http://localhost:8080/health/live`（5 秒逾時、30 秒啟動寬
限、3 次重試），打的是 liveness，不是 `/health/ready`。`docker ps`／`docker inspect` 看到的
健康狀態，因此只代表「行程還在回應」，不代表資料庫或快取連得上。

### 啟動這個映像至少要給什麼

下面五個環境變數決定這個映像能不能啟動，以及啟動之後怎麼運作；其餘每一個設定鍵的完整語義見
[第 4 章](04-configuration.md)。

- `Database__DbType`——選哪個後端（`PostgreSQL`、`Sqlite`、`MySql`、`SqlServer`、
  `Oracle`）。
- `Database__ConnectionString`——必須設定，映像裡預設帶的是含 `REPLACE_ME` 佔位字串的
  PostgreSQL 連線字串，不能直接拿去用。
- `Database__MigrateOnStartup`（選填）——留空就是 `false`，部署時要另外跑 `migrate`；設成
  `true`，API 啟動時自己套用 migration。
- `Redis__ConnectionString`——留空就是單副本、不撐重啟的行程內快取。
- `Struo__Files__Backend`——預設 `local`，寫進 `/app/App_Data`（落在宣告的 volume 裡）；要
  用 `s3`，還得配上對應的四個 `Struo__Files__S3__*` 鍵。

這個映像沒有設定 `ASPNETCORE_ENVIRONMENT`，不設就是 ASP.NET Core 的框架預設值
`Production`。這決定了認證 cookie 的安全政策，見上面的正式環境檢查清單。

### DataProtection 金鑰

DataProtection 金鑰圈（簽章、加密認證 cookie 與 antiforgery token 用的）寫在容器內的
`/home/app/.aspnet/DataProtection-Keys`，這個映像沒有把它宣告成 volume，也沒有掛載。

不持久化這個目錄有兩個後果：換掉容器，現有的每一張認證 cookie 跟 antiforgery token 全部失
效，所有使用者都要重新登入；跑一個以上的副本，每個副本各自有一份不共用的金鑰圈，一個請求打
到跟核發時不同的副本，cookie 驗證或 antiforgery 會直接失敗。

單一副本，把這個路徑掛成 volume 就夠了，金鑰能撐過容器重建；一個以上的副本，得自己配一個共
享的金鑰存放區——共享檔案系統、Redis，或雲端供應商的金鑰圈服務，這個專案沒有替任何一種做好
設定。不論掛在哪裡，金鑰都是明文存放的：沒有配置 XML 加密器，金鑰圈目錄裡是可以直接讀的金
鑰材料，要比照其他密鑰保護。

### 後台 SPA 映像

監聽 `80`，用 nginx 提供 Vite 的 production build。跟 API 映像不同，這裡沒有 `HEALTHCHECK`：
沒有下游依賴可以探測，要探測就直接打 `/`。

SPA 的 API 位址是建置期就烤進映像裡的：`vite build` 在 production 模式下會讀
`frontend/.env.production`（有這份檔案的話），`frontend/.dockerignore` 也刻意不把它排除在建置範圍
之外，只排除開發者自己的 `.env`／`.env.local`／`.env.*.local`。儲存庫本身沒有附
`.env.production`，只有追蹤中的 `frontend/.env.example`。改了這個值，要重新建置映像才會生效。

`API_UPSTREAM`（預設 `http://api:8080`）要寫成 `scheme://host:port`，不能帶路徑或結尾斜
線：`proxy_pass` 吃的是一個 nginx 變數，不是字面值，所以容器就算 API 還沒起來也能先啟動，代
價是變數裡任何路徑片段都會取代請求的 URI，而不是被當成前綴接上去。

映像會把 `/api/*`、`/signin-oidc` 與 `/signout-callback-oidc` 代理給 API，並把用戶端的 `Host` 標頭（含埠號）
當作 `Host` 與 `X-Forwarded-Host` 轉送，讓 API 看到瀏覽器實際使用的來源。兩個回呼路徑是 OIDC 的重新導向
目標；`/api/` 之外的自訂 `Oidc:CallbackPath` 要在範本另加一個 `location` 區塊。登記重新導向網址的做法見
第 16 章。

`HSTS_VALUE` 預設空字串，nginx 遇到值是空字串的 `add_header` 就完全不送這個標頭；只有在這裡
或前面的代理已經終結 TLS 的每一個可觸達請求上，才該把它設起來。不管 `HSTS_VALUE` 怎麼設，
`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、
`Referrer-Policy: strict-origin-when-cross-origin` 這三個標頭一律都送；API 自己的
`nosniff` 在代理到 `/api/*` 的回應上被隱藏掉，讓這個標頭只出現一次。

映像另外在每個回應上送 `Content-Security-Policy` 與 `Permissions-Policy`：

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline';
  img-src 'self' data: blob: ${CSP_IMG_EXTRA}; connect-src 'self' ${CSP_CONNECT_EXTRA};
  frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
```

指令稿只能從映像本身載入（主題初始化是檔案 `theme-init.js`，不是內嵌指令稿），頁面不能被嵌入框架，
表單送出、`<base>` 與外掛都被限制在同一來源或關閉。從 `/api/*` 與這兩個 OIDC 回呼路徑代理出去的回應改帶
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; sandbox`，API 自己在那裡送的
政策會被丟掉。

`CSP_IMG_EXTRA` 與 `CSP_CONNECT_EXTRA` 預設空字串，各自接受以空白分隔的來源（`https://cdn.example.com`），
會附加到 `img-src` 與 `connect-src`。圖片從別的來源載入時設 `CSP_IMG_EXTRA`，例如儲存體前面的 CDN，或
`Struo:Files:PresignedRedirect` 為 `true` 而瀏覽器直接向 bucket 取檔時的 presigned URL，或 `Branding:LogoUrl` 是其他主機上的絕對 URL。瀏覽器要呼叫別的來源時
設 `CSP_CONNECT_EXTRA`，例如用 `VITE_API_BASE_URL` 建置進映像、指向不同主機的 API。被擋下的請求會在瀏覽器主控台
顯示 `Content-Security-Policy` 違規。

快取規則：`index.html` 送 `Cache-Control: no-cache`；`/assets/` 送
`public, max-age=31536000, immutable`（Vite 的檔名帶內容雜湊）。`client_max_body_size` 是
`32m`，高於後端 `Struo:Files:MaxUploadBytes` 的 25 MiB，讓過大的上傳收到後端自己的 JSON 錯
誤信封，而不是 nginx 的 HTML 錯誤頁。

`NGINX_RESOLVER`（預設 `127.0.0.11`，Docker 內建 DNS，5 秒逾時）只有在使用者自訂的 Docker
network 上才有意義；掛在預設的 bridge network 上，每一個 `/api/*` 請求都會在解析逾時之後收
到 502。

要讓 SPA 部署在跟 API 不同的來源，設定 `VITE_API_BASE_URL` 的方式與生效時機，見
[第 19 章：後台客製化](19-admin-customization.md)；建這個映像時，值要放在你自己新增並提交的
`frontend/.env.production`，開發者自己的 `frontend/.env` 不會進到映像裡。

### 映像之外還要自己做的事

映像只負責把應用程式跑起來。終結 TLS、API 前有代理時設定 `ForwardedHeaders`、重啟策略、水平擴展、密鑰
注入，以及把 `/health/live` 與 `/health/ready` 接到編排系統自己的探針上，都要自己接。

### 兩個映像一起驗證

下面五道指令建一個使用者自訂的 network 與一個 volume，先對 volume 裡的 SQLite 資料庫跑
`migrate`，再起兩個容器讓彼此看得到對方；這是驗證步驟，不是建議的正式環境拓樸。本章最後的本機
Compose 環境會把兩個映像連同 PostgreSQL、Redis 一起起來：

```
docker network create struo-verify
docker volume create struo-verify-data
docker run --rm --volume struo-verify-data:/app/App_Data --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local migrate
docker run --detach --name api --network struo-verify --volume struo-verify-data:/app/App_Data --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local
docker run --detach --name admin --network struo-verify --publish 8081:80 struo-admin:local
```

第三道指令最後印出 `Applied 202610080000  CreateCoreSchema` 與 `Applied 202610080001  SeedCoreData`。
少了它，`api` 容器會以上面〈啟動時會快速失敗的事〉的訊息結束。

admin 容器預設的 `API_UPSTREAM=http://api:8080` 之所以解析得到，是因為兩個容器同在這個使用
者自訂的 network 上，而 API 容器就叫 `api`。API 容器如果要連到 Docker host 上的資料庫，用
`host.docker.internal`。這個名稱只在 Docker Desktop 上開箱即用，Linux 上要在 `docker run`
加 `--add-host=host.docker.internal:host-gateway`。

## 健康檢查探針

`/health/live` 不跑任何檢查（`Predicate = _ => false`），只確認行程還在接受請求；liveness
探針該在這裡沒回應時重啟容器或 pod。

`/health/ready` 執行兩項標了 `ready` 的檢查：`DbReadinessCheck` 對資料庫做一次真正的連線往
返，`CacheReadinessCheck` 對設定好的 `IDistributedCache`（Redis 或行程內快取）做一次寫入再
讀回；readiness 探針該等兩項都連得上，才把流量導進來。

兩個路由怎麼打，[第 3 章：快速開始](03-getting-started.md)已經介紹過；下面是對一個匿名請求的
完整回應：

```
$ curl -i http://localhost:5221/health/live
HTTP/1.1 200 OK
Content-Type: text/plain
Date: Thu, 17 Sep 2026 03:24:31 GMT
Server: Kestrel
Cache-Control: no-store, no-cache
Expires: Thu, 01 Jan 1970 00:00:00 GMT
Pragma: no-cache
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

Healthy
```

`/health/ready` 通過時回的東西一模一樣：`200`、body `Healthy`、同一組標頭；差別在於它是兩項
檢查都連得上才這樣回。

## 重新部署與開著的分頁

後台重新部署之後，一個還開著的分頁，它的 `index.html` 仍然在要求一個新版本不再提供的 chunk
檔名；`frontend/src/router/chunkLoadRecovery.ts` 在這種 chunk 404 時重新整理頁面來復原，靠
一個只用一次的 `sessionStorage` 標記擋住無窮迴圈。真的不存在的 chunk 會變成一個錯誤，而不
是不斷重新整理。

## 備份

- **資料庫**——除了上傳檔案之外，所有資料的權威來源都在資料庫裡，用你那個後端自己的工具
  備份，例如 PostgreSQL 的 `pg_dump`，或託管服務的快照與 PITR；StruoCMS 沒有另外提供備份機制。
  Migration 沒有回滾命令，每次 `migrate` 之前都先備份。
- **上傳檔案**——存在資料庫之外，備份哪裡看 `Struo:Files:Backend` 設成什麼：`local` 模式備
  份 `Local:RootPath` 那個目錄，`s3` 模式靠 S3 相容儲存桶自己的版本控制或複寫；只備份資料
  庫，會悄悄漏掉每一個上傳過的檔案。
- **Redis 不需要**——Redis（或行程內的替代品）裡只有短命狀態：cookie 工作階段的 ticket、帳
  號登入嘗試計數、工作階段撤銷紀錄。丟了它只會逼每個已登入的使用者重新登入，並把登入計數歸
  零，它不是權威來源，不需要另外備份。
- **Migration 與設定**——fork 自己的 migration 類別、`appsettings.*`、環境變數或密鑰管理系統
  裡的值，是一般的原始碼或部署管線產物，跟其餘部署一起備份即可。

## 本機的 Compose 環境

`docker compose up -d --build` 用上面兩份 Dockerfile 起 `postgres`、`redis`、`api`、`admin` 四個
服務，怎麼啟動、怎麼登入見[第 3 章：快速開始](03-getting-started.md)。它是開發與測試用的環境：
`api` 以 Production 執行，每次啟動先自己跑 migration（`Database__MigrateOnStartup`）。上正式
環境，照本章前面的檢查清單自己組部署。

PostgreSQL 的密碼是固定的開發值，Redis 沒有密碼。每個埠預設只綁在 `127.0.0.1`，只有本機連得到
資料庫、Redis、API 跟後台。`STRUO_BIND_ADDRESS` 決定綁定的位址；設成 `0.0.0.0` 會把埠開在所有
網路介面上，同一個網路裡的其他機器就連得到，固定的開發密碼與沒有密碼的 Redis 也跟著公開。
只在信得過的網路上這樣做。

健康檢查端點在 API 的埠上：`/health/live` 與 `/health/ready`，預設是
`http://localhost:8081/health/live`（`STRUO_API_PORT` 可改）。後台的 nginx 只轉發 `/api/*`，
從後台的埠打 `/health/...` 拿到的是 SPA 頁面。Compose 替 `api` 另外設了每 5 秒一次的健康檢查，
`docker compose up --wait` 不必等映像預設的 30 秒間隔。

DataProtection 金鑰掛在 volume `struo-dpkeys`，容器重建也撐得住，但是明文存放：第一次啟動的
log 會出現一筆 `No XML encryptor configured` 警告。開發與測試這樣就夠了；正式環境要照上面
「DataProtection 金鑰」一節，自己保護金鑰。

### S3 相容儲存

`docker-compose.yml` 的 `minio`、`createbuckets` 兩個服務都掛在 `profiles: ["s3"]` 底下，
預設的 `docker compose up -d` 既不會拉這兩個映像，也不會等它們的健康檢查。

Compose 起的 `api` 服務用的是 `local` 檔案後端，MinIO 是給自己跑、`Struo:Files:Backend` 設成
`s3` 的 API 用的。要跟相依服務一起起：

```
docker compose --profile s3 up -d postgres redis minio createbuckets
```

`createbuckets` 是一次性的：啟動時建好 bucket `struo-media` 就結束，結束代碼 `0`。
`docker compose ps` 預設會把它濾掉，要 `docker compose ps -a` 才看得到它顯示 `Exited (0)`。

連接埠由 `STRUO_MINIO_PORT`（預設 9000，API）與 `STRUO_MINIO_CONSOLE_PORT`（預設 9001，主控
台）決定；改了 `STRUO_MINIO_PORT`，記得同步改 `appsettings.Development.json` 裡的
`Struo:Files:S3:Endpoint`。

## 接下來

部署就談到這裡；資料庫結構怎麼由 migration 建立與修改、核心的結構變動怎麼進到你的 fork，見
[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)。
