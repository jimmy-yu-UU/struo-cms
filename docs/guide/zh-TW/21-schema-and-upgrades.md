# 21. 資料庫結構管理與升級

資料表從哪裡來、要怎麼改，以及核心的結構變動怎麼進到你的 fork——這一章講這三件事。資料庫是
v0.8.x 用 CodeFirst 建出來的話，先照[第 24 章：接上 v0.8.x 的資料庫](24-upgrading-from-0-8.md)
把它接上 migration。

## Migration 是唯一的結構來源

每一張表、每一個欄位與索引，都來自一支 FluentMigrator migration。API 不會照 entity 自己建表或改表；
套用過的 migration 記在版本表 `{前綴}schema_versions` 裡，前綴就是 `Database:TablePrefix`，
預設 `struo_`。

Migration 放在三個地方：

- **核心**：`src/Struo.Infrastructure/Migrations/Core/`。`CreateCoreSchema` 建框架資料表，
  `SeedCoreData` 植入語言、`admin` 與 `public` 兩個角色、bootstrap 管理員，以及
  `Rbac:PublicReadCollections` 列出的公開讀取權限。
- **範例**：`samples/Struo.Sample.Blog/Migrations/` 的 `CreateBlogSchema`，只有範例列在
  `Struo:ContentAssemblies` 裡時才會被掃到。
- **你的 fork**：放在自己的內容專案裡。這個專案要列在 `Struo:ContentAssemblies`，並參照
  `Struo.Infrastructure`。

命令會掃描 `Struo.Infrastructure` 加上 `Struo:ContentAssemblies` 列出的每一個組件，依版本號由
小到大執行。植入資料也是 migration 的一部分，所以只在 `migrate` 或 `MigrateOnStartup` 時發生。

## 六個命令

六個命令都是 `Struo.Api` 的第一個參數：

| 命令 | 做什麼 |
|---|---|
| `migrate` | 套用所有待套用的 migration |
| `migrate:status` | 列出每支 migration 的狀態 |
| `migrate:preview` | 印出待套用 migration 的 SQL，不執行 |
| `migrate:check` | 比對資料庫與 entity，唯讀 |
| `migrate:baseline` | 把每支 migration 記成已套用，不執行 |
| `make:migration` | 產生一個新的 migration 檔 |

在儲存庫裡用 `dotnet run --project src/Struo.Api -- <命令>`。這會套用 `http` 啟動設定檔，環境是
Development，讀你的 `appsettings.Development.json`。發佈出去的成品用 `dotnet Struo.Api.dll <命令>`；
API 映像的進入點就是它，所以 `docker run --rm … <映像> <命令>` 也行。

命令讀的設定跟 API 一樣：appsettings 加上 `Section__Key` 環境變數，設定鍵也可以接在命令後面，
例如 `--Database:ConnectionString=…`。結束代碼 `0` 是成功，`1` 是失敗，`2` 是用法錯誤。

`migrate` 先印出 FluentMigrator 每一步的紀錄，最後每套用一支印一行 `Applied <版本>  <名稱>`；
沒有東西要套用時只印 `Nothing to migrate.`。`migrate:status` 每支 migration 一行：

```
$ dotnet run --project src/Struo.Api -- migrate:status
202610080000   Applied   2026-10-12 09:57:49Z  CreateCoreSchema
202610080001   Applied   2026-10-12 09:57:49Z  SeedCoreData
202610080100   Applied   2026-10-12 09:57:49Z  CreateBlogSchema
3 applied, 0 pending, 0 orphaned
```

`Pending` 是還沒套用，`Orphaned` 是版本表裡有紀錄、但掃描到的組件裡已經找不到那支
migration，例如把範例從 `Struo:ContentAssemblies` 拿掉之後。孤兒紀錄不會擋住啟動。

`migrate:preview` 印出待套用 migration 會執行的 SQL，什麼都不寫，最後一行是
`-- <n> migration(s) pending`。

> 在新資料庫上，預覽裡有 `SeedCoreData` 的 `INSERT`，其中包含 bootstrap 管理員的 email 與密碼
> 雜湊。把這份輸出當成機密處理，不要貼進 issue 或公開的 CI 紀錄。

`migrate:check` 比對實際資料表與 entity 宣告的結構，不拿鎖、不寫入，乾淨時只印
`0 error(s), 0 warning(s)`。錯誤包括缺表、缺欄、可否為 null 不同、型別類別不同、字串長度不同，
以及缺了該有的唯一索引；有任何錯誤就以代碼 `1` 結束。警告包括資料庫裡有、但沒有 entity 宣告的
欄位，以及 SQL Server 上的非 Unicode 字串欄位。

`make:migration` 在〈撰寫 migration〉一節，`migrate:baseline` 在第 24 章。

## 啟動時的檢查

API 開始服務之前，`StartupMigrationGate` 先看資料庫。`Database:MigrateOnStartup` 在每個環境的
預設都是 `false`：這時只要還有待套用的 migration，API 就拒絕啟動，以代碼 `1` 結束：

```
[17:57:36 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 3 migration(s) pending, the lowest is 202610080000; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

所以部署流程要把 `migrate` 當成獨立的一步，在新版 API 上線之前跑完。設成 `true` 時，API 會先
拿 migration 鎖、套用待套用的 migration，再開始服務；本機的 Compose 環境就是這樣跑的。

框架資料表已經存在、卻沒有版本表時，兩種設定都拒絕啟動，訊息要你先跑一次
`migrate:baseline`，見[第 24 章](24-upgrading-from-0-8.md)。

在 Development，啟動還會跑一次跟 `migrate:check` 相同的比對，有錯誤就拒絕啟動。其他環境不跑
這一步，要自己跑 `migrate:check`。

## 版本表、鎖與交易

版本表 `{前綴}schema_versions` 有 `version`、`appliedon`、`description` 三欄，`version` 上有
唯一索引。一支 migration 有沒有套用過，只看版本號。

`migrate`、`migrate:baseline` 與 `MigrateOnStartup` 都會先拿一把以表前綴命名的 advisory lock：
PostgreSQL 用 `pg_try_advisory_lock`，MySQL／MariaDB 用 `GET_LOCK`，SQL Server 用
`sp_getapplock`。好幾個副本同時啟動時，只有一個在套用，其他的等它做完，再發現沒有東西要套用。
等待上限是 `Database:MigrationLockTimeoutSeconds`（預設 60，範圍 1 到 3600），逾時就失敗。
SQLite 與 Oracle 不拿鎖；Oracle 會記一筆警告，不支援同時跑 `migrate`。

每支 migration 在自己的交易裡執行。失敗時回滾那一支，訊息點名最小的未套用版本，已經套用的
保持套用。MySQL、MariaDB 與 Oracle 的 DDL 會隱含提交，失敗的那一支可能只做了一半。Migration
只往前走，沒有回滾命令，部署前先備份資料庫。

## 撰寫 migration

`make:migration` 產生 migration 檔，只讀 entity 的 metadata，不碰資料庫，也從不覆寫已經存在的
檔案：

```
make:migration <Name> [--entity <Type>] --output <dir> [--namespace <Ns>]
```

- 版本號取 UTC 時間到分鐘（`yyyyMMddHHmm`），撞到目錄裡已有的檔案就往後推一分鐘。
- `--entity` 接一個集合、翻譯 sidecar 或 junction 的型別名稱，產生一支照它的 metadata 建表的
  `Create.Table`；省略時產生空的骨架。
- `--output` 的目錄必須已經存在。相對路徑從行程的工作目錄算起，用
  `dotnet run --project src/Struo.Api` 時那是 `src/Struo.Api`，所以要寫
  `../../<你的專案>/Migrations`，或直接寫絕對路徑。

範例的 `Tag` 產生出來是這樣（開頭的 using 與 namespace、後面的欄位與 `Down()` 省略）：

```csharp
[Migration(202610120958, "CreateTags")]
public sealed class CreateTags : StruoMigration
{
    public override void Up()
    {
        Create.Table("tags")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
…
```

寫 migration 時的規則：

- 繼承 `StruoMigration`，用 FluentMigrator 的可攜呼叫（`Create.Table`、`Alter.Table`、
  `Create.Index`、`Insert.IntoTable`）。各後端型別不同的欄位用 `AsTimestamp(Db)`、`AsJson(Db)`、
  `AsLongText(Db)`，或 `AsShape(ColumnShape.<形狀>, Db)`。
- 框架資料表的名稱用 `FrameworkTable("users")`，它會套上表前綴；你自己的表沒有前綴。
- 只有某個後端真的需要不同寫法時才分支，而且用常數：`if (Db == StruoDbType.MySql)`。不要用
  FluentMigrator 的 `IfDatabase("…")`：它吃一個自由字串，拼錯就什麼都不做，migration 照樣記成
  已套用。
- 不寫 SQL 字串，也不引用 entity 類別：migration 是寫下那一刻的結構快照。
- 一支 migration 可能已經在某處套用過，就不要再改它，另外加一支。

自動比對看不出意圖，碰到既有資料的改動要拆成幾步：

1. **改欄位名稱**：寫一支 `Rename.Column` 的 migration，再改 entity；「砍一欄、加一欄」會丟資料。
2. **移除屬性**：確定那一欄真的不用了，再用一支 `Delete.Column` 拿掉。
3. **限縮型別**：加新欄位、回填並驗證、再切換並刪掉舊欄位；直接限縮會失敗或截斷資料。
4. **對已有資料的表加 `NOT NULL`**：先加可為 null 的欄位、回填，最後才補 `NOT NULL`。
5. **對有重複值的欄位加唯一索引**：先去重，再建索引。
6. **拆分或合併欄位**：一定要寫一支搬資料的 migration。

寫完先用 `migrate:preview` 看 SQL，再對一份你實際用的後端的拋棄式資料庫跑 `migrate` 與
`migrate:check`；SQLite 接受的 DDL，別的後端不一定接受。fork 自己的測試可以呼叫
`SchemaChecker.Check`，斷言沒有錯誤。

## 核心的四個 raw SQL 例外

「所有資料庫存取都走 SqlSugar」這條不變式（見[第 2 章：系統架構](02-architecture.md)）有四個
刻意的例外：

- **關聯篩選的子查詢包裝**：在 SqlSugar 產生的 SQL 外面組四種固定形式。
- **`OrderByExpressionBuilder` 的 `ORDER BY` 文字**：欄位、關聯路徑與翻譯欄位三種排序形式。
- **Migration 鎖 `MigrationLockSql`**：每個後端一組取得與釋放鎖的參數化語句。
- **`IndexCatalog` 的索引查詢**：`migrate:check` 讀索引用的唯讀系統目錄查詢；Oracle 沒有這支
  查詢，索引檢查以警告跳過。

## 升級核心

核心的結構變動以核心 migration 的形式放在 `Struo.Infrastructure` 裡，合併新版核心就一起進到你的
fork；[版本紀錄](changelog.md)會點名每一支新的核心 migration。合併之後，`migrate:status` 把它們
列成 `Pending`。先用 `migrate:preview` 看要執行的 SQL，在備份過的資料庫上跑 `migrate`，再跑
`migrate:check`。

## 各後端的差異

- **MySQL／MariaDB**：migration 不指定字元集，資料表沿用資料庫的預設值。建資料庫時明確指定
  `CHARACTER SET utf8mb4`；SqlSugar 的 `CreateDatabase` 這類工具可能建出 utf8mb3，存不了 emoji。
  用伺服器預設的嚴格 `sql_mode`，非嚴格模式會悄悄截斷或替換值。
- **SQL Server**：`migrate:check` 對 `varchar` 欄位發警告；超出定序字碼頁的字元存進去會變成 `?`。
- **Oracle**：只有型別對應，沒有實測過。不拿鎖，`migrate:check` 跳過型別、長度與索引檢查並發警告。
- **單元測試用 CodeFirst 建表時**，唯一群組索引叫 `Index_{表}_{欄位}_Unique`，要控制在 63 個字元
  以內：MySQL／MariaDB 拒絕超過 64 個字元的名稱，PostgreSQL 會截斷超過 63 個字元的名稱。

## 接下來

結構管理談到這裡；下一步是這個系統有哪幾層測試、CI 又跑了什麼，見
[第 22 章：測試與 CI](22-testing.md)。
