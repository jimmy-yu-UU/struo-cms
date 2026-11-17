# 24. 接上 v0.8.x 的資料庫

這一章帶一份 v0.8.x 在開機時用 CodeFirst 建出來的資料庫接上 migration，之後就能照
[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)用 `migrate` 管理它。

這種資料庫裡有框架資料表，卻沒有版本表。`migrate:baseline` 就是為它準備的：把對應 v0.8 結構的
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
   junction，只要表已經存在，就跑一次 `make:migration`。下面的紀錄來自一次演練，演練的內容專案
   放在 `samples/Acme.Content`；`--output` 要指向你自己內容專案的 migration 資料夾，`--namespace`
   用它的命名空間：

   ```
   $ dotnet run --project src/Struo.Api -- make:migration CreateNotes --entity Note --output ../../samples/Acme.Content/Migrations --namespace Acme.Content.Migrations
   Created ../../samples/Acme.Content/Migrations\202610120942_CreateNotes.cs
   ```

   檢查產生的檔案，重新建置；用映像部署的話，重建映像。還不存在的表不要在這一步產生：baseline
   會把它記成已套用，那張表就永遠不會被建出來，等第 5 步之後再加。`--output` 的相對路徑從
   `src/Struo.Api` 算起，目錄要先建好，見[第 21 章〈撰寫 migration〉](21-schema-and-upgrades.md#撰寫-migration)。

   fork 若有多個在 migration 出現之前建好的資料庫，要先把它們全部 baseline，再加入第 4 步以外的
   fork migration。baseline 會把當下存在的每一個 fork migration 記成已套用、但不執行，對跑它的那個
   資料庫都一樣，所以在某個資料庫 baseline 之後才加入的 migration，會在之後才 baseline 的資料庫上
   被跳過。baseline 這樣的資料庫之前，先確認它的結構已經包含那些 migration 會建立的內容。
5. **跑一次 `migrate:baseline`。** 在這之前，`migrate` 與 API 啟動都會拒絕，訊息要你跑
   `migrate:baseline`。baseline 拿 migration 鎖，在一個交易裡寫入核心（到 `SeedCoreData` 為止）、範例
   （有開啟的話）與 fork 的版本，並列出 FluentMigrator 執行的 SQL：

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
6. **只有 MySQL 與 MariaDB：把資料庫與框架資料表轉成 utf8mb4。** `migrate` 會拒絕預設字元集
   （資料庫的，或框架資料表的）不是 utf8mb4 的資料庫，所以這一步要先做；語句在下面
   〈MySQL／MariaDB：字元集換成 utf8mb4〉一節。PostgreSQL 與 SQL Server 直接進第 7 步。
7. **跑 `migrate`。** 比 `SeedCoreData` 新的核心 migration，例如 `AddLanguageCodeUniqueIndex`、
   `CreateRoleCapabilities`、`DropRoleCapabilities` 與 `ConvertStringColumnsToUtf8Mb4`，以及
   baseline 之後新增的 migration 都在這裡套用；沒有待套用的就印 `Nothing to migrate.`。
   若有兩個語言代碼對資料庫來說相等，`AddLanguageCodeUniqueIndex` 會讓執行停下；在 MySQL 與
   MariaDB 上，唯一欄位裡有兩個值在 utf8mb4 的定序下相等時，
   `ConvertStringColumnsToUtf8Mb4` 也會讓執行停下。怎麼找出並移除它們，見
   [版本紀錄](changelog.md)；哪些欄位它不會轉換，見下面字元集那一節。若跳過了第 6 步，`migrate`
   會在套用任何東西之前停下並印出該跑什麼；訊息的樣子見下面字元集那一節。
8. **跑 `migrate:check`。** PostgreSQL 應該是 `0 error(s), 0 warning(s)`；MySQL 與 MariaDB 在每個
   字串欄位都是 utf8mb4 時也是，否則每個不是的欄位發一筆警告。SQL Server 對每個 `varchar` 字串
   欄位發一筆警告：

   ```
   $ dotnet run --project src/Struo.Api -- migrate:check
   WARNING article_tags.note: database has non-Unicode varchar (length 255); characters outside the collation's code page are lost, use nvarchar
   …
   0 error(s), 30 warning(s)
   ```

   警告的數量依你的表而定。任何錯誤都表示某個 entity 跟它的表不一致，通常是 entity 宣告了、表裡
   卻沒有的欄位，或某支 `.sql` 腳本改過、entity 沒有跟上的欄位。先修好再在 Development 啟動，
   Development 的啟動檢查遇到錯誤會拒絕啟動。
9. **做下一節屬於你那個後端的後續處理。** `migrate:check` 看不到 SQL Server 的 access token 索引
   與 MySQL／MariaDB 的時間精度；在 MySQL 與 MariaDB 上，它會對不是 utf8mb4 的字串欄位發警告，第 6
   步轉換它們。SQL Server 上先做完再啟動 API：在那之前存進去的中文與 emoji 會變成 `?`，救不回來。
10. **啟動 API。**

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

不是 utf8mb4 的字串欄位會拒絕 emoji 這類 4 位元組字元，API 回 `INTERNAL_SERVER_ERROR`；
`migrate:check` 對每個這樣的欄位發一筆警告。沒有指定字元集的欄位沿用所在資料表的預設值，所以
只有在資料庫與框架資料表的預設值都是 utf8mb4 時，`migrate` 才會轉換核心的字串欄位。

套用任何東西之前，`migrate` 與 `Database:MigrateOnStartup` 的啟動都會先讀資料庫的預設字元集與
定序，以及每張已經存在的框架資料表的預設定序。資料庫或其中一張表的預設值不是 utf8mb4，就不
套用、不改動任何東西，`migrate` 以代碼 `1` 結束，API 不啟動，訊息會寫出找到什麼，以及該執行的
語句。預設值是 utf8mb3 或 latin1 的資料庫會被拒絕，例如伺服器預設值是 latin1、建立時又沒有指定
`CHARACTER SET utf8mb4` 的資料庫。下面是在 MySQL 8.4 上，做完第 5 步後對一個 utf8mb3 資料庫跑
`migrate`：

```
migrate failed: Nothing was applied or changed: migrations on MySQL and MariaDB need utf8mb4 as the default
character set of the database and of the framework tables, and the items below do not have it.
Database `u3` has the default character set utf8mb3.
Table `struo_file_translations` has the default collation utf8mb3_general_ci.
Table `struo_files` has the default collation utf8mb3_general_ci.
Table `struo_languages` has the default collation utf8mb3_general_ci.
Table `struo_media_folders` has the default collation utf8mb3_general_ci.
Table `struo_permissions` has the default collation utf8mb3_general_ci.
Table `struo_revisions` has the default collation utf8mb3_general_ci.
Table `struo_roles` has the default collation utf8mb3_general_ci.
Table `struo_schema_versions` has the default collation utf8mb3_general_ci.
Table `struo_site_settings` has the default collation utf8mb3_general_ci.
Table `struo_user_roles` has the default collation utf8mb3_general_ci.
Table `struo_user_sessions` has the default collation utf8mb3_general_ci.
Table `struo_users` has the default collation utf8mb3_general_ci.
Run these statements, then run 'migrate' again:
ALTER DATABASE `u3` CHARACTER SET utf8mb4;
ALTER TABLE `struo_file_translations` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_files` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_languages` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_media_folders` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_permissions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_revisions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_roles` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_schema_versions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_site_settings` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_user_roles` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_user_sessions` CONVERT TO CHARACTER SET utf8mb4;
ALTER TABLE `struo_users` CONVERT TO CHARACTER SET utf8mb4;
Tables of your own that do not default to utf8mb4 are converted the same way;
'migrate:check' lists their columns.
```

要執行哪些語句，看資料庫自己的預設值：

| 資料庫的預設字元集 | 要執行的語句 |
|---|---|
| 不是 utf8mb4 | `ALTER DATABASE`，再對每張表一句 `ALTER TABLE` |
| 是 utf8mb4（任何定序） | 每張表一句 `ALTER TABLE`，結尾帶資料庫的定序 |

執行訊息印出的語句，或用下面的查詢印出 `ALTER TABLE` 語句。資料庫的預設值不是 utf8mb4（訊息會
說）時，先執行 `ALTER DATABASE`，之後建的表才會沿用；已經是 utf8mb4 就跳過這一句。查詢還會列出
你的 fork 自己的表（訊息不會列），以及自己的預設值是 utf8mb4、卻含有非 utf8mb4 欄位的表。每句
`ALTER TABLE` 都以資料庫的預設定序結尾，所以轉換後的表與之後建的表定序一致，不同定序的欄位互相
比對時不會出現 `Illegal mix of collations`。這組步驟在 MySQL 8.4 與 MariaDB 11.8 上，對 utf8mb3
的資料庫、latin1 的資料庫，以及建成 `utf8mb4 COLLATE utf8mb4_unicode_ci`、表卻是 utf8mb3
的資料庫都可行：

```sql
ALTER DATABASE `<your database>` CHARACTER SET utf8mb4;
```

```sql
SELECT CONCAT('ALTER TABLE `', t.TABLE_NAME, '` CONVERT TO CHARACTER SET utf8mb4 COLLATE ',
              s.DEFAULT_COLLATION_NAME, ';') AS stmt
FROM information_schema.TABLES t
JOIN information_schema.SCHEMATA s ON s.SCHEMA_NAME = t.TABLE_SCHEMA
WHERE t.TABLE_SCHEMA = DATABASE()
  AND t.TABLE_TYPE = 'BASE TABLE'
  AND (t.TABLE_COLLATION NOT LIKE 'utf8mb4\_%'
       OR EXISTS (SELECT 1 FROM information_schema.COLUMNS c
                  WHERE c.TABLE_SCHEMA = t.TABLE_SCHEMA
                    AND c.TABLE_NAME = t.TABLE_NAME
                    AND c.CHARACTER_SET_NAME IS NOT NULL
                    AND c.CHARACTER_SET_NAME <> 'utf8mb4'))
ORDER BY t.TABLE_NAME;
```

`CONVERT TO` 轉換表裡每個文字欄位，保留索引與可否為 null，並設定表的預設字元集與定序。utf8mb3
欄位與 latin1 欄位裡的文字，轉換後讀出來一樣。連刻意宣告成其他字元集（例如 `ascii`）的欄位也會
轉換，這類表的轉換語句要檢查過再執行。

`CONVERT TO` 也會改變欄位型別：`TEXT` 欄位變成 `MEDIUMTEXT`（在 MySQL 8.4 與 MariaDB 11.8 上，
latin1 與 utf8mb3 的欄位都是如此）。核心沒有 `TEXT` 欄位；fork 用 `AsString` 宣告 8193 到 65535
的欄位就是 `TEXT`。
轉換之後，`migrate:check` 對這個欄位沒有任何發現。

之後上面的查詢回傳零列，`migrate` 能跑，`migrate:check` 也不會有字元集警告。你的 fork 自己的表
（`migrate` 不會轉換）用同一句 `ALTER TABLE` 轉換，欄位由 `migrate:check` 列出。

#### 轉換後的比對結果

轉換後欄位的定序依路徑而定：

| 路徑 | 欄位轉換後的定序 |
|---|---|
| `CONVERT TO` 沒有 `COLLATE` | 字元集的預設定序，與資料庫的定序無關 |
| `CONVERT TO` 帶 `COLLATE <c>` | `<c>` |
| `migrate` 重新宣告欄位 | 該表的預設定序 |

字元集的預設定序在 MySQL 8.4 是 `utf8mb4_0900_ai_ci`，在 MariaDB 11.8 是
`utf8mb4_uca1400_ai_ci`。下面的表是三種轉換在這兩個預設定序下的量測結果：

- A：MySQL 8.4，`utf8mb3_general_ci` 轉成 `utf8mb4_0900_ai_ci`。
- B：MariaDB 11.8，`utf8mb3_general_ci` 轉成 `utf8mb4_uca1400_ai_ci`。
- C：MariaDB 11.8，`utf8mb3_uca1400_ai_ci` 轉成 `utf8mb4_uca1400_ai_ci`。

每格是「轉換前 → 轉換後」，「是」表示兩個值相等；最後一列問的是 `a` 是否排在 `_` 之前：

| 成對的值 | A | B | C |
|---|---|---|---|
| `a` 與 `a` 加結尾空白 | 是 → 否 | 是 → 是 | 是 → 是 |
| sharp s 與 `ss` | 否 → 是 | 否 → 是 | 是 → 是 |
| sharp s 與 `s` | 是 → 否 | 是 → 否 | 否 → 否 |
| 全形 `Ａ` 與 `A` | 否 → 是 | 否 → 是 | 是 → 是 |
| `a` 與 `a` 加 U+200B | 否 → 是 | 否 → 是 | 是 → 是 |
| `a` 排在 `_` 之前 | 是 → 否 | 是 → 否 | 否 → 否 |

資料庫用別的定序時，語句的 `COLLATE` 就是那個定序，上表不適用，找成對的值時要用它。

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
