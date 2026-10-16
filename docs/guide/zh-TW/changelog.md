# 版本紀錄

## 0.11.0

::: danger 破壞性變更
- `SchemaCheckReport` 的 `Errors` 與 `Warnings` 是方法：`GetErrors()` 與 `GetWarnings()`。呼叫它們的程式
  要把屬性存取改成方法呼叫。
- `IRelationExpander.ExpandAsync` 與 `RelationExpander.ExpandAsync` 以一個 `EntityAccessors` 取代原本的三個
  委派，參數位置在 `deep` 之後。它的欄位是 `ProjectTarget`、`ParentId`、`ReadProp`。實作 `IRelationExpander`
  的 fork，要把自己的 `ExpandAsync` 簽章改成接收 `EntityAccessors`；直接呼叫 `ExpandAsync` 的 fork，要改成傳
  `new EntityAccessors(...)`。
- `GraphQlQueryBuilder.BuildQuery` 接受一個 `GraphQlQueryArgs`（篩選、排序、分頁、搜尋、`deep`、facets、
  aggregate），後面接 `collection` 與 `relationTarget`。直接呼叫它的程式要改成先建 `GraphQlQueryArgs`。
- `SchemaTypeMapper.TagItemInputName` 是常數，不是方法。把 `TagItemInputName()` 改成 `TagItemInputName`。
- 範本不再附帶 bootstrap 管理員密碼。`appsettings.json` 裡的 `Auth:BootstrapAdmin:Password` 是 `""`，
  `docker-compose.yml` 的 `STRUO_ADMIN_PASSWORD` 預設為空。密碼留空時，建立管理員的那次執行會隨機產生
  至少 24 個字元的密碼，並以 Warning 印出一次。設定的密碼若不符合 `Auth:Password`，或不分大小寫等於 `admin`，
  現在會在任何環境被拒絕：`migrate`、`migrate:preview` 與 `Database:MigrateOnStartup` 啟動都會失敗，錯誤訊息點名
  `Auth__BootstrapAdmin__Password`，資料庫已經植入過也一樣，直到把值改好或移除。只在 Production 出現的
  預設密碼警告已移除。Playwright 測試從 `E2E_PASSWORD` 讀密碼，這個變數是必填。原本依賴
  `admin@admin.com` / `admin` 的 fork，必須自己設定密碼，或從 `migrate` 的輸出讀產生的密碼。
- 已啟用的 `Oidc` 區段，若 `Authority` 的路徑含有 `common`、`organizations` 或 `consumers` 其中一段，
  除非設定了 `Oidc:AllowedTenantId` 或 `Oidc:AllowedEmailDomains`，否則啟動時會被拒絕，錯誤訊息點名這兩個鍵。
  使用多租用戶 authority 又沒有設定限制的 fork，必須設定其中一個，或改用單一 tenant 的 authority。
- `GET /api/items/{collection}/{id}/revisions` 對未知的項目 id 回 404，原本回空陣列。垃圾桶中項目的版本紀錄
  （列表與單筆，REST 與 GraphQL）需要該集合的刪除授權；沒有的呼叫端在 REST 收到 404，在 GraphQL 收到空清單
  或 `null`。`IItemUseCases.ListRevisionsAsync` 與 `IGraphQlDataSource.ListRevisionsAsync` 改回傳可為 null 的
  清單，`null` 代表項目未知或被隱藏。讀取結果的程式必須處理 `null`；fork 自己的實作遇到被隱藏的項目也必須回傳
  `null`，否則不會隱藏垃圾桶中的項目。
- `/openapi/v1.json` 與 `/scalar` 只在 Development 註冊，session cookie 在 Development 之外的所有環境一律帶
  `Secure`；兩者原本只看 Production，所以類 Staging 環境（Staging、UAT 等）會公開 API 規格，並讓 cookie 走純
  HTTP。新增的 `OpenApi:Enabled` 與 `Auth:Cookie:RequireSecure` 可分別覆寫預設值。在 Development 之外對外提供
  OpenAPI，或跑在 HTTP 上的 fork，必須設定 `OpenApi__Enabled=true` 或 `Auth__Cookie__RequireSecure=false`。
- 每個 GraphQL mutation 都需要已登入的呼叫端。就算 `public` 角色在該集合上有寫入授權，匿名 mutation 也會被以
  `UNAUTHORIZED` 拒絕；查詢不受影響。這與 REST 的寫入端點一致，那邊本來就要求登入。原本不帶 session cookie
  或 bearer token 就透過 GraphQL 寫入的用戶端，必須先登入。
- 使用者 email 在每條寫入路徑上都以去除前後空白並轉成小寫的形式儲存：`POST /api/users`、透過 items API 與
  GraphQL 對 `user` 集合的建立與更新、OIDC 登入建立的帳號，以及 bootstrap 管理員。與另一位使用者的 email
  只差大小寫的值，會被以 `409` `CONFLICT` 拒絕。登入仍然不分大小寫比對，所以以大小寫混合儲存的資料列照常
  可用，也不會改寫任何既有資料列。期待讀回自己送出的大小寫的用戶端，現在收到的是小寫。升級前，維運人員可用
  `SELECT lower(email), count(*) FROM struo_users GROUP BY lower(email) HAVING count(*) > 1;`
  列出既有的大小寫重複（資料表名稱依 `Database:TablePrefix` 調整），再合併或刪除多餘的資料列。
:::

### 安全性

- 在 MySQL 與 MariaDB 上，語言代碼可以透過翻譯欄位的排序注入 SQL。用 `"Code"` 這種 JSON 鍵（而不是
  `code`）寫入的語言會略過代碼格式檢查，而排序把 locale 以重複引號的字串常值嵌進 ORDER BY 文字，這兩個後端
  並不把這種寫法視為安全。排序現在由型別化的 SqlSugar 運算式建出，把 locale 當成 SQL 參數綁定，任何排序都
  不再組 SQL 文字。代碼格式規則（`[A-Za-z0-9_-]`，1 到 35 個字元）現在在寫入交易內對儲存的語言資料列執行，
  與 JSON 鍵的大小寫無關。

- 帶著有效 session cookie 的密碼登入或 OIDC 登入，會把新使用者登入到該 cookie 既有的 session，因此被塞進
  受害者瀏覽器的 cookie 會變成受害者的 session。現在每次登入一律發出全新的 session，請求帶來的那個 session
  在登入成功後即撤銷；登入失敗則不動它。

- 登入嘗試現在會在驗證密碼之前就先記到帳號上，同時發出的猜測不再能讀到同一個計數、一起溜過
  `RateLimiting:LoginAccount:PermitLimit`。設了 `Redis:ConnectionString` 時，計數是所有實例共用的
  Redis 原子計數器，Redis 連不上時登入會回 5xx。新增的 `RateLimiting:LoginConcurrency` 區段
  （`Enabled` 為 true、`PermitLimit` 4、`QueueLimit` 32）限制單一行程內同時驗證密碼的登入數，其餘回
  429 並帶 `Retry-After`。因為每次嘗試都算數，知道某個帳號 email 的人可以讓它在視窗結束前無法用密碼
  登入；SSO 登入不受影響。

### 修正

- `/health/live` 與 `/health/ready` 不再解析呼叫端的權限，所以角色資料庫失敗時，存活探測不會回 500
  而讓健康的執行個體被重啟。

- Production 環境啟動時若 `Redis:ConnectionString` 是空的，會記錄一則警告：工作階段與登入節流此時存放在
  行程記憶體，只能跑單一副本，重新啟動會讓所有使用者登出。取代角色的權限授權時，會加入呼叫端已經開啟的交易，
  不再自行提交。第 16 章說明 API 存取權杖在改密碼後仍然有效。

### 升級說明

Redis 登入節流改用新的鍵計數，所以升級時每個帳號的登入失敗計數會歸零一次。

語言代碼的格式規則在每次寫入語言集合（包括刪除）時，檢查該集合的每一個資料列。已經存在的格式不合的代碼，會讓
語言集合的所有寫入被擋下，直到你修正它為止；若有兩個以上格式不合的資料列，API 既修不了也刪不掉它們。請先檢查
已儲存的語言資料列裡是否有不符合 `[A-Za-z0-9_-]{1,35}` 的代碼，並在升級前直接在資料庫裡修正或刪除這些資料列。

若已啟用 OIDC，且 `Oidc:Authority` 的路徑含有 `common`、`organizations` 或 `consumers` 其中一段（或 consumers
租用戶的 GUID），升級前請先設定 `Oidc:AllowedTenantId` 或 `Oidc:AllowedEmailDomains`，否則主機會拒絕啟動。

若你的 `.env` 是從較早的 `.env.example` 複製來的，裡面會有 `STRUO_ADMIN_PASSWORD=admin`。升級前請清空它，或改成
符合 `Auth:Password` 的值：compose 的 `api` 服務會執行 `Database:MigrateOnStartup`，遇到這個值就拒絕啟動，資料庫
已經植入過也一樣。

## 0.10.0 — 2026-10-14

::: danger 破壞性變更
- REST 回應裡每個 `DateTime` 都是以 `Z` 結尾的 UTC 時刻，例如 `2026-10-14T02:00:00Z`。多數欄位以前是沒有
  時區的字串。把那些字串當成本地時間讀的客戶端，現在會讀成不同的時間，請檢查解析這些值的程式。
  從這版起擷取的修訂快照也帶 `Z`，REST 與 GraphQL 都一樣。
- 透過 API 寫入只有日期的值（例如 `2026-10-14`）是 UTC 午夜，回傳 `2026-10-14T00:00:00Z`，所以 UTC 以西
  的瀏覽器會顯示前一天。
:::

### 修正

- 管理後台的日期時間欄位顯示瀏覽器本地時間。以前在 UTC+8 輸入 10:00，重新載入後會顯示 02:00。
- 透過 REST 或 GraphQL 寫入帶時差的日期時間（例如 `2026-10-14T10:00:00+08:00`），在一般的 `DateTime`
  欄位，以及 MySQL、MariaDB 與 SQLite 上帶時區的欄位，現在存成它指的那個時刻。以前是存成 API 主機的本地時間。

### 升級說明

0.9.4 及更早版本，在 UTC 以外的主機上，透過 API 寫入帶非 `Z` 時差的值，在一般的 `DateTime` 欄位，
以及 MySQL、MariaDB 與 SQLite 上帶時區的欄位，存進去的是主機本地時間。那些資料列和正確的看不出差別，
需要的話得手動修正。PostgreSQL 與 SQL Server 上帶時區的欄位，從 0.9.2 起就存得正確。

管理後台寫入的值一直都是對的。0.9.4 及更早版本產生的修訂快照，仍是沒有時區的字串。

## 0.9.4 — 2026-10-14

### 安全性

- `System.Security.Cryptography.Pkcs` 升到 10.0.13。`Microsoft.Data.SqlClient`（經由 `SqlSugarCore` 與
  `FluentMigrator.Runner.SqlServer`）和 `Oracle.ManagedDataAccess.Core` 帶進的 9.0.0 受 GHSA-846f-f764-76mx
  （高）與 GHSA-2fmv-rv2c-59x7（中）影響，兩者都是 .NET 的阻斷服務弱點。NuGet 稽核會因此讓 0.9.3 及更早
  版本的還原失敗。

## 0.9.3 — 2026-10-13

### 修正

- 一般 `DateTime` 欄位（沒有掛 `TimestampWithTimeZone`，例如 `CreatedAt`、`UpdatedAt`、`DeletedAt`
  與 fork 自己的日期欄位）用 `_eq`、`_neq`、`_gt`、`_gte`、`_lt`、`_lte`、`_in`、`_nin` 篩選時，帶 `Z` 或時差的值
  照它指的時刻比，沒有時區的值當成 UTC。0.9.2 及更早版本把這種值換成 API 主機的當地時間來比，所以 API
  主機不在 UTC 時，結果會差一個時差；改版後這類查詢的結果會跟著變，這是行為修正。
- SQL Server 上，對掛 `TimestampWithTimeZone` 的欄位算 `min`／`max` 不再擲出
  `InvalidCastException`。
- PostgreSQL 上，這些欄位的 `min`／`max` 回傳 UTC 時刻（JSON 結尾帶 `Z`），不再帶 API 主機的時差。

### 安全性

這幾項都是建置工具的間接依賴，不會進到打包後的程式：

- 前端與文件站的 `source-map-js` 升到 1.2.2（GHSA-68fv-2mgg-jv7q）。
- 文件站的 `vue` 升到 3.5.43，帶進修好的 `@vue/server-renderer`（GHSA-g2v6-rqmx-r4w6）。
- 前端的 `postcss-selector-parser` 升到 7.1.6（GHSA-rj75-hqrm-r3gf）。`@tailwindcss/typography` 把它
  鎖在 6.0.10，6.x 沒有修補版，所以 `frontend/pnpm-workspace.yaml` 用 override 跨主版號升級。

### 升級說明

不需要處理資料：CMS 自己寫入的時間戳（稽核欄位、修訂、工作階段等）一直都是對的，改的只有篩選與彙總怎麼讀值。

## 0.9.2 — 2026-10-13

### 修正

- 掛 `[ColumnShape(ColumnShape.TimestampWithTimeZone)]` 的 `DateTime` 欄位，在 PostgreSQL 與
  SQL Server 上存的是 UTC 時刻，讀回 `DateTimeKind.Utc`，不受 PostgreSQL 伺服器 `TimeZone` 與 API
  主機時區影響。核心裡是 `UserSession.CreatedAt`／`ExpiresAt`、`SiteSettings.UpdatedAt`、
  `MediaFolder.CreatedAt`／`UpdatedAt`。
- 更新媒體資料夾時 `MediaFolder.CreatedAt` 保持不變，不管 API 主機與 PostgreSQL 的時區是否相同。
- SQL Server 上這些欄位保留到 100 奈秒，不會被捨入到 1/300 秒。
- REST 與 GraphQL 用 `_eq`、`_neq`、`_gt`、`_gte`、`_lt`、`_lte`、`_in`、`_nin` 篩選這些欄位時，值
  當成時刻比較：帶 `Z` 或時差的值照它指的時刻，沒有時區的值當成 UTC，結果不受資料庫伺服器與 API
  主機的時區影響，每個後端都一樣。0.9.1 的 MySQL、MariaDB 與 SQLite 把帶 `Z` 或時差的值換成 API
  主機的當地時間來比，PostgreSQL 依連線時區解讀沒有時區的值。`_contains`、`_starts_with`、`_ends_with`
  照舊。
- PostgreSQL 與 SQL Server 上，這些欄位在 API JSON 裡是結尾帶 `Z` 的 UTC 字串，例如 `MediaFolder`
  的 `createdAt`、`updatedAt`，以及 fork 自己掛 `TimestampWithTimeZone` 的欄位。PostgreSQL 原先輸出
  API 主機的時差（如 `+08:00`），SQL Server 沒有時區後綴；用戶端照字串解析時要確認這點。

### 升級說明

只有 PostgreSQL 需要處理，而且只在 0.9.1 或更早版本寫入資料時，連線的時區（伺服器的 `TimeZone`，
連線字串有 `Timezone` 時以它為準）不是 UTC。那段期間寫入的值都提早了該時區的時差，0.9.2 會照實際
時刻讀出，看起來就差了那個時差。

查舊時區時，用 API 的資料庫帳號連到 API 的資料庫，在沒有設定 `PGTZ` 的 psql 裡跑
`SHOW timezone;`：`ALTER DATABASE` 或 `ALTER ROLE` 設的 `timezone` 會讓它和伺服器預設不同。
API 的連線字串設了 `Timezone`，或 API 主機設了 `PGTZ` 環境變數時，舊時區就是那個值。

先備份資料庫，再停下舊版 API，在啟動 0.9.2 之前跑一次下面的修正，把
`'Asia/Taipei'` 換成舊時區；表名前綴不是 `struo_` 時換成 `Database:TablePrefix` 的值。0.9.2
啟動後寫入的資料已經正確，修正只能跑一次，跑兩次或在新資料寫入後才跑都會再位移一次。fork 自己掛
`TimestampWithTimeZone` 的欄位照同樣寫法修正。

```sql
BEGIN;
UPDATE struo_user_sessions
   SET createdat = (createdat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC',
       expiresat = (expiresat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
UPDATE struo_site_settings
   SET updatedat = (updatedat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
UPDATE struo_media_folders
   SET createdat = (createdat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC',
       updatedat = (updatedat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
COMMIT;
```

psql 依序印出 `BEGIN`、三行 `UPDATE <列數>` 與 `COMMIT`。

API 主機時區與 PostgreSQL 時區不同的環境，`MediaFolder.CreatedAt` 已經累積的位移，這段修正救不
回來：資料庫裡沒有原本的值。SQL Server、MySQL、MariaDB 與 SQLite 不用處理。

## 0.9.1 — 2026-10-13

::: danger 破壞性變更
- Compose 預設把每個埠只綁在 `127.0.0.1`，只有跑這套服務的機器連得到 PostgreSQL、Redis、API、
  後台與 MinIO。從其他機器連進來的人要設定 `STRUO_BIND_ADDRESS=0.0.0.0`（寫進 `.env` 或設在
  shell），埠就會開在所有網路介面上；固定的開發密碼與沒有密碼的 Redis 也會對網路公開。
:::

### 修正

- `migrate:preview` 印出核心種子的 `INSERT` 時，bootstrap 管理員的密碼雜湊以 `<redacted>`
  取代，管理員 email 照常顯示。`migrate` 與 `MigrateOnStartup` 寫入的仍是真正的雜湊。

## 0.9.0 — 2026-10-12

::: danger 破壞性變更
- 資料表只由 FluentMigrator migration 建立；啟動時 CodeFirst 不再建表。還有 migration 沒套用時，API 拒絕啟動。
  把 `migrate`（`dotnet run --project src/Struo.Api -- migrate`，映像內是 `dotnet Struo.Api.dll migrate`）
  放進部署步驟，或設 `Database:MigrateOnStartup=true`。
- 集合、翻譯 sidecar 與 junction 的表要有 migration：每個都用
  `make:migration <名稱> --entity <型別> --output <目錄>` 產生，再用 `migrate` 套用。
- v0.8.x 建出來的既有資料庫，啟動、`migrate` 與 `MigrateOnStartup` 都會拒絕並要求 `migrate:baseline`。
  先替 fork 自己已存在的表產生 migration，再跑一次 `migrate:baseline`，之後才用 `migrate`。
  完整步驟見[第 24 章](24-upgrading-from-0-8.md)。
- `Database:MigrationsPath`、`Database:AutoSyncSchema` 與 `db/migrations/` 移除，`.sql` 腳本不再執行；
  設定鍵還在時啟動會記警告。之後的結構變更寫成繼承 `StruoMigration` 的 FluentMigrator migration。
  `{prefix}schema_migrations` 表不再使用，版本記在 `{prefix}schema_versions`。
- 種子資料改由核心的種子 migration 寫入，在 `migrate` 或 `MigrateOnStartup` 期間執行，讀
  `Auth:BootstrapAdmin:*`、`Localization` 與 `Rbac:PublicReadCollections`；跑 `migrate` 的環境要有這些設定。
- 移除的公開型別：`DatabaseInitializer`、`MigrationRunner`、`SchemaMigration`、`SchemaGuard`
  （連同 `TranslationSidecarDescriptor`）、
  `DataSeeder`、`LanguageSeeder`、`AdminUserSeeder`、`RbacSeeder`（後三者只留在測試專案當輔助類別）。
  呼叫它們的 fork 程式碼要移除。
- Development 環境啟動時以 `SchemaChecker` 比對資料庫與 entity，任何錯誤都中止啟動（取代 `SchemaGuard`，
  檢查範圍更廣）。用 `migrate:check` 看明細。
- Compose 服務不再有固定的 `container_name`（`struo-postgres` 變成 `<專案名>-postgres-1`）；
  用 `docker exec struo-postgres` 的腳本要改。具名 volume 名稱不變，資料照舊。
- 從 v0.8.x 升上來的資料庫還要做各後端的後續處理（SQL Server 的 access token 索引與
  `varchar` → `nvarchar`；MySQL／MariaDB 的 `datetime(6)` 與 utf8mb4），見第 24 章。
:::

### 新增

- `Struo.Api` 的 migration 指令（第一個引數）：`migrate`、`migrate:status`、`migrate:preview`、
  `migrate:baseline`、`migrate:check`、`make:migration`。結束碼：0 成功、1 失敗、2 用法錯誤。
- 設定鍵 `Database:MigrateOnStartup`（預設 `false`）與 `Database:MigrationLockTimeoutSeconds`
  （預設 60，範圍 1–3600）。
- `migrate` 會取得 advisory lock，並行執行時每支 migration 只套用一次：PostgreSQL、MySQL／MariaDB
  與 SQL Server 有鎖；SQLite 與 Oracle 沒有，Oracle 會記警告。
- `StruoMigration` 基底類別：`Db`、`FrameworkTable(...)`、`CreateTranslationUniqueIndex(...)`，以及
  欄位輔助方法 `AsJson(Db)`、`AsLongText(Db)`、`AsTimestamp(Db)`、`AsShape(shape, Db)`。
- `SchemaChecker.Check(db, dbType, entityTypes)`：fork 的測試可斷言 `HasErrors` 為 false。
- `ColumnShape.Timestamp`。
- 核心 migration `202610080000_CreateCoreSchema` 與 `202610080001_SeedCoreData`；範例 Blog 附
  `202610080100_CreateBlogSchema`。
- `docker compose up -d --build` 起整套環境：postgres、redis、`api`（埠 8081）與 `admin`（埠 8080）；
  變數 `STRUO_ADMIN_PORT`、`STRUO_API_PORT`、`STRUO_ADMIN_EMAIL`、`STRUO_ADMIN_PASSWORD`。
- 新依賴 `FluentMigrator.Runner.*` 8.0.1（Core、Postgres、SqlServer、MySql、SQLite、Oracle）。
- 測試連線設定鍵 `Testing:SqlServerConnection`、`Testing:MySqlConnection`、
  `Testing:MariaDbConnection`，只認 `STRUO_TEST_SQLSERVER_CONNECTION`、
  `STRUO_TEST_MYSQL_CONNECTION`、`STRUO_TEST_MARIADB_CONNECTION` 覆寫；`STRUO_TEST_BACKEND`
  把整套測試搬到實際後端。見第 22 章。
- `migrate` 與 `MigrateOnStartup` 不把執行的 SQL 寫進記錄，因為種子的 `INSERT` 含預設管理員的
  密碼雜湊。`migrate:preview` 會印出這段 SQL，輸出要當機密處理。

### 變更

- SQL Server 2025、MySQL 8.4 與 MariaDB 11.8 列為已驗證的後端，與 PostgreSQL 並列；Oracle 仍未驗證。
  版本表見 `AGENTS.md` 的「Verification」。
- SQL Server 的字串欄位用 `nvarchar`；`migrate:check` 對 `varchar`／`char`／`text` 欄位發出
  `NonUnicodeString` 警告。
- MySQL／MariaDB 上沒有指定形狀的 `DateTime` 是 `datetime(6)`（微秒）；其他後端不變。
- API 映像不再含 `db/migrations`。CI 的 docker job 在 smoke test 前先跑 `migrate`，並加測 Compose 環境。
- `docker compose up -d` 會一併建置並啟動 API 與後台。開發時只要依賴服務，改用
  `docker compose up -d postgres redis`；要加 MinIO，用
  `docker compose --profile s3 up -d postgres redis minio createbuckets`。
- 後端套件升級：`AWSSDK.S3` 4.0.104.1、`NetVips.Native` 8.18.7、`Scalar.AspNetCore` 2.17.13、
  `coverlet.collector` 10.1.0。
- 前端套件升級：11 個 `@tiptap/*` 套件 3.31.4、`@lucide/vue` 1.49.0、`@tanstack/vue-table` 9.2.6、
  `vue-i18n` 11.4.13、`vite` 8.3.2、`@types/node` 26.6.4、`jsdom` 30.1.2、`vue-tsc` 3.3.12。

### 修正

- SQL Server：沒有 access token 的使用者可以並存；`accesstoken` 索引改為非唯一（舊的唯一索引只容許
  一筆 NULL）。只影響新建的資料庫，升級的資料庫見第 24 章。
- SQL Server：整數欄位的 `aggregate[avg]` 不再用整數除法。
- SQL Server：`int` 欄位的 `aggregate[sum]` 超過 2^31 不再溢位。
- SQL Server：取消的請求以取消例外呈現，不再是 `SqlSugarException`。
- MySQL／MariaDB：一秒內的兩次寫入維持順序（微秒時間戳）。只影響新建的資料庫。
- 檔名超過 255 個 UTF-16 編碼單位的上傳不再失敗：檔名截到欄位寬度並保留副檔名；檔名與標題的截斷都不會切開代理對。
- 同一個行程使用多個資料庫後端時，欄位型別與資料列繫結不再互相混用（`ProviderScopedReflectionCache`）。

## 0.8.2 — 2026-10-01

### 變更

- 後端套件升級：`AWSSDK.S3` 4.0.103.4、`HotChocolate.AspNetCore` 16.6.7、`Scalar.AspNetCore` 2.17.10。
- 前端套件升級：`@lucide/vue` 1.48.0、`vite` 8.3.1、`@types/node` 26.6.3。

## 0.8.1 — 2026-10-01

### 修正

- 後台側欄的版號改為讀取 `frontend/package.json` 的 `version`（建置時由 Vite 注入），不再是寫死的字串。

## 0.8.0 — 2026-10-01

::: danger 破壞性變更
- 框架自帶的 12 張資料表現在帶有前綴，由 `Database:TablePrefix` 決定，預設 `struo_`（`users` →
  `struo_users`，`schema_migrations` → `struo_schema_migrations`）。範例專案與你自己的集合不加前綴。
  對既有資料庫：啟動時會在新名稱下建出一組空的框架資料表並植入初始資料（含預設管理員），舊表不會
  被讀取。二選一：把 `Database:TablePrefix` 設成空字串沿用舊表；或把舊表改成帶前綴的名稱再啟動。
- 核心資料表的索引與唯一約束名稱跟著表名走：`ix_<表名>_<欄位>`、`ux_<表名>_<意義>`，例如
  `ux_struo_revisions_item_no`。自己寫的 migration 若引用舊索引名稱，要一併更新。
- 有設定 `Database:MigrationsPath` 的 fork 要注意：追蹤表也帶前綴（`struo_schema_migrations`），新表是空
  的，所以什麼都不做就啟動會把目錄裡每一支腳本重跑一次，第一支對既有表的 `ALTER` 就會失敗並中止啟動。
  處理方式同上：把前綴設成空字串，或連同其他表一起把 `schema_migrations` 改名。
- 自己寫的 migration 若以名稱指到核心資料表（例如 `ALTER TABLE users`），要改成帶前綴的名稱
  （`struo_users`）。把舊表改名而不是重建時，舊的索引名稱會留下來，不會自動變成 `ux_struo_…`。
- `LanguageSeeder.SeedAsync` 與 `DataSeeder.SeedAsync` 多了一個 `LocalizationOptions` 參數；只影響直接呼叫它們的 fork 程式碼。
- 沒有任何啟用語言時，`ILanguageProvider.DefaultCode()` 擲出例外，不再回傳 `"en"`。正常情況下走不到這裡（見下方 `language` 集合的規則）。
- 後台 SPA 的介面語言 API 改了：`UiLocale` 型別搬到 `@/locales`；`DEFAULT_UI_LOCALE` 移除；
  `resolveInitialUiLocale(saved, enabled, fallback)` 改為純函式；`uiLocaleStore.set()` 回傳布林值並拒絕
  未啟用的語系；掛載前必須先呼叫 `uiLocaleStore.configure()`。fork 若改過這幾個檔案要跟著調整。
- i18n 的 fallback 語系改為 `AdminUi:DefaultLocale`（出貨為 `zh-TW`），不再固定是 `en`。
- 既有的 `struo_languages` 表若已違反規則（例如零個或兩個預設、`en` 與 `EN` 並存），任何讓它維持違規
  的寫入都會被拒絕；做一次修正寫入即可。
:::

### 新增

- `Database:TablePrefix` 設定鍵（環境變數 `Database__TablePrefix`），啟動時驗證格式。
- `Localization` 設定段：`struo_languages` 表第一次建立時的種子來源（`Languages`、`DefaultLanguage`），啟動時驗證。
- `AdminUi` 設定段：後台提供的介面語言與預設（`Locales`、`DefaultLocale`），經 `GET /api/config` 的 `uiLocales`、`uiDefaultLocale` 送給前端；只啟用一個語言時切換器不出現。
- `language` 集合的寫入規則：`code` 不分大小寫唯一、至少一列啟用、啟用列中恰好一列為預設；違反回 400。

## 0.7.0 — 2026-09-23

專案轉為開源時的現狀。版本紀錄從這一版開始。
