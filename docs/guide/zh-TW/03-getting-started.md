# 3. 快速開始

這一章把 API 與管理後台跑起來，並用植入的管理員帳號登入一次：可以用一道 Docker Compose 指令起整套服務，
也可以只起需要的服務，其餘在本機自己跑。

## 先決條件

用 Docker Compose 起整套服務，只需要 Docker 加上 Compose；下面其餘的工具，是在本機自己跑 API
與後台時才用得到。

需要準備好這些工具：

- .NET SDK 10.0.x。
- Node.js 24（CI 用的版本）。
- pnpm 10.x，由 corepack 依 `packageManager` 欄位自動套用，不用自己指定版本。
- Docker 加上 Compose，或自己架 PostgreSQL 與 Redis；自架的話，第 2 步的連線字串要換成對應的值。

## 用 Docker Compose 起整套服務

這是最簡單的啟動服務進行開發及測試的方式：

```bash
docker compose up -d --build
```

這會建置 API 與後台兩個映像，起 `postgres`、`redis`、`api`、`admin` 四個服務。第一次啟動時，
API 會跑完資料庫 migration，並植入管理員帳號，密碼是隨機產生的。

等它們都起來：

```bash
docker compose ps
```

`postgres`、`redis`、`api` 的狀態要是 `healthy`；`admin` 沒有健康檢查，顯示 `Up` 就好。也可以
改用 `docker compose up -d --build --wait`，全部就緒才回到命令列。

打開 `http://localhost:8080`，用 `admin@admin.com` 登入。密碼在第一次啟動時隨機產生，只會在 `api` 的
log 印出一次：

```bash
docker compose logs api
```

找開頭是 `Bootstrap admin created` 的那筆 Warning。API 本身在 `http://localhost:8081`。

對外的埠預設是後台 8080、API 8081、PostgreSQL 5432、Redis 6379。被佔用的話，複製
`.env.example` 成 `.env`，用 `STRUO_ADMIN_PORT`、`STRUO_API_PORT`、`STRUO_PG_PORT`、
`STRUO_REDIS_PORT` 覆寫；改的只是主機這一側，服務之間照樣用服務名稱互連。

這些埠預設只綁在 `127.0.0.1`，只有本機連得到。要讓網路上的其他機器連進來，把
`STRUO_BIND_ADDRESS` 設成 `0.0.0.0`；固定的開發密碼與沒有密碼的 Redis 也會跟著公開。

`api` 以 Production 環境執行，session cookie 帶 `Secure`。瀏覽器把 `http://localhost` 當成安全
來源，所以本機登入沒問題；用其他主機名稱連進來，前面要先架一層 HTTPS。

這個帳號只在第一次啟動、資料庫還是空的時候植入。要自己指定帳號密碼，第一次啟動前在 `.env`
設 `STRUO_ADMIN_EMAIL`／`STRUO_ADMIN_PASSWORD`；指定的密碼必須通過密碼原則，而且不能是 `admin`。
這個帳號與資料庫的帳密都只給本機開發用。

`docker compose down` 會移除容器，資料庫與上傳的檔案留在 volume 裡；`docker compose down -v`
連 volume 一起刪，下次從空資料庫開始。Compose 沒有替 Redis 掛具名 volume，session 跟著消失，
所以 `down` 再 `up` 之後要重新登入；只重啟 API（`docker compose restart api`），登入狀態還在。

## 自己起需要的服務

做開發或有特殊需求時，只起你需要的服務，其餘自己跑。下面六個步驟只用 Compose 起 PostgreSQL
與 Redis，API 與管理後台在本機執行。

### 1. 啟動 PostgreSQL 與 Redis

```bash
docker compose up -d postgres redis
```

會起 `postgres` 與 `redis` 兩個服務。確認它們都通過健康檢查：

```bash
docker compose ps
```

狀態要是 `healthy`，只有 `running` 還不算好。

容器用的是開發專用帳密（`struo`／`struo`／`struo`），下一步的設定檔直接沿用。

對外的埠預設是 PostgreSQL 5432、Redis 6379；如果本機已經有服務佔用這兩個埠，複製
`.env.example` 成 `.env`，用 `STRUO_PG_PORT`、`STRUO_REDIS_PORT` 覆寫即可。改了
`STRUO_PG_PORT` 的話，下一步連線字串裡的 `Port=` 也要跟著改。

### 2. 設定 API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

複製過來就能直接用：連線字串與 `Redis:ConnectionString` 都對上上一步容器的預設值。
`appsettings.Development.json` 有列進 `.gitignore`，不會被提交。

任何一個設定鍵都可以用環境變數覆寫，把 `:` 換成 `__`，例如 `Database__ConnectionString`；例
外是測試用的四個 `Testing:*Connection` 鍵，各自只認一個 `STRUO_TEST_*_CONNECTION` 環境變數，見
[第 4 章](04-configuration.md)最後一節。

### 3. 建立資料表與初始資料

```bash
dotnet run --project src/Struo.Api -- migrate
```

`migrate` 依序套用 migration：先建框架資料表，再植入語言、`admin` 與 `public` 兩個角色，以及管理員
帳號。輸出前段是 FluentMigrator 每一步的紀錄，最後每套用一支印一行 `Applied`，結束代碼
`0`：

```text
$ dotnet run --project src/Struo.Api -- migrate
[16:47:28 INF] Beginning Transaction
[16:47:28 INF] Rolling back transaction
[16:47:28 INF] VersionMigration migrating
[16:47:28 INF] Beginning Transaction
…
[16:47:28 INF] 202610080001: SeedCoreData migrated
[16:47:28 INF] => 0.0019894s
[16:47:28 WRN] Bootstrap admin created: admin@admin.com with the generated password Yo43Ui4K7N3WJB92vKVIxFL8. It is shown only once; change it after the first login.
Applied 202610080000  CreateCoreSchema
Applied 202610080001  SeedCoreData
```

這一步不能省：複製過來的設定檔把 `Database:MigrateOnStartup` 設成 `false`，資料庫還有待套用的
migration 時，API 會拒絕啟動。也可以把它改成 `true`，讓 API 每次啟動時自己套用；六個命令與啟動
時的檢查見[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)。

資料庫已經被前面的 Compose 整套服務建好時（兩者共用同一個 PostgreSQL volume），`migrate` 只印
`Nothing to migrate.`。

### 4. 執行 API

```bash
dotnet run --project src/Struo.Api
```

預設走 `http` profile：監聽 `http://localhost:5221`，`ASPNETCORE_ENVIRONMENT` 是
`Development`。

啟動成功會看到：

```text
[18:30:38 INF] Now listening on: http://localhost:5221
[18:30:38 INF] Application started. Press Ctrl+C to shut down.
[18:30:38 INF] Hosting environment: Development
…
```

`MigrateOnStartup` 是 `false` 時，這幾行前面沒有任何資料庫的紀錄；設成 `true` 時，前面是
FluentMigrator 的紀錄，加上每套用一支 migration 一行 `Applied migration …`。

啟動失敗時，log 會印一筆 Fatal，並以結束碼 1 結束。漏了上一步，訊息是這樣（堆疊追蹤省略）：

```text
[18:30:49 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 2 migration(s) pending, the lowest is 202610080000; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

另開一個終端機確認 API 有回應：

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
```

```text
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-10-12T10:30:38.9562717Z"}}
HTTP_STATUS:200
```

### 5. 執行管理後台

回到剛才跑 `curl` 的那個終端機：

```bash
cd frontend
pnpm install
pnpm dev
```

啟動成功會看到：

```text
…
  VITE v8.3.2  ready in 299 ms

  ➜  Local:   http://127.0.0.1:5173/
```

橫幅印的是 `127.0.0.1`（dev server 綁在 IPv4），打 `http://localhost:5173` 一樣連得到。

開發伺服器把 `/api` 轉發到 `http://localhost:5221`，後台與 API 在瀏覽器眼裡是同一個來源，開
發環境不需要另外設定 CORS。它也會轉發每個提供者的 OIDC 回呼與登出路徑；要試外部
登入的話，向提供者登記 `http://localhost:5173` 加上該提供者的回呼路徑：Entra 是
`http://localhost:5173/signin-oidc`，Google 是 `http://localhost:5173/signin-oidc-google`（[第 16 章](16-authentication.md)）。

### 6. 第一次登入

打開 `http://localhost:5173`，用 `admin@admin.com` 與第 3 步 `migrate` 印出的密碼登入。它是 `migrate`
輸出裡的一筆 Warning，只印一次，之後不會再出現。

登入後先把密碼改掉。

這組帳密由第 3 步的 `migrate` 植入，只在空資料庫上植入一次。要自己指定帳號密碼，在跑
`migrate` 之前先設 `Auth__BootstrapAdmin__Email`／`Auth__BootstrapAdmin__Password`；植入之後，
只能登入後台改。

## 你會看到什麼

登入後側欄只有 System 群組，沒有任何內容集合——這是預期中的狀態，原因在[第 1 章：StruoCMS
是什麼](01-what-is-struocms.md)說過了。

同樣可以打 API 的 `/health/live`（行程活著就回 200，不跑任何檢查）與 `/health/ready`（資料庫
與快取都通過才回 200）。這兩個端點在 API 自己的埠上：自己跑是 `http://localhost:5221`，用
Compose 是 `http://localhost:8081`；從後台的埠（8080）打，拿到的是 SPA 頁面。

`/health/ready` 有一個要注意的地方：快取檢查在沒設定
`Redis:ConnectionString` 時，會改用記憶體內的暫存，一樣會通過，所以這個綠燈不能當成 Redis
真的連得上的證明。

API 文件在 `/scalar`，原始的 OpenAPI 規格在 `/openapi/v1.json`；兩個路由都只在 Development
環境開放，其他環境（含 Production）回 404，除非 `OpenApi:Enabled` 設為 `true`。這兩個路由沒有任何驗證就公開整份 API 規格；如果你要在
Development 之外打開它們，前面要自己加一層驗證。

## 接下來

跑起來之後，下一步是定義你自己的第一個內容集合，這留到後面專門介紹定義集合的章節。設定的完
整參考在[第 4 章：設定參考](04-configuration.md)。
