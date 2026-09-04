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

具體而言，已在此份 checkout 上驗證過:一個預設安裝**零個內容集合**。資料庫中只有十一張框架資料表
(見下文);管理後台 SPA 的側欄完全沒有「Content」導覽群組，因為根本沒有東西可顯示。這正是一份全新樣板
checkout 該有的正確、預期樣貌——不是 bug，也不是安裝不完整。

還有一點值得明說:StruoCMS 的 `Database:DbType` 設定所接受的五種資料庫引擎
(`PostgreSQL`、`MySql`、`SqlServer`、`Sqlite`、`Oracle`) 之中，只有 **PostgreSQL 是受支援的執行期
資料庫**，SQLite 僅用於測試套件。MySQL、SqlServer 與 Oracle 雖然在程式碼中有型別對應，但屬於
未驗證/實驗性質——部分 ORDER-BY 與字面值強制轉型 (literal-coercion) 的程式路徑，是專門針對
PostgreSQL/SQLite 的行為所寫的。

## Core 與 sample 的界線

**Core (核心)** 是指 `src/Struo.*` 下的一切 (下方四個後端專案)，加上框架自身持久化的 entity 型別，
統一收錄於單一清單——`FrameworkEntityTypes.All`。這份清單目前有 11 個項目，各自對應一張資料庫資料表:

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
| `UserSession` | `user_sessions` |

有兩個目錄**雖然不在 `src/Struo.*` 之下，但仍屬於 core**，fork 時要與它一併保留:`frontend/`
(管理後台 SPA) 與 `schema/` (schema gate 用來檢驗前後端兩邊的已提交契約快照——`schema/README.md`)。
除了這兩者之外，任何不在 `src/Struo.*` 之下、也不是以上十一種型別之一的東西，就不是 core。特別是
`samples/Struo.Sample.Blog` 是一個示範專案，fork 時會被刪除;`db/migrations/` 為核心出貨**零份** SQL
腳本——CodeFirst 會在每一個環境、五種受支援後端的任何一種上建立核心自己的資料表，因此核心不需要自己的
bootstrap 腳本——一個 fork 若在 `db/migrations/` 下新增腳本，那些腳本屬於該 fork，不屬於核心;而框架
程式碼永遠不會參照 `samples/*`——這是一項可檢驗的不變條件 (invariant)，不只是一種慣例 (見下方的依賴
規則)。

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
├── db/migrations/               # reviewed *.sql scripts that alter existing tables (all backends); ships empty
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
`ProjectReference`;只有 `tests/Struo.Tests/Struo.Tests.csproj` (刻意用來測試該範例) 有。這正是讓該
範例在 `src/Struo.*` 這個層級上真正「選用且可刪除」的原因——移除 `samples/Struo.Sample.Blog` 不會破壞
core。不過，單純執行 `rm -rf samples/` 仍會破壞 solution 層級的 `dotnet build`(`StruoCMS.slnx` 與上述
測試專案參照)——完整、安全的移除程序請見第 16 章的移除檢查清單。

## 什麼可以換、什麼不能換

本手冊其他地方所說的「資料庫可替換」，指的是 SqlSugar 的 **provider**——不是指 ORM 本身。
`Database:DbType` 可選五個值，在 `DbTypeMapper.Map`
(`src/Struo.Infrastructure/Persistence/DbTypeMapper.cs`) 中一對一對應到 `SqlSugar.DbType`:
PostgreSQL 是已驗證的執行期目標，SQLite 支撐測試套件，MySQL/SqlServer/Oracle 雖然在程式碼中有對應，
但屬未驗證 (見上方「StruoCMS 不是什麼」一節)。

ORM 本身則不是 fork 的 content project 可以抽換的東西。每一個 content entity——參見
`samples/Struo.Sample.Blog/Article.cs`——都以 `using SqlSugar;` 開頭，並直接掛上 SqlSugar
自身的 attribute:`[SugarTable]`、`[SugarColumn]`、`[SugarIndex]`、`[Navigate]`，與 StruoCMS
自己的 `[CmsCollection]`/`[CmsField]` 並列。第 4、5 章記載的 CodeFirst DDL 規則——`Id`
override 需要 `[SugarColumn(IsPrimaryKey = true)]`、`[ColumnShape]` 整體——都是 SqlSugar 自身的
語意，並非 StruoCMS 在其上包了一層抽象。這裡舉一個本手冊其他地方沒有明講的例子:`DateTime`
屬性需要 `[ColumnShape(TimestampWithTimeZone)]` 才能在 PostgreSQL 上得到具時區欄位，而非一個
單純的 `timestamp`——這正是 `UserSession.CreatedAt`/`ExpiresAt`
(`src/Struo.Infrastructure/Identity/UserSession.cs`) 明確掛上該 shape 的原因。第 13 章的軟刪除
底線是同一個直接依賴，只是換了形式，以註冊在 client 上的查詢過濾器呈現，而非 DDL attribute
(詳見下方)。
`IItemRepository`
(`src/Struo.Application/Query/IItemRepository.cs`) 是 core 內部的 seam——它唯一的實作是
`SqlSugarItemRepository`——不是為了讓 fork 換 ORM 而設計的抽象層;不論由誰實作這個介面，content
entity 上的 SqlSugar attribute 都仍然綁定 SqlSugar。

實際影響是:SqlSugar 的大版本升級，或是像 `[SugarIndex]` 這類 attribute 語意上的變動，會直接
衝擊每一個 fork 的 entity 類別——core 不會、也無法替 fork 吸收這類變動。有兩個過去屬於這種情況的
DDL 決策現在不再是了:單純的 `[SugarColumn(IsJson = true)]` 欄位寬度、以及翻譯 sidecar 的
`(fk, locale)` 複合唯一鍵，兩者都改由 core 的 `SqlSugarClientFactory` 內部計算，而非宣告在
entity 上，所以這兩項變動都由 core 替你吸收。升級 core 時，請把 `Directory.Packages.props` 中
`SqlSugarCore` 的版本列與你 fork 先前 checkout 的版本相比對，並在合併前讀過該版本的 changelog。

這種耦合會產生的具體陷阱，已記載於既有章節，這裡不重複:第 4 章的
[最小集合](04-defining-a-collection.md#最小集合) (`Id` override 與
`[SugarColumn(IsPrimaryKey = true)]`) 與第 5 章的
[常見陷阱](05-field-types.md#常見陷阱) (`[ColumnShape]`，包括與 JSON 欄位結合時會被直接拒絕的
情況)，是這種耦合在 DDL attribute 上的表現。第 13 章的
[全域查詢過濾器](13-revisions-and-soft-delete.md#全域查詢過濾器) 則是另一種 SqlSugar
耦合:版本紀錄不需要在 entity 上多加任何欄位，`ISoftDeletable` 本身也是一個不依賴任何套件的標記
介面——軟刪除真正綁定 SqlSugar 的地方，是那道註冊在 `SqlSugarClientFactory.Create` 中、針對
SqlSugar client 本身的查詢過濾器 `db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt ==
null)`，而不是宣告在 entity 上的東西。這一節說明耦合本身，那些章節則展示它的具體樣貌。

## 技術堆疊

後端:

| 元件 | 詳情 | 來源 |
|---|---|---|
| .NET SDK | `rollForward: latestMinor` | `global.json` |
| 目標 Framework | `net10.0` | `Directory.Build.props` |
| C# 語言版本 | `latest` | `Directory.Build.props` |
| Nullable 參考型別 | 啟用 | `Directory.Build.props` |
| ORM | SqlSugarCore | `Directory.Packages.props` |
| 執行期資料庫 | PostgreSQL (`docker-compose.yml` 中的 `postgres:17-alpine`) | `docker-compose.yml` |
| 僅測試用資料庫 | SQLite (`Microsoft.Data.Sqlite`) | `Directory.Packages.props` |
| GraphQL | HotChocolate.AspNetCore | `Directory.Packages.props` |
| API 瀏覽工具 | Scalar.AspNetCore (Mars 佈景主題、Axios client) | `Directory.Packages.props`, `Program.cs` |
| 日誌 | Serilog.AspNetCore + Serilog.Sinks.File | `Directory.Packages.props` |
| 密碼雜湊 | Isopoh.Cryptography.Argon2 (Argon2id) | `Directory.Packages.props` |
| 物件儲存 (S3 後端) | AWSSDK.S3 | `Directory.Packages.props` |
| 圖片轉換 | NetVips / NetVips.Native | `Directory.Packages.props` |
| 富文本清理 | HtmlSanitizer | `Directory.Packages.props` |
| Session 存放 | Redis, via Microsoft.Extensions.Caching.StackExchangeRedis | `Directory.Packages.props` |
| SSO | Microsoft.AspNetCore.Authentication.OpenIdConnect | `Directory.Packages.props` |
| 測試執行器 | xUnit | `Directory.Packages.props` |

前端 (確切版本請見 `frontend/package.json`;caret 範圍依提交的 lockfile 解析):

| 元件 | 詳情 | 來源 |
|---|---|---|
| Vue | — | `frontend/package.json` |
| UI 元件庫 | Tailwind CSS + shadcn-vue 的供應商元件 (reka-ui;toast 用 vue-sonner) | `frontend/package.json` |
| 富文本編輯器 | TipTap (starter-kit + extensions) | `frontend/package.json` |
| 狀態管理 | Pinia | `frontend/package.json` |
| 路由 | vue-router | `frontend/package.json` |
| 國際化 | vue-i18n | `frontend/package.json` |
| 建置工具 | Vite | `frontend/package.json` |
| 程式語言 | TypeScript | `frontend/package.json` |
| E2E 測試 | Playwright | `frontend/package.json` |

各道 gate 所使用的 .NET SDK、Node.js 與 pnpm 版本固定於 `.github/workflows/ci.yml`。

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
| 查詢 DSL | Core | `filter`/`sort`/分頁，欄位與關聯路徑皆採白名單驗證 |
| REST API | Core | ASP.NET Core Controllers，統一回應信封 (envelope) |
| GraphQL API | Core | HotChocolate，schema 由相同的集合中介資料產生 |
| 管理後台 SPA | Core | Vue 3 + Tailwind v4 + shadcn-vue + TipTap |
| Blog 範例 | Demo，預設不出貨 | `samples/Struo.Sample.Blog`;選用啟用、可刪除 |

## 接下來該去哪

- 第 2 章 [快速入門](02-getting-started.md)，啟動整套系統並登入。
- 第 3 章 [設定參考](03-configuration-reference.md)，涵蓋每一個 `appsettings.json` 鍵。
- 第 4 章 [定義一個集合](04-defining-a-collection.md)，當你準備好要新增自己的內容型別時。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，在你動手打造自己的集合之前，先看看用這些
  基礎元件建構出來的完整集合長什麼樣子。
