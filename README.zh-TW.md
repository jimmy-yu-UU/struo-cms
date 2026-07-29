# StruoCMS

一個可重複使用的無頭 (headless) CMS **樣板 (template)**：fork 它、定義你自己的內容集合，出貨你自己的
產品。它不是一個成品 CMS 產品，也不出貨任何屬於自己的業務內容模型。

[![CI](https://github.com/stu640978/struo-cms/actions/workflows/ci.yml/badge.svg)](https://github.com/stu640978/struo-cms/actions/workflows/ci.yml)

## 你會得到什麼

- 一套中介資料驅動 (metadata-driven) 的集合引擎——你只需宣告一個帶有 attribute 的一般 C#
  entity，StruoCMS 便會由這單一宣告推導出其資料庫資料表、REST 端點、GraphQL schema、查詢 DSL 介面，
  以及管理後台 UI 表單。
- 身分與角色型存取控制 (RBAC)：逐集合的讀取/寫入/刪除權限、Argon2id 密碼雜湊、cookie 與
  bearer-token 認證，以及選用的 OpenID Connect SSO。
- 檔案與媒體 (media)，支援本機磁碟或 S3 相容的儲存後端，並提供即時圖片轉換。
- 版本紀錄 (revisions) 與軟刪除 (soft delete)，兩者皆為逐集合選用 (opt-in)。
- 國際化 (per-locale 內容)、站台設定/品牌、查詢 DSL、REST API、GraphQL API，以及驅動以上一切、以
  Vue 3 打造的管理後台 SPA。

## 你不會得到什麼

沒有任何業務內容模型。核心中不出貨任何「Article」「Product」或其他 domain 集合——預設安裝的內容
集合數量為零。`samples/Struo.Sample.Blog` 是一個可選、可拆除的示範專案 (Article/Tag/Category)，
示範*如何*用你自己的 fork 也會用到的相同基礎元件來定義集合；它預設不會被 API host 參照，其用意是
讓你在用它學會這些模式之後，就把它刪掉。

## 快速入門

先決條件：.NET SDK 10.0.x、Node.js 24.x、pnpm 10.x、Docker (含 Compose)。

```bash
# 1. Start PostgreSQL and Redis
docker compose up -d

# 2. Configure the API (gitignored local settings file)
cp src/Struo.Api/appsettings.Development.json.example src/Struo.Api/appsettings.Development.json

# 3. Run the API
dotnet run --project src/Struo.Api
# listens on http://localhost:5221

# 4. In a second terminal, run the admin SPA
cd frontend
pnpm install
pnpm dev
# listens on http://localhost:5173, proxies /api to :5221
```

開啟 `http://localhost:5173`，用已植入種子資料的 bootstrap 管理員帳號登入：`admin@admin.com` /
`admin`（只在 `users` 資料表第一次被建立時植入種子資料——之後即使對著一個已清空的資料表重新啟動，
也不會重新建立或重設它；若要覆寫，請在第一次啟動之前設定
`Auth:BootstrapAdmin:Email`/`Auth:BootstrapAdmin:Password`）。正式環境啟動時若仍在使用預設密碼，
會記錄一則啟動警告，但並不會拒絕啟動——請在上線之前先變更它。完整細節，包括 MinIO 選用的 `s3`
Compose profile 與 port 覆寫變數，請見[第 2 章](docs/guide/en/02-getting-started.md)。

## 架構

四個後端專案形成一條嚴格、單向的依賴鏈，另外還有一個獨立的前端 workspace：

```
Struo.Domain  <──  Struo.Application  <──  Struo.Infrastructure  <──  Struo.Api
   (nothing)         (→ Domain)           (→ Application, Domain)   (→ Application, Infrastructure)

frontend/            Vue 3 admin SPA (separate pnpm workspace), talks to Struo.Api over REST/GraphQL
```

- `Struo.Domain` — domain 型別；完全沒有任何 project 或 package 參照。
- `Struo.Application` — application 層的抽象、選項 (options)、查詢/安全性合約。
- `Struo.Infrastructure` — SqlSugar 接線、身分、檔案、健康檢查、DI extension。
- `Struo.Api` — ASP.NET Core host：controllers、GraphQL、Scalar、Serilog、`Program.cs`。

框架程式碼永遠不會參照 `samples/*`——只有 `tests/Struo.Tests` 會，這正是讓
`samples/Struo.Sample.Blog` 真正做到選用且可刪除的原因。

## 文件

完整手冊放在 `docs/` 之下，以英文與繁體中文 (zh-TW) 逐章對照撰寫：

| # | Chapter |
|---|---|
| 1 | [簡介與架構](docs/guide/zh-TW/01-introduction-and-architecture.md) |
| 2 | [快速入門](docs/guide/zh-TW/02-getting-started.md) |
| 3 | [設定參考](docs/guide/zh-TW/03-configuration-reference.md) |
| 4 | [定義一個集合](docs/guide/zh-TW/04-defining-a-collection.md) |
| 5 | [欄位型別與介面](docs/guide/zh-TW/05-field-types.md) |
| 6 | [國際化](docs/guide/zh-TW/06-internationalization.md) |
| 7 | [關聯](docs/guide/zh-TW/07-relations.md) |
| 8 | [查詢 DSL](docs/guide/zh-TW/08-query-dsl.md) |
| 9 | [REST API](docs/guide/zh-TW/09-rest-api.md) |
| 10 | [GraphQL API](docs/guide/zh-TW/10-graphql-api.md) |
| 11 | [檔案、媒體與圖片轉換](docs/guide/zh-TW/11-files-and-media.md) |
| 12 | [認證、SSO 與 RBAC](docs/guide/zh-TW/12-auth-and-rbac.md) |
| 13 | [版本紀錄與軟刪除](docs/guide/zh-TW/13-revisions-and-soft-delete.md) |
| 14 | [管理後台 SPA 客製化](docs/guide/zh-TW/14-admin-spa-customization.md) |
| 15 | [部署、維運與測試](docs/guide/zh-TW/15-deployment-operations-testing.md) |
| 16 | [範例走查](docs/guide/zh-TW/16-sample-walkthrough.md) |

zh-TW 讀者請由 [`docs/README.md`](docs/README.md) 開始，查看翻譯後的索引。在此 repository 中工作的
AI 程式代理 (coding agent) 應閱讀 [`AGENTS.md`](AGENTS.md)。

## 授權

StruoCMS 採用 [MIT 授權](LICENSE)。隨圖片轉換功能一併打包的第三方元件，其各自的授權條款列於
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

---

[English](README.md)
