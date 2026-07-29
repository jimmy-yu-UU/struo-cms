# 1. 簡介與架構

## StruoCMS 是什麼

StruoCMS 是一個可重複使用的無頭 (headless) CMS **樣板 (template)**，建構於 .NET 10、SqlSugar 與
PostgreSQL 之上，並搭配 Vue 3 打造的管理後台單頁應用程式 (SPA)。這裡的「樣板」二字是精確的說法：本
repository 只出貨核心的框架/系統能力，下游團隊 fork 之後，才依自己的實際專案定義自己的內容集合
(collection) 與 migration。

核心出貨內容包括:

- 一套中介資料驅動 (metadata-driven) 的集合引擎——你只需宣告一個帶有 attribute 的一般 C#
  entity，StruoCMS 便會由這單一宣告推導出其資料庫資料表、REST 端點、GraphQL schema、查詢 DSL 介面，
  以及管理後台 UI 表單。
- 身分與角色型存取控制 (RBAC)，具備逐集合的讀取/寫入/刪除權限、Argon2id 密碼雜湊、cookie 與
  bearer-token 認證，以及選用的 OpenID Connect SSO。
- 檔案與媒體 (media)，支援本機磁碟或 S3 相容的儲存後端，並提供即時圖片轉換。
- 版本紀錄 (revisions) 與軟刪除 (soft delete)，兩者皆為逐集合選用 (opt-in)。
- 國際化 (per-locale 內容)、站台設定/品牌、查詢 DSL、REST API、GraphQL API，以及驅動以上一切的管理
  後台 SPA 外殼。

## StruoCMS 不是什麼

StruoCMS 不是一個成品，且**不含任何業務內容模型**。你可能會從一般 CMS 展示範例中認得的那些
集合——文章、標籤、分類——並不屬於出貨的核心；它們只存在於 `samples/Struo.Sample.Blog` 這個示範
專案中，用來示範*如何*用你自己的 fork 也會用到的相同基礎元件來定義集合。該範例預設不會被 API
host 參照，其用意是讓你學會這些模式之後就把它刪掉 (第 16 章涵蓋選用啟用方式與刪除清單)。

具體而言，已在此份 checkout 上驗證過:一個預設安裝**零個內容集合**。資料庫中只有十張框架資料表
(見下文);管理後台 SPA 的側欄完全沒有「Content」導覽群組，因為根本沒有東西可顯示。這正是一份全新樣板
checkout 該有的正確、預期樣貌——不是 bug，也不是安裝不完整。

還有一點值得明說:StruoCMS 的 `Database:DbType` 設定所接受的五種資料庫引擎
(`PostgreSQL`、`MySql`、`SqlServer`、`Sqlite`、`Oracle`) 之中，只有 **PostgreSQL 是受支援的執行期
資料庫**，SQLite 僅用於測試套件。MySQL、SqlServer 與 Oracle 雖然在程式碼中有型別對應，但屬於
未驗證/實驗性質——部分 ORDER-BY 與字面值強制轉型 (literal-coercion) 的程式路徑，是專門針對
PostgreSQL/SQLite 的行為所寫的。

## Core 與 sample 的界線

**Core (核心)** 是指 `src/Struo.*` 下的一切 (下方四個後端專案)，加上框架自身持久化的 entity 型別，
統一收錄於單一清單——`FrameworkEntityTypes.All`。這份清單目前有 10 個項目，各自對應一張資料庫資料表:

| Entity 型別 | 資料表 |
|---|---|
| `Language` | `languages` |
| `File` | `files` |
| `FileTranslation` | `file_translations` |
| `MediaFolder` | `media_folders` |
| `User` | `users` |
| `Role` | `roles` |
| `Permission` | `permissions` |
| `UserRole` | `user_roles` |
| `Revision` | `revisions` |
| `SiteSettings` | `site_settings` |

如果某段程式碼不在 `src/Struo.*` 之下，也不是以上十種型別之一，它就不是 core。特別是
`samples/Struo.Sample.Blog` 是一個示範專案，fork 時會被刪除;`db/migrations/` 只承載 core-only 的
schema (`001-core-baseline.sql` 這個 bootstrap);而框架程式碼永遠不會參照 `samples/*`——這是一項可
檢驗的不變條件 (invariant)，不只是一種慣例 (見下方的依賴規則)。

## 方案 (Solution) 版面配置

```
struo-cms/
├── src/
│   ├── Struo.Domain/           # domain types; no project or package dependencies
│   ├── Struo.Application/      # application-layer abstractions, options, query/security contracts
│   ├── Struo.Infrastructure/   # SqlSugar wiring, identity, files, health checks, DI extensions
│   └── Struo.Api/               # ASP.NET Core host: controllers, GraphQL, Scalar, Serilog, Program.cs
├── samples/
│   └── Struo.Sample.Blog/      # demo content collections (Article/Tag/Category) — deletable
├── tests/
│   └── Struo.Tests/            # xUnit tests; references all four src projects and the sample
├── frontend/                    # Vue 3 admin SPA (separate pnpm workspace)
├── db/migrations/               # reviewed *.sql schema migrations (PostgreSQL, core schema only)
└── docs/                        # this manual
```

## 依賴規則

四個後端專案形成一條嚴格、單向的依賴鏈 (可由各專案自身的 `.csproj` 讀出):

| Project | 參照 |
|---|---|
| `Struo.Domain` | — (完全沒有任何 project 或 package 參照) |
| `Struo.Application` | → `Struo.Domain` |
| `Struo.Infrastructure` | → `Struo.Application`, `Struo.Domain` |
| `Struo.Api` | → `Struo.Application`, `Struo.Infrastructure` |

```
Struo.Domain  <──  Struo.Application  <──  Struo.Infrastructure  <──  Struo.Api
   (nothing)         (→ Domain)           (→ Application, Domain)   (→ Application, Infrastructure)
```

請注意 `Struo.Api` 並不直接參照 `Struo.Domain`——只透過 `Struo.Application` 與 `Struo.Infrastructure`
間接參照。`Struo.Domain` 完全不依賴任何外部套件;持久化用的 attribute 位於 `Struo.Infrastructure` 中的
entity 上，而非 domain 型別上。

**框架程式碼永遠不會參照 `samples/*`。** 四個 `src/Struo.*` 專案沒有任何一個對 `samples/` 有
`ProjectReference`;只有 `tests/Struo.Tests.csproj` (刻意用來測試該範例) 有。這正是讓該範例真正
「選用且可刪除」的原因——移除 `samples/Struo.Sample.Blog` 不會破壞 core。

## 技術堆疊

後端:

| 元件 | 版本 | 來源 |
|---|---|---|
| .NET SDK | 10.0.0, `rollForward: latestMinor` | `global.json` |
| 目標 Framework | `net10.0` | `Directory.Build.props` |
| C# 語言版本 | `latest` | `Directory.Build.props` |
| Nullable 參考型別 | 啟用 | `Directory.Build.props` |
| ORM | SqlSugarCore 5.1.4.215 | `Directory.Packages.props` |
| 執行期資料庫 | PostgreSQL (`docker-compose.yml` 中的 `postgres:17-alpine`) | `docker-compose.yml` |
| 僅測試用資料庫 | SQLite (`Microsoft.Data.Sqlite` 10.0.9) | `Directory.Packages.props` |
| GraphQL | HotChocolate.AspNetCore 16.4.0 | `Directory.Packages.props` |
| API 瀏覽工具 | Scalar.AspNetCore 2.16.5 (Mars 佈景主題、Axios client) | `Directory.Packages.props`, `Program.cs` |
| 日誌 | Serilog.AspNetCore 10.0.0 + Serilog.Sinks.File 7.0.0 | `Directory.Packages.props` |
| 密碼雜湊 | Isopoh.Cryptography.Argon2 2.0.0 (Argon2id) | `Directory.Packages.props` |
| 物件儲存 (S3 後端) | AWSSDK.S3 4.0.25.3 | `Directory.Packages.props` |
| 圖片轉換 | NetVips 3.2.0 / NetVips.Native 8.18.4 | `Directory.Packages.props` |
| 富文本清理 | HtmlSanitizer 9.1.968-beta | `Directory.Packages.props` |
| Session 存放 | Redis, via Microsoft.Extensions.Caching.StackExchangeRedis 10.0.9 | `Directory.Packages.props` |
| SSO | Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.9 | `Directory.Packages.props` |
| 測試執行器 | xUnit 2.9.3 | `Directory.Packages.props` |

前端 (版本以 `frontend/package.json` 所宣告者為準;caret 範圍依提交的 lockfile 解析):

| 元件 | 版本 | 來源 |
|---|---|---|
| Vue | ^3.5.39 | `frontend/package.json` |
| UI 元件庫 | PrimeVue ^4.5.5 (+ `@primeuix/themes` ^2.0.3) | `frontend/package.json` |
| 富文本編輯器 | TipTap ^3.27.1 (starter-kit + extensions) | `frontend/package.json` |
| 狀態管理 | Pinia ^3.0.4 | `frontend/package.json` |
| 路由 | vue-router ^5.1.0 | `frontend/package.json` |
| 國際化 | vue-i18n ^11.4.6 | `frontend/package.json` |
| 建置工具 | Vite ^8.1.1 | `frontend/package.json` |
| 程式語言 | TypeScript ~6.0.2 | `frontend/package.json` |
| E2E 測試 | Playwright ^1.61.1 | `frontend/package.json` |

CI 中固定的工具鏈版本 (`.github/workflows/ci.yml`):.NET SDK `10.0.x`、Node.js `24`、pnpm `10`。

## 能力總覽

| 能力 | 狀態 | 備註 |
|---|---|---|
| 中介資料驅動的集合 | Core | `[CmsCollection]` attribute，從 `Struo:ContentAssemblies` 中列出的組件掃描而來 |
| 身分與 RBAC | Core | Argon2id 雜湊;cookie + bearer 認證;逐集合讀取/寫入/刪除授權 |
| SSO (OpenID Connect) | Core，預設關閉 | `Oidc:Enabled = false` |
| 檔案與媒體 | Core | 本機磁碟或 S3 相容後端;即時圖片轉換 |
| 版本紀錄 | Core，逐集合選用 | `[CmsCollection(Revisions = true)]` |
| 軟刪除 | Core，逐集合選用 | `ISoftDeletable` |
| 國際化 | Core | 逐 locale 的翻譯附屬資料表 |
| 站台設定 / 品牌 | Core | 單例 `site_settings` 資料列，可由超級管理員在應用程式內編輯 |
| 查詢 DSL | Core | filter/sort/分頁，欄位與關聯路徑皆採白名單驗證 |
| REST API | Core | ASP.NET Core Controllers，統一回應信封 (envelope) |
| GraphQL API | Core | HotChocolate，schema 由相同的集合中介資料產生 |
| 管理後台 SPA | Core | Vue 3 + PrimeVue + TipTap |
| Blog 範例 | Demo，預設不出貨 | `samples/Struo.Sample.Blog`;選用啟用、可刪除 |

## 接下來該去哪

- 第 2 章 [快速入門](02-getting-started.md)，啟動整套系統並登入。
- 第 3 章 [設定參考](03-configuration-reference.md)，涵蓋每一個 `appsettings.json` 鍵。
- 第 4 章 [定義一個集合](04-defining-a-collection.md)，當你準備好要新增自己的內容型別時。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，在你動手打造自己的集合之前，先看看用這些
  基礎元件建構出來的完整集合長什麼樣子。
