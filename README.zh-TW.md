# StruoCMS

StruoCMS 是一個可直接 fork 的 headless CMS template，建構在 .NET 10 與 SqlSugar 之上，實際驗證
過的資料庫是 PostgreSQL，管理後台是一個 Vue 3 單頁應用程式。fork 之後，你在自己的專案裡宣告內容
集合，StruoCMS 把它們變成資料表、API 與後台介面。核心不出貨任何業務內容模型，預設安裝的內容集合
數量為零；`samples/Struo.Sample.Blog` 是一個可選、可拆除的示範專案，示範如何用同一套基礎元件定義
集合，學完之後就可以刪掉。

[![CI](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/jimmy-yu-UU/struo-cms/actions/workflows/ci.yml)

## 它包含什麼

- **集合引擎**：宣告一個帶有 attribute 的 C# entity，StruoCMS 便由這單一宣告推導出資料表結構、
  REST 端點、GraphQL schema、查詢 DSL，以及後台的表單畫面。
- **REST API 與 GraphQL API**：兩者由同一份集合 metadata 產生，回應一律包在同一個信封格式裡。
- **身分驗證與角色式權限 (RBAC)**：cookie 與 bearer token 兩種登入方式、Argon2id 密碼雜湊、選用
  的 OpenID Connect SSO；讀取、寫入、刪除權限以集合為單位授予。
- **檔案與媒體**：本機磁碟或 S3 相容的儲存後端，下載檔案時即時做圖片轉換。
- **版本紀錄與軟刪除**：兩者都是核心功能，各自以集合為單位開關。
- **多語內容、站台設定與品牌**，以及把以上一切收在同一個操作介面裡、以 Vue 3 打造的管理後台 SPA。

## 快速入門

先決條件：.NET SDK 10.0.x、Node.js 24.x、pnpm 10.x、Docker (含 Compose)。若要改以容器 image 執行
API 與管理後台 SPA，兩個 Dockerfile 都已提供；完整步驟留到之後專門介紹部署的章節。

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

開啟 `http://localhost:5173`，用已植入種子資料的 bootstrap 管理員帳號登入：`admin@admin.com` /
`admin`（只在 `users` 資料表第一次被建立時植入種子資料；若要覆寫，請在第一次啟動之前設定
`Auth:BootstrapAdmin:Email`/`Auth:BootstrapAdmin:Password`）。正式環境啟動時若仍在使用預設密碼，
會記錄一則啟動警告，但並不會拒絕啟動——請在上線之前先變更它。完整步驟，包括 PostgreSQL／Redis
埠號覆寫與健康檢查，請見[第 3 章：快速開始](docs/guide/zh-TW/03-getting-started.md)。

## 架構

四個後端專案（`Struo.Domain`、`Struo.Application`、`Struo.Infrastructure`、`Struo.Api`）依單向依
賴鏈排列，另外還有獨立的 `frontend/` 前端 workspace 與 `schema/` 契約快照；完整說明見
[第 2 章：系統架構](docs/guide/zh-TW/02-architecture.md)。

## 文件

完整手冊放在 `docs/` 之下，以英文與繁體中文逐章對照撰寫，從
[第 1 章：StruoCMS 是什麼](docs/guide/zh-TW/01-what-is-struocms.md)開始讀；在此 repository 中工作
的 AI 程式代理應閱讀 [`AGENTS.md`](AGENTS.md)。文件站台是獨立的專案，用以下指令安裝並啟動：

```bash
pnpm -C docs install
pnpm -C docs dev
```

## 授權

StruoCMS 採用 [MIT 授權](LICENSE)。隨圖片轉換功能一併打包的第三方元件，其各自的授權條款列於
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

---

[English](README.md)
