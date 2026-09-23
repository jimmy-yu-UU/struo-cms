# StruoCMS

StruoCMS 是一個可直接 fork 的 headless CMS template，建構在 .NET 10 與 SqlSugar 之上，實際驗證過的資
料庫是 PostgreSQL，管理後台是一個 Vue 3 單頁應用程式。fork 之後，你在自己的專案裡宣告內容集合，
StruoCMS 把它們變成資料表、API 與後台介面。

內容模型從你這邊開始：剛裝好的 StruoCMS 一個內容集合都沒有，Article、Tag、Category 這些集合都由你自
己宣告。`samples/Struo.Sample.Blog` 是一個可選的示範專案，示範怎麼用核心提供的工具定義自己的集合，學
完之後可以刪掉；完整的移除步驟留到之後專門介紹範例專案的章節。

[![CI](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml)

## 專案狀態

StruoCMS 仍在開發與測試階段，目前版本是 0.7.x。版本之間可能出現 breaking change：設定鍵的預設
值、資料表或端點的形狀都可能改變。把新版核心合併進你的 fork 之前，先讀[版本紀錄](docs/guide/zh-TW/changelog.md)
裡的「破壞性變更」條目，逐條核對你的 fork 有沒有碰到。

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

先決條件：.NET SDK 10.0.x、Node.js 24、pnpm 10.x、Docker 加上 Compose。

API 與管理後台也可以用容器跑，兩個 Dockerfile 都在 repo 裡；完整步驟留到之後專門介紹部署的章節。

```bash
# 1. 啟動 PostgreSQL 與 Redis
docker compose up -d

# 2. 設定 API（gitignore 排除的本機設定檔）
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json

# 3. 執行 API
dotnet run --project src/Struo.Api
# 監聽 http://localhost:5221

# 4. 另開一個終端機，執行管理後台
cd frontend
pnpm install
pnpm dev
# 監聽 http://localhost:5173，並將 /api 轉發到 :5221
```

開啟 `http://localhost:5173`，用預設帳號登入：`admin@admin.com` / `admin`。這組帳密只在 `users` 資料
表第一次建立時植入；要換帳密，第一次啟動前先設 `Auth__BootstrapAdmin__Email`／
`Auth__BootstrapAdmin__Password`。

用 Production 環境啟動、密碼還是預設值時，log 只會留下一筆警告，不會擋下啟動——上線前先把它改掉。完整
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

## 授權

StruoCMS 採用 [MIT 授權](LICENSE)。隨圖片轉換功能一併打包的第三方元件，其各自的授權條款列於
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

---

[English](README.md)
