# 3. 設定參考

本章的每一項設定都讀自 `src/Struo.Api/appsettings.json` (已提交的預設值)，可依序被
`appsettings.{Environment}.json`、環境變數、最後是命令列參數覆寫——這是標準的 ASP.NET Core 設定
分層順序。

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

## `Database`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `Database:DbType` | enum: `PostgreSQL`\|`MySql`\|`SqlServer`\|`Sqlite`\|`Oracle` | `PostgreSQL` | 選擇 SqlSugar 後端。只有 `PostgreSQL` 是經驗證的執行期目標;`Sqlite` 僅用於測試;`MySql`/`SqlServer`/`Oracle` 雖有型別對應但屬實驗性質。 |
| `Database:ConnectionString` | string，必填 | 無——出貨時為 `REPLACE_ME` 預留值 | 所選引擎的 ADO.NET 連線字串。缺少或空值會導致啟動失敗 (`[Required]` + `ValidateOnStart`)，而不是在第一次查詢時才浮現令人困惑的失敗。 |
| `Database:MigrationsPath` | string?，選填 | 空白/未設定 (停用) | 已審查的 `*.sql` migration 腳本所在目錄，由 migration runner 在啟動時套用。只有當 `DbType` 為 `PostgreSQL` 時才會生效——在其他每一種後端上都是完全的無作用 (no-op)。開發環境通常讓這個值保持空白，改由 `InitTables` 依 entity 類別建構 schema;正式環境則指向已部署的 migrations 目錄，讓 `001-core-baseline.sql` 為一個空資料庫執行 bootstrap。 |

以上三項都需要重新啟動才會生效。

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
- 每一個具名的組件都必須真的能被解析——不論是 host 專案的 `ProjectReference` (在
  `Struo.Api.csproj` 中)，或以其他方式存在為一個可載入的 DLL。無法解析的項目會讓啟動失敗，而不是
  被靜默略過。

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

## `RateLimiting:Login`

| 鍵 | 型別 | 預設值 | 作用 |
|---|---|---|---|
| `RateLimiting:Login:Enabled` | bool | `true` | 開啟或關閉應用程式內的登入速率限制器。 |
| `RateLimiting:Login:PermitLimit` | int | `5` | 在視窗期間內，每個 client IP 允許的嘗試次數。 |
| `RateLimiting:Login:WindowSeconds` | int | `60` | 固定視窗的長度，單位為秒。 |

此限流器只套用在 `POST /api/auth/login` 上 (固定視窗，依 client IP 分區)；它不是通用的 API 速率
限制器。對於直接部署或單一實例部署而言，`Enabled = true` 屬於安全的預設值。只有在多 pod 部署
(例如 Kubernetes) 中，且已在 ingress/edge/WAF 那一層改用逐 IP 速率限制時，才把它設為 `false`——那
一層能看到真實的 client IP，且位於每個 pod 之前，而這個限流器的狀態是記憶體內、逐 pod 的，因此在
那種拓樸下無法在多個 replica 間強制一個真正的全域限制。需要重新啟動。

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
