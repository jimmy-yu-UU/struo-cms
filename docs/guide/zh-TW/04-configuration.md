# 4. 設定參考

要調一個設定鍵，又不確定它叫什麼名字、預設值是什麼、設錯會不會擋下啟動時，答案都在這一章。

## 設定從哪裡來

設定依序疊加，後面的來源覆寫前面：`src/Struo.Api/appsettings.json` →
`appsettings.{Environment}.json` → 環境變數 → 命令列參數。`Query`、`Struo:Cors`、`GraphQl` 三個
區段沒有出現在出廠的 `appsettings.json` 裡。設定檔裡沒有這一段可以改，不代表這些鍵不存在，只是
要自己補上一段。

任何一個鍵都可以用環境變數覆寫，把 `:` 換成 `__`，例如 `Database__ConnectionString`；唯一的例
外是 `Testing:PostgresConnection`，見本章最後一節。

改任何一個鍵都要重啟行程才生效。表格下面寫『沒有驗證規則』的鍵，指的是型別正確的值不會被檢
查；型別對不上（例如 `Database:DbType` 拼錯、數字欄位填了文字）在用 `ValidateOnStart` 綁定
的區段會在啟動時失敗，其他區段要到第一次用到時才會出錯。

陣列型的鍵要整段換掉：在較高優先層只填前幾個元素，不會縮短出廠設定檔已經填滿的陣列，沒被覆
寫的索引維持原值。出廠設定檔填滿的四個陣列是 `Oidc:Scopes`、
`Struo:Files:ImageTransform:AllowedFormats`、`Struo:Files:AllowedContentTypes`、
`Serilog:WriteTo`；前兩個即使整段刪掉，啟動時也會補回內建預設值。陣列元素用索引：
`Oidc__Scopes__0=openid`；要縮短陣列就改設定檔本身。

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | 環境變數 | 未設定時為 `Production` |

`launchSettings.json` 的兩個 profile 都把它設成 `Development`，所以第 3 章的 `dotnet run`
跑的是 Development。

`ASPNETCORE_ENVIRONMENT` 決定讀哪個 `appsettings.{Environment}.json`，也決定下面這些依環境
切換的行為：

- 只在 Development 開：`GraphQl:ExposeSchema` 的預設值、Nitro IDE、`Database:AutoSyncSchema`、
  schema guard。
- 只在非 Production 開：`/scalar` 與 `/openapi/v1.json`。
- 只在 Production 生效：cookie `SecurePolicy` 固定為 `Always`、預設管理員密碼未改的警告。

以下四個是 `docker-compose.yml` 的 host 對外埠，不會傳進 API 的設定：

- `STRUO_PG_PORT`
- `STRUO_REDIS_PORT`
- `STRUO_MINIO_PORT`
- `STRUO_MINIO_CONSOLE_PORT`

改了 `STRUO_MINIO_PORT`，`Struo:Files:S3:Endpoint` 要自己跟著改。

## Database

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Database:DbType` | 列舉 | `PostgreSQL` |
| `Database:ConnectionString` | 字串 | 見下文 |
| `Database:MigrationsPath` | 字串（可留空） | `""` |
| `Database:AutoSyncSchema` | 布林 | `false` |

以下是實際值，不是 `appsettings.json` 的巢狀寫法：
```text
Database:ConnectionString = Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME
```

`ConnectionString` 留空或省略會直接讓啟動失敗，不會等到查詢時才出錯。框架自帶的資料表永遠自動
建立，不需要設定。

`MigrationsPath` 只用來套用你自己準備的 migration 指令碼，留空就停用；它在每一種後端都會執
行，不會檢查指令碼是不是寫給這個後端，寫錯後端會在套用時直接失敗。migration runner 沒有互斥
鎖，多個副本一起啟動時可能同時嘗試套用同一個檔案。

啟動時的順序是：快照既有資料表 → 建立缺少的資料表 →（Development）結構同步 →（設了
`MigrationsPath`）套用 SQL 指令碼 →（Development）schema guard → 植入資料；植入只針對這次啟
動剛建立的資料表。CodeFirst 先建表、指令碼後跑，所以你自己的 migration 只該 `ALTER` 既有資料
表。

`AutoSyncSchema=true` 會依 entity 類別的結構同步既有資料表——新增、修改甚至刪除欄位；只在
Development 生效，其他環境會被忽略並記一筆警告。對已經有資料的資料表，這個刪除欄位的行為可能
直接讓資料消失，而且刪不刪還依後端而定：PostgreSQL 會真的刪掉欄位，SQLite 不會。

## Struo:ContentAssemblies

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:ContentAssemblies` | 字串陣列 | `[]` |

清單裡的每個組件名稱都要能被 `Assembly.Load` 解析，通常代表 API 主機專案要直接參照它；解析不
到就直接讓啟動失敗，不會被跳過。

這個鍵在 `builder.Build()` 之前就直接從 `builder.Configuration` 讀出，不經過
`IOptions<T>`；因此它讀得到啟動前裝好的所有一般設定來源，但要是你在 `CreateBuilder` 之後才註
冊自訂設定提供者，它的內容就看不到，而且不會有任何錯誤訊息。

## Struo:Files

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Files:Backend` | 字串 | `"local"` |
| `Struo:Files:MaxUploadBytes` | 長整數（bytes） | `26214400` |
| `Struo:Files:AllowedContentTypes` | 字串陣列 | `[]` |
| `Struo:Files:PresignedRedirect` | 布林 | `false` |

`Backend` 是這個區段裡唯一在啟動時真的被驗證的頂層鍵：無法辨識的值直接讓啟動失敗；所選後端的必
要欄位（見下面 Local／S3）也一併驗證。`MaxUploadBytes`、`AllowedContentTypes`、
`PresignedRedirect` 跟整個 `ImageTransform` 都沒有驗證規則：型別正確的值不會被檢查。

- `MaxUploadBytes`：同時卡住宣告長度與實際位元組數，謊報長度的用戶端一樣受限。
- `AllowedContentTypes`：留空代表不限制格式，出廠設定檔已經列了 18 種 MIME 類型。
- `PresignedRedirect`：設成 `true` 時，下載改成 302 轉址到儲存端的預簽章網址；預設 `false`
  由 API 直接串流位元組，適合瀏覽器連不到儲存端的架構。

### Local

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Files:Local:RootPath` | 字串 | `"App_Data/uploads"` |

`Backend="local"` 時，`RootPath` 是唯一在啟動時被驗證的欄位。

### S3

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Files:S3:Endpoint` | 字串（可留空） | `"REPLACE_ME"` |
| `Struo:Files:S3:Bucket` | 字串（可留空） | `"REPLACE_ME"` |
| `Struo:Files:S3:AccessKey` | 字串（可留空） | `"REPLACE_ME"` |
| `Struo:Files:S3:SecretKey` | 字串（可留空） | `"REPLACE_ME"` |
| `Struo:Files:S3:Region` | 字串 | `"us-east-1"` |
| `Struo:Files:S3:ForcePathStyle` | 布林 | `true` |
| `Struo:Files:S3:PresignTtlSeconds` | 整數（秒） | `300` |

`Backend="s3"` 時，`Endpoint`、`Bucket`、`AccessKey`、`SecretKey` 這四個鍵在啟動時一定要有
值，出廠值是待替換的 `REPLACE_ME`；`Region`、`ForcePathStyle`、`PresignTtlSeconds` 設錯也不
會擋下啟動。

### ImageTransform

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Files:ImageTransform:Enabled` | 布林 | `true` |
| `Struo:Files:ImageTransform:MaxWidth` | 整數（px） | `4096` |
| `Struo:Files:ImageTransform:MaxHeight` | 整數（px） | `4096` |
| `Struo:Files:ImageTransform:AllowedFormats` | 字串陣列 | 見下文 |
| `Struo:Files:ImageTransform:DefaultQuality` | 整數 | `82` |
| `Struo:Files:ImageTransform:CachePath` | 字串 | `"App_Data/image-cache"` |

以下是實際值，不是 `appsettings.json` 的巢狀寫法：
```text
Struo:Files:ImageTransform:AllowedFormats = ["webp", "jpeg", "png", "avif"]
```

`AllowedFormats` 留空清單代表沒有一種格式會通過檢查。轉換只在呼叫端要求寬、高或格式其中一項、
該筆檔案本身是圖片，而且 `Enabled` 為 `true` 時才會發生，否則直接串流原始檔案。

`CachePath` 是相對路徑時，是相對於應用程式的 content root，不是行程當下的工作目錄——用
systemd 之類會從別的目錄啟動行程時，這點會影響快取實際落在哪裡。

## Struo:Cors

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Cors:AllowedOrigins` | 字串陣列 | `[]` |

`AllowedOrigins` 留空時，`UseCors` 根本不會被呼叫，任何回應都不會帶 CORS header；同源架構
（Vite 開發代理、後台與 API 同網域部署）都該留空。這個鍵直接讀 `IConfiguration`，本身沒有啟動
時驗證。

只要設定了任何一個來源，就會同時把 session cookie 改成 `SameSite=None`＋
`SecurePolicy=Always`，並讓 CORS 政策對這些來源開放 `AllowCredentials`、任意 header 與任意方
法。瀏覽器只在 HTTPS 下才認 `SameSite=None`，所以一旦這個鍵非空，前後端都要走 HTTPS，否則驗證
會悄悄失效。

## Query

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Query:MaxLimit` | 整數 | `100` |
| `Query:DefaultLimit` | 整數 | `25` |
| `Query:MaxFilterConditions` | 整數 | `50` |
| `Query:MaxRelationDepth` | 整數 | `6` |
| `Query:MaxFacets` | 整數 | `10` |
| `Query:MaxFacetValues` | 整數 | `50` |
| `Query:MaxAggregates` | 整數 | `10` |
| `Query:MaxSearchCandidates` | 整數 | `1000` |

八個鍵全部都有 `[Range(1, int.MaxValue)]` 驗證，並用 `ValidateOnStart` 綁定，改成 0 或負數
會直接讓啟動失敗；宣告以外的鍵名（例如打錯字）則會被靜靜忽略，不會報錯。

`limit` 超過 `MaxLimit` 會被夾到上限，不會被拒絕；沒帶 `limit` 或給了非正值，改用
`DefaultLimit`。超過 `MaxFilterConditions`、`MaxFacets` 或 `MaxAggregates` 則是直接丟出例
外，訊息會點名是哪個上限，查詢在執行前就先被擋下來。

`MaxRelationDepth` 算的是路徑裡的關聯跳轉次數，最後一個欄位本身跟 `_junction` 虛擬區段都不算
在內。超過 `MaxSearchCandidates` 不是截斷結果，而是視為 `ISearchProvider` 回傳了不該回傳的
量，直接丟出例外（500）。

## Auth

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Auth:BootstrapAdmin:Email` | 字串 | `"admin@admin.com"` |
| `Auth:BootstrapAdmin:Password` | 字串 | `"admin"` |
| `Auth:Password:MinLength` | 整數 | `8` |
| `Auth:Password:MaxLength` | 整數 | `128` |

這組帳密只在 `users` 資料表第一次建立時套用，之後開機不會重讀，也不會補到已經存在的資料庫上。
seeder 不套用密碼原則，這裡填什麼都會被接受——出廠的 `admin` 只有五個字元，比 `MinLength` 的
8 還短。

密碼原則在 `POST /api/users` 與 `PUT /api/users/{id}/password` 共用同一個驗證；
`MinLength`／`MaxLength` 只是輸入合理性的界線。這兩個鍵本身有
`MinLength >= 1 && MinLength <= MaxLength` 的啟動驗證，設反了會讓啟動失敗；只有 `MinLength`
會透過 `GET /api/config` 回傳給前端。

## Rbac

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Rbac:PublicReadCollections` | 字串陣列 | `[]` |

這個鍵只在 `roles` 資料表第一次建立時套用；改了鍵值再重開，不會回頭幫既有資料庫加上公開讀取權
限——要嘛在第一次啟動前先設好，要嘛之後直接在後台或 API 補授權。

## GraphQl

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `GraphQl:ExposeSchema` | 布林（可留空） | 無（依環境判斷） |

留空時開發環境預設開放、正式環境預設關閉；管的只是 schema 能不能被查詢（introspection 與
`GET /graphql?sdl`），`POST /graphql` 本身不受影響。內建的 Nitro 瀏覽器 IDE 另外只認開發環
境，不受這個鍵控制。

## RateLimiting

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `RateLimiting:Login:Enabled` | 布林 | `false` |
| `RateLimiting:Login:PermitLimit` | 整數 | `5` |
| `RateLimiting:Login:WindowSeconds` | 整數（秒） | `60` |
| `RateLimiting:LoginAccount:Enabled` | 布林 | `true` |
| `RateLimiting:LoginAccount:PermitLimit` | 整數 | `10` |
| `RateLimiting:LoginAccount:WindowSeconds` | 整數（秒） | `900` |
| `RateLimiting:Password:Enabled` | 布林 | `true` |
| `RateLimiting:Password:PermitLimit` | 整數 | `5` |
| `RateLimiting:Password:WindowSeconds` | 整數（秒） | `60` |

這九個鍵都沒有啟動時驗證，設錯的值要到請求進來才會出錯。兩個限流政策被擋下時回 429，訊息依
政策命名，有 `Retry-After` 時一併帶出；`LoginAccount` 的 429 由登入端點自己回，訊息固定。

`Login:*` 是以用戶端 IP 分桶的固定視窗限制，只保護 `POST /api/auth/login`；預設關閉，因為後
台使用者常共用同一個對外 IP，開啟後容易連坐擋下整個辦公室，即使 `UseForwardedHeaders` 設定正
確也一樣。

要在反向代理後面啟用，得先自己加上 `UseForwardedHeaders` 並列出
`KnownProxies`／`KnownNetworks`——這個專案預設沒有註冊它。這層狀態留在行程記憶體，不會跨副本
共用；跨副本的 IP 限制要放在前面的閘道或 WAF。

`LoginAccount:*` 依請求帶的 email 分桶，在驗證密碼之前就先檢查，被擋下的請求不會消耗
Argon2id 運算；只有失敗會累計，登入成功會清空該帳號的計數。不論帳號存不存在、密碼錯還是帳號被
停用，都算一次失敗，而且一律回同一種 429，這是防止帳號列舉的關鍵。

這層限流的計數存在 `IDistributedCache`，跟 session ticket 同一個底層：設了
`Redis:ConnectionString` 就是每個帳號一個跨副本共用的計數器，留空則各副本各自計數。

計數不是原子的讀改寫，極端情況下兩個同時發生的失敗可能只被記成一次。預設值比 IP 限流寬鬆、視
窗長（900 秒），擋的是長時間針對單一帳號的密碼噴灑。

`Password:*` 只保護 `PUT /api/users/{id}/password`，以呼叫者自己的 user id 分桶，取不到
user id 時才退回用 IP——這個端點要登入才能呼叫，實務上不會發生。因為分桶鍵是操作者本人，
super-admin 幫別人大量重設密碼扣的是自己的額度，不會鎖到被重設的使用者。

## Branding

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Branding:Name` | 字串 | `"StruoCMS"` |
| `Branding:LogoUrl` | 字串（可留空） | 無 |

品牌名稱與 Logo 平常在後台改，不是改設定檔。這兩個鍵只是資料庫 `site_settings` 還沒有值時的預
設：`GET /api/config` 兩個欄位各自判斷，只有沒存值（或 logo 檔案已下架）時才退回這裡。

## Redis

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Redis:ConnectionString` | 字串（可留空） | `""` |

留空時退回記憶體內的分散式快取，session 會在行程每次重啟後消失，只適合短暫的本機測試；有多個
副本、或需要 session 撐過重啟時，一定要設定這個鍵。出廠值是空字串，鍵整個不見時是 `null`，兩
者都會落到這個記憶體內分支。這個鍵同時也是 `RateLimiting:LoginAccount` 共用的儲存底層。

## Oidc

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Oidc:Enabled` | 布林 | `false` |
| `Oidc:Authority` | 字串（可留空） | 見下文 |
| `Oidc:ClientId` | 字串（可留空） | `"REPLACE_ME"` |
| `Oidc:ClientSecret` | 字串（可留空） | 無 |
| `Oidc:CallbackPath` | 字串 | `"/signin-oidc"` |
| `Oidc:Scopes` | 字串陣列 | 見下文 |
| `Oidc:ReturnUrlDefault` | 字串 | `"/"` |
| `Oidc:RequireEmailVerified` | 布林 | `false` |
| `Oidc:AllowedTenantId` | 字串 | `"REPLACE_TENANT_ID"` |
| `Oidc:AllowedEmailDomains` | 字串陣列 | `[]` |

以下是實際值，不是 `appsettings.json` 的巢狀寫法：
```text
Oidc:Authority = https://login.microsoftonline.com/REPLACE_TENANT_ID/v2.0
Oidc:Scopes = ["openid", "email", "profile"]
```

`Enabled=true` 時，`Authority`、`ClientId`、`ClientSecret` 都要非空，否則啟動失敗；
`Enabled=false` 時這條規則永遠通過。但 `Enabled` 為 `false`，或 `Authority` 是空的，OIDC 這
個認證 scheme 根本不會被註冊，`/api/auth/login/oidc` 會回 404，不是設定錯誤。

第一次用外部身分登入時，會用 email 相同去對應既有的本機帳號；`AllowedTenantId`、
`RequireEmailVerified`、`AllowedEmailDomains` 這三道檢查都通過才會這麼做——放寬了它們，任何
身分提供者裡 email 對得上的人都能接管一個密碼帳號。

`RequireEmailVerified` 跟 `AllowedEmailDomains` 預設是放行的；`AllowedTenantId` 不是，出廠
值是打不中任何 tenant 的 `REPLACE_TENANT_ID`，在替換之前會擋掉所有外部登入。正式環境啟用
OIDC，這三個鍵都該明確設定，不要依賴預設值。

## Serilog

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Serilog:MinimumLevel:Default` | 字串 | `"Information"` |
| `Serilog:MinimumLevel:Override` | 對照表 | `{"Microsoft.AspNetCore":"Warning"}` |
| `Serilog:WriteTo` | 陣列 | 見下文 |

預設兩個 sink：主控台，以及每日輪替的 `logs/struo-.log`（`shared: true`）。

這個區段不綁自訂選項類別，直接交給 Serilog 自己的設定讀取器解析。兩個 logger 都在啟動時建立
一次，所以改等級要重開行程；`WriteTo` 讀進來之前的最初幾行，只會寫到主控台。

## Testing

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Testing:PostgresConnection` | 字串 | `""` |

這個區段執行中的 API 完全不會讀，只有測試專案自己的 `ConfigurationBuilder` 會讀；也是整個設
定面唯一一個環境變數覆寫規則不適用的鍵，只認 `STRUO_TEST_PG_CONNECTION`（優先），其次才是這
個鍵本身。留空就跳過整組資料庫測試；連線字串指到的資料庫名稱要包含 `test` 字樣。

## 接下來

設定就緒之後，下一步是定義你自己的第一個內容集合——這是下一章的主題。
