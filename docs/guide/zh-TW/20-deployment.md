# 20. 部署

把 API 專案（`Struo.Api`）與後台 SPA 部署到正式環境，要弄清楚哪些設定送錯只是被忽略、哪些會讓
啟動直接失敗；後半段接著談兩個容器映像各自打包了什麼、各自把什麼留給操作者自己做。

## 正式環境檢查清單

以下每一項都是一個容易送錯、後果卻不明顯的設定；鍵名的完整語義見
[第 4 章：設定參考](04-configuration.md)，這裡只講部署時的後果。

### `Database:MigrationsPath` 要是絕對路徑

`MigrationRunner.ApplyAsync` 把設定值原封不動丟給 `Directory.Exists`，不會先解析成應用程式
資料夾底下的路徑。相對路徑因此解析到的是「行程啟動當下的工作目錄」，不是應用程式自己的資料
夾——systemd 的 `WorkingDirectory`、容器的 `WORKDIR`，或任何先切換目錄才啟動的啟動器，都可
能跟它不一樣，指到的目錄不存在就會讓啟動直接失敗（見下方〈啟動時會快速失敗的事〉）。

### 第一次啟動前就換掉 bootstrap 密碼

`Auth:BootstrapAdmin:Password` 只在 `struo_users` 資料表第一次建立時被讀一次，之後重開行程再怎麼
改這個值，都不會影響已經存在的帳號。用預設值在 Production 啟動只會在 Warning 等級記一筆，點
名這個設定鍵，不會擋下啟動。這道警告比對的是目前設定裡的值，不是資料庫裡實際存的雜湊：先用
預設值種子、之後才改設定，警告會消失，帳號的密碼卻還是預設值那一組。

### 登入速率限制：帳號層開、IP 層看拓樸

帳號層的節流（`RateLimiting:LoginAccount`）預設開著，能單獨擋下針對單一帳號的暴力嘗試；IP 層
的節流（`RateLimiting:Login`）預設關閉，因為它依 `Connection.RemoteIpAddress` 分桶：反向代理
後面每個人看起來都是代理自己的位址，而共用一條對外 NAT 的辦公室即使代理設定正確，也一樣擠在
同一個桶裡。兩層各自的行為見[第 16 章：認證與 SSO](16-authentication.md)〈登入速率限制〉一節。

### 反向代理要自己加 `UseForwardedHeaders`

`Program.cs` 沒有呼叫 `UseForwardedHeaders(...)`，是刻意的決定。部署在反向代理後面，要自己加
上這個中介軟體並列出 `KnownProxies`／`KnownNetworks`；不加，`Connection.RemoteIpAddress` 看到
的一律是代理自己的位址，不只是上面的登入 IP 限流，任何依賴用戶端位址的行為都會被打亂。

### `Redis:ConnectionString`：多副本或要撐過重啟就必設

留空時，工作階段狀態（cookie 對應的 ticket、`RateLimiting:LoginAccount` 的計數）全部退回行程
內的快取。只有一個副本、也不在乎重啟就丟掉所有工作階段，留空沒關係；有一個以上的副本，或需要
工作階段撐過行程重啟，就得設定它。不設，每次重啟都逼所有使用者重新登入，多副本則各自有各自
的一份，同一個使用者打到不同副本會看起來像沒登入過。設了 Redis，這個計數是所有副本共用的，但
不是保證上限——`IDistributedCache` 沒有原子的讀改寫，同時打進來的失敗有機會少算一次。

### Cookie 只在 HTTPS 下送回

Production 環境下，認證 cookie 只有透過 HTTPS 的請求才會被送回；沒有先終結 TLS 就把流量送進
來，登入回應看起來正常（帶著 `Set-Cookie`），但瀏覽器往後不會再帶著這個 cookie，呼叫端等於卡
在「登入了卻又要登入」的迴圈。安全政策怎麼判斷、CORS 開了之後多出哪些規則，見
[第 16 章](16-authentication.md)。

### Scalar／OpenAPI 在 Production 關閉

`GET /scalar` 與 `/openapi/v1.json` 只在非 Production 環境註冊，Production 啟動時這兩個路由
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

`Database:MigrationsPath` 指到一個不存在的目錄，是同一套快速失敗機制的另一個例子：

```
[11:24:56 FTL] StruoCMS host terminated unexpectedly
System.IO.DirectoryNotFoundException: MigrationRunner: migrations directory not found: 'D:/does-not-exist/migrations'. Check the Database:MigrationsPath configuration value.
```

行程隨後以代碼 `1` 結束，後面接著一段堆疊追蹤（此處省略）；訊息本身點名了出錯的設定鍵。

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
後台 SPA；沒有正式環境用的 `docker compose` 檔。本機建置跟 CI 的 `docker` job 跑的是同樣兩道
指令，只是本機習慣打 `:local` 標籤，CI 打的是 `:ci`：

```
docker build --tag struo-api:local .
docker build --tag struo-admin:local frontend
```

API 映像要讀進整個儲存庫，不只是 `src/Struo.Api/`：一個 fork 的內容專案是透過
`ProjectReference` 參照進來的，建置因此需要那個參照能碰到的每一個專案，加上根目錄的
`Directory.Build.props`／`Directory.Packages.props`。根目錄的 `.dockerignore` 把
`frontend/`、`docs/` 跟一般的建置產物擋在外面。

### API 映像

監聽 `8080`，以非 root 使用者 `app` 執行。`db/migrations` 已經複製進映像的
`/app/db/migrations`；`Database:MigrationsPath` 在這個映像裡預設留空，要讓 migration 腳本在
這裡動起來，把它設成這個絕對路徑。

`app` 能寫兩個目錄：`/app/App_Data`（本機檔案後端的上傳內容與圖片轉檔快取）跟 `/app/logs`（Serilog
的 File sink）；只有 `/app/App_Data` 額外宣告成 `VOLUME`，就算 `docker run` 完全不帶
`--mount`／`-v`，這裡也會拿到一個匿名 volume。

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
- `Database__MigrationsPath`（選填）——留空停用 migration 腳本；要打開，填這個映像裡的絕對
  路徑 `/app/db/migrations`。
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

`HSTS_VALUE` 預設空字串，nginx 遇到值是空字串的 `add_header` 就完全不送這個標頭；只有在這裡
或前面的代理已經終結 TLS 的每一個可觸達請求上，才該把它設起來。不管 `HSTS_VALUE` 怎麼設，
`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、
`Referrer-Policy: strict-origin-when-cross-origin` 這三個標頭一律都送；API 自己的
`nosniff` 在代理到 `/api/*` 的回應上被隱藏掉，讓這個標頭只出現一次。

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

映像只負責把應用程式跑起來。終結 TLS、設定 `UseForwardedHeaders`、重啟策略、水平擴展、密鑰
注入，以及把 `/health/live` 與 `/health/ready` 接到編排系統自己的探針上，都要自己接。

### 兩個映像一起驗證

下面三道指令建一個使用者自訂的 network，起兩個容器讓彼此看得到對方；這是驗證步驟，不是建議
的正式環境拓樸：

```
docker network create struo-verify
docker run --detach --name api --network struo-verify --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local
docker run --detach --name admin --network struo-verify --publish 8081:80 struo-admin:local
```

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

- **資料庫**——除了上傳檔案之外，所有資料的權威來源都在 PostgreSQL，用一般的 PostgreSQL
  工具備份：`pg_dump`、`pg_basebackup`，或託管服務自己的快照與 PITR；StruoCMS 沒有另外提
  供備份機制。
- **上傳檔案**——存在資料庫之外，備份哪裡看 `Struo:Files:Backend` 設成什麼：`local` 模式備
  份 `Local:RootPath` 那個目錄，`s3` 模式靠 S3 相容儲存桶自己的版本控制或複寫；只備份資料
  庫，會悄悄漏掉每一個上傳過的檔案。
- **Redis 不需要**——Redis（或行程內的替代品）裡只有短命狀態：cookie 工作階段的 ticket、帳
  號登入失敗計數、工作階段撤銷紀錄。丟了它只會逼每個已登入的使用者重新登入，並把登入計數歸
  零，它不是權威來源，不需要另外備份。
- **腳本與設定**——`db/migrations/`、`appsettings.*`、環境變數或密鑰管理系統裡的值，是一般
  的原始碼或部署管線產物，跟其餘部署一起備份即可。

## 本機的 S3 相容儲存

`docker-compose.yml` 的 `minio`、`createbuckets` 兩個服務都掛在 `profiles: ["s3"]` 底下，
預設的 `docker compose up -d` 既不會拉這兩個映像，也不會等它們的健康檢查；要啟用，
`docker compose --profile s3 up -d`。

`createbuckets` 是一次性的：啟動時建好 bucket `struo-media` 就結束，結束代碼 `0`。
`docker compose ps` 預設會把它濾掉，要 `docker compose ps -a` 才看得到它顯示 `Exited (0)`。

連接埠由 `STRUO_MINIO_PORT`（預設 9000，API）與 `STRUO_MINIO_CONSOLE_PORT`（預設 9001，主控
台）決定；改了 `STRUO_MINIO_PORT`，記得同步改 `appsettings.Development.json` 裡的
`Struo:Files:S3:Endpoint`。

## 接下來

部署就談到這裡；資料庫的結構怎麼分層管理、跨 fork 怎麼升級核心，見
[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)。
