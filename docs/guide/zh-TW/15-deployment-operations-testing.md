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
| `RateLimiting:LoginAccount:Enabled` | `true` (the shipped default) for essentially every deployment | `AuthController.Login` 會在花費任何 Argon2id CPU 之前先呼叫 `ILoginAttemptThrottle`
(`src/Struo.Infrastructure/Identity/DistributedCacheLoginAttemptThrottle.cs`)，依請求本文中的**帳號**
分區，而不是依 client IP。把它關掉，會讓一次針對單一帳號、來自多個來源 IP 的緩慢分散式密碼噴灑攻擊
完全不受限制，因為下方的 `RateLimiting:Login` 看不到請求本文，也就沒辦法依被嘗試的帳號分區。跟這張表
裡另外兩個速率限制器不同，這一層是逐 pod 還是全域，取決於 `Redis:ConnectionString`：有設定時，它與
session ticket 存放區共用同一個 `IDistributedCache`，讓每一個 replica 共用同一個帳號的同一份計數器，
而不是各自擁有一份——但即使如此，這仍然只是一份共用計數器，不是一個保證的上限：`IDistributedCache`
沒有原子性的讀取-寫回操作，針對同一個帳號同時抵達的兩次失敗嘗試可能會漏掉一次遞增(細節見
`DistributedCacheLoginAttemptThrottle` 的 class doc)；在多 replica 部署中留空時，會悄悄退回與 ticket
存放區相同的行程內、逐 pod 快取，實際生效
的上限會像另外兩個限流器一樣變成大約設定值的 N 倍。 |
| `RateLimiting:Login:Enabled` | `false` (the shipped default) unless the deployment is single-instance, directly reachable, and its users don't share an egress IP | 應用程式內建、逐 client IP 的登入限制器以 `Connection.RemoteIpAddress` 分區，其計數器存在於逐行程的記憶體中(`Program.cs`，`AddRateLimiter`/`AddPolicy("login", …)` 那個區塊)。若在一個 admin 後台部署中，員工共用同一個 NAT 對外 IP，卻把這個值改為 `true`，會把整個辦公室收斂成單一共用桶——在出貨預設的 `5`/`60` 之下，只要幾位員工在差不多時間登入，就會共同觸發限制，變成一次自我加害型的阻斷服務，而且不需要代理設定錯誤就會發生。在一個以上的 replica 上，還會額外變成大約 N 倍於設定值、且不一致的上限，永遠不是一個真正的全域上限——程式碼自己的註解就明白寫著正是為此才「委由 ingress/edge/WAF 處理」。上方的 `RateLimiting:LoginAccount` 才是出貨即開啟的那一層。 |
| Reverse-proxy forwarded headers | The deployment must add `UseForwardedHeaders` (with `KnownProxies`/`KnownNetworks`) itself | `Program.cs`在整條管線中完全沒有呼叫過 `app.UseForwardedHeaders(...)`——登入限制器分區鍵旁邊的註解就明白寫著這件事(「這裡刻意不處理」)。在任何反向代理之後，`Connection.RemoteIpAddress` 都會是**代理伺服器自己的**位址，而不是真正客戶端的位址，因此若仍然在這種代理背後把 `RateLimiting:Login:Enabled` 打開，每一次透過該代理的登入嘗試都會被摺疊進單一個速率限制分區——代理伺服器背後的每一位使用者共用同一個每 60 秒 5 次的額度，是上一列所述共用對外 IP 收斂問題的一個更嚴重版本。 |
| `RateLimiting:Password:Enabled` | `false` only when a global per-user limit doesn't matter for a multi-pod deployment | 改密碼限制器依**已驗證呼叫端的使用者 id** 分區，不是依 `Connection.RemoteIpAddress`，所以它**不會**有上面那一列描述的、反向代理把所有請求收斂成單一共用桶的失效模式。不過它仍然有登入限制器的另一種失效模式：它的計數器是位於行程內、逐 pod 的記憶體(`Program.cs`、`AddPolicy("password", …)`)，所以在 N 個 replica 背後，同一個使用者的請求可能落在不同的 pod 上，讓有效的逐使用者上限變成大約設定值的 N 倍，而不是一個真正的全域上限。 |
| Scalar / OpenAPI | Not reachable in Production — do not rely on network-level blocking alone | `app.MapOpenApi()` 與 `app.MapScalarApiReference(...)` 都由 `if (!app.Environment.IsProduction())`(`Program.cs`)守護。已針對一個 Production 模式的執行個體進行即時驗證：`GET /scalar` → 404、`GET /openapi/v1.json` → 404。(第 10 章記載了鄰近的 GraphQL schema 揭露路由——introspection 與 `GET /graphql?sdl`——它們由 `GraphQl:ExposeSchema` 把關，預設在 Production 同樣是關閉的。) |
| `Redis:ConnectionString` | Set to a real Redis instance for any deployment with more than one API replica, or any deployment where sessions must survive a restart | 留空時會回退到 `AddDistributedMemoryCache()`(`AuthWiring.cs`)——一個支撐 cookie 驗證 ticket 存放區的行程內、逐執行個體快取。重新啟動會遺失每一個 session(強制重新登入)；在負載平衡器背後有一個以上的複本時，每個複本各自擁有自己的 session 存放區，因此一個使用者的 session 只有在核發它的那個複本上才有效。 |
| Cookie `Secure` policy | The reverse proxy/load balancer must terminate HTTPS in front of a Production deployment | `AuthWiring.cs` 只要 `env.IsProduction()` 就會設定 `CookieSecurePolicy.Always`(否則為 `CookieSecurePolicy.SameAsRequest`，這樣開發/測試用的 HTTP host(即執行本章範例的
`Struo.Api` 執行個體)才能繼續運作)。以純 HTTP 提供 Production 服務，代表瀏覽器永遠不會在任何後續請求中把驗證 cookie 送回去——登入看似成功一次，之後卻始終悄悄地不會被保留。 |
| `Oidc:RequireEmailVerified` / `AllowedTenantId` / `AllowedEmailDomains` | Pinned explicitly whenever `Oidc:Enabled=true` | `AllowedTenantId` 出貨時是一個不會相符的預留值 `"REPLACE_TENANT_ID"`(`appsettings.json`)，而 `ExternalLoginService.ResolveOrProvisionAsync`(`src/Struo.Application/Security/ExternalLoginService.cs`)會拒絕任何租戶不等於它的外部身分(`TenantNotAllowed`)——因此在**出貨**預設值之下，外部登入對每一個真實租戶都會失敗封閉，直到這個值被換成真正的租戶為止。不過 `RequireEmailVerified`(`false`)與 `AllowedEmailDomains`(`[]`)確實預設寬鬆：一旦 `AllowedTenantId` 被設為一個真實、相符的租戶，剩下的檢查就都是選用的，而連結接下來就只會依**電子郵件相等性**進行——任何外部帳號，只要其宣稱的電子郵件與一個既有本機使用者相符，就會被當成該使用者處理，無論其驗證狀態為何。 |
| Security response headers | Not configurable — the application always sends `X-Content-Type-Options: nosniff`; the reverse proxy is responsible for `Strict-Transport-Security`, `X-Frame-Options`/CSP `frame-ancestors`, and `Referrer-Policy`; `frontend/nginx/default.conf.template` is a working reference implementation | `Program.cs`管線中最前面註冊了一個內建 middleware，會在每一個回應上設定 `X-Content-Type-Options: nosniff`——一個普通的 200、一個錯誤 envelope、一個 CORS preflight、一個裸的 404 皆然——除非下游的處理常式已經自行設定過它。`Struo.Api` 本身不提供任何 HTML(沒有 `wwwroot`、`UseStaticFiles`，也沒有 `MapFallbackToFile`；admin SPA 是另外部署的)，所以這是檔案下載端點 `Content-Disposition: attachment`(`FilesController.Download`)與圖片轉檔路徑固定的點陣圖 content type 之後的縱深防禦，不是在修補一個現存的漏洞。應用程式本身**不會**送出其他常見的標頭：若在反向代理那一層也略過它們，會讓其後方的任何 HTML 表面(例如另外部署的 admin SPA)完全沒有 HSTS 的降級保護、沒有 `X-Frame-Options`/CSP `frame-ancestors` 的點擊劫持防護、也沒有 `Referrer-Policy` 限制外洩到連結目的地的內容——而這個 API 本身的 JSON 回應原本就不存在這類暴露。 |

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

- **快速失敗的選項驗證：** `Database`、`Struo:Files`、`Oidc`、`Query` 與 `Auth:Password` 都以
  `ValidateOnStart` 繫結；一個缺少的 `Database:ConnectionString`、一個缺少 `ClientId` 的
  `Oidc:Enabled=true` 設定，
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

## 容器映像

這個儲存庫出貨兩份 Dockerfile，各自產生一個獨立的 image：根目錄的 `Dockerfile` 建置 API，
`frontend/Dockerfile` 建置管理後台 SPA。這個儲存庫沒有正式環境用的 `docker compose` 檔——見下方
「部署仍然是你的責任」一節。

**建置**——與 CI `docker` job 相同的兩道指令，只是 tag 改成本機用的：

```bash
docker build --tag struo-api:local .
docker build --tag struo-admin:local frontend
```

API image 的建置情境(build context)是整個儲存庫，不只是 `src/Struo.Api/`：一個 fork 的內容專案
是以 `ProjectReference` 從 `Struo.Api.csproj` 參照進來的，因此建置需要那個參照能指向的每一個專案，
加上根目錄的 `Directory.Build.props`/`Directory.Packages.props`(`Dockerfile` 自己的註解 1)。
根目錄的 `.dockerignore` 把 `frontend/`、`docs/`，以及一般的建置/測試產物排除在這個情境之外。

### API image

- 監聽連接埠 `8080`(`EXPOSE 8080`)，並以非 root 使用者 `app` 執行，不是 root。
- `db/migrations` 會被複製進 image，位於 `/app/db/migrations`。`Database:MigrationsPath` 預設留空，
  與 `appsettings.json` 本身「不自動在啟動時執行 migration」的預設值一致——要在這個 image 內開啟
  runner，請明確把它設為 `/app/db/migrations`(必須是絕對路徑;見上方檢查清單那一列)。
- `app` 可寫入兩個目錄：`/app/App_Data`(local 後端上傳檔案與圖片轉檔快取)與 `/app/logs`(Serilog 的
  File sink)。`/app/App_Data` 另外被宣告為一個 `VOLUME`，因此即使是一次沒有 `--mount`/`-v` 的單純
  `docker run`，也會在那裡取得一個匿名 volume，而不是讓寫入悄悄落在容器的可寫層裡。
- `HEALTHCHECK` 每 30 秒對 `http://localhost:8080/health/live` 探測一次(5 秒逾時、30 秒啟動寬限期、
  3 次重試)——是 liveness 路由，不是 `/health/ready`。因此 `docker ps`/`docker inspect` 的健康狀態
  只反映「行程正在接受請求」，不反映資料庫或快取的可連線性;每個路由實際檢查什麼，見下方的健康檢查
  探針表。

已針對 PostgreSQL 進行即時驗證：把 `Database:ConnectionString` 指向一個從容器內部可連線的 PostgreSQL
執行個體(若資料庫執行在主機上，則是 `host.docker.internal`——這只有在 Docker Desktop 上才會開箱
即用可解析；在 Linux 上，需要在 `docker run` 指令加上
`--add-host=host.docker.internal:host-gateway`)，並設定
`Database:MigrationsPath=/app/db/migrations`，啟動時建立了十一張核心資料表，migration runner 也
針對該目錄執行，待處理腳本數為零——`db/migrations/` 只出貨了它的 `README.md`。`/health/ready` 在大約
2 秒內回報 `Healthy`。一次透過 SPA image 完成的真實上傳，最終落在
`/app/App_Data/uploads/<yyyy>/<MM>/<id>.<ext>`，確認了 `Struo:Files:Local:RootPath` 的預設值確實會
解析到這個已宣告的 volume 之內。

#### 環境變數

下方每一個鍵都存在於 `src/Struo.Api/appsettings.json`(完整參考見第 3 章);這裡只列出與執行這個
image 最相關的幾個。

| 變數 | 用途 | 備註 |
|---|---|---|
| `Database__DbType` | 要連線的後端 | `PostgreSQL`(出貨預設值)、`Sqlite`、`MySql`、`SqlServer`，或 `Oracle`(`StruoDbType.cs`);CI 的 smoke test 使用 `Sqlite`。 |
| `Database__ConnectionString` | 對應後端的連線字串 | 出貨時是一整組 PostgreSQL 連線字串，其中的帳密是 `REPLACE_ME` 預留值(`Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME`)——image 若要能啟動，這是必要設定。 |
| `Database__MigrationsPath` | 啟動時要套用的一批已審查 `.sql` migration 腳本所在目錄 | 必須是**絕對路徑**(見上方檢查清單)。留空/未設定(預設)會完全停用 runner。在這個 image 內，migrations 目錄是 `/app/db/migrations`。 |
| `Redis__ConnectionString` | 支撐 session ticket 存放區的分散式快取 | 留空(預設)會回退到行程內快取——單一 replica 沒問題，一個以上就不行。 |
| `Struo__Files__Backend` | `local` 或 `s3` | 預設 `local`，寫入 `Struo:Files:Local:RootPath`(`App_Data/uploads`，位於已宣告的 volume 之內)。 |
| `Struo__Files__S3__Endpoint` / `__Bucket` / `__AccessKey` / `__SecretKey` | S3 相容儲存憑證 | 只有在 `Struo__Files__Backend=s3` 時才會被查閱;這四個全部出貨為 `REPLACE_ME` 預留值。 |
| `Struo__Files__S3__Region` | S3 區域 | 出貨時有一個真正的預設值 `us-east-1`，不是預留值——若儲存桶不在該區域，請自行覆寫。 |
| `Auth__BootstrapAdmin__Email` / `__Password` | 覆寫種子建立的預設 admin 帳號 | 只會在 `users` 資料表**第一次**被建立時讀取——見上方檢查清單那一列。 |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core 的 hosting 環境 | 這個 image 中完全沒有設定它(`docker image inspect struo-api:ci` 顯示 env 清單裡沒有 `ASPNETCORE_ENVIRONMENT`)——它之所以預設為 `Production`，是因為這個變數不存在時，那就是 ASP.NET Core 框架自身的預設值，不是這份 `Dockerfile` 自己設定的。原因見下方段落。 |

因為 `ASPNETCORE_ENVIRONMENT` 預設為 `Production`，`CookieSecurePolicy.Always` 就會生效(上方檢查
清單中的 Cookie `Secure` policy 那一列)：以純 HTTP 登入看似成功，但瀏覽器在之後的請求中永遠不會把
cookie 送回去。請在容器前面終結 TLS，或者只在本機、純 HTTP 的 smoke test 中設定
`ASPNETCORE_ENVIRONMENT=Development`——絕不可用於真正的部署。

#### DataProtection 金鑰

ASP.NET Core 的 DataProtection 金鑰環(key ring)——負責簽署與加密驗證 cookie 與 antiforgery
token——會被寫在容器內部的 `/home/app/.aspnet/DataProtection-Keys`。這個 image 中沒有任何東西會
持久化或共用這個目錄：它沒有被掛載成 volume，應用程式也會在啟動時針對這件事記錄一則警告
(「No XML encryptor configured」)。這則記錄同時也是在警告金鑰環本身的儲存格式：在沒有設定
encryptor 的情況下，DataProtection 會以未加密的明文形式將金鑰儲存在磁碟上。因此，任何為金鑰環
掛載的目錄裡存放的都是明文金鑰材料，必須比照其他機密來保護它——檔案系統權限、備份加密、存取
記錄——而不是當成一般的應用程式狀態來對待。容器不持久化這個目錄，會直接帶來兩個後果：替換容器(一次重新部署、一次因映像更新而重啟)會讓每一個既有的驗證 cookie 與 antiforgery
token 全部失效，強迫每一位使用者重新登入；而執行一個以上的 replica，會讓每個 replica 各自擁有一份
互不相通的金鑰環，導致負載平衡器把請求路由到與核發者不同的 replica 時，cookie 驗證與 antiforgery
雙雙失敗。若只有單一實例，請把 `/home/app/.aspnet/DataProtection-Keys` 掛載成一個 volume，讓金鑰能
在容器被替換後存活。若有一個以上的 replica，請改為設定一個共用的 DataProtection 金鑰存放區(一個共用
檔案系統、Redis，或雲端供應商自己的金鑰環服務)——這個儲存庫本身並未設定任何一種;請把這當成這個
image 一個誠實的限制，而不是一項功能。

### 管理後台 SPA image

- 監聽連接埠 `80`(`EXPOSE 80`)，透過 nginx 提供 Vite 正式環境建置的產物。
- 這個 image 的建置會採納一份已提交的 `frontend/.env.production`(`frontend/.env.example` 有
  廣告這個檔案)：若是跨來源(cross-origin)部署，請在裡面設定 `VITE_API_BASE_URL`；若要沿用這個
  image 提供的同來源(same-origin)`/api` 代理，就讓它保持未設定。
- `API_UPSTREAM`(預設 `http://api:8080`)是要把 `/api/*` 反向代理過去的後端。它必須是
  `scheme://host:port`，**沒有路徑、也沒有結尾斜線**：`proxy_pass` 使用的是一個 nginx 變數而不是
  字面值(這樣容器才能在 API 尚未可解析時也照樣啟動)，而在使用變數目標時，`API_UPSTREAM` 攜帶的任何
  路徑片段都會*取代*請求的 URI，而不是被加在前面(`frontend/nginx/default.conf.template`，
  註解 (d))。
- `HSTS_VALUE` 預設留空，代表這個 image 完全**不會**送出 `Strict-Transport-Security`——因為
  nginx 對於值為空字串的 `add_header` 會直接不送出。只有在這裡、或某個前置代理，已經針對每一個能
  抵達這個容器的請求終結了 TLS 之後，才設定它(例如 `max-age=31536000; includeSubDomains`)。
- 不論 `HSTS_VALUE` 為何，都一律會送出三個安全標頭：`X-Content-Type-Options: nosniff`、
  `X-Frame-Options: DENY`，以及 `Referrer-Policy: strict-origin-when-cross-origin`。這正是上方檢查
  清單中 Security response headers 那一列所說的「reverse proxy 負責」的意思——
  `frontend/nginx/default.conf.template` 是這個責任的一個可執行的參考實作，不是規定一定要用
  nginx。API 自己送出的 `X-Content-Type-Options: nosniff` 在代理過的 `/api/*` 回應上會被隱藏
  (`proxy_hide_header`)，讓這個標頭在那些回應上恰好只出現一次，而不是兩次。
- 快取：`index.html` 以 `Cache-Control: no-cache` 提供(它參照的是內容雜湊過的 bundle，一份過期的
  快取副本會一直指向新版部署已經替換掉的檔案)；`/assets/` 底下的一切則以
  `Cache-Control: public, max-age=31536000, immutable` 提供(Vite 的檔名帶有內容雜湊，因此每次新
  建置都是新的 URL)。SPA 的文字型回應開啟了 gzip，`server_tokens off` 讓 nginx 不再對外揭露自己的
  版本號，而 `client_max_body_size 32m` 在後端的 `Struo:Files:MaxUploadBytes`(25 MiB /
  26214400 bytes，`src/Struo.Api/appsettings.json`)之上留了一些餘裕，讓一個真正過大的上傳能拿到
  後端自己的 JSON 錯誤 envelope，而不是 nginx 的 HTML 錯誤頁。
- `NGINX_RESOLVER`(預設 `127.0.0.11`，Docker 內建的 DNS，`resolver_timeout` 為 5 秒)只存在於
  **user-defined** 的 Docker network 上——也就是 `docker compose` 或 `docker network create` 產生
  的那種。在預設的 bridge network 上(一次沒有 `--network` 的單純 `docker run`)，每一個 `/api/*`
  請求都會在解析逾時後以 502 失敗，因為那裡根本沒有東西在監聽 `127.0.0.11`。請把兩個容器放在同一個
  user-defined network 上，或把 `NGINX_RESOLVER` 指向一個從 image 執行環境真正可連線的 DNS
  伺服器。

### 部署仍然是你的責任

單靠這兩個 image 本身並不構成一次部署——除了上方的 DataProtection 金鑰環之外：

- **HTTPS 終結。** 兩個 image 都不會自行終結 TLS；兩者都預期前面有一個反向代理或負載平衡器
  (上方檢查清單中的 Cookie `Secure` policy 那一列)。
- **`UseForwardedHeaders`。** 若 API 位於任何反向代理之後——包括管理後台 SPA image 自己的
  nginx——請設定 `UseForwardedHeaders`，並附上明確的 `KnownProxies`/`KnownNetworks` 允許清單，如上方
  檢查清單那一列已涵蓋的。
- **編排(Orchestration)。** 重啟原則、擴縮、密鑰注入，以及把健康檢查接進實際執行這些 image 的
  平台(Kubernetes、ECS、一個帶 `--restart` 的單純 `docker run`……)，都在兩份 Dockerfile 提供的範圍
  之外。這個儲存庫不出貨正式環境用的 `docker compose` 檔。

### 一起驗證這兩個 image

確認兩個 image 真的能互相溝通的最小方式——這是一份**驗證用的操作步驟**，不是一個部署拓樸：

```bash
docker network create struo-verify
docker run --detach --name api --network struo-verify \
  --env Database__DbType=Sqlite \
  --env 'Database__ConnectionString=Data Source=/app/App_Data/struo.db' \
  struo-api:local
docker run --detach --name admin --network struo-verify --publish 8081:80 \
  struo-admin:local
```

admin 容器預設的 `API_UPSTREAM=http://api:8080` 之所以能解析成功，是因為兩個容器都位於同一個
user-defined network `struo-verify` 上，而且 API 容器命名為 `api`——與上方 `NGINX_RESOLVER` 那段
說明相同的道理。打開 `http://localhost:8081`，確認 SPA 能載入，且它的 `/api/*` 呼叫能抵達後端。

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

`.github/workflows/ci.yml` 定義了六個 job。`backend`、`frontend`、`docs` 與 `docker` 都會在推送到
`main`、每一次 pull request，以及手動觸發時被觸發；`sonar-backend` 與 `sonar-frontend` 則額外加了
一個條件(見它們各自下方的說明)：

- **`backend`**——`dotnet restore`、`dotnet build --no-restore --configuration Release`，接著
  `dotnet test --no-build --configuration Release --verbosity normal`——完整的後端單元/整合套件。
  這個工作流程中的任何地方都沒有設定 `STRUO_TEST_PG_CONNECTION`，因此即時 PostgreSQL 套件中的測試
  在 CI 裡全部都會走無操作通過的路徑；那裡只有 SQLite 支援的測試才會真正演練任何東西。
- **`frontend`**——`pnpm install --frozen-lockfile --ignore-scripts`，接著 `pnpm test`，然後 `pnpm build`——前端
  單元套件，加上一次完整的正式環境建置(`vue-tsc -b && vite build`)，同時也是 CI 對這個 SPA 的
  TypeScript 型別唯一的強制檢查。
- **`docs`**——在 `docs/` 底下執行 `pnpm install --frozen-lockfile --ignore-scripts`，接著
  `pnpm build`——這是手冊自己的關卡：`vitepress build` 會解析每一個跨章節連結，遇到失效連結就失敗；
  接著一個 `check-rendered-chapters.mjs` 步驟會斷言每一章確實渲染出非空內容，因為單靠
  `vitepress build` 即使某一頁渲染成空白，結束代碼仍然是 `0`。
- **`docker`**——建置兩個容器 image(根目錄的 `Dockerfile` 對應 API，`frontend/Dockerfile` 對應
  管理後台 SPA;細節見上方「容器映像」一節)，並對每一個都做 smoke test：API image 以 SQLite 啟動
  (`Database__DbType=Sqlite`、`Database__ConnectionString=Data Source=/tmp/struo-ci.db`)，job 反覆
  探測 `/health/ready` 直到回報健康;SPA image 啟動後，job 對回應的 `/` 內容做 grep，確認其中含有
  `assets/index-`，證明建置出來的 bundle 確實能透過 nginx 存取。這個 job 刻意**不是**第六個 standing
  gate——貢獻者不需要在本機安裝 Docker 也能開發這個儲存庫——而且和下方的兩個 `sonar-*` job 不同，它
  不需要任何 secret，因此無論是來自 fork 的 pull request、Dependabot，或一次普通的 push，執行方式
  都完全相同。
- **`sonar-backend`** / **`sonar-frontend`**——回報給 SonarQube Cloud。兩者都不是 standing
  gate，而且都會在來自 fork 的 pull request 與 Dependabot 時被跳過，因為兩者都需要 `SONAR_TOKEN`
  這個那些情境讀不到的 secret——恰好與上方的 `docker` job 相反。

五個 standing gate 仍然是 `dotnet build`、`dotnet test`、`pnpm test` 與 `pnpm build`(後兩者來自
`frontend/`)，加上來自 `docs/` 的 `pnpm build`——`docker`、`sonar-backend` 與 `sonar-frontend` 是
額外的 job，不是額外的 gate。

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
