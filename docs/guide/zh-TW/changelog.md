# 版本紀錄

## 0.8.0

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
:::

### 新增

- `Database:TablePrefix` 設定鍵（環境變數 `Database__TablePrefix`），啟動時驗證格式。
- `Localization` 設定段：`struo_languages` 表第一次建立時的種子來源（`Languages`、`DefaultLanguage`），啟動時驗證。
- `AdminUi` 設定段：後台提供的介面語言與預設（`Locales`、`DefaultLocale`），經 `GET /api/config` 的 `uiLocales`、`uiDefaultLocale` 送給前端；只啟用一個語言時切換器不出現。
- `language` 集合的寫入規則：`code` 不分大小寫唯一、至少一列啟用、啟用列中恰好一列為預設；違反回 400。

## 0.7.0 — 2026-09-23

專案轉為開源時的現狀。版本紀錄從這一版開始。
