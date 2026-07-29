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

| Key | Type | Default | Effect |
|---|---|---|---|
| `Database:DbType` | enum: `PostgreSQL`\|`MySql`\|`SqlServer`\|`Sqlite`\|`Oracle` | `PostgreSQL` | Selects the SqlSugar backend. Only `PostgreSQL` is the verified runtime target; `Sqlite` is test-only; `MySql`/`SqlServer`/`Oracle` are type-mapped but experimental. |
| `Database:ConnectionString` | string, required | none — ships as a `REPLACE_ME` placeholder | ADO.NET connection string for the selected engine. A missing or empty value fails startup (`[Required]` + `ValidateOnStart`), rather than surfacing as a confusing failure on first query. |
| `Database:MigrationsPath` | string?, optional | empty/absent (disabled) | Directory of reviewed `*.sql` migration scripts applied at startup by the migration runner. Only honored when `DbType` is `PostgreSQL` — a hard no-op on every other backend. Development normally leaves this empty and lets `InitTables` build the schema from the entity classes instead; Production points it at the deployed migrations directory so `001-core-baseline.sql` bootstraps an empty database. |

以上三項都需要重新啟動才會生效。

## `Struo:ContentAssemblies`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:ContentAssemblies` | string[] | `[]` (empty) | Assembly names scanned at startup for `[CmsCollection]` content types. |

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

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:Backend` | string: `"local"`\|`"s3"` | `"local"` | Selects the storage backend. Validated at startup — an unrecognized value fails immediately. |
| `Struo:Files:MaxUploadBytes` | long | `26214400` (25 MB) | Maximum accepted upload size. |
| `Struo:Files:AllowedContentTypes` | string[] | the MIME-type list shown in `appsettings.json` (images, PDF, plain text, MP4, MP3, the Office formats, ZIP) | Allow-list of accepted content types for uploads. An empty array allows all content types. |
| `Struo:Files:PresignedRedirect` | bool | `false` | When `true`, `GET /api/files/{id}/content` responds with a 302 redirect to a storage-presigned URL instead of the API streaming the bytes itself. |

以上全部都在啟動時被驗證 (`ValidateOnStart`)，且需要重新啟動。

### `Struo:Files:Local`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:Local:RootPath` | string | `"App_Data/uploads"` | Filesystem root for uploaded files when `Backend` is `local`. Required (validated) when that backend is selected. |

### `Struo:Files:S3`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:S3:Endpoint` | string? | `REPLACE_ME` placeholder | S3-compatible endpoint URL. Required (validated) when `Backend` is `s3`. |
| `Struo:Files:S3:Bucket` | string? | `REPLACE_ME` placeholder | Target bucket name. Required when `Backend` is `s3`. |
| `Struo:Files:S3:AccessKey` | string? | `REPLACE_ME` placeholder | Access key. Required when `Backend` is `s3`. |
| `Struo:Files:S3:SecretKey` | string? | `REPLACE_ME` placeholder | Secret key. Required when `Backend` is `s3`. |
| `Struo:Files:S3:Region` | string | `"us-east-1"` | Region passed to the AWS S3 SDK client. |
| `Struo:Files:S3:ForcePathStyle` | bool | `true` | Path-style addressing — needed by MinIO and most self-hosted S3-compatible servers. |
| `Struo:Files:S3:PresignTtlSeconds` | int | `300` | Lifetime of generated presigned URLs, in seconds. |

若要使用內建的 MinIO 容器做為此後端，需要執行 `docker compose --profile s3 up -d` (它也會執行那個
一次性的 bucket 建立步驟)——見第 2 章。

### `Struo:Files:ImageTransform`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Struo:Files:ImageTransform:Enabled` | bool | `true` | Turns the on-the-fly image-transform endpoint on or off. |
| `Struo:Files:ImageTransform:MaxWidth` | int | `4096` | Upper bound on requested transform width. |
| `Struo:Files:ImageTransform:MaxHeight` | int | `4096` | Upper bound on requested transform height. |
| `Struo:Files:ImageTransform:AllowedFormats` | string[] | `["webp", "jpeg", "png", "avif"]` | Output formats the transform endpoint will produce. |
| `Struo:Files:ImageTransform:DefaultQuality` | int | `82` | Default encode quality when a request does not specify one. |
| `Struo:Files:ImageTransform:CachePath` | string | `"App_Data/image-cache"` | Root directory for cached transformed-image variants. A relative path is resolved against the application's content root, **not** the process's current working directory — this matters if you ever launch the process from a different working directory than the project folder (e.g. a systemd unit). |

## `Auth:BootstrapAdmin`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Auth:BootstrapAdmin:Email` | string | `"admin@admin.com"` | Bootstrap administrator email. |
| `Auth:BootstrapAdmin:Password` | string | `"admin"` | Bootstrap administrator password. |

**這一組值只會被參照一次:`users` 資料表第一次被建立的時候。** 一旦該資料表存在，之後任何一次啟動都
不會重新讀取或重新套用這些值，即使針對一個被清空的 `users` 資料表也一樣——這個帳號 (以及它的密碼)
就是那第一次植入種子資料時的樣子，或是後來透過正常使用被改成的樣子。若要出貨不同的 bootstrap 身分，
請在針對全新資料庫的第一次啟動*之前*設定這些鍵 (在部署流程中，通常透過
`Auth__BootstrapAdmin__Email` / `Auth__BootstrapAdmin__Password` 環境變數)。

如果一個 `Production` 環境的啟動仍以 (或仍設定為要植入) 字面預設密碼 `admin` 做為種子，API 會記錄
一則**警告**，指名該變更哪一項設定——但它並不會因此拒絕啟動。

## `Rbac:PublicReadCollections`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Rbac:PublicReadCollections` | string[] | `[]` (empty) | Names of collections granted anonymous ("public" role) read access. |

如同 `Auth:BootstrapAdmin`，這份清單也只有第一次啟動時才會生效，但觸發時機不同:它只在 `roles`
資料表第一次被建立時才會被參照——就在那個時刻，種子邏輯 (seeder) 也會建立 `admin` (超級管理員) 與
`public` 角色，並把 bootstrap 管理員指派給 `admin`。之後每一次啟動，整個 RBAC 種子植入步驟 (包含
這個授權迴圈) 都會被跳過，因為 `roles` 已經存在。**編輯這個鍵並重新啟動應用程式，並不會回溯性地在
既有資料庫上授予公開讀取權限**——請直接對一個運作中的資料庫授予該權限 (透過 RBAC 管理 UI 或 API)，
或是在針對全新資料庫的第一次啟動之前就設定好這個值。

## `RateLimiting:Login`

| Key | Type | Default | Effect |
|---|---|---|---|
| `RateLimiting:Login:Enabled` | bool | `true` | Turns the in-app login rate limiter on or off. |
| `RateLimiting:Login:PermitLimit` | int | `5` | Attempts allowed per client IP within the window. |
| `RateLimiting:Login:WindowSeconds` | int | `60` | Fixed-window length, in seconds. |

此限流器只套用在 `POST /api/auth/login` 上 (固定視窗，依 client IP 分區)；它不是通用的 API 速率
限制器。對於直接部署或單一實例部署而言，`Enabled = true` 屬於安全的預設值。只有在多 pod 部署
(例如 Kubernetes) 中，且已在 ingress/edge/WAF 那一層改用逐 IP 速率限制時，才把它設為 `false`——那
一層能看到真實的 client IP，且位於每個 pod 之前，而這個限流器的狀態是記憶體內、逐 pod 的，因此在
那種拓樸下無法在多個 replica 間強制一個真正的全域限制。需要重新啟動。

## `Branding`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Branding:Name` | string | `"StruoCMS"` | Product name shown on the login page, the admin topbar and the browser tab title. |
| `Branding:LogoUrl` | string? | `null` | Logo URL shown in the same places. |

這些是**部署期預設值**，並非唯一的真實來源:超級管理員可以在應用程式內編輯品牌名稱與 logo (設定 →
站台設定)，這會被儲存到單例的 `site_settings` 資料庫資料列中。在請求當下
(`GET /api/config`)，實際生效的品牌名稱與 logo 會逐欄位優先採用已儲存的 `site_settings` 值，只有在
尚未儲存任何值時 (或就 logo 而言，已儲存的檔案不再是已發布狀態時) 才退回這些
`appsettings.json` 值。只有要變更*部署期預設值*時才需要重新啟動——變更線上的值是一個應用程式內
的操作，不是一次設定變更。

## `Redis`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Redis:ConnectionString` | string | `""` (empty) | StackExchange.Redis connection string backing the cookie-authentication session ticket store. |

留空 (預設值) 會退回到記憶體內的分散式快取——這樣一來，每次程序重新啟動都會遺失 session，這對於單次
快速的本機執行沒問題，但不適合任何存活較久或多實例的情境。將此值設為一個真實的 Redis 實例 (慣例上
`docker compose up -d` 已經會在 `localhost:6379` 啟動一個)，即可取得持久、共享的 session。這個值
是在服務註冊期間直接從設定讀取的，並非透過 `IOptions<T>`——不論如何都需要重新啟動。

## `Oidc`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Oidc:Enabled` | bool | `false` | Turns external OpenID Connect login on or off. |
| `Oidc:Authority` | string? | placeholder URL | OIDC authority/issuer. Required when `Enabled` is `true`. |
| `Oidc:ClientId` | string? | `REPLACE_ME` placeholder | OAuth client ID. Required when `Enabled` is `true`. |
| `Oidc:ClientSecret` | string? | none — never in `appsettings.json` | OAuth client secret. Required when `Enabled` is `true`; supply via user secrets, environment variables, or a secret manager — never commit it. |
| `Oidc:CallbackPath` | string | `"/signin-oidc"` | Local callback path registered with the identity provider. |
| `Oidc:Scopes` | string[] | `["openid", "email", "profile"]` | OIDC scopes requested. |
| `Oidc:ReturnUrlDefault` | string | `"/"` | Default post-login redirect. |
| `Oidc:RequireEmailVerified` | bool | `false` | Whether the identity provider's `email_verified` claim is required for JIT account linking. |
| `Oidc:AllowedTenantId` | string? | placeholder | Restricts JIT linking to a single tenant, where the provider supports one. |
| `Oidc:AllowedEmailDomains` | string[] | `[]` (empty — unrestricted) | Restricts JIT linking to specific email domains. |

若 `Enabled` 為 `true`，啟動驗證會要求 `Authority`、`ClientId` 與 `ClientSecret` 全部非空。JIT
(即時) 佈建會在通過 tenant/驗證/網域檢查後，**依電子郵件相等**將外部身分連結到一個既有的本機帳號。
`RequireEmailVerified` 與 `AllowedEmailDomains` 的預設值偏向寬鬆;但 `AllowedTenantId` 不是——它
出貨時是不會匹配任何東西的預留值 `REPLACE_TENANT_ID`，這會採取失敗封閉 (fail closed) 的方式，拒絕
每一個外部 tenant，直到它被換成真實的值為止。一個啟用 OIDC 的正式環境部署，仍應明確釘住 (pin) 這三
項設定 (一個真實的單一 `AllowedTenantId` 與/或 `AllowedEmailDomains`，以及
`RequireEmailVerified = true`)，而不是依賴零設定的預設值。此區段的所有鍵都需要重新啟動。

## `Serilog`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Serilog:MinimumLevel:Default` | string | `"Information"` | Default minimum log level. |
| `Serilog:MinimumLevel:Override` | object (namespace → level) | `{ "Microsoft.AspNetCore": "Warning" }` | Per-namespace level overrides. |
| `Serilog:WriteTo` | array | Console sink, plus a File sink writing `logs/struo-.log` with daily rolling and shared-file access | Configured log sinks. |

和本章其他每一個區段不同，`Serilog` 並未被綁定到一個自訂的 C# options 類別——它是在 host 啟動期間，
直接由 Serilog 自己的設定讀取器 (`ReadFrom.Configuration`) 消費的，所以它的形狀依循 Serilog 自身
的設定慣例，而非一個固定的 schema。需要重新啟動 (bootstrap logger 與完整 logger 都只會在啟動時
建構一次)。

## `Testing`

| Key | Type | Default | Effect |
|---|---|---|---|
| `Testing:PostgresConnection` | string | `""` (empty — suite skips) | Opt-in connection string for the live-PostgreSQL integration test suite. Point it at a **disposable** database whose name contains `test`. |

這個區段完全不會被執行中的 API 讀取——只有測試專案會讀取它，且是透過它自己的
`ConfigurationBuilder`，該建構器並未註冊環境變數提供者。這就是為什麼在整個設定介面中，這是唯一
`Section__Key` 這個慣例**不**適用的鍵:設定 `Testing__PostgresConnection` 沒有任何效果。真正有效的
環境變數是 **`STRUO_TEST_PG_CONNECTION`**，它會在 `appsettings.Development.json` 中的鍵之前
被檢查。
