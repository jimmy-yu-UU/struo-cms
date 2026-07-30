# 15. 部署、維運與測試

第 3 章記載了每一個設定鍵的語意。本章談的是若一次正式環境部署把其中某個設定弄錯了會發生什麼事，以及
圍繞在設定之外的維運層面：schema 管理、啟動與失敗行為、記錄、健康檢查探針、備份，以及這個儲存庫出貨
的四個測試層。

## 正式環境檢查清單

下方每一列都是直接對照所引用的原始碼寫成，並在有註明之處，針對一個用完即棄的暫存資料庫，以及本次建置
的第二個 Production 模式執行個體進行了即時驗證——絕不會針對共用的開發資料庫或正在執行的開發 API 進行
驗證。

| 設定 | 必要值 | 設定錯誤時的後果 |
|---|---|---|
| `Database:MigrationsPath` | An **absolute** path | `MigrationRunner.ApplyAsync`(`src/Struo.Infrastructure/Persistence/MigrationRunner.cs`)會把設定值原封不動地傳給 `Directory.Exists(...)`，自己完全不做 content-root 解析——不同於 `Struo:Files:ImageTransform:CachePath`，它會明確針對 `IHostEnvironment.ContentRootPath` 解析(`FileStorageServiceCollectionExtensions.cs:51`)。因此一個相對路徑，會在啟動時針對**行程目前的工作目錄**解析，而這並不保證就是應用程式自己的資料夾(一個 systemd unit 的 `WorkingDirectory`、一個容器的 `WORKDIR`，或任何在啟動行程之前先 `cd` 到別處的啟動器，都可能與它不同)。已即時驗證：從 `src/Struo.Api` 執行，並設定 `MigrationsPath=db/migrations`(只相對於儲存庫根目錄存在，並非相對於該目錄)，會拋出 `DirectoryNotFoundException: MigrationRunner: migrations directory not found: 'db/migrations'`，行程並以代碼 1 結束；改用絕對路徑 `D:/dotnet/struo-cms/db/migrations`，針對一個空的暫存資料庫執行，則正確建立了全部 10 個核心資料表，並在 `schema_migrations` 中記錄了 `001-core-baseline.sql`。 |
| `Auth:BootstrapAdmin:Password` | Overridden before the **first** boot against a fresh database | 只會被查閱一次，也就是 `users` 資料表第一次被建立的當下(`DataSeeder.cs`)；之後的啟動永遠不會再讀取它。一次 `Production` 啟動，若仍然用字面預設值 `admin` 完成種子資料建立，會記錄一則 `WARNING`，指名確切要變更的設定(`DataSeeder.WarnIfDefaultAdminPasswordInProduction`)，但**不會**拒絕啟動——已即時驗證：`[17:30:16 WRN] Bootstrap admin is using the default password 'admin'. Change it immediately via Auth__BootstrapAdmin__Password.` 這則警告比較的是**目前設定的**值與字面字串 `"admin"`(`DataSeeder.cs:63`)，而不是該帳號實際儲存的密碼雜湊——因此一個維運者若先前讓資料庫以預設值完成種子建立，之後才在設定中改用一個高強度的值，會讓這則警告在之後每一次啟動時都被靜音，即使所儲存的帳號實際上仍然是原本那組預設密碼的雜湊。 |
| `RateLimiting:Login:Enabled` | `false` only when per-IP limiting is enforced at the ingress/edge | 應用程式內建的登入限制器以 `Connection.RemoteIpAddress` 分區，其計數器存在於逐行程的記憶體中(`Program.cs`，`AddRateLimiter`/`AddPolicy("login", …)` 那個區塊)。若在一個扇出到 N 個複本的負載平衡器背後仍保留為 `true`，實際生效的上限會不一致地變成大約 N 倍於設定值，而且永遠不是一個真正的全域上限——程式碼自己的註解就明白寫著正是為此才「委由 ingress/edge/WAF 處理」。 |
| Reverse-proxy forwarded headers | The deployment must add `UseForwardedHeaders` (with `KnownProxies`/`KnownNetworks`) itself | `Program.cs`在整條管線中完全沒有呼叫過 `app.UseForwardedHeaders(...)`——登入限制器分區鍵旁邊的註解就明白寫著這件事(「這裡刻意不處理」)。在任何反向代理之後，`Connection.RemoteIpAddress` 都會是**代理伺服器自己的**位址，而不是真正客戶端的位址，因此每一次透過該代理的登入嘗試都會被摺疊進單一個速率限制分區——結果要嘛是代理伺服器背後的每一位使用者共用同一個每 60 秒 5 次的額度(一次意外的自我加害型阻斷服務)，要嘛在搭配 `RateLimiting:Login:Enabled=false` 時，若邊緣層實際上也沒有提供逐 IP 保護，就會完全沒有任何逐 IP 保護存在。 |
| Scalar / OpenAPI | Not reachable in Production — do not rely on network-level blocking alone | `app.MapOpenApi()` 與 `app.MapScalarApiReference(...)` 都由 `if (!app.Environment.IsProduction())`(`Program.cs`)守護。已針對一個 Production 模式的執行個體進行即時驗證：`GET /scalar` → 404、`GET /openapi/v1.json` → 404。(第 10 章記載了唯一一個沒有以相同方式設防的鄰近路由：`GET /graphql?sdl`。) |
| `Redis:ConnectionString` | Set to a real Redis instance for any deployment with more than one API replica, or any deployment where sessions must survive a restart | 留空時會回退到 `AddDistributedMemoryCache()`(`AuthWiring.cs`)——一個支撐 cookie 驗證 ticket 存放區的行程內、逐執行個體快取。重新啟動會遺失每一個 session(強制重新登入)；在負載平衡器背後有一個以上的複本時，每個複本各自擁有自己的 session 存放區，因此一個使用者的 session 只有在核發它的那個複本上才有效。 |
| Cookie `Secure` policy | The reverse proxy/load balancer must terminate HTTPS in front of a Production deployment | `AuthWiring.cs` 只要 `env.IsProduction()` 就會設定 `CookieSecurePolicy.Always`(否則為 `CookieSecurePolicy.SameAsRequest`，這樣開發/測試用的 HTTP host(即執行本章範例的
`Struo.Api` 執行個體)才能繼續運作)。以純 HTTP 提供 Production 服務，代表瀏覽器永遠不會在任何後續請求中把驗證 cookie 送回去——登入看似成功一次，之後卻始終悄悄地不會被保留。 |
| `Oidc:RequireEmailVerified` / `AllowedTenantId` / `AllowedEmailDomains` | Pinned explicitly whenever `Oidc:Enabled=true` | `AllowedTenantId` 出貨時是一個不會相符的預留值 `"REPLACE_TENANT_ID"`(`appsettings.json`)，而 `ExternalLoginService.ResolveOrProvisionAsync`(`src/Struo.Application/Security/ExternalLoginService.cs`)會拒絕任何租戶不等於它的外部身分(`TenantNotAllowed`)——因此在**出貨**預設值之下，外部登入對每一個真實租戶都會失敗封閉，直到這個值被換成真正的租戶為止。不過 `RequireEmailVerified`(`false`)與 `AllowedEmailDomains`(`[]`)確實預設寬鬆：一旦 `AllowedTenantId` 被設為一個真實、相符的租戶，剩下的檢查就都是選用的，而連結接下來就只會依**電子郵件相等性**進行——任何外部帳號，只要其宣稱的電子郵件與一個既有本機使用者相符，就會被當成該使用者處理，無論其驗證狀態為何。 |

## Schema 管理

- **Development：** `InitTables`(SqlSugar CodeFirst)會建立缺少的資料表，並以增量方式新增缺少的
  欄位，直接由實體類別驅動；它只在 `app.Environment.IsDevelopment()`(`Program.cs`)為真時執行，在
  Production 中永遠不會執行。
- **已審核的 migration：** 設定 `Database:MigrationsPath`(第 3 章)會讓 `MigrationRunner` 指向一個
  存放 `NNN-short-kebab-description.sql` 檔案的目錄。一旦設定好，它會在**每一個**環境中執行——這正是
  重點所在，讓已審核的腳本送達 Production 正是整套機制的目的——而且在任何非 PostgreSQL 後端上是一個
  徹底的無操作(`db.CurrentConnectionConfig.DbType != DbType.PostgreSQL` 會直接短路，完全不做任何
  讀寫)。
- **追蹤：** 一個 `schema_migrations (filename text PRIMARY KEY, appliedat timestamptz)` 資料表，
  由 runner 在第一次使用時建立。已套用的檔名只會**以檔名本身**被記錄——沒有 checksum 或內容雜湊——
  因此一個已被記錄為套用過的檔案，即使之後其磁碟內容被編輯過，也絕不會被重新執行
  (`001-core-baseline.sql` 自己的「FILENAME-KEYED TRACKING」註解明白寫出了這個後果：絕不要編輯一個
  可能已經在任何地方被記錄為套用過的檔名；請改為出貨一個新檔案)。
- **撰寫下一支腳本：** `NNN-short-kebab-description.sql`，一個連續、補零的單一序列(下一個編號永遠是
  目前最大值 **+ 1**)，每個檔案一項邏輯變更，具冪等性(`IF NOT EXISTS` / 有防護的 `ALTER` /
  `DO $$ … $$` 存在性檢查)，只能向前(沒有自動的 down-migration——回滾是一支新的補償腳本)。出貨的
  baseline 是 `001-core-baseline.sql`；一個 fork 的第一次 schema 變更是 `002-…`
  (`db/migrations/README.md`)。
- **時間戳記慣例：** 任何新的時間性欄位都使用 `timestamptz`，絕不使用裸的 `timestamp`，儲存 UTC——
  這正是追蹤資料表自己的 `appliedat` 欄位所遵循的慣例。baseline 本身並不一致：大多數
  `AuditableEntity` 的 `createdat`/`updatedat` 欄位都是裸的 `timestamp`，但
  `media_folders.createdat`/`media_folders.updatedat` 與 `site_settings.updatedat`
  (`site_settings` 完全沒有 `createdat` 欄位)已經是 `timestamptz`
  (`db/migrations/001-core-baseline.sql`、`src/Struo.Infrastructure/Files/MediaFolder.cs`)——請
  針對正在變更的資料表直接檢查 baseline，而不要假設是任一種型別。baseline 中既有的裸 `timestamp`
  欄位都沒有被刻意回溯遷移來補上這個落差，因為把已經儲存的值重新錨定到某個 session 時區，是一次靜默的
  資料位移(`db/migrations/README.md`)。

已即時驗證：透過 `MigrationRunner` 把 `001-core-baseline.sql` 套用到一個空的暫存資料庫
(`Database:MigrationsPath` 設為其絕對路徑，`ASPNETCORE_ENVIRONMENT=Production`)，正好產生了十個
核心資料表加上追蹤資料表，並記錄了那唯一一個已套用的檔案：

```
$ docker exec struo-postgres psql -U struo -d struo_probe -c "\dt"
             List of relations
 Schema |       Name        | Type  | Owner
--------+-------------------+-------+-------
 public | file_translations | table | struo
 public | files             | table | struo
 public | languages         | table | struo
 public | media_folders     | table | struo
 public | permissions       | table | struo
 public | revisions         | table | struo
 public | roles             | table | struo
 public | schema_migrations | table | struo
 public | site_settings     | table | struo
 public | user_roles        | table | struo
 public | users             | table | struo
(11 rows)

$ docker exec struo-postgres psql -U struo -d struo_probe -c "SELECT * FROM schema_migrations;"
       filename        |           appliedat
-----------------------+-------------------------------
 001-core-baseline.sql | 2026-07-29 09:32:09.208427+00
(1 row)
```

## 啟動行為與失敗模式

- **快速失敗的選項驗證：** `Database`、`Struo:Files`、`Oidc` 與 `Query` 都以 `ValidateOnStart`
  繫結；一個缺少的 `Database:ConnectionString`、一個缺少 `ClientId` 的 `Oidc:Enabled=true` 設定，
  或一個超出範圍的 `Query:MaxLimit`，都會在應用程式開始監聽之前拋出 `OptionsValidationException`，
  而不是以一次令人困惑的首次請求失敗浮現出來
  (`tests/Struo.Tests/DependencyInjection/OptionsValidationTests.cs` 針對一個真實的泛型 host，正好
  演練了這些案例)。
- **非零結束代碼：** `Program.cs` 把啟動過程包在一個最外層的 `try`/`catch`/`finally` 之中。任何啟動
  例外都會透過 `Log.Fatal` 被記錄下來，並在 `catch` 區塊中把 `Environment.ExitCode` 設為 `1`——程式
  碼自己的註解解釋了原因：如果沒有這一步，例外仍然會被記錄，但行程會以 `0` 結束，因此一個協調器或行程
  監督者會看到一個表面上成功的結束，而永遠不會重新啟動或發出警報。已即時驗證：一個相對於行程工作目錄
  並不存在的 `Database:MigrationsPath` 產生了

  ```
  [17:34:20 FTL] StruoCMS host terminated unexpectedly
  System.IO.DirectoryNotFoundException: MigrationRunner: migrations directory not found: 'db/migrations'. Check the Database:MigrationsPath configuration value.
     at Struo.Infrastructure.Persistence.MigrationRunner.ApplyAsync(ISqlSugarClient db, String migrationsDirectory, ILogger logger, CancellationToken ct) in D:\dotnet\struo-cms\src\Struo.Infrastructure\Persistence\MigrationRunner.cs:line 73
     at Program.<Main>$(String[] args) in D:\dotnet\struo-cms\src\Struo.Api\Program.cs:line 196
  ```

  而該行程自己的結束代碼確實是 `1`。
- **Dev schema 防護：** `SchemaGuard.AssertCriticalConstraintsAsync` 只在 Development 中執行，於
  `InitTables` 與 migration runner 之後，並斷言 `revisions` 的複合 UNIQUE 索引，以及每個已設定翻譯
  附屬資料表的 UNIQUE `(fk, locale)` 索引確實實際存在——若缺少任一個，就拋出一個帶有可行動訊息的
  `InvalidOperationException`，而不是讓應用程式帶著一個靜默的正確性落差繼續執行。這是一個快速失敗的
  開發便利機制，不是一個 Production 安全網；Production 的 schema 預期已經從
  `001-core-baseline.sql` 或一個 fork 自己的 migration 中攜帶了這些索引。

## 記錄與記錄檔

Serilog 完全在 `Serilog:*` 之下設定(第 3 章)：一個 Console sink，以及一個寫入 `logs/struo-.log`
的 File sink，具備逐日輪替與共用檔案存取。已針對這份檢出程式碼自己的記錄目錄進行驗證——每個日曆日一個
檔案，命名為 `struo-YYYYMMDD.log`：

```
$ ls -la src/Struo.Api/logs
...
-rw-r--r-- 1 AzureAD+YuJimmy 4096   1572 Jun 25 17:51 struo-20260625.log
-rw-r--r-- 1 AzureAD+YuJimmy 4096  72757 Jun 26 16:26 struo-20260626.log
...
-rw-r--r-- 1 AzureAD+YuJimmy 4096  46961 Jul 28 20:28 struo-20260728.log
-rw-r--r-- 1 AzureAD+YuJimmy 4096 185558 Jul 29 17:52 struo-20260729.log
```

(每個日曆日一個檔案；上方只顯示最舊與最新的檔案，中間的檔案已省略——在任何持續執行的安裝上，這個目錄
每天都會增加一個項目，因此一個精確的計數會立刻過時。)

就跟第 3 章的其他每一個選項一樣，Serilog 設定只會在啟動時建立一次(bootstrap logger 與完整 logger
皆然)；變更 `Serilog:*` 需要重新啟動行程，即使 `appsettings.json` 上有檔案監看器也一樣。
`builder.Host.UseSerilog(...)` 會同時從 `IConfiguration` 與 DI 容器讀取(`ReadFrom.Services`)，
因此任何透過 DI 註冊的 enricher 也都會被納入。

## 給協調器用的健康檢查探針

第 2 章介紹了這兩個健康檢查路由；這裡要說明的是每一個實際檢查了什麼，供接入協調器的
liveness/readiness 探針使用：

| 路由 | 執行的檢查 | 用途 |
|---|---|---|
| `/health/live` | 沒有 (`Predicate = _ => false`)——只確認行程正在接受請求 | Liveness 探針：若這個路由完全停止回應，就重新啟動 container/pod。 |
| `/health/ready` | 每一個標記為 `"ready"` 的檢查：`DbReadinessCheck`(對設定的資料庫執行一次 `IsValidConnection()` 往返)與 `CacheReadinessCheck`(透過設定的 `IDistributedCache`——Redis，或在 `Redis:ConnectionString` 為空時的記憶體內備援方案——執行一次 `SetString`/`GetString` 往返) | Readiness 探針：在資料庫與快取後端兩者都可連線之前，不要把流量導向這個執行個體。 |

## 備份

- **PostgreSQL** 是除了已上傳檔案位元組之外，其他一切的系統記錄——用一般的 PostgreSQL 工具
  (`pg_dump`、`pg_basebackup`，或代管服務自己的快照/PITR 功能)備份它。StruoCMS 之中沒有任何東西
  會取代或凌駕一般的 PostgreSQL 備份實務。
- **已上傳的檔案** 存放在資料庫之外：備份無論哪一個已設定的 `Struo:Files:Backend`——在 `local` 模式
  下是 `Struo:Files:Local:RootPath` 目錄，或在 `s3` 模式下仰賴該 S3 相容儲存桶自己的版本控制/複寫
  功能(第 3 章、第 11 章)。一次只針對資料庫的備份，會靜默地漏掉每一個已上傳的檔案。
- **Redis** 只保存 cookie 驗證用的 session ticket(`DistributedCacheTicketStore` 是除了
  readiness 檢查之外，`IDistributedCache` 唯一的使用者)——它是一個 session 快取，不是一個系統記錄。
  遺失它只會強制每一位目前已通過驗證的使用者重新登入；它本身不需要任何備份。
- **Migration 腳本與設定**(`db/migrations/`、`appsettings.*`、環境變數/密鑰管理系統的值)是一般
  受版控或部署管線管理的產物——用與部署其餘部分相同的方式備份它們，而不是把它當成一個資料庫層面的
  問題。

## 四個測試層

- **後端單元/整合測試**——`tests/Struo.Tests`(xUnit)，以 `dotnet test` 執行。預設使用 SQLite：
  大多數測試都會為每個測試建立一個獨立的暫存檔資料庫(`Support/SqliteTestDatabase.cs`，於 dispose
  時刪除)。有一個選用啟用的即時 PostgreSQL 套件(`PostgresIntegrationTests`)，專門用來抓
  「SQLite 綠燈 ≠ Postgres 正確」這一類 bug；它只有在設定了 `Testing:PostgresConnection` 時才會
  啟用(這是第 3 章 `Section__Key` 環境變數慣例的唯一例外——這個鍵實際上只有
  `STRUO_TEST_PG_CONNECTION` 有效)，否則其中的每一個測試都會走無操作的通過路徑。它會拒絕針對任何
  名稱不包含 `test` 的資料庫執行，因此請把它指向一個用完即棄的資料庫。
- **前端單元測試**——`pnpm test`(Vitest，`jsdom` 環境，設定於 `frontend/vite.config.ts` 的
  `test` 區塊之中)。元件層級與 lib 層級的 `*.test.ts` 檔案，都放在它們所涵蓋的原始碼旁邊。
- **E2E**——Playwright(`frontend/playwright.config.ts`)，兩個專案：`core`(`pnpm e2e`)針對出貨
  的樣板、在零內容集合的情況下，執行 `e2e/` 底下純框架層級的 spec(排除 `e2e/sample/**`)；
  `sample`(`pnpm e2e:sample`)執行 `e2e/sample/**`，需要先選用啟用 Blog 範例(第 16 章)。
  `pnpm e2e:all` 會同時執行這兩個專案。Playwright 自己的 `webServer` 區塊只會啟動**前端**開發伺服器
  (`pnpm dev`，若已有一個在執行則重複使用)——這兩個專案仍然都需要一個在設定的代理目標上可連線的、
  正在執行的 API 與資料庫；Playwright 設定中沒有任何東西會啟動這兩者中的任何一個。
- **Schema 契約**——一對受版控的檔案，`schema/core-collections.json`(七個核心集合在
  `GET /api/schema` 連線形狀下的樣子)與 `schema/interfaces.json`(每一個已宣告的 `FieldInterface`
  與 `RelationInterface` 成員)，從兩側各自斷言:後端的快照測試
  `tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs`，以及前端的契約測試
  `frontend/tests/schemaContract.test.ts`——後者會把同一組檔案，餵進真正的 `selectListColumns`
  與欄位型別 `registry`。這是第四種*類型*的測試，不是第四道指令:兩邊都搭乘在上方已有的
  `dotnet test` 與 `pnpm test` 之中。`schema/README.md` 是權威來源，包括
  `UPDATE_SCHEMA_SNAPSHOT=1` 這個重新產生快照的步驟。

## CI 會執行什麼——以及它刻意不執行什麼

`.github/workflows/ci.yml` 恰好定義了兩個 job，兩者都會在推送到 `main`、每一次 pull request，以及
手動觸發時被觸發：

- **`backend`**——`dotnet restore`、`dotnet build --no-restore --configuration Release`，接著
  `dotnet test --no-build --configuration Release --verbosity normal`——完整的後端單元/整合套件。
  這個工作流程中的任何地方都沒有設定 `STRUO_TEST_PG_CONNECTION`，因此即時 PostgreSQL 套件中的測試
  在 CI 裡全部都會走無操作通過的路徑；那裡只有 SQLite 支援的測試才會真正演練任何東西。
- **`frontend`**——`pnpm install --frozen-lockfile`，接著 `pnpm test`，然後 `pnpm build`——前端
  單元套件，加上一次完整的正式環境建置(`vue-tsc -b && vite build`)，同時也是 CI 對這個 SPA 的
  TypeScript 型別唯一的強制檢查。

CI 刻意**兩者皆不執行**——既不執行 `pnpm e2e`，也不執行 `pnpm e2e:sample`/`pnpm e2e:all`：
`ci.yml` 中沒有任何一個步驟會啟動資料庫、啟動 API，或呼叫 `playwright test`。端到端涵蓋率需要一個
正在執行的 API 與資料庫，與前端開發伺服器一起運作——這比這裡任何一個 job 所架設的環境都要重——因此在
這個儲存庫中，執行它是一項本機、合併前的自律行為，而不是一道自動化關卡。

Schema 契約關卡完全不需要變更 `ci.yml`:`CoreSchemaSnapshotTests` 只是 `backend` job 既有的
`dotnet test` 步驟中的另一個測試，而 `schemaContract.test.ts` 也只是 `frontend` job 既有的
`pnpm test` 步驟中的另一個測試——兩邊都搭乘在上方已經涵蓋過的同兩道指令之中。

## 接下來該去哪

- 第 3 章 [設定參考](03-configuration-reference.md)，涵蓋上方檢查清單中每一列背後完整的逐鍵語意。
- 第 2 章 [快速入門](02-getting-started.md)，涵蓋健康檢查端點表，以及本章假設已經在執行的相依服務。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，涵蓋如何選用啟用 Blog 範例——這是
  `pnpm e2e:sample` 的先決條件。
