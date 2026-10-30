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

有一個以上的副本時，把 `migrate` 當成獨立的 job 只跑一次，跑完再推出 API，不要在每個副本上都設
`MigrateOnStartup`。PostgreSQL 的鎖是 session 層級的 advisory lock，交易模式的連線池（例如
PgBouncer）可能把取得和釋放分給不同的伺服器 session。`migrate` 這一步要直接連資料庫，不要經過連線池。

Migration 只往前走，沒有回滾命令，跑之前先備份資料庫。正式環境啟動時不做結構檢查，`migrate` 之後
自己跑一次 `migrate:check`。命令與鎖的細節見[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)。

### SQL Server：開啟 `READ_COMMITTED_SNAPSHOT`

SQL Server 資料庫需要開啟 `READ_COMMITTED_SNAPSHOT`。沒開的話，並行寫入有翻譯之集合的不同項目會死結：
每筆交易的讀取會等其他交易尚未提交的資料列，SQL Server 再從每個死結循環（牽涉兩個以上的連線）中
擇一中止交易，錯誤碼 1205。
在沒有其他連線連著這個資料庫時執行一次：

```sql
ALTER DATABASE [your_database] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
```

`WITH ROLLBACK IMMEDIATE` 會回滾該資料庫上所有未完成的交易，所以要在 API 啟動前或維護時段執行。
Azure SQL Database 預設就是開啟的。

API 自己不會改這個設定。migration 步驟之後它會讀一次旗標，旗標是關的就記一筆警告，內容點出
`READ_COMMITTED_SNAPSHOT` 與上面那行語句。警告不會讓啟動失敗，其他後端也不做這個檢查。

### 資料庫連線：TLS、連線池大小、密碼

PostgreSQL 在正式環境要設 `SSL Mode=VerifyFull`：驅動程式會加密連線，並驗證伺服器的憑證與主機名稱。
伺服器的憑證來自私有 CA 或託管服務自己的 CA 時，再加上 `Root Certificate=/path/to/ca.crt`。

`SSL Mode=Require` 只加密，不驗證憑證，擋不住中間人。加了 `Root Certificate` 也不會改變這一點。
`SSL Mode=Disable` 是明文，只在私有網路裡、而且伺服器接受非 TLS 連線時才用。

沒設 `SSL Mode` 時，預設是 `Prefer`：伺服器提供 TLS 就加密連線，但不驗證憑證；伺服器不提供，就退回明文。

沒用 Kerberos 的話，建議加上 `GSS Encryption Mode=Disable`。不加的話，驅動程式每開一條新的實體連線，
都會先試一次 GSS 加密，連線因此變慢。migration 鎖的連線不走連線池，所以每次 `migrate` 都會再慢一次。

其他後端就用該驅動程式自己的 TLS 選項寫在連線字串裡。

每個 API 行程最多開 `Maximum Pool Size` 條連線（Npgsql 預設 100）。把它乘上副本數，結果要低於伺服器的
`max_connections`，還要留空間給 `migrate` job 與你自己的工具。

連線字串裡有資料庫密碼。用環境變數（`Database__ConnectionString`）或密鑰管理工具注入，不要放在映像或儲存庫
的檔案裡。

### 第一次 `migrate` 之前先設 bootstrap 密碼

`Auth:BootstrapAdmin:Password` 在核心的 seed migration 第一次執行時用來植入帳號，之後再怎麼改這個
值，都不會影響已經存在的帳號。`migrate` 作為獨立的 job 執行時，`Auth__BootstrapAdmin__*` 要設在
那個 job 的環境裡，API 自己的環境跟植入無關。

範本不附 bootstrap 密碼，也不會自動產生。建立帳號一定要有密碼，這個密碼要符合 `Auth:Password`，
也不能是 `admin`。

seed 要執行時，密碼若是空的或太弱，`migrate`、`migrate:preview` 與 `Database:MigrateOnStartup`
啟動都會拒絕，錯誤訊息點名 `Auth__BootstrapAdmin__Password`。已經植入過的資料庫不會被拒絕，
帳號建立之後可以把這個值移除。

預設 email `admin@admin.com` 也請改掉。Compose 檔讀 `STRUO_ADMIN_PASSWORD`，沒設就不會啟動。

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

`ForwardedHeaders:ForwardLimit` 是用戶端與 API 之間的代理數量，預設 `1`。

請求先經過負載平衡器，再經過 nginx，最後才到 API，中間就有兩個代理，要設
`ForwardedHeaders__ForwardLimit=2`。設成 `1` 的話，API 會把負載平衡器的位址當成用戶端，
每 IP 登入限流就把所有用戶端放進同一個桶。

鏈上的每個代理都要受信任：`KnownProxies` 或 `KnownNetworks` 要同時涵蓋負載平衡器與 nginx 容器的位址。
否則 API 讀到第一個不受信任的位址，就停止往前讀。

映像的 nginx 只送一個 `X-Forwarded-Proto`。收到代理送來的值就原樣帶過去，沒收到才用自己連線的 scheme。
前面有終結 TLS 的代理時這樣正好，該代理應該覆寫收到的值而不是附加。這個 nginx 本身是用戶端直連的邊緣
時，用戶端可以自己送這個標頭並被原樣帶過去。這種情況下，在 `frontend/nginx/default.conf.template` 裡，
把兩行 `proxy_set_header X-Forwarded-Proto` 的 `$struo_forwarded_proto` 換成 `$scheme`。

### `Redis:ConnectionString`：多副本或要撐過重啟就必設

留空時，工作階段狀態（cookie 對應的 ticket、`RateLimiting:LoginAccount` 的計數）全部退回行程
內的快取。只有一個副本、也不在乎重啟就丟掉所有工作階段，留空沒關係；有一個以上的副本，或需要
工作階段撐過行程重啟，就得設定它。不設，每次重啟都逼所有使用者重新登入，多副本則各自有各自
的一份，同一個使用者打到不同副本會看起來像沒登入過。設了 Redis，登入嘗試計數是所有副本共用的
Redis 原子計數器，Redis 連不上時登入會回 5xx。沒設 Redis 時，每個行程各自計數，並在驗證密碼之前
於帳號層級的鎖內把每次嘗試算進去，所以單一副本不會少算，但副本之間看不到彼此的嘗試。

設了 Redis，它也存放 DataProtection 金鑰圈，見下面的「DataProtection 金鑰」。

### S3 相容儲存：`ChecksumMode`

`Struo:Files:S3:ChecksumMode` 預設 `WhenRequired`：S3 用戶端只在操作要求時，才替請求加上 checksum、
才檢查回應的 checksum。這能配合最多種 S3 相容服務。有些服務會拒絕用戶端預設送出的 checksum，上傳就會失敗。
服務的文件說有支援時，`WhenSupported` 會對每個能帶 checksum 的操作都開啟。其他任何值，在
`Struo:Files:Backend` 為 `s3` 時都會讓啟動失敗。症狀見第 15 章。

### 設定 `AllowedHosts`

範本沒有設 `AllowedHosts`，沒設時 ASP.NET Core 的主機過濾接受任何 `Host` 標頭。正式環境把它設成你的主機名稱，
例如 `AllowedHosts=cms.example.com`，多個名稱用 `;` 分隔。後台映像的 nginx 會轉送用戶端的 `Host` 標頭，所以
要列公開的名稱。

主機不在清單內的請求會得到 400。這包括用 pod IP 或其他名稱打 API 的探針：映像自己的 `HEALTHCHECK` 要把
`localhost` 列進去，編排系統的探針要帶一個允許的 `Host` 標頭。

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

這三個檢查是依提供者設定的，放在 `Oidc:Providers:{id}` 底下。隨附檔案裡 `microsoft` 項目的
`AllowedTenantId` 是打不中任何 tenant 的佔位字串，換成真正的 tenant 之前，經由它的外部登入對任何人都會
失敗。`google` 項目沒有 tenant 可釘，隨附時已打開 `RequireEmailVerified`，但啟用前必須設好 `AllowedEmailDomains`：啟用中的 Google 提供者清單為空時，
啟動會失敗。

`RequireEmailVerified`（預設不要求）與 `AllowedEmailDomains`（預設空清單即不限制）都是預設放行的檢查。
正式環境每個啟用的提供者，都該把適用於它的檢查釘死（有 tenant 的提供者釘 tenant，再加上這兩個），
否則只剩下 email 相等這一道關卡。檢查順序見[第 16 章](16-authentication.md)。

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
`ValidateOnStart`：缺 `Database:ConnectionString`、某個啟用的 `Oidc:Providers:{id}` 卻沒給 `ClientId`，
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

`HEALTHCHECK` 每 30 秒打一次 `localhost` 上的 `/health/live`（5 秒逾時、30 秒啟動寬限、3 次
重試）。埠號取自 `ASPNETCORE_HTTP_PORTS`（值是以 `;` 分隔的清單時取第一個，沒設是 8080），
不讀 `ASPNETCORE_URLS`，所以要改埠請用 `ASPNETCORE_HTTP_PORTS`。打的是 liveness，不是
`/health/ready`。`docker ps`／`docker inspect` 看到的健康狀態，因此只代表「行程還在回應」，
不代表資料庫或快取連得上。

### 啟動這個映像至少要給什麼

下面五個環境變數決定這個映像能不能啟動，以及啟動之後怎麼運作；其餘每一個設定鍵的完整語義見
[第 4 章](04-configuration.md)。

- `Database__DbType`——選哪個後端（`PostgreSQL`、`Sqlite`、`MySql`、`SqlServer`、
  `Oracle`）。
- `Database__ConnectionString`——必須設定，映像裡預設帶的是含 `REPLACE_ME` 佔位字串的
  PostgreSQL 連線字串，不能直接拿去用。
- `Database__MigrateOnStartup`（選填）——留空就是 `false`，部署時要另外跑 `migrate`；設成
  `true`，API 啟動時自己套用 migration。
- `Redis__ConnectionString`——留空就是單副本，行程內快取與容器內的金鑰圈都不在副本之間共用。
- `Struo__Files__Backend`——預設 `local`，寫進 `/app/App_Data`（落在宣告的 volume 裡）；要
  用 `s3`，還得配上對應的四個 `Struo__Files__S3__*` 鍵。

這個映像沒有設定 `ASPNETCORE_ENVIRONMENT`，不設就是 ASP.NET Core 的框架預設值
`Production`。這決定了認證 cookie 的安全政策，見上面的正式環境檢查清單。

### DataProtection 金鑰

DataProtection 金鑰圈（簽章、加密認證 cookie 與 antiforgery token 用的）寫在容器內的
`/home/app/.aspnet/DataProtection-Keys`，這個映像沒有把它宣告成 volume，也沒有掛載。

設了 `Redis:ConnectionString` 時，金鑰圈改存在 Redis 的 `DataProtection-Keys` 這個鍵，所有副本讀同一份金鑰。
請求可以落在任何副本，換掉容器也不會讓人被登出。這種情況下上面那個目錄不會被使用。金鑰圈載入之前
都要連得上 Redis，所以啟動時 Redis 掛了，登入會失敗，既有的工作階段也一樣：cookie 解不開，已登入的人會被當成
未登入，直到它恢復。

`DataProtection:ApplicationName`（預設 `StruoCMS`）決定金鑰圈的範圍。同一個部署的每個副本必須用同一個值。
共用同一個 Redis 的不同部署，各自要用不同的名稱，或不同的 Redis 資料庫，以免讀到彼此的金鑰。改了名稱，
所有使用者會被登出一次。

沒有 Redis 時，金鑰留在容器內的金鑰目錄。不持久化它有兩個後果：換掉容器，每一張認證 cookie 與 antiforgery
token 都失效；跑一個以上的副本，每個副本各自有一份金鑰圈，一個請求打到跟核發時不同的副本，cookie 驗證或
antiforgery 會直接失敗。單一副本，把這個路徑掛成 volume 就夠了，金鑰能撐過容器重建；一個以上的副本就要設
`Redis:ConnectionString`。

不論存在哪裡，金鑰都沒有靜態加密：這個專案沒有配置 XML 加密器，Redis 的那個鍵或金鑰圈目錄裡都是可以直接讀的
金鑰材料，要比照其他密鑰保護。限制誰能讀那個 Redis 資料庫，並要求認證。

### 後台 SPA 映像

監聽 `8080`，以非 root 使用者 `nginx` 執行 nginx，提供 Vite 的 production build。跟 API 映像不同，這裡沒有
`HEALTHCHECK`：沒有下游依賴可以探測，要探測就直接打 `/`。

SPA 的 API 位址與預設後台語言（`VITE_API_BASE_URL`、`VITE_DEFAULT_UI_LOCALE`）是建置期就烤進映像
裡的：`vite build` 在 production 模式下會讀 `frontend/.env.production`（有這份檔案的話），
`frontend/.dockerignore` 也刻意不把它排除在建置範圍之外，只排除開發者自己的
`.env`／`.env.local`／`.env.*.local`。儲存庫本身沒有附 `.env.production`，只有追蹤中的
`frontend/.env.example`。改了這些值，要重新建置映像才會生效。

`API_UPSTREAM`（預設 `http://api:8080`）要寫成 `scheme://host:port`，不能帶路徑或結尾斜
線：`proxy_pass` 吃的是一個 nginx 變數，不是字面值，所以容器就算 API 還沒起來也能先啟動，代
價是變數裡任何路徑片段都會取代請求的 URI，而不是被當成前綴接上去。

映像會把 `/api/*` 與 OIDC 路徑 `/signin-oidc`、`/signin-oidc-{id}`、`/signout-callback-oidc-{id}`、`/signout-oidc-{id}` 代理給 API，並把用戶端的 `Host` 標頭（含埠號）
當作 `Host` 與 `X-Forwarded-Host` 轉送，讓 API 看到瀏覽器實際使用的來源。回呼路徑是 OIDC 的重新導向
目標；`/api/` 之外的自訂 `CallbackPath` 要在範本另加一個 `location` 區塊。登記重新導向網址的做法見
第 16 章。

`HSTS_VALUE` 預設空字串，nginx 遇到值是空字串的 `add_header` 就完全不送這個標頭；只有在這裡
或前面的代理已經終結 TLS 的每一個可觸達請求上，才該把它設起來。正式環境建議設，例如
`max-age=31536000`：API 自己不送 HSTS 標頭，而 TLS 可能終結在這個映像前面的代理。不管 `HSTS_VALUE` 怎麼設，
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
表單送出、`<base>` 與外掛都被限制在同一來源或關閉。從 `/api/*` 與這些 OIDC 回呼、登出路徑代理出去的回應改帶
`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; sandbox`，API 自己在那裡送的
政策會被丟掉。

`CSP_IMG_EXTRA` 與 `CSP_CONNECT_EXTRA` 預設空字串，各自接受以空白分隔的來源（`https://cdn.example.com`），
會附加到 `img-src` 與 `connect-src`。圖片從別的來源載入時設 `CSP_IMG_EXTRA`，例如儲存體前面的 CDN，或
`Struo:Files:PresignedRedirect` 為 `true` 而瀏覽器直接向 bucket 取檔時的 presigned URL，或 `Branding:LogoUrl` 是其他主機上的絕對 URL。瀏覽器要呼叫別的來源時
設 `CSP_CONNECT_EXTRA`，例如用 `VITE_API_BASE_URL` 建置進映像、指向不同主機的 API。被擋下的請求會在瀏覽器主控台
顯示 `Content-Security-Policy` 違規。

快取規則：`/assets/` 送
`public, max-age=31536000, immutable`（Vite 的檔名帶內容雜湊）；`index.html` 與其他所有檔案
（例如 `theme-init.js`、`favicon.svg`）送 `Cache-Control: no-cache`。`client_max_body_size` 是
`32m`，高於後端 `Struo:Files:MaxUploadBytes` 的 25 MiB，讓過大的上傳收到後端自己的 JSON 錯
誤信封，而不是 nginx 的 HTML 錯誤頁。

`NGINX_RESOLVER`（預設 `127.0.0.11`，Docker 內建 DNS，5 秒逾時）只有在使用者自訂的 Docker
network 上才有意義；掛在預設的 bridge network 上，每一個 `/api/*` 請求都會在解析逾時之後收
到 502。在 Kubernetes 上要設成叢集的 DNS，例如 `NGINX_RESOLVER=kube-dns.kube-system.svc.cluster.local`；
`127.0.0.11` 是 Docker 的，pod 裡沒有東西在那裡監聽。nginx 的 `resolver` 不會套用 pod 的搜尋網域，所以 `API_UPSTREAM` 要用完整的服務名稱，
例如 `http://api.<namespace>.svc.cluster.local:8080`。

要讓 SPA 部署在跟 API 不同的來源，設定 `VITE_API_BASE_URL` 的方式與生效時機，見
[第 19 章：後台客製化](19-admin-customization.md)；建這個映像時，值要放在你自己新增並提交的
`frontend/.env.production`，開發者自己的 `frontend/.env` 不會進到映像裡。不用這個映像、改把 SPA 放在
靜態託管服務的做法，見下面的〈後台放在靜態託管服務〉。

### 映像之外還要自己做的事

映像只負責把應用程式跑起來。終結 TLS、API 前有代理時設定 `ForwardedHeaders`、重啟策略、水平擴展、密鑰
注入，以及把 `/health/live` 與 `/health/ready` 接到編排系統自己的探針上，都要自己接。

### 後台放在靜態託管服務

後台 SPA 只是一包靜態檔案，也可以不用上面的映像，改放在靜態託管服務，API 另外部署。

**支援的前提**：後台與 API 在同一個主網域底下，只差子網域，例如 `https://admin.example.com` 與
`https://api.example.com`。這兩個來源屬於同一個 site，session cookie 的 `SameSite=Lax` 會隨 SPA 的請求送出。

跨主網域不支援：例如後台在託管服務給的 `*.pages.dev` 網址，API 在 `example.com`。這時 SPA 對 API 的請求是
跨站請求，瀏覽器不會送出 `SameSite=Lax` 的 session cookie，登入會失敗，`<img>` 載入的媒體與縮圖也會失效。
請替後台綁一個與 API 同主網域的自訂網域。

設定的步驟：

1. 建置後台時設 `VITE_API_BASE_URL=https://api.example.com/api`。API 的路由掛在 `/api` 底下，
   所以值要帶 `/api`。它是建置期的變數，見[第 19 章：後台客製化](19-admin-customization.md)。
2. API 設 `Struo:Cors:AllowedOrigins`，列出後台的來源，例如 `["https://admin.example.com"]`。
   每一筆要寫成精確的來源：scheme、主機，非預設埠才加埠號。結尾有斜線、帶路徑或萬用字元的項目不會
   與任何來源相符。設了之後 cookie 一律 `Secure`，兩邊都得走 HTTPS，見
   [第 4 章：設定參考](04-configuration.md)。
3. OIDC 的重新導向網址登記在 API 的網域：`https://api.example.com` 加上提供者的回呼路徑，也就是它的
   `CallbackPath`，沒設時是 `/signin-oidc-{id}`（例如 `https://api.example.com/signin-oidc-google`）。
   後台的網域不需要登記。細節見[第 16 章](16-authentication.md)。
4. 單一登入的 `returnUrl` 可以是後台的完整網址，只要它的來源在 `Struo:Cors:AllowedOrigins` 裡。
   SPA 發現 API 與頁面不同來源時會自動這樣送；登入失敗也會回到後台的 `/login`。

靜態託管服務不會送 nginx 映像送的標頭，要自己補上。下面以 Cloudflare Pages 的 `_headers`
檔案為例（其他服務有各自的設定方式）。放在建置輸出的根目錄（用 Vite 時，放進 `public/` 的檔案會被複製到那裡）：

```text
# _headers
/*
  X-Content-Type-Options: nosniff
  X-Frame-Options: DENY
  Referrer-Policy: strict-origin-when-cross-origin
  Strict-Transport-Security: max-age=31536000
  Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://api.example.com; connect-src 'self' https://api.example.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'
  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
/assets/*
  Cache-Control: public, max-age=31536000, immutable
```

這些值與映像的 nginx 範本一致，差別只在 CSP 的 `img-src` 與 `connect-src` 多了 API 的來源。
把 `https://api.example.com` 換成你的 API 來源；圖片若還有別的來源（CDN、`Struo:Files:PresignedRedirect`
的 presigned URL），也加到 `img-src`。HSTS 要等兩個網域都確定只走 HTTPS 才送。

`/assets/` 以外的檔案（HTML、`theme-init.js` 與每一條 SPA 路由）不從這個檔案取得 `Cache-Control`。
Cloudflare Pages 預設送 `public, max-age=0, must-revalidate`，每次請求都會重新驗證，效果與映像的
`no-cache` 相同。換到其他服務時，要確認 HTML 不會被長期快取。

SPA 用的是前端路由，直接開 `/collections/posts` 這類網址時，伺服器要回 `index.html`。在 Cloudflare
Pages 上，建置輸出裡不放 `404.html` 就會有這個行為；其他服務要查它們的「單頁應用程式」設定。

### 兩個映像一起驗證

下面五道指令建一個使用者自訂的 network 與一個 volume，先對 volume 裡的 SQLite 資料庫跑
`migrate`，再起兩個容器讓彼此看得到對方；這是驗證步驟，不是建議的正式環境拓樸。本章最後的本機
Compose 環境會把兩個映像連同 PostgreSQL、Redis 一起起來：

```
docker network create struo-verify
docker volume create struo-verify-data
docker run --rm --volume struo-verify-data:/app/App_Data --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local migrate
docker run --detach --name api --network struo-verify --volume struo-verify-data:/app/App_Data --env Database__DbType=Sqlite --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' struo-api:local
docker run --detach --name admin --network struo-verify --publish 8081:8080 struo-admin:local
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

資料庫檢查不會等驅動程式的連線逾時。資料庫在 `Database:ReadinessTimeoutSeconds`（預設 `3`，範圍 1 到 60）
內沒有回應時，`/health/ready` 回 503。

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
  份 `Local:RootPath` 那個目錄，`s3` 模式靠 S3 相容儲存桶自己的版本控制或複寫，`custom` 則備份你的儲存實際寫入的地方；只備份資料
  庫，會悄悄漏掉每一個上傳過的檔案。
- **Redis 不需要**——Redis（或行程內的替代品）裡只有短命狀態：cookie 工作階段的 ticket、帳
  號登入嘗試計數、工作階段撤銷紀錄。丟了它只會逼每個已登入的使用者重新登入，並把登入計數歸
  零，它不是權威來源，不需要另外備份。
- **Migration 與設定**——fork 自己的 migration 類別、`appsettings.*`、環境變數或密鑰管理系統
  裡的值，是一般的原始碼或部署管線產物，跟其餘部署一起備份即可。

## 清除孤兒 blob

沒有任何 `files` 資料列指向某個儲存鍵的 blob 就是孤兒，例如清除檔案時儲存層出錯，留下了位元組。
`files:sweep` 會在 `Struo:Files:Backend` 選的那個後端裡找出它們，需要時再刪掉：

```text
$ dotnet run --project src/Struo.Api -- files:sweep
storage:  local, root /srv/struo/uploads
database: PostgreSQL, database struo
orphan 2026/10/00000000000000000000000000000001.png
1 of 4 eligible blobs (25%) are orphans; 1 younger than 24 hour(s) skipped. Dry run: nothing was deleted; pass --apply to delete.
```

::: warning 加 `--apply` 之前，先確認兩個目標
這個命令會對設定指向的任何儲存與資料庫動手，包括你忘了還在生效的 `appsettings.Development.json`
或環境變數。看清楚最前面兩行 `storage:` 與 `database:`，確認它們就是你要清理的那一組。相對路徑的 `Local:RootPath` 會以你執行命令時所在的目錄為基準，
`storage:` 那一行會顯示解析後的完整路徑。自訂儲存時，那一行是該儲存自己的描述。
:::

命令一開始一定先印這兩行：儲存後端與它的目標（本機的根目錄路徑，或 S3 的儲存桶與服務主機），
以及資料庫的提供者與資料庫名稱。憑證與連線字串一律不會印出來。

不加 `--apply` 就是試跑：每個孤兒印一行 `orphan <key>`，最後一行是摘要，什麼都不刪。看過清單，
再加上 `--apply` 重跑才會刪除。

| 選項 | 意義 |
|---|---|
| `--apply` | 刪除孤兒；不加就什麼都不刪 |
| `--force` | 孤兒超過一半時，讓 `--apply` 照刪 |
| `--min-age-hours N` | 略過不到 `N` 小時的 blob（預設 `24`） |

`N` 是 `0` 到 `87600` 的整數。寬限期是保護「blob 已經存了、資料列還沒寫入」的上傳。只要有任何一列
`files` 參照某個 blob，它就不是孤兒，垃圾桶裡的列也算。要先清除資料列，blob 才會變成孤兒。儲存端
沒有回報修改時間的 blob 視為剛上傳，絕不會被刪。

保險起見，當符合資格的 blob（鍵的格式正確、且超過寬限期）有超過 50% 是孤兒時，`--apply` 會拒絕刪除。
這種比例通常代表設定指到了錯的儲存或資料庫，而不是媒體庫真有一半是垃圾。拒絕時會印出數量與兩個
目標，並以 `3` 結束；兩邊都確認過之後，再加 `--force` 才會照刪。試跑也會顯示同樣的比例，並指出
`--apply` 何時需要 `--force`。

只有形如 `{yyyy}/{MM}/{32 位十六進位}{副檔名}` 的鍵會被考慮，所以變體快取或其他放在儲存根目錄的
檔案不會被動到。列舉是串流的（S3 用 `ListObjectsV2` 分頁，本機後端惰性走訪根目錄），資料庫查詢
也分批進行，儲存量再大也不必整批載入記憶體。`--apply` 不加 `--force` 時會列舉儲存兩次：一次為保險機制
計數，一次才真的刪除。

跟 migration 命令一樣，它讀的是 API 的設定，所以 S3 或本機的設定與連線字串都要就位。結束代碼
`0` 是成功，`1` 是失敗（儲存或資料庫連不上，或某次刪除失敗；其餘的刪除仍會執行），`2` 是用法錯誤，
`3` 是被 50% 保險機制拒絕。

## 本機的 Compose 環境

`docker compose up -d --build` 用上面兩份 Dockerfile 起 `postgres`、`redis`、`api`、`admin` 四個
服務，怎麼啟動、怎麼登入見[第 3 章：快速開始](03-getting-started.md)。它是開發與測試用的環境：
`api` 以 Production 執行，每次啟動先自己跑 migration（`Database__MigrateOnStartup`）。上正式
環境，照本章前面的檢查清單自己組部署。

PostgreSQL 的密碼是固定的開發值，Redis 沒有密碼。每個埠預設只綁在 `127.0.0.1`，只有本機連得到
資料庫、Redis、API 跟後台。`STRUO_BIND_ADDRESS` 決定綁定的位址；設成 `0.0.0.0` 會把埠開在所有
網路介面上，同一個網路裡的其他機器就連得到，固定的開發密碼與沒有密碼的 Redis 也跟著公開。
只在信得過的網路上這樣做。

`api` 服務開出它的埠（`STRUO_API_PORT`，預設 8081）只是為了本機除錯。把 `STRUO_BIND_ADDRESS` 改成
非 loopback 時，也要把 `docker-compose.yml` 裡 `api` 的 `ports` 對應拿掉：照前面檢查清單的建議，
從後台 nginx 的 `/api/` 連 API，不要直接開出去。

健康檢查端點在 API 的埠上：`/health/live` 與 `/health/ready`，預設是
`http://localhost:8081/health/live`（`STRUO_API_PORT` 可改）。後台的 nginx 只轉發 `/api/*`，
從後台的埠打 `/health/...` 拿到的是 SPA 頁面。Compose 替 `api` 另外設了每 5 秒一次的健康檢查，
`docker compose up --wait` 不必等映像預設的 30 秒間隔。映像的健康檢查與 Compose 的探測都從
`ASPNETCORE_HTTP_PORTS` 讀埠號（沒設就是 8080；值是以 `;` 分隔的清單時取第一個）。

這個環境設了 `Redis__ConnectionString`，所以 DataProtection 金鑰圈存在它的 `redis` 服務裡，沒有加密，
為檔案位置掛上的 volume `struo-dpkeys` 用不到。`redis` 服務沒有 volume：重建它會丟掉金鑰圈，連同所有
工作階段。開發與測試這樣就夠了；正式環境要照上面「DataProtection 金鑰」一節，自己保護金鑰。

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
