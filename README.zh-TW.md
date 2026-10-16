# StruoCMS

StruoCMS 是一個可直接 fork 的 headless CMS template，建構在 .NET 10 與 SqlSugar 之上，實際驗證過的資
料庫是 PostgreSQL，管理後台是一個 Vue 3 單頁應用程式。fork 之後，你在自己的專案裡宣告內容集合，
StruoCMS 把它們變成資料表、API 與後台介面。

內容模型從你這邊開始：剛裝好的 StruoCMS 一個內容集合都沒有，Article、Tag、Category 這些集合都由你自
己宣告。`samples/Struo.Sample.Blog` 是一個可選的示範專案，示範怎麼用核心提供的工具定義自己的集合，學
完之後可以刪掉；完整的移除步驟留到之後專門介紹範例專案的章節。

[![CI](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml)

## 專案狀態

StruoCMS 仍在開發與測試階段，版本之間可能出現 breaking change：設定鍵的預設值、資料表或端點的
形狀都可能改變。目前版本與每一筆「破壞性變更」都記在[版本紀錄](docs/guide/zh-TW/changelog.md)；
把新版核心合併進你的 fork 之前，先逐條核對你的 fork 有沒有碰到。

## 它包含什麼

- **集合引擎**：宣告一個帶有 attribute 的 C# entity，StruoCMS 便由這單一宣告推導出資料表結構、REST
  端點、GraphQL schema、查詢 DSL，以及後台的表單畫面。
- **REST API 與 GraphQL API**：兩者由同一份集合 metadata 產生；REST 的每個回應都包在同一個信封格式
  裡。
- **身分驗證與角色式權限**：cookie 與 bearer token 兩種方式、Argon2id 密碼雜湊、預設關閉的 OpenID
  Connect 單一登入；讀取、寫入、刪除權限以集合為單位授予。
- **檔案與媒體**：本機硬碟或 S3 相容的儲存後端，下載檔案時即時做圖片轉換。
- **版本紀錄與軟刪除**：兩者都是核心功能，各自以集合為單位開關。
- **多語內容、站台設定與品牌**：欄位可以各語言分開存翻譯；整站共用一筆設定，super-admin 直接在後台
  改。
- **管理後台**：一個 Vue 3 單頁應用程式，把以上功能收在同一個操作介面裡。

## 快速開始

最簡單的啟動服務進行開發及測試的方式是 Docker Compose，只需要 Docker 加上 Compose：

```bash
docker compose up -d --build
docker compose ps   # 等 postgres、redis、api 都顯示 (healthy)
```

開啟 `http://localhost:8080`，用 `admin@admin.com` 登入。API 在 `http://localhost:8081`。
第一次啟動會建立資料表並植入這個帳號，密碼是隨機產生的，只會在 api 的 log 印出一次
（`docker compose logs api`）。要自己指定，第一次啟動前在 `.env` 設
`STRUO_ADMIN_EMAIL`／`STRUO_ADMIN_PASSWORD`。

- 埠號：被佔用時，複製 `.env.example` 成 `.env`，覆寫 `STRUO_ADMIN_PORT`、`STRUO_API_PORT`、
  `STRUO_PG_PORT` 或 `STRUO_REDIS_PORT`。
- 埠預設只綁在 `127.0.0.1`；`STRUO_BIND_ADDRESS=0.0.0.0` 會把它們連同固定的開發密碼開放給網路。
- 這套服務以 Production 執行，session cookie 帶 Secure：在 `http://localhost` 能登入，用其他主機名稱
  連進來，前面要先架 HTTPS。
- `docker compose down` 保留資料；`docker compose down -v` 連資料一起刪。

做開發或有特殊需求時，只起你需要的服務，其餘自己跑。先決條件：.NET SDK 10.0.x、Node.js 24、pnpm 10.x、
Docker 加上 Compose。

```bash
# 1. 啟動 PostgreSQL 與 Redis
docker compose up -d postgres redis

# 2. 設定 API（gitignore 排除的本機設定檔）
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json

# 3. 建立資料表與種子資料（沒跑過這一步，API 不會啟動）
dotnet run --project src/Struo.Api -- migrate

# 4. 執行 API
dotnet run --project src/Struo.Api
# 監聽 http://localhost:5221

# 5. 另開一個終端機，執行管理後台
cd frontend
pnpm install
pnpm dev
# 監聽 http://localhost:5173，並將 /api 轉發到 :5221
```

開啟 `http://localhost:5173`，用 `admin@admin.com` 登入。這個帳號由 `migrate` 指令植入，並只印出一次隨機產生的密碼；
要自己指定，執行它之前先設 `Auth__BootstrapAdmin__Email`／`Auth__BootstrapAdmin__Password`。完整
步驟，包括 PostgreSQL／Redis 埠號覆寫與健康檢查，見
[第 3 章：快速開始](docs/guide/zh-TW/03-getting-started.md)。

## 架構

後端有四個專案（`Struo.Domain`、`Struo.Application`、`Struo.Infrastructure`、`Struo.Api`），依賴只往
一個方向走；另外還有獨立的 `frontend/` workspace 與 `schema/` 契約快照。完整說明見
[第 2 章：系統架構](docs/guide/zh-TW/02-architecture.md)。

## 文件

完整手冊放在 `docs/` 之下，以英文與繁體中文逐章對照撰寫，從
[第 1 章：StruoCMS 是什麼](docs/guide/zh-TW/01-what-is-struocms.md)開始讀。用 AI coding agent 開發這
個專案，先讀 [`AGENTS.md`](AGENTS.md)。文件站台是獨立的專案，用以下指令安裝並啟動：

```bash
pnpm -C docs install
pnpm -C docs dev
```

## CI 與 SonarQube Cloud

`.github/workflows/ci.yml` 的 `sonar-backend`、`sonar-frontend` 兩個 job 把程式碼送到 SonarQube
Cloud 分析。fork 要設定儲存庫變數 `SONAR_ORGANIZATION`、`SONAR_BACKEND_PROJECT_KEY`、
`SONAR_FRONTEND_PROJECT_KEY`，以及 secret `SONAR_TOKEN`；沒有 `SONAR_ORGANIZATION` 時兩個 job 都會
跳過。

## 授權

StruoCMS 採用 [MIT 授權](LICENSE)。隨圖片轉換功能一併打包的第三方元件，其各自的授權條款列於
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

---

[English](README.md)
