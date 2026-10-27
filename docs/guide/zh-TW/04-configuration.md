# 4. 設定參考

要調一個設定鍵，又不確定它叫什麼名字、預設值是什麼、設錯會不會擋下啟動時，答案都在這一章。

## 設定從哪裡來

設定依序疊加，後面的來源覆寫前面：`src/Struo.Api/appsettings.json` →
`appsettings.{Environment}.json` → 環境變數 → 命令列參數。`Query`、`Struo:Cors`、`GraphQl` 三個
區段沒有出現在預設的 `appsettings.json` 裡。設定檔裡沒有這一段可以改，不代表這些鍵不存在，只是
要自己補上一段。

任何一個鍵都可以用環境變數覆寫，把 `:` 換成 `__`，例如 `Database__ConnectionString`；例外是
`Testing` 區段的四個測試連線鍵，見本章最後一節。

改任何一個鍵都要重啟行程才生效。表格下面寫『沒有驗證規則』的鍵，指的是型別正確的值不會被檢
查；型別對不上（例如 `Database:DbType` 拼錯、數字欄位填了文字）在用 `ValidateOnStart` 綁定
的區段會在啟動時失敗，其他區段大多要到第一次用到時才會出錯。

陣列型的鍵要整段換掉：在較高優先層只填前幾個元素，不會縮短預設設定檔已經填滿的陣列，沒被覆
寫的索引維持原值。預設設定檔填滿的六個陣列是 `Oidc:Providers:{id}:Scopes`、
`Struo:Files:ImageTransform:AllowedFormats`、`Struo:Files:ImageTransform:SizeSteps`、
`Struo:Files:ImageTransform:TransformableContentTypes`、`Struo:Files:AllowedContentTypes`、
`Serilog:WriteTo`；前四個即使整段刪掉，啟動時也會補回內建預設值。陣列元素用索引：
`Oidc__Providers__google__Scopes__0=openid`；要縮短陣列就改設定檔本身。

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | 環境變數 | 未設定時為 `Production` |

`launchSettings.json` 的兩個 profile 都把它設成 `Development`，所以第 3 章的 `dotnet run`
跑的是 Development。

`ASPNETCORE_ENVIRONMENT` 決定讀哪個 `appsettings.{Environment}.json`，也決定下面這些依環境
切換的行為：

- 只在 Development 開：`GraphQl:ExposeSchema` 的預設值、Nitro IDE、啟動時的結構檢查（跟
  `migrate:check` 相同的比對，有錯誤就拒絕啟動）。
- 預設只在 Development 開：`/scalar` 與 `/openapi/v1.json`（`OpenApi:Enabled` 可覆寫）。
- 預設除了 Development 之外的環境都生效：cookie `SecurePolicy` 為 `Always`
  （`Auth:Cookie:RequireSecure` 可覆寫）。

以下七個是 `docker-compose.yml` 的 host 對外設定，不會傳進 API 的設定：

- `STRUO_PG_PORT`
- `STRUO_REDIS_PORT`
- `STRUO_ADMIN_PORT`
- `STRUO_API_PORT`
- `STRUO_MINIO_PORT`
- `STRUO_MINIO_CONSOLE_PORT`
- `STRUO_BIND_ADDRESS`（每個埠綁定的 host 位址，預設 `127.0.0.1`）

改了 `STRUO_MINIO_PORT`，`Struo:Files:S3:Endpoint` 要自己跟著改。

`STRUO_ADMIN_EMAIL`、`STRUO_ADMIN_PASSWORD` 不一樣：Compose 會把它們當成
`Auth__BootstrapAdmin__Email`／`Auth__BootstrapAdmin__Password` 傳給 `api` 服務，只影響 Compose
起的那個 API，自己跑的 API 不受影響。`STRUO_ADMIN_PASSWORD` 預設是空的，所以 seed 會隨機產生密碼，
並在 `api` 的 log 印出一次。

## Database

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Database:DbType` | 列舉 | `PostgreSQL` |
| `Database:ConnectionString` | 字串 | 見下文 |
| `Database:TablePrefix` | 字串（可留空） | `struo_` |
| `Database:MigrateOnStartup` | 布林 | `false` |
| `Database:MigrationLockTimeoutSeconds` | 整數（秒） | `60` |

以下是實際值，不是 `appsettings.json` 的巢狀寫法：
```text
Database:ConnectionString = Host=localhost;Port=5432;Database=struo;Username=REPLACE_ME;Password=REPLACE_ME
```

`ConnectionString` 留空或省略會直接讓啟動失敗，不會等到查詢時才出錯。API 自己不建表：資料表由
`migrate` 命令建立，或在 `MigrateOnStartup` 為 `true` 時由啟動建立，見
[第 21 章：資料庫結構管理與升級](21-schema-and-upgrades.md)。

`TablePrefix` 接在框架自帶 12 張資料表的基底名前面（`FrameworkEntityTypes.All` 的 11 個 entity 加上
版本表 `schema_versions`），只能是小寫字母、數字與底線，首字必須是字母，最長 16 字元；空字串就是
不加前綴。範例專案與你自己的集合不受影響。

對一個已經有框架資料表的資料庫改這個值，新名稱下沒有任何表，也沒有版本紀錄：`migrate` 會在新
名稱下建出一整組空的框架資料表並植入初始資料，既有的表不會被讀取；`MigrateOnStartup` 為 `false` 時，
啟動則以「migration 待套用」拒絕。要換前綴，先把既有的表（連同版本表）改成新名稱。

`MigrateOnStartup` 為 `false`（每個環境的預設）時，資料庫還有待套用的 migration，API 就拒絕啟動，
部署流程要先跑 `migrate`。設成 `true`，API 在開始服務之前先套用待套用的 migration。

`MigrationLockTimeoutSeconds` 是 `migrate`、`migrate:baseline` 與 `MigrateOnStartup` 等待
migration 鎖的秒數上限，逾時就失敗；範圍 1 到 3600，設在範圍外會讓啟動失敗。

API 不使用 `Database:MigrationsPath` 與 `Database:AutoSyncSchema` 這兩個鍵：留在設定裡沒有作
用，啟動時各記一筆警告，例如：

```text
[17:42:58 WRN] The configuration key Database:MigrationsPath is not used: migrations are code in the scanned assemblies and are applied with the 'migrate' command.
```

啟動時資料庫這一段的順序是：

1. 設定裡有上面這兩個不使用的鍵時，各記一筆警告。
2. `MigrateOnStartup` 為 `true` 就拿鎖、套用 migration；為 `false` 就檢查資料庫，框架資料表存在
   卻沒有版本表，或還有待套用的 migration，都拒絕啟動。
3. 在 Development，跑一次結構檢查，有錯誤就拒絕啟動。
4. `DbType` 為 `SqlServer` 時，讀一次資料庫有沒有開 `READ_COMMITTED_SNAPSHOT`，沒開就記一筆警告；這個設定是必要的，見
   [第 20 章：部署](20-deployment.md#sql-server-開啟-read-committed-snapshot)。

## Struo:ContentAssemblies

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:ContentAssemblies` | 字串陣列 | `[]` |

清單裡的每個組件名稱都要能被 `Assembly.Load` 解析，通常代表 API 專案要直接參照它；解析不
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
| `Struo:Files:DefaultStatus` | 字串 | `"published"` |
| `Struo:Files:PresignedRedirect` | 布林 | `false` |

`Backend` 與 `DefaultStatus` 是這個區段裡在啟動時真的被驗證的頂層鍵：無法辨識的值直接讓啟動失敗；所選後端的必
要欄位（見下面 Local／S3）也一併驗證。`Backend` 是 `local`、`s3` 或 `custom`；`custom` 在核心不建任何儲存，需要 fork
註冊的 `IFileStorage`（見[第 15 章](15-files-and-media.md)），沒有的話啟動會失敗。`MaxUploadBytes`、`AllowedContentTypes`、
`PresignedRedirect` 跟 `ImageTransform` 裡沒在 [ImageTransform](#imagetransform) 標成會驗證的鍵都沒有驗證規則：型別正確的值不會被檢查。

- `MaxUploadBytes`：同時卡住宣告長度與實際位元組數，謊報長度的用戶端一樣受限。
- `AllowedContentTypes`：留空代表不限制格式，預設設定檔已經列了 18 種 MIME 類型。
- `DefaultStatus`：新上傳檔案的狀態，`"published"` 或 `"draft"`；其他值會讓啟動失敗，錯誤訊息指出
  `Struo:Files:DefaultStatus`。已發布的檔案任何持有 id 的人都能讀取；草稿只有已登入且有 `file` 寫入
  授權的呼叫端能讀取。編輯者之後可以切換狀態（見[第 15 章](15-files-and-media.md)）。設成 `draft` 時，從設定頁上傳的
  Logo 必須先發布才能儲存，否則儲存回 `400`；匿名頁面與唯讀的媒體庫使用者讀這個檔案會得到 `404`。
- `PresignedRedirect`：設成 `true` 時，下載改成 302 轉址到儲存端的預簽章網址；預設 `false`
  由 API 直接串流位元組，適合瀏覽器連不到儲存端的架構。

### Local

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Files:Local:RootPath` | 字串 | `"App_Data/uploads"` |

`Backend="local"` 時，`RootPath` 是唯一在啟動時被驗證的欄位。`Backend="custom"` 時，Local 與 S3 的鍵都不驗證。

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
值，預設值是待替換的 `REPLACE_ME`；`Region`、`ForcePathStyle`、`PresignTtlSeconds` 設錯也不
會擋下啟動。`PresignTtlSeconds` 也決定 `custom` 儲存所回傳的預簽章網址的有效時間。

### ImageTransform

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Files:ImageTransform:Enabled` | 布林 | `true` |
| `Struo:Files:ImageTransform:MaxWidth` | 整數（px） | `4096` |
| `Struo:Files:ImageTransform:MaxHeight` | 整數（px） | `4096` |
| `Struo:Files:ImageTransform:AllowedFormats` | 字串陣列 | 見下文 |
| `Struo:Files:ImageTransform:DefaultQuality` | 整數 | `82` |
| `Struo:Files:ImageTransform:CachePath` | 字串 | `"App_Data/image-cache"` |
| `Struo:Files:ImageTransform:SizeSteps` | 整數陣列 | 見下文 |
| `Struo:Files:ImageTransform:TransformableContentTypes` | 字串陣列 | 見下文 |
| `Struo:Files:ImageTransform:MaxConcurrentTransforms` | 整數 | `2` |
| `Struo:Files:ImageTransform:QueueTimeoutSeconds` | 整數 | `10` |
| `Struo:Files:ImageTransform:CacheMaxBytes` | 整數（位元組） | `2147483648` |
| `Struo:Files:ImageTransform:MaxSourcePixels` | 整數 | `50000000` |

以下是實際值，不是 `appsettings.json` 的巢狀寫法：
```text
Struo:Files:ImageTransform:AllowedFormats = ["webp", "jpeg", "png", "avif"]
```

`AllowedFormats` 留空或整段刪掉，啟動時都會補回內建的 `webp`／`jpeg`／`png`／`avif`，所以設不出「一
種格式都不允許」的狀態；而且這份清單只在呼叫端明確指定 `format` 時才檢查。要完全關掉轉換，
用 `Enabled=false`。轉換只在呼叫端要求寬、高或格式其中一項、該筆檔案的內容類型在 `TransformableContentTypes` 裡，而且
`Enabled` 為 `true` 時才會發生，否則直接串流原始檔案。

`CachePath` 是相對路徑時，是相對於應用程式的 content root，不是行程當下的工作目錄——用
systemd 之類會從別的目錄啟動行程時，這點會影響快取實際落在哪裡。

下面這幾個鍵限制單一呼叫端能讓伺服器計算與儲存多少東西。

- `SizeSteps`：請求能拿到的輸出寬高。請求的 `width` 或 `height` 會往上對齊到下一個階梯；超過最後
  一階就用最後一階（`MaxWidth`／`MaxHeight` 仍然封頂）。預設是 `[64, 128, 256, 320, 480, 640, 768,
  1024, 1280, 1600, 1920, 2560, 3840, 4096]`。階梯越少，快取的變體越少。留空會補回預設值；每個元素都
  必須是正數。
- `TransformableContentTypes`：會被轉換的來源類型，預設是 `image/jpeg`、`image/png`、`image/webp`、
  `image/avif`、`image/gif`。其他圖片（包含 SVG）即使帶了轉換參數，也一律送出原始位元組。
- `MaxConcurrentTransforms`：所有請求加起來同時進行的轉換數（至少 `2`）。匿名請求最多只能占用總數減
  一個名額，所以已驗證的請求（session cookie 或 bearer token）永遠有一個名額可用。請求遇到沒有自己能用的空名額時，最多等
  `QueueTimeoutSeconds` 秒（`0` 以上；`0` 代表不等），之後回 `503` `IMAGE_TRANSFORM_BUSY`，並帶
  `Retry-After` 標頭。
- `CacheMaxBytes`：磁碟上變體快取的容量上限。一次寫入讓快取超過上限時，會從最久沒被讀取的變體開始刪，
  直到快取降到上限的 90%，讓接下來的寫入不必再次清理。設成 `0` 或更小代表不設上限。刪除失敗會記錄下來，不會讓請求失敗。
- `MaxSourcePixels`：會被解碼的來源大小上限，寬乘高（至少 `1`）。大小從檔案標頭讀出；超過上限的來源，
  或標頭讀不出來的來源，一律送出原始位元組。大小是不解碼、只讀標頭得到的；GIF 取其影格所需的畫布大小，
  AVIF 取標頭宣告的最大影像尺寸。

`MaxWidth` 與 `MaxHeight`（至少 `1`）、`DefaultQuality`（1 到 100）、`MaxConcurrentTransforms`（至少 `2`）、
`QueueTimeoutSeconds`、`MaxSourcePixels` 與 `SizeSteps` 的各個元素在啟動時驗證；值超出範圍會讓啟動失敗，並指出是哪個鍵。

## Struo:Cors

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Struo:Cors:AllowedOrigins` | 字串陣列 | `[]` |

`AllowedOrigins` 留空時，`UseCors` 根本不會被呼叫，任何回應都不會帶 CORS header；同源架構（Vite 開發
代理、後台與 API 同網域部署）都該留空。這個鍵直接讀 `IConfiguration`，本身沒有驗證
規則。

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
| `Query:MaxSortFields` | 整數 | `5` |
| `Query:MaxDeepRelations` | 整數 | `20` |
| `Query:MaxExpandRows` | 整數 | `1000` |
| `Query:MaxFacets` | 整數 | `10` |
| `Query:MaxFacetValues` | 整數 | `50` |
| `Query:MaxAggregates` | 整數 | `10` |
| `Query:MaxSearchCandidates` | 整數 | `1000` |

十一個鍵全部都有 `[Range(1, int.MaxValue)]` 驗證，並用 `ValidateOnStart` 綁定，改成 0 或負數
會直接讓啟動失敗；宣告以外的鍵名（例如打錯字）則會被靜靜忽略，不會報錯。

`DefaultLimit` 不可大於 `MaxLimit`。`DefaultLimit` 較大時啟動會失敗，錯誤訊息同時點名這兩個鍵。

`limit` 超過 `MaxLimit` 會被夾到上限，不會被拒絕；沒帶 `limit` 或給了非正值，改用
`DefaultLimit`。超過 `MaxFilterConditions`、`MaxFacets` 或 `MaxAggregates` 則是直接丟出例
外，訊息會點名是哪個上限，查詢在執行前就先被擋下來。`MaxSortFields` 也一樣：頂層 `sort` 與每個巢狀清單的 `sort` 各自計算欄位數；
`MaxDeepRelations` 計算一次請求的 `deep` 樹裡所有關聯節點。`MaxFilterConditions` 是整個請求共用的一份
額度：根層 `filter` 與每個巢狀 `deep` 篩選一起從裡面扣。

`MaxExpandRows` 限制 `deep` 展開一頁資料時，單次關聯讀取最多讀幾列。讀到超過 `MaxExpandRows` 列就回
400 並點名該關聯，不會截斷結果。唯一的例外是只展開一個父列、經由一對多關聯，而且巢狀清單帶有 `limit`：
視窗由資料庫套用，所以不受這個上限限制。`offset` 沒有上限。

`MaxRelationDepth` 算的是路徑裡的關聯跳轉次數，最後一個欄位本身跟 `_junction` 虛擬區段都不算
在內。超過 `MaxSearchCandidates` 不是截斷結果，而是視為 `ISearchProvider` 回傳了不該回傳的
量，直接丟出例外（500）。

## Auth

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Auth:BootstrapAdmin:Email` | 字串 | `"admin@admin.com"` |
| `Auth:BootstrapAdmin:Password` | 字串 | `""` |
| `Auth:Password:MinLength` | 整數 | `8` |
| `Auth:Password:MaxLength` | 整數 | `128` |
| `Auth:Cookie:RequireSecure` | 布林（可留空） | 無（依環境判斷） |
| `Auth:Cookie:Name` | 字串 | `"struo.session"` |

這組帳密由核心的 seed migration（`SeedCoreData`）在空資料庫上套用一次，不會補到已經存在的資料
庫上。這兩個鍵沒有選項類別，seed migration 直接從設定讀成字串，所以跑 `migrate` 的環境裡就要有它們。
密碼留空時，seed 會隨機產生至少 24 個字元的密碼，並在建立管理員的那次執行裡以 Warning 印出一次。自己指定
的密碼必須符合 `Auth:Password`，而且不能是 `admin`（不分大小寫）。只要它還是弱值，`migrate`、`migrate:preview`
與 `Database:MigrateOnStartup` 啟動都會拒絕，錯誤訊息點名 `Auth__BootstrapAdmin__Password`。這個值只用來
植入帳號，帳號建立之後就可以移除。

密碼原則在 `POST /api/users` 與 `PUT /api/users/{id}/password` 共用同一個驗證；
`MinLength`／`MaxLength` 只是輸入合理性的界線。這兩個鍵本身有
`MinLength >= 1 && MinLength <= MaxLength` 的啟動驗證，設反了會讓啟動失敗；只有 `MinLength`
會透過 `GET /api/config` 回傳給前端。

`Auth:Cookie:RequireSecure` 決定 session cookie 是否一律帶 `Secure`。留空時，除了 Development 之外的環境
一律是 `Always`，Development 則跟隨請求本身；`true` 在所有環境強制 `Secure`；`false` 讓 cookie 可以走純
HTTP，前面沒有 TLS 的類 Staging 主機需要這樣設。只要設定了任何 `Struo:Cors` 來源，不論這個鍵怎麼設，
都會強制 `Secure`。

`Auth:Cookie:Name` 是 session cookie 的名稱，留空就用預設值。CSRF 防護與 session 輪替都跟著它走。名稱不是
有效的 cookie 名稱（字母、數字與 ``! # $ % & ' * + - . ^ _ ` | ~``），或 `__Secure-`、`__Host-` 名稱碰上不是
一律 `Secure` 的 cookie，或 `__Host-` 名稱碰上帶 `Domain` 或 `Path` 不是 `/` 的 cookie，啟動都會失敗，
訊息會點出這個鍵。StruoCMS 不送 `Domain`，也不送 `/` 以外的 `Path`。見
[第 25 章](25-after-forking.md)的 `__Host-` 前綴那一節。

## OpenApi

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `OpenApi:Enabled` | 布林（可留空） | 無（依環境判斷） |

留空時，`/openapi/v1.json` 與 `/scalar` 只在 Development 存在；其他環境（含 Production）一律回 404。
`true` 在任何環境都開，`false` 在所有環境都關。這兩個路由沒有任何驗證就公開整份 API 規格，要在
Development 之外打開之前，前面要自己加一層驗證。

## Rbac

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Rbac:PublicReadCollections` | 字串陣列 | `[]` |

這個鍵只在核心的 seed migration 執行時套用一次；改了鍵值再重開，不會回頭幫既有資料庫加上公開讀取
權限——要嘛在第一次跑 `migrate` 前先設好，要嘛之後直接在後台或 API 補授權。

## GraphQl

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `GraphQl:ExposeSchema` | 布林（可留空） | 無（依環境判斷） |
| `GraphQl:MaxExecutionDepth` | 整數，至少 1 | `12` |
| `GraphQl:MaxFieldCost` | 數字，大於 0 | `150` |
| `GraphQl:MaxTypeCost` | 數字，大於 0 | `150` |

留空時只有 Development 預設開放，其他環境（含 Production）一律預設關閉；管的只是 schema 能不能被
查詢（introspection 與 `GET /graphql?sdl`），`POST /graphql` 本身不受影響。內建的 Nitro 瀏覽器
IDE 另外只認開發環境，不受這個鍵控制。

另外三個鍵是單一操作的上限：選擇集最多能巢狀幾層，以及最多能累積多少欄位成本與型別成本。設成 0
或更小，啟動會失敗，訊息會點出是哪一個鍵。預設值是只針對核心 schema 量出來的。fork 加了比較重的
查詢，就把成本上限調高到合適的值，見[第 14 章：GraphQL API](14-graphql.md)的〈深度上限〉。這幾個鍵只從設定讀取：在程式碼裡設定
`GraphQlOptions` 對它們沒有作用。

## ForwardedHeaders

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `ForwardedHeaders:Enabled` | 布林 | `false` |
| `ForwardedHeaders:KnownProxies` | 字串陣列（IP 位址） | `[]` |
| `ForwardedHeaders:KnownNetworks` | 字串陣列（CIDR 範圍） | `[]` |

預設關閉，`X-Forwarded-For`、`X-Forwarded-Proto`、`X-Forwarded-Host` 都會被忽略，API 看到的是它實際
收到的那條連線的位址、scheme 與 host。反向代理擋在 API 前面、而 API 需要知道真正的用戶端時才打開：
每個 IP 的登入限流依這個位址分桶，OIDC 也靠 scheme 與 host 組出重新導向網址。

`Enabled=true` 時，只有 TCP 來源列在 `KnownProxies`（單一位址）、或落在 `KnownNetworks`（CIDR，例如
`172.16.0.0/12`）某個範圍內的請求才受信任，其他來源送的標頭一律忽略。至少要有一筆。值不是 IP 位址或
CIDR 範圍，或兩個清單都空，主機會在啟動時拒絕，並在錯誤訊息點名鍵，例如
`ForwardedHeaders:KnownNetworks:0`。`Enabled=false` 時不會讀這兩個清單。

這個中介軟體排在請求記錄、驗證、工作階段輪替與速率限制之前，只認一層代理。環境變數用索引給陣列，
例如 `ForwardedHeaders__KnownNetworks__0=172.16.0.0/12`。

## RateLimiting

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `RateLimiting:Login:Enabled` | 布林 | `false` |
| `RateLimiting:Login:PermitLimit` | 整數 | `5` |
| `RateLimiting:Login:WindowSeconds` | 整數（秒） | `60` |
| `RateLimiting:LoginAccount:Enabled` | 布林 | `true` |
| `RateLimiting:LoginAccount:PermitLimit` | 整數 | `10` |
| `RateLimiting:LoginAccount:WindowSeconds` | 整數（秒） | `900` |
| `RateLimiting:LoginConcurrency:Enabled` | 布林 | `true` |
| `RateLimiting:LoginConcurrency:PermitLimit` | 整數 | `4` |
| `RateLimiting:LoginConcurrency:QueueLimit` | 整數 | `32` |
| `RateLimiting:Password:Enabled` | 布林 | `true` |
| `RateLimiting:Password:PermitLimit` | 整數 | `5` |
| `RateLimiting:Password:WindowSeconds` | 整數（秒） | `60` |

這十二個鍵都沒有驗證規則，設錯的值要到請求進來才會出錯。`Login:*`、`LoginConcurrency:*` 與 `Password:*` 這幾個限流政策
被擋下時回 429，訊息依政策命名，並帶 `Retry-After`；`LoginAccount` 的 429 由登入端點
自己回，訊息固定。

`Login:*` 是以用戶端 IP 分桶的固定視窗限制，只保護 `POST /api/auth/login`；預設關閉，因為後
台使用者常共用同一個對外 IP，開啟後容易連坐擋下整個辦公室，即使 `ForwardedHeaders` 設定正
確也一樣。

要在反向代理後面啟用，也要設 `ForwardedHeaders:Enabled`，並把代理列在 `KnownProxies` 或
`KnownNetworks`（上面的 `ForwardedHeaders` 一節）；不設，所有用戶端都擠在代理自己的那個桶裡。
這層狀態留在行程記憶體，不會跨副本共用；跨副本的 IP 限制要放在前面的閘道或 WAF。

`LoginAccount:*` 依請求帶的 email 分桶，在驗證密碼之前就先把這次嘗試記上，被擋下的請求不會消耗
Argon2id 運算，同時發出的猜測也不可能一起溜過上限；登入成功會清空該帳號的計數。不論帳號存不存
在、密碼錯還是帳號被停用，都算一次嘗試，而且一律回同一種 429，這是防止帳號列舉的關鍵。

設了 `Redis:ConnectionString` 時，每個帳號是一個跨副本共用的 Redis 原子計數器，Redis 連不上時登
入會回 5xx，不會跳過檢查；留空則計數存在行程內的分散式快取，由各副本內的每帳號鎖序列化，副本之
間各自計數。

預設值比 IP 限流寬鬆、視窗長（900 秒），擋的是長時間針對單一帳號的密碼噴灑。

因為每一次嘗試都會計數，知道某個帳號 email 的人可以把它的額度用光，讓該帳號在視窗剩下的時間內
無法用密碼登入。這是刻意接受的取捨；SSO 登入不受影響。

`LoginConcurrency:*` 限制單一行程內同時驗證密碼的登入數：同時跑 `PermitLimit` 個，最多再有
`QueueLimit` 個在排隊等空位，其餘回 429。不論來源 IP 或帳號，都能框住 Argon2id 的 CPU 與記憶體用
量；每個副本各自執行自己的上限。

`Password:*` 只保護 `PUT /api/users/{id}/password`，以呼叫者自己的 user id 分桶，取不到
user id 時才退回用 IP——這個端點要登入才能呼叫，實務上不會發生。因為分桶鍵是操作者本人，
super-admin 幫別人大量重設密碼扣的是自己的額度，不會鎖到被重設的使用者。

## Branding

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Branding:Name` | 字串 | `"StruoCMS"` |
| `Branding:LogoUrl` | 字串（可留空） | 無 |

品牌名稱與 Logo 平常在後台改，不是改設定檔。這兩個鍵只是資料庫 `struo_site_settings` 還沒有值時的預
設：`GET /api/config` 兩個欄位各自判斷，只有沒存值（或 logo 檔案已下架）時才退回這裡。

## Localization

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Localization:Languages` | 陣列（`Code`、`Name`） | `en`／`zh-TW` |
| `Localization:DefaultLanguage` | 字串 | `en` |

這一節只是 `struo_languages` 資料表的**種子來源**：核心的 seed migration 執行時（`migrate` 或
`MigrateOnStartup`），依清單順序種入每一列（`sort` 依序、`enabled` 皆為真、`isDefault` 標在
`DefaultLanguage` 那一列）。seed migration 只執行一次，用 `migrate:baseline` 接上的資料庫不會
植入；語言在後台 System › Language 編輯。啟動時驗證：至少一個語言、每個 `Code` 符合
`[A-Za-z0-9_-]{1,35}` 且不分大小寫唯一、`Name` 非空白、`DefaultLanguage` 在清單內；任一不符啟
動就失敗。環境變數與 `appsettings.{Environment}.json` 對陣列都是依索引合併：
可以改某一項或往後新增（`Localization__Languages__2__Code=ja`），
但刪不掉既有的項目；要拿掉語言，直接改 `appsettings.json`（這個檔案由 fork 擁有）。

## Redis

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Redis:ConnectionString` | 字串（可留空） | `""` |

留空時退回記憶體內的分散式快取，session 會在行程每次重啟後消失，只適合短暫的本機測試；有多個
副本、或需要 session 撐過重啟時，一定要設定這個鍵。預設值是空字串，鍵整個不見時是 `null`，兩
者都會落到這個記憶體內分支。這個鍵同時決定登入節流的計數存在哪裡：
留空時用上面的記憶體內快取，設了就直接在 Redis 計數。

## Oidc

外部登入以身分提供者為單位設定。`Oidc:ReturnUrlDefault` 對所有提供者通用，每個提供者是
`Oidc:Providers:{id}` 底下的一筆。id 只能用小寫字母、數字與連字號（`[a-z0-9-]`），因為它會出現在網址
裡：`microsoft`、`google`、`acme-sso` 可以，`Google`、`acme_sso` 不行。

| 鍵 | 型別 | 預設值 |
|---|---|---|
| `Oidc:ReturnUrlDefault` | 字串 | `"/"` |
| `Oidc:Providers:{id}:Enabled` | 布林 | `false` |
| `Oidc:Providers:{id}:DisplayName` | 字串 | 無 |
| `Oidc:Providers:{id}:Icon` | 字串 | 無 |
| `Oidc:Providers:{id}:Order` | 整數 | `0` |
| `Oidc:Providers:{id}:Authority` | 字串 | 無 |
| `Oidc:Providers:{id}:ClientId` | 字串 | 無 |
| `Oidc:Providers:{id}:ClientSecret` | 字串 | 無 |
| `Oidc:Providers:{id}:CallbackPath` | 字串 | `"/signin-oidc-{id}"` |
| `Oidc:Providers:{id}:Scopes` | 字串陣列 | `["openid", "email", "profile"]` |
| `Oidc:Providers:{id}:Prompt` | 字串（可留空） | `"select_account"` |
| `Oidc:Providers:{id}:RequireEmailVerified` | 布林 | `false` |
| `Oidc:Providers:{id}:AllowedTenantId` | 字串 | 無 |
| `Oidc:Providers:{id}:AllowedEmailDomains` | 字串陣列 | `[]` |

預設值指的是鍵不存在時的行為。隨附的 `appsettings.json` 放了兩筆停用的項目，`microsoft` 與 `google`，
各自填好自己的值：

```json
"Oidc": {
  "ReturnUrlDefault": "/",
  "Providers": {
    "microsoft": {
      "Enabled": false,
      "DisplayName": "Microsoft 365",
      "Icon": "microsoft",
      "Order": 1,
      "Authority": "https://login.microsoftonline.com/REPLACE_TENANT_ID/v2.0",
      "ClientId": "REPLACE_ME",
      "CallbackPath": "/signin-oidc",
      "AllowedTenantId": "REPLACE_TENANT_ID"
    },
    "google": {
      "Enabled": false,
      "DisplayName": "Google",
      "Icon": "google",
      "Order": 2,
      "Authority": "https://accounts.google.com",
      "ClientId": "REPLACE_ME",
      "CallbackPath": "/signin-oidc-google",
      "RequireEmailVerified": true
    }
  }
}
```

檔案裡還列了 `Scopes`、`Prompt`、`AllowedEmailDomains` 與其他跟預設值相同的鍵（`microsoft` 的
`RequireEmailVerified`、`google` 的 `AllowedTenantId`），上面省略了。

停用的提供者完全不驗證，所以佔位的項目可以留在檔案裡。啟用的提供者必須有 `DisplayName`、`Authority`、
`ClientId`、`ClientSecret`，少了啟動就失敗，錯誤訊息會點名那個提供者的鍵。下列規則也一樣：

- 有設 `CallbackPath` 時，要以 `/` 開頭。
- `Prompt` 是空字串或合法值（見下文）。
- 多租用戶的 Entra `Authority`（`common`、`organizations`、`consumers` 或 consumers tenant 的 GUID）要搭配 `AllowedTenantId` 或
  `AllowedEmailDomains`。
- Google 的 `Authority`（`https://accounts.google.com`）要搭配非空的 `AllowedEmailDomains`，因為 Google
  沒有 tenant 可釘，否則任何 Google 帳號都能登入。
- 啟用中的提供者之間不能共用路徑。每個提供者還有兩條由 id 推導的登出路徑，
  `/signout-callback-oidc-{id}` 與 `/signout-oidc-{id}`，它們不能設定，自訂的 `CallbackPath` 也不能
  跟其中任何一條相撞。

提供者 id 不符合 id 規則時，不論有沒有啟用，啟動都會失敗。提供者停用或不存在時，
`/api/auth/login/oidc/{provider}` 回 404，這不是設定錯誤。

`DisplayName` 是該提供者登入按鈕上的文字。`Icon` 是一個代號，由後台介面對應到圖示；內建的代號與新增
的做法見[第 16 章](16-authentication.md)。按鈕依 `Order` 由小到大排列，相同時依 id。之所以要有
`Order`，是因為設定不會保留檔案裡鍵的順序。

第一次用外部身分登入時，會用 email 相同去對應既有的本機帳號；該提供者的 `AllowedTenantId`、
`RequireEmailVerified`、`AllowedEmailDomains` 這三道檢查都通過才會這麼做——放寬了它們，那個身分提供者裡
email 對得上的人都能接管一個密碼帳號。`AllowedTenantId` 留空會略過 tenant 檢查。提供者若不驗證 email，
就打開 `RequireEmailVerified`，或用 `AllowedEmailDomains` 釘住網域。

`ClientSecret` 刻意不放在 `appsettings.json`，也絕不能提交進版本庫。用環境變數
`Oidc__Providers__<id>__ClientSecret`（例如 `Oidc__Providers__google__ClientSecret`）、
user-secrets（`dotnet user-secrets set "Oidc:Providers:google:ClientSecret" <值> --project src/Struo.Api`），
或不納入版控的 `appsettings.Development.json` 設定。其他鍵也用同樣方式覆寫，例如
`Oidc__Providers__google__Enabled=true`。

`Prompt` 會原樣送成授權請求的 `prompt` 參數。`select_account`（預設）一律顯示提供者的帳號挑選畫面，
不會有人因為瀏覽器剛好持有某個帳號就被靜默登入。`login` 強制使用者重新輸入帳密，`consent` 重新詢問同意，
`none` 完全不顯示頁面，使用者尚未登入時會失敗。可以用空格組合多個值，例如 `login consent`。設為空字串就不送
`prompt`，由提供者決定；JSON 的 `null` 則維持預設值。其他任何值在提供者啟用時會讓啟動失敗，錯誤訊息點名
`Oidc:Providers:<id>:Prompt`。

`CallbackPath` 是身分提供者把瀏覽器導回來的路徑。隨附的項目都明確設了：`microsoft` 是 `/signin-oidc`，`google` 是
`/signin-oidc-google`；沒設的項目用 `/signin-oidc-{id}`。每個提供者要有自己的路徑，兩個提供者不能共用同一條，
因為第一個處理程序會接走所有回呼。要向提供者登記的重新導向網址，是使用者開啟管理介面的來源加上這個路徑；
代理端的做法見[第 16 章](16-authentication.md)。

`RequireEmailVerified` 跟 `AllowedEmailDomains` 預設是放行的。隨附檔案的 `microsoft` 項目帶著
`AllowedTenantId: "REPLACE_TENANT_ID"`，這個值打不中任何 tenant，替換之前會擋掉所有外部登入；`google`
沒有 tenant 可釘，隨附時已打開 `RequireEmailVerified`，啟用前還要設好 `AllowedEmailDomains`（啟用中的 Google
提供者清單為空時，啟動會失敗）。正式環境啟用提供者時，適用於它的檢查都該釘死，
不要依賴預設值：有 tenant 的提供者釘 tenant，再加上 `RequireEmailVerified` 與 `AllowedEmailDomains`。

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

| 鍵 | 覆寫用的環境變數 |
|---|---|
| `Testing:PostgresConnection` | `STRUO_TEST_PG_CONNECTION` |
| `Testing:SqlServerConnection` | `STRUO_TEST_SQLSERVER_CONNECTION` |
| `Testing:MySqlConnection` | `STRUO_TEST_MYSQL_CONNECTION` |
| `Testing:MariaDbConnection` | `STRUO_TEST_MARIADB_CONNECTION` |

四個鍵的預設值都是 `""`。執行中的 API 完全不讀這個區段，只有測試專案自己的
`ConfigurationBuilder` 會讀：先看表上的環境變數，沒有才讀 `appsettings.json` 與
`appsettings.Development.json` 裡的鍵。它們是整個設定面唯一不適用 `__` 覆寫規則的鍵，
`Testing__PostgresConnection` 這類變數沒有作用。

每個連線各自打開那個後端的實機測試：migration 測試、`LiveRepositoryTests`、`TestDatabaseLiveTests`，PostgreSQL
另外還有 `PostgresOnlyTests`。指到的資料庫名稱要包含 `test` 字樣、而且要事先建好，登入帳號要有建立與刪除
資料庫的權限。

留空時，那個後端的實機測試直接回傳、判定通過，所以要看每個測試花的時間來判斷有沒有真的連線。
SQLite 那組資料庫測試不受影響，照樣會跑。

環境變數 `STRUO_TEST_BACKEND` 選測試跑在哪個後端：`Sqlite`（預設）、`PostgreSQL`、`SqlServer`、
`MySql` 或 `MariaDb`。選了實機後端，API 測試主機與單元測試的資料庫也搬到那個後端；那個後端的連
線沒設定時，整個測試執行直接失敗，不會悄悄通過。測試分層見[第 22 章：測試與 CI](22-testing.md)。

## 接下來

設定就緒之後，下一步是定義你自己的第一個內容集合——這是下一章的主題。
