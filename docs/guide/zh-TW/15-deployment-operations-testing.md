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
| `Database:MigrationsPath` | An **absolute** path | `MigrationRunner.ApplyAsync`(`src/Struo.Infrastructure/Persistence/MigrationRunner.cs`)會把設定值原封不動地傳給 `Directory.Exists(...)`，自己完全不做 content-root 解析——不同於 `Struo:Files:ImageTransform:CachePath`，它會明確針對 `IHostEnvironment.ContentRootPath` 解析(`FileStorageServiceCollectionExtensions.cs:51`)。因此一個相對路徑，會在啟動時針對**行程目前的工作目錄**解析，而這並不保證就是應用程式自己的資料夾(一個 systemd unit 的 `WorkingDirectory`、一個容器的 `WORKDIR`，或任何在啟動行程之前先 `cd` 到別處的啟動器，都可能與它不同)。已即時確認(`db/migrations/README.md` 第 6 節)：一個只相對於儲存庫根目錄存在、並非相對於行程實際工作目錄的相對路徑，會拋出 `DirectoryNotFoundException: MigrationRunner: migrations directory not found: '<path>'`，行程並以代碼 `1` 結束。這個 runner 本身現在會在**每一個**後端上執行，不只 PostgreSQL；把這個鍵留空(預設值)會在任何後端上完全停用它。 |
| `Auth:BootstrapAdmin:Password` | Overridden before the **first** boot against a fresh database | 只會被查閱一次，也就是 `users` 資料表第一次被建立的當下(`DataSeeder.cs`)；之後的啟動永遠不會再讀取它。一次 `Production` 啟動，若仍然用字面預設值 `admin` 完成種子資料建立，會記錄一則 `WARNING`，指名確切要變更的設定(`DataSeeder.WarnIfDefaultAdminPasswordInProduction`)，但**不會**拒絕啟動——已即時驗證：`[17:30:16 WRN] Bootstrap admin is using the default password 'admin'. Change it immediately via Auth__BootstrapAdmin__Password.` 這則警告比較的是**目前設定的**值與字面字串 `"admin"`(`DataSeeder.cs:63`)，而不是該帳號實際儲存的密碼雜湊——因此一個維運者若先前讓資料庫以預設值完成種子建立，之後才在設定中改用一個高強度的值，會讓這則警告在之後每一次啟動時都被靜音，即使所儲存的帳號實際上仍然是原本那組預設密碼的雜湊。 |
| `RateLimiting:Login:Enabled` | `false` only when per-IP limiting is enforced at the ingress/edge | 應用程式內建的登入限制器以 `Connection.RemoteIpAddress` 分區，其計數器存在於逐行程的記憶體中(`Program.cs`，`AddRateLimiter`/`AddPolicy("login", …)` 那個區塊)。若在一個扇出到 N 個複本的負載平衡器背後仍保留為 `true`，實際生效的上限會不一致地變成大約 N 倍於設定值，而且永遠不是一個真正的全域上限——程式碼自己的註解就明白寫著正是為此才「委由 ingress/edge/WAF 處理」。 |
| Reverse-proxy forwarded headers | The deployment must add `UseForwardedHeaders` (with `KnownProxies`/`KnownNetworks`) itself | `Program.cs`在整條管線中完全沒有呼叫過 `app.UseForwardedHeaders(...)`——登入限制器分區鍵旁邊的註解就明白寫著這件事(「這裡刻意不處理」)。在任何反向代理之後，`Connection.RemoteIpAddress` 都會是**代理伺服器自己的**位址，而不是真正客戶端的位址，因此每一次透過該代理的登入嘗試都會被摺疊進單一個速率限制分區——結果要嘛是代理伺服器背後的每一位使用者共用同一個每 60 秒 5 次的額度(一次意外的自我加害型阻斷服務)，要嘛在搭配 `RateLimiting:Login:Enabled=false` 時，若邊緣層實際上也沒有提供逐 IP 保護，就會完全沒有任何逐 IP 保護存在。 |
| Scalar / OpenAPI | Not reachable in Production — do not rely on network-level blocking alone | `app.MapOpenApi()` 與 `app.MapScalarApiReference(...)` 都由 `if (!app.Environment.IsProduction())`(`Program.cs`)守護。已針對一個 Production 模式的執行個體進行即時驗證：`GET /scalar` → 404、`GET /openapi/v1.json` → 404。(第 10 章記載了鄰近的 GraphQL schema 揭露路由——introspection 與 `GET /graphql?sdl`——它們由 `GraphQl:ExposeSchema` 把關，預設在 Production 同樣是關閉的。) |
| `Redis:ConnectionString` | Set to a real Redis instance for any deployment with more than one API replica, or any deployment where sessions must survive a restart | 留空時會回退到 `AddDistributedMemoryCache()`(`AuthWiring.cs`)——一個支撐 cookie 驗證 ticket 存放區的行程內、逐執行個體快取。重新啟動會遺失每一個 session(強制重新登入)；在負載平衡器背後有一個以上的複本時，每個複本各自擁有自己的 session 存放區，因此一個使用者的 session 只有在核發它的那個複本上才有效。 |
| Cookie `Secure` policy | The reverse proxy/load balancer must terminate HTTPS in front of a Production deployment | `AuthWiring.cs` 只要 `env.IsProduction()` 就會設定 `CookieSecurePolicy.Always`(否則為 `CookieSecurePolicy.SameAsRequest`，這樣開發/測試用的 HTTP host(即執行本章範例的
`Struo.Api` 執行個體)才能繼續運作)。以純 HTTP 提供 Production 服務，代表瀏覽器永遠不會在任何後續請求中把驗證 cookie 送回去——登入看似成功一次，之後卻始終悄悄地不會被保留。 |
| `Oidc:RequireEmailVerified` / `AllowedTenantId` / `AllowedEmailDomains` | Pinned explicitly whenever `Oidc:Enabled=true` | `AllowedTenantId` 出貨時是一個不會相符的預留值 `"REPLACE_TENANT_ID"`(`appsettings.json`)，而 `ExternalLoginService.ResolveOrProvisionAsync`(`src/Struo.Application/Security/ExternalLoginService.cs`)會拒絕任何租戶不等於它的外部身分(`TenantNotAllowed`)——因此在**出貨**預設值之下，外部登入對每一個真實租戶都會失敗封閉，直到這個值被換成真正的租戶為止。不過 `RequireEmailVerified`(`false`)與 `AllowedEmailDomains`(`[]`)確實預設寬鬆：一旦 `AllowedTenantId` 被設為一個真實、相符的租戶，剩下的檢查就都是選用的，而連結接下來就只會依**電子郵件相等性**進行——任何外部帳號，只要其宣稱的電子郵件與一個既有本機使用者相符，就會被當成該使用者處理，無論其驗證狀態為何。 |

## Schema 管理

Schema 管理依**職責**、而非依環境，拆分為三層：

| 職責 | 執行者 | 環境 | 後端 | 預設 |
|---|---|---|---|---|
| 建立**不存在**的資料表 | CodeFirst(過濾後的 `InitTables`) | 全環境 | 全部五種 | 一律啟用 |
| **改既有**資料表(自動 diff) | 完整 CodeFirst 同步(`SyncSchema`) | 僅 Development | 全部五種 | `Database:AutoSyncSchema=false`——須明確開啟 |
| **改既有**資料表(受審查) | `MigrationRunner` + `Database:MigrationsPath` 下的 `.sql` 腳本 | 全環境 | 全部五種 | `Database:MigrationsPath` 空白 = 關閉 |

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
存有你在乎的資料的資料表上，它很危險。總則：

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
| 7 | SQLite 後端刪欄位 | SQLite 自 3.35.0(2021 年)起已支援 `ALTER TABLE … DROP COLUMN`，但當該欄位是 `PRIMARY KEY`、是 `UNIQUE`、已建立索引，或被某個 generated column、partial index、trigger 或 view 參照時，會拒絕執行 | 後端差異，勿假設各引擎行為一致 |
| 8 | 多副本(`replicas > 1`)同時啟動 | 各副本同時進行 DDL diff 與執行，存在競態 | 見下方「已知限制」 |
| 9 | 想預覽部署 | **無法預覽**將執行的 DDL(啟動時由 code diff 即時計算) | 這正是不在 Production 啟用的核心理由 |

這裡有這麼多項是真正的危險情境、而非邊角案例，原因是結構性的，不是偶然的：CodeFirst 的自動同步計算的
是實體類別與正在執行中資料表之間的一次**結構 diff**——它知道目標形狀，但完全不知道你的*意圖*為何。
一次改名，與一次「刪一欄、加一欄」，在 diff 眼中完全無法區分。

### 撰寫 migration：可攜性與冪等性

盡量使用標準 SQL，以保留未來更換資料庫引擎的可能性：

- `ALTER TABLE … ADD COLUMN` / `DROP COLUMN` / `RENAME COLUMN`
- `CREATE INDEX` / `CREATE UNIQUE INDEX`
- `UPDATE` / `INSERT` / `DELETE`(資料回填)
- 標準型別名：`varchar(n)`、`integer`、`bigint`、`boolean`、`timestamp`、`numeric(p,s)`

建議避免、並附替代方案：

| 避免 | 原因 | 替代 |
|---|---|---|
| PostgreSQL 專屬型別(`jsonb`、`uuid`、`timestamptz`、`serial`) | 其餘四個後端無此型別 | 標準型別；由 ORM 負責應用層映射 |
| `DO $$ … $$` | PL/pgSQL，僅 PostgreSQL | 拆為多個單純語句 |
| `ALTER` / `CREATE INDEX` 上的 `IF NOT EXISTS` | SQL Server 不支援 | **不需要**——追蹤資料表已保證每個檔案只執行一次(見下文) |
| `::` cast 語法 | PostgreSQL 專屬 | `CAST(x AS type)` |
| 方言函式(`now()` vs `GETDATE()` vs `SYSDATE`) | 各引擎不同 | 由應用層傳值；或於各自 fork 中刻意接受此耦合 |
| `RETURNING` | 非標準 | 分開查詢 |

**冪等性不再是必要的，這與早先的指引相反。** 這個儲存庫較早版本的 migration 指引，要求每支腳本都自我
防護(`IF NOT EXISTS`、有防護的 `ALTER`、存在性檢查)——那項要求源自一支需要能安全重複套用的 baseline
bootstrap 腳本，而該 baseline 現已不存在(template 出貨零份 `.sql` 檔案；見下文)。由 CodeFirst 建立
的 `schema_migrations` 追蹤資料表，已經保證每個檔名最多只會被套用一次，因此一支腳本永遠不需要自我防護
以應付被重跑的情況——而 `IF NOT EXISTS` 正是上方「避免清單」中可攜性最差的語法之一(SQL Server 完全
沒有對應的語法)。可攜性現在優先於冪等性：請撰寫語句最單純、不具防護性的形式。

已套用的檔名只會**以檔名本身**被追蹤——沒有 checksum 或內容雜湊——記錄在一個由 CodeFirst 自己建立的
`schema_migrations` 資料表中(因此這套機制本身不帶有任何 vendor SQL)。一個已被記錄為套用過的檔案，
即使之後其磁碟內容被編輯過，也絕不會被重新執行——**絕不要編輯一個可能已經在任何地方套用過的檔名；
請改為出貨一個新檔案。** 檔案命名(`NNN-short-kebab-description.sql`，一個連續、補零的單一序列，
每個檔案一項邏輯變更)及其餘機制細節，記載於 `db/migrations/README.md`，也就是這份指引的權威版本——
本章與該檔案保持一致。

**時間戳記慣例：** 任何新的時間性欄位都應具時區感知(time-zone-aware)並儲存 UTC，而不是裸的
`timestamp`——上方「建議使用」清單裡的標準 SQL `timestamp` 指的只是型別名稱，並非要撤回這項慣例。
如果該欄位也被建模為一個 entity 屬性，請標記
`[ColumnShape(ColumnShape.TimestampWithTimeZone)]`
(`src/Struo.Infrastructure/Persistence/ColumnShape.cs`)，而不是一個 PostgreSQL 專屬的
`timestamptz` 字面型別，這樣 CodeFirst 就會依各後端解析出對應的型別，讓一張全新建立的資料表，與這支
migration 為既有資料表所加上的欄位一致。若是直接手寫 DDL(一個沒有任何 entity 屬性支撐的欄位)？
`ColumnTypeMap.cs` 集中收錄了各後端對應的字面型別可供複製。不論哪一種情況，都請直接檢查目標資料表
實際目前的欄位型別——`src/` 中的 entity 宣告，或是正在執行中的 schema——而不要假設是任一種;這個
儲存庫自己的框架資料表也並不一致。大多數 `AuditableEntity` 的 `createdat`/`updatedat` 欄位都是裸的
`timestamp`，但 `media_folders.createdat`/`updatedat` 已經具時區感知
(兩者皆標記 `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`——
`src/Struo.Infrastructure/Files/MediaFolder.cs`)，`site_settings.updatedat` 以及 migration
runner 自己的追蹤欄位 `schema_migrations.appliedat`
(`src/Struo.Infrastructure/Persistence/SchemaMigration.cs`) 也是如此。沒有任何既有的裸 `timestamp`
欄位曾被回溯性地轉換過，因為把已經儲存的值重新錨定到某個 session 時區，是一次靜默的資料位移。

### 已知限制

1. **DDL 回滾在 MySQL 或 Oracle 上無效。** runner 把每個檔案的執行與其追蹤列的插入包在同一個
   transaction 中，但 MySQL 與 Oracle 的 DDL 都是隱式 commit——在這兩個後端上，若腳本執行到一半失敗，
   已經跑過的 DDL 會原地留下；transaction 無法將其回滾。在這些後端上執行結構變更前請先備份，並在
   維護窗口中執行。
2. **無 advisory lock。** 若多個副本同時針對同一個 `Database:MigrationsPath` 目錄啟動，可能有一個以上
   的行程同時嘗試套用同一支待處理的檔案。部署 schema 變更時，請先讓單一副本完成(或改以一個獨立的
   一次性 job)，而不要仰賴 N 個副本互相競爭。同一項限制也適用於 CodeFirst 建表，以及
   `AutoSyncSchema`(上方危險情境 #8)。
3. **無 checksum、無 down-migration、無 dry-run。** 這個 runner 的職責就是「套用 `ALTER` 腳本並記錄
   已執行的內容」——僅此而已。若你需要 checksum、可回滾的 migration，或 dry-run 模式，請改用專屬工具
   (DbUp、Flyway、Liquibase)；把 `Database:MigrationsPath` 留空會完全停用這套機制，因此不會與其他
   工具衝突。

### 跨 fork 升級核心版本

template 出貨**零份** SQL 腳本，而且建表是 create-only 的——它絕不會動到一張已經存在的資料表。這兩項
事實加在一起，代表 **StruoCMS 核心自身的 schema 變更，無法自動送達一個既有 fork 的部署。**

實務上的做法：

- 核心 schema 變更於 release notes 中明確公告——哪張表變了、哪個欄位、變成什麼型別。
- 每個 fork 依該公告，為自己實際執行的後端撰寫對應的 `ALTER` 腳本，放進自己的 `db/migrations/`。
- 輔助手段：可在 Development 中對一份**複本**的 production schema 執行
  `Database:AutoSyncSchema=true`，藉此觀察 CodeFirst 的 diff 會改動什麼。但請只把它當成撰寫手動腳本
  的提示——**這份 diff 的輸出並非 production 的執行計畫**；上方的危險情境表(破壞性改名、靜默截斷等)
  對這份 diff 的適用程度，與其他任何地方完全相同，因此 diff 提議的變更並不因此自動變得可以照抄進
  migration 腳本。

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
