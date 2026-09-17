# 1. StruoCMS 是什麼

每次要自己做一套內容管理系統，都得重新處理同一批基礎建設：使用者與權限、檔案儲存、多語內容、
REST 與 GraphQL API。StruoCMS 是一個可以直接 fork 的 headless CMS template，建構在 .NET 10 與
SqlSugar 之上，實際驗證過的資料庫是 PostgreSQL，管理後台是一個 Vue 3 單頁應用程式。

fork 之後，你在自己的專案裡宣告內容集合，StruoCMS 把它們變成資料表、API 與後台介面，基礎建設的
部分不用你再重做一次。

## 它包含什麼

- **集合引擎**：一個 C# entity 宣告，同時驅動資料表結構、REST 端點、GraphQL schema、查詢 DSL，
  以及後台的表單畫面——改一個地方，五個介面一起更新。
- **REST API**：每個回應都包在同一個信封格式裡：成功時是 `{success, data, meta?}`，失敗時是
  `{success:false, error:{code, message, details?}}`，呼叫端不用猜回應長什麼樣子。
- **GraphQL API**：schema 由和 REST 同一份集合 metadata 產生，兩邊看到的是同一套資料形狀。
- **查詢 DSL**：每一個篩選條件、排序與關聯路徑，都會先對照 metadata 驗證過，才組出 SQL，寫錯
  欄位名稱在組 SQL 之前就會被擋下來。
- **身分驗證**：支援 cookie 與 bearer token 兩種方式，密碼一律以 Argon2id 雜湊；需要對接既有
  身分系統時，OpenID Connect 單一登入也是核心功能，只是預設關閉。
- **角色式權限**：讀取、寫入、刪除權限以集合為單位授予。
- **檔案與媒體**：可以選擇本機硬碟或 S3 相容的儲存後端，下載檔案時能即時做圖片轉換，不用另外
  跑一套轉檔服務。
- **版本與軟刪除**：兩者都是核心功能，而且都是每個集合各自開關——需要的集合才開，不是全站強制
  啟用。
- **多語內容**：欄位可以各語言分開存翻譯，同一筆資料的不同語言版本互不干擾。
- **網站設定與品牌**：整站共用一筆設定，super-admin 直接在後台改，不用動設定檔或重新部署。
- **管理後台**：一個 Vue 3 單頁應用程式，把以上所有功能收在同一個操作介面裡。

## 你要自己完成的部分

內容模型從你這邊開始：剛裝好的 StruoCMS，集合數量是零，Article、Tag、Category 這些集合都由你
自己宣告。fork 之後，把你的集合類別放進 `Struo:ContentAssemblies` 指定的組件，StruoCMS 會連同
API 專案（`Struo.Api`）的組件一起掃描，自動變成資料表、REST 端點與 GraphQL schema。

你也需要處理三件事：

- 幫自己的集合寫資料庫 migration。
- 把商業邏輯放進你自己的服務或集合裡。
- 把整個專案部署到你自己的環境。

核心只負責框架本身這一層。

`samples/Struo.Sample.Blog` 是一個可選的示範專案，用 Article、Category 這類集合示範怎麼用核心
提供的工具定義自己的內容模型；API 專案完全不會參照到它，學完之後就可以刪掉。

刪除它不只是刪資料夾：它同時寫進了 solution 檔和測試專案，直接刪除資料夾會讓 solution 層級的
build 失敗；完整的移除步驟見[第 23 章：範例專案導覽](23-sample-walkthrough.md)。

## 全新安裝看起來是什麼樣

剛裝好、還沒加入任何集合的 StruoCMS，長這樣：

- 資料庫裡剛好十一張框架資料表；如果你設定了 `Database:MigrationsPath`，啟動時會多建立第十二
  張 `schema_migrations`，用來追蹤跑過的 migration。
- 後台側欄沒有內容群組，只有 System：對 super-admin 來說，裡面是 Language、Role、User。System
  只列出這三項，是因為 File、MediaFolder、Permission、UserRole 這四個框架集合宣告了
  `Hidden = true`——這是顯示層的旗標，不是權限。
- Dashboard 固定顯示；Media Library 要有 `file.read` 權限（或 super-admin），Settings 只有
  super-admin 看得到。

這是正確的狀態，不是安裝有問題——核心刻意不帶任何內容，等你自己的集合加進來把它填滿。

## 支援的資料庫

`Database:DbType` 可以設成五種值：PostgreSQL、MySql、SqlServer、Sqlite、Oracle。

| 資料庫 | 狀態 |
|---|---|
| PostgreSQL | 唯一驗證過的執行目標 |
| Sqlite | 只用在測試套件 |
| MySql、SqlServer、Oracle | 有型別對應，未經驗證 |

查詢裡有些排序與常值轉換的寫法，是針對 PostgreSQL 與 SQLite 的行為專門處理的。選擇 PostgreSQL
之外的資料庫，目前沒有支援保證，自己上線前要先做好驗證。

## 接下來

想先弄懂內部怎麼分工、核心與範例的界線畫在哪裡，讀[第 2 章：系統架構](02-architecture.md)；想
跳過理論、直接把 API 與後台跑起來看看，讀[第 3 章：快速開始](03-getting-started.md)。
