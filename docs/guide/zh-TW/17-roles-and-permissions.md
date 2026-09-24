# 17. 角色與權限

通過驗證之後，呼叫端能對哪個集合做什麼、系統怎麼算出這個答案、以及要去哪裡改，是這一章的主題。
呼叫端怎麼證明自己是誰，見[第 16 章：認證與 SSO](16-authentication.md)。

## 三張表

RBAC 的資料模型落在三個集合上：使用者本身、角色，以及逐集合的授權。

| 集合 | 資料表 | 內容 |
|---|---|---|
| `user` | `struo_users` | 帳號本體：email、密碼雜湊、啟用狀態、存取權杖雜湊，加上角色 |
| `role` | `struo_roles` | 角色名稱、`isSuperAdmin` 旗標、描述 |
| `permission` | `struo_permissions` | 一列代表一個角色對一個集合的授權 |

`user` 的欄位裡，`password` 與 `accessToken` 都同時是 `Hidden` 又 `ReadOnly`，密碼雜湊與權杖雜湊
因此既不會出現在一般的查詢結果，也不能被呼叫端直接寫入。

使用者的角色是一個對 `role` 的 `TagSelect` 多對多，後台表單上就是勾選角色名稱；使用者與角色之間
的 junction 是第四張表 `userRole`（資料表 `struo_user_roles`），呼叫端看不到它。

`permission` 這一列除了指向哪個角色、哪個集合之外，只有三個授權旗標：`canRead`、`canWrite`、
`canDelete`，沒有第四個，也沒有另外存一個「可以讀取未發布內容」的旗標。角色本身另外帶一個
`isSuperAdmin`，它不是逐集合的授權，而是整條檢查鏈的短路開關。

## 有效權限怎麼算

因為每個請求都可能牽涉好幾個角色，實際套用的權限不是查一個角色就結束，而是每次請求都重新算一次。

`PermissionResolutionMiddleware` 在認證之後跑一次解析：先把呼叫端持有的角色，跟 `public` 這個
角色的授權聯集起來——不管呼叫端是匿名、沒有任何角色，還是本來就持有好幾個角色，`public` 的授權
永遠會被加進去，不是只在角色集合是空的時候才拿來墊底。

少了這一步，一個登入的使用者反而可能比匿名訪客讀得更少：只要 `public` 開了某個集合的讀取，而這
個使用者自己的角色都沒有重複授予，登入就成了倒退。這一段查詢直接讀資料，不經過一般的投影與權限
檢查，因為算出授權門檻的這道查詢本身，不能再被同一道門檻擋住。

聯集之後交給 `PermissionResolver.Resolve` 折算：同一個集合上，只要任何一個持有的角色開了
`canRead`／`canWrite`／`canDelete` 的其中一個，折算結果就是真，這是「或」不是「且」；比對集合名
稱不分大小寫。

角色裡只要有一個 `isSuperAdmin`，直接短路成每個集合三個旗標全部為真，不再看任何一列授權；一個
集合完全沒有授權列，則三個旗標全部視為假——這是安全的預設值，不是遺漏。

折算結果算出來之後，快取在這次請求範圍內，控制器與後續的每一個授權檢查都讀同一份，不會重算。整
條管線的順序是認證、授權、CSRF、權限解析、流量限制、控制器，權限解析永遠看得到已經通過認證的身
分。

## 讀、寫、刪

三個旗標各自守住哪些動作，對照[第 13 章：REST API 端點參考](13-rest-endpoints.md)每一列的權限欄
位就看得出來：

- `canRead` 守住這個集合的每一種讀取——清單、單筆 `GET`、版本紀錄的列表與單筆讀取、GraphQL 的讀
  取，以及跟著清單一起回來的 facet 與彙總。
- `canWrite` 守住通用路徑上的建立、更新與版本還原，另外也守住檔案上傳。
- `canDelete` 守住通用路徑上的刪除（丟進垃圾桶或清除）與復原，另外也守住檔案的刪除與復原。

要求 `deleted=` 非預設模式時，需要的是刪除授權，不是讀取授權；為什麼這道檢查刻意更嚴格，以及完整
的三種拼法與訊息，見[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)。

沒有獨立的「可讀取未發布內容」旗標，檔案是唯一的例外：未發布的檔案要同時滿足兩件事才讀得到——呼
叫端已經登入，而且對 `file` 有寫入授權；其餘一律 `404`，細節見
[第 15 章：檔案、媒體與圖片轉換](15-files-and-media.md)。

## `AdminOnly` 集合

框架自帶的集合裡，標成 `AdminOnly` 的就是 `user`、`role`、`permission`、`userRole` 這四個；屬性
本身的定義與怎麼在自己的集合上設，見[第 5 章：定義集合](05-collections.md)。

`AdminOnly` 只管寫入：通用 CRUD 路徑上的建立、更新、刪除、復原與版本還原，一律要求呼叫端是超級管
理員，不管這個集合的授權本身寫了什麼——就算某個角色被授予了 `canWrite`，只要不是超級管理員，一樣
會被擋下來。讀取不受影響，照一般的 `canRead` 走。

每個寫入點都先跑一般的集合授權檢查，超級管理員檢查緊接在後：建立、更新、版本還原檢查的是寫入授
權，失敗訊息是 `Write not permitted.`；刪除、復原檢查的是刪除授權，失敗訊息是
`Delete not permitted.`。

呼叫端在這個集合上完全沒有授權，看到的就是這兩句一般訊息；只有本來就持有相關授權、卻不是超級管
理員的呼叫端，才會看到專屬訊息 `Writes to '{collection}' require a super-admin.`，這句話因此只
對已經有資格寫、但少了超級管理員身分的人出現。

## 公開讀取

`struo_roles` 資料表第一次建立時會一併建立 `admin`、`public` 兩個角色，第一個管理者被加進 `admin`；
`public` 的授權是每個呼叫端的下限，怎麼被聯集進去見〈有效權限怎麼算〉。它本身怎麼被授予讀取，由設定
鍵 `Rbac:PublicReadCollections` 決定。

清單裡的每個集合名稱都變成 `public` 角色上的一筆 `canRead = true` 授權。這個鍵只在 `struo_roles` 資料
表第一次建立時套用，改了再重開對既有資料庫沒有追溯效果，鍵名與這個限制見
[第 4 章：設定參考](04-configuration.md)。

## `Hidden` 欄位與權限

欄位層級還有一個 `Hidden`，跟集合的 `AdminOnly` 是兩回事：屬性本身怎麼宣告、對後台表單有什麼影
響，見[第 6 章：欄位型別與編輯介面](06-field-types.md)；這裡只講它對權限的意義。

`Hidden` 的欄位會整個從查詢語法的欄位白名單裡消失——不是不投影出來就算了事，而是連拿這個欄位名稱去
篩選、排序都會被當成未知欄位擋下來，核心算給內建搜尋與搜尋提供者的可搜尋欄位清單（對提供者只是個
提示）也不會收錄它。少了這一步，一個雖然沒有被回傳、卻仍然可以拿來篩選的欄位，配上分頁的
`meta.total`，就能被一個字元一個字元地把內容猜出來，不投影本身擋不住這種手法。

`Hidden` 不是寫入的關卡。欄位覆蓋邏輯完全不檢查這個旗標，真正擋下呼叫端亂寫值的，是系統欄位／唯
讀欄位那一層檢查；`password` 與 `accessToken` 這兩個內建的 `Hidden` 欄位同時也宣告成 `ReadOnly`，
真正保護它們的是後者。自己的集合上想擋住敏感欄位，兩個旗標要一起設：只設 `Hidden`，欄位對猜得到
名字的呼叫端仍然可寫，而且值真的會被寫進去，只是讀不回來；同時宣告 `ReadOnly`，送進來的值才會被
系統／唯讀欄位的檢查悄悄丟掉，而不是被拒絕。

## 沒權限時你會看到什麼

權限檢查失敗只回兩種狀態碼：呼叫端沒有通過認證，回 `401`；已經通過認證、但沒有對應的授權，回
`403`。信封的形狀跟其他錯誤一樣，見[第 12 章：REST API 慣例](12-rest-conventions.md)。

以角色只被授予 `article` 讀取的 `editor@example.com` 為例，拿它去改一篇文章，送出的 body 在前，
回應在後：

```text
$ PUT /api/items/article/01a08f92-3a18-7c5d-99a3-5b14cd1279ea
body:
{"status":"draft"}
{"success":false,"error":{"code":"FORBIDDEN","message":"Write not permitted."}}
HTTP_STATUS:403
pretty:
{
  "success": false,
  "error": {
    "code": "FORBIDDEN",
    "message": "Write not permitted."
  }
}
```

同一個呼叫端讀一個自己沒有授權的集合（例如 `tag`），同樣是 `403`，訊息換成 `Read not permitted.`；完
全匿名送同一個請求會是 `401`；分辨的是這次請求有沒有通過認證，跟這個呼叫端原本能不能通過認證是兩件
事。

## 預覽有效權限

後台的使用者表單在儲存角色選擇之前，要能先看看這樣選的話，使用者實際上會拿到什麼權限——
`GET /api/users/{id}/effective-permissions` 就是給這個畫面用的，跟 `RolesController` 一樣，只有
超級管理員能呼叫，已登入但不是超級管理員的呼叫端一律 `403` `FORBIDDEN`、`Admin role required.`。

它走的是跟真正請求完全一樣的兩段解析：先聯集角色的授權，再折算，所以預覽算出來的答案，跟這個使
用者實際發出請求時算出來的答案，是同一套邏輯算出來的，不是另外維護的一份近似值；`public` 這層下
限，在假設的角色組合上一樣會被聯集進去。

`roles=` 這個查詢參數決定預覽的是誰的角色：

- 完全不帶這個參數，預覽的是這個使用者實際儲存的角色；
- 帶了但是空字串，預覽的是一個空的假設角色組合；
- 帶一個或多個角色 id，預覽的是那組假設的角色；
- 帶到查無此角色的 id，回 `400`、`Unknown role ids: …`；格式本身就不是合法 GUID，回 `400`、
  `Malformed role id: {value}`，是不同的訊息。

不管是哪一種，`public` 的授權都仍然會被聯集進去，不會因為換成假設的角色組合就少算這一層下限。分
辨完全不帶跟帶了空字串，讀的是原始查詢字串，兩者在一般的模型繫結下會變成同一個 null，但它們該預
覽的東西不一樣。使用者 id 本身不存在，回 `404`、`User not found.`，這個檢查在解析任何角色之前就
先跑。

超級管理員的結果固定是 `isSuperAdmin: true` 加上一個空的 `permissions`，因為每個集合的授權都已
經隱含為真，不需要再列一份逐集合的清單；`GET /api/auth/me` 對一個真正的超級管理員工作階段回的是
同一種形狀。非超級管理員的情況下，兩個介面都只列出至少有一個旗標為真的集合，完全沒有授權的集合
不會出現在 `permissions` 裡。

以角色只有 `article` 讀取的 `editor@example.com` 為例，先看不帶 `roles=` 的結果，再看換成一個超
級管理員角色的假設結果：

```text
$ GET /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/effective-permissions
{"success":true,"data":{"isSuperAdmin":false,"permissions":{"article":{"read":true,"write":false,"delete":false}}}}
HTTP_STATUS:200
pretty:
{
  "success": true,
  "data": {
    "isSuperAdmin": false,
    "permissions": {
      "article": {
        "read": true,
        "write": false,
        "delete": false
      }
    }
  }
}

$ GET /api/users/01a0a2e2-2751-74e9-9f41-0913880cba31/effective-permissions?roles=01a08f90-8c13-764d-84c1-11fd1380ac9a
{"success":true,"data":{"isSuperAdmin":true,"permissions":{}}}
HTTP_STATUS:200
pretty:
{
  "success": true,
  "data": {
    "isSuperAdmin": true,
    "permissions": {}
  }
}
```

第二段換成一個持有 `isSuperAdmin` 的角色 id，回應直接變成超級管理員的形狀，跟這個使用者實際儲存
的角色完全無關，因為那是假設，不是真的替換。

## 在後台管理角色

後台不會讓管理者直接編輯 `permission`／`userRole` 這兩個集合本身：`Hidden` 與 `AdminOnly` 是各自獨立
的旗標，一個集合可以只設其中一個，這兩個集合卻兩個都設，因為它們純粹是 RBAC 機制內部的資料，讓一般的
寫入授權就能碰到它們，正好是 `AdminOnly` 要擋住的那種自我升權。要改一個角色的授權，走的是角色權限矩
陣，逐集合勾選 `canRead`／`canWrite`／`canDelete`，一次送出就是整組覆蓋。使用者表單上的有效權限預覽
面板，則即時顯示目前選的角色組合換算出來的結果。

角色的授權整組寫在 `PUT /api/roles/{id}/permissions`，整組覆蓋、在同一個交易裡完成，端點本身的
語意見[第 13 章：REST API 端點參考](13-rest-endpoints.md)。被擋下時的訊息是：

- body 不是 JSON 陣列：`400`；
- 角色 id 不存在：`404`；
- 同一個集合出現兩次：`400`，`Duplicate collection entries: …`；
- 集合名稱不存在或空白：`400`，`Unknown collections: …`。

三個旗標都是假的列不會被存下來，等於直接刪掉那筆授權。這些改動都直接呼叫上面的端點，改完不需要
重開任何行程，下一次請求就照資料庫裡新的內容重新算。

## 接下來

角色與權限就談到這裡；下一章[第 18 章：擴充點：搜尋提供者與寫入通知](18-extension-points.md)談的
是框架故意留給 fork 自己接手的兩個位置。
