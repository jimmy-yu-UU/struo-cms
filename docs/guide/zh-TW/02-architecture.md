# 2. 系統架構

fork 之後要加內容、要換元件，得先知道整個系統怎麼分層、哪裡是不能動的核心。

## 四個專案與依賴方向

後端 solution 在 `src/` 底下有四個專案，各自負責一層：

- `Struo.Domain`：純粹的 domain 型別。
- `Struo.Application`：應用層的介面與設定選項，以及查詢和安全性的合約。
- `Struo.Infrastructure`：SqlSugar 的介接設定、身分驗證、檔案儲存、健康檢查與各種 DI 擴充。
- `Struo.Api`：ASP.NET Core host 本身——controller、GraphQL、Scalar、Serilog、`Program.cs`。

依賴方向只能往一個方向走：`Struo.Domain` 不依賴任何專案；`Struo.Application` 依賴
`Struo.Domain`；`Struo.Infrastructure` 依賴 `Struo.Application` 與 `Struo.Domain`；
`Struo.Api` 依賴 `Struo.Application` 與 `Struo.Infrastructure`。`Struo.Api` 不會直接參照
`Struo.Domain`，只是透過中間兩層間接用到它。

`Struo.Domain` 本身沒有任何 `PackageReference` 或 `ProjectReference`——連 SqlSugar 的持久化
屬性都不會出現在 domain 型別上。

測試專案 `tests/Struo.Tests` 是唯一參照全部四個專案、外加範例專案的地方；`Struo.Application`
與 `Struo.Infrastructure` 也都對它開放 `InternalsVisibleTo`，fork 如果重新命名測試專案，這
兩個 `InternalsVisibleTo` 要一起改。

## 核心與範例的界線

核心是 `src/Struo.*` 底下的所有程式碼，加上框架自己會持久化的 entity。這些 entity 集中列在
`FrameworkEntityTypes.All`，一共十一筆。資料表的實際名稱是
`Database:TablePrefix` 加上基底名，預設前綴 `struo_`，所以 `User` 的表是 `struo_users`；把前綴設成空
字串就只剩基底名。範例專案與你自己的集合不加前綴。

| Entity | 基底名 |
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

這十一種裡有七種本身也是集合（宣告了 `[CmsCollection]`）：`Language`、`File`、`MediaFolder`、
`User`、`Role`、`Permission`、`UserRole`。另外四種——`FileTranslation`、`Revision`、
`SiteSettings`、`UserSession`——是框架資料表，但不是集合。

核心還有兩個不在 `src/Struo.*` 底下的資料夾，加上 `db/migrations/README.md`，fork 時都要留
著：

- `frontend/`：管理後台。
- `schema/`：schema gate 用來比對前後端契約的快照。

`db/migrations/` 裡屬於核心的只有一份說明機制的 `README.md`：CodeFirst 在任何環境、任何一
種設定好的資料庫上都會自己建出框架資料表，核心不需要開機用的 SQL 腳本；fork 自己加進去的腳
本屬於那個 fork。

框架程式碼不會參照 `samples/` 底下的任何東西，這條邊界有測試守著。

## 一個宣告推導出一切

一個 entity 類別的宣告，就決定了資料表結構、REST 端點、GraphQL schema、查詢語法與後台表單
這五樣東西的形狀。

一個內容 entity 類別上，SqlSugar 自己的 `[SugarTable]`、`[SugarColumn]`、`[SugarIndex]`、
`[Navigate]`，和 StruoCMS 的 `[CmsCollection]`／`[CmsField]` 寫在同一個類別上，不是兩層各管
各的。

這些屬性只在啟動時被掃描一次，結果快取進一個單例的 metadata provider，之後不會每個請求重新
掃一次。

## 什麼可以換、什麼不能換

可以換的部分：

- **資料庫引擎**：`Database:DbType` 的五個值，一對一對應到 SqlSugar 自己的 `DbType`，換的
  是 SqlSugar 底下的 provider，不是 SqlSugar 本身。CodeFirst 靠不分廠牌的
  `[ColumnShape]`／`ColumnTypeMap` 對應，五種引擎共用同一份建表邏輯；哪幾種驗證過，第 1
  章已列。
- **檔案後端**：`IFileStorage` 是核心讓出去的介面，本機硬碟與 S3 相容儲存都是它的實作。
- **搜尋提供者**：`ISearchProvider` 是同一種讓出去的介面，核心只內建一個
  `NullSearchProvider`（用 `TryAddScoped` 註冊，永遠不處理搜尋），fork 沒註冊自己的實作之
  前，走的是內建的 LIKE 掃描。
- **變更通知**：`IItemChangeListener` 同樣是讓出去的介面，核心只提供一個預設註冊的
  `ItemChangeNotifier` 負責派送；fork 沒註冊任何監聽者時，一次寫入通知零個監聽者，fork 可以
  註冊任意數量。
- **識別 store**：身分資料的存取也是收在幾個介面後面——`IUserCredentialStore`、
  `IUserAccountStore`、`IPermissionGrantStore`、`IRolePermissionStore`、
  `IExternalUserStore`、`IUserSessionStore`，都在 `Struo.Application/Security/` 底下。每個
  介面各自有對應的 `SqlSugar*` 實作類別，在 `AddStruoInfrastructure` 裡用 `AddScoped` 註冊。
  fork 要換掉識別儲存的實作，得在呼叫 `AddStruoInfrastructure` 之後再註冊自己的版本——後註
  冊的贏，這幾個不是 `TryAdd`。

不能換的是 SqlSugar 本身，以及它牽動的框架資料表：每個內容 entity 都直接背著 SqlSugar 的屬
性，fork 寫的每一個 entity 都繼承這層語意；`IItemRepository` 也只有一個實作
`SqlSugarItemRepository`，不是一層可以拿掉重寫的 ORM 抽象層。

SqlSugar 大版本升級，或是 `[SugarIndex]` 這類屬性的語意變動，會直接衝擊 fork 的每一個
entity 類別，核心無法幫忙吸收——升級前，先比對 `Directory.Packages.props` 裡 `SqlSugarCore`
的版本，讀過那個版本的 changelog 再動手。

框架資料表牽涉的 bare JSON 欄位寬度，是 `SqlSugarClientFactory` 內部算出來的；翻譯
sidecar的唯一索引由 `TranslationSidecarIndexPolicy` 算出、由同一個 factory 套用；soft delete
的過濾條件，也是同一個 factory 註冊的查詢過濾器（`db.QueryFilter.AddTableFilter<ISoftDeletable>`），
fork 不需要自己重算或重新註冊。

所有資料庫存取都得經過 SqlSugar，只有四個刻意留下的例外會組字串 SQL：`db/migrations/` 的腳
本、`SchemaGuard` 只在開發環境下用的唯讀查詢、關聯篩選子查詢組出來的四種字串形式，以及
ORDER BY 用的字串。

## 技術堆疊

| 層 | 技術 | 版本或依據 |
|---|---|---|
| .NET SDK | 10.0.0，`rollForward: latestMinor` | `global.json` |
| 後端建置設定 | `net10.0`、nullable 開啟、警告視為錯誤 | `Directory.Build.props` |
| 開發用資料庫容器 | PostgreSQL `17-alpine` | `docker-compose.yml` |
| 開發用快取容器 | Redis `7-alpine` | `docker-compose.yml` |
| 前端框架 | Vue 3.5.x、Vite 8.2.0、TypeScript 6.0.x | `frontend/package.json`／lockfile |
| 前端樣式與元件 | Tailwind CSS 4.3.x、shadcn-vue（reka-ui 2.10.x） | `frontend/package.json`／lockfile |
| 前端編輯器與狀態 | TipTap 3.30.x、Pinia 4.x、vue-router 5.x、vue-i18n 11.x | `frontend/package.json`／lockfile |
| 前端測試 | Playwright 1.62.x、Vitest 4.1.x | `frontend/package.json`／lockfile |
| CI 版本釘選 | Node 24、pnpm 10、.NET SDK `10.0.x` | `.github/workflows/ci.yml` |

`Directory.Build.props` 的設定對整個 solution 生效，fork 新增的專案不用再設定一次就會照樣
繼承。`global.json` 釘的 `10.0.0`＋`latestMinor` 與 CI 安裝的 `10.0.x`，是兩個各自獨立的
版本釘選，不是同一件事。

兩個外圍專案的套件清單直接看各自的 `.csproj`：`Struo.Api` 那邊是 GraphQL、OpenAPI 與
Serilog 這一組，`Struo.Infrastructure` 那邊是 SqlSugarCore、S3、密碼雜湊與影像處理。

API 文件用 Scalar，套用 Mars 主題，預設客戶端是 JavaScript／Axios。

設定了 `Redis:ConnectionString` 時，Redis 除了快取，也存 cookie 驗證的 session ticket。

## 一次請求經過哪裡

進入 controller 之前，請求依序經過：nosniff header、Serilog 的請求紀錄、CORS、例外處理、身
分驗證、授權、CSRF 防護、權限判斷、rate limiter，最後才到 controller 或 GraphQL。例外處理刻
意排在身分驗證之前，這樣身分驗證階段本身出錯，也還是會回一個包好的 500。

查詢參數先由 `QueryValidator` 對照 metadata 驗證過，才會組成 SQL。進了 controller 之後，資
料存取只走一條路：`IItemRepository` 這個介面只有一個實作 `SqlSugarItemRepository`；沒有一
個 controller 直接碰 `ISqlSugarClient`。

## 接下來

分層與界線都看過了，接下來把 API 與後台實際跑起來：讀[第 3 章：快速開始](03-getting-started.md)。
