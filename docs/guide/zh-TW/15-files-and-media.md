# 15. 檔案、媒體與圖片轉換

檔案怎麼上傳、存到哪裡、怎麼送到讀者面前，以及圖片怎麼在下載當下即時轉換，是這一章的主題。

## `file` 集合與它的翻譯

`File` 與 `MediaFolder` 是框架自帶的集合，每一套 StruoCMS 裝好就有。媒體庫因此是現成的：設定
與程式碼都由框架提供。

`File` 的寫入走一條專屬管線：`FilesController`（路由 `api/files`）、`FileService`、
`IFileStorage`，不是走其他集合共用的 `ItemService`／`ItemsController` 路徑。一次上傳牽涉位
元組儲存、內容類型檢查、圖片尺寸與轉換，是通用的項目 CRUD 路徑不需要知道的事。

讀取、更新與刪除 `file` 的中繼資料，仍然走一般的 `api/items/file` 與 `api/items/mediaFolder`：
瀏覽、篩選與排序媒體庫，是一次普通的查詢。每個端點的授權與狀態碼見
[第 13 章：REST API 端點參考](13-rest-endpoints.md)。

透過通用的項目 API 建立 `file` 一律被拒，這道檢查寫在 `ItemService.CreateAsync`，因此 GraphQL
的 `createFile` mutation 也被同一句訊息擋下：`Files cannot be created through the generic items
API. Upload one with POST /api/files instead.`，狀態碼 `400`／`BAD_USER_INPUT`。

`File` 的集合名稱是 `file`，分組在 `System`，並且宣告了 `Hidden = true`。它管的是這個集合在
後台怎麼呈現，跟[第 6 章：欄位型別與編輯介面](06-field-types.md)講的欄位層級 `Hidden` 是兩
件事。

`file` 自己的欄位：

| 欄位 | 介面 | 可寫 |
|---|---|---|
| `fileName` | Text | 否 |
| `contentType` | Text | 否 |
| `size` | Number | 否 |
| `width` | Number（可為 null） | 否 |
| `height` | Number（可為 null） | 否 |
| `status` | Select `draft`／`published` | 是 |

`fileName` 是上傳當下記下的原始檔名，也是這個集合唯一自己宣告 `Searchable` 的欄位。超過 255
個 UTF-16 編碼單位的檔名會縮短到欄寬，保留副檔名，也不會切開一個代理對（surrogate pair）。
`contentType` 是驗證過的 MIME 類型；`size` 是實際存下的位元組數。`width`／`height` 只有在標
頭讀得出尺寸時才會填上，其餘上傳不管是不是圖片，兩者都留白。`status` 是唯一可寫的自有欄
位，也是決定誰讀得到這一列與它的位元組的關鍵，下一節細講。

`folderId` 不是 `[CmsField]`，而是宣告成 `[CmsRelation]` 的多對一關聯，指到 `mediaFolder`，
`TreeSelect` 介面，`OnDelete.Restrict`，這也是資料夾裡還有檔案時刪不掉的原因。它刻意不是唯
讀，把檔案搬到另一個資料夾就是一次普通的項目更新。`storageKey` 完全沒有 `[CmsField]`，不會
投影出去，也沒有任何寫入路徑碰得到它。

可翻譯的 `title` 與 `alt` 存在 sidecar 實體 `FileTranslation`（資料表 `struo_file_translations`，
在 `(fileid, locale)` 上唯一），跟[第 7 章：多語內容](07-i18n.md)講的 `[CmsTranslations]` 機
制完全一樣。上傳時，預設 locale 的 `title` 會用檔名去掉副檔名自動填上；去掉副檔名後變成空字
串（檔名只剩副檔名時）就退回完整檔名，結果一律截到 255 個 UTF-16
編碼單位，同樣不會切開代理對。

`alt` 不會被自動填，其他 locale 也不會拿到任何自動產生的紀錄，除非有人自己寫一筆。
`FileTranslation.Title` 也是 `Searchable`，所以對 `file` 下 `search=` 同時比對檔名與各
locale 的標題。

新上傳的檔案採用 `Struo:Files:DefaultStatus` 的值（鍵見[第 4 章](04-configuration.md)）：預設
是 `published`，也可以設成 `draft`。這個欄位自己宣告的預設值 `"draft"` 不會套用到上傳。預設為
`published` 時，剛上傳的檔案立刻能被任何持有其 id 的人讀取，不需要另外發布。

## 上傳

上傳是 `POST /api/files`，`Content-Type: multipart/form-data`，一個必要的分段叫 `file`，外
加一個選用的 `folderId` 分段帶 GUID。上傳另外要求 `file` 的寫入授權，沒有授權會得到 `Write
not permitted.`。

成功是 `201`，`Location` 指向剛建立的檔案，body 是投影過的一列：

```text
$ POST /api/files  (multipart: file=@swatch.png; type=image/png)
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Tue, 15 Sep 2026 03:01:43 GMT
Server: Kestrel
Location: /api/files/c4440800-43d3-4ff0-9fe7-b5d7671b30df
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff

{"success":true,"data":{"id":"c4440800-43d3-4ff0-9fe7-b5d7671b30df","fileName":"swatch.png","contentType":"image/png","size":125,"width":64,"height":48,"status":"published","folderId":null}}
HTTP_STATUS:201
```

呼叫端沒有送 `width`／`height`，回應裡卻已經讀出來了，上傳當下就會偵測。

請求本身的形式不對會被擋下，都是 `400`／`BAD_USER_INPUT`：

- 不是 multipart：`Expected multipart/form-data.`
- 沒有 `file` 這個分段：`Missing 'file' part.`
- `folderId` 不是合法 GUID：`Invalid 'folderId'.`
- `folderId` 指到一個不存在的資料夾：`Folder '{id}' does not exist.`（這是應用層的查找，不
  是資料庫外鍵）
- 空檔案：`Empty file.`

大小有兩道獨立的檢查。呼叫端自己宣告的 `Content-Length` 先被拿來跟
`Struo:Files:MaxUploadBytes` 比對（鍵本身見[第 4 章：設定](04-configuration.md)），超過是
`400`，`File exceeds the maximum size of {n} bytes.`。

呼叫端如果低報大小、實際又串進更多位元組，會被緩衝串流自己的同一道上限擋下，這次是
`413`／`PAYLOAD_TOO_LARGE`，同一句訊息，不會有一個被靜悄悄截斷的檔案。這道緩衝有 64 KB 的
記憶體門檻，超過就溢出到暫存檔，所以多個大檔案同時上傳，不會每一個都在記憶體裡各占滿一份
上限。

內容類型先過白名單 `Struo:Files:AllowedContentTypes`：留空代表不限制，也是編譯進去的預設
值；隨附的 `appsettings.json` 另外列了一份清單（完整內容見[第 4 章](04-configuration.md)），
fork 把這個鍵整段刪掉，就會回到不限制。不在清單裡的類型是 `400`，`Content type '{x}' is not
allowed.`。

過了白名單之後，宣告成 PNG、JPEG（`image/jpg` 也算）、GIF、WebP 或 PDF 的上傳，還會拿檔案開
頭的位元組再比對一次，不一致是 `400`，`File contents do not match the declared content type
'{x}'.`；沒有已知簽章的宣告類型（例如 `text/plain`）沒有東西可以比對，直接放行。

點陣尺寸的讀取只看容器標頭裡的位元組，不解碼任何像素：認得 PNG、GIF、WebP、JPEG，按開頭位元
組判斷，跟宣告的內容類型無關；其餘一律留白，`image/svg+xml` 也不例外。

儲存用的 key 是伺服器自己產生的，呼叫端從來不能指定：`{yyyy}/{MM}/{guid:N}{副檔名}`，用目
前的 UTC 年月加一個新的 GUID，原始檔名不會出現在儲存層的路徑上。

真正存下的 `size` 是緩衝串流實際讀到的位元組數，不是呼叫端宣告的 `Content-Length`——先把串
流整個讀完，再送去儲存，就是為了不讓一個謊報大小的呼叫端記錄錯的體積，或送出一個被截斷的
物件。

檔案列與它自動填好的翻譯列在同一筆交易裡一起寫入，不會出現有檔案卻沒有標題的中間狀態。

## 存到哪裡：local 與 s3

`Struo:Files:Backend` 只能選一種已註冊的 `IFileStorage`：`"local"`（預設）或 `"s3"`，其他值
會讓啟動直接失敗。兩種後端的鍵與驗證規則都在[第 4 章](04-configuration.md)；這裡只講行為上
的差異。

`local` 把檔案存成普通檔案，放在 `Struo:Files:Local:RootPath` 底下。每一次解析路徑都會檢查
有沒有落在根目錄之內，想逃出根目錄的 key 直接丟例外，不會被拿去做任何 I/O。

這個相對路徑是對著執行程序的工作目錄解析的，跟稍後會講到的圖片變體快取路徑不一樣——後者是
對著應用程式的內容根目錄解析。兩個設定看起來很像，行為卻不同：從別的目錄啟動 API，上傳的
檔案就會落到別的地方。

`local` 存檔時不理會內容類型，一律由 API 依資料庫裡記的 `contentType` 決定回應標頭，而且完
全不支援 presigned 網址。`s3` 則相反：存檔時會把驗證過的內容類型記成物件的 metadata，所以不
經過 API 的直接或 presigned `GET` 也能得到正確的 `Content-Type`，而且支援 presigned 網址。

`s3` 的 presigned `GET` 還會透過回應標頭覆寫，強制帶上 `Content-Disposition: attachment`，即
使是 API 自己的下載處置邏輯覆蓋不到的類型（例如 SVG），瀏覽器跟著重新導向之後還是會下載而不
是直接呈現。`UseHttp` 由設定的端點是不是以 `http://` 開頭決定，因為 SDK 產生 presigned 網址
預設用 `https`，一個只支援 http 的 MinIO 在重新導向之後會拒絕。

兩種後端遇到「這一列還在、位元組卻不見了」都丟同一種例外：`local` 是缺檔案或缺年月目錄，
`s3` 只認 `NoSuchKey` 這個錯誤碼，缺一整個 bucket 是設定錯誤，仍然是 `500`。不管哪一種，對不
上的 blob 對呼叫端來說都是乾淨的 `404`，不是 `500`。

## 提供檔案：誰看得到

`GET /api/files/{id}` 與 `GET /api/files/{id}/content` 完全沒有 `[Authorize]`，匿名請求兩者
都到得了：已發布檔案的中繼資料與位元組都可以嵌進公開頁面，不需要工作階段。

可見性採「不公開連結」模型：`published` 的檔案，任何持有其 id 的人都能讀取，id 就是唯一的秘密。
`status` 不是 `published` 的檔案，需要同時滿足兩件事：呼叫端已登入，而且對 `file` 有寫入授
權。沒有另一個「讀取未發布」的授權旗標，這條規則就是從每個集合都有的那三個既有旗標推出來
的。列出媒體庫（`GET /api/items/file`）是另一回事，需要一般的 `file` 讀取授權。

```text
$ GET /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9
{"success":false,"error":{"code":"NOT_FOUND","message":"Resource not found."}}
HTTP_STATUS:404
```

```text
$ GET /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9
{"success":true,"data":{"id":"27327d5a-6cb0-40e3-b288-6d1186db6ab9","fileName":"note.txt","contentType":"text/plain","size":19,"width":null,"height":null,"status":"draft","folderId":null}}
HTTP_STATUS:200
```

同一個 id，差別只在呼叫端是誰。門檻刻意設在寫入而不是讀取：`public` 是每個呼叫端的授權底
線，一旦 `file` 帶有公開讀取授權，`CanRead("file")` 對匿名與已登入的呼叫端都是真的，拿它當
門檻就形同虛設。草稿是一種編輯狀態，可以編輯檔案的呼叫端，才是應該看得到草稿的呼叫端。

隨附的種子資料只給 `public` 讀取授權，但框架本身沒有禁止某個超級管理員反過來替 `public` 開
寫入授權，這麼做會讓每一個已登入的呼叫端都看得到草稿（匿名仍然是 404）。

這道檢查失敗時，回應一律是單純的 `404`，不是 `403`，草稿是否存在不會被洩漏；一個已經丟進垃
圾桶的檔案，兩個讀取動作也都是 `404`，因為查詢本來就套用著同一道軟刪除過濾。

一次沒有轉檔的下載，是以附件的形式送出的：回應帶 `Content-Disposition`，裡面是原始檔名；成
功轉檔的回應則沒有這個標頭，只帶轉檔後的內容類型（轉檔失敗、退回原始位元組的情況見下文）。

`Struo:Files:PresignedRedirect` 開著、又沒有要求轉檔時，下載改成 `302` 轉址到儲存端的
presigned 網址；它預設關閉，好讓後台的縮圖不需要任何額外設定就能顯示，也讓儲存端點本身可以
保持私有。`local` 後端上這個設定完全沒有作用，presigned 查詢一律回 null，下載照樣落回串
流。每個回應都帶 `X-Content-Type-Options: nosniff`，是下載端點附件處置之外再加的一層防線。

### 把檔案設為私有

把檔案的 `status` 設成 `draft`。在管理介面中，到媒體庫開啟該檔案，關掉「已發布」開關後儲存；
用 API 的話，則是在一般的項目更新帶上 `status`：

```text
PUT /api/items/file/{id}
{"status":"draft","version":3}
```

這需要 `file` 的寫入授權，`version` 是一般的樂觀並行權杖。`published`、`draft` 以外的值一律
`400`。改回 `published`，任何持有 id 的人又能讀取。想讓每個新上傳一開始就是私有，把
`Struo:Files:DefaultStatus` 設成 `draft`。

品牌 Logo 必須是 `published`。設定頁拒絕儲存不是 `published` 的 Logo；已儲存的 Logo 之後被改成
`draft`，就不再解析：公開設定端點會退回預設 Logo。因此 `DefaultStatus` 設成 `draft` 時，從設定頁上傳的
Logo 必須先在媒體庫發布才能儲存，否則儲存會回 `400`。草稿對匿名頁面與唯讀的媒體庫使用者也是 `404`，
他們看到的縮圖與圖片會壞掉，直到檔案發布。

## 媒體資料夾

`MediaFolder` 是第二個框架自帶集合：一棵只拿來分類的樹，自我參照父層，跟儲存 key 完全脫
鉤，`Hidden` 的理由跟 `File` 一樣。資料夾的 CRUD 走一般的 `api/items/mediaFolder`，不經過
`FilesController`。刪除一個還有檔案或子資料夾的資料夾，會被普通的 `OnDelete.Restrict` 擋
下，不是另外寫的邏輯，回 `409`／`CONFLICT`，訊息是 `Cannot delete 'mediaFolder/{id}':
referenced by 'file'.`。

## 即時圖片轉換

轉換走的是下載端點本身，加上一組查詢參數：`GET /api/files/{id}/content?width=&height=
&format=&fit=&quality=`。只有三件事同時成立才會真的轉檔：功能本身開著（鍵是
`Struo:Files:ImageTransform:Enabled`，預設 `true`）、`width`／`height`／`format` 至少給了一
個，而且這一列的 `contentType` 是 `TransformableContentTypes` 之一（預設是 JPEG、PNG、
WebP、AVIF、GIF）。缺一個條件，請求就直接落回原本的串流或轉址，不受影響。SVG 是最常見的例子：
對 SVG 帶 `?width=50`，回的就是那份 SVG 本身。

### 參數

| 參數 | 值 | 超出範圍時 |
|---|---|---|
| `width` | 整數 | 夾進 1–`MaxWidth`，再往上對齊到尺寸階梯 |
| `height` | 整數 | 夾進 1–`MaxHeight`，再往上對齊到尺寸階梯 |
| `format` | `AllowedFormats` 之一 | 其他值 `400`；缺省退回 WebP，不會保留來源格式 |
| `fit` | `inside` 或 `cover` | 其他值 `400`；缺省是 `inside` |
| `quality` | 整數 | 四捨五入到 10 的倍數，範圍 10–100；缺省用 `DefaultQuality` |

`MaxWidth`、`MaxHeight`、`AllowedFormats`、`DefaultQuality`、`SizeSteps` 與其他轉換限制的鍵都
在 [第 4 章](04-configuration.md)。`format` 只有呼叫端明確給了值才會去比對 `AllowedFormats`，
比對時大小寫不拘，`?format=WEBP` 一樣算數；不在清單裡的值是 `400`，`Unsupported format
'{x}'.`。省略 `format` 時一律是 WebP，連 `Content-Type` 也一起變成 `image/webp`，不會沿用來
源檔案自己的編碼。

尺寸會對齊，讓呼叫端能製造出的變體數量維持在小範圍：預設階梯下 `width=100` 會得到 128 寬的圖，
`width=65` 與 `width=128` 共用同一份快取變體；超過最後一階就取最後一階。`fit=cover` 對齊的是寬度，
高度則依請求的 `width` 與 `height` 比例推算，所以裁切後仍維持被要求的比例；`width=300&height=200
&fit=cover` 會得到 320×216。推算出的高度會四捨五入到 8 像素的倍數，所以每個寬度階梯只有有限幾種
cover 高度。

`fit=cover` 只有寬高兩個維度都給，才會真的裁切成那個框；只給一個維度的 `cover` 是等比縮到框
內、不放大。`fit=inside` 則把給了的每一邊各自對齊。連寬高都沒給，只調格式或品質，轉換會跳過縮圖那一步，直接用
原本的解析度重新編碼，沒有框可以縮。只給 `height` 的請求，會先從標頭讀出來源寬度（同樣不解
碼像素），讓沒被限制的那一邊也能正確縮放。

縮圖交給 libvips：對支援載入時縮小的編碼（JPEG、WebP）會邊載入邊縮，解碼用的記憶體大致只跟
輸出尺寸成比例，而不是來源的完整解析度；PNG 沒有更便宜的解碼路徑，還是會完整解碼。

轉檔本身若丟出例外，來源損毀、編碼不受支援或 libvips 本身失敗都算，例外會以 Warning 等級記
錄，連同檔案 id，然後改送出原始位元組，請求仍然成功；退回的原始位元組是照一般下載的方式送
出的，所以帶的是這一列自己的內容類型與附件 `Content-Disposition`，轉檔失敗的回應從標頭上就
看得出來跟成功的不一樣。

解碼之前，會先從標頭讀出來源的寬與高。超過 `MaxSourcePixels` 的來源，或標頭讀不出來的來源，一律
送出原始位元組，所以標頭宣告了巨大影像的小檔案不會被解碼。GIF 的大小取其影格所需的畫布，AVIF 取
標頭宣告的最大影像尺寸。衍生的 AVIF 影像（網格 grid 或疊圖 overlay）一律原樣送出，不轉換。libvips 開啟來源時讀到的尺寸，會在解碼任何像素之前再對 `MaxSourcePixels` 檢查一次。呼叫端給的 `quality` 會四捨五入到 10 的倍數；設定的 `DefaultQuality` 則照寫的值使用。同時最多進行 `MaxConcurrentTransforms` 個
轉換；請求等超過 `QueueTimeoutSeconds` 秒還拿不到名額，就回 `503`，代碼是 `IMAGE_TRANSFORM_BUSY`，
並帶 `Retry-After` 標頭。

### 例子

```text
$ GET /api/files/c4440800-43d3-4ff0-9fe7-b5d7671b30df/content?width=5000&format=png
HTTP/1.1 200 OK
Content-Length: 394
Content-Type: image/png
# decoded PNG IHDR: width=64, height=48
# body: 394 bytes (binary, not shown)
HTTP_STATUS:200
```

`width=5000` 超過 `MaxWidth`，請求不會被拒絕，而是被夾到上限，但夾住的上限仍然比來源的
64×48 大，而縮圖本來就不放大，所以輸出就是來源自己的尺寸。

### 變體快取

轉出來的變體存在磁碟上，路徑是 `Struo:Files:ImageTransform:CachePath`（鍵見
[第 4 章](04-configuration.md)），相對路徑對著應用程式的內容根目錄解析，跟 `local` 的根目
錄不同。

快取鍵是一段帶標籤文字的 SHA-256，轉成小寫十六進位：檔案 id、這一列的 `version`，加上
`width`、`height`、`format`、`fit`、`quality`，各佔一行，取自對齊與四捨五入之後的請求。改動其中任何一項都會換一把鍵。版
本戳記用的是樂觀並行控制的 `version` 計數器，不是 `UpdatedAt`。這一列任何一次更新都會讓
`version` 前進，舊的變體就自然作廢，不需要任何機制主動失效。

每一筆快取項目按鍵的前兩個十六進位字元分到一個子目錄，寫入時先寫進同目錄的暫存檔，再搬移
過去，所以並發的讀取不會看到寫到一半的變體。讀取如果跟一次並發的寫入搶輸了，會被當成沒中快
取，不是錯誤。

快取容量由 `CacheMaxBytes` 設上限（預設 2 GB）。每次命中都會把該變體標成剛用過；寫入讓快取超過上限
時，會從最久沒被讀取的變體開始刪，直到快取降到上限的 90%。某個變體在另一個請求讀取它的時候被淘汰，會被當成
沒中快取，重新轉換一次。

## NetVips 與 LGPL

轉換管線建立在 NetVips 上，一個 MIT 授權的管理層封裝。StruoCMS 透過 NetVips 在執行期動態載
入 libvips，不靜態連結，也不直接連結。libvips 本身是 LGPL-2.1-or-later；因為連結是動態的，
LGPL-2.1 只要求重新標明授權聲明、不限制使用者改用另一個修改過的 libvips、並讓 libvips 自己
的原始碼可以取得，這件事上游本來就做到了——StruoCMS 沒有額外加上任何限制，也沒有內嵌 libvips
的原始碼，自己的 MIT 授權不受影響。

實際釘住的版本號記在 `Directory.Packages.props` 與 `THIRD-PARTY-NOTICES.md`，這裡不重複。
`NetVips.Native` 系列套件還一併打包了 libvips 幾個選用相依項目（mozjpeg、libpng、libwebp、
cairo、pango、librsvg 等），授權是 MIT、BSD 與 LGPLv3 的混合，各自記在 `NetVips.Native.*`
套件自己的聲明檔裡。

主機在註冊檔案服務時就會設定 `NetVips.BlockUntrusted`，所以即使停用圖片轉換，啟動時也會載入原生
libvips。沒有可用 libvips 的主機會在啟動時失敗，而不是等到第一次轉換才失敗。

## 檔案的垃圾桶

軟刪除的通用機制，包括底層的查詢過濾、`deleted=` 篩選、版本紀錄的互動，都在
[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)；這裡只講檔案獨有的部分。

預設的 `DELETE /api/files/{id}` 是丟進垃圾桶：跟其他可軟刪除集合用同一個原子的儲存庫操作，
blob 與翻譯列都留在原地，等著之後復原。如果被丟進垃圾桶的檔案剛好是目前的品牌標誌，同一筆
交易也會把那個參照清掉，公開的設定端點才不會解析出一個失效的網址。`POST
/api/files/{id}/restore` 會清掉刪除戳記，跟任何其他可軟刪除集合的復原完全一樣。

`DELETE` 與 `restore` 都要求 `file` 的刪除授權，不是寫入授權，沒有授權會得到 `Delete not
permitted.`。兩者成功都回 `204`，對象不存在回 `404`。只有 `DELETE`（丟進垃圾桶與永久刪除）會在其他項目仍透過
`OnDelete.Restrict` 關聯參照這個檔案時回 `409` `CONFLICT`；`restore` 不檢查參照。

`DELETE /api/files/{id}?purge=true` 才是真正的永久刪除：檔案列、翻譯列與儲存的 blob 全部一
起清掉。清除遵守跟其他集合一樣的關聯規則：`Restrict` 參照會擋下清除，`OnDelete.SetNull` 的參照會被設為
null，指向這個檔案的多對多連接資料列會被移除。blob 的清除是盡力而為，而且在交易之外進行，資料列已經沒了，
一次儲存層的失敗頂多留下孤兒位元組；[`files:sweep`](20-deployment.md#清除孤兒-blob)可以清掉這類 blob。

清除會用清掉軟刪除過濾的方式重新查這一列，所以先丟進垃圾桶、再清除（預設刪除本來就是先丟垃
圾桶）這條最常見的流程走得通。清除品牌標誌參照的方式跟丟進垃圾桶一樣。

只有 `true` 會觸發永久刪除，大小寫不拘；`?purge=1` 這種寫法不行：

```text
$ DELETE /api/files/27327d5a-6cb0-40e3-b288-6d1186db6ab9?purge=1
{"success":false,"error":{"code":"VALIDATION","message":"One or more validation errors occurred.","details":[{"field":"purge","message":"The value '1' is not valid."}]}}
HTTP_STATUS:400
```

`purge` 繫結成布林值，`1` 讓繫結本身失敗，`details` 指名這個欄位。正確的寫法是
`?purge=true`。

上傳、丟進垃圾桶、復原、清除這四條檔案自己的寫入路徑，各自會發出自己的寫入通知（見
[第 18 章：擴充點：搜尋提供者與寫入通知](18-extension-points.md)），因為 `FileService` 自己處理檔案的寫入。
一次清除只會回報一批通知：檔案本身是 `Purged`，參照被設為 null 的每個存活項目，以及失去連接資料列的每個
父項，都是 `Updated`。

項目 API（`DELETE /api/items/file/{id}`）與 GraphQL 的刪除檔案，會交給跟媒體庫同一條檔案生命週期，行為
完全一致，包括 `409`。

清掉品牌標誌參照不會另外算成一次站台設定的變更，回報的只有檔案本身這一筆。

## 接下來

檔案與媒體講完之後，下一步是認證怎麼運作，見[第 16 章：認證與 SSO](16-authentication.md)。
