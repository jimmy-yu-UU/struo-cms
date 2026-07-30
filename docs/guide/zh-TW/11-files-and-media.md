# 11. 檔案、媒體與圖片轉換

`File` 與 `MediaFolder` 是隨每一次 StruoCMS 安裝都會出貨的框架集合 (collection)
(`src/Struo.Infrastructure/Files/*`)——媒體庫完全不需要下游額外設定。對 `File` 的寫入，會經過
一條專屬管線 (`FileService`、`IFileStorage`、位於 `api/files` 底下的 `FilesController`)，而不是
其他每一個集合都使用的通用 `ItemService`/`ItemsController` 介面，因為一次檔案上傳所牽涉的
關注點——位元組儲存、內容類型偵測、圖片尺寸、即時轉換——通用 CRUD 路徑沒有理由要為非檔案的集合
承擔這些。讀取 `file`/`mediaFolder` 的*中介資料* (列出/篩選/排序一個媒體庫，而不是位元組本身)，
仍然是透過第 8、9 章記載的一般 `api/items/file` 介面——本章涵蓋的是這兩章沒有涵蓋的檔案子系統
部分:上傳、儲存後端、提供服務、轉換，以及授權條款。

## 檔案模型與它的翻譯

`File` (`src/Struo.Infrastructure/Files/File.cs`) 是 `[CmsCollection("File", Group = "System",
DefaultDisplayField = nameof(FileName), Hidden = true)]`——這裡的 `Hidden` 是第 4 章的展示旗標
(從管理後台側欄中省略，因為媒體庫是它自己專屬的畫面)，而不是第 12 章涵蓋的欄位層級 `Hidden`。
它自己的欄位:

| 欄位 | 介面 | 唯讀 | 備註 |
|---|---|---|---|
| `fileName` | Text | 是 | 原始檔名，於上傳時設定。 |
| `contentType` | Text | 是 | 上傳時儲存、已通過驗證的 MIME type。 |
| `size` | Number | 是 | 已儲存 blob 的位元組數。 |
| `width` / `height` | Number | 是 | 像素尺寸，只有在上傳內容是尺寸讀取器 (見下文) 所能辨識的點陣格式時才會填入——其他情況 (包括每一種非圖片上傳) 一律為 `null`。 |
| `status` | Select (`draft`/`published`) | 否 | 決定該檔案的位元組與中介資料，對匿名者/公開存取是否可見 (見下方的「提供服務」)。 |

`StorageKey` 與 `FolderId` 都不帶任何 `[CmsField]`——`StorageKey` 是純粹的儲存層內部欄位 (永遠
不會被投影，也無法透過任何 API 寫入)，而 `FolderId` 則改為透過 `[CmsRelation]` 宣告 (一個指向
`MediaFolder` 的可為 null many-to-one，`OnDelete = OnDelete.Restrict`——第 7 章的關聯刪除守衛，
所以一個仍包含檔案的資料夾無法被刪除)。

**可翻譯**的欄位——`title` 與 `alt`——存放在一個附屬資料表 entity 上，`FileTranslation`
(`src/Struo.Infrastructure/Files/FileTranslation.cs`，資料表 `file_translations`，在
`(fileid, locale)` 上具備唯一性)，正是第 6 章記載、任何集合的 `[CmsTranslations]` 附屬資料表所
使用的同一套機制。一次上傳，會用去除副檔名後的檔名，播下**預設語言**的 `title` 種子
(`FileService.SaveAsync`，`src/Struo.Infrastructure/Files/FileService.cs:127-133`)——一個去除
副檔名後變成空字串的點檔案 (dotfile) 會退回使用完整檔名，結果並會被夾限在 255 字元以內——所以
每一個上傳的檔案，在媒體庫中都能立即被人類辨識，不需要額外的編輯步驟。`alt` 則永遠不會被自動
填入;預設語言以外的語言，在有人寫入之前，完全沒有種子資料列，與任何其他可翻譯集合 (第 6 章)
相同。

## 上傳

```
POST /api/files
Content-Type: multipart/form-data
  file: <binary>       (required)
  folderId: <uuid>     (optional)
```

需要 Cookie or Bearer，再加上 `IFileAccessPolicy.CanWrite()`——這是 `file` 集合的 RBAC 授權，
之所以在這裡而不是在 `ItemService` 內部強制執行，是因為上傳完全繞過了它
(`src/Struo.Api/Auth/FileAccessPolicy.cs`)。一次成功的上傳是 `201`，帶有該資料列的公開形狀，
以及一個 `Location: /api/files/{id}` 標頭——第 9 章對 `EnvelopeResultFilter`/`CreatedResult`
的說明在這裡同樣適用:`FilesController.Upload` 呼叫 `Created($"/api/files/{id}", ...)`，而這個
filter 會重建該 `CreatedResult` (而不是把它壓平成一個單純的 `ObjectResult`)，讓標頭在包裝之後
仍然存活:

```
$ curl -s -X POST http://localhost:5221/api/files -H "X-Struo-CSRF: 1" -b cookies.txt \
    -F "file=@doc-sample.png;type=image/png"
{"success":true,"data":{"id":"4cac2852-e270-4091-94d1-01831452a9b9","fileName":"doc-sample.png","contentType":"image/png","size":6363,"width":64,"height":48,"status":"published","folderId":null}}
```

每一次上傳，在寫入任何一個位元組之前，都會對照三個各自獨立的上限做驗證
(`FileService.UploadAsync`，`src/Struo.Infrastructure/Files/FileService.cs:26-89`;預設值來自
`src/Struo.Api/appsettings.json:15-29` 中的 `Struo:Files`，完整記載於第 3 章):

- **大小**——`Struo:Files:MaxUploadBytes`，預設 **25 MB** (`26214400` 位元組)。客戶端宣告的
  `Content-Length` 會先被前置檢查 (`QueryException`，`400`);實際串流的位元組數，則透過
  `FileBufferingReadStream` 的緩衝區上限，獨立受到相同上限的限制，所以一個少報大小、之後卻串流
  更多內容的客戶端，仍然會失敗——以 `PAYLOAD_TOO_LARGE` / `413` (`DomainErrorMap`，第 9 章)
  的形式，而不是一個被靜默截斷的檔案。
- **內容類型白名單**——`Struo:Files:AllowedContentTypes`，預設就是 `appsettings.json` 中出貨的
  這份確切清單:`image/jpeg`、`image/png`、`image/gif`、`image/webp`、`image/avif`、
  `image/svg+xml`、`application/pdf`、`text/plain`、`video/mp4`、`audio/mpeg`、
  `application/vnd.ms-powerpoint`、
  `application/vnd.openxmlformats-officedocument.presentationml.presentation`、
  `application/msword`、
  `application/vnd.openxmlformats-officedocument.wordprocessingml.document`、
  `application/vnd.ms-excel`、
  `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`、`application/zip`、
  `application/x-zip-compressed`。**一個空陣列會允許每一種內容類型**——這是 `[]` 有文件記載的
  意義，不是一種誤設定。
- **簽章一致性**——`FileSignatureValidator`
  (`src/Struo.Infrastructure/Files/FileSignatureValidator.cs`) 會針對它認得 magic-byte 簽章的
  格式 (PNG、JPEG、GIF、WebP、PDF)，把前幾個位元組拿來對照*宣告*的內容類型做偵測;不符會被拒絕
  (`QueryException`，`400`)——這可以阻止一個以 `image/png` 儲存的指令碼，之後又被當成
  `image/png` 提供服務。一個沒有已知簽章的宣告類型 (例如 `text/plain`、
  `application/octet-stream`) 沒有東西可供矛盾比對，會被原封不動地放行。

像素尺寸只會從檔案的**標頭**讀取——`ImageDimensionReader`
(`src/Struo.Infrastructure/Files/ImageDimensionReader.cs`) 會直接剖析 PNG/GIF/WebP/JPEG 的
容器結構 (magic bytes 加上少數幾個固定位移)，完全不會解碼像素資料，這正是它不會替一次不受信任的
上傳增加任何攻擊面的原因。無論宣告的內容類型為何，它恰好只辨識這四種點陣格式;其他任何格式——
包括*確實*名列在預設白名單中的 `image/svg+xml`——都會讓 `width`/`height` 維持 `null`。

儲存鍵本身是產生出來的，絕不會由客戶端提供:`StorageKey.Create`
(`src/Struo.Infrastructure/Files/StorageKey.cs`) 會根據目前的 UTC 日期與一個新產生的 GUID，
建構出 `{yyyy}/{MM}/{guid:N}{ext}`，所以鍵永遠不會碰撞，而原始檔名也永遠不會進入儲存層的
命名空間。

## 儲存後端:`local` 與 `s3`

`Struo:Files:Backend` 恰好會選出一個已註冊的 `IFileStorage`
(`FileStorageServiceCollectionExtensions.AddStruoFiles`，
`src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs:26-32`)
——`"local"` (預設) 或 `"s3"`;任何其他值都會讓啟動驗證失敗 (第 3 章)。

**`local`** (`LocalFileStorage`，`src/Struo.Infrastructure/Files/LocalFileStorage.cs`) 會在
`Struo:Files:Local:RootPath` (預設 `App_Data/uploads`) 底下讀寫一般檔案。每一個解析出的路徑，
在做任何 I/O 之前，都會用一個對結尾分隔符號安全的前綴比對，去對照根目錄做檢查——一個會解析到根
目錄之外的儲存鍵，會擲出 `InvalidOperationException`，而不是真的逃脫出去。這個後端從不支援
presigned URL (`SupportsPresignedUrls => false`)，而且在儲存時會忽略傳入的內容類型——位元組
永遠是透過 API 本身串流回去的，並以已保存的 `File.ContentType` 作為 `Content-Type` 標頭
(`FilesController.Download`)，從來不是一個記錄在儲存層的類型。**這正是本章從頭到尾即時驗證過的
後端**——上方每一個範例所使用的執行中 host (即執行本章範例的 `Struo.Api` 執行個體)，都設定為
`Struo:Files:Backend = "local"`。

**`s3`** (`S3FileStorage`，`src/Struo.Infrastructure/Files/S3FileStorage.cs`) 是一個相容 S3 的
客戶端 (`AmazonS3Client`)，設定來自 `Struo:Files:S3`:

| 鍵 | 預設值 | 備註 |
|---|---|---|
| `Endpoint` | `REPLACE_ME` (必填) | S3 相容端點的 URL，例如本機 MinIO 用的 `http://localhost:9000`。 |
| `Bucket` | `REPLACE_ME` (必填) | 目標 bucket 名稱。 |
| `AccessKey` / `SecretKey` | `REPLACE_ME` (必填) | 上方端點所使用的憑證。 |
| `Region` | `"us-east-1"` | 傳遞給 AWS S3 SDK 客戶端。 |
| `ForcePathStyle` | `true` | Path-style 定址——MinIO 與大多數自架的 S3 相容伺服器都需要它，因為它們不支援 virtual-hosted-style 的 bucket URL。 |
| `PresignTtlSeconds` | `300` | 產生出的 presigned URL 存活時間，單位為秒。 |

一旦 `Backend` 是 `s3`，`Endpoint`/`Bucket`/`AccessKey`/`SecretKey` 全部都是必填 (啟動時驗證，
第 3 章)。這個後端會在儲存時，把已驗證的內容類型記錄為 S3 物件中介資料 (所以即使 API 不在流程
中，一次直接/presigned 的 `GET` 也能提供正確的 `Content-Type`)，而且確實支援 presigned URL——
`GetPresignedUrlAsync` 會鑄造一個有時間限制的 `GET` URL，並強制開啟
`ResponseHeaderOverrides.ContentDisposition = "attachment"`，所以瀏覽器跟隨這個重新導向時會
下載而不是渲染——這是對某些類型 (例如 SVG) 的縱深防禦，因為 API 自身的 `Content-Disposition`
處理在這條路徑上原本不會涵蓋到它們。`AmazonS3Config.UseHttp` 是依 `Endpoint` 是否以 `http://`
開頭推導出來的，因為 SDK 的 presigned URL 預設綱要是 `https`，一個僅支援 http 的 MinIO 端點在
重新導向之後會拒絕它。

**本機開發用的 MinIO:** `docker-compose.yml` 的 `minio` 服務放在 `s3` 這個 Compose profile
之後 (不會被單純的 `docker compose up -d` 啟動，第 2 章)——`docker compose --profile s3 up -d`
會啟動它，再加上一個一次性的 `createbuckets` 容器，負責建立 bucket 並以 0 結束。它出貨的預設值:
API port **9000**、console port **9001**、憑證 `struoadmin`/`struoadmin` (僅供開發用——絕不會
在其他任何地方重複使用)、bucket **`struo-media`**。每一個 port 都可以在不編輯受版本控制的
compose 檔案的情況下覆寫——`STRUO_MINIO_PORT` / `STRUO_MINIO_CONSOLE_PORT` 環境變數 (或是那份
已被 gitignore、從 `.env.example` 複製而來的 `.env`)，與第 2 章記載
`STRUO_PG_PORT`/`STRUO_REDIS_PORT` 的慣例相同。一個指向該容器預設值的 `Struo:Files:S3` 區塊:

```json
"Struo": {
  "Files": {
    "Backend": "s3",
    "S3": {
      "Endpoint": "http://localhost:9000",
      "Bucket": "struo-media",
      "AccessKey": "struoadmin",
      "SecretKey": "struoadmin",
      "ForcePathStyle": true
    }
  }
}
```

**本章即時驗證了 `local` 後端，並直接依據 `S3FileStorage`/`FileStorageOptions.S3Options` 與
`docker-compose.yml` 記載 `s3`**——`s3` 後端本身在這裡並未即時演練。

## 提供檔案服務:已驗證 vs. 匿名、presigned 重新導向

`GET /api/files/{id}` 與 `GET /api/files/{id}/content` 都不帶任何 `[Authorize]`——允許匿名
請求通過，這樣一個**已發布**檔案的中介資料/位元組，就能不需要 session 就內嵌在公開頁面中。一個
**未發布** (`status != "published"`) 的檔案，額外需要 `IFileAccessPolicy.CanReadUnpublished`
——一個**已驗證的身分**，加上一個 `file` 集合的**`CanWrite`**授權 (`FileAccessPolicy.cs:36-37`)，
不是一個 `CanRead` 授權，也不僅僅是已登入。這道關卡刻意設在寫入上:`public` 對每一個呼叫端都是一道
權限底線 (第 12 章)，所以一旦 `file` 帶有一個公開*讀取*授權——這正是匿名提供圖片服務的文件記載
設定——`CanRead("file")` 對匿名者與已驗證的呼叫端都同樣成立，因此完全無法把關任何東西;一份草稿是
一種編輯狀態，所以能*編輯*檔案的呼叫端，才是能看見它的呼叫端，而且沒有任何公開角色會被授予寫入權。
這項檢查失敗時，會回傳單純的 `404` 而不是 `403`，所以一份草稿的存在與否，不會外洩給一個沒有該授權
的呼叫端 (`FilesController.Get`/`Download`，`src/Struo.Api/Controllers/FilesController.cs:57-72`、
`74-163`)。這也代表一個**已移入回收桶** (軟刪除——見下文) 的檔案，這兩個 action 也都會回傳
`404`，因為 `FileService.GetAsync` 讀取時，會經過與其他每一次讀取相同的 `ISoftDeletable`
查詢過濾器——已即時驗證:

```
$ curl -s -i -X DELETE http://localhost:5221/api/files/4cac2852-e270-4091-94d1-01831452a9b9 -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content

$ curl -s -i -b cookies.txt http://localhost:5221/api/files/4cac2852-e270-4091-94d1-01831452a9b9
HTTP/1.1 404 Not Found
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
```

當沒有要求任何轉換時 (見下文)，若 `Struo:Files:PresignedRedirect` 為 `true`，`Download` 會
`302` 重新導向到一個儲存端 presigned 的 URL，而不是自行串流位元組——這是一項明確的部署選用啟用
項目，適用於瀏覽器可以直接觸及儲存端/CDN 的架構，預設 (`false`) 關閉，讓管理後台 SPA 的縮圖/
預覽，無條件都能透過 API 運作，也讓儲存端點本身得以維持私有。在 `local` 後端上，這項設定沒有
任何效果 (`GetPresignedUrlAsync` 在那裡永遠回傳 `null`)，所以 `Download` 一律會落回串流。

## 媒體資料夾

`MediaFolder` (`src/Struo.Infrastructure/Files/MediaFolder.cs`) 是第二個框架集合——一棵純粹的
組織用樹狀結構 (自我參照的 `ParentId`、`TreeSelect` 關聯介面)，與實體儲存鍵完全脫鉤。它與
`File` 一樣是 `Hidden` (它唯一的 UI 就是媒體庫);CRUD 是透過一般的 `api/items/mediaFolder`
介面，而不是 `FilesController`。刪除一個仍包含檔案或子資料夾的資料夾，會以與任何
`OnDelete.Restrict` 關聯 (第 7 章) 相同的方式被拒絕——沒有任何客製化的守衛程式碼，只是
`File.Folder` 與 `MediaFolder.Parent` 上宣告好的關聯:

```
$ curl -s -i -X DELETE http://localhost:5221/api/items/mediaFolder/<id> -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":false,"error":{"code":"CONFLICT","message":"Cannot delete 'mediaFolder/<id>': referenced by 'file'."}}
```

## 即時圖片轉換

```
GET /api/files/{id}/content?width=&height=&format=&fit=&quality=
```

只有在這項功能已啟用 (`Struo:Files:ImageTransform:Enabled`，預設 `true`)、`width`/`height`/
`format` 至少存在一個，而且該檔案的 `contentType` 是以 `image/` 開頭時，才會嘗試做轉換——否則
`Download` 會直接落回上方單純的直通/重新導向行為，不做任何改變
(`FilesController.Download:91-94`)。以下參數取自即時程式碼，而不只是文件說明:

| 參數 | 意義 | 夾限/預設值 |
|---|---|---|
| `width` | 目標寬度 (像素) | `Math.Clamp(width, 1, Struo:Files:ImageTransform:MaxWidth)`——預設 `MaxWidth` 為 **4096**。 |
| `height` | 目標高度 (像素) | `Math.Clamp(height, 1, Struo:Files:ImageTransform:MaxHeight)`——預設 `MaxHeight` 為 **4096**。 |
| `format` | 輸出格式 | 必須是 `Struo:Files:ImageTransform:AllowedFormats` 之一——預設為 `["webp", "jpeg", "png", "avif"]`——否則回傳 `400 BAD_USER_INPUT` ("Unsupported format '…'.");省略時會保留原始位元組自身的編碼邏輯 (見下文)。 |
| `fit` | 縮放策略 | 除了 `"cover"` (或只給定 width/height 其中一個時的 `"cover"`) 以外的任何值，都會被當作「在框內縮放，絕不放大」處理。省略時預設為 `"inside"`，解析結果相同。 |
| `quality` | 編碼品質/壓縮率 | `Math.Clamp(quality ?? Struo:Files:ImageTransform:DefaultQuality, 1, 100)`——預設 `DefaultQuality` 為 **82**。 |

只有 `fit=cover` 且**同時**給定 `width` 與 `height` 時，才會真正裁切
(`NetVipsImageTransformer.Transform`，
`src/Struo.Infrastructure/Files/NetVipsImageTransformer.cs:43-46`)——精確地置中裁切成那個
方框 (libvips 的 `Enums.Size.Both` + `Interesting.Centre`)。其他任何組合——`inside`/
`contain`/一個無法辨識的值，或只給定一個維度的 `cover`——都會縮放至符合給定的邊界，絕不會超過
原始來源的原生解析度做放大 (`Size.Down`)。兩者 (`width`、`height`) 都不要求時 (只做格式/品質
轉換)，會完全跳過縮圖路徑，直接透過 `Image.NewFromBuffer` 重新編碼完整解碼過的圖片——因為沒有
框可以縮進去。

已對照上方上傳的那張 64×48 PNG (`doc-sample.png`) 做即時驗證，解碼每一個回應自身的容器標頭，
以確認實際的輸出尺寸——而不只是看一個 200 狀態:

```
$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=32&format=webp" -o t1.webp
HTTP/1.1 200 OK
Content-Type: image/webp
# decoded VP8X header: width=32, height=24 (aspect preserved: 64:48 == 32:24)

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=20&height=20&fit=cover&format=png" -o t2.png
HTTP/1.1 200 OK
Content-Type: image/png
# decoded IHDR: width=20, height=20 (fit=cover + both dims -> centre-cropped square)

$ curl -s -b cookies.txt "http://localhost:5221/api/files/<id>/content?format=bogus"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unsupported format 'bogus'."}}

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=10&quality=500&format=jpeg" -o t3.jpg
HTTP/1.1 200 OK
Content-Type: image/jpeg
# decoded SOF0: width=10, height=8 (quality=500 accepted -> clamped to 100 server-side, no error)

$ curl -s -i -b cookies.txt "http://localhost:5221/api/files/<id>/content?width=5000&format=png" -o t4.png
HTTP/1.1 200 OK
Content-Type: image/png
# decoded IHDR: width=64, height=48 (width=5000 clamped to MaxWidth=4096, but the source is only
# 64px wide and ThumbnailBuffer's Size.Down never upscales past it -> output stays at the source size)
```

縮放是透過 libvips 的 `ThumbnailBuffer` (`Image.ThumbnailBuffer`)，而不是 `NewFromBuffer` +
`ThumbnailImage`——`NetVipsImageTransformer` 上的文件註解說明了原因:`ThumbnailImage` 是對一張
已經完整解碼過的圖片做操作 (沒有載入時縮小)，所以一個巨大的來源，無論要求的輸出尺寸為何，都會先
以完整解析度解碼，之後才被縮小。`ThumbnailBuffer` 則會針對支援的編解碼器 (JPEG/WebP 等) 在載入
時就先縮小，把解碼記憶體用量限制在大致等於要求的輸出尺寸，而不是來源的完整解析度——PNG 在
libvips 中沒有更便宜的解碼路徑，仍然會完整解碼，但常見的大型相片情境會被限制住。一個只給定
height 的請求，會先只從標頭窺視來源的寬度 (不解碼像素)，這樣未受限制的那個維度，仍然可以正確地
傳給 `ThumbnailBuffer`。

回應的 `Content-Type`，是透過 `ImageContentTypes`
(`src/Struo.Application/Files/ImageContentTypes.cs`) 從 `ImageTransformRequest.Format`
推導出來的——`webp`/`jpeg`/`jpg`/`png`/`avif` 會對應到各自顯而易見的 MIME type，其他任何情況
(包括省略的情況) 都會退回 `image/webp`。這份對應表，必須與內建在
`NetVipsImageTransformer.Transform` 自身裡的格式→內容類型切換邏輯逐位元組完全一致，因為一次
快取**命中** (見下文) 從不會重新執行轉換器——controller 只有快取的位元組加上請求的 `Format`，
可以用來推導回應標頭。

如果轉換本身擲出例外 (一次損毀的上傳、一種不受支援的來源編碼、一次 libvips 失敗)，`Download`
會連同檔案 id 記錄下這個例外，並退回提供**原始**位元組，而不是直接讓整個請求失敗
(`FilesController.Download:130-145`)——一張損壞的縮圖，被認為比一張未經轉換的原圖更糟，但這個
錯誤絕不會被靜默吞掉。

### 快取位置與版本控制

轉換後的變體會快取在磁碟上——`DiskImageVariantCache`
(`src/Struo.Infrastructure/Files/DiskImageVariantCache.cs`)——放在
`Struo:Files:ImageTransform:CachePath` (預設 `App_Data/image-cache`) 底下，並根據應用程式的
**content root** (內容根目錄) 解析，絕不是行程的目前工作目錄
(`FileStorageServiceCollectionExtensions.cs:45-52`——第 3 章對這項確切設定所標記的同一個
CWD 對 content root 的陷阱)。快取鍵是檔案 id、目前的
`Version` (`AuditableEntity` 的樂觀並行控制計數器——不是 `UpdatedAt`，因為這個 entity 把它視為
不可為 null，所以沒有一個自然的「未設定」哨兵值可以依賴)，以及每一個轉換參數，各自在雜湊之前
各佔自己標記過的一行 (`DiskImageVariantCache.DeriveKey`/`FilesController.Download:110-116`)
的 SHA-256 摘要 (以小寫十六進位表示)——所以一次重新上傳 (會使 `Version` 遞增) 自然會發生
快取未命中，而不會提供一個過時的變體，也完全不需要任何明確的快取失效機制。鍵值會依照自己前兩個
十六進位字元，切分進一個子目錄，並以原子方式寫入 (暫存檔案再重新命名)，所以一個並行的讀取者，
絕不會看到一個寫到一半的變體。已即時驗證:在上方四次轉換之後，快取目錄裡每一個不同的鍵各有一個
檔案，各自位於一個雙字元的分片目錄底下:

```
$ find src/Struo.Api/App_Data/image-cache -type f
src/Struo.Api/App_Data/image-cache/8b/8b6791b1b88b7ad046f6d69219f24cbdb1aec32a811b9a9454fb0528634a0968
src/Struo.Api/App_Data/image-cache/a8/a8906004f2b920caf99b71f67e80424f2755b5d63b6d584d98e2109425455405
src/Struo.Api/App_Data/image-cache/ad/ad7814d39400620fc7cc152244584778e7f1ed86e29c80ad8617e4069fa32833
src/Struo.Api/App_Data/image-cache/d0/d03c56a833ffb5c93816e6a8271de100b96f57dd3c1d4e44bae1f364e67e4964
```

## libvips/NetVips 的 LGPL 說明

這套轉換管線是依據 **NetVips** (MIT 授權的受管包裝) 實作的，它會在執行期動態載入原生的
**libvips** 函式庫——StruoCMS 從不直接連結 libvips，也從不靜態連結它。這一點很重要，因為
libvips 本身是 **LGPL-2.1-or-later**，而 StruoCMS 是 MIT。依照 `THIRD-PARTY-NOTICES.md`
所述:動態連結代表 LGPL-2.1 的條款，只會要求 StruoCMS (a) 重製授權聲明、(b) 不限制使用者重新
連結一個修改過的 libvips 的能力，以及 (c) 讓 libvips 自身的原始碼可取得 (上游已經如此)
——StruoCMS 沒有對 libvips 施加任何額外限制，也沒有內嵌任何 libvips 原始碼，所以
**StruoCMS 自身的 MIT 授權不受影響**。內建的原生版本是 libvips **8.18.4** (透過
`NetVips.Native` 套件——並非以 RID 命名的變體，它為每一個受支援的執行期都內建了原生組件——
釘選於 `Directory.Packages.props`);NetVips 本身則是
**3.2.0**。`NetVips.Native.*` 套件另外還內建了 libvips 自身的幾個選用相依套件 (mozjpeg、
libpng、libwebp、cairo、pango、librsvg 等)，各自採用混合的 MIT/BSD/LGPLv3 授權，記載於各自
套件的 `THIRD-PARTY-NOTICES.md` 中。

## 檔案的回收桶、還原與清除

`File` 是**唯一**實作 `ISoftDeletable` 的框架集合 (第 13 章完整涵蓋這個介面及其全域查詢過濾器;
本節是檔案專屬的走查)。`FileService` 公開了 `FilesController` 的 `DELETE`/`restore` action 所
呼叫的三個操作:

- **`TrashAsync`** (預設的 `DELETE`)——透過與其他每一個可軟刪除集合相同的原子儲存庫基本操作
  (`WHERE deletedat IS NULL`，第 13 章) 做軟刪除;blob 與它的 `FileTranslation` 資料列都會原地
  保留，供之後還原。如果被移入回收桶的檔案是目前的品牌 logo (`site_settings.logofileid`)，這個
  參照會在同一個 transaction 中被清除，讓 `ConfigController` 不再把它解析成一個失效的
  `/content` URL。
- **`RestoreAsync`** (`POST /{id}/restore`)——清除 `DeletedAt`，與任何其他可軟刪除集合的
  還原相同。
- **`DeleteAsync`** (`DELETE ?purge=true`)——一次真正的硬刪除:`File` 資料列、它的
  `FileTranslation` 資料列，以及儲存的 blob (盡力而為——這裡的儲存層失敗會被吞掉，因為資料列
  已經消失，位元組只是單純變成孤兒) 全部都會被移除。基於相同原因，同樣會清除
  `site_settings.logofileid`，與回收桶時相同。

已端對端即時驗證 (上傳 → 移入回收桶 → 還原 → 再次移入回收桶 → 清除):

```
$ curl -s -X POST http://localhost:5221/api/files -H "X-Struo-CSRF: 1" -b cookies.txt -F "file=@purge-test.txt;type=text/plain"
{"success":true,"data":{"id":"a1f1112e-e8c7-4bfa-8aff-460303d8546d","fileName":"purge-test.txt", ...}}

$ curl -s -i -X DELETE http://localhost:5221/api/files/a1f1112e-e8c7-4bfa-8aff-460303d8546d -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content

$ curl -s -i -X DELETE "http://localhost:5221/api/files/a1f1112e-e8c7-4bfa-8aff-460303d8546d?purge=true" -H "X-Struo-CSRF: 1" -b cookies.txt
HTTP/1.1 204 No Content

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?deleted=with&sort=fileName"
{"success":true,"data":[{"fileName":"alpha-report.txt", ...},{"fileName":"beta-notes.txt", ...},{"fileName":"doc-sample.png", ...},{"fileName":"gamma-draft.txt", ...}],"meta":{"total":4,"limit":25,"offset":0}}
```

(即使在這裡，`purge-test.txt` 也不存在——`?deleted=with` 包含已移入回收桶的資料列，但一次
清除會把資料列完全移除，所以沒有任何東西留給任何 `deleted=` 模式可以找到。)

已確認被清除檔案的 blob，已從磁碟上的 `App_Data/uploads` 中移除 (它的儲存鍵底下沒有留下任何
孤兒檔案)。

## 接下來該去哪

- 第 3 章 [設定參考](03-configuration-reference.md)，個別涵蓋每一個 `Struo:Files:*` 鍵的
  預設值與驗證行為。
- 第 7 章 [關聯](07-relations.md)，涵蓋 `OnDelete.Restrict`——媒體資料夾拒絕一次非空刪除背後
  的機制。
- 第 8 章 [查詢 DSL](08-query-dsl.md) 與第 9 章 [REST API](09-rest-api.md)，涵蓋
  `file`/`mediaFolder` 集合自身中介資料上，一般的 `filter`/`sort`/`deleted=` 介面。
- 第 12 章 [認證、SSO 與 RBAC](12-auth-and-rbac.md)，涵蓋 `IFileAccessPolicy` 與
  `CanReadUnpublished` 背後的 `file` 集合 RBAC 授權。
- 第 13 章 [版本紀錄與軟刪除](13-revisions-and-soft-delete.md)，涵蓋一般性的
  `ISoftDeletable`/全域查詢過濾器/`deleted=` 機制，而 `File` 正是它唯一即時上線的範例。
