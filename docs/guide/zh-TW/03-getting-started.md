# 3. 快速開始

跟著以下五個步驟，十分鐘內就能把 API 與管理後台跑起來，並用預設帳號登入一次。

## 先決條件

需要準備好這些工具：

- .NET SDK 10.0.x。
- Node.js 24（CI 用的版本）。
- pnpm 10.x，由 corepack 依 `packageManager` 欄位自動套用，不用自己指定版本。
- Docker 加上 Compose，或自己架 PostgreSQL 與 Redis；自架的話，下一步的連線字串要換成對應的值。

## 1. 啟動 PostgreSQL 與 Redis

```bash
docker compose up -d
```

會起 `struo-postgres` 與 `struo-redis` 兩個容器。確認它們都通過健康檢查：

```bash
docker compose ps
```

狀態要是 `healthy`，只有 `running` 還不算好。

容器用的是開發專用帳密（`struo`／`struo`／`struo`），下一步的設定檔直接沿用。

對外的埠預設是 PostgreSQL 5432、Redis 6379；如果本機已經有服務佔用這兩個埠，複製
`.env.example` 成 `.env`，用 `STRUO_PG_PORT`、`STRUO_REDIS_PORT` 覆寫即可。改了
`STRUO_PG_PORT` 的話，下一步連線字串裡的 `Port=` 也要跟著改。

## 2. 設定 API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

複製過來就能直接用：連線字串與 `Redis:ConnectionString` 都對上上一步容器的預設值。
`appsettings.Development.json` 有列進 `.gitignore`，不會被提交。

任何一個設定鍵都可以用環境變數覆寫，把 `:` 換成 `__`，例如 `Database__ConnectionString`；唯
一的例外是 `Testing:PostgresConnection`，它只認 `STRUO_TEST_PG_CONNECTION`。

## 3. 執行 API

```bash
dotnet run --project src/Struo.Api
```

預設走 `http` profile：監聽 `http://localhost:5221`，`ASPNETCORE_ENVIRONMENT` 是
`Development`。

啟動成功會看到：

```text
[11:38:30 INF] Now listening on: http://localhost:5221
[11:38:30 INF] Application started. Press Ctrl+C to shut down.
[11:38:30 INF] Hosting environment: Development
```

這三行是輸出的尾段：資料表已經存在時，前面還有一行 `DatabaseInitializer` 說 schema 已經是最
新的，加上三行 `DataSeeder: skip …`。針對空白資料庫第一次啟動，前面則是建立資料表與植入資料
的紀錄。

啟動失敗時，log 會印一筆 Fatal，並以結束碼 1 結束。

另開一個終端機確認 API 有回應：

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
```

```text
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-09-10T03:39:30.3332812Z"}}
HTTP_STATUS:200
```

## 4. 執行管理後台

回到剛才跑 `curl` 的那個終端機：

```bash
cd frontend
pnpm install
pnpm dev
```

啟動成功會看到：

```text
  VITE v8.2.0  ready in 276 ms
  ➜  Local:   http://127.0.0.1:5173/
```

橫幅印的是 `127.0.0.1`（dev server 綁在 IPv4），打 `http://localhost:5173` 一樣連得到。

開發伺服器把 `/api` 轉發到 `http://localhost:5221`，後台與 API 在瀏覽器眼裡是同一個來源，開
發環境不需要另外設定 CORS。

## 5. 第一次登入

打開 `http://localhost:5173`，用預設帳號登入：`admin@admin.com` / `admin`。

登入後先把密碼改掉。

如果用 Production 環境啟動、密碼還是預設值，log 只會留下一筆警告，指名
`Auth__BootstrapAdmin__Password`，不會擋下啟動。

這組帳密只在 `users` 資料表第一次建立時植入；要用別的帳號密碼開站，在第一次啟動前先設
`Auth__BootstrapAdmin__Email`／`Auth__BootstrapAdmin__Password`，第一次啟動之後，只能登入
後台改。

## 你會看到什麼

登入後側欄只有 System 群組，沒有任何內容集合——這是預期中的狀態，原因在[第 1 章：StruoCMS
是什麼](01-what-is-struocms.md)說過了。

同樣可以打 `/health/live`（行程活著就回 200，不跑任何檢查）與 `/health/ready`（資料庫與快取
都通過才回 200）。`/health/ready` 有一個要注意的地方：快取檢查在沒設定
`Redis:ConnectionString` 時，會改用記憶體內的暫存，一樣會通過，所以這個綠燈不能當成 Redis
真的連得上的證明。

API 文件在 `/scalar`，原始的 OpenAPI 規格在 `/openapi/v1.json`；兩個路由都只在非 Production
環境開放，Production 回 404。這兩個路由沒有任何驗證就公開整份 API 規格；如果你要在
Production 打開它們，前面要自己加一層驗證。

## 接下來

跑起來之後，下一步是定義你自己的第一個內容集合，這留到後面專門介紹定義集合的章節。設定的完
整參考在[第 4 章：設定參考](04-configuration.md)。
