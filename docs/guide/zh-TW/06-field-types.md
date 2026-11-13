# 6. 欄位型別與編輯介面

要替一個欄位挑介面，又不確定它會存成什麼欄位型別、後台會長成什麼樣子時，答案都在這一章。
`[CmsField]` 的 `Interface` 值一次決定三件事：資料庫欄位、REST 與 GraphQL 公開的欄位資訊，
以及後台用哪個編輯器。

## 介面總覽

介面依用途分成七組，每組一張表。關聯不是欄位介面，宣告在 `[CmsRelation]` 上，見
[第 8 章：關聯](08-relations.md)。介面一定要自己寫，沒寫就是 `Text`（見
[第 5 章：定義集合](05-collections.md)）。哪些介面不能設為可翻譯，見
[第 7 章：多語內容](07-i18n.md)。

下面表格的「資料庫欄位」欄用幾種說法：

- `varchar(255)`：沒有指定長度的 `string`，`make:migration` 寫出 `AsString(255)`；SQL Server 上
  是 `nvarchar(255)`。
- 長文字：套用 `ColumnShape.LongText` 之後的結果，各後端有自己的對應型別。
- JSON：長文字加上 `IsJson = true`，整個 CLR 集合由 SqlSugar 直接序列化與還原。
- CLR 預設：數值、`bool`、`Guid` 照 FluentMigrator 與 SqlSugar 原本的對應，長度不會被動到；
  `DateTime` 各後端的型別見〈日期時間〉。
- `ColumnShape` 有三個成員：`LongText`、`TimestampWithTimeZone` 與 `Timestamp`，沒有數值形狀。

MySQL／MariaDB 上，文字欄位用的是資料庫的預設字元集，建資料庫時要指定 utf8mb4，見
[第 21 章〈各後端的差異〉](21-schema-and-upgrades.md#各後端的差異)。

### 文字

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Text` | `string` | `varchar(255)` | `TextField` |
| `Textarea` | `string` | 長文字 | `TextareaField` |
| `RichText` | `string` | 長文字 | `RichTextField` |
| `Markdown` | `string` | 長文字 | `TextareaField` |
| `Code` | `string` | 長文字 | `TextareaField` |
| `Slug` | `string` | `varchar(255)` | `TextField` |
| `Email` | `string` | `varchar(255)` | `TextField` |
| `Url` | `string` | `varchar(255)` | `TextField` |
| `Password` | `string` | `varchar(255)` | `TextField` |
| `Color` | `string` | `varchar(255)` | `TextField` |
| `Phone` | `string` | `varchar(255)` | `TextField` |

長文字介面是 `Textarea`、`RichText`、`Markdown`、`Code`，以及後面的 `Json`；其他 `string`
介面都停在 `varchar(255)`。欄位怎麼決定，見〈屬性怎麼決定欄位〉。除了 `Required` 與
`MaxLength` 之外，格式檢查只有 `Email` 與 `Url`，包含 `Phone` 在內的其他 `string` 介面接受任何
文字。

`Markdown` 與 `Code` 都是一般文字方塊，沒有預覽或語法標示。`Password` 的輸入不遮蔽，也不會出現在
GraphQL schema 裡。`Color` 同樣是純文字輸入，沒有色票選色器。

`Email` 與 `Url` 的值每次寫入都會檢查，不符的值回 `400` `BAD_USER_INPUT` 並指名該欄位。Email
必須剛好一個 `@`、前面有文字、網域裡有一個既不是第一個也不是最後一個字元的 `.`，且任何位置都
不能有空白：`Field '{name}' must be a valid email address.` Url 必須是帶主機、不含空白的絕對
`http` 或 `https` 網址：`Field '{name}' must be a valid http or https URL.` `null` 與空字串略過
檢查。`Required` 欄位裡只有空白的文字也略過，由 `Required` 回報為缺漏；其他欄位裡只有空白的
文字會被檢查，且不通過。關聯表的 payload 列不跑 `Required` 檢查，所以 `Required` 欄位的空值或只有
空白的值在那裡不會被拒絕。已存的值讀取時不會重新檢查，只有寫入送出該欄位時才檢查。需要存相對路徑
的欄位應該用 `Text`，而不是 `Url`。同樣的規則也適用於翻譯值，以及 `Repeater` 裡的 `Email` 與
`Url` 子欄位。

一個值同時違反多條規則時，先回報哪個錯誤取決於寫入路徑。root 欄位先回報 `MaxLength`，再回報格式，
`Required` 在兩者之後。翻譯值先回報所有語系的 `Required`，再回報格式，最後是 `MaxLength`。
`Repeater` 子欄位先回報 `Required`，再回報 `MaxLength`，最後是格式。

### 數字與布林

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Number` | `int`／`long`／`decimal`／`double` | CLR 預設 | `NumberField` |
| `Slider` | 數值 | CLR 預設 | `NumberField` |
| `Rating` | 數值 | CLR 預設 | `NumberField` |
| `Boolean` | `bool` | CLR 預設 | `BooleanField` |
| `Checkbox` | `bool` | CLR 預設 | `BooleanField` |

`Slider` 與 `Rating` 沿用 `Number` 的編輯器，後台沒有滑桿或星級元件。`Checkbox` 沿用
`Boolean` 的編輯器。

### 日期時間

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Date` | `DateTime`／`DateTime?` | CLR 預設 | `DateField` |
| `Time` | `DateTime`／`DateTime?` | CLR 預設 | `DateField` |
| `DateTime` | `DateTime`／`DateTime?` | CLR 預設 | `DateField` |

三者共用同一個編輯器，差別只在只顯示日期、只顯示時間、或兩者都顯示。要有時區的欄位，屬性另
外掛 `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`。

`DateTime` 屬性在各後端的欄位型別：

| 後端 | `DateTime` | 加上 `TimestampWithTimeZone` |
|---|---|---|
| PostgreSQL | `timestamp` | `timestamptz` |
| SQL Server | `datetime` | `datetimeoffset` |
| MySQL／MariaDB | `datetime(6)` | `datetime(6)` |
| SQLite | `datetime` | `timestamptz` |
| Oracle | FluentMigrator 預設，未驗證 | `timestamp with time zone` |

MySQL／MariaDB 上沒有形狀的 `DateTime` 就是 `ColumnShape.Timestamp`，保留到微秒，同一秒內的
兩次寫入才分得出先後；那裡的 `datetime` 只到整秒。其他後端明寫 `Timestamp` 也不會改變型別。
SQLite 不檢查型別名稱，`timestamptz` 照字面收下。

`TimestampWithTimeZone` 欄位存的是 UTC 時刻，不受 PostgreSQL 伺服器的 `TimeZone` 與 API 主機時區
影響。在 PostgreSQL 與 SQL Server 上，讀回的值是 `DateTimeKind.Utc`；寫入時 `Local` 的值先換成
UTC，`Unspecified` 的值當成 UTC。MySQL、MariaDB 與 SQLite 照原樣存下時鐘數字，寫入前要自己換成
UTC。

用比較運算子與 `_in`／`_nin` 篩選這些欄位時，值也當成時刻比較：帶 `Z` 或時差的值照它指的時刻，
沒有時區的值當成 UTC。`_contains` 這類文字比對照欄位的文字形式比。Oracle 上沒有這層處理，也未經
驗證。

### 選擇

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Select` | `string` | `varchar(255)` | `SelectField` |
| `MultiSelect` | `List<string>` | JSON | `MultiSelectField` |
| `Radio` | `string` | `varchar(255)` | `RadioField` |
| `CheckboxGroup` | `List<string>` | JSON | `CheckboxGroupField` |
| `Tags` | `List<TagItem>` | JSON | `TagsField` |

`Select`／`MultiSelect`／`Radio`／`CheckboxGroup` 要掛 `[CmsOptions]` 才有值可選；`Tags`
可以搭配 `[CmsOptions]`，但不是必要，通常留自由輸入。`[CmsOptions]` 的宣告方式見
[第 5 章：定義集合](05-collections.md)。寫入時，`MultiSelect`、`CheckboxGroup`、
`Select`、`Radio` 都會拿每個值對照選項清單。`Select` 或 `Radio` 的值必須與某個選項的值完全相同，
大小寫也要一致：`Field '{name}' value '{value}' is not in its options.` 頂層欄位、翻譯值與
`Repeater` 子欄位都適用。沒有 `[CmsOptions]` 的 `Select` 或 `Radio` 接受任意字串，`null` 與空字串
略過檢查。屬性型別不是 `string` 的 `Select` 或 `Radio` 欄位，不會拿去對照選項。

### 結構化 JSON

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Json` | `string`（原始 JSON 文字） | 長文字 | `JsonField` |
| `KeyValue` | `Dictionary<string,string>` | JSON | `KeyValueField` |
| `Repeater` | `List<TChild>` | JSON | `RepeaterField` |

`Json` 是一個裝著原始 JSON 文字的 `string` 屬性，API 層讀取時重新解析、寫入時重新序列化。
真正的 JSON 欄位是 `MultiSelect`、`CheckboxGroup`、`Tags`、`KeyValue`、`Files`、`Repeater`
這六個結構化介面，以及任何介面上的 `List<>` 屬性。SqlSugar 直接對 JSON 欄位（反）序列化整個
CLR 集合，`List<>` 屬性不必另外標 `IsJson`；`KeyValue` 的鍵不能留白。

`Repeater` 的 `TChild` 必須是類別而不是 `string`，且至少要宣告一個 `[CmsField]`，兩者違反都
是點名該欄位的啟動期 `MetadataException`。子欄位僅限純量介面：`Text`、`Textarea`、
`Markdown`、`Code`、`Slug`、`Email`、`Url`、`Color`、`Phone`、`Number`、`Slider`、
`Rating`、`Boolean`、`Checkbox`、`Date`、`Time`、`DateTime`、`Select`、`Radio`，且不能設為
可翻譯。以下都不允許：

- 巢狀 `Repeater`
- `RichText`、`Json`、`KeyValue`
- `File`、`Image`、`Files`
- `Password`、`Hidden`、`Uuid`、`Divider`

### 媒體與檔案

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `File` | `Guid`／`Guid?` | CLR 預設 | `FileField` |
| `Image` | `Guid`／`Guid?` | CLR 預設 | `FileField` |
| `Files` | `List<Guid>` | JSON | `FilesField` |

`File` 與 `Image` 共用同一個編輯器，`Image` 多了圖片預覽。`Files` 存的 id 在寫入時不會檢查
是否存在——指向已被清除檔案的 id 一樣會被存下來，後台選擇器只能退回顯示原始 id。

### 顯示用與識別碼

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Hidden` | 不限 | 依屬性型別 | `ReadonlyField` |
| `Divider` | 不限 | 依屬性型別，或無 | `DividerField` |
| `Uuid` | `Guid`／`Guid?` | CLR 預設 | `ReadonlyField` |

這裡的 `Hidden` 是介面本身，用來渲染一個唯讀的顯示元件，跟下一節的
`[CmsField(Hidden = true)]` 是兩回事——後者能掛在任何介面上。`Divider` 忽略欄位值，通常另外
掛 `[SugarColumn(IsIgnore = true)]`，讓它完全不落地成欄位。`Uuid` 只是唯讀顯示一個 GUID。

### 清單欄位與 GraphQL 的排除

一個集合的清單畫面最多顯示六欄，只從非系統、非隱藏、且介面本身有清單欄位格式化器的欄位裡
選，`DefaultDisplayField` 符合條件時排最前面，其餘照欄位在 API 回應裡的順序。沒有格式化器、
因此永遠不會出現在清單欄位裡的介面：`Password`、`Markdown`、`Code`、`RichText`、
`Divider`、`File`、`Image`、`Files`、`Hidden`、`Uuid`。只用這些介面的集合，清單畫面就是空
的；GraphQL schema 另外完全排除三個介面：`Hidden`、`Divider`、`Password`。

## 屬性怎麼決定欄位

屬性上的宣告，決定資料庫欄位和沒寫 `MaxLength` 時的預設輸入上限。下表一列是一種宣告方式。

| 宣告 | 建出的欄位 | 預設 `MaxLength` |
|---|---|---|
| 沒有特別宣告的 `string` | `varchar(255)` | 255 |
| 長文字介面上的 `string` | 長文字 | 不限 |
| JSON 介面 | 長文字，存 JSON | 不適用 |
| 任何介面上的 `List<>` 屬性 | 長文字，存 JSON | 不適用 |
| `[ColumnShape(ColumnShape.LongText)]` | 長文字 | 不限 |
| `[SugarColumn(Length = n)]` | `varchar(n)` | `n` |
| `[SugarColumn(ColumnDataType = "t", Length = n)]` | 型別 `t`，長度 `n` | `n` |
| `[SugarColumn(ColumnDataType = "t")]` | 型別 `t`，照字面 | 不限 |
| `[SugarColumn(IsJson = true)]` | 長文字，存 JSON | 不適用 |

表裡的用語：

- 長文字介面：`Textarea`、`RichText`、`Markdown`、`Code`、`Json`。
- JSON 介面：`MultiSelect`、`CheckboxGroup`、`Tags`、`KeyValue`、`Files`、`Repeater`。
- `varchar(255)` 與 `varchar(n)` 在 SQL Server 上是 `nvarchar`。
- 不限：沒有上限。不適用：屬性不是 `string`，不能設 `MaxLength`；同樣的宣告掛在 `string` 屬性上
  則是不限。
- 長文字介面上的 `Length` 不算，要搭配 `ColumnDataType` 才算（第七列）。

寫了 `ColumnDataType`，整個欄位定義由你負責，包含長度。寫在型別字串裡的長度，例如
`varchar(500)`，不會當成欄位寬度讀取；要寬度，另外設 `Length`。

### 啟動時被擋的宣告

下面的組合互相衝突。主機啟動時就失敗，丟出 `MetadataException`，訊息點名類別、屬性和兩個衝突
的宣告。每個環境都一樣，不用等到用到那張表。

| 衝突的宣告 | 怎麼改 |
|---|---|
| JSON 介面加 `[ColumnShape]` | 拿掉 `[ColumnShape]` |
| JSON 介面加 `ColumnDataType` | 拿掉 `ColumnDataType` |
| `IsJson = true` 加 `ColumnDataType` | 拿掉 `ColumnDataType` |
| `List<>` 屬性加 `ColumnDataType` | 拿掉 `ColumnDataType` |
| `[ColumnShape]` 加 `ColumnDataType` | 兩個只留一個 |
| `Nullable<T>` 或 `string?` 加 `IsNullable = false` | 屬性改成不可為 null，或拿掉 `IsNullable = false` |

JSON 欄位的型別由框架決定，不能用 `[ColumnShape]` 或 `ColumnDataType` 指定。

`Nullable<T>`（`Guid?`、`int?`、`DateTime?`）與 `string?` 一律得到可為 null 的欄位。要 NOT NULL，
就宣告成 `Guid`、`string`，不是 `Guid?`、`string?`。其他可為 null 的型別，例如 `List<string>?` 與
`byte[]?`，不在這條檢查裡。

檢查對象是集合、翻譯實體與 junction payload 的 `[CmsField]` 屬性。`[ColumnShape]` 加長文字介面
照常可用。`Repeater` 子欄位存在 JSON 裡，沒有自己的欄位，不檢查；掛
`[SugarColumn(IsIgnore = true)]` 的屬性也不檢查。

### 存長文字

用長文字介面，或寫 `[ColumnShape(ColumnShape.LongText)]`。不要寫
`[SugarColumn(ColumnDataType = "text")]`：`ColumnShape` 在每個後端都成立。

### 已建好的表要加寬

宣告加寬之後，已經建好、欄位比較窄的表，要另外寫一支 `Alter.Column` 的 migration。
`migrate:check` 把兩邊對不上報成錯誤。調低 `MaxLength`，或表的欄位本來就夠寬、只是補上
`[SugarColumn(Length = n)]`，不必寫 migration。

## `MaxLength` 的行為

`MaxLength` 以 UTF-16 code unit 計。後台輸入與寫入驗證都以它為準，超過回 400。後台把有效的值綁到
原生輸入的 `maxlength` 屬性上。

沒有明寫（或寫 0）時，預設值照上一節的表。預設跟介面無關：`Hidden`、`Time` 與宣告在 `string`
上的 `Uuid` 一樣是 255。要上限的「不限」欄位，在 `[CmsField]` 上設 `MaxLength`。

啟動時的檢查：

- 負值，或掛在非 `string` 屬性上：丟 `MetadataException`。
- 明寫的值大於欄位寬度：丟 `MetadataException`，訊息點名類別、屬性、`MaxLength` 與欄位寬度。
  等於或小於欄位寬度照常。
- 不檢查：沒有欄位寬度的屬性（表裡「不限」與「不適用」的列）、`Repeater` 子欄位、掛
  `[SugarColumn(IsIgnore = true)]` 的屬性。
- 範圍：集合、翻譯實體與 junction payload 的欄位。

要存比預設寬度更長的值，見〈存長文字〉與〈已建好的表要加寬〉。

## 必填、唯讀、隱藏與系統欄位

`Required` 在建立時一定要帶那個欄位；在更新時驗證的是合併後的 entity，不是請求 body 本身，細
節見[第 5 章：定義集合](05-collections.md)。

值是陣列或物件的集合型別，`Required` 的語意是「有帶值」：空的 `[]`、`{}` 也算有帶值而通過，前後
端一致。要求至少一筆，要由 fork 在寫入攔截器自行檢查。

`RichText` 的內容如果清理後在視覺上等於空白，會先被轉成 `null` 再檢查 `Required`，所以看起
來有內容的編輯器文件仍可能未通過必填檢查，`<img>`、`<hr>` 都算有內容。

建立時先報 `Required`；更新時如果 body 同時違反長度或結構驗證，先報的是那些錯誤。

`ReadOnly` 欄位讀得到，更新時寫不進去：更新的欄位覆蓋邏輯會跳過每一個 `ReadOnly` 與
`IsSystem` 欄位。建立時要真的鎖住，屬性必須可為 `null`——
`[CmsField(ReadOnly = true)] public int Views` 這種不可為 `null` 的值型別，建立時客戶端傳
什麼就存什麼，之後才唯讀。後台的輸入框也會停用。

四個稽核欄位由掃描器自動變成唯讀的系統欄位，介面依 CLR 型別挑——時間是 `DateTime`，使用者是
`Text`。新增時四個都由框架蓋印，更新時只重蓋 `UpdatedAt`／`UpdatedBy`，客戶端傳的值一律無
效。系統欄位讀取時照常回傳，只是不出現在後台的表單與清單欄位裡。

`Hidden`（`[CmsField(Hidden = true)]`）跟介面完全無關，任何介面都能設——例如範例的
`Article.InternalNote` 就是一個 `Hidden` 的 `Text` 欄位。

它會讓欄位從 `GET /api/schema`、GraphQL schema、項目回應、查詢語法的已知欄位允許清單與可搜尋白名單，
以及對外回傳的版本快照裡消失，連帶把它從集合的 `translation.fields` 清單裡也拿掉，原始快照列仍保留這
個值。它不是一個 RBAC 邊界：更新的欄位覆蓋邏輯完全不檢查 `Hidden`，已經知道欄位名稱的呼叫端仍能正常
寫入；要讓欄位真的不能寫，得跟 `ReadOnly` 搭配。

### 寫入時的整理

- 寫入 body 裡不認識的鍵會被直接丟掉、不會報錯，打錯欄位名照樣成功回應，只是值沒存進去。
- `MultiSelect`／`CheckboxGroup` 去重、保留第一個；`Tags` 空白值拒絕、重複丟掉。
- `Files` 丟掉 `Guid.Empty` 與重複；`Repeater` 丟掉整列空白的列，但錯誤訊息的列號仍以送進
  來的順序（含被丟掉的）從 1 起算。

## `RichText` 存下來的 HTML

`RichText` 欄位每一次經過 API 的寫入——不管是走後台編輯器、直接呼叫 REST／GraphQL，還是自己
寫的腳本送進來的 HTML——最後都只經過同一個伺服器端 sanitizer（`GanssHtmlSanitizer`）。標籤、
屬性、URL scheme 各自有一份白名單，不在名單裡的一律丟掉；丟一個標籤是連同它整個子樹一起丟，
不是只清掉標籤本身。編輯器這一側因此要顧的地方，見
[第 19 章：後台客製化](19-admin-customization.md)。

標籤白名單是基本格式加上 `h2` 起跳的標題、連結、圖片、簡單表格、`sub`／`sup` 與 `span`，沒
有 `h1` 也沒有 `div`；絕對網址只收 `http`、`https`、`mailto`，相對路徑（編輯器上傳的圖片）
照常留著，網址被擋下來時掉的是那個屬性，不是整個元素。

### 連結的 `target` 與 `rel`

`rel` 從來不是照輸入原樣存下來的，一律由伺服器端依 `target` 推導出來：

| 你送進去的 | 存下來的 |
|---|---|
| `<a href="…" target="_blank">` | `<a href="…" target="_blank" rel="noopener">` |
| 其他任何 `target` 寫法 | `<a href="…">` |

只有精確、區分大小寫的 `_blank` 才算數；`_Blank`、`_self`，或 `href` 本身就被丟掉的連結，
都拿不到 `target` 也拿不到 `rel`，兩個屬性一起消失。`target` 同時也在全域屬性白名單裡，但
只有 `<a>` 用得到它，非連結元素上的 `target` 一律被清掉，就算那個標籤本身在白名單裡也一
樣。

### 圖片的 `width` 與 `height`

`width` 只在 `<img>` 上留得住，而且整個值要符合 `\A[1-9][0-9]{0,4}\z` 這個規則——一
個 1 到 5 位數、沒有前導零的純像素整數，沒有單位、沒有百分比、沒有空白。其他元素上的
`width` 一律被清掉，就算是白名單裡的屬性也一樣。

`height` 完全沒有進白名單，不管來源是什麼都不會被存下來：寬高兩個維度一起送進只設
`max-width: 100%` 的版面，圖片反而會被壓扁。渲染 `RichText` 圖片時只設 `max-width: 100%`、
不去限制高度，單靠存下來的寬度就能等比例縮放。

兩條規則都是 `GanssHtmlSanitizer` 一個地方定的政策，不是不能改的限制——想改成允許
`noreferrer`、允許百分比寬度，或是連高度一起存，動的是這個 sanitizer 本身，不是編輯器的連
結對話框，也不是別處的白名單。表格 `<thead>` 的整理是同一個 sanitizer 做的另一件事，見
[第 19 章](19-admin-customization.md)。

## 新增自訂欄位編輯器

下面把 `Color` 介面預設的 `TextField` 換成一個原生 color input。每一個欄位編輯器元件都吃同
樣的三個 prop、發同一個事件：`field`（已解析的欄位 metadata）、`modelValue`（表單目前的值）、
可選的 `disabled`，變更時發回 `update:modelValue`：

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

**1. 寫元件**——`frontend/src/components/fields/ColorSwatchField.vue`： <!-- citation-guard:allow: 本段步驟要讀者自己建立的檔案 -->

```vue
<script setup lang="ts">
import { Input } from '@/components/ui/input'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// 原生 color input 使用「#rrggbb」格式；欄位仍為空值時回退到黑色。
function onPick(e: Event): void {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex items-center gap-2">
    <input
      type="color"
      :value="(modelValue as string) || '#000000'"
      :disabled="disabled"
      class="border-input h-9 w-12 shrink-0 cursor-pointer rounded-md border p-0.5"
      @input="onPick"
    />
    <Input
      :model-value="(modelValue as string)"
      :disabled="disabled"
      :maxlength="field.maxLength ?? undefined"
      @update:model-value="(v) => emit('update:modelValue', v)"
    />
  </div>
</template>
```

**2. 註冊它**——在 `frontend/src/lib/fieldTypes/registry.ts` 換掉 `color` 項目的元件，其他一
切（預設值、剖析、序列化、清單欄位格式化器）對一個純字串欄位仍然正確：

```ts
import ColorSwatchField from '../../components/fields/ColorSwatchField.vue'
// …
color: def({ component: ColorSwatchField, listColumn: asString }),
```

`FieldInput.vue` 依 `field.interface` 到 registry 拿元件，所有集合裡的 `Color` 欄位都跟著
換，不用逐集合接線。

**3. 看結果**——重啟前端開發伺服器，打開任何有 `Color` 欄位的項目，輸入框旁邊會多一個色票。

分派器 `FieldInput.vue` 另外轉發第四個綁定 `id`，用 fallthrough attribute 的方式傳下去，本
身不是任何編輯器自己宣告的 prop。根元素是原生控制項的編輯器會接住這個 `id`，跟呼叫端的
`<label>` 對上；根元素是包裝用 `div` 的編輯器則讓 `id` 落在那個 `div` 上，跟裡面的控制項對
不起來，需要自己處理標籤。

`frontend/src/lib/fieldTypes/types.ts` 的 `FieldInterface` 聯集是封閉的：新增一個真正全新的
介面值，而不是像上面那樣替換既有介面的編輯器，還得同步後端的 `FieldInterface` enum、前端的
聯集型別，以及 registry 的每一個地方，超出單純客製化編輯器的範圍。schema 合約測試雙向擋下
不同步：後端加了新成員前端沒跟上、或是前端留了過期項目，都會讓測試失敗。

schema 契約測試比對用的那份快照由一次帶 `UPDATE_SCHEMA_SNAPSHOT=1` 的後端測試重新產生，跑完一定要清
掉，否則之後不相關的測試會悄悄改寫快照，而不是檢查它。數值、布林與 `Uuid` 這類非字串欄位留白時送
`null`（`number`、`slider`、`rating`、`boolean`、`checkbox`、`uuid` 等）；文字類介面刻意送空字
串，清空文字欄位才會真的清空。

## 接下來

介面挑好之後，下一步是讓一個欄位在每種語言各存一份——這是
[第 7 章：多語內容](07-i18n.md)的主題。
