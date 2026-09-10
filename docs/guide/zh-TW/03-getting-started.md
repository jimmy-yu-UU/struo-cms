# 3. 快速開始

跟著以下五個步驟，十分鐘內就能把 API 與管理後台跑起來，並用預設帳號登入一次。

## 先決條件

需要準備好這些工具：

- .NET SDK 10.0.x，版本由 `global.json` 鎖定。
- Node.js 24.x。
- pnpm 10.x——`frontend/package.json` 用 `packageManager` 欄位把版本鎖在 10.33.2，由 corepack
  套用；Node 本身在這個 repo 裡沒有鎖版本，只有 CI 用的是 24。
- Docker 加上 Compose，或者自架 PostgreSQL 與 Redis；自架的話，下一步的連線字串要換成對應的值。

## 1. 啟動 PostgreSQL 與 Redis

```bash
docker compose up -d
```

這個指令只會啟動兩個容器，`struo-postgres` 與 `struo-redis`，這是預設設定唯一需要的兩個服
務。兩個容器都宣告了健康檢查，`docker compose ps` 顯示的狀態會是 `healthy`，不只是
`running`。

PostgreSQL 容器內建的是本機開發專用的帳密：資料庫 `struo`、使用者 `struo`、密碼 `struo`，下
一步的設定檔會直接沿用這組值。

對外的埠預設是 PostgreSQL 5432、Redis 6379；如果本機已經有服務占用這兩個埠，複製
`.env.example` 成 `.env`，用 `STRUO_PG_PORT`、`STRUO_REDIS_PORT` 覆寫即可。

## 2. 設定 API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

這個檔案本身被 git 忽略，內容是帶註解的 JSON，透過 ASP.NET Core 標準的設定機制載入。裡面的連
線字串（`Host=localhost;Port=5432;Database=struo;Username=struo;Password=struo`）與
`Redis:ConnectionString`（`localhost:6379`）都對應上一步容器的預設值，複製後不用改就能直接
執行。

任何一個設定鍵都可以用環境變數覆寫，把 `:` 換成 `__`，例如 `Database__ConnectionString`。

## 3. 執行 API

```bash
dotnet run --project src/Struo.Api
```

這個指令套用的是 `http` 這個 launch profile，監聽位址與環境變數都由它決定：監聽
`http://localhost:5221`，`ASPNETCORE_ENVIRONMENT` 是 `Development`。另外還有一個 `https`
profile，同時監聽 `https://localhost:7031` 與 `http://localhost:5221`，快速上手用不到它。

啟動成功會看到：

```text
[11:38:30 INF] Now listening on: http://localhost:5221
[11:38:30 INF] Application started. Press Ctrl+C to shut down.
[11:38:30 INF] Hosting environment: Development
```

針對一個空白資料庫第一次啟動時，這三行前面還會多出建立資料表與植入資料的紀錄；資料表已經存
在時，這三行就是全部輸出。啟動失敗時（例如某個設定選項沒通過驗證），記錄會印一筆 Fatal，行
程以結束碼 1 結束，讓外層的監控機制能偵測到啟動失敗。

另開一個終端機確認 API 有回應：

```bash
curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
```

```text
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-09-10T03:39:30.3332812Z"}}
HTTP_STATUS:200
```

同樣可以打 `/health/live`（行程活著就回 200，不跑任何檢查）與 `/health/ready`（資料庫與快取
都通過才回 200）。`/health/ready` 有一個要注意的地方：快取檢查在沒設定 `Redis:ConnectionString`
時，會改用記憶體內的暫存，一樣會通過，所以這個綠燈不能當成 Redis 真的連得上的證明。

## 4. 執行管理後台

另開一個終端機：

```bash
cd frontend
pnpm install
pnpm dev
```

`dev` 這個 script 執行的是 `vite`，開發伺服器監聽埠 5173，並且明確綁定 IPv4 的
`127.0.0.1`，所以啟動橫幅印出來的是 `127.0.0.1`；瀏覽器打開 `http://localhost:5173` 一樣連
得到。啟動成功會看到：

```text
  VITE v8.2.0  ready in 276 ms
  ➜  Local:   http://127.0.0.1:5173/
```

開發伺服器把 `/api` 轉發到 `http://localhost:5221`，後台與 API 在瀏覽器眼裡是同一個來源，開
發環境不需要另外設定 CORS。

## 5. 第一次登入

打開 `http://localhost:5173`，用預設帳號登入：`admin@admin.com` / `admin`。

這組帳密只在 `users` 資料表第一次建立時植入，資料表已經存在時不會重新建立或重設。登入後先把
密碼改掉：如果用 Production 環境啟動、密碼還是預設值，記錄只會寫一筆警告，不會擋下啟動，警
告不能取代你自己動手改密碼。

## 你會看到什麼

登入後看到的後台側欄是空的，這是預期中的狀態，原因在[第 1 章](01-what-is-struocms.md)已經說
過。

API 文件在 `/scalar`，是套用 Mars 主題的互動式 API 文件頁面；原始的 OpenAPI 規格在
`/openapi/v1.json`。兩個路由都只在非 Production 環境開放，Production 環境兩個都回應 404。

## 接下來

跑起來之後，下一步是定義你自己的第一個內容集合，這部分留到後面專門介紹如何定義集合的章節；
宣告好集合類別後，記得把它所在的組件名稱加進 `Struo:ContentAssemblies`，host 掃描不到的組
件，啟動會直接失敗。設定的完整參考在[第 4 章：設定參考](04-configuration.md)。
