# 2. 快速入門

本章從頭到尾啟動這個樣板——完全依出貨狀態、零個內容集合 (collection):相依服務、設定、API、管理後台 SPA，
以及第一次登入。以下每一道指令都已在撰寫本章時，於此份 checkout 上成功執行過。

## 先決條件

| 需求 | 版本 | 備註 |
|---|---|---|
| .NET SDK | 10.0.x (見 `global.json`，`rollForward: latestMinor`) | `dotnet --version` |
| Node.js | 24.x | `node --version`;與 CI 固定使用的版本相符 |
| pnpm | 10.x | `pnpm --version`;與 CI 固定使用的版本相符 |
| Docker (with Compose) | 任何近期版本 | 在本機開發環境執行 PostgreSQL 與 Redis |

## 1. 啟動相依服務

```bash
docker compose up -d
```

這只會啟動 **PostgreSQL 與 Redis**——出貨預設設定所需要的這兩項服務。用 `docker compose ps` 確認過:
在一台乾淨的機器上，它會恰好列出 `struo-postgres` 與 `struo-redis`，兩者皆為 healthy。

預設的檔案儲存後端是本機磁碟，所以 S3 相容的 MinIO 服務不會被單純的 `up -d` 啟動——它藏在一個 Compose
profile 之後:

```bash
docker compose --profile s3 up -d
```

這會額外啟動 `struo-minio`，以及一個一次性的 `struo-createbuckets` 容器，該容器會建立 `struo-media`
bucket 後以 exit 0 結束。單純的 `docker compose ps` 不會列出已經結束的一次性容器——用
`docker compose ps -a` 才能看到它顯示為 `Exited (0)`。只有當你打算把 `Struo:Files:Backend` 設為
`s3` 時才需要 `s3` profile (第 3 章完整涵蓋此鍵);下面的走查使用本機磁碟這個預設值，不需要它。

Host port 預設為慣用值——PostgreSQL `5432`、Redis `6379`、MinIO `9000`/`9001`——若你機器上這些
port 已被佔用，可透過 `.env` (複製已提交的 `.env.example`) 用
`STRUO_PG_PORT`/`STRUO_REDIS_PORT`/`STRUO_MINIO_PORT`/`STRUO_MINIO_CONSOLE_PORT` 逐服務覆寫。

## 2. 設定 API

```bash
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json
```

`appsettings.Development.json` 是 **gitignored** 的——本機、不提交的設定 (資料庫憑證、Redis
位址) 就放在這裡。`.example` 檔案是帶註解的 JSON;它能透過真正的 ASP.NET Core 設定提供者正確載入，
其預設連線字串 (`Host=localhost;Port=5432;...`) 與 `docker-compose.yml` 的慣用 port 相符 (複製後
即為如此)。

如果你在步驟 1 (透過 `.env`) 重新映射了任何 port，請更新 `appsettings.Development.json` 中對應的
連線字串，或不編輯檔案、只在這一次執行時覆寫它:

```bash
Database__ConnectionString="Host=localhost;Port=<your-port>;Database=struo;Username=struo;Password=struo" \
Redis__ConnectionString="localhost:<your-port>" \
dotnet run --project src/Struo.Api
```

`appsettings.json` 中的每一個鍵都能用這種方式覆寫，以雙底線做為路徑分隔符號——第 3 章會完整記載唯一
的例外 (`Testing:PostgresConnection`) 以及其他每一個鍵。

## 3. 執行 API

```bash
dotnet run --project src/Struo.Api
```

`dotnet run` 會自動套用 `http` launch profile，因此在 `Development` 環境下，host 會監聽
`http://localhost:5221`——已針對此份 checkout 執行並確認過:

```
[INF] Now listening on: http://localhost:5221
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Development
```

第一次針對空資料庫啟動時，`users`、`roles` 及其他框架資料表會被建立並植入種子資料 (見步驟 5)。之後
每一次啟動，既有的資料表都會維持不變。

## 4. 執行管理後台 SPA

在第二個終端機視窗:

```bash
cd frontend
pnpm install
pnpm dev
```

已確認的輸出:

```
VITE v8.1.2  ready in 180 ms
➜  Local:   http://127.0.0.1:5173/
```

Vite 的開發伺服器明確綁定 IPv4 loopback (`frontend/vite.config.ts` 的 `server.host`)，因此啟動橫幅印出
的是 `127.0.0.1` 而非 `localhost`——這繞開了一個 Windows 特有的陷阱: "localhost" 有時會優先解析為
IPv6 loopback，導致 Chromium 系瀏覽器連不到 IPv4 位址。在瀏覽器中開啟 `http://localhost:5173` 仍然
可以正常存取 SPA，差別只在啟動時印出的橫幅。Vite 的開發伺服器將 `/api` 請求代理 (proxy) 到
`http://localhost:5221` (`frontend/vite.config.ts`)，因此 SPA 與 API 可以直接搭配使用，不需要任何
跨來源 (cross-origin) 設定。在瀏覽器中開啟 `http://localhost:5173`。

## 5. 第一次登入

用 bootstrap 管理員帳號登入:

- **電子郵件:** `admin@admin.com`
- **密碼:** `admin`

這組帳號**只會在 `users` 資料表第一次被建立時**植入種子資料——之後即使該資料表被清空，重新啟動也
不會重新建立或重設它。若要在全新資料庫上覆寫植入的憑證，請在第一次啟動之前設定
`Auth:BootstrapAdmin:Email` / `Auth:BootstrapAdmin:Password` (或等效的
`Auth__BootstrapAdmin__*` 環境變數)。

如果一個正式環境啟動 (`ASPNETCORE_ENVIRONMENT=Production`) 仍在使用預設密碼 `admin`，API 會在啟動
時記錄一則**警告**，提示你變更它——但它並不會拒絕啟動，所以不要指望這則被忽略的紀錄能當作安全網。

## 尚無集合時你會看到什麼

這是必須先建立的重要預期心理:在出貨預設狀態下——`Struo:ContentAssemblies` 沒有任何項目——管理後台
SPA 的側欄**完全沒有「Content」導覽群組**。已直接針對此份 checkout 驗證過:資料庫中恰好只有十一張框架
資料表 (`languages`、`files`、`file_translations`、`media_folders`、`users`、`roles`、
`permissions`、`user_roles`、`revisions`、`site_settings`、`user_sessions`)，別無其他;側欄只顯示
Dashboard (儀表板)、Media Library (媒體庫)、Settings (設定) 與 System (Language/Role/User)——因為
現在確實還沒有任何內容集合可列出。

**這是正確的，不是 bug。** 一個空白的 Content 區塊，正是一個零業務集合的樣板該有的樣子。
第 4 章會示範如何新增你的第一個集合，讓這個群組出現;第 16 章則示範同樣的事，若你想在設計自己
的集合之前，先看看用預先建好的 Blog 範例運作起來的樣子。

## 健康檢查端點與 Scalar

| URL | 用途 |
|---|---|
| `GET /health/live` | Liveness——程序啟動後一律回傳 200 |
| `GET /health/ready` | Readiness——只有在資料庫與快取檢查都通過後才回傳 200 |
| `GET /api/ping` | 輕量的信封 (envelope) 回應，適合用來對 REST pipeline 做 smoke test |
| `/scalar` | 互動式 API 瀏覽工具 (Mars 佈景主題)——**僅限非 Production 環境** |
| `/openapi/v1.json` | 產生的 OpenAPI 文件——**僅限非 Production 環境** |
| `/graphql` | GraphQL 端點——在每一個環境都可連上;內建的 Nitro IDE **僅限 Development**，而兩條 schema 揭露路由 (introspection 與 `?sdl`) 由 `GraphQl:ExposeSchema` 一起把關，預設僅限 Development——比上面的「非正式環境」限制更嚴格。查詢*執行*不受該閘門影響;兩者的差別見第 10 章。 |

已針對此份 checkout 確認過:

```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/health/ready
Healthy
HTTP_STATUS:200

$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5221/api/ping
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-07-29T03:42:17.4968695Z"}}
HTTP_STATUS:200
```

Scalar 瀏覽工具與原始 OpenAPI 文件，只有在環境不是 `Production` 時才會被掛載——在 Production 環境
下，這兩條路由都會回傳 404。如果你需要在 Production 環境使用它們，請自行在前面架設驗證機制。

## 接下來的步驟

- 第 3 章 [設定參考](03-configuration-reference.md)，記載上面用到的每一項設定 (以及所有沒用到的)。
- 第 4 章 [定義一個集合](04-defining-a-collection.md)，用你的第一個真實集合讓
  Content 導覽群組出現。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，改為選用啟用 Blog 示範專案 (以及乾淨地退出它)。
