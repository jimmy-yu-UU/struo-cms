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
| `Database:MigrationsPath` | An **absolute** path | `MigrationRunner.ApplyAsync`(`src/Struo.Infrastructure/Persistence/MigrationRunner.cs`)會把設定值原封不動地傳給 `Directory.Exists(...)`，自己完全不做 content-root 解析——不同於 `Struo:Files:ImageTransform:CachePath`，它會明確針對 `IHostEnvironment.ContentRootPath` 解析(`AddStruoFiles` 的 `IImageVariantCache` 工廠，`FileStorageServiceCollectionExtensions.cs`)。因此一個相對路徑，會在啟動時針對**行程目前的工作目錄**解析，而這並不保證就是應用程式自己的資料夾(一個 systemd unit 的 `WorkingDirectory`、一個容器的 `WORKDIR`，或任何在啟動行程之前先 `cd` 到別處的啟動器，都可能與它不同)。已即時確認(`db/migrations/README.md` 第 6 節)：一個只相對於儲存庫根目錄存在、並非相對於行程實際工作目錄的相對路徑，會拋出 `DirectoryNotFoundException: MigrationRunner: migrations directory not found: '<path>'`，行程並以代碼 `1` 結束。這個 runner 本身現在會在**每一個**後端上執行，不只 PostgreSQL；把這個鍵留空(預設值)會在任何後端上完全停用它。 |
| `Auth:BootstrapAdmin:Password` | Overridden before the **first** boot against a fresh database | 只會被查閱一次，也就是 `users` 資料表第一次被建立的當下(`DataSeeder.cs`)；之後的啟動永遠不會再讀取它。一次 `Production` 啟動，若仍然用字面預設值 `admin` 完成種子資料建立，會記錄一則 `WARNING`，指名確切要變更的設定(`DataSeeder.WarnIfDefaultAdminPasswordInProduction`)，但**不會**拒絕啟動——已即時驗證：`[17:30:16 WRN] Bootstrap admin is using the default password 'admin'. Change it immediately via Auth__BootstrapAdmin__Password.` 這則警告比較的是**目前設定的**值與字面字串 `"admin"`(在 `WarnIfDefaultAdminPasswordInProduction` 內部)，而不是該帳號實際儲存的密碼雜湊——因此一個維運者若先前讓資料庫以預設值完成種子建立，之後才在設定中改用一個高強度的值，會讓這則警告在之後每一次啟動時都被靜音，即使所儲存的帳號實際上仍然是原本那組預設密碼的雜湊。 |
| `RateLimiting:Login:Enabled` | `false` only when per-IP limiting is enforced at the ingress/edge | 應用程式內建的登入限制器以 `Connection.RemoteIpAddress` 分區，其計數器存在於逐行程的記憶體中(`Program.cs`，`AddRateLimiter`/`AddPolicy("login", …)` 那個區塊)。若在一個扇出到 N 個複本的負載平衡器背後仍保留為 `true`，實際生效的上限會不一致地變成大約 N 倍於設定值，而且永遠不是一個真正的全域上限——程式碼自己的註解就明白寫著正是為此才「委由 ingress/edge/WAF 處理」。 |
| Reverse-proxy forwarded headers | The deployment must add `UseForwardedHeaders` (with `KnownProxies`/`KnownNetworks`) itself | `Program.cs`在整條管線中完全沒有呼叫過 `app.UseForwardedHeaders(...)`——登入限制器分區鍵旁邊的註解就明白寫著這件事(「這裡刻意不處理」)。在任何反向代理之後，`Connection.RemoteIpAddress` 都會是**代理伺服器自己的**位址，而不是真正客戶端的位址，因此每一次透過該代理的登入嘗試都會被摺疊進單一個速率限制分區——結果要嘛是代理伺服器背後的每一位使用者共用同一個每 60 秒 5 次的額度(一次意外的自我加害型阻斷服務)，要嘛在搭配 `RateLimiting:Login:Enabled=false` 時，若邊緣層實際上也沒有提供逐 IP 保護，就會完全沒有任何逐 IP 保護存在。 |
| `RateLimiting:Password:Enabled` | `false` only when a global per-user limit doesn't matter for a multi-pod deployment | 改密碼限制器依**已驗證呼叫端的使用者 id** 分區，不是依 `Connection.RemoteIpAddress`，所以它**不會**有上面那一列描述的、反向代理把所有請求收斂成單一共用桶的失效模式。不過它仍然有登入限制器的另一種失效模式：它的計數器是位於行程內、逐 pod 的記憶體(`Program.cs`、`AddPolicy("password", …)`)，所以在 N 個 replica 背後，同一個使用者的請求可能落在不同的 pod 上，讓有效的逐使用者上限變成大約設定值的 N 倍，而不是一個真正的全域上限。 |
| Scalar / OpenAPI | Not reachable in Production — do not rely on network-level blocking alone | `app.MapOpenApi()` 與 `app.MapScalarApiReference(...)` 都由 `if (!app.Environment.IsProduction())`(`Program.cs`)守護。已針對一個 Production 模式的執行個體進行即時驗證：`GET /scalar` → 404、`GET /openapi/v1.json` → 404。(第 10 章記載了鄰近的 GraphQL schema 揭露路由——introspection 與 `GET /graphql?sdl`——它們由 `GraphQl:ExposeSchema` 把關，預設在 Production 同樣是關閉的。) |
| `Redis:ConnectionString` | Set to a real Redis instance for any deployment with more than one API replica, or any deployment where sessions must survive a restart | 留空時會回退到 `AddDistributedMemoryCache()`(`AuthWiring.cs`)——一個支撐 cookie 驗證 ticket 存放區的行程內、逐執行個體快取。重新啟動會遺失每一個 session(強制重新登入)；在負載平衡器背後有一個以上的複本時，每個複本各自擁有自己的 session 存放區，因此一個使用者的 session 只有在核發它的那個複本上才有效。 |
| Cookie `Secure` policy | The reverse proxy/load balancer must terminate HTTPS in front of a Production deployment | `AuthWiring.cs` 只要 `env.IsProduction()` 就會設定 `CookieSecurePolicy.Always`(否則為 `CookieSecurePolicy.SameAsRequest`，這樣開發/測試用的 HTTP host(即執行本章範例的
`Struo.Api` 執行個體)才能繼續運作)。以純 HTTP 提供 Production 服務，代表瀏覽器永遠不會在任何後續請求中把驗證 cookie 送回去——登入看似成功一次，之後卻始終悄悄地不會被保留。 |
| `Oidc:RequireEmailVerified` / `AllowedTenantId` / `AllowedEmailDomains` | Pinned explicitly whenever `Oidc:Enabled=true` | `AllowedTenantId` 出貨時是一個不會相符的預留值 `"REPLACE_TENANT_ID"`(`appsettings.json`)，而 `ExternalLoginService.ResolveOrProvisionAsync`(`src/Struo.Application/Security/ExternalLoginService.cs`)會拒絕任何租戶不等於它的外部身分(`TenantNotAllowed`)——因此在**出貨**預設值之下，外部登入對每一個真實租戶都會失敗封閉，直到這個值被換成真正的租戶為止。不過 `RequireEmailVerified`(`false`)與 `AllowedEmailDomains`(`[]`)確實預設寬鬆：一旦 `AllowedTenantId` 被設為一個真實、相符的租戶，剩下的檢查就都是選用的，而連結接下來就只會依**電子郵件相等性**進行——任何外部帳號，只要其宣稱的電子郵件與一個既有本機使用者相符，就會被當成該使用者處理，無論其驗證狀態為何。 |

## Schema 管理

Schema 管理依**職責**、而非依環境，拆分為三層：CodeFirst 建立不存在的資料表(永遠、每個環境都做)、
`Database:AutoSyncSchema` 依自動 diff 修改既有資料表(僅 Development，預設關閉)，以及 `MigrationRunner`
套用受審查的 `.sql` 腳本(全環境，預設關閉)。完整的職責對照表在 `db/migrations/README.md` §2;本節談的是
一個**部署**必須做對的部分，以及下方的 auto-sync 風險。

**啟動順序**(`Program.cs`)：表名快照 → `CreateMissingTables`(全環境、全後端、無條件執行) →
選用的 `SyncSchema`(只有在 `Database:AutoSyncSchema=true` **且** host 處於 Development 時才會
真正執行完整同步——在其他任何環境設為 true，都會被忽略並記錄一則警告，而不是被採納) →
`MigrationRunner.ApplyAsync`(只有在設定了 `Database:MigrationsPath` 時執行) → 僅限 dev 的
`SchemaGuard` → `DataSeeder` 的種子植入。建表之所以排在最前面，是因為 migration runner 的
`ALTER` 腳本鎖定的是必須已經存在的資料表；`SchemaGuard` 與種子植入都排在最後，因為兩者都依賴 schema
已經是最終形狀。

**在每一種情況下都成立的唯一初始化保證：** 在任何環境、任何一種已設定的五種後端上，只要某個 entity
型別對應的資料表尚不存在，它就會在其他任何動作之前被建立，接著 `DataSeeder` 會為該次啟動新建的每一張
資料表植入初始資料。這是刻意做到跨環境、跨後端一致的——它補上了這個儲存庫過去確實存在的一個落差：一個
設定為 `MySql`、`SqlServer` 或 `Oracle` 的 `Production` 部署，過去會針對一個完全空白的資料庫乾淨地
啟動(沒有 schema、沒有種子資料，`DbReadinessCheck` 卻回報 Healthy)，直到第一次查詢才失敗——因為建表
過去只在 Development 執行，而 migration runner 過去只把關 PostgreSQL。話雖如此，「一致」描述的是*程式
碼路徑*，不是這個路徑背後的證據:PostgreSQL 是這個儲存庫唯一針對正在執行中的實例驗證過的後端(第 1
章)。`MySql`、`SqlServer` 與 `Oracle` 依設計做了型別對應，因此預期同一條程式碼路徑在它們身上也會產生
有效的 DDL，但這三個後端都沒有任何一次即時執行能佐證這一點——在你自己實際跑過之前，請把這三個後端上
的建表視為未經驗證。

**永遠不會自動發生的事：既有資料表。** 一張已經存在的資料表，唯二會被改動的方式，是在 Development 中
明確設定 `Database:AutoSyncSchema=true`，或是由 `MigrationRunner` 套用一支已審查的腳本。兩者都是
選用、預設關閉；預設設定在任何後端、任何環境下，都只會建立尚不存在的資料表。

### 自動同步的九項危險情境

`Database:AutoSyncSchema`(bool，預設 `false`，僅限 Development——第 3 章)會開啟針對既有資料表的
完整 CodeFirst 結構同步，允許 SqlSugar 依 entity 類別完全比照新增、修改與刪除欄位。它很強大，而在一張
存有你在乎的資料的資料表上，它很危險。經實測，在真正的 PostgreSQL 上，被移除的欄位確實會被刪除
(`PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres`)。在 SQLite 上，
同一個未過濾呼叫卻會把該欄位原封不動地留下
(`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite`)——原因並不是 SQLite
或 SqlSugar 的 SQLite dialect 缺少這個能力(SqlSugar 的 `SqliteCodeFirst.ExistLogic` 確實實作了
`DROP COLUMN`)，而是這段程式碼被把關在
`ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn` 之後，而這個儲存庫的
`SqlSugarClientFactory` 從未設定過 `MoreSettings`(細節見下方第 7 項)。因此這個儲存庫只跑 SQLite 的
CI 套件，無法證明被移除的欄位真的會被刪除；只有那次真實的 PostgreSQL 執行才證得出來。
總則：

> 任何涉及既有資料的結構變更，一律走 migration。`AutoSyncSchema` 僅適用於 Development 中尚無正式資料
> 的 schema 快速迭代。

| # | 情境 | 自動同步的實際行為 | 正確處理方式 |
|---|---|---|---|
| 1 | **欄位改名** | 視為「舊屬性消失＋新屬性出現」→ `DROP COLUMN` + `ADD COLUMN`，**該欄資料永久遺失** | 先寫 `ALTER TABLE … RENAME COLUMN` migration，再改 entity |
| 2 | 移除屬性 | `DROP COLUMN`，資料遺失 | 確認資料確實不再需要；正式環境改以 migration 明確執行 |
| 3 | 型別收窄(如 `varchar(255)` → `varchar(50)`) | 依引擎不同：失敗，或**靜默截斷** | migration 分三步：新增新欄位 → 回填並驗證 → 切換與移除 |
| 4 | 對既有資料表新增 `NOT NULL` 欄位 | ALTER 失敗，啟動中斷 | migration 三步：先加可空欄位 → 回填 → 再加 NOT NULL 約束 |
| 5 | 對含重複值的欄位加 UNIQUE | ALTER 失敗，啟動中斷 | migration 先去重(資料操作)→ 再加約束 |
| 6 | 拆分／合併欄位、抽出獨立表 | 結構 diff 無法表達此意圖，結果必為資料遺失或空欄位 | 一律 migration |
| 7 | SQLite 後端刪欄位 | 實測(SqlSugarCore 5.1.4.215)：在這個儲存庫裡，被移除的欄位在 SQLite 上會存活下來(`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite`)，原因是 `SqliteCodeFirst.ExistLogic` 把「刪除 entity 已不再宣告的欄位」這件事把關在 `ConnectionConfig.MoreSettings.SqliteCodeFirstEnableDropColumn` 之後，而這個儲存庫的 `SqlSugarClientFactory` 從未設定過它。這個旗標**並未**把關 SQLite 上每一次由 CodeFirst 發出的 `DROP COLUMN`：`ExistLogic` 的改鍵路徑會呼叫 `SqliteCodeFirst.ChangeKey`，後者呼叫 `DbMaintenance.UpdateColumn`，而 `SqliteDbMaintenance.UpdateColumn` 改寫一個欄位的方式是新增暫存欄位、複製資料、再刪掉原欄位(兩次 `DropColumn`，都不檢查旗標)。旗標真正管的是這裡量測的「屬性被移除」情境。對照上游(tag `5.1.4.197`)驗證過：要刪除的集合本身沒有過濾——`Realization/Sqlite/CodeFirst/SqliteCodeFirst.cs:24-27` 把*每一個*被移除的欄位都放進 `dropColumns`，`PRIMARY KEY`、`UNIQUE`、已索引的欄位都不例外，完全沒有排除；而真正執行刪除的 `DbMaintenance.DropColumn`(`Abstract/DbMaintenanceProvider/Methods.cs:532-538`)只是把 `SqliteDbMaintenance` 的 `DropColumnToTableSql`(`Realization/Sqlite/DbMaintenance/SqliteDbMaintenance.cs:118-124`，純粹的 `ALTER TABLE {0} DROP {1}`)格式化後直接執行——沒有任何 table-rebuild 的後備方案，`SqliteDbMaintenance` 也沒有覆寫 `DropColumn` 本身。所以一個把這個旗標打開的 fork，得到的結果不只一種，也不是均勻解鎖：一個普通欄位會被靜默刪除；一個 SQLite 自身原生就會拒絕刪除的欄位(例如 `PRIMARY KEY`、`UNIQUE`、已建立索引、被 `CHECK` 約束或 `FOREIGN KEY` 參照，或被某個 generated column、partial index、trigger 或 view 參照；原生支援自 SQLite 3.35.0 起，2021 年)會讓那句原生 `ALTER TABLE` 失敗、例外往外拋——同步會丟例外，啟動中斷；它**不會**被刪除 | 欄位在這裡會存活下來，原因是這個儲存庫的設定——但打開這個旗標並不會讓 SQLite 均勻地「解鎖」刪欄位：它是用普通欄位的靜默資料遺失，換來受限欄位的啟動硬失敗。勿假設某後端自身原生支援的 DDL 能力，等同於 SqlSugar 的 CodeFirst 同步在該後端上實際做的事——請逐一後端驗證，如同這個儲存庫對 PostgreSQL 所做的 |
| 8 | 多副本(`replicas > 1`)同時啟動 | 各副本同時進行 DDL diff 與執行，存在競態 | 見下方「已知限制」 |
| 9 | 想預覽部署 | **無法預覽**將執行的 DDL(啟動時由 code diff 即時計算) | 這正是不在 Production 啟用的核心理由 |

這裡有這麼多項是真正的危險情境、而非邊角案例，原因是結構性的，不是偶然的：CodeFirst 的自動同步計算的
是實體類別與正在執行中資料表之間的一次**結構 diff**——它知道目標形狀，但完全不知道你的*意圖*為何。
一次改名，與一次「刪一欄、加一欄」，在 diff 眼中完全無法區分。

### 撰寫 migration

撰寫規則——檔案命名、可攜 SQL 的建議/避免對照表、為何**不需要**冪等性、以檔名為準的追蹤方式、具時區
感知的時間戳記慣例，以及 runner 的已知限制(MySQL/Oracle 上 DDL 無法回滾、無 advisory lock、無
checksum/down-migration/dry-run)——全部收錄於 **`db/migrations/README.md`** §§4–6。那份檔案是這些
內容的唯一歸屬;本章不再重述。

其中有兩項限制對部署有直接影響，值得在此重申:

- **無 advisory lock。** 同時啟動、且指向同一個 `Database:MigrationsPath` 的多個副本，可能各自嘗試
  套用同一支待處理檔案。部署 schema 變更時請先讓單一副本完成，或改以一個獨立的一次性 job。同一項限制
  也適用於 CodeFirst 建表與 `AutoSyncSchema`(危險情境 #8)。
- **會複製 `db/migrations/*.sql` 的部署管線，可能不再自動建立該目錄。** template 出貨**零份** `.sql`
  檔案，而先前一律出貨 `001-core-baseline.sql`，因此一個「複製該目錄底下現有內容」的步驟，不再保證
  部署映像中該目錄本身存在。若 `Database:MigrationsPath` 已設定但目錄不存在，啟動時會拋出
  `DirectoryNotFoundException`，行程以結束代碼 `1` 退出(見上方檢查清單中 `Database:MigrationsPath`
  那一列)。請確認管線在此設定值被配置之處仍會建立該目錄——即使是空的。

### 跨 fork 升級核心版本

由於 template 出貨零份 SQL 腳本、而且建表是 create-only 的，**StruoCMS 核心自身的 schema 變更，無法
自動送達一個既有 fork 的部署。** 核心 schema 變更會在 release notes 中公告;每個 fork 依此為自己實際
執行的後端撰寫對應的 `ALTER` 腳本。完整程序見 `db/migrations/README.md` §7，包含把
`AutoSyncSchema=true` 對著 production schema 的一份**複本**執行、當成撰寫時的提示——但絕不可當成執行
計畫，因為上方的危險情境表對那份 diff 的適用程度與其他任何地方完全相同。

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
     at Struo.Infrastructure.Persistence.MigrationRunner.ApplyAsync(ISqlSugarClient db, String migrationsDirectory, ILogger logger, CancellationToken ct)
     at Program.<Main>$(String[] args)
  ```

  堆疊追蹤中的行號刻意省略：這份文字紀錄的產生時間早於後續變更，而那些變更已經讓
  `MigrationRunner.cs` 與 `Program.cs` 兩份檔案裡原本的行號不再對應正確位置——行為本身，以及該行程
  結束代碼確實是 `1` 這件事，都沒有改變。
- **Dev schema 防護：** `SchemaGuard.AssertCriticalConstraintsAsync` 只在 Development 中執行，於
  建表、選用的 `SyncSchema` 與 migration runner 之後，並斷言 `revisions` 的複合 UNIQUE 索引，以及
  每個已設定翻譯附屬資料表的 UNIQUE `(fk, locale)` 索引確實實際存在——若缺少任一個，就拋出一個帶有
  可行動訊息的 `InvalidOperationException`，而不是讓應用程式帶著一個靜默的正確性落差繼續執行。這是
  一個快速失敗的開發便利機制，不是一個 Production 安全網——Production 的 schema 預期已經從 CodeFirst
  建表或一個 fork 自己的 migration 中攜帶了這些索引，而 Production 完全沒有任何自動檢查來確認它們
  確實存在。

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
  與 `RelationInterface` 成員)，從兩側各自斷言：後端的快照測試
  `tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs`，以及前端的契約測試
  `frontend/tests/schemaContract.test.ts`——後者會把同一組檔案，餵進真正的 `selectListColumns`
  與欄位型別 `registry`。這是第四種*類型*的測試，不是第四道指令：兩邊都搭乘在上方已有的
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

Schema 契約關卡完全不需要變更 `ci.yml`：`CoreSchemaSnapshotTests` 只是 `backend` job 既有的
`dotnet test` 步驟中的另一個測試，而 `schemaContract.test.ts` 也只是 `frontend` job 既有的
`pnpm test` 步驟中的另一個測試——兩邊都搭乘在上方已經涵蓋過的同兩道指令之中。

## 接下來該去哪

- 第 3 章 [設定參考](03-configuration-reference.md)，涵蓋上方檢查清單中每一列背後完整的逐鍵語意。
- 第 2 章 [快速入門](02-getting-started.md)，涵蓋健康檢查端點表，以及本章假設已經在執行的相依服務。
- 第 16 章 [範例走查](16-sample-walkthrough.md)，涵蓋如何選用啟用 Blog 範例——這是
  `pnpm e2e:sample` 的先決條件。
