# 3. 設定參考

本章的每一項設定都透過標準的 ASP.NET Core 設定分層順序解析:先是
`src/Struo.Api/appsettings.json` (已提交的預設值)，接著依序是 `appsettings.{Environment}.json`、
環境變數，最後是命令列參數。

**其中有三節刻意不存在於出貨的 `appsettings.json` 中**——`Query`、`Struo:Cors` 與 `GraphQl`。它們的
預設值放在 C# 裡(或者，就 `GraphQl:ExposeSchema` 而言，放在「未設定即代表僅限 Development」這條規則
裡)，而不是放在檔案中，所以在你自己新增之前，檔案裡不會有可編輯的區塊;下方每一節都會明確說明這點。

**環境變數覆寫慣例:** 任何鍵都可以用環境變數提供，把其路徑中每一個 `:` 換成雙底線 (`__`)，例如
`Database:ConnectionString` 會變成 `Database__ConnectionString`。這個規則對本章的每一個鍵都成立，
只有**一個例外**:`Testing:PostgresConnection` (記載於本章末尾) 是由測試套件透過它自己的
`ConfigurationBuilder` 讀取的，而該建構器並未註冊任何環境變數提供者——所以
`Testing__PostgresConnection` 對它沒有作用;只有 `STRUO_TEST_PG_CONNECTION` 有效。

**重新啟動行為:** 本章中的每一個選項都是透過 ASP.NET Core 的 `IOptions<T>` 模式，在應用程式啟動時或
第一次被解析時綁定一次。程序執行期間，沒有任何一項會從即時重新載入的設定檔重新讀取——變更任何鍵都需要
**重新啟動 API 程序**才會生效，即使底層的 `appsettings.json` 檔案監看器 (file watcher) 本身仍在
運作中。若某個鍵除了「只要重新啟動」之外還有額外的眉角——例如某個值只在資料表第一次被建立時才會被
參照——下方會明確指出。

**本章只有兩個陣列型設定帶有非空的 C# 內建預設值**——`Oidc:Scopes` 和
`Struo:Files:ImageTransform:AllowedFormats`。對這兩者而言，當設定完全沒有為該鍵提供任何元素時，現在
會正確地退回該內建預設值，而不是把設定的元素併入到它上面(先前那個有缺陷的行為)。下方的
`Struo:Files:AllowedContentTypes` 和 `Serilog:WriteTo` 也顯示非空的預設值，但那個預設值來自出貨的
`appsettings.json` 檔案，而不是來自 C# 屬性初始化式——本段的區別不適用於它們。

**在較高優先序的來源中設定陣列型鍵，並不會縮短較低優先序來源已經填入的清單。**
`IConfiguration` 是跨分層來源、逐索引 (index-by-index) 合併陣列元素的，所以較窄的覆寫只會取代它明確
設定的那些索引——覆寫省略的任何尾端索引，仍然來自設定它的那個較低優先序來源。這是 `IConfiguration`
本身跨提供者解析索引鍵的特性(已透過堆疊兩個記憶體內設定來源驗證:一個基底來源設定全部三個索引，另一個
較高優先序來源只設定索引 0——結果解析出全部三筆，而不是一筆)，並非本節任何單一設定獨有:
`Oidc:Scopes`、`Struo:Files:ImageTransform:AllowedFormats`、`Struo:Files:AllowedContentTypes` 和
`Serilog:WriteTo` 都適用同樣的行為。由於出貨的 `appsettings.json` 已經明確設定這四者的每一個元素，
用環境變數覆寫或 `appsettings.{Environment}.json` 只設定較短的前綴(例如
`Oidc__Scopes__0=openid`)**不會**縮短有效清單——出貨檔案裡剩下的項目仍然會被綁定。若要真正縮短這
四份清單中的任何一份，請直接編輯或移除出貨 `appsettings.json` 裡的項目，而不是在上面疊加一個較短的
陣列。

## `Database`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Database:DbType` | enum: `PostgreSQL`\|`MySql`\|`SqlServer`\|`Sqlite`\|`Oracle` | `PostgreSQL` | 選擇 SqlSugar 後端。只有 `PostgreSQL` 是經驗證的執行期目標;`Sqlite` 僅用於測試;`MySql`/`SqlServer`/`Oracle` 雖有型別對應但屬實驗性質。 |
| `Database:ConnectionString` | string，必填 | 無——出貨時為 `REPLACE_ME` 預留值 | 所選引擎的 ADO.NET 連線字串。缺少或空值會導致啟動失敗 (`[Required]` + `ValidateOnStart`)，而不是在第一次查詢時才浮現令人困惑的失敗。 |
| `Database:MigrationsPath` | string?，選填 | 空白/未設定 (停用) | 已審查的 `*.sql` migration 腳本所在目錄，由 `MigrationRunner` 在啟動時套用。會在**每一個**已設定的後端上執行，不只 PostgreSQL——一支針對錯誤後端撰寫的腳本，只會在套用時單純失敗;沒有任何 per-backend 的守衛。留空會在任何後端上完全停用這個 runner。這是三層 schema 管理機制之一，另外兩層是建表(全環境無條件執行，不需要任何設定)與下方的 `Database:AutoSyncSchema`——完整全貌見第 15 章。 |
| `Database:AutoSyncSchema` | bool | `false` | 讓 CodeFirst 對已存在的資料表執行一次完整的結構同步——新增、修改，以及**刪除**欄位——直接由 entity 類別驅動。只有在 Development 才會生效;在其他任何環境設為 `true`，都會被忽略並記錄一則警告，而不會被採納。預設關閉，因為對一張已經存有資料的資料表而言，這可能會靜默地摧毀資料(例如一次欄位改名，會被讀成「刪一欄、加一欄」)——第 15 章的九項危險情境表完整涵蓋這一點。「刪除」這一半本身依後端／設定而異，並非放諸四海皆準：在 PostgreSQL 上實測會發生;在 SQLite 上實測不會，因為 SqlSugar 把 SQLite 的 `DROP COLUMN` 把關在一個這個儲存庫從未設定過的 `ConnectionConfig.MoreSettings` 旗標之後——細節見第 15 章第 7 項。 |

以上四項都需要重新啟動才會生效。

## `Struo:ContentAssemblies`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Struo:ContentAssemblies` | string[] | `[]` (空) | 啟動時掃描的組件 (assembly) 名稱，用來尋找 `[CmsCollection]` 內容型別。 |

這是整個設定介面中，讀取時機真正特殊的唯一一項設定:**它是在 `Program.cs` 中、於 `builder.Build()`
被呼叫*之前*，直接從 `builder.Configuration` 讀取的**，而不是透過其他地方一律採用的
`IOptions<T>` 管線。實務上，這代表:

- 它仍能從所有*正常*的設定來源正確運作——`appsettings.json`、`appsettings.{Environment}.json`、
  user secrets、環境變數、命令列參數——因為 `WebApplication.CreateBuilder` 會在自己的建構函式內
  安裝好這些來源，早於這次讀取發生之前。
- 一個 fork 在 `CreateBuilder` 回傳*之後*才加入的**自訂**設定提供者 (例如 secret manager 或
  key-vault 提供者)，必須在這次讀取之前註冊到 `builder.Configuration` 上，否則它裡面的
  `Struo:ContentAssemblies` 項目永遠不會被中介資料掃描看見。
- 每一個具名的組件都必須真的能透過 `Assembly.Load` 被解析，這代表它必須被登記在
  `Struo.Api.deps.json` 中——實務上，就是 host 專案參照了它 (在 `Struo.Api.csproj` 中的一個
  `ProjectReference`)。一個只是放在 `Struo.Api.dll` 旁邊、卻未被登記的 DLL 並不足夠。無法解析的
  項目會讓啟動失敗，而不是被靜默略過。

需要重新啟動 (依其設計，這本來就是一次啟動期掃描)。第 16 章會走過 Blog 範例具體的兩步驟選用啟用
方式。

## `Struo:Files`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Struo:Files:Backend` | string: `"local"`\|`"s3"` | `"local"` | 選擇儲存後端。啟動時會驗證——無法辨識的值會立即導致失敗。 |
| `Struo:Files:MaxUploadBytes` | long | `26214400` (25 MB) | 允許上傳的最大檔案大小。 |
| `Struo:Files:AllowedContentTypes` | string[] | `appsettings.json` 中列出的 MIME 類型清單 (images、PDF、plain text、MP4、MP3、Office 格式、ZIP) | 允許上傳的內容類型允許清單 (allow-list)。空陣列代表允許所有內容類型。 |
| `Struo:Files:PresignedRedirect` | bool | `false` | 為 `true` 時，`GET /api/files/{id}/content` 會回應一個 302 redirect 導向儲存端 presigned URL，而不是由 API 自己串流位元組資料。 |

以上全部都在啟動時被驗證 (`ValidateOnStart`)，且需要重新啟動。

### `Struo:Files:Local`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Struo:Files:Local:RootPath` | string | `"App_Data/uploads"` | 當 `Backend` 為 `local` 時，上傳檔案的檔案系統根目錄。選用該後端時為必填 (會被驗證)。 |

### `Struo:Files:S3`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Struo:Files:S3:Endpoint` | string? | `REPLACE_ME` 預留值 | S3 相容的 endpoint URL。當 `Backend` 為 `s3` 時為必填 (會被驗證)。 |
| `Struo:Files:S3:Bucket` | string? | `REPLACE_ME` 預留值 | 目標 bucket 名稱。當 `Backend` 為 `s3` 時為必填。 |
| `Struo:Files:S3:AccessKey` | string? | `REPLACE_ME` 預留值 | Access key。當 `Backend` 為 `s3` 時為必填。 |
| `Struo:Files:S3:SecretKey` | string? | `REPLACE_ME` 預留值 | Secret key。當 `Backend` 為 `s3` 時為必填。 |
| `Struo:Files:S3:Region` | string | `"us-east-1"` | 傳遞給 AWS S3 SDK client 的 region。 |
| `Struo:Files:S3:ForcePathStyle` | bool | `true` | Path-style 定址方式——MinIO 及大多數自架的 S3 相容伺服器都需要它。 |
| `Struo:Files:S3:PresignTtlSeconds` | int | `300` | 產生的 presigned URL 存活時間，單位為秒。 |

若要使用內建的 MinIO 容器做為此後端，需要執行 `docker compose --profile s3 up -d` (它也會執行那個
一次性的 bucket 建立步驟)——見第 2 章。

### `Struo:Files:ImageTransform`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Struo:Files:ImageTransform:Enabled` | bool | `true` | 開啟或關閉即時圖片轉換端點。 |
| `Struo:Files:ImageTransform:MaxWidth` | int | `4096` | 請求轉換寬度的上限。 |
| `Struo:Files:ImageTransform:MaxHeight` | int | `4096` | 請求轉換高度的上限。 |
| `Struo:Files:ImageTransform:AllowedFormats` | string[] | `["webp", "jpeg", "png", "avif"]` | 轉換端點會輸出的格式。 |
| `Struo:Files:ImageTransform:DefaultQuality` | int | `82` | 當請求未指定時使用的預設編碼品質。 |
| `Struo:Files:ImageTransform:CachePath` | string | `"App_Data/image-cache"` | 快取轉換後圖片變體的根目錄。相對路徑會相對於應用程式的 content root 解析，**不是**程序的目前工作目錄——如果你曾經在不同於專案資料夾的工作目錄下啟動程序 (例如一個 systemd unit)，這一點就很重要。 |

## `Struo:Cors`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Struo:Cors:AllowedOrigins` | string[] | `[]` (空白——停用跨來源) | 允許從不同來源呼叫此 API 的來源清單。不存在於出貨的 `appsettings.json`;只要它是空的，`app.UseCors(...)` 就完全不會被呼叫，因此任何回應都不會帶上 CORS 標頭。 |

設定任何一個來源會做**兩件**事，不是一件。它會用那些來源加上
`AllowCredentials`/`AllowAnyHeader`/`AllowAnyMethod` 填入 `StruoSpa` 政策
(`src/Struo.Api/Auth/CorsWiring.cs`)，**而且**會把 session cookie 從
`SameSite=Lax`/`SecurePolicy=SameAsRequest` 重新設定為 `SameSite=None` + `SecurePolicy=Always`
(`src/Struo.Api/Auth/AuthWiring.cs`)。瀏覽器只在 HTTPS 之下才會遵守 `SameSite=None`，所以一旦這個鍵
非空，SPA 與 API 兩邊都必須以 HTTPS 提供服務，否則認證會無聲地失效——cookie 設得出去，但永遠送不回來。

同來源 (same-origin) 的部署請讓它保持空白;第 2 章的 Vite dev proxy 與同源託管的正式版建置產出的都是
這種形態。第 14 章完整說明真正的跨來源模式，包含前端對應的建置期變數 `VITE_API_BASE_URL`。

與本章大多數設定不同，這個鍵是直接從 `IConfiguration` 讀取的，並非透過 `IOptions<T>` 綁定，因此它自身
沒有任何 `ValidateOnStart` 驗證。需要重新啟動。

## `Query`

查詢 DSL (第 8 章) 的各項上限，由 `QueryValidator`，以及它所驗證的關聯子查詢下推機制
`FilterTranslator` (第 7 章) 共同執行。這一節**不在**出貨的 `appsettings.json` 裡——下方每一個
預設值都來自 `StruoQueryOptions`
(`src/Struo.Application/Configuration/StruoQueryOptions.cs`) 的 C# 屬性初始化式;只有在你要覆寫某一項
時才需要新增 `"Query"` 區塊。

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Query:MaxLimit` | int | `100` | 單頁 `limit` 的上限。超過的請求會被向下夾制到這個值，而不是被拒絕。 |
| `Query:DefaultLimit` | int | `25` | 當請求省略 `limit` 或送出非正值時所套用的 `limit`。 |
| `Query:MaxFilterConditions` | int | `50` | 每次查詢的葉節點條件總數上限，跨每一個 `_and`/`_or` 分支一併計算，也包含 `_some`/`_none` 量詞自身內層 filter 裡的每一個葉節點。超過會在任何查詢執行之前擲出 `"Too many filter conditions (max 50)."`。 |
| `Query:MaxRelationDepth` | int | `6` | 點狀路徑中關聯**跳數 (hop)** 的上限——filter、sort 鍵或巢狀 `deep` 皆適用。最後的葉欄位不計入，所以 `folder.name` 是 1 跳 (第 7 章)。`_junction` 這個偽片段不計入此上限。 |
| `Query:MaxFacets` | int | `10` | 單一請求中相異的 `facets=`/`"facets"` 路徑數量上限 (去重後計算)。超過會擲出 `"Too many facets (max 10)."`。 |
| `Query:MaxFacetValues` | int | `50` | 每個 facet (分面計數) 回傳的 `{ value, count }` bucket 數量上限——沒有 `otherCount` 餘量。自有欄位／外鍵／關聯名稱這三種形態是在資料庫層套用 (`ORDER BY count DESC, value ASC` 再 `Take`);一跳加葉欄位形態則是在葉值合併**之前**先對 target-id bucket 套用此上限，合併後的結果會在記憶體中另外重新套用一次上限 (第 8 章「排序與數值上限」)。 |
| `Query:MaxAggregates` | int | `10` | 單一請求中跨所有 op 的 `aggregate[<op>]=`/`"aggregate"` 欄位總數上限。超過會擲出 `"Too many aggregate fields (max 10)."`。這只是驗證用的上限，不是每條 SQL 陳述式的批次大小——批次大小是固定常數 `AggregateRow.SlotCount = 10` (第 8 章「這項功能要付出多少次查詢」);在預設值下兩者恰好相等，所以上限內的請求永遠只花一條彙總陳述式;但若某個 fork 把這個選項調高超過 10，每多 10 個欄位就會多花一條彙總陳述式，因為分批用的常數並不會跟著調整。 |

這七項都帶 `[Range(1, int.MaxValue)]` 驗證並以 `ValidateOnStart` 綁定，所以填 0 或負值的覆寫會讓啟動
失敗，而不是產生一個沒有意義的上限。第 8 章說明每個上限在實際情境中約束的是什麼。需要重新啟動。

一個 fork 的 `appsettings.*.json` 裡若殘留這項設定被移除之前留下的 `Query:MaxResolvedFilterIds` 鍵，
並不會讓啟動失敗:選項繫結器只會填入 `StruoQueryOptions` 上實際存在的屬性，所以 `Query` 底下一個
無法識別的鍵只會被靜默忽略，而不是被拒絕。

## `Auth:BootstrapAdmin`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Auth:BootstrapAdmin:Email` | string | `"admin@admin.com"` | Bootstrap 管理員電子郵件。 |
| `Auth:BootstrapAdmin:Password` | string | `"admin"` | Bootstrap 管理員密碼。 |

**這一組值只會被參照一次:`users` 資料表第一次被建立的時候。** 一旦該資料表存在，之後任何一次啟動都
不會重新讀取或重新套用這些值，即使針對一個被清空的 `users` 資料表也一樣——這個帳號 (以及它的密碼)
就是那第一次植入種子資料時的樣子，或是後來透過正常使用被改成的樣子。若要出貨不同的 bootstrap 身分，
請在針對全新資料庫的第一次啟動*之前*設定這些鍵 (在部署流程中，通常透過
`Auth__BootstrapAdmin__Email` / `Auth__BootstrapAdmin__Password` 環境變數)。

如果一個 `Production` 環境的啟動仍以 (或仍設定為要植入) 字面預設密碼 `admin` 做為種子，API 會記錄
一則**警告**，指名該變更哪一項設定——但它並不會因此拒絕啟動。

出貨預設值 (`"admin"`，5 個字元) 比下一節記載的 8 字元下限還短——這不是疏漏。種子邏輯直接把
`Auth:BootstrapAdmin:Password` 從設定值雜湊，完全不經過 `PasswordPolicy.Validate`，所以一次全新安裝
在操作者還沒改過任何東西之前，就已經有一個能登入的帳號。

## `Auth:Password`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Auth:Password:MinLength` | int | `8` | API 所接受的任何明文密碼的最小長度。 |
| `Auth:Password:MaxLength` | int | `128` | API 所接受的任何明文密碼的最大長度。 |

這項規則會在每一個接受明文密碼的請求路徑寫入點被強制執行——`POST /api/users` (建立使用者) 與
`PUT /api/users/{id}/password` (變更密碼)——兩者都透過同一個共用的驗證器，因此不會彼此漂移。
`MaxLength` 只是一個健全性上限，不是安全控制:Argon2id 的成本是由它自己的時間/記憶體/平行度參數
決定的，不受輸入長度影響，所以較長的密碼並不會放大雜湊運算量。可透過 `Auth__Password__MinLength` /
`Auth__Password__MaxLength` 覆寫;需要重新啟動。

這兩個鍵同樣以 `ValidateOnStart` 繫結:`MinLength` 必須 `>= 1` 且 `<= MaxLength`，所以一個設定錯誤的
覆寫值(例如把 `MinLength` 調到高於 `MaxLength`)會讓啟動時就拋出 `OptionsValidationException` 而失
敗，而不是讓這份設定被接受，之後每一次密碼請求才發現一律被拒。

## `Rbac:PublicReadCollections`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Rbac:PublicReadCollections` | string[] | `[]` (空) | 被授予匿名 (`"public"` 角色) 讀取權限的集合 (collection) 名稱。 |

如同 `Auth:BootstrapAdmin`，這份清單也只有第一次啟動時才會生效，但觸發時機不同:它只在 `roles`
資料表第一次被建立時才會被參照——就在那個時刻，種子邏輯 (seeder) 也會建立 `admin` (超級管理員) 與
`public` 角色，並把 bootstrap 管理員指派給 `admin`。之後每一次啟動，整個 RBAC 種子植入步驟 (包含
這個授權迴圈) 都會被跳過，因為 `roles` 已經存在。**編輯這個鍵並重新啟動應用程式，並不會回溯性地在
既有資料庫上授予公開讀取權限**——請直接對一個運作中的資料庫授予該權限 (透過 RBAC 管理 UI 或 API)，
或是在針對全新資料庫的第一次啟動之前就設定好這個值。

## `GraphQl`

| 鍵 | 型別 | 預設 | 意義 |
|---|---|---|---|
| `GraphQl:ExposeSchema` | bool (可為 null) | *未設定* → 僅限 Development | 這個執行個體是否可以**揭露自己的 GraphQL schema**。它透過同一個解析出來的旗標，管控兩條能讀取 schema 的路由——introspection 查詢 (`__schema`/`__type`) 與 HotChocolate 內建的 `GET /graphql?sdl`——所以兩者不會各自漂移。未設定時維持出貨行為 (Development 開啟，其他環境關閉)。 |

明確設定這個值，就能在不重新建置的情況下依環境覆寫——`GraphQl__ExposeSchema=true` 正是為那種刻意
公開 public GraphQL API、希望正式環境也能讀取 schema 的 fork 所準備的開關。

這只涉及 schema **揭露**。無論設成哪一種，透過 `POST /graphql` 的查詢執行都不受影響:一個已經知道
自己要發什麼查詢的用戶端，執行期根本不會去讀 schema，所以關掉它不會弄壞任何 GraphQL 消費端。真正
會受影響的是依賴 schema 的**工具鏈**——codegen、Postman/Insomnia 匯入 schema、Apollo Sandbox——這些
應該指向 Development 或 staging 執行個體。Nitro 瀏覽器 IDE 由另一道閘門把關，不受這個設定影響，
一律僅限 Development。各環境的實測行為見第 10 章。

## `RateLimiting:LoginAccount`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `RateLimiting:LoginAccount:Enabled` | bool | `true` | 開啟或關閉逐帳號的登入節流器。 |
| `RateLimiting:LoginAccount:PermitLimit` | int | `10` | 在視窗期間內，單一帳號被允許的失敗嘗試次數，超過即被節流。 |
| `RateLimiting:LoginAccount:WindowSeconds` | int | `900` | 固定視窗的長度，單位為秒。 |

此節流器只套用在 `POST /api/auth/login` 上，依**被嘗試的帳號**(請求本文中的 email)分區，而不是依
client IP，且會在密碼驗證**之前**就先檢查——`AuthController.Login` 會在呼叫
`IAuthService.AuthenticateAsync` 之前先呼叫它，所以一次被節流的請求完全不會耗用任何 Argon2id CPU。
只有*失敗*的嘗試才會計數;一次成功登入會清除該帳號的計數器，所以一個正常登入的合法使用者永遠不會
把自己鎖在外面。無論失敗的原因為何(密碼錯誤、帳號不存在、帳號已停用)，每一次失敗都會計入額度，
而且無論帳號是否存在都會回傳相同的 `429`——這正是讓這一層不會變成帳號列舉 (enumeration) 探測器的
原因。此節流器背後是 `IDistributedCache`(`RateLimiting:LoginAccount`，`LoginAccountRateLimitOptions`，
`src/Struo.Application/Configuration/LoginAccountRateLimitOptions.cs`)——與 session ticket 存放區
共用同一個儲存體，這一點不同於下方的 `RateLimiting:Login`：無論 Redis 是否設定，那個限流器一律是
行程內、逐 pod 的 `AddRateLimiter` 政策。也就是說，是否設定 Redis 決定了這個節流器自己的拓樸：
設定了 `Redis:ConnectionString`，每一個 replica 就會共用同一個帳號的同一份計數器，而不是各自擁有
一份;留空時，則會退回與 ticket 存放區相同的行程內、逐 pod 快取，並失去這個特性。即使設定了 Redis，
這份共用計數器也不是一個保證的上限:`IDistributedCache` 沒有原子性的讀取-寫回操作，因此針對同一個
帳號同時抵達的兩次失敗嘗試，可能都讀到相同的計數值，各自寫回同一個遞增後的值，導致漏掉一次遞增
——這是刻意接受的取捨，不是一個錯誤(細節見 `DistributedCacheLoginAttemptThrottle` 的 class doc)。
`PermitLimit`(`10`)與 `WindowSeconds`(`900`)刻意設定得比逐 IP 限流器的 `5`/`60` 寬鬆許多——這一層
鎖定的是針對單一帳號的
緩慢、持續性密碼噴灑攻擊，而不是短時間的大量流量，也讓一個真實使用者打錯幾次密碼還有餘裕。需要
重新啟動。

`POST /api/auth/login` 一共有**兩道**互相獨立的登入防線——另一道見下一節。第 12 章說明各自實際
對應到哪一種威脅。

## `RateLimiting:Login`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `RateLimiting:Login:Enabled` | bool | `false` | 開啟或關閉逐 client IP 的登入速率限制器。 |
| `RateLimiting:Login:PermitLimit` | int | `5` | 在視窗期間內，每個 client IP 允許的嘗試次數。 |
| `RateLimiting:Login:WindowSeconds` | int | `60` | 固定視窗的長度，單位為秒。 |

此限流器只套用在 `POST /api/auth/login` 上 (固定視窗，依 client IP 分區)；它不是通用的 API 速率
限制器。`Enabled = false` 是出貨預設值，原因是這個部署形態常見使用者的特性，而不是速率限制器本身
的通則:admin 後台的使用者通常是同一個組織自己的員工，而員工之間常常共用同一個 NAT 對外 IP。依
client IP 分區，就會把整個辦公室收斂成單一共用桶，於是在出貨預設的 `5`/`60` 之下，只要幾位員工在
差不多時間登入，就會共同觸發限制——讓這個限流器變成一次自我加害型的阻斷服務，而不是防禦，而且
**不需要**反向代理設定錯誤就會發生;即使 `UseForwardedHeaders` 設定完全正確，光是共用一個對外 IP
就會如此。上方的 `RateLimiting:LoginAccount` 才是出貨即開啟的那一層，因為逐帳號的計數器不會有這種
收斂問題:無論是哪一個 IP 在嘗試，每個帳號都有自己獨立的額度。

開啟這個限流器，只有在單一實例、可直接連線、前面沒有 edge/WAF、且使用者彼此不共用對外 IP 的部署
中才是正確的選擇——例如個人或單租戶安裝，或是一個開發用的 host。若要在任何反向代理背後開啟它，
必須先加上 `UseForwardedHeaders`(並明確設定 `KnownProxies`/`KnownNetworks` 白名單)——這個應用程式
預設不會註冊它——否則這個限流器量到的會是代理伺服器的位址，而不是真正的客戶端，屆時無論是否共用
NAT，該代理背後的每一位使用者都會收斂進同一個桶。若一個共用對外 IP 的團隊仍想啟用這一層，該做的
是調高 `PermitLimit`。多 pod 部署還有第二個理由，即使不存在共用對外 IP 的問題也該讓它保持關閉:
這個限流器的狀態是記憶體內、逐 pod 的，因此無法在多個 replica 間強制一個真正的全域限制——那種拓樸
下的逐 IP 速率限制應該交給 ingress/edge/WAF，那一層看得到真實的 client IP，且位於每個 pod 之前。
需要重新啟動。

## `RateLimiting:Password`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `RateLimiting:Password:Enabled` | bool | `true` | 開啟或關閉改密碼的速率限制器。 |
| `RateLimiting:Password:PermitLimit` | int | `5` | 在視窗期間內，每個已驗證使用者允許的嘗試次數。 |
| `RateLimiting:Password:WindowSeconds` | int | `60` | 固定視窗的長度，單位為秒。 |

此限流器只套用在 `PUT /api/users/{id}/password` 上 (固定視窗，依**已驗證呼叫端**自己的使用者 id
分區，而不是依 client IP)。採用不同的分區鍵是刻意的:這個端點永遠有一個呼叫端身分可以拿來當鍵，所以不同於
`RateLimiting:Login` 的匿名端點，它不會碰到那個但書——登入限流器的逐 IP 鍵，在反向代理沒有轉發
真實 client IP 時，會收斂成單一共用桶。這也代表被消耗的是**動作發出者**的額度:一個
正在為其他帳號做批次重設密碼的超級管理員，會耗盡自己單一的額度並被限流擋下，而每一個目標使用者自己
的額度則完全不受影響——這個端點永遠無法被用來讓某個受害者無法變更自己的密碼。對於直接部署或
單一實例部署而言，`Enabled = true` 屬於安全的預設值;只有在多 pod 部署中，且這個記憶體內、逐 pod
的限流器無法在多個 replica 間強制一個真正的全域上限時，才把它設為 `false`——這跟 `RateLimiting:Login`
面對的但書相同。需要重新啟動。

## `Branding`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Branding:Name` | string | `"StruoCMS"` | 顯示在登入頁、管理後台頂欄與瀏覽器分頁標題上的產品名稱。 |
| `Branding:LogoUrl` | string? | `null` | 顯示在相同位置的 logo URL。 |

這些是**部署期預設值**，並非唯一的真實來源:超級管理員可以在應用程式內編輯品牌名稱與 logo (設定 →
站台設定)，這會被儲存到單例的 `site_settings` 資料庫資料列中。在請求當下
(`GET /api/config`)，實際生效的品牌名稱與 logo 會逐欄位優先採用已儲存的 `site_settings` 值，只有在
尚未儲存任何值時 (或就 logo 而言，已儲存的檔案不再是已發布狀態時) 才退回這些
`appsettings.json` 值。只有要變更*部署期預設值*時才需要重新啟動——變更線上的值是一個應用程式內
的操作，不是一次設定變更。

## `Redis`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Redis:ConnectionString` | string | `""` (空) | 支撐 cookie 認證 session ticket store 的 StackExchange.Redis 連線字串。 |

留空 (預設值) 會退回到記憶體內的分散式快取——這樣一來，每次程序重新啟動都會遺失 session，這對於單次
快速的本機執行沒問題，但不適合任何存活較久或多實例的情境。將此值設為一個真實的 Redis 實例 (慣例上
`docker compose up -d` 已經會在 `localhost:6379` 啟動一個)，即可取得持久、共享的 session。這個值
是在服務註冊期間直接從設定讀取的，並非透過 `IOptions<T>`——不論如何都需要重新啟動。

## `Oidc`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Oidc:Enabled` | bool | `false` | 開啟或關閉外部 OpenID Connect 登入。 |
| `Oidc:Authority` | string? | 預留 URL | OIDC authority/issuer。當 `Enabled` 為 `true` 時為必填。 |
| `Oidc:ClientId` | string? | `REPLACE_ME` 預留值 | OAuth client ID。當 `Enabled` 為 `true` 時為必填。 |
| `Oidc:ClientSecret` | string? | 無——絕不會出現在 `appsettings.json` 中 | OAuth client secret。當 `Enabled` 為 `true` 時為必填;請透過 user secrets、環境變數或 secret manager 提供——絕不要提交它。 |
| `Oidc:CallbackPath` | string | `"/signin-oidc"` | 向身分提供者註冊的本機回呼路徑。 |
| `Oidc:Scopes` | string[] | `["openid", "email", "profile"]` | 請求的 OIDC scope。 |
| `Oidc:ReturnUrlDefault` | string | `"/"` | 登入後預設的重新導向位置。 |
| `Oidc:RequireEmailVerified` | bool | `false` | JIT 帳號連結是否要求身分提供者的 `email_verified` claim。 |
| `Oidc:AllowedTenantId` | string? | 預留值 | 在提供者支援 tenant 的情況下，將 JIT 連結限制在單一 tenant。 |
| `Oidc:AllowedEmailDomains` | string[] | `[]` (空——不限制) | 將 JIT 連結限制在特定的電子郵件網域。 |

若 `Enabled` 為 `true`，啟動驗證會要求 `Authority`、`ClientId` 與 `ClientSecret` 全部非空。JIT
(即時) 佈建會在通過 tenant/驗證/網域檢查後，**依電子郵件相等**將外部身分連結到一個既有的本機帳號。
`RequireEmailVerified` 與 `AllowedEmailDomains` 的預設值偏向寬鬆;但 `AllowedTenantId` 不是——它
出貨時是不會匹配任何東西的預留值 `REPLACE_TENANT_ID`，這會採取失敗封閉 (fail closed) 的方式，拒絕
每一個外部 tenant，直到它被換成真實的值為止。一個啟用 OIDC 的正式環境部署，仍應明確釘住 (pin) 這三
項設定 (一個真實的單一 `AllowedTenantId` 與/或 `AllowedEmailDomains`，以及
`RequireEmailVerified = true`)，而不是依賴零設定的預設值。此區段的所有鍵都需要重新啟動。

## `Serilog`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Serilog:MinimumLevel:Default` | string | `"Information"` | 預設的最低日誌等級。 |
| `Serilog:MinimumLevel:Override` | object (namespace → 等級) | `{ "Microsoft.AspNetCore": "Warning" }` | 逐 namespace 的等級覆寫。 |
| `Serilog:WriteTo` | array | Console sink，外加一個 File sink，寫入 `logs/struo-.log`，具備每日輪替 (daily rolling) 與共享檔案存取 | 已設定的日誌 sink。 |

和本章其他每一個區段不同，`Serilog` 並未被綁定到一個自訂的 C# options 類別——它是在 host 啟動期間，
直接由 Serilog 自己的設定讀取器 (`ReadFrom.Configuration`) 消費的，所以它的形狀依循 Serilog 自身
的設定慣例，而非一個固定的 schema。需要重新啟動 (bootstrap logger 與完整 logger 都只會在啟動時
建構一次)。

## `Testing`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Testing:PostgresConnection` | string | `""` (空——測試套件會跳過) | live-PostgreSQL 整合測試套件的選用連線字串。請指向一個名稱包含 `test` 的**可拋棄**資料庫。 |

這個區段完全不會被執行中的 API 讀取——只有測試專案會讀取它，且是透過它自己的
`ConfigurationBuilder`，該建構器並未註冊環境變數提供者。這就是為什麼在整個設定介面中，這是唯一
`Section__Key` 這個慣例**不**適用的鍵:設定 `Testing__PostgresConnection` 沒有任何效果。真正有效的
環境變數是 **`STRUO_TEST_PG_CONNECTION`**，它會在 `appsettings.Development.json` 中的鍵之前
被檢查。
