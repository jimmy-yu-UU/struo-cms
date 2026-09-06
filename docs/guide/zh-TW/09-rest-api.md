# 9. REST API

每一個框架 controller 都放在 `src/Struo.Api/Controllers/*.cs` 底下——總共十個:`ItemsController`、
`FilesController`、`UsersController`、`RolesController`、`LanguagesController`、`SettingsController`、
`SchemaController`、`AuthController`、`ConfigController`、`PingController`——再加上直接掛載在
`Program.cs` 中的兩個健康檢查端點 (沒有對應的 controller 類別)。本章記錄每一個 controller 所公開的
連線協定內容:每一個回應所包裹的信封、穩定的錯誤代碼目錄、驗證與 CSRF、樂觀並行控制，以及依
controller 分組的每一個端點。查詢語意——`ItemsController` 的 `GET`/`POST query` action 與 GraphQL
共用的 `filter`/`sort`/分頁/`fields`/`deep`/`deleted`/`locale` 介面——是第 8 章的主題;本章只涵蓋
端點本身。

## 回應信封

每一個 JSON 回應 (成功或錯誤) 都由 `EnvelopeResultFilter`
(`src/Struo.Api/Http/EnvelopeResultFilter.cs`) 包裹——這是一個註冊在 MVC 管線上的
`IAlwaysRunResultFilter` (`Program.cs`)。一個成功回應是 `{ "success": true, "data": ... }`，只有在
清單結果有分頁時才會多帶一個選擇性的 `meta` 物件 (`Envelope.cs` 的 `SuccessEnvelope.Meta` 為 `null`
時會整個從 JSON 中省略——絕不會以 `null` 的形式輸出):

```
$ curl -s http://localhost:5221/api/ping
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-07-29T07:23:58.4897657Z"}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?sort=fileName&limit=2"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}, ...],"meta":{"total":3,"limit":2,"offset":0}}
```

`meta` 還會多兩個選擇性鍵，`facets`（分面計數）與 `aggregate`（彙總），但只有請求有要求時才會出現
(`facets=`/`aggregate[<op>]=`，第 8 章)——兩者各自是 `MetaInfo`
(`src/Struo.Api/Http/Envelope.cs`) 上獨立的 `[JsonIgnore(Condition = WhenWritingNull)]`
屬性，所以一個兩者都沒指名的請求兩個鍵都省略，只指名其中一個的請求就省略另一個——與 `meta`
自身在非分頁回應中省略的方式完全相同:

```
"meta": {
  "total": 42, "limit": 25, "offset": 0,
  "facets": {
    "status": [{"value": "published", "count": 30}, {"value": "draft", "count": 12}],
    "category.name": [{"value": "Tech", "count": 25}, {"value": null, "count": 3}]
  },
  "aggregate": {"sum": {"price": 1234.5}, "max": {"price": 99, "rating": 5}, "count": {"publishedAt": 30}}
}
```

`facets` 是一個以請求的 facet 路徑字串為鍵的物件，順序與請求時相同;每個值都是一個
`{ "value", "count" }` 配對組成的陣列，已依第 8 章「排序與數值上限」排序並套用上限。
`aggregate` 是一個以 op 為鍵的物件，每個值又是一個以欄位名稱為鍵的物件——第 8 章「Facets 與
彙總」一節有兩者完整的語法、語意，以及實際輸出 (query string、JSON 信封、四個驗證錯誤範例)。

一個錯誤回應永遠是 `{ "success": false, "error": { "code": "...", "message": "...", "details": [...] } }`
——`details` (一份 `{ "field", "message" }` 配對的清單) 只有 `VALIDATION` 代碼才會出現，其餘情況下
會以與 `meta` 相同的方式省略:

```
$ curl -s http://localhost:5221/api/languages
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}
```

這個信封是由三個各自獨立的層所產生的，三者最終都會匯流到同一組 `Envelope.Success`/`Envelope.Error`
輔助方法 (`src/Struo.Api/Http/Envelope.cs`)，所以無論是哪一層做出回應，形狀都完全一致:

- **`EnvelopeResultFilter`** 會包裝 MVC action 傳回的任何內容:一個落在 2xx 範圍內的 `ObjectResult`
  會變成 `Envelope.Success`;一個沒有本文的裸 `NotFound()`/`StatusCode(4xx)` 會變成
  `Envelope.Error`，並帶有對應該狀態碼的預設訊息 (`400` → "Bad request."、`401` → "Authentication
  required."、`403` → "Forbidden."、`404` → "Resource not found."、`409` → "Conflict.");一個
  `204 NoContent` 結果會原樣直接通過 (完全不包裹，保持裸狀態——見下方的「狀態碼慣例」);一個本身
  已經是 `ErrorEnvelope`/`SuccessEnvelope` 的結果 (由 `ApiResults.Fail` 輔助方法或驗證工廠建構而成)
  則會被保持原樣，所以包裝這個動作具有冪等性。
- **`StruoExceptionHandler`** (`src/Struo.Api/Http/StruoExceptionHandler.cs`，透過
  `AddExceptionHandler<T>()` 註冊) 會攔截 action 本身未處理的任何例外，透過下方的 `DomainErrorMap`
  對應，並透過 `WriteAsJsonAsync` 直接寫入該信封——這是在 MVC 管線**之外**執行的，所以
  `EnvelopeResultFilter` 永遠不會看到它，而且當它收斂為 `INTERNAL_SERVER_ERROR` 時，該例外會先在
  伺服器端被記錄下來。
- **MVC 之前的 middleware** (`CsrfProtectionMiddleware` 的 403、cookie 驗證方案的
  `OnRedirectToLogin`/`OnRedirectToAccessDenied` 401/403、登入與變更密碼這兩個速率限制器的
  429——兩者都經過同一個 `OnRejected` callback) 是手動寫入相同的信封形狀，使用
  `EnvelopeJsonOptionsHolder` 中共用的 camelCase `JsonSerializerOptions`——因為這些
  元件的執行時機，早於 MVC 自身的 `JsonOptions` (同樣是 camelCase，設定於 `Program.cs`) 生效之前。

## 錯誤代碼

`ErrorCodes` (`src/Struo.Api/Http/ErrorCodes.cs`) 恰好宣告了以下這十四個穩定的 `code` 值。這正是
GraphQL 的 `StruoErrorFilter` 所使用的同一份目錄 (第 10 章)——舉例來說，一個
`PermissionDeniedException` 在兩種協定上都會對應到相同的代碼——所以一個已經處理過 GraphQL 錯誤的
客戶端，也能透過同一組字串辨識出 REST 的錯誤。

| 代碼 | 常見狀態碼 | 意義 |
|---|---|---|
| `UNAUTHORIZED` | 401 | 沒有憑證或憑證無效，或是遇到 `PermissionDeniedException` 但呼叫端根本尚未通過驗證 (`DomainErrorMap` 之所以選擇這個而不是 `FORBIDDEN`，正是因為呼叫端完全沒有通過驗證)。以*正確*密碼登入一個已停用的帳號，並**不會**落在這裡——那有自己專屬的代碼 (`ACCOUNT_INACTIVE`，見下文)，因為呼叫端已經證明了自己的憑證。 |
| `FORBIDDEN` | 403 | 已通過驗證但不被允許——一次逐集合 (collection) 的 RBAC 拒絕、一次未具超級管理員身分卻嘗試寫入 `AdminOnly` 集合、或是缺少 `X-Struo-CSRF` 標頭。 |
| `NOT_FOUND` | 404 | 未知的 id、未知的集合 (`CollectionNotFoundException`)，或任何裸 `NotFound()` 結果。 |
| `CONFLICT` | 409 | 一個 `RelationConflictException`——刪除一列仍被另一列在 `OnDelete.Restrict` 下參照的資料 (第 7 章)——或任何其他裸 `409` 結果。 |
| `VERSION_CONFLICT` | 409 | `ConcurrencyConflictException`——樂觀並行控制中，呼叫端送出的 `version` 已不再符合已儲存的資料列。之所以從單純的 `CONFLICT` 中拆分出來，是為了讓客戶端的「重新載入並重試」復原路徑，可以只依這一個代碼作為判斷依據 (見下方的「樂觀並行控制」)。 |
| `BAD_USER_INPUT` | 400 | 一個 `QueryException`——格式錯誤的查詢參數、未知的篩選欄位、一次失敗的應用層檢查 (弱密碼、重複/未知的角色權限集合、格式錯誤的 id、寫入時缺少必填欄位——見下文) 等等。 |
| `VALIDATION` | 400 | ASP.NET Core 自身的 model-binding/model-state 驗證失敗 (一個請求本文屬性在 action 尚未執行之前，就未通過 `[Required]`/資料註記驗證)——唯一會帶有 `details` 的代碼。 |
| `INTERNAL_SERVER_ERROR` | 500 | 任何 `DomainErrorMap` 無法辨識的例外。給客戶端看到的訊息永遠是遮蔽過的通用字串
`"An internal error occurred."`;真正的例外會在伺服器端被記錄下來，絕不會外洩到回應中。 |
| `TOO_MANY_REQUESTS` | 429 | 三個各自獨立的來源之一拒絕了這次請求，沒有一個經由 `DomainErrorMap`(沒有任何例外被擲出):`POST /api/auth/login` 的逐帳號節流器(以請求本文中的帳號為單位;第 3 章的 `RateLimiting:LoginAccount` 段落)、`POST /api/auth/login` 的逐 client IP 限流器(第 3 章的 `RateLimiting:Login` 段落)，或是 `PUT /api/users/{id}/password` 的限流器(以已驗證呼叫端的使用者 id 為單位;第 3 章的 `RateLimiting:Password` 段落)。逐 client IP 與逐使用者 id 這兩個限流器是直接從同一個共用的 `OnRejected` 回呼寫出的，會依實際拒絕的是哪一個政策，挑選對應的訊息文字(「login attempts」或「password change attempts」);逐帳號節流器則是直接從 `AuthController.Login` 寫出，使用完全相同的「login attempts」文字，因此這兩個作用在登入端點上的來源，共用完全相同的狀態碼、代碼與訊息。兩者之間唯一不同的是 `Retry-After`:逐 client IP 限流器的上限是 `RateLimiting:Login:WindowSeconds`(預設 `60`，而且這一層是否啟用都還兩說);逐帳號節流器的上限則是 `RateLimiting:LoginAccount:WindowSeconds`(預設 `900`),因此可能大上許多——見下方的即時範例。 |
| `PAYLOAD_TOO_LARGE` | 413 | 一次串流上傳，即使宣告的 `Content-Length` 通過了前置檢查，實際位元組數仍超過 `Struo:Files:MaxUploadBytes` (一次「說謊」或分塊上傳)。本章並未即時演練這個項目——要觸發它需要上傳超過預設 25 MB 上限的內容——但這個對應是真實的:`DomainErrorMap.StatusFor` → 413。 |
| `INVALID_CURRENT_PASSWORD` | 400 | `PUT /api/users/{id}/password` 的自助式分支:呼叫端送出的 `currentPassword` 缺漏或錯誤。刻意不是 `401`——呼叫端本來就持有一個有效的 session，而 SPA 的全域 401 處理器只要看到 `401` 就會清除 session，所以沿用 `UNAUTHORIZED` 會讓呼叫端因為一個單純的打字錯誤而被登出。 |
| `NO_LOCAL_PASSWORD` | 400 | 在一個完全透過外部 OIDC 建立的帳號上，嘗試自助式變更密碼——它儲存的雜湊值是空字串，因為它從來沒有本機密碼。 |
| `ACCOUNT_INACTIVE` | 401 | `POST /api/auth/login` 以*正確*密碼登入一個已停用的帳號。只有在雜湊驗證成功之後才會抵達這裡，所以揭露它並不會洩漏呼叫端尚未證明過的任何資訊——不同於把「密碼錯誤」與「沒有這個帳號」拆開，那兩者仍合併在上方的 `UNAUTHORIZED` 之下 (原因見第 12 章)。 |
| `SESSION_REVOCATION_FAILED` | 500 | `SessionRevocationFailedException`——變更密碼或刪除使用者已經成功提交，但後續撤銷 session 的步驟卻失敗了，因此該使用者原有的部分 session 可能仍然存活 (第 12 章)。`DomainErrorMap.StatusFor` 並沒有為這個代碼準備專屬分支，所以會落到預設的 500 狀態；即便如此，代碼與訊息本身仍是專屬的，而不是 `INTERNAL_SERVER_ERROR` 通常帶有的那個遮蔽用通用字串。 |

`DomainErrorMap` (`src/Struo.Api/Http/DomainErrorMap.cs`) 是例外對應到代碼的唯一來源，與 GraphQL
的錯誤過濾器逐字共用:

```csharp
PermissionDeniedException when !authenticated => (ErrorCodes.Unauthorized, exception.Message),
PermissionDeniedException => (ErrorCodes.Forbidden, exception.Message),
CollectionNotFoundException => (ErrorCodes.NotFound, exception.Message),
FileBlobNotFoundException => (ErrorCodes.NotFound, exception.Message),
ConcurrencyConflictException => (ErrorCodes.VersionConflict, exception.Message),
RelationConflictException => (ErrorCodes.Conflict, exception.Message),
QueryException => (ErrorCodes.BadUserInput, exception.Message),
PayloadTooLargeException => (ErrorCodes.PayloadTooLarge, exception.Message),
SessionRevocationFailedException => (ErrorCodes.SessionRevocationFailed, exception.Message),
_ => (ErrorCodes.Internal, "An internal error occurred."),
```

下方即時觸發的範例，涵蓋了以上除了 `PAYLOAD_TOO_LARGE`(已在上方自己的那一列中說明為何略過)、`INTERNAL_SERVER_ERROR`(在下文中說明為何略過)，以及另外三個代碼之外的每一個代碼——`INVALID_CURRENT_PASSWORD` 在下方的 Users 章節有自己的即時範例;`NO_LOCAL_PASSWORD` 與 `ACCOUNT_INACTIVE` 則本章完全沒有即時觸發過:

```
$ curl -s http://localhost:5221/api/languages
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}

$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" -b cookies.txt -d '{}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/nope"
{"success":false,"error":{"code":"NOT_FOUND","message":"Unknown collection 'nope'."}}

$ curl -s -X DELETE http://localhost:5221/api/items/mediaFolder/<guides-id> -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediaFolder/<guides-id>': referenced by 'file'."}}

$ curl -s -X PUT http://localhost:5221/api/items/file/<alpha-id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"status":"published","version":1}'
{"success":false,"error":{"code":"VERSION_CONFLICT","message":"The record was modified by someone else since you loaded it. Reload and try again."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Bbogus%5D%5B_eq%5D=x"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown field 'bogus' on collection 'file'."}}

$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"password":"whatever123"}'
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"Email","message":"The Email field is required."}]}}

$ for i in 1 2 3 4 5 6 7 8 9 10 11; do curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"nobody@example.com","password":"wrong"}'; done
401 401 401 401 401 401 401 401 401 401 429
$ curl -s -i -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"nobody@example.com","password":"wrong"}'
HTTP/1.1 429 Too Many Requests
Content-Type: application/json; charset=utf-8
Retry-After: 897
X-Content-Type-Options: nosniff
{"success":false,"error":{"code":"TOO_MANY_REQUESTS","message":"Too many login attempts. Please try again later."}}
```

這是逐帳號節流器(`RateLimiting:LoginAccount`，預設開啟，`PermitLimit` 為 `10`——見第 3 章)產生的
結果，不是逐 client IP 限流器:第十一次針對 `nobody@example.com` 的失敗才會觸發它，而其
`Retry-After` 反映的是觸發那一刻，`900` 秒視窗中還剩下多少(此處是 `897`——已經過了三秒)。逐
client IP 限流器(`RateLimiting:Login`)出貨即關閉，所以同一個 client IP 對**另一個**帳號的登入
失敗完全不會被擋下——分區鍵是帳號，不是 IP:

```
$ for i in 1 2 3 4 5 6; do curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"someone-else@example.com","password":"wrong"}'; done
401 401 401 401 401 401
```

(`INTERNAL_SERVER_ERROR` 刻意沒有用刻意構造的請求來演示——要引發一個，代表要找出一個真正未被處理的
伺服器端 bug，這不是本章該做的事;上面的對應關係，以及遮蔽/記錄的行為，都是直接讀自
`StruoExceptionHandler`/`DomainErrorMap` 原始碼。)

## 狀態碼慣例

| 狀態 | 適用時機 |
|---|---|
| `200 OK` | 一次讀取，或是結果會有意義地回傳在本文中的一次寫入 (建立/更新/取得/列出全部都回傳 `200`——注意下方 `Created` 的 `201` 是唯一的例外)。 |
| `201 Created` | `POST /api/items/{collection}`、`POST /api/users`，以及 `POST /api/files`——回應本文帶有建立好的資料列，而這三者都會送出一個相對的 `Location` 標頭，指向該資料列的正規 `GET` 路由:分別是 `/api/items/{collection}/{id}`、`/api/items/user/{id}` (刻意指向通用的 items 路由，而不是一個專屬的 users 路由——資料列實際上就是從那裡讀回來的)，以及 `/api/files/{id}`。`ItemsController.Create`、`UsersController.Create` 與 `FilesController.Upload` 全部都呼叫 `Created(uri, value)` (一個 `CreatedResult`，它自身的 `ExecuteResultAsync` 通常會寫出 `Location`)。`EnvelopeResultFilter` 在其通用的 `ObjectResult` 分支之前，特別處理了 `CreatedResult`，把它重建成一個**新的** `CreatedResult`，帶有包裝後的本文——所以當 ASP.NET Core 格式化回應時，寫入 `Location` 的行為仍然會執行，不同於通用分支會把它壓平成一個單純的 `ObjectResult` 而遺失標頭。`CreatedAtActionResult`/`CreatedAtRouteResult` 則刻意**不**被這個重建機制涵蓋:它們的 `Location` 是在格式化時，由 `IUrlHelper` 計算出來的——那已經是這個 filter 執行完之後的事了，所以無法在這裡重建。這個樣板中沒有任何地方使用這兩種結果型別;若一個 fork 需要用到其中之一，應該改回傳 `Created(uri, value)`。 |
| `204 No Content` | 每一次刪除 (回收桶或清除)、還原，以及登出——完全沒有本文;`EnvelopeResultFilter` 會明確地讓一個 `NoContentResult` 保持裸狀態，不會把它包進信封裡。 |
| `400 Bad Request` | `BAD_USER_INPUT` 或 `VALIDATION` (見錯誤代碼表)。 |
| `401 Unauthorized` | `UNAUTHORIZED`。 |
| `403 Forbidden` | `FORBIDDEN`。 |
| `404 Not Found` | `NOT_FOUND`。 |
| `409 Conflict` | `CONFLICT` 或 `VERSION_CONFLICT`。 |
| `413 Payload Too Large` | `PAYLOAD_TOO_LARGE`。 |
| `429 Too Many Requests` | `TOO_MANY_REQUESTS` (登入端點的兩道防線之一，或改密碼限制器——見上文)。 |
| `500 Internal Server Error` | `INTERNAL_SERVER_ERROR`。 |

```
$ curl -s -i -X DELETE http://localhost:5221/api/files/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
```

## 驗證錯誤 `details` 的形狀

一個未通過 ASP.NET Core 自身 model-binding/資料註記驗證的請求本文 (不是一個網域層級的
`QueryException`)，根本不會抵達 controller action——設定在 `Program.cs` 中的
`InvalidModelStateResponseFactory` 會攔截它，並回傳 `400`，代碼為 `VALIDATION`，`details` 則由
`ModelState` 填入，每一個無效屬性各一筆 `{ "field", "message" }` 項目 (當 ASP.NET Core 本身沒有提供
訊息時，訊息會退回 `"Invalid value."`):

```
$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"password":"whatever123"}'
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"Email","message":"The Email field is required."}]}}
```

這和一個應用層級的 `QueryException` 不同，後者會以 `BAD_USER_INPUT` 的形式呈現，完全沒有 `details`
陣列——訊息本身就是全部的內容:

```
$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"email":"nobody@example.com","password":"short"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Password must be at least 8 characters."}}
```

**一個 `Required` 的 `[CmsField]`，必須在每一次寫入時重新送出，包括一次部分更新在內。** 這一點很
容易被忽略:`ItemService.UpdateCoreAsync` 「只疊加客戶端有送出的欄位」這個合併邏輯，只決定了一個
送出的欄位是否會*覆蓋*既有的資料列——但 `ItemDeserializer.Deserialize` (由建立與更新共用，
`src/Struo.Application/Query/Write/ItemDeserializer.cs`) 會在那次合併執行之前，就對照**剛剛解析出
的請求本文**，驗證每一個 `Required` 欄位，無論那個欄位是否屬於這次呼叫原本的意圖。從一個原本合法
的部分 `PUT` 中省略一個 `Required` 欄位，會以 `BAD_USER_INPUT` 失敗，而不是靜默保留既有值:

```
$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"description":"partial update, no name"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'name' is required."}}

$ curl -s -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"name":"Demo9","description":"partial update, name resent"}'
{"success":true,"data":{"id":"...","version":1,"name":"Demo9","isSuperAdmin":false,"description":"partial update, name resent", ...}}
```

可翻譯的 `Required` 欄位不受此限——它們是在翻譯附屬資料表的同步過程中，逐 locale 各自驗證的，而不是
在父層的反序列化階段 (見 `ItemWriteSideSync` 與第 6 章)。

## 驗證:cookie 或 bearer

有兩種機制被接受，兩者都註冊在 `AuthWiring.AddStruoAuth`
(`src/Struo.Api/Auth/AuthWiring.cs`) 中:

- **Cookie** (`AuthSchemes.Cookie`，名為 `struo.session` 的 session cookie)——由
  `POST /api/auth/login` 設定，是一張以 Redis (或記憶體內備援;第 3 章) 為後盾、8 小時滑動有效期的
  票證。
- **Bearer** (`AuthSchemes.Bearer`，一個由 `POST /api/users/{id}/access-token` 取得的逐使用者存取
  權杖，以 `Authorization: Bearer <token>` 送出)——由 `BearerTokenAuthenticationHandler`
  (`src/Struo.Api/Auth/BearerTokenAuthenticationHandler.cs`) 對照雜湊過的權杖儲存區做驗證。

這兩者本身都不是 ASP.NET Core 的*預設*驗證機制——真正的預設是第三個轉發用機制:
`AuthSchemes.Adaptive` (`"Adaptive"`，透過 `AuthWiring.AddStruoAuth` 中的
`AddAuthentication(AuthSchemes.Adaptive)` 加上 `AddPolicyScheme` 註冊)，只要請求的
`Authorization` 標頭以 `Bearer ` 開頭，就轉發給 `Bearer`，否則轉發給 `Cookie`。因為 `Adaptive`
是預設機制，ASP.NET Core 會以這種方式驗證**每一個請求**——無論該 action 是否帶有 `[Authorize]`
attribute——比對出實際符合的那個真正機制。這個選擇器只看標頭本身，從不會參考 cookie:一個同時帶有
session cookie 與 `Authorization: Bearer` 標頭的呼叫端，在每一個不帶 `[Authorize]` 的 action 上，
都會被解析成 bearer 身分，所以一個失效或已撤銷的權杖，會讓該呼叫端降級成 `public` 底線，而不是回退
到 cookie 自己的授權——這是刻意設計成 fail-closed，不是一個 bug (第 12 章涵蓋 `public` 底線)。

帶有 `[Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)]` (一份以逗號連接的機制清單:
`"Cookies,Bearer"`) 的 action——`ItemsController`/`FilesController` 上的每一個寫入，以及
`UsersController`/`RolesController`/`LanguagesController`/`SettingsController`/`SchemaController`
上的每一個 action，再加上 `AuthController` 的 `logout`/`me`——額外明確指名了**兩種**機制:ASP.NET
Core 的 `PolicyEvaluator` 會依序對每一個指名的機制呼叫 `AuthenticateAsync`，並把成功驗證出來的
principal 合併起來，這在驅動 `[Authorize]` 自身挑戰/拒絕邏輯之外，同時也是在做驗證本身——所以在
這些地方，無論 `Adaptive` 原本預設會轉發給哪個機制，一個 bearer 權杖向來都會被探測到，效果與一個
cookie 完全相同:

```
$ curl -s -i -X PUT http://localhost:5221/api/items/file/<id> -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"status":"published"}'
HTTP/1.1 200 OK
{"success":true,"data":{"id":"...","version":2, ...}}
```

**`ItemsController` 的讀取 action (`/api/items/{collection}` 與 `/api/items/{collection}/{id}`
上的 `GET`/`POST query`，再加上 `.../revisions` 系列 action) 仍然完全不帶任何 `[Authorize]`
attribute**——這是刻意的，因為一次讀取只需要一般的逐集合 `CanRead` RBAC 檢查，而不是一個一律要求
驗證的門檻 (第 8 章)，`Adaptive` 並不會改變這一點。真正改變的是:一個純 bearer 呼叫端命中這些
action 時，會解析成哪個身分。`Adaptive` 即使在一個完全沒有指名任何機制的 action 上，仍然會驗證
`Bearer` 標頭，所以呼叫端會被解析為**它自己**——連同它自己的角色授權 (與 `public` 底線聯集，第
12 章)——與一個以 cookie 驗證的呼叫端完全相同，而不是被當成匿名者。一個真正匿名的請求 (完全沒有
任何憑證) 仍然只能得到 `public` 自身授權所允許的內容，這一點沒有改變。因此同一個 bearer 權杖，
無論命中 `ItemsController` 上的一次單純讀取，還是另一個 controller 上帶 `[Authorize]` 的手足
端點，現在的行為都完全相同:

```
$ curl -s -H "Authorization: Bearer <token>" "http://localhost:5221/api/languages"
{"success":true,"data":[{"code":"en","name":"English","isDefault":true}, ...]}
```

`LanguagesController` 與 `SchemaController` 值得注意的地方，是它們只要求*已通過驗證*，而不需要
一個逐集合的 `CanRead` 授權——任何已登入的使用者 (無論 cookie 或 bearer) 都能讀取完整的語言清單或
完整的 schema，無論其 `language`/逐集合 RBAC 授權為何 (原因請見各個 controller 上的原始碼註解)。

## CSRF:`X-Struo-CSRF` 標頭

`CsrfProtectionMiddleware` (`src/Struo.Api/Auth/CsrfProtectionMiddleware.cs`) 會要求每一個非安全
HTTP 方法 (`POST`/`PUT`/`DELETE`/……——`GET`/`HEAD`/`OPTIONS`/`TRACE` 則豁免) 都帶有 `X-Struo-CSRF`
標頭——只檢查它是否**存在**，其值從不會被檢視——但**只有在請求依附於 session cookie 上時才會如此**:
一個以 `Bearer` 驗證的請求可豁免 (沒有瀏覽器環境憑證可供偽造)，完全沒有帶 session cookie 的請求也
一樣 (還沒有東西可以被攻擊，例如尚未建立任何 session 之前的 `POST /api/auth/login`)。這正是 OWASP
的「自訂請求標頭」CSRF 防禦法:一個跨站頁面無法在一次帶有憑證的請求上附加自訂標頭，除非目標的 CORS
政策本身就已經允許該來源，而 StruoCMS 的 CORS 一律採用白名單制且預設關閉。

```
$ curl -s -X POST http://localhost:5221/api/items/file/query -H "Content-Type: application/json" -b cookies.txt -d '{}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Missing required \u0027X-Struo-CSRF\u0027 header."}}
```

這個 middleware 會把關**每一個**非安全的請求路徑，不只是 `/api/*`——`POST /graphql` 同樣是一個非
安全方法，所以一個以 cookie 驗證的 GraphQL 請求 (不論是 query 還是 mutation) 也需要相同的標頭 (第
10 章會在 GraphQL 情境下涵蓋這一點)。

## 樂觀並行控制 (`version` 往返)

每一個集合的資料列都帶有一個 `version` 整數 (來自 `AuditableEntity`)。一次 `GET`/清單回應永遠會
包含它;一次 `UPDATE` 則可以在請求本文中把它回傳。`ItemService.UpdateCoreAsync` 會把客戶端的
`version` 疊加到即將儲存的 entity 上，而儲存庫的比較並交換更新，會以 `WHERE version = <回傳的值>`
執行:零筆受影響資料列會擲出 `ConcurrencyConflictException` → `VERSION_CONFLICT` / `409`。完全省略
`version` 會退回使用剛剛載入的那一份——沒有任何保護，但對不追蹤它的呼叫端保持向後相容:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>"
{"success":true,"data":{"id":"...","version":2,"fileName":"alpha-report.txt", ...}}

$ curl -s -X PUT http://localhost:5221/api/items/file/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"status":"published","version":1}'
{"success":false,"error":{"code":"VERSION_CONFLICT","message":"The record was modified by someone else since you loaded it. Reload and try again."}}
```

## `POST`/`PUT` 請求本文能設定什麼

`ItemDeserializer.Deserialize` (`src/Struo.Application/Query/Write/ItemDeserializer.cs`) 由建立與更新
共用，它會依允許清單來繫結請求本文，而不是繫結到整個 CLR entity 型別:只有已宣告、可寫入的
`[CmsField]`，以及已宣告的 `ManyToOne` 關聯外鍵 (例如 `categoryId`)，會被繫結到父層 entity 上。
`translations` 附掛承載與多對多關聯鍵 (例如 `tags`) 則被排除在這次繫結之外——`ItemDeserializer` 在
反序列化父層 entity 之前就把它們剝除——但它們依然會生效:`ItemService` 會直接從原始請求本文讀取
這兩者，並在與父層資料列相同的交易中，透過
`ItemWriteSideSync.SyncTranslationsAsync`/`SyncM2MAsync`
(`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`) 個別套用它們。其餘每一個頂層鍵——包括
`id`、`version`，以及軟刪除/稽核欄位 (`deletedAt`、`deletedBy`、`createdAt`、`createdBy`、
`updatedAt`、`updatedBy`)——都會被靜默捨棄，而不是被拒絕:寫入仍會成功，只是那個鍵不會生效。`POST`
與 `PUT` 共用同一份允許清單，因此建立請求無法設定客戶端指定的 id，也無法竄改樂觀並行控制版本號，
就跟更新請求做不到一樣。

### Many-to-many 寫入形狀:純 id 與 junction payload

一個 many-to-many 關聯的陣列 (`"tags": [...]`) 接受**混合清單**:陣列中每個元素，可以是一個純
id，也可以是 (對於 junction 帶有 payload 的關聯，第 7 章) 一個帶著 `id` 加上 payload 欄位值的
物件。`ItemWriteSideSync.SyncM2MAsync`
(`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`) 在同步之前，會把陣列合併成一份依 id
索引、有順序的連結清單，遵循以下三條規則:

- **純 id** 只代表成員關係——目標會被連結 (或維持連結)，其 junction 資料列上任何既有的
  payload 都不受影響。
- **物件** (`{ "id": ..., "<payloadField>": ... }`) 代表成員關係，*加上*把它點名的 payload
  欄位合併進 junction 資料列——物件沒提到的欄位維持原值。當同一個 id 在陣列中出現超過一次時，
  物件永遠勝過純 id (不論物件出現在前或在後)，而在兩個物件之間，後出現的那個勝出——它是
  **取代**而非合併前一個物件的 payload。
- **陣列中第一次出現的順序**就是排序順序 (若該關聯宣告了 `SortField`)，無論最終是哪個重複的
  id 勝出。

payload 物件裡不認得的鍵一律被忽略，若呼叫端在 payload 部分放進 junction 自己的外鍵、排序或
`id` 相關的結構性鍵，同樣會被忽略——只有該關聯宣告過的 payload 欄位 (第 7 章) 才會被真正合併
進去。帶 payload 的元素，還額外要求呼叫端持有該 junction collection 本身的**寫入**授權 (若
該 junction 是 `AdminOnly`，還要求超級管理員身分)——這項檢查會在解析任何 payload 之前先執行，
所以缺乏該授權的呼叫端得到的是 `403 FORBIDDEN` (`"Write to '{junction}' not permitted."`，或
`"Writes to '{junction}' require a super-admin."`)，而不是關於 payload 自身欄位的驗證錯誤。
純 id 則只需要父集合原本的寫入授權，跟這個功能出現之前一樣。這個授權關卡對
`.../revisions/{n}/revert` (第 13 章) 同樣適用:只要被還原的那筆快照帶有 junction payload，
revert 就會像一般寫入一樣重新套用 `[{id, ...payload}]` 元素，因此除了父集合之外，也需要該
junction collection 本身的寫入授權。針對範例 `article`
(`Article.Tags`，第 16 章——`Note` 是 `ArticleTag` 的 payload 欄位) 的示意混合陣列請求本文:

```json
{
  "tags": [
    "<tag-id-1>",
    { "id": "<tag-id-2>", "note": "editor pick" }
  ]
}
```

`<tag-id-1>` 會被連結，其既有 payload 不受影響 (若是新連結，則維持預設值);`<tag-id-2>` 會被
連結，且其 junction 資料列的 `note` 欄位會被設為 `"editor pick"`。

junction 資料列本身，現在能撐過一次重新同步:`ManyToManySync`
(`src/Struo.Infrastructure/Query/ManyToManySync.cs`) 會把傳入的連結清單，與該父項目前已儲存的
資料做差異比對——刪除不再存在的目標的資料列、插入新增目標的資料列、就地更新維持不變的目標的
資料列——而不是這個同步機制較早版本那種「全部刪除、全部重新插入」的做法;維持連結狀態的目標，
其 junction 資料列自身的主鍵因此保持穩定，這對任何以 id 參照 junction 資料列的東西都很重要
(若既有資料留下同一組 `(parent, target)` 的重複資料列——來自這個行為存在之前寫入的資料——下次
該父項同步時，會被收斂成主鍵最小的那一列，並記一筆警告)。

## 端點參考，依 controller 分類

下方的「必要權限」永遠是指 `ItemService`/`FileAccessPolicy` 所檢查的逐集合 RBAC 授權
(`CanRead`/`CanWrite`/`CanDelete`——第 12 章涵蓋 RBAC 自身的管理後台介面)，**再加上**任何有註明的
`[Authorize]` attribute (如果有的話)。寫入一個 `AdminOnly` 集合 (`permission`、`role`、`user`、
`userRole`) 額外要求呼叫端必須是超級管理員，不論是否已有委派的逐集合授權
(`ItemService.RequireSuperAdminForAdminOnly`)——單純對這四者之一擁有逐集合寫入授權是不夠的:

```
$ curl -s -i -X PUT http://localhost:5221/api/items/role/<id> -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"description":"hacked"}'
{"success":false,"error":{"code":"FORBIDDEN","message":"Writes to 'role' require a super-admin."}}
```

**路由限制會在這一切之前就先失敗。** `ItemsController` 的通用 `{id}` 區段是一個單純、不受限制的
路由參數 (一個集合的 id 欄位不見得永遠是 `Guid`)，但每一個 `FilesController`/`UsersController`/
`RolesController` 的 id 路由都宣告為 `{id:guid}`，而 `ItemsController` 的兩個版本紀錄路由則是
`{id}/revisions/{revisionNumber:long}`——一個無法通過該限制的值，根本不會抵達 action:ASP.NET Core
的路由機制根本不會比對成功，所以回應是一個**裸、空本文的 `404`** (沒有信封，`Content-Length: 0`)，
而不是一個格式正確但確實未知的 id 所產生的網域 `{"success":false,"error":{"code":"NOT_FOUND",...}}`
形狀:

```
$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/not-a-guid"
HTTP/1.1 404 Not Found
Content-Length: 0

$ curl -s -i -b cookies.txt "http://localhost:5221/api/items/file/<id>/revisions/not-a-number"
HTTP/1.1 404 Not Found
Content-Length: 0
```

### Items (`ItemsController`，`api/items/{collection}`)

對每一個 `[CmsCollection]` 的通用 CRUD 介面——在這個 host (即執行本章範例的 `Struo.Api` 執行個體)
上是 `language`、`permission`、`role`、`user`、`userRole`、`file`、`mediaFolder` (沒有任何範例被
選用啟用)。完整的
`filter`/`sort`/`limit`/`offset`/`fields`/`deep`/`search`/`locale`/`deleted`/`facets`/`aggregate`
查詢介面請見第 8 章。

| 方法與路徑 | 查詢參數 | 本文 | 回應 | 驗證 | 權限 |
|---|---|---|---|---|---|
| `GET /api/items/{collection}` | `filter[...]`、`sort`、`limit`、`offset`、`fields`、`deep`、`search`、`locale`、`deleted`、`facets`、`aggregate[<op>]` | — | `200`，清單 + `meta` | 無 (不帶 `[Authorize]`;但若請求帶有 cookie 或 bearer 憑證，`Adaptive` 仍會驗證它——見上文) | `CanRead` |
| `POST /api/items/{collection}/query` | `locale`、`deleted` (即使在這裡也是從 URL 讀取) | JSON 信封 (第 8 章)，包含 `facets`/`aggregate` | `200`，清單 + `meta` | 無 (同上) | `CanRead` |
| `GET /api/items/{collection}/{id}` | `deep`、`locale`、`deleted` | — | `200` 項目，或 `404` | 無 (同上) | `CanRead` (`deleted=only\|with` 額外需要 `CanDelete`) |
| `POST /api/items/{collection}` | — | 可寫入欄位組成的 JSON 物件 (經過允許清單過濾——見上方) | `201` 已建立的項目，`Location: /api/items/{collection}/{id}` (見上方的「狀態碼慣例」) | Cookie or Bearer | `CanWrite` (若為 `AdminOnly` 則另需超級管理員) |
| `PUT /api/items/{collection}/{id}` | — | JSON 物件，部分更新 (只有送出的鍵值會疊加——但請見上方 `Required` 欄位的但書) | `200` 已更新的項目，或 `404` | Cookie or Bearer | `CanWrite` (若為 `AdminOnly` 則另需超級管理員) |
| `DELETE /api/items/{collection}/{id}` | `purge` (bool，預設 `false`) | — | `204`，或 `404` | Cookie or Bearer | `CanDelete` (若為 `AdminOnly` 則另需超級管理員) |
| `POST /api/items/{collection}/{id}/restore` | — | — | `200` 已還原的項目，或 `404` | Cookie or Bearer | `CanDelete` (若為 `AdminOnly` 則另需超級管理員) |
| `GET /api/items/{collection}/{id}/revisions` | — | — | `200`，`{ revisionNumber, operation, createdAt, createdBy, sourceRevisionNumber }` 的陣列 (若該集合沒有 `Revisions=true`則為 `[]`) | 無 (同上) | `CanRead` |
| `GET /api/items/{collection}/{id}/revisions/{n}` | — | — | `200`，上方那筆條目再加上 `snapshot` (隱藏欄位已遮蔽)，或 `404` | 無 (同上) | `CanRead` |
| `POST /api/items/{collection}/{id}/revisions/{n}/revert` | — | — | `200` 還原後的項目 (以更新的形式重新套用該快照，並記錄成一筆新的 `"revert"` 版本紀錄)，或 `404` | Cookie or Bearer | `CanWrite` (若為 `AdminOnly` 則另需超級管理員) |

`GET /api/items/{collection}/{id}` 會透過與清單/查詢 action 完全相同的 `DeletedMode`/
`DeletedAccessGuard` 路徑解析 `?deleted=` (第 8 章)——無論抵達的是這三個 action 中的哪一個，一個
無效的值都會以相同的方式被拒絕:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/e2269ae7-094d-448d-bcb9-b484418de9b2?deleted=bogus"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Query parameter 'deleted' must be exclude|only|with."}}
```

七個即時上線的框架集合，沒有任何一個宣告了 `[CmsCollection(Revisions = true)]`，所以在這個 host
上，上面每一個版本紀錄/還原 action 雖然都可以即時觸及，但一律會落入「沒有版本紀錄」這個分支:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>/revisions"
{"success":true,"data":[]}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/items/file/<id>/revisions/1"
HTTP/1.1 404 Not Found
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
```

對一個可軟刪除的集合做 `DELETE` (七個之中只有 `file` 如此——它的 schema 中有 `softDelete: true`)，
預設會移入回收桶，並可透過 `?purge=true` 永久清除;其他每一個集合都完全沒有軟刪除層級，所以不論
查詢字串為何，`DELETE` 永遠是一次清除:

```
$ curl -s -i -X DELETE http://localhost:5221/api/items/file/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=only"
{"success":true,"data":[{"fileName":"...", ...}],"meta":{"total":1, ...}}
```

一次因另一列的 `OnDelete.Restrict` (第 7 章) 而被擋下的刪除，會呈現為單純的 `CONFLICT`，而不是
`VERSION_CONFLICT`:

```
$ curl -s -i -X DELETE http://localhost:5221/api/items/mediaFolder/<guides-id> -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediaFolder/<guides-id>': referenced by 'file'."}}
```

### Files (`FilesController`，`api/files`)

`file` 集合專屬的上傳/儲存/圖片轉換管線——寫入會經過 `FileService`/`IFileStorage`，而不是通用的
`ItemService`，所以這裡的 RBAC 是透過 `IFileAccessPolicy` 強制執行的。

| 方法與路徑 | 查詢參數 | 本文 | 回應 | 驗證 | 權限 |
|---|---|---|---|---|---|
| `POST /api/files` | — | `multipart/form-data`:`file` (必填)、`folderId` (選填) | `201`，`Location: /api/files/{id}`，`{ id, fileName, contentType, size, width, height, status, folderId }` | Cookie or Bearer | `IFileAccessPolicy.CanWrite()` |
| `GET /api/files/{id}` | — | — | `200` 中介資料，或 `404` (一個未發布的檔案，除非呼叫端具有讀取未發布內容的權限，否則同樣是 `404`) | 無 (不帶 `[Authorize]`;但若請求帶有 cookie 或 bearer 憑證，`Adaptive` 仍會驗證它——右側的權限檢查需要它) | 已發布的檔案不需要任何權限;一個未發布的檔案則需要**已驗證的身分，加上一個 `file` 寫入授權** (`IFileAccessPolicy.CanReadUnpublished`——第 11 章——不是讀取授權) |
| `GET /api/files/{id}/content` | `width`、`height`、`format`、`fit`、`quality` (圖片轉換，第 11 章) | — | `200` 位元組 (串流，或在 `Struo:Files:PresignedRedirect` 開啟時為 `302`)，或 `404` | 與上方的 `Get` 相同 | 與上方的 `Get` 相同 |
| `DELETE /api/files/{id}` | `purge` (bool，預設 `false`) | — | `204`，或 `404` | Cookie or Bearer | `CanDelete()` |
| `POST /api/files/{id}/restore` | — | — | `204`，或 `404` | Cookie or Bearer | `CanDelete()` |

```
$ curl -s -X POST http://localhost:5221/api/files -H "X-Struo-CSRF: 1" -b cookies.txt -F "file=@pixel.png;type=image/png"
{"success":true,"data":{"id":"...","fileName":"pixel.png","contentType":"image/png","size":70,"width":1,"height":1,"status":"published","folderId":null}}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=1&format=webp"
HTTP/1.1 200 OK
Content-Type: image/webp

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?format=bogus"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unsupported format 'bogus'."}}

$ curl -s -i -X DELETE http://localhost:5221/api/files/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
$ curl -s -i -X POST http://localhost:5221/api/files/<id>/restore -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
$ curl -s -i -X DELETE "http://localhost:5221/api/files/<id>?purge=true" -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content
```

### Users (`UsersController`，`api/users`)——所有 action 都需要 Cookie or Bearer

| 方法與路徑 | 本文 | 回應 | 權限 |
|---|---|---|---|
| `POST /api/users` | `{ email, password, name? }` | `201`，`Location: /api/items/user/{id}`，`{ id, email, name }` | 超級管理員 |
| `PUT /api/users/{id}/password` | `{ newPassword, currentPassword? }` | `204`，或 `404` | 超級管理員 (變更另一位使用者) ——或本人，並提供 `currentPassword` |
| `POST /api/users/{id}/access-token` | — | `200`，`{ token }` (只會顯示一次——只有雜湊值會被儲存) | 超級管理員 |
| `DELETE /api/users/{id}/access-token` | — | `204`，或 `404` | 超級管理員 |
| `GET /api/users/{id}/effective-permissions` | — | `200`，`{ isSuperAdmin, permissions }`——接受 `?roles=` (一份以逗號分隔的角色 id 清單)，可預覽一個*假設性、尚未儲存*的角色選擇，而不是使用者實際儲存的角色;`?roles=` (存在但為空) 會預覽公開角色的底線授權，這與該參數完全不存在是不同的兩種情況 | 超級管理員 |

```
$ curl -s -X POST http://localhost:5221/api/users -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"email":"editor@example.com","password":"editorpass1"}'
{"success":true,"data":{"id":"...","email":"editor@example.com","name":null}}

$ curl -s -i -X PUT http://localhost:5221/api/users/<self-id>/password -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b editor-cookies.txt -d '{"newPassword":"newpassword2"}'
HTTP/1.1 400 Bad Request
{"success":false,"error":{"code":"INVALID_CURRENT_PASSWORD","message":"Current password is incorrect."}}

$ curl -s -X POST http://localhost:5221/api/users/<id>/access-token -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":true,"data":{"token":"<token>"}}

$ curl -s -b cookies.txt "http://localhost:5221/api/users/<id>/effective-permissions?roles="
{"success":true,"data":{"isSuperAdmin":false,"permissions":{}}}
```

### Roles (`RolesController`，`api/roles`)——所有 action 都需要 Cookie or Bearer + 超級管理員

| 方法與路徑 | 本文 | 回應 |
|---|---|---|
| `GET /api/roles/{id}/permissions` | — | `200`，`{ collection, canRead, canWrite, canDelete }` 的陣列，或 `404` |
| `PUT /api/roles/{id}/permissions` | 相同形狀的 JSON 陣列——是該角色授權集合的**完整替換** (在同一個 transaction 中先全部刪除、再全部插入);一筆全為 `false` 的項目會被儲存為不存在 | `200`，已儲存的 (非全 `false`) 資料列，或 `400`/`404` |

```
$ curl -s -X PUT http://localhost:5221/api/roles/<id>/permissions -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[{"collection":"role","canRead":true,"canWrite":true,"canDelete":false}]'
{"success":true,"data":[{"collection":"role","canRead":true,"canWrite":true,"canDelete":false}]}

$ curl -s -X PUT http://localhost:5221/api/roles/<id>/permissions -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[{"collection":"file","canRead":true,"canWrite":false,"canDelete":false},{"collection":"file","canRead":false,"canWrite":true,"canDelete":false}]'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Duplicate collection entries: file."}}

$ curl -s -X PUT http://localhost:5221/api/roles/<id>/permissions -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '[{"collection":"bogus","canRead":true,"canWrite":false,"canDelete":false}]'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown collections: bogus."}}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/roles/00000000-0000-0000-0000-000000000000/permissions"
{"success":false,"error":{"code":"NOT_FOUND","message":"Role not found."}}

$ curl -s -i -b editor-cookies.txt -H "X-Struo-CSRF: 1" -X PUT "http://localhost:5221/api/roles/<id>/permissions" -d '[]'
{"success":false,"error":{"code":"FORBIDDEN","message":"Admin role required."}}
```

### Languages (`LanguagesController`，`api/languages`)——只需要 Cookie or Bearer

| 方法與路徑 | 回應 |
|---|---|
| `GET /api/languages` | `200`，`{ code, name, isDefault }` 的陣列——只包含已啟用的語言 |

```
$ curl -s -b cookies.txt "http://localhost:5221/api/languages"
{"success":true,"data":[{"code":"en","name":"English","isDefault":true},{"code":"zh-TW","name":"繁體中文","isDefault":false}]}
```

### Settings (`SettingsController`，`api/settings`)——需要 Cookie or Bearer + 超級管理員

| 方法與路徑 | 本文 | 回應 |
|---|---|---|
| `PUT /api/settings/branding` | `{ brandName, logoFileId? }`——`logoFileId` 若有設定，必須指向一個*已發布*的檔案 | `200`，`{ brandName, brandLogoUrl }`，或 `400`/`403` |

在這裡寫入，會立即清除 `ConfigController` 的快取鍵，所以下一次 `GET /api/config` 不需要等滿
30 秒的 TTL 就能反映出來:

```
$ curl -s -X PUT http://localhost:5221/api/settings/branding -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{"brandName":"StruoCMS Docs Demo","logoFileId":null}'
{"success":true,"data":{"brandName":"StruoCMS Docs Demo","brandLogoUrl":null}}
$ curl -s "http://localhost:5221/api/config"
{"success":true,"data":{"oidcEnabled":false,"brandName":"StruoCMS Docs Demo","brandLogoUrl":null,"passwordMinLength":8}}
```

### Schema (`SchemaController`，`api/schema`)——只需要 Cookie or Bearer

| 方法與路徑 | 回應 |
|---|---|
| `GET /api/schema` | `200`，每一個非隱藏欄位的 `CollectionMetadata` 陣列 |
| `GET /api/schema/{collection}` | `200`，單一個 `CollectionMetadata`，或 `404` |

在這個 host 上，會回傳七個集合:`language`、`permission`、`role`、`user`、`userRole`、`file`、
`mediaFolder`。`Hidden` 欄位永遠不會出現在任何一個回應中 (第 5 章)。

### Auth (`AuthController`，`api/auth`)

| 方法與路徑 | 驗證 | 速率限制 | 本文 | 回應 |
|---|---|---|---|---|
| `POST /api/auth/login` | 匿名 | 每帳號每 900 秒 10 次失敗 (`RateLimiting:LoginAccount`，預設開啟) + 每客戶端 IP 每 60 秒 5 次 (`RateLimiting:Login`，預設關閉) ——見第 3 章 | `{ email, password }` | `200`，`{ id }`，設定 session cookie;或 `401` |
| `POST /api/auth/logout` | Cookie or Bearer | — | — | `204`，清除 session cookie |
| `GET /api/auth/me` | Cookie or Bearer | — | — | `200`，`{ id, email, name, isSuperAdmin, permissions }` (對超級管理員而言 `permissions` 是 `{}`——每一項授權都是隱含存在的) |
| `GET /api/auth/login/oidc` | 匿名 | — | `?returnUrl=` | `302` 挑戰導向設定好的 OIDC 提供者，或在 `Oidc:Enabled` 為 `false` 時回傳 `404` |

```
$ curl -s -c cookies.txt -X POST http://localhost:5221/api/auth/login -H "Content-Type: application/json" -d '{"email":"admin@admin.com","password":"admin"}'
{"success":true,"data":{"id":"019fa8b2-4d09-7155-b641-2c3e2519233b"}}

$ curl -s -b cookies.txt "http://localhost:5221/api/auth/me"
{"success":true,"data":{"id":"019fa8b2-4d09-7155-b641-2c3e2519233b","email":"admin@admin.com","name":"Administrator","isSuperAdmin":true,"permissions":{}}}

$ curl -s -i -X POST -H "X-Struo-CSRF: 1" -b cookies.txt "http://localhost:5221/api/auth/logout"
HTTP/1.1 204 No Content
$ curl -s -i -b cookies.txt "http://localhost:5221/api/auth/me"
{"success":false,"error":{"code":"UNAUTHORIZED","message":"Authentication required."}}

$ curl -s -i "http://localhost:5221/api/auth/login/oidc"
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
```

### Config (`ConfigController`，`api/config`)——匿名

| 方法與路徑 | 回應 |
|---|---|
| `GET /api/config` | `200`，`{ oidcEnabled, brandName, brandLogoUrl, passwordMinLength }`——伺服器端快取 30 秒;一次品牌設定的儲存會立即清除它 |

```
$ curl -s "http://localhost:5221/api/config"
{"success":true,"data":{"oidcEnabled":false,"brandName":"StruoCMS Docs Demo","brandLogoUrl":null,"passwordMinLength":8}}
```

(這裡的 `brandName` 是上面品牌範例留下的已儲存覆寫值，不是 `Branding:Name` 在 appsettings.json 中的
預設值 `"StruoCMS"`——那個預設值記載於第 3 章。)

### Ping (`PingController`，`api/ping`)——匿名

| 方法與路徑 | 回應 |
|---|---|
| `GET /api/ping` | `200`，`{ status: "ok", service: "StruoCMS", utc }` |

```
$ curl -s "http://localhost:5221/api/ping"
{"success":true,"data":{"status":"ok","service":"StruoCMS","utc":"2026-07-29T07:23:58.4897657Z"}}
```

### Health (掛載於 `Program.cs`，不是一個 controller)——匿名，未經包裝

`/health/live` 與 `/health/ready` 是 ASP.NET Core 自身的健康檢查 middleware，直接掛載而非透過一個
controller action——它們的回應是健康檢查框架自身的純文字，**不是**上方的 REST 信封:

| 路徑 | 檢查項目 | 回應 |
|---|---|---|
| `GET /health/live` | 無 (`Predicate = _ => false`——永遠回報健康，純粹是一個存活探針) | `200`，`Healthy` |
| `GET /health/ready` | `database` (`DbReadinessCheck`)、`cache` (`CacheReadinessCheck`)——兩者都標記為 `"ready"` | `200 Healthy`，或當某個相依項目故障時回傳失敗檢查項目自身的非 200 狀態 |

```
$ curl -s http://localhost:5221/health/ready
Healthy
```

## 接下來該去哪

- 第 8 章 [查詢 DSL](08-query-dsl.md)，涵蓋 `ItemsController` 的清單/取得/查詢 action 與 GraphQL
  共用的 `filter`/`sort`/`limit`/`offset`/`fields`/`deep`/`search`/`locale`/`deleted` 介面。
- 第 7 章 [關聯](07-relations.md)，涵蓋 `OnDelete.Restrict`，以及一次刪除/篩選可能觸及的跨關聯
  帶點號路徑規則。
- 第 10 章 [GraphQL API](10-graphql-api.md)，說明同一批集合與權限模型，如何以一份型別化的 schema
  公開，包括每一次以 cookie 驗證的 `POST /graphql` 都需要的 `X-Struo-CSRF` 標頭。
- 第 12 章 [認證、SSO 與 RBAC](12-auth-and-rbac.md)，說明上方每一張表格中「權限」欄位背後
  完整的 RBAC/`AdminOnly`/OIDC 模型。
