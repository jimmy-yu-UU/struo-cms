# 24. 接上 v0.8.x 的資料庫

這一章帶一份 v0.8.x 在開機時用 CodeFirst 建出來的資料庫接上 migration，之後就能照
[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)用 `migrate` 管理它。

這種資料庫裡有框架資料表，卻沒有版本表。`migrate:baseline` 就是為它準備的：把每一支已知的
migration 記成已套用，但不執行。下面的步驟在 PostgreSQL、SQL Server、MySQL 與 MariaDB 上都
實際走過。

## 依序執行的步驟

1. **備份資料庫。** 沒有命令能撤銷 migration。
2. **合併新版核心並建置。** `Database:TablePrefix` 保持原值；換了前綴，`migrate` 會在新名稱下
   另建一套空的框架資料表。
3. **拿掉 `Database:MigrationsPath` 與 `Database:AutoSyncSchema`。** 每個 appsettings 檔與環境
   變數都要清。它們沒有任何作用，API 啟動時會為每個還在的鍵記一筆警告：

   ```
   [17:42:58 WRN] The configuration key Database:MigrationsPath is not used: migrations are code in the scanned assemblies and are applied with the 'migrate' command.
   …
   ```

   沒有東西會執行 `.sql` 腳本；它們已經做到資料庫裡的改動留著，確認 entity 類別有描述它們。
4. **在 baseline 之前，替 fork 自己已經存在的表寫 migration。** 每個集合、翻譯 sidecar 與
   junction，只要表已經存在，就跑一次 `make:migration`（`Acme.Content` 代表 fork 的內容專案）：

   ```
   $ dotnet run --project src/Struo.Api -- make:migration CreateNotes --entity Note --output ../../samples/Acme.Content/Migrations --namespace Acme.Content.Migrations
   Created ../../samples/Acme.Content/Migrations\202610120942_CreateNotes.cs
   ```

   檢查產生的檔案，重新建置；用映像部署的話，重建映像。還不存在的表不要在這一步產生：baseline
   會把它記成已套用，那張表就永遠不會被建出來，等第 5 步之後再加。`--output` 的相對路徑從
   `src/Struo.Api` 算起，目錄要先建好，見[第 21 章〈撰寫 migration〉](21-schema-and-upgrades.md#撰寫-migration)。
5. **跑一次 `migrate:baseline`。** 在這之前，`migrate` 與 API 啟動都會拒絕，訊息要你跑
   `migrate:baseline`。baseline 拿 migration 鎖，在一個交易裡寫入核心、範例（有開啟的話）與 fork
   的每一個版本，並列出 FluentMigrator 執行的 SQL：

   ```
   $ dotnet run --project src/Struo.Api -- migrate:baseline
   …
   Recorded 202610080000  CreateCoreSchema
   Recorded 202610080001  SeedCoreData
   Recorded 202610080100  CreateBlogSchema
   Recorded 202610120942  CreateNotes
   4 migration(s) recorded as applied.
   ```

   植入資料只被記錄、沒有執行，既有的使用者、角色與語言不受影響。它只新增版本表，v0.8.x 的
   執行個體還在服務時也能跑。資料庫裡已經有紀錄，或框架資料表不存在時，它拒絕並以代碼 `1`
   結束。
6. **跑 `migrate`。** baseline 之後新增的 migration 會在這裡套用；沒有的話印 `Nothing to migrate.`。
7. **跑 `migrate:check`。** PostgreSQL、MySQL 與 MariaDB 應該是 `0 error(s), 0 warning(s)`。
   SQL Server 對每個 `varchar` 字串欄位發一筆警告：

   ```
   $ dotnet run --project src/Struo.Api -- migrate:check
   WARNING article_tags.note: database has non-Unicode varchar (length 255); characters outside the collation's code page are lost, use nvarchar
   …
   0 error(s), 30 warning(s)
   ```

   警告的數量依你的表而定。任何錯誤都表示某個 entity 跟它的表不一致，通常是 entity 宣告了、表裡
   卻沒有的欄位，或某支 `.sql` 腳本改過、entity 沒有跟上的欄位。先修好再在 Development 啟動，
   Development 的啟動檢查遇到錯誤會拒絕啟動。
8. **做下一節屬於你那個後端的後續處理。** `migrate:check` 看不到這幾件事：SQL Server 的 access
   token 索引、MySQL／MariaDB 的時間精度，以及資料庫的字元集。SQL Server 上先做完再啟動 API：
   在那之前存進去的中文與 emoji 會變成 `?`，救不回來。
9. **啟動 API。**

## 各後端的後續處理

先停掉 API 再跑這些語句。下面的名稱以預設前綴 `struo_` 為例，換了前綴就跟著換。

### SQL Server：access token 索引（必做）

v0.8.x 在 `struo_users.accesstoken` 上建的是唯一索引。SQL Server 的唯一索引只容許一個 NULL，
第二個沒有 access token 的使用者就新增失敗。換成一般索引：

```sql
DROP INDEX ux_struo_users_accesstoken ON struo_users;
CREATE INDEX ix_struo_users_accesstoken ON struo_users (accesstoken);
```

PostgreSQL 與 MySQL／MariaDB 的 NULL 不會互撞，這一步可做可不做。PostgreSQL 的
`DROP INDEX` 不接 `ON struo_users`，其餘相同。

### SQL Server：`varchar` 換成 `nvarchar`

v0.8.x 建的字串欄位是 `varchar`，超出定序字碼頁的字元（例如 Latin1 定序下的中文與 emoji）存進去會變成 `?`。要存這類
文字，就在存之前轉換：已經變成 `?` 的資料，轉換後也救不回來。

有索引的欄位不能直接 `ALTER COLUMN`，要先刪索引、改欄位、再照原樣建回索引。下面這支查詢不改
任何東西，只印出這三組語句，涵蓋核心、範例與 fork 的表，保留長度、可否為 null，以及索引的名稱、
唯一性與欄位順序：

```sql
WITH cols AS (
    SELECT c.object_id, c.column_id, c.name, c.max_length, c.is_nullable
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE ty.name = 'varchar' AND t.is_ms_shipped = 0
),
idx AS (
    SELECT DISTINCT i.object_id, i.index_id, i.name, i.is_unique
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN cols ON cols.object_id = ic.object_id AND cols.column_id = ic.column_id
    WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0
)
SELECT stmt FROM (
    SELECT 1 AS step, OBJECT_NAME(object_id) AS tbl, name AS item,
           'DROP INDEX ' + QUOTENAME(name) + ' ON ' + QUOTENAME(OBJECT_NAME(object_id)) + ';' AS stmt
    FROM idx
    UNION ALL
    SELECT 2, OBJECT_NAME(object_id), name,
           'ALTER TABLE ' + QUOTENAME(OBJECT_NAME(object_id)) + ' ALTER COLUMN ' + QUOTENAME(name)
           + ' nvarchar(' + CASE WHEN max_length = -1 OR max_length > 4000 THEN 'max'
                                 ELSE CAST(max_length AS varchar(4)) END + ')'
           + CASE WHEN is_nullable = 1 THEN ' NULL;' ELSE ' NOT NULL;' END
    FROM cols
    UNION ALL
    SELECT 3, OBJECT_NAME(object_id), name,
           'CREATE ' + CASE WHEN is_unique = 1 THEN 'UNIQUE ' ELSE '' END + 'INDEX ' + QUOTENAME(name)
           + ' ON ' + QUOTENAME(OBJECT_NAME(object_id)) + ' ('
           + (SELECT STRING_AGG(QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END, ', ')
                     WITHIN GROUP (ORDER BY ic.key_ordinal)
              FROM sys.index_columns ic
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = idx.object_id AND ic.index_id = idx.index_id AND ic.is_included_column = 0)
           + ');'
    FROM idx
) s
ORDER BY step, tbl, item;
```

先做完上一步的 access token 索引。檢查印出來的語句，包在 `SET XACT_ABORT ON; BEGIN TRANSACTION;`
與 `COMMIT;` 之間一次執行。做完之後 `migrate:check` 應該是 `0 error(s), 0 warning(s)`。

### MySQL／MariaDB：`datetime` 換成 `datetime(6)`

v0.8.x 建的時間欄位多半是只到秒的 `datetime`，新資料庫則是 `datetime(6)`。同一秒內寫入的資料，
預設排序（`createdAt` 由新到舊）就分不出先後。把核心、範例與 fork 每個 `datetime` 欄位改成
`datetime(6)`；已經是 `datetime(6)` 的欄位不用動，版本表的 `appliedon` 也不用動。這支查詢只印出
`ALTER TABLE` 語句，每張表一句，保留可否為 null：

```sql
SET SESSION group_concat_max_len = 1000000;
SELECT CONCAT('ALTER TABLE `', TABLE_NAME, '` ',
              GROUP_CONCAT(CONCAT('MODIFY `', COLUMN_NAME, '` datetime(6) ',
                                  IF(IS_NULLABLE = 'YES', 'NULL', 'NOT NULL'))
                           ORDER BY ORDINAL_POSITION SEPARATOR ', '),
              ';') AS stmt
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND COLUMN_TYPE = 'datetime'
  AND TABLE_NAME <> 'struo_schema_versions'
GROUP BY TABLE_NAME
ORDER BY TABLE_NAME;
```

`MODIFY` 會拿掉語句裡沒有重新寫出的預設值。核心與範例的這些欄位都沒有預設值；你自己的欄位有的
話，把它加回印出來的語句裡。這件事不要寫成 fork 的 migration：之後才做 baseline 的部署，會把它
記成已套用卻從沒執行。

### MySQL／MariaDB：字元集換成 utf8mb4

utf8mb3 或 latin1 的資料庫在嚴格模式下會拒絕 emoji 這類 4 位元組字元，API 回
`INTERNAL_SERVER_ERROR`。先改資料庫預設值，之後 migration 建的表才會沿用；再印出每張還不是
utf8mb4 的表的轉換語句，檢查後執行。這組語句在 MySQL 8.4 與 MariaDB 11.8 上實際跑過：

```sql
ALTER DATABASE `<your database>` CHARACTER SET utf8mb4;

SELECT CONCAT('ALTER TABLE `', TABLE_NAME, '` CONVERT TO CHARACTER SET utf8mb4;') AS stmt
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_TYPE = 'BASE TABLE'
  AND TABLE_COLLATION NOT LIKE 'utf8mb4%'
ORDER BY TABLE_NAME;
```

## 留下來的東西

- 用過 `MigrationsPath` 的部署會留下 `struo_schema_migrations`。沒有東西讀它，`migrate:check`
  也不會提到它。確認 entity 已經反映那些腳本的效果之後可以刪掉，留著當紀錄也行。
- fork 裡的 `db/migrations/` 目錄可以刪掉。
- v0.8.x 建的唯一群組索引與翻譯 sidecar 索引叫 `Index_{表}_{欄位}_Unique`（PostgreSQL 上是小寫），
  新資料庫則是 `ux_…`。`migrate:check` 依欄位比對，不在意名稱；之後的 migration 要用名稱刪除或
  改名某個索引時，先查出它的實際名稱。

## 漏了第 4 步時

漏掉第 4 步，baseline 時就不會記到 fork 的 migration。這件事一開始看不出來：`migrate:check`
乾淨，API 也照常啟動，因為它比對的是 entity 與資料表，不是 migration。等你補上那支 migration，
`migrate:status` 把它列成 `Pending`，API 拒絕啟動，`migrate` 則在已經存在的表上失敗：

```
$ dotnet run --project src/Struo.Api -- migrate
…
migrate failed: Migration run failed; lowest unrecorded version: 202610120942. 42P07: relation "notes" already exists
```

只要 baseline 之後還沒有套用過任何 migration，就清掉版本表、加好 migration、重新跑
`migrate:baseline`，再用 `migrate:status`、`migrate` 與 `migrate:check` 確認：

```sql
DELETE FROM struo_schema_versions;
```

## 接下來

接上之後，日常的結構管理與核心升級回到[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)；
每個版本改了什麼，見[版本紀錄](changelog.md)。
