# 版本紀錄

## 0.15.0

::: danger 破壞性變更
- `ItemService` 的建構子變了。`IUserSessionRevocationService`、`ILanguageCollectionRules`、
  `IUserCredentialStore` 與 `IFileLifecycle` 這幾個參數不見了，建構子最後多了
  `IEnumerable<IItemWriteInterceptor>? interceptors = null` 與
  `IEnumerable<ICollectionDeleteHandler>? deleteHandlers = null`。核心對 `file`、`user`、`language`
  集合的規則，搬進了註冊的攔截器與刪除處理器。`ItemService` 是 DI 替你建的，所以只有自己動手建構
  `ItemService` 的 fork（例如測試裡）才會碰到：改成傳入需要的攔截器與處理器（或都不傳），不再傳那幾個被
  移除的服務。見[第 18 章](18-extension-points.md)。
- 在不可建立的集合裡建立項目，現在回 `400` `BAD_USER_INPUT`，訊息是
  `Items cannot be created in collection '<name>' through the generic items API.`。原本點名
  `POST /api/files` 與 `POST /api/users` 的訊息不見了，拿它們做比對的客戶端要改成比對代碼或新的文
  字。`/api/schema` 在每個集合上多了一個 `creatable` 旗標。
- 後台的 `NON_CREATABLE_COLLECTIONS` 常數移除了，`createRouteGuard` 多了第二個參數 `creatable`。曾把集
  合加進該常數的 fork，要改在集合上設 `Creatable = false`。`ItemDeserializer.Deserialize` 也移除了：由
  `DeserializeForCreate` 取代，它不再檢查純量必填欄位，這項檢查改由 `ItemService` 在 `BeforeWriteAsync`
  之後執行。直接呼叫過 `Deserialize` 的 fork，要自己補上這項檢查。
:::

### 新增

- `IItemWriteInterceptor` 讓 fork 不必改 `ItemService` 就能替項目寫入加上商業規則：
  `BeforeWriteAsync`（交易之外、body 合併之後）、`InTransactionAsync`（交易之內，丟例外會回滾）與
  `AfterCommitAsync`（commit 之後、通知監聽者之前）。它透過 `ItemWriteContext` 看得到建立、更新、還
  原、丟進垃圾桶、復原與清除。攔截器按註冊順序執行，核心的排在前面。專屬端點與級聯清除所帶走的子列不
  會執行它們。見[第 18 章](18-extension-points.md)。
- `ICollectionDeleteHandler` 取代集合通用的丟進垃圾桶與清除，用於帶著自己副作用的刪除。核心的 `file`
  集合用的就是它。
- `[CmsCollection(Creatable = false)]` 讓通用的項目 API 拒絕建立該集合的列。核心把它設在 `file` 與
  `user`。後台從 `/api/schema` 讀 `creatable` 來隱藏「新增」按鈕。見[第 5 章](05-collections.md)。

### 變更

- 建立時，body 若同時違反驗證器或 `MaxLength` 規則與純量必填欄位，現在先回報驗證器的錯誤，跟更新一
  樣。必填檢查在 `BeforeWriteAsync` 之後，所以攔截器可以填起 slug 這類純量必填欄位。
- 更新與還原 `user` 時，email 的正規化與唯一性檢查，現在在必填檢查之前執行，不再在它之後。
- 刪除 `user` 之後的工作階段撤銷，現在在通知變更監聽者之前執行，不再在之後。

## 0.14.0 — 2026-10-27

::: danger 破壞性變更
- `Program.cs` 不再放主機的組裝。服務註冊、速率限制器、請求管線與啟動檢查，全部搬進 `Struo.Api`，
  收成兩個呼叫：`builder.AddStruoCms()` 與 `app.UseStruoCmsAsync()`。`Program.cs` 變成一個很短、
  歸 fork 所有的檔案：上游不會再在裡面改動組裝的內容，檔案本身由你自己改。

  如果你的 fork 改過舊的 `Program.cs`，請改用這個版本的 `Program.cs`，並把你自己的註冊搬到
  `AddStruoCms()` 之後，那裡的註冊贏過核心的預設。設定來源要在呼叫之前加。核心用 `TryAdd` 註冊
  `ISearchProvider`、`IItemChangeNotifier`、`IHtmlSanitizer`、`IPasswordHasher`、`IFileAccessPolicy` 與
  `IFileStorage`，所以這幾個服務寫在 `AddStruoCms()` 之前的註冊也會被保留。
  之前：

  ```csharp
  builder.Services.AddStruoInfrastructure();
  builder.Services.AddStruoMetadata(builder.Configuration, typeof(Program).Assembly);
  builder.Services.AddStruoData();
  // ……其餘的註冊與速率限制器，接著：
  builder.Services.AddScoped<ISearchProvider, MySearchProvider>();
  var app = builder.Build();
  app.UseStruoForwardedHeaders();
  // ……其餘的管線、遷移閘門與遷移 CLI 的分支
  await app.RunAsync();
  ```

  之後：

  ```csharp
  var cli = MigrationCli.Parse(args);
  var builder = WebApplication.CreateBuilder(cli.HostArgs);

  builder.AddStruoCms();
  builder.Services.AddScoped<ISearchProvider, MySearchProvider>();

  var app = builder.Build();
  if (await app.TryRunStruoCommandAsync(cli)) return;
  await app.UseStruoCmsAsync();
  await app.RunAsync();
  ```
- `IHtmlSanitizer`、`IPasswordHasher`、`IFileAccessPolicy` 與 `IFileStorage` 改用 `TryAdd` 註冊。fork 若在
  `AddStruoCms()` 之前就註冊了自己的實作，現在會被保留；以前核心的預設會贏過這麼早的註冊。
- `AuthSchemes.SessionCookieName` 常數移除了。改過它的 fork（第 25 章以前這樣教，改成
  `__Host-struo.session`）必須改設 `Auth:Cookie:Name`，例如 `Auth__Cookie__Name=__Host-struo.session`；
  合併時如果直接採用上游的檔案而沒有這個設定，cookie 會悄悄變回 `struo.session`。讀過那個常數的程式碼改為
  解析 `SessionCookieName`，讀它的 `Value`，或使用 `SessionCookieName.Default`。`__Host-` 或 `__Secure-` 名稱現在
  需要一律 `Secure` 的 cookie：在政策還不是 `Always` 的地方（包含 Development）把 `Auth:Cookie:RequireSecure`
  設成 `true`，否則啟動失敗。
- 設定 `CmsFieldAttribute.Display`、`CmsRelationAttribute.DisplayColumns` 或 `CmsRelationAttribute.MaxDepth`
  會讓建置失敗：它們現在是 `[Obsolete]`，而範本的 `TreatWarningsAsErrors` 把警告變成錯誤。請從你的集合刪掉
  這個屬性；它從來沒有作用。
:::

### 新增

- `GraphQl:MaxExecutionDepth`（12）、`GraphQl:MaxFieldCost`（150）與 `GraphQl:MaxTypeCost`（150）
  設定 GraphQL 的深度與成本上限，各自必須大於 0，否則啟動失敗，訊息會點出是哪一個鍵。預設值不變。
- `AddStruoCms()` 回呼上的 `ConfigureGraphQl`，讓 fork 取得 HotChocolate 的 builder，加上自己的型別與
  根欄位。名稱和產生出來的 schema 衝突時，啟動會失敗，訊息是 `GraphQL name collision`。
  見[第 18 章](18-extension-points.md)。
- `Struo:Files:Backend=custom` 讓 fork 提供自己的 `IFileStorage`，在 `AddStruoCms()` 之後註冊。這個模式下
  核心不建任何儲存，也不驗證 Local 與 S3 的鍵；沒有註冊 `IFileStorage` 時，啟動會失敗，訊息會點出那個鍵。
  `local` 或 `s3` 的儲存也可以被 fork 取代，因為核心現在用 `TryAdd` 註冊預設。
- `IFileStorage.Describe()` 有預設值（型別名稱），local 與 S3 後端各自實作。`files:sweep` 會把它印在
  `storage:` 後面：local 是根目錄，S3 是 bucket 與 endpoint 主機，絕不含憑證。見[第 18 章](18-extension-points.md)。
- `StruoDomainException`（在 `Struo.Domain`）是 fork 自己、給客戶端看的錯誤的基底類別：代碼、400 到 499 的
  狀態與訊息。REST 與 GraphQL 都原樣回傳。狀態超出 400 到 499，在建構例外時就會丟錯。
  見[第 12 章](12-rest-conventions.md#自己的錯誤代碼)。
- 比已套用的最高版本還舊的待套用 migration（較晚才合併的分支）會被標出來：`migrate` 與會套用 migration 的
  啟動會記一則警告，`migrate:status` 在那一列標上 `(out of order)`，`MigrateOnStartup` 關閉時啟動在因待套用
  的 migration 而拒絕之前也會記下警告。migration 仍會套用，結束碼不變。見[第 21 章](21-schema-and-upgrades.md)。
- `Auth:Cookie:Name` 設定 session cookie 的名稱（預設 `struo.session`），CSRF 防護與 session 輪替都跟著它走。
  名稱不是有效的 cookie 名稱、`__Secure-` 或 `__Host-` 名稱碰上不是一律 `Secure` 的 cookie、或 `__Host-` 名稱
  碰上帶 `Domain` 或 `Path` 不是 `/` 的 cookie，啟動都會失敗，訊息會點出這個鍵。見
  [第 4 章](04-configuration.md)與[第 25 章](25-after-forking.md)。

### 安全性

- session 輪替在登入時用大小寫完全一致的比對來隱藏 session cookie，但 cookie handler 讀 cookie 不分大小寫，
  所以被塞進來的 `STRUO.SESSION=` cookie 會被當成 session 續期，而不是被丟棄（session fixation）。
  現在比對不分大小寫。

### 棄用

- `CmsFieldAttribute.Display`、`CmsRelationAttribute.DisplayColumns` 與 `CmsRelationAttribute.MaxDepth` 標成
  `[Obsolete]`。沒有任何東西讀它們；設定它們會產生編譯器警告，範本的 `TreatWarningsAsErrors` 會把它變成建置錯誤，
  下一個次要版本會移除。請從你的集合中刪掉。

## 0.13.1 — 2026-10-27

### 變更

- SQL Server 資料庫需要開啟 `READ_COMMITTED_SNAPSHOT`。在沒有其他連線連著資料庫時，執行一次
  `ALTER DATABASE [your_database] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE`；Azure SQL
  Database 預設就是開啟的。沒開的時候，啟動會記一筆警告，內容點出資料庫名稱與要執行的語句，啟動
  仍會成功。見[第 20 章](20-deployment.md#sql-server-開啟-read-committed-snapshot)。

### 修正

- 並行建立有翻譯的項目時，MySQL、MariaDB 與 SQL Server 會發生死結，API 回 500。建立項目時不再先
  刪除翻譯列再寫入，這是 MySQL 與 MariaDB 的成因；SQL Server 的成因是預設的鎖定隔離等級，由上面
  的設定解決。
- 並行更新有翻譯之集合的不同項目時，SQL Server 會發生死結，MariaDB 偶爾也會。更新時改成就地取代
  每個已存在的翻譯列，沒有的語系才新增，不再先刪除再重新寫入，MariaDB 的情況就此消除；SQL Server
  則在開啟 `READ_COMMITTED_SNAPSHOT` 之後更新都能成功。

### 安全性

- 後台映像在 runtime 階段執行 `apk upgrade`，每次建置都會套用 `nginx-unprivileged` 基底映像建置之後
  才發布的 Alpine 套件修補（本版發布時為 brotli-libs CVE-2026-111497 與 curl/libcurl
  CVE-2026-92392）。容器仍以 uid 101 執行。

## 0.13.0 — 2026-10-22

::: danger 破壞性變更
- `appsettings.json` 的 `Oidc` 區段換了新的結構：共用的 `Oidc:ReturnUrlDefault`，加上每個身分提供者
  各一筆 `Oidc:Providers:{id}`（id 為小寫字母、數字、連字號）。每個提供者的鍵是 `Enabled`、
  `DisplayName`、`Icon`、`Order`、`Authority`、`ClientId`、`ClientSecret`、`CallbackPath`、`Scopes`、
  `Prompt`、`RequireEmailVerified`、`AllowedTenantId`、`AllowedEmailDomains`。請把原本的設定移到某個
  提供者 id 底下，密鑰的環境變數也照樣改名（`Oidc__ClientSecret` 變成
  `Oidc__Providers__microsoft__ClientSecret`；user-secrets 或 `appsettings.Development.json` 裡的
  `Oidc:ClientSecret` 變成 `Oidc:Providers:microsoft:ClientSecret`）。
  直接放在 `Oidc` 底下的舊平面鍵會被忽略，啟動時會用一則警告列出找到的鍵。
  改之前：

  ```json
  "Oidc": {
    "Enabled": true,
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "ClientId": "<client-id>",
    "AllowedTenantId": "<tenant-id>"
  }
  ```

  改之後：

  ```json
  "Oidc": {
    "Providers": {
      "microsoft": {
        "Enabled": true,
        "DisplayName": "Microsoft 365",
        "Icon": "microsoft",
        "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "ClientId": "<client-id>",
        "CallbackPath": "/signin-oidc",
        "AllowedTenantId": "<tenant-id>"
      }
    }
  }
  ```
- 針對編譯依賴它們的 fork，程式碼層級的更名：`appConfigStore.oidcEnabled` 改為 `oidcProviders`；
  `AuthSchemes.Oidc` 改為 `AuthSchemes.OidcFor(id)`，scheme 名稱成為 `oidc-{id}`；`OidcOptions` 的平面屬性
  移到 `OidcOptions.Providers` 底下的 `OidcProviderOptions`；`OidcWiring.ResolveLocalPrincipalAsync`
  多了 `providerId` 參數。
- `GET /api/auth/login/oidc` 改為 `GET /api/auth/login/oidc/{provider}`，提供者不存在或已停用時回 `404`。
- `GET /api/config` 不再回傳 `oidcEnabled`，改回傳 `oidcProviders`，是依按鈕順序排列的
  `{ id, displayName, icon }` 陣列。
- 預設回呼路徑改成每個提供者各一條：`/signin-oidc-{id}`，不再是 `/signin-oidc`。隨附的 `microsoft` 項目把
  `CallbackPath` 設成 `/signin-oidc`，所以已登記的 Entra 重新導向網址照常可用；隨附的 `google` 項目用
  `/signin-oidc-google`。沒設 `CallbackPath` 的提供者項目用 `/signin-oidc-{id}`，請到身分提供者那邊登記這個
  重新導向網址，或把 `CallbackPath` 設成已登記的路徑。每個提供者要有自己的路徑。每個提供者另有自己的登出路徑，
  `/signout-callback-oidc-{id}` 與 `/signout-oidc-{id}`；有用登出回呼的話要重新登記。API 前面的反向代理
  必須轉發這些路徑；後台映像的 nginx 與 Vite 開發伺服器已經會轉發。
- 啟用中、`Authority` 為 Google（`https://accounts.google.com`）的提供者，必須設定非空的
  `Oidc:Providers:{id}:AllowedEmailDomains`，否則啟動失敗，錯誤訊息會點名這個鍵。Google 沒有 tenant 可釘，
  沒有網域的話，任何 Google 帳號都能登入並得到一個新帳號。
:::

### 新增

- 可以同時有多個登入提供者。登入頁為每個啟用的提供者顯示一顆按鈕，依 `Order`、再依 id 排序，每顆按鈕
  啟動該提供者自己的登入。
- Google 預設項目：`appsettings.json` 隨附一筆停用的 `google`，`Authority` 為
  `https://accounts.google.com`，並已打開 `RequireEmailVerified`。
- 位於 `frontend/src/lib/ssoIcons.ts` 的 SSO 圖示登錄表。提供者的 `Icon` 代號決定顯示哪個圖示
  （內建 `microsoft` 與 `google`，其他一律顯示鑰匙圖示），fork 改這個檔案就能加上自己的圖示。
- 登入後，管理介面會回到原本要開的頁面。未登入時開啟深層連結，或工作到一半工作階段結束，重新登入
  （密碼或外部提供者）後都會回到那一頁；只接受同源路徑，其他一律退回儀表板。

### 修正

- 介面語言設為中文時，管理介面仍有部分文字顯示英文。列表欄位的「是／否」與項目數量、關聯項目的
  訊息、上傳失敗訊息、富文本的顏色選單（按鈕、色票、自訂顏色與清除）、對話框與抽屜給螢幕閱讀器的
  「關閉」標籤、行動版側邊欄給螢幕閱讀器的標題與說明，以及伺服器沒有回傳可讀錯誤時顯示的通用
  「請求失敗」訊息，現在都會跟著所選語言。網站名稱載入前，瀏覽器分頁標題顯示為「StruoCMS」。
- 登出請求失敗時，使用者會卡在管理介面裡。現在登出一律會回到登入頁，清掉瀏覽器內屬於該使用者的
  資料，並在伺服器未能確認登出時於登入頁顯示提示；`401`（已經沒有工作階段可結束）視為成功。
- 工作階段過期時，開著的編輯表單會被「尚未儲存」的提示卡住。現在伺服器因工作階段已結束而拒絕請求
  時，管理介面會直接前往登入頁並顯示「登入已逾時」的提示，不再詢問、直接捨棄未儲存的編輯，重新登入後
  回到同一頁。輸入錯誤密碼或未登入時的行為不變。
- 媒體詳細資料對話框可能把一個檔案的標題、替代文字與狀態存到另一個檔案上：快速切換檔案時，較慢的
  前一次載入會蓋掉表單，之後按儲存就會寫到對話框當下指向的檔案。現在載入下一個檔案期間表單會先
  清空，檔案載入完成前儲存按鈕維持停用，更新一律送往已載入的那個檔案；切換檔案之後才完成的版本衝突
  重新整理，也不會再動到新檔案。
- 有效權限面板、檔案挑選器、多檔案欄位與關聯項目清單，不再讓較早、較慢的回應蓋過較新的回應，過期的
  失敗也不會再清掉載入狀態或顯示錯誤。
- 富文本編輯器會把外部圖片改寫成本機路徑。只要 `<img>` 的網址含有 `/files/…/content`，就會被當成受管理
  檔案，並以 `/api/files/…/content` 存入，圖片因此失效。現在只有本站自己的檔案網址、且檔案編號有效
  （可帶查詢字串，查詢字串會保留）才視為受管理檔案；其他圖片載入與儲存時一律照原樣處理。
- 管理介面映像現在對 `/assets/` 以外的所有檔案送 `Cache-Control: no-cache`，`theme-init.js`、
  `favicon.svg` 等檔案在升級後會重新驗證，不再停在瀏覽器裡的舊版本。`/assets/` 底下帶雜湊的檔案
  維持永久快取。
- 直接開啟 `/collections/user/new` 或 `/collections/file/new` 時，現在會回到該集合的列表。使用者
  由「建立使用者」對話框建立、檔案由上傳而來，通用的新增表單本來就不適用。

### 安全性

- `GET /api/auth/login/oidc/{provider}` 不再接受含控制字元的 `returnUrl`。瀏覽器會從網址中移除 tab、歸位與換行字元，
  `/<tab>/evil.example` 因此能通過站內路徑檢查、再被導向站外；這類值現在改為退回
  `Oidc:ReturnUrlDefault`。管理介面自己的 `?redirect=` 檢查也拒絕同樣的字元。
- 前端測試工具升到 `vitest` 4.1.12，修正 `@vitest/mocker` 的 GHSA-qxg6-89mg-rqv8 與
  GHSA-xp26-3454-mjgp。`docs/` 裡 `vitepress` 1.6.4 鎖定的 `vite` 5 另有五則開發伺服器的安全公告，
  列為接受的風險並記在 `docs/pnpm-workspace.yaml`：`vite` 5 沒有修正版，建置出的網站不受影響。

## 0.12.0 — 2026-10-21

::: danger 破壞性變更
- `appsettings.json` 的 `AdminUi` 區段移除，`AdminUiOptions` 與它的啟動驗證一併移除，
  `GET /api/config` 不再回傳 `uiLocales` 與 `uiDefaultLocale`。後台介面提供 `frontend/src/locales`
  裡登錄的每一份訊息目錄，預設語言由 `zh-TW` 改成 `en`。想保留中文為預設，在 `frontend/.env`
  （dev server）或提交的 `frontend/.env.production`（映像建置）設 `VITE_DEFAULT_UI_LOCALE=zh-TW`
  後重新建置 SPA；使用者在切換器自己選的語言仍然優先。請從設定移除 `AdminUi__*` 環境變數與
  `AdminUi` 鍵，主機會直接忽略它們。
- `uiLocaleStore.configure(enabled, default)` 改為 `uiLocaleStore.init()`，`appConfigStore` 不再有
  `uiLocales` 與 `uiDefaultLocale`。i18n 的 fallback 語言是建置時的預設目錄。
- 後台映像改建在 `nginxinc/nginx-unprivileged` 上，nginx 以非 root 使用者執行，容器埠從 `80` 改成
  `8080`。Compose 的對應是 `${STRUO_ADMIN_PORT:-8080}:8080`，主機端的埠不變；fork 自己的 manifest、
  `docker run --publish` 指令或反向代理 upstream 必須改指向容器埠 `8080`。
- `IFileStorage` 新增成員 `IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken)`，fork 自己的
  儲存實作必須補上才能編譯。它以 `StoredBlob(Key, LastModifiedUtc)` 回傳每一個已儲存的 blob：鍵就是
  傳給 `SaveAsync` 的那個，修改時間用 UTC；後端無法回報時給 `DateTime.MaxValue`，依時間清理的流程就會
  保留它。請一頁一頁串流回傳，不要先整批載入；`files:sweep` 每次執行會讀它一到兩次。
- `Struo:Files:ImageTransform:MaxConcurrentTransforms` 至少要是 `2`；設成 `1` 會讓啟動失敗，並指出
  是哪個鍵。匿名請求最多只占用 `MaxConcurrentTransforms - 1` 個轉換名額，所以已驗證的請求（session
  cookie 或 bearer token）永遠有一個名額可用；超過上限的匿名請求會等 `QueueTimeoutSeconds` 秒，之後回
  `503` `IMAGE_TRANSFORM_BUSY`。預設值 `2` 下匿名的冷轉換一次只跑一個；對外提供公開圖片、想讓它們有
  更多並行的 fork，請調高這個設定。
- `POST /api/items/user` 與 GraphQL 的 `createUser` 回 `400` `BAD_USER_INPUT`：建立使用者需要密碼雜湊
  與密碼原則，只有 `POST /api/users` 會套用。請改用該端點，或由超級管理員在後台使用者清單按「新增」
  開啟的對話框建立。
- `ISqlSugarClient` 改為單例：整個行程共用一個 `SqlSugarScope`，不再每個請求建一個，記憶體因此不再
  隨請求數增長（每個請求約 150 位元組，每次 `/health/ready` 約 2.5 KB）。每個非同步流程仍有自己的
  連線與交易，但資料庫工作必須在 async 方法裡執行（不要在 `Task.Run` 的同步 lambda、計時器回呼或
  同步工作中存取資料庫），而且 `CopyNew()` 不會做稽核欄位寫入與 SQL Server 取消轉譯。fork 若把這個
  註冊覆寫成 scoped，或依賴請求結束時釋放 client，行為會不同。fork 自己註冊的 `ICurrentUserAccessor`
  不可以是 scoped（否則啟動失敗），而且要在每次呼叫時才讀取目前使用者。
:::

### 新增

- `files:sweep` 清除本機或 S3 儲存裡的孤兒 blob。不加 `--apply` 只是試跑並列出孤兒；它絕不碰比
  `--min-age-hours`（預設 24）還新的 blob，垃圾桶裡的 `files` 列所參照的 blob 視為使用中。命令會先
  印出儲存與資料庫目標（不含憑證）；孤兒超過符合資格 blob 的 50% 時，`--apply` 會以結束代碼 `3`
  拒絕，除非加 `--force`。見[清除孤兒 blob](20-deployment.md#清除孤兒-blob)。
- [第 25 章：Fork 之後要做的事](25-after-forking.md)列出 fork 要先改的東西：授權著作權行、
  `UserSecretsId`、Sonar 變數、範例專案、後台語言、`__Host-` cookie 前綴，以及 CSP 與轉送標頭設定。
  根目錄新增 `SECURITY.md`，說明這個模板怎麼私下回報漏洞；fork 要換成自己的版本。
- CI 的 `docker` job 用 Trivy 掃描 API 與後台映像，只要有上游已提供修補版本的 HIGH 或 CRITICAL 漏洞
  就會失敗；結果顯示在公開儲存庫的 Code scanning。

### 變更

- 一頁只展開一個父列、經由一對多關聯，而且巢狀清單帶有 `limit` 時，巢狀 `sort`、`offset` 與 `limit`
  改由 SQL 與巢狀 `filter` 一起套用，`Query:MaxExpandRows` 不適用：子列數超過上限的父列，也只回傳
  所要求的那個視窗。這條路徑的排序與列表查詢一樣由資料庫決定（null 位置與文字比對規則依資料庫；
  沒有巢狀 `sort` 時最新的子列在前）。多個父列、或沒有 `limit` 的巢狀 `sort` 仍在記憶體中排序，null
  在最前面，所以一個父列與多個父列的順序可能不同。多對多關聯不變。新增的
  `IItemRepository.QueryWhereInWindowAsync` 預設回傳 `null`，沒有實作它的類別會留在有上限的記憶體路徑。
- API 映像的 `HEALTHCHECK` 與 Compose 的 `api` 健康檢查改從 `ASPNETCORE_HTTP_PORTS` 讀埠號（沒設是
  8080；以 `;` 分隔的清單取第一個），fork 改了監聽埠後探測仍然有效。

### 修正

- 集合產生的 GraphQL 名稱若跟其他集合或內建名稱撞在一起（`article` 與 `articleList`、`category` 與
  `categories`、名為 `revision` 的集合），啟動會中止，原因鏈裡的 `MetadataException` 點名相關集合與
  撞名的名稱，而不是在 GraphQL schema 建置途中以重複型別錯誤失敗。
- JPEG 存下的 `width`／`height` 會跟著 Exif 方向：方向 5 到 8（轉 90 度）會把兩者對調，所以手機橫著
  存下的直幅照片回報的是實際顯示的尺寸。只套用在此後上傳的 JPEG；既有資料列維持原本的尺寸，WebP 與
  AVIF 仍回報編碼尺寸。
- 外部（OIDC）登入可以在 `pnpm dev` 下使用。Vite 開發伺服器把 `/signin-oidc` 與
  `/signout-callback-oidc` 轉發給 API，且所有代理路徑都保留瀏覽器的 `Host`，所以提供者拿到的
  `redirect_uri` 是 `http://localhost:5173/signin-oidc`，不再是 `:5221`。開發時請向提供者登記這個
  網址（以及登出回呼）。

### 安全性

- `Directory.Packages.props` 不再抑制 GHSA-2m69-gcr7-jv3q；`SQLitePCLRaw` 已是 2.1.12，
  `dotnet list package --vulnerable` 查不到任何漏洞。

## 0.11.0 — 2026-10-20

::: danger 破壞性變更
- `SchemaCheckReport` 的 `Errors` 與 `Warnings` 是方法：`GetErrors()` 與 `GetWarnings()`。呼叫它們的程式
  要把屬性存取改成方法呼叫。
- `IRelationExpander.ExpandAsync` 與 `RelationExpander.ExpandAsync` 以一個 `EntityAccessors` 取代原本的三個
  委派，參數位置在 `deep` 之後。它的欄位是 `ProjectTarget`、`ParentId`、`ReadProp`。實作 `IRelationExpander`
  的 fork，要把自己的 `ExpandAsync` 簽章改成接收 `EntityAccessors`；直接呼叫 `ExpandAsync` 的 fork，要改成傳
  `new EntityAccessors(...)`。
- `GraphQlQueryBuilder.BuildQuery` 接受一個 `GraphQlQueryArgs`（篩選、排序、分頁、搜尋、`deep`、facets、
  aggregate），後面接 `collection` 與 `relationTarget`。直接呼叫它的程式要改成先建 `GraphQlQueryArgs`。
- `FileService` 的建構函式以一個 `FileServiceCollaborators`（`Repository`、`Languages`、`CurrentUser`）
  取代 `storage` 之後的三個參數，後面接選用的 `notifier`、`logger` 與 `references`（`ItemPurgePipeline`）。
  依賴注入會自動解析；自己手動建構 `FileService` 的 fork，要改成傳 `new FileServiceCollaborators(...)`。
- `SchemaTypeMapper.TagItemInputName` 是常數，不是方法。把 `TagItemInputName()` 改成 `TagItemInputName`。
- 範本不再附帶 bootstrap 管理員密碼。`appsettings.json` 裡的 `Auth:BootstrapAdmin:Password` 是 `""`，
  `docker-compose.yml` 的 `STRUO_ADMIN_PASSWORD` 預設為空。密碼留空時，建立管理員的那次執行會隨機產生
  至少 24 個字元的密碼，並以 Warning 印出一次。設定的密碼若不符合 `Auth:Password`，或不分大小寫等於 `admin`，
  現在會在任何環境被拒絕：`migrate`、`migrate:preview` 與 `Database:MigrateOnStartup` 啟動都會失敗，錯誤訊息點名
  `Auth__BootstrapAdmin__Password`，資料庫已經植入過也一樣，直到把值改好或移除。只在 Production 出現的
  預設密碼警告已移除。Playwright 測試從 `E2E_PASSWORD` 讀密碼，這個變數是必填。原本依賴
  `admin@admin.com` / `admin` 的 fork，必須自己設定密碼，或從 `migrate` 的輸出讀產生的密碼。
- 已啟用的 `Oidc` 區段，若 `Authority` 的路徑含有 `common`、`organizations` 或 `consumers` 其中一段，
  或是 consumers 租用戶的 GUID，除非設定了 `Oidc:AllowedTenantId` 或 `Oidc:AllowedEmailDomains`，否則啟動時會被拒絕，錯誤訊息點名這兩個鍵。
  使用多租用戶 authority 又沒有設定限制的 fork，必須設定其中一個，或改用單一 tenant 的 authority。
- OIDC 登入現在預設送出 `prompt=select_account`，所以既有的 OIDC 部署在每次登入時都會看到提供者的帳號挑選
  畫面。把 `Oidc:Prompt` 設為空字串，即可維持原本的行為（見「新增」）。
- `GET /api/items/{collection}/{id}/revisions` 對未知的項目 id 回 404，原本回空陣列。垃圾桶中項目的版本紀錄
  （列表與單筆，REST 與 GraphQL）需要該集合的刪除授權；沒有的呼叫端在 REST 收到 404，在 GraphQL 收到空清單
  或 `null`。`IItemUseCases.ListRevisionsAsync` 與 `IGraphQlDataSource.ListRevisionsAsync` 改回傳可為 null 的
  清單，`null` 代表項目未知或被隱藏。讀取結果的程式必須處理 `null`；fork 自己的實作遇到被隱藏的項目也必須回傳
  `null`，否則不會隱藏垃圾桶中的項目。
- `/openapi/v1.json` 與 `/scalar` 只在 Development 註冊，session cookie 在 Development 之外的所有環境一律帶
  `Secure`；兩者原本只看 Production，所以類 Staging 環境（Staging、UAT 等）會公開 API 規格，並讓 cookie 走純
  HTTP。新增的 `OpenApi:Enabled` 與 `Auth:Cookie:RequireSecure` 可分別覆寫預設值。在 Development 之外對外提供
  OpenAPI 的 fork，必須設定 `OpenApi__Enabled=true`；在 Development 之外跑在 HTTP 上的 fork，必須設定
  `Auth__Cookie__RequireSecure=false`。
- 每個 GraphQL mutation 都需要已登入的呼叫端。就算 `public` 角色在該集合上有寫入授權，匿名 mutation 也會被以
  `UNAUTHORIZED` 拒絕；查詢不受影響。這與 REST 的寫入端點一致，那邊本來就要求登入。原本不帶 session cookie
  或 bearer token 就透過 GraphQL 寫入的用戶端，必須先登入。
- 使用者 email 在每條寫入路徑上都以去除前後空白並轉成小寫的形式儲存：`POST /api/users`、透過 items API 與
  GraphQL 對 `user` 集合的建立與更新、OIDC 登入建立的帳號，以及 bootstrap 管理員。與另一位使用者的 email
  只差大小寫的值，會被以 `409` `CONFLICT` 拒絕。登入仍然不分大小寫比對，所以以大小寫混合儲存的資料列照常
  可用，也不會改寫任何既有資料列。期待讀回自己送出的大小寫的用戶端，現在收到的是小寫。升級前，維運人員可用
  `SELECT lower(email), count(*) FROM struo_users GROUP BY lower(email) HAVING count(*) > 1;`
  列出既有的大小寫重複（資料表名稱依 `Database:TablePrefix` 調整），再合併或刪除多餘的資料列。
- `ILoginAttemptThrottle` 以 `TryReserveAsync` 取代 `CheckAsync` 與 `RecordFailureAsync`；`ResetAsync` 不變。
  `TryReserveAsync` 會把這次嘗試算進去並回報是否可以繼續，呼叫端要在驗證密碼之前呼叫它。實作這個介面的
  fork 必須提供它，呼叫舊方法的 fork 必須改呼叫它。
- 每次登入嘗試都計入 `RateLimiting:LoginAccount:PermitLimit`，不只失敗的（登入成功仍會重設計數）；設了
  `Redis:ConnectionString` 時，Redis 連不上期間登入會回 5xx。
- `ILanguageProvider` 改為非同步：`Enabled()`、`DefaultCode()`、`IsEnabled(code)` 由 `GetEnabledAsync()`、
  `DefaultCodeAsync()`、`IsEnabledAsync(code)` 取代，另新增 `CanonicalizeAsync(code)`，回傳已啟用語言設定的
  寫法，未啟用則回 `null`。實作這個介面的 fork 必須改這些簽章並加上 `CanonicalizeAsync`，呼叫舊方法的 fork
  必須改呼叫非同步版本。
- 翻譯 payload 裡的 locale 鍵與 `?locale=` 的值，以語言列設定的寫法儲存與查詢（`ZH-tw` 會變成 `zh-TW`），
  不會改寫任何既有資料列。在 locale 比對區分大小寫的 PostgreSQL 與 SQLite 上（Oracle 預設也區分大小寫，但
  文件中仍列為未驗證），若資料庫裡已有同一個項目以同一個 locale 的兩種寫法各存一列，兩列都會保留；
  維運人員可用
  `SELECT <外鍵欄位>, lower(locale), count(*) FROM <翻譯資料表> GROUP BY <外鍵欄位>, lower(locale) HAVING count(*) > 1;`
  （把 `<外鍵欄位>` 換成翻譯資料表的外鍵欄位）找出，再刪除多餘的資料列。另有一種情況：某列只有一種寫法，
  但不完全等於設定的語言代碼（語言是 `zh-TW` 而資料列存 `zh-tw`），在這些後端上會讀不到，該項目下次更新時
  還會在它旁邊再插入一列。照上面的方式清掉重複列之後，用
  `SELECT DISTINCT locale FROM <翻譯資料表> WHERE locale NOT IN (SELECT code FROM <語言資料表>);`
  找出這類資料列，再逐一用
  `UPDATE <翻譯資料表> SET locale = '<設定的代碼>' WHERE locale = '<儲存的寫法>';` 修正。框架自己的資料表
  帶有 `Database:TablePrefix`（預設 `struo_`）；你自己的資料表維持你取的名稱。
- 新增三個 `Query` 鍵來限制單一請求的成本。`MaxSortFields`（預設 5）限制頂層排序與每個巢狀清單 `sort`
  的欄位數，`MaxDeepRelations`（預設 20）限制 `deep` 樹的關聯節點數，`MaxExpandRows`（預設 1000）限制
  單次關聯讀取最多讀的列數。`offset` 沒有上限。`MaxFilterConditions` 現在是根層篩選與每個巢狀 `deep` 篩選共用的一份額度，
  過去每個巢狀篩選各自計算。超過任一上限的請求回 400；關聯讀取超過 `MaxExpandRows` 時直接拒絕，不會
  截斷。`IItemRepository.QueryWhereInFilteredAsync` 與 `QueryEntityWhereInAsync` 在取消權杖之前多了選用的
  `int? maxRows`，實作這個介面的 fork 必須加上，以位置傳入權杖的呼叫必須改成具名（`ct: ct`）。
  `QueryValidator.Validate` 多了選用的 `FilterBudget`。`DeepExpansionCoordinator.ExpandAsync` 由 `Prepare`
  （驗證 `deep` 樹，接受 `FilterBudget?`）與 `ExpandPreparedAsync`（展開已準備好的樹）取代，用過
  `ExpandAsync` 的呼叫端要改成兩個都呼叫。`ItemService` 在搜尋查詢或根層查詢之前就驗證 `deep` 樹，所以
  依 id 的 `GET` 帶超限的 `deep` 時，即使該 id 不存在也回 400（以前回 404）。
- 媒體庫對檔案的垃圾桶與清除（`DELETE /api/files/{id}`，清除加 `?purge=true`）改為套用其他集合刪除時
  一樣的關聯規則。仍被 `OnDelete.Restrict` 關聯參照的檔案，兩者都會回 `409 CONFLICT`，過去會被移到
  垃圾桶或清除。清除時會把 `OnDelete.SetNull` 的參照設為 null、移除多對多的連接資料列，過去這些參照
  會留著、指向不存在的檔案。通用項目 API 與 GraphQL 刪除檔案走同一條流程（見「修正」）。
- 圖片轉換的參數會被正規化，轉換本身也有了上限。請求的 `width` 或 `height` 會往上對齊到
  `Struo:Files:ImageTransform:SizeSteps` 的下一個值（預設 64、128、256、320、480、640、768、1024、1280、
  1600、1920、2560、3840、4096），所以 `width=100` 會得到 128 寬的圖。`fit=cover` 只對齊寬度，高度依請求的
  比例推算並四捨五入到 8 像素的倍數。`fit` 只能是 `inside` 或 `cover`，其他值回 `400`。呼叫端給的
  `quality` 四捨五入到 10 的倍數，範圍 10 到 100。只有 `TransformableContentTypes`（JPEG、PNG、WebP、
  AVIF、GIF）裡的來源會被轉換。對 SVG 帶 `?width=` 會回原始位元組，超過 `MaxSourcePixels`
  （50 000 000）或標頭讀不出來的來源也一樣。AVIF 網格或疊圖影像一律不轉換。libvips 讀到的尺寸也會在
  解碼前對同一上限檢查。同時最多
  進行 `MaxConcurrentTransforms`（2）個轉換，請求等超過 `QueueTimeoutSeconds`（10）秒就回 `503`
  `IMAGE_TRANSFORM_BUSY`。變體快取由 `CacheMaxBytes`（2 GB）設上限，並以最久未使用者優先淘汰。需要精確像素
  尺寸的用戶端，必須請求階梯上的尺寸或自行設定 `SizeSteps`；傳過其他 `fit` 值的用戶端必須改掉。
- `IImageTransformer.Transform` 在請求之後多一個 `long maxSourcePixels` 參數，並回傳可為 null 的
  `ImageTransformResult?`，`null` 代表解碼器會產生的影像超過該上限。實作這個介面的 fork 必須加上參數並遵守
  上限，呼叫它的 fork 必須傳入上限並處理 `null`（改回傳原始位元組）。
- API 主機啟動時就會載入原生 libvips（`NetVips.BlockUntrusted` 在服務註冊時設定），即使停用圖片轉換也一樣。
  沒有可用 libvips 的主機會啟動失敗。
- `Query:DefaultLimit` 大於 `Query:MaxLimit` 時啟動失敗，錯誤訊息點名這兩個鍵；過去主機照常啟動，預設分頁的
  筆數會超過 `MaxLimit`。
- 管理端映像的 Content-Security-Policy 會擋下指向自身以外來源的圖片與 API 呼叫，除非 `CSP_IMG_EXTRA` /
  `CSP_CONNECT_EXTRA` 列出這些來源。從 CDN 或 presigned bucket URL（`Struo:Files:PresignedRedirect`）提供檔案、
  設了絕對的外部 `Branding:LogoUrl`，或把 `VITE_API_BASE_URL` 指向其他來源的 fork，升級後這些圖片或請求會失效，
  需先設定這兩個變數。
:::

### 新增

- `ForwardedHeaders:Enabled`（預設 `false`）、`ForwardedHeaders:KnownProxies` 與
  `ForwardedHeaders:KnownNetworks` 讓 API 認列出的代理送來的 `X-Forwarded-For`、`X-Forwarded-Proto`、
  `X-Forwarded-Host`，套用在驗證、工作階段輪替與速率限制之前。位址或 CIDR 範圍格式錯誤，或啟用卻兩個清單
  都空，啟動會失敗並點名鍵。部署在反向代理後面、需要真正的用戶端位址（每個 IP 的登入限流）或 `https`
  OIDC 重新導向網址的，才需要設定；關閉時一切照舊。第 4、20 章有說明。

- `[CmsCollection(Name = "...")]` 可以固定集合名稱。這個名稱是權限授予、版本紀錄與其他資料列裡存的鍵，
  所以 fork 在資料出現之前先設好，之後重新命名 C# 類別就不會讓這些資料失去對應（翻譯類別的外鍵屬性
  仍須叫 `{類別名稱}Id`）。名稱必須符合 `^[a-z][A-Za-z0-9]*$` 且不可重複；重複時啟動會丟出
  `MetadataException` 並指出兩個型別。沒設 `Name` 時，集合仍沿用 camelCase 的類別名稱。

- 編輯者可以在媒體庫的檔案對話框把檔案切換為 `published` 或 `draft`；項目 API 對 `published`、`draft`
  以外的檔案 `status` 回 `400`。`Struo:Files:DefaultStatus`（`published` 或 `draft`，預設
  `published`）決定新上傳檔案的狀態，其他值會讓啟動失敗。已發布的檔案仍然任何持有其 id 的人都能讀取。
  模型說明見第 15 章。
  若某個檔案資料列儲存的 `status` 兩者皆非，任何帶有 `status` 的更新都會回 `400`，管理介面儲存標題或替代文字也
  包含在內；用 `PUT /api/items/file/{id}` 送出有效的 `status`，或直接修正該欄位，即可修復這類資料列。

- `Oidc:Prompt`（預設 `select_account`）設定 OIDC 授權請求的 `prompt` 參數，讓提供者一律顯示帳號挑選畫面。
  值可以是 `select_account`、`login`、`consent`、`none` 的一個或多個，以空格分隔；留空則不送 `prompt`
  （也就是原本的靜默登入）。無效的值會讓啟動失敗並點名 `Oidc:Prompt`。
  第 4、16 章有說明。

### 安全性

- 在 MySQL 與 MariaDB 上，語言代碼可以透過翻譯欄位的排序注入 SQL。用 `"Code"` 這種 JSON 鍵（而不是
  `code`）寫入的語言會略過代碼格式檢查，而排序把 locale 以重複引號的字串常值嵌進 ORDER BY 文字，這兩個後端
  並不把這種寫法視為安全。排序現在由型別化的 SqlSugar 運算式建出，把 locale 當成 SQL 參數綁定，任何排序都
  不再組 SQL 文字。代碼格式規則（`[A-Za-z0-9_-]`，1 到 35 個字元）現在在寫入交易內對儲存的語言資料列執行，
  與 JSON 鍵的大小寫無關。

- 帶著有效 session cookie 的密碼登入或 OIDC 登入，會把新使用者登入到該 cookie 既有的 session，因此被塞進
  受害者瀏覽器的 cookie 會變成受害者的 session。現在每次登入一律發出全新的 session，請求帶來的那個 session
  在登入成功後即撤銷；登入失敗則不動它。

- 登入嘗試現在會在驗證密碼之前就先記到帳號上，同時發出的猜測不再能讀到同一個計數、一起溜過
  `RateLimiting:LoginAccount:PermitLimit`。設了 `Redis:ConnectionString` 時，計數是所有實例共用的
  Redis 原子計數器。新增的 `RateLimiting:LoginConcurrency` 區段
  （`Enabled` 為 true、`PermitLimit` 4、`QueueLimit` 32）限制單一行程內同時驗證密碼的登入數，其餘回
  429 並帶 `Retry-After`。知道某個帳號 email 的人可以讓它在視窗結束前無法用密碼登入；SSO 登入不受影響。

- 管理端映像會送 `Content-Security-Policy`（指令稿只從映像本身載入、不可被嵌入框架、不允許外掛）與
  `Permissions-Policy`（關閉相機、麥克風、定位、支付與 USB）。代理出去的 `/api/*` 回應與兩條
  OIDC 回呼路徑帶 `default-src 'none'; frame-ancestors 'none'; sandbox`。容器變數 `CSP_IMG_EXTRA` 與
  `CSP_CONNECT_EXTRA`（預設空字串）可為 `img-src` 與 `connect-src` 加入來源；圖片或 API 呼叫走向其他來源
  （例如 CDN 或 bucket 的 presigned URL）的 fork 必須設定它們（見破壞性變更）。主題初始化是檔案
  `theme-init.js`，不再是 `index.html` 裡的內嵌指令稿。

### 修正

- OIDC 登入可以在隨附的管理映像 nginx 後面運作。映像把 `/signin-oidc` 與 `/signout-callback-oidc` 和
  `/api/` 一樣代理給 API，並把用戶端的 `Host` 標頭連同埠號當作 `Host` 與 `X-Forwarded-Host` 轉送（原本送的是
  不含埠號的主機名稱），讓 API 組出的重新導向網址與向提供者登記的一致。`/api/` 之外的自訂
  `Oidc:CallbackPath` 要在 `frontend/nginx/default.conf.template` 另加一個 `location` 區塊。

- 外部登入失敗或被拒絕（提供者錯誤、使用者拒絕同意、tenant、網域、email 或停用帳號的拒絕）時，瀏覽器會被
  導向 `/login?error=<code>`，登入頁顯示在地化的訊息後移除該參數；原本瀏覽器會停在一段原始的 `401` JSON
  回應上。代碼是 `external_login_failed`、`external_login_denied`，以及
  `external_login_<NoEmail|EmailNotVerified|DomainNotAllowed|TenantNotAllowed|Inactive>`；例外訊息絕不會進到
  網址。第 16 章列出這些代碼。

- `Struo:ContentAssemblies` 裡載入不了的項目仍會讓啟動失敗並丟出 `MetadataException`，現在它的
  `InnerException` 帶著原始的載入失敗。

- 已啟用語言清單以非同步查詢載入，載入失敗時不會留到請求結束：下一次存取會重新查詢。

- `/health/live` 與 `/health/ready` 不再解析呼叫端的權限，所以角色資料庫失敗時，存活探測不會回 500
  而讓健康的執行個體被重啟。

- Production 環境啟動時若 `Redis:ConnectionString` 是空的，會記錄一則警告：工作階段與登入節流此時存放在
  行程記憶體，只能跑單一副本，重新啟動會讓所有使用者登出。取代角色的權限授權時，會加入呼叫端已經開啟的交易，
  不再自行提交。第 16 章說明 API 存取權杖在改密碼後仍然有效。

- 寫入撞到資料庫的唯一鍵（例如重複的角色名稱）時，REST 與 GraphQL 都回 `409 CONFLICT`，訊息是
  `A record with the same unique value already exists.`，每一種支援的資料庫都一樣；先前會回 500。訊息不會
  出現索引或欄位名稱。

- 清除（purge）項目時，會把所有參照它的資料列的外鍵設為 null，這個陳述式現在會為欄位名稱加上引號。關聯欄位的
  欄位名稱若是保留字（`order`、`user`、`group`），清除會因 SQL 語法錯誤而失敗；混合大小寫的自訂欄位名稱在
  PostgreSQL 上則比對不到。

- 透過通用項目 API（`DELETE /api/items/file/{id}`）或 GraphQL 的 `deleteFile` 刪除檔案，行為與媒體庫一致。
  移到垃圾桶時，若該檔案是品牌 Logo 會一併清除這個設定；清除（purge）時還會刪除翻譯、Logo 參照與儲存的檔案
  內容。清除不再留下檔案內容，刪除檔案內容時儲存端失敗，會以警告記錄並附上儲存鍵，不再被忽略。

- 在影格標頭之前帶有超過 64 KB EXIF 或 ICC 中繼資料的 JPEG（手機照片常見），上傳時會取得寬度與高度，圖片轉換端點
  也會縮放它，不再直接回傳原圖。讀取器依 JPEG 區段長度一路走到影格標頭；被截斷的檔案，或區段長度超出檔案結尾的檔案，
  仍回報沒有尺寸。

- 上傳時若資料庫寫入失敗（資料庫中斷、請求被取消），會移除已經儲存的檔案內容，並回傳原本的錯誤。移除檔案內容
  時若失敗，會以警告記錄並附上儲存鍵，不會取代上傳本身的錯誤。

### 升級說明

Redis 登入節流改用新的 `login-attempts:` 鍵前綴計數，所以升級時每個帳號的登入嘗試計數會歸零；舊的
`login-throttle:` 項目會自行過期。滾動式部署期間，仍在 0.10 的實例與
0.11 的實例各用不同的鍵計數，帳號在所有實例都升到 0.11 之前，可能收到上限兩倍的嘗試次數。

語言代碼的格式規則在每次寫入語言集合（包括刪除）時，檢查該集合的每一個資料列。已經存在的格式不合的代碼，會讓
語言集合的所有寫入被擋下，直到你修正它為止；若有兩個以上格式不合的資料列，API 既修不了也刪不掉它們。請先檢查
已儲存的語言資料列裡是否有不符合 `[A-Za-z0-9_-]{1,35}` 的代碼，並在升級前直接在資料庫裡修正或刪除這些資料列。

快取裡既有的圖片變體是依未正規化的參數產生鍵，現在的請求會算出不同的鍵，所以這些變體再也不會被讀到。它們仍
計入 `CacheMaxBytes`，第一次碰到上限的寫入可能會把它們清掉。刪除 `CachePath` 目錄可以立刻回收空間；
`CacheMaxBytes` 設為 `0`（不設上限）時，它們會一直留著，直到你自己刪除。

升級前請在每台主機安裝 libvips，因為主機現在需要它才能啟動（見破壞性變更）。

若已啟用 OIDC，且 `Oidc:Authority` 的路徑含有 `common`、`organizations` 或 `consumers` 其中一段（或 consumers
租用戶的 GUID），升級前請先設定 `Oidc:AllowedTenantId` 或 `Oidc:AllowedEmailDomains`，否則主機會拒絕啟動。

若你的 `.env` 是從較早的 `.env.example` 複製來的，裡面會有 `STRUO_ADMIN_PASSWORD=admin`。升級前請清空它，或改成
符合 `Auth:Password` 的值：compose 的 `api` 服務會執行 `Database:MigrateOnStartup`，遇到這個值就拒絕啟動，資料庫
已經植入過也一樣。

若 fork 換掉了 `frontend/nginx/default.conf.template`，請帶過去 CSP 與 `Permissions-Policy` 標頭、`/signin-oidc` 與
`/signout-callback-oidc` 兩個 location，以及 `Host` 與 `X-Forwarded-Host` 用的 `$http_host`。

## 0.10.0 — 2026-10-14

::: danger 破壞性變更
- REST 回應裡每個 `DateTime` 都是以 `Z` 結尾的 UTC 時刻，例如 `2026-10-14T02:00:00Z`。多數欄位以前是沒有
  時區的字串。把那些字串當成本地時間讀的客戶端，現在會讀成不同的時間，請檢查解析這些值的程式。
  從這版起擷取的修訂快照也帶 `Z`，REST 與 GraphQL 都一樣。
- 透過 API 寫入只有日期的值（例如 `2026-10-14`）是 UTC 午夜，回傳 `2026-10-14T00:00:00Z`，所以 UTC 以西
  的瀏覽器會顯示前一天。
:::

### 修正

- 管理後台的日期時間欄位顯示瀏覽器本地時間。以前在 UTC+8 輸入 10:00，重新載入後會顯示 02:00。
- 透過 REST 或 GraphQL 寫入帶時差的日期時間（例如 `2026-10-14T10:00:00+08:00`），在一般的 `DateTime`
  欄位，以及 MySQL、MariaDB 與 SQLite 上帶時區的欄位，現在存成它指的那個時刻。以前是存成 API 主機的本地時間。

### 升級說明

0.9.4 及更早版本，在 UTC 以外的主機上，透過 API 寫入帶非 `Z` 時差的值，在一般的 `DateTime` 欄位，
以及 MySQL、MariaDB 與 SQLite 上帶時區的欄位，存進去的是主機本地時間。那些資料列和正確的看不出差別，
需要的話得手動修正。PostgreSQL 與 SQL Server 上帶時區的欄位，從 0.9.2 起就存得正確。

管理後台寫入的值一直都是對的。0.9.4 及更早版本產生的修訂快照，仍是沒有時區的字串。

## 0.9.4 — 2026-10-14

### 安全性

- `System.Security.Cryptography.Pkcs` 升到 10.0.13。`Microsoft.Data.SqlClient`（經由 `SqlSugarCore` 與
  `FluentMigrator.Runner.SqlServer`）和 `Oracle.ManagedDataAccess.Core` 帶進的 9.0.0 受 GHSA-846f-f764-76mx
  （高）與 GHSA-2fmv-rv2c-59x7（中）影響，兩者都是 .NET 的阻斷服務弱點。NuGet 稽核會因此讓 0.9.3 及更早
  版本的還原失敗。

## 0.9.3 — 2026-10-13

### 修正

- 一般 `DateTime` 欄位（沒有掛 `TimestampWithTimeZone`，例如 `CreatedAt`、`UpdatedAt`、`DeletedAt`
  與 fork 自己的日期欄位）用 `_eq`、`_neq`、`_gt`、`_gte`、`_lt`、`_lte`、`_in`、`_nin` 篩選時，帶 `Z` 或時差的值
  照它指的時刻比，沒有時區的值當成 UTC。0.9.2 及更早版本把這種值換成 API 主機的當地時間來比，所以 API
  主機不在 UTC 時，結果會差一個時差；改版後這類查詢的結果會跟著變，這是行為修正。
- SQL Server 上，對掛 `TimestampWithTimeZone` 的欄位算 `min`／`max` 不再擲出
  `InvalidCastException`。
- PostgreSQL 上，這些欄位的 `min`／`max` 回傳 UTC 時刻（JSON 結尾帶 `Z`），不再帶 API 主機的時差。

### 安全性

這幾項都是建置工具的間接依賴，不會進到打包後的程式：

- 前端與文件站的 `source-map-js` 升到 1.2.2（GHSA-68fv-2mgg-jv7q）。
- 文件站的 `vue` 升到 3.5.43，帶進修好的 `@vue/server-renderer`（GHSA-g2v6-rqmx-r4w6）。
- 前端的 `postcss-selector-parser` 升到 7.1.6（GHSA-rj75-hqrm-r3gf）。`@tailwindcss/typography` 把它
  鎖在 6.0.10，6.x 沒有修補版，所以 `frontend/pnpm-workspace.yaml` 用 override 跨主版號升級。

### 升級說明

不需要處理資料：CMS 自己寫入的時間戳（稽核欄位、修訂、工作階段等）一直都是對的，改的只有篩選與彙總怎麼讀值。

## 0.9.2 — 2026-10-13

### 修正

- 掛 `[ColumnShape(ColumnShape.TimestampWithTimeZone)]` 的 `DateTime` 欄位，在 PostgreSQL 與
  SQL Server 上存的是 UTC 時刻，讀回 `DateTimeKind.Utc`，不受 PostgreSQL 伺服器 `TimeZone` 與 API
  主機時區影響。核心裡是 `UserSession.CreatedAt`／`ExpiresAt`、`SiteSettings.UpdatedAt`、
  `MediaFolder.CreatedAt`／`UpdatedAt`。
- 更新媒體資料夾時 `MediaFolder.CreatedAt` 保持不變，不管 API 主機與 PostgreSQL 的時區是否相同。
- SQL Server 上這些欄位保留到 100 奈秒，不會被捨入到 1/300 秒。
- REST 與 GraphQL 用 `_eq`、`_neq`、`_gt`、`_gte`、`_lt`、`_lte`、`_in`、`_nin` 篩選這些欄位時，值
  當成時刻比較：帶 `Z` 或時差的值照它指的時刻，沒有時區的值當成 UTC，結果不受資料庫伺服器與 API
  主機的時區影響，每個後端都一樣。0.9.1 的 MySQL、MariaDB 與 SQLite 把帶 `Z` 或時差的值換成 API
  主機的當地時間來比，PostgreSQL 依連線時區解讀沒有時區的值。`_contains`、`_starts_with`、`_ends_with`
  照舊。
- PostgreSQL 與 SQL Server 上，這些欄位在 API JSON 裡是結尾帶 `Z` 的 UTC 字串，例如 `MediaFolder`
  的 `createdAt`、`updatedAt`，以及 fork 自己掛 `TimestampWithTimeZone` 的欄位。PostgreSQL 原先輸出
  API 主機的時差（如 `+08:00`），SQL Server 沒有時區後綴；用戶端照字串解析時要確認這點。

### 升級說明

只有 PostgreSQL 需要處理，而且只在 0.9.1 或更早版本寫入資料時，連線的時區（伺服器的 `TimeZone`，
連線字串有 `Timezone` 時以它為準）不是 UTC。那段期間寫入的值都提早了該時區的時差，0.9.2 會照實際
時刻讀出，看起來就差了那個時差。

查舊時區時，用 API 的資料庫帳號連到 API 的資料庫，在沒有設定 `PGTZ` 的 psql 裡跑
`SHOW timezone;`：`ALTER DATABASE` 或 `ALTER ROLE` 設的 `timezone` 會讓它和伺服器預設不同。
API 的連線字串設了 `Timezone`，或 API 主機設了 `PGTZ` 環境變數時，舊時區就是那個值。

先備份資料庫，再停下舊版 API，在啟動 0.9.2 之前跑一次下面的修正，把
`'Asia/Taipei'` 換成舊時區；表名前綴不是 `struo_` 時換成 `Database:TablePrefix` 的值。0.9.2
啟動後寫入的資料已經正確，修正只能跑一次，跑兩次或在新資料寫入後才跑都會再位移一次。fork 自己掛
`TimestampWithTimeZone` 的欄位照同樣寫法修正。

```sql
BEGIN;
UPDATE struo_user_sessions
   SET createdat = (createdat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC',
       expiresat = (expiresat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
UPDATE struo_site_settings
   SET updatedat = (updatedat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
UPDATE struo_media_folders
   SET createdat = (createdat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC',
       updatedat = (updatedat AT TIME ZONE 'Asia/Taipei') AT TIME ZONE 'UTC';
COMMIT;
```

psql 依序印出 `BEGIN`、三行 `UPDATE <列數>` 與 `COMMIT`。

API 主機時區與 PostgreSQL 時區不同的環境，`MediaFolder.CreatedAt` 已經累積的位移，這段修正救不
回來：資料庫裡沒有原本的值。SQL Server、MySQL、MariaDB 與 SQLite 不用處理。

## 0.9.1 — 2026-10-13

::: danger 破壞性變更
- Compose 預設把每個埠只綁在 `127.0.0.1`，只有跑這套服務的機器連得到 PostgreSQL、Redis、API、
  後台與 MinIO。從其他機器連進來的人要設定 `STRUO_BIND_ADDRESS=0.0.0.0`（寫進 `.env` 或設在
  shell），埠就會開在所有網路介面上；固定的開發密碼與沒有密碼的 Redis 也會對網路公開。
:::

### 修正

- `migrate:preview` 印出核心種子的 `INSERT` 時，bootstrap 管理員的密碼雜湊以 `<redacted>`
  取代，管理員 email 照常顯示。`migrate` 與 `MigrateOnStartup` 寫入的仍是真正的雜湊。

## 0.9.0 — 2026-10-12

::: danger 破壞性變更
- 資料表只由 FluentMigrator migration 建立；啟動時 CodeFirst 不再建表。還有 migration 沒套用時，API 拒絕啟動。
  把 `migrate`（`dotnet run --project src/Struo.Api -- migrate`，映像內是 `dotnet Struo.Api.dll migrate`）
  放進部署步驟，或設 `Database:MigrateOnStartup=true`。
- 集合、翻譯 sidecar 與 junction 的表要有 migration：每個都用
  `make:migration <名稱> --entity <型別> --output <目錄>` 產生，再用 `migrate` 套用。
- v0.8.x 建出來的既有資料庫，啟動、`migrate` 與 `MigrateOnStartup` 都會拒絕並要求 `migrate:baseline`。
  先替 fork 自己已存在的表產生 migration，再跑一次 `migrate:baseline`，之後才用 `migrate`。
  完整步驟見[第 24 章](24-upgrading-from-0-8.md)。
- `Database:MigrationsPath`、`Database:AutoSyncSchema` 與 `db/migrations/` 移除，`.sql` 腳本不再執行；
  設定鍵還在時啟動會記警告。之後的結構變更寫成繼承 `StruoMigration` 的 FluentMigrator migration。
  `{prefix}schema_migrations` 表不再使用，版本記在 `{prefix}schema_versions`。
- 種子資料改由核心的種子 migration 寫入，在 `migrate` 或 `MigrateOnStartup` 期間執行，讀
  `Auth:BootstrapAdmin:*`、`Localization` 與 `Rbac:PublicReadCollections`；跑 `migrate` 的環境要有這些設定。
- 移除的公開型別：`DatabaseInitializer`、`MigrationRunner`、`SchemaMigration`、`SchemaGuard`
  （連同 `TranslationSidecarDescriptor`）、
  `DataSeeder`、`LanguageSeeder`、`AdminUserSeeder`、`RbacSeeder`（後三者只留在測試專案當輔助類別）。
  呼叫它們的 fork 程式碼要移除。
- Development 環境啟動時以 `SchemaChecker` 比對資料庫與 entity，任何錯誤都中止啟動（取代 `SchemaGuard`，
  檢查範圍更廣）。用 `migrate:check` 看明細。
- Compose 服務不再有固定的 `container_name`（`struo-postgres` 變成 `<專案名>-postgres-1`）；
  用 `docker exec struo-postgres` 的腳本要改。具名 volume 名稱不變，資料照舊。
- 從 v0.8.x 升上來的資料庫還要做各後端的後續處理（SQL Server 的 access token 索引與
  `varchar` → `nvarchar`；MySQL／MariaDB 的 `datetime(6)` 與 utf8mb4），見第 24 章。
:::

### 新增

- `Struo.Api` 的 migration 指令（第一個引數）：`migrate`、`migrate:status`、`migrate:preview`、
  `migrate:baseline`、`migrate:check`、`make:migration`。結束碼：0 成功、1 失敗、2 用法錯誤。
- 設定鍵 `Database:MigrateOnStartup`（預設 `false`）與 `Database:MigrationLockTimeoutSeconds`
  （預設 60，範圍 1–3600）。
- `migrate` 會取得 advisory lock，並行執行時每支 migration 只套用一次：PostgreSQL、MySQL／MariaDB
  與 SQL Server 有鎖；SQLite 與 Oracle 沒有，Oracle 會記警告。
- `StruoMigration` 基底類別：`Db`、`FrameworkTable(...)`、`CreateTranslationUniqueIndex(...)`，以及
  欄位輔助方法 `AsJson(Db)`、`AsLongText(Db)`、`AsTimestamp(Db)`、`AsShape(shape, Db)`。
- `SchemaChecker.Check(db, dbType, entityTypes)`：fork 的測試可斷言 `HasErrors` 為 false。
- `ColumnShape.Timestamp`。
- 核心 migration `202610080000_CreateCoreSchema` 與 `202610080001_SeedCoreData`；範例 Blog 附
  `202610080100_CreateBlogSchema`。
- `docker compose up -d --build` 起整套環境：postgres、redis、`api`（埠 8081）與 `admin`（埠 8080）；
  變數 `STRUO_ADMIN_PORT`、`STRUO_API_PORT`、`STRUO_ADMIN_EMAIL`、`STRUO_ADMIN_PASSWORD`。
- 新依賴 `FluentMigrator.Runner.*` 8.0.1（Core、Postgres、SqlServer、MySql、SQLite、Oracle）。
- 測試連線設定鍵 `Testing:SqlServerConnection`、`Testing:MySqlConnection`、
  `Testing:MariaDbConnection`，只認 `STRUO_TEST_SQLSERVER_CONNECTION`、
  `STRUO_TEST_MYSQL_CONNECTION`、`STRUO_TEST_MARIADB_CONNECTION` 覆寫；`STRUO_TEST_BACKEND`
  把整套測試搬到實際後端。見第 22 章。
- `migrate` 與 `MigrateOnStartup` 不把執行的 SQL 寫進記錄，因為種子的 `INSERT` 含預設管理員的
  密碼雜湊。`migrate:preview` 會印出這段 SQL，輸出要當機密處理。

### 變更

- SQL Server 2025、MySQL 8.4 與 MariaDB 11.8 列為已驗證的後端，與 PostgreSQL 並列；Oracle 仍未驗證。
  版本表見 `AGENTS.md` 的「Verification」。
- SQL Server 的字串欄位用 `nvarchar`；`migrate:check` 對 `varchar`／`char`／`text` 欄位發出
  `NonUnicodeString` 警告。
- MySQL／MariaDB 上沒有指定形狀的 `DateTime` 是 `datetime(6)`（微秒）；其他後端不變。
- API 映像不再含 `db/migrations`。CI 的 docker job 在 smoke test 前先跑 `migrate`，並加測 Compose 環境。
- `docker compose up -d` 會一併建置並啟動 API 與後台。開發時只要依賴服務，改用
  `docker compose up -d postgres redis`；要加 MinIO，用
  `docker compose --profile s3 up -d postgres redis minio createbuckets`。
- 後端套件升級：`AWSSDK.S3` 4.0.104.1、`NetVips.Native` 8.18.7、`Scalar.AspNetCore` 2.17.13、
  `coverlet.collector` 10.1.0。
- 前端套件升級：11 個 `@tiptap/*` 套件 3.31.4、`@lucide/vue` 1.49.0、`@tanstack/vue-table` 9.2.6、
  `vue-i18n` 11.4.13、`vite` 8.3.2、`@types/node` 26.6.4、`jsdom` 30.1.2、`vue-tsc` 3.3.12。

### 修正

- SQL Server：沒有 access token 的使用者可以並存；`accesstoken` 索引改為非唯一（舊的唯一索引只容許
  一筆 NULL）。只影響新建的資料庫，升級的資料庫見第 24 章。
- SQL Server：整數欄位的 `aggregate[avg]` 不再用整數除法。
- SQL Server：`int` 欄位的 `aggregate[sum]` 超過 2^31 不再溢位。
- SQL Server：取消的請求以取消例外呈現，不再是 `SqlSugarException`。
- MySQL／MariaDB：一秒內的兩次寫入維持順序（微秒時間戳）。只影響新建的資料庫。
- 檔名超過 255 個 UTF-16 編碼單位的上傳不再失敗：檔名截到欄位寬度並保留副檔名；檔名與標題的截斷都不會切開代理對。
- 同一個行程使用多個資料庫後端時，欄位型別與資料列繫結不再互相混用（`ProviderScopedReflectionCache`）。

## 0.8.2 — 2026-10-01

### 變更

- 後端套件升級：`AWSSDK.S3` 4.0.103.4、`HotChocolate.AspNetCore` 16.6.7、`Scalar.AspNetCore` 2.17.10。
- 前端套件升級：`@lucide/vue` 1.48.0、`vite` 8.3.1、`@types/node` 26.6.3。

## 0.8.1 — 2026-10-01

### 修正

- 後台側欄的版號改為讀取 `frontend/package.json` 的 `version`（建置時由 Vite 注入），不再是寫死的字串。

## 0.8.0 — 2026-10-01

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
- 後台 SPA 的介面語言 API 改了：`UiLocale` 型別搬到 `@/locales`；`DEFAULT_UI_LOCALE` 移除；
  `resolveInitialUiLocale(saved, enabled, fallback)` 改為純函式；`uiLocaleStore.set()` 回傳布林值並拒絕
  未啟用的語系；掛載前必須先呼叫 `uiLocaleStore.configure()`。fork 若改過這幾個檔案要跟著調整。
- i18n 的 fallback 語系改為 `AdminUi:DefaultLocale`（出貨為 `zh-TW`），不再固定是 `en`。
- 既有的 `struo_languages` 表若已違反規則（例如零個或兩個預設、`en` 與 `EN` 並存），任何讓它維持違規
  的寫入都會被拒絕；做一次修正寫入即可。
:::

### 新增

- `Database:TablePrefix` 設定鍵（環境變數 `Database__TablePrefix`），啟動時驗證格式。
- `Localization` 設定段：`struo_languages` 表第一次建立時的種子來源（`Languages`、`DefaultLanguage`），啟動時驗證。
- `AdminUi` 設定段：後台提供的介面語言與預設（`Locales`、`DefaultLocale`），經 `GET /api/config` 的 `uiLocales`、`uiDefaultLocale` 送給前端；只啟用一個語言時切換器不出現。
- `language` 集合的寫入規則：`code` 不分大小寫唯一、至少一列啟用、啟用列中恰好一列為預設；違反回 400。

## 0.7.0 — 2026-09-23

專案轉為開源時的現狀。版本紀錄從這一版開始。
