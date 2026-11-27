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
- JSON：內容是 JSON 的長文字欄位，整個 CLR 值由 SqlSugar 直接序列化與還原。
- CLR 預設：數值、`bool`、`Guid` 照 FluentMigrator 與 SqlSugar 原本的對應，長度不會被動到；
  `DateTime` 各後端的型別見〈日期時間〉。
- `ColumnShape` 有三個成員：`LongText`、`TimestampWithTimeZone` 與 `Timestamp`，沒有數值形狀。

本章的用語：

- 多值介面：`MultiSelect`、`CheckboxGroup`、`Tags`、`KeyValue`、`Files`、`Repeater` 這六個
  介面，值整個存成一個 JSON 欄位。
- 單一值型別：`string`、`bool`、`int`、`long`、`decimal`、`double`、`float`、`Guid`、
  `DateTime`、`byte[]`，以及前面這些值型別的 `Nullable<T>`。各介面收其中哪幾種，見下面各組的表。
- 結構化型別：不是基本型別、列舉、`string`、`decimal`、`Guid`、日期或時間型別、`byte[]` 的型別，
  例如 `List<>`、`HashSet<>`、`Dictionary<,>`、陣列、自訂類別與結構。
- 單一值介面：除了多值介面、`Hidden` 與 `Json` 以外的介面。

屬性型別要跟介面對得上，而且要完全相同：`MultiSelect` 收 `List<string>`，不收 `string[]` 或
`IList<string>`。對不上的宣告在主機啟動時就被擋下，見[啟動時被擋的宣告](#啟動時被擋的宣告)。

MySQL／MariaDB 上，文字欄位用的是資料庫的預設字元集，建資料庫時要指定 utf8mb4，見
[第 21 章〈各後端的差異〉](21-schema-and-upgrades.md#各後端的差異)。

### 文字

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Text` | `string`／`Guid`／`Guid?` | `varchar(255)`；`Guid` 為 CLR 預設 | `TextField` |
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
文字會被檢查，且不通過。關聯表的 payload 列不跑 `Required` 檢查，所以 `Required` 欄位的空字串或只有
空白的文字在那裡不會被拒絕；送給不可為 null 的資料庫欄位的 `null` 則會被拒絕，見
[第 8 章](08-relations.md)。已存的值讀取時不會重新檢查，只有寫入送出該欄位時才檢查。需要存相對路徑
的欄位應該用 `Text`，而不是 `Url`。同樣的規則也適用於翻譯值，以及 `Repeater` 裡的 `Email` 與
`Url` 子欄位。

一個值同時違反多條規則時，先回報哪個錯誤取決於寫入路徑。root 欄位先回報 `MaxLength`，再回報格式，
`Required` 在兩者之後。翻譯值先回報所有語系的 `Required`，再回報格式，最後是 `MaxLength`。
`Repeater` 子欄位先回報 `Required`，再回報 `MaxLength`，最後是格式。

### 數字與布林

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Number` | `int`／`long`／`decimal`／`double`／`float` | CLR 預設 | `NumberField` |
| `Slider` | 同 `Number` | CLR 預設 | `NumberField` |
| `Rating` | 同 `Number` | CLR 預設 | `NumberField` |
| `Boolean` | `bool`／`bool?` | CLR 預設 | `BooleanField` |
| `Checkbox` | `bool`／`bool?` | CLR 預設 | `BooleanField` |

數值型別的可為 null 形式（`int?`、`decimal?` 等）一樣收。`short`、`byte`、無號整數不收，要用
`int` 或 `long`。

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

用比較運算子與 `_in`／`_nin` 篩選這些欄位時，值也當成時刻比較：帶 `Z` 或時差的值照它指的時刻，沒有時
區的值當成 UTC。`_contains`、`_starts_with`、`_ends_with` 用在這種欄位上回 400；要比對某一天，改用
`_gte` 加 `_lt` 的範圍。Oracle 上沒有這層處理，也未經驗證。

### 選擇

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Select` | `string` | `varchar(255)` | `SelectField` |
| `MultiSelect` | `List<string>` | JSON | `MultiSelectField` |
| `Radio` | `string` | `varchar(255)` | `RadioField` |
| `CheckboxGroup` | `List<string>` | JSON | `CheckboxGroupField` |
| `Tags` | `List<TagItem>` | JSON | `TagsField` |

`MultiSelect` 與 `CheckboxGroup` 一定要掛有項目的 `[CmsOptions]`，沒掛、或掛了卻一個項目都沒有，主機
啟動都失敗，因為沒有選項就會拒絕每一個值。`Select` 與 `Radio` 掛了 `[CmsOptions]` 才有值可選；`Tags`
可以搭配 `[CmsOptions]`，但不是必要，通常留自由輸入。`[CmsOptions]` 的宣告方式見
[第 5 章：定義集合](05-collections.md)。寫入時，`MultiSelect`、`CheckboxGroup`、`Select`、`Radio` 都
會拿每個值對照選項清單。`Select` 或 `Radio` 的值必須與某個選項的值完全相同，大小寫也要一致：
`Field '{name}' value '{value}' is not in its options.` 頂層欄位、翻譯值與 `Repeater` 子欄位都適用。
沒有 `[CmsOptions]` 的 `Select` 或 `Radio` 接受任意字串，`null` 與空字串略過檢查。要用一組固定的值，
宣告成 `string`，再用 `[CmsOptions]` 列出值；列舉型別的屬性不收。

### `Json`、`KeyValue` 與 `Repeater`

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Json` | `string`（原始 JSON 文字） | 長文字 | `JsonField` |
| `KeyValue` | `Dictionary<string,string>` | JSON | `KeyValueField` |
| `Repeater` | `List<TChild>` | JSON | `RepeaterField` |

`Json` 是一個裝著原始 JSON 文字的 `string` 屬性，API 層讀取時重新解析、寫入時重新序列化。
真正的 JSON 欄位來自多值介面，以及 `Hidden` 上的結構化型別屬性。SqlSugar 直接對 JSON 欄位
（反）序列化整個 CLR 值，這些屬性不必另外標 `IsJson`；`KeyValue` 的鍵不能留白。

結構化型別的屬性只能放在多值介面或 `Hidden`，放在其他介面，主機啟動時就失敗；`Json` 介面的
屬性則必須是 `string`。型別是 `object`、型別裡含有 `object`，或元素沒有型別的屬性，任何介面都不
收。規則見[啟動時被擋的宣告](#啟動時被擋的宣告)。

`Repeater` 的 `TChild` 必須是類別而不是 `string`，且至少要宣告一個 `[CmsField]`，兩者違反都
是點名該欄位的啟動期 `MetadataException`。可寫的子欄位要有 public 的 setter（`set` 或 `init`），
或由 `TChild` 唯一的 public 建構式裡同名、同型別的參數設值；兩者都沒有時啟動就失敗。
標了 `ReadOnly = true` 的子欄位必須是請求寫不進去的：沒有 public 的 setter、沒有對應的建構式參數、
也沒有 `[JsonInclude]`。請求仍寫得進去的會在啟動時被擋下，因為送來的值照樣會存進去。
子欄位不能設為可翻譯，屬性型別照各介面的表檢查，
也只能用下列介面，範圍比單一值介面窄：`Text`、`Textarea`、`Markdown`、`Code`、`Slug`、`Email`、
`Url`、`Color`、`Phone`、`Number`、`Slider`、`Rating`、`Boolean`、`Checkbox`、`Date`、`Time`、
`DateTime`、`Select`、`Radio`。不允許的介面有：

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
| `Hidden` | 單一值或結構化型別，裡面沒有 `object` | 依屬性型別；結構化型別存成 JSON | `ReadonlyField` |
| `Divider` | 單一值型別，或掛 `IsIgnore = true` 的任何型別 | 依屬性型別，`IsIgnore` 則無 | `DividerField` |
| `Uuid` | `Guid`／`Guid?` | CLR 預設 | `ReadonlyField` |

`Hidden` 收的單一值型別有 `string`、數值型別、`bool`、`DateTime`、`Guid`、它們的可為 null 形式與
`byte[]`。`Divider` 收同一批，但不含 `byte[]`。

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
| 多值介面 | 長文字，存 JSON | 不適用 |
| `Hidden` 上的結構化型別屬性 | 長文字，存 JSON | 不適用 |
| `[ColumnShape(ColumnShape.LongText)]` | 長文字 | 不限 |
| `[SugarColumn(Length = n)]` | `varchar(n)` | `n` |
| `[SugarColumn(ColumnDataType = "t", Length = n)]` | 型別 `t`，長度 `n` | `n` |
| `[SugarColumn(ColumnDataType = "t")]` | 型別 `t`，照字面 | 不限 |
| 結構化型別屬性加 `[SugarColumn(IsJson = true)]` | 長文字，存 JSON | 不適用 |

表裡的多值介面與結構化型別，定義見[介面總覽](#介面總覽)。其他用語：

- 長文字介面：`Textarea`、`RichText`、`Markdown`、`Code`、`Json`。
- `varchar(255)` 與 `varchar(n)` 在 SQL Server 上是 `nvarchar`。
- 不限：沒有上限。不適用：屬性不是 `string`，不能設 `MaxLength`；同樣的宣告掛在 `string` 屬性上
  則是不限。
- 長文字介面上的 `Length` 不算，要搭配 `ColumnDataType` 才算，也就是
  `[SugarColumn(ColumnDataType = "t", Length = n)]` 那一列。

寫了 `ColumnDataType`，整個欄位定義由你負責，包含長度。寫在型別字串裡的長度，例如
`varchar(500)`，不會當成欄位寬度讀取；要寬度，另外設 `Length`。

### 啟動時被擋的宣告

下面的宣告互相衝突，或屬性型別跟介面、欄位形狀對不上。主機啟動時就失敗，丟出
`MetadataException`，訊息點名類別、屬性和有問題的宣告。每個環境都一樣，不用等到用到那張表。

| 衝突的宣告 | 怎麼改 |
|---|---|
| 多值介面加 `[ColumnShape]` | 拿掉 `[ColumnShape]` |
| 多值介面加 `ColumnDataType` | 拿掉 `ColumnDataType` |
| `IsJson = true` 加 `ColumnDataType` | 拿掉 `ColumnDataType` |
| `IsJson = true` 加 `[ColumnShape]` | 拿掉 `[ColumnShape]` |
| 結構化型別屬性加 `ColumnDataType` | 拿掉 `ColumnDataType` |
| 結構化型別屬性加 `[ColumnShape]` | 拿掉 `[ColumnShape]` |
| 單一值介面上的結構化型別屬性 | 改用多值介面或 `Hidden` |
| `Json` 介面上不是 `string` 的屬性 | 屬性改宣告成 `string` |
| `object`，或含有 `object` 或無型別元素的型別 | 改宣告成具體型別 |
| `[ColumnShape]` 加 `ColumnDataType` | 兩個只留一個 |
| `Nullable<T>` 或 `string?` 加 `IsNullable = false` | 屬性改成不可為 null，或拿掉 `IsNullable = false` |
| 屬性型別不是該介面收的型別 | 改宣告成該介面收的型別，或換介面 |
| 沒有任何介面收的屬性型別 | 見〈沒有介面收的型別〉 |
| `ColumnDataType` 與屬性型別衝突 | 見〈與屬性型別衝突的欄位型別〉 |
| 單一值型別屬性加 `IsJson = true` | 拿掉 `IsJson` |
| `[ColumnShape(LongText)]` 在不是 `string` 的屬性上 | 拿掉，或屬性改成 `string` |
| `Timestamp`、`TimestampWithTimeZone` 在不是 `DateTime` 的屬性上 | 拿掉，或屬性改成 `DateTime` |
| `MultiSelect`、`CheckboxGroup` 沒有 `[CmsOptions]` | 加上 `[CmsOptions]` 列出可選的值 |

JSON 欄位的型別由框架決定，不能用 `[ColumnShape]` 或 `ColumnDataType` 指定。結構化型別屬性存成
JSON 欄位，同樣如此。

單一值介面上的結構化型別屬性，有沒有寫 `IsJson` 都被擋，要改成宣告單一值型別的屬性，或改用
多值介面或 `Hidden`。`Json` 介面存的是原始 JSON 文字，所以屬性要宣告成 `string`。反過來，單一值
型別的屬性不能標 `IsJson`，任何介面都一樣；結構化型別的屬性放在 `Hidden` 或多值介面，不必標。

`[ColumnShape]` 的三個成員各有適用的型別：`LongText` 只能用在 `string`，`Timestamp` 與
`TimestampWithTimeZone` 只能用在 `DateTime` 與 `DateTime?`。

每個介面收哪些屬性型別，列在〈介面總覽〉各組的表。型別要完全相同，所以 `List<string>` 放在
`Tags` 上被擋，`Tags` 要宣告成 `List<TagItem>`；`string[]` 或 `IList<string>` 也不能代替
`List<string>`。訊息指出屬性的型別、介面收什麼，以及哪些介面收這個型別。

### 與屬性型別衝突的欄位型別

下表的 `ColumnDataType` 同樣讓主機啟動失敗。`Nullable<T>` 當作 `T`。不在表裡的名稱，包含
`timestamp with local time zone`，是你自己的，照常通過。

| 屬性型別 | `ColumnDataType` | 改成 |
|---|---|---|
| `DateTime` | `timestamptz`、`timestamp with time zone`、`datetimeoffset` | 用欄位形狀，拿掉 `ColumnDataType` |
| `int` | `bigint` | 屬性改 `long`，或拿掉 `ColumnDataType` |
| `int` | `smallint` | 拿掉 `ColumnDataType`，或改用 `int` |
| `long` | `int`、`integer` | 屬性改 `int`，或拿掉 `ColumnDataType` |
| `long` | `smallint` | 拿掉 `ColumnDataType`，或改用 `bigint` |
| `int`、`long` | `tinyint(1)` | 寫不帶長度的 `tinyint`，或拿掉 `ColumnDataType` |
| `decimal` | `real`、`float`、`float4`、`float8` | 改用 `decimal(18,4)`，或拿掉 `ColumnDataType` |
| `decimal` | `double`、`double precision` | 改用 `decimal(18,4)`，或拿掉 `ColumnDataType` |
| `decimal` | `decimal`、`numeric`、`dec`、`fixed`，後面沒有括號 | 寫明精度與小數位數，或拿掉 `ColumnDataType` |
| `bool` | `varchar`、`nvarchar`、`char`、`nchar` | 拿掉 `ColumnDataType`，或改用 `boolean` 或 `bit` |
| `bool` | `character`、`character varying`、`bpchar` | 拿掉 `ColumnDataType`，或改用 `boolean` 或 `bit` |
| `bool` | `text`、`ntext`、`tinytext`、`mediumtext`、`longtext` | 拿掉 `ColumnDataType`，或改用 `boolean` 或 `bit` |

比對型別名稱時看的是各個單字，不分大小寫：

- 括號裡的長度或精度不看，有兩個例外：`tinyint` 要帶 `(1)` 才在表裡；`decimal`、`numeric`、
  `dec`、`fixed` 要沒有括號才在表裡。
- 整個單字被中括號、雙引號或反引號包起來時算一個單字，所以 `[bigint]` 就是 `bigint`，
  `"int custom"` 不是 `int`。
- `double precision` 算 `double`，`character varying` 算 `character`。
- 名稱後面的字依下表判斷。

| 名稱後面的字 | 接在哪些名稱後面 | 怎麼算 |
|---|---|---|
| `unsigned`、`zerofill` | `real`、`float`、`double` | 欄位修飾：`float unsigned` 當作 `float` |
| `unsigned`、`zerofill` | `decimal`、`numeric`、`dec`、`fixed` | 欄位修飾 |
| `unsigned`、`zerofill` | 其他名稱 | 另一種型別：`int unsigned` 不是 `int` |
| `array`，或 `int[]` 這樣的中括號 | 任何名稱 | 另一種型別 |
| 其他字，例如 `not null` | 任何名稱 | 欄位修飾：`bigint not null` 當作 `bigint` |

`DateTime` 要用的形狀是 `[ColumnShape(ColumnShape.TimestampWithTimeZone)]`（見
[日期時間](#日期時間)）。在 PostgreSQL 與 SQL Server 上，形狀讓欄位以 UTC 時刻讀寫。沒有形狀時，
資料庫拿的是值的時鐘數字：PostgreSQL 依伺服器的時區解讀，SQL Server 當作 UTC。以下是在
PostgreSQL 17 與 SQL Server 2022 上量到的，主機分別在 UTC+8 與 UTC：

- PostgreSQL 的伺服器時區不是 UTC 時（用 `Asia/Taipei` 量），`Kind = Utc` 的值存在錯的時刻：
  UTC 05:06 存成前一天的 UTC 21:06。JSON 請求本文裡的日期時間讀成 `Kind = Utc`，所以只用 API
  就會發生。`Kind = Unspecified` 的值存法相同。伺服器時區是 UTC 時，以及在 SQL Server 上，
  兩者都存得正確。
- `Kind = Local` 的值，由 hook、interceptor、seed 這類伺服器端程式碼寫入，在 SQL Server 上與
  伺服器時區為 UTC 的 PostgreSQL 上，存進去會差主機的 UTC 位移。主機在 UTC+8 時，本地 13:06
  （UTC 05:06）存成 UTC 13:06。
- PostgreSQL 每次讀取都把這個欄位回傳成 `Kind = Local` 的值，不論資料列是誰寫的；有形狀的
  欄位回傳 `Kind = Utc`。程式碼拿讀到的值跟 `DateTime.UtcNow` 比較，會得到錯的答案。
- 在 SQL Server 上，同一個程序裡對這個欄位與對不帶時區的 `DateTime` 欄位做 facet，兩個順序
  都會讓對方以綁定錯誤失敗。

只有第二條需要伺服器端程式碼來寫入值；其他幾條，值經 API 寫入也會發生。

整數那幾列，列的是 SQL Server 有的整數型別名稱，而且 ORM 的讀取快取把它們當成同一個項目：
`bigint`、`int`（同義詞 `integer`）與 `smallint`。整數屬性放在這幾種寬度中另一種的欄位上，
在 SQL Server 上，同一個程序裡若先對屬性自己寬度的欄位做過 facet，這個欄位的 facet 就會失敗；
讀取與篩選正常。檢查在每個後端都一樣，雖然只有 SQL Server 會失敗。`tinyint` 讀成另一種型別，
`mediumint`、`int2`、`int4`、`int8` 在 SQL Server 上不存在，所以都通過。

MySQL 驅動程式把 `tinyint(1)` 欄位讀成布林值，MySQL 與 MariaDB 都一樣：存了 7 的 `int` 或
`long` 屬性，讀取與 facet 都得到 1，不過篩選 7 仍然比得到那一列。寫不帶長度的 `tinyint`。

`decimal` 放在二進位浮點欄位上，欄位會把值捨入。在 PostgreSQL 17、SQL Server 2022、MySQL 8.4
與 MariaDB 11.8 上量到：單精度型別（PostgreSQL 與 SQL Server 的 `real`，MySQL 與 MariaDB 的
`float`）把 12345.6789 讀成 12345.68 或 12345.7；雙精度型別把 12345678901234.5678 讀成
12345678901234.6。篩選的筆數、facet 的分組與加總也跟著錯。不寫 `ColumnDataType` 時，
`decimal` 屬性的欄位在 SQL Server、MySQL 與 MariaDB 是 `decimal(18,4)`，在 PostgreSQL 是不限
精度的 `numeric`。

在 SQLite 上，`decimal` 欄位不論怎麼宣告，包含不寫 `ColumnDataType` 與 `decimal(18,4)`，有效數字
超過 15 位的值讀回時都捨入成 15 位：12345678901234.5678 讀回 12345678901234.6，這類值的篩選與
加總也跟著錯。

`decimal`、`numeric`、`dec`、`fixed` 沒寫精度與小數位數時，在 MySQL 與 MariaDB 上建出
`decimal(10,0)`；在 SQL Server 上，`numeric` 與 `dec` 建出小數位數為 0 的型別。12345.6789 讀回
12346，小數部分整個丟掉。PostgreSQL 的 `decimal`、`numeric`、`dec` 與 SQL Server 的 `decimal`
保留小數，不過檢查在每個後端都一樣。要明寫型別時，寫明精度與小數位數，例如 `decimal(18,4)`：
帶括號的名稱照寫的內容建欄位，照常通過。`unsigned`、`zerofill` 接在後面也一樣。

`bool` 放在字串欄位上，值存成 `0` 與 `1`（PostgreSQL 存成 `false` 與 `true`）。資料庫肯收這個
寫入時，讀取該列與對欄位做 facet 都會失敗，在 SQLite、PostgreSQL、SQL Server、MySQL 與
MariaDB 都一樣；在 PostgreSQL 上篩選也失敗。要明寫型別時，PostgreSQL、MySQL 與 MariaDB 寫
`boolean`，SQL Server 寫 `bit`；不寫 `ColumnDataType` 時，框架依後端挑選布林型別。

### 沒有介面收的型別

下面的型別，包含它們的 `Nullable<T>`，沒有任何介面收，宣告成這些型別的 `[CmsField]` 屬性都被擋：

| 型別 | 改用 |
|---|---|
| `DateOnly`、`DateTimeOffset` | `DateTime`，介面用 `Date`、`Time` 或 `DateTime` |
| `TimeOnly`、`TimeSpan` | `DateTime`（`Time`），或存文字的 `string`（`Text`） |
| `char` | `string` |
| 列舉 | `string`，介面用 `Select` 或 `Radio`，值列在 `[CmsOptions]` |
| `short`、`byte`、無號整數 | `int` 或 `long`（`Number`） |

`object` 讀回來的是 JSON 節點，不是寫入的值，所以包含多值介面與 `Hidden` 在內，任何介面都不收。
被擋的不只型別本身是 `object` 的屬性：`object` 出現在型別參數（`Dictionary<string, object>`、
`List<object>`）、陣列元素型別，或自訂類別與結構的公開屬性（不論有沒有 `[CmsField]`，也包含
`Repeater` 子型別的屬性，層數不限）時同樣被擋。非泛型容器（`ArrayList`、`Hashtable`、`IList`，以及
實作 `IEnumerable` 卻沒實作 `IEnumerable<T>` 的型別）的元素沒有型別，同樣被擋；`string` 與
`byte[]` 不在此限。訊息指出 `object` 或無型別容器在哪裡。

`Nullable<T>`（`Guid?`、`int?`、`DateTime?`）與 `string?` 一律得到可為 null 的欄位。要 NOT NULL，
就宣告成 `Guid`、`string`，不是 `Guid?`、`string?`。其他可為 null 的型別，例如 `List<string>?` 與
`byte[]?`，不在這條檢查裡。

檢查對象是集合、翻譯實體與 junction payload 的 `[CmsField]` 屬性。`[ColumnShape]` 加長文字介面照常可
用。`Repeater` 子欄位存在 JSON 裡，沒有自己的欄位，`[ColumnShape]`、`ColumnDataType` 這類欄位宣告的
檢查略過它；屬性型別是否對得上介面、`object` 與無型別容器的檢查，子欄位照做。掛
`[SugarColumn(IsIgnore = true)]` 的屬性不做這些宣告檢查，`Repeater` 屬性掛了它，子欄位的型別也不
檢查；GraphQL 要求 `Repeater` 有可寫的子欄位，不看這個旗標，見[第 5 章](05-collections.md)。

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

沒有明寫（或寫 0）時，預設值照上一節的表。預設跟介面無關：`Hidden` 上的 `string` 跟 `Text` 一樣是
255。要上限的「不限」欄位，在 `[CmsField]` 上設 `MaxLength`。

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

值是陣列或物件的欄位，`Required` 的語意是「有帶值」：空的 `[]`、`{}` 也算有帶值而通過，前後
端一致。要求至少一筆，要由 fork 在寫入攔截器自行檢查。

`RichText` 的內容如果清理後在視覺上等於空白，會先被轉成 `null` 再檢查 `Required`，所以看起
來有內容的編輯器文件仍可能未通過必填檢查，`<img>`、`<hr>` 都算有內容。

建立時先報 `Required`；更新時如果 body 同時違反長度或結構驗證，先報的是那些錯誤。

集合類別的 `ReadOnly` 欄位讀得到、寫不進去：建立、更新與還原時，送給它的鍵都會被丟掉，欄位同時是
多對一關聯的外鍵時也一樣，GraphQL 的建立與更新 input 也不列這個鍵。建立時存進去的是類別給這個
屬性的值——`public string Source { get; set; } = "import";` 存的是 `import`，沒有初始值的屬性
存該型別的預設值——除非寫入攔截器另外設定。後台的輸入框也會停用。

四個稽核欄位由掃描器自動變成唯讀的系統欄位，介面依 CLR 型別挑——時間是 `DateTime`，使用者是
`Text`。新增時四個都由框架蓋印，更新時只重蓋 `UpdatedAt`／`UpdatedBy`，客戶端傳的值一律無
效。系統欄位讀取時照常回傳，只是不出現在後台的表單與清單欄位裡。

`Hidden`（`[CmsField(Hidden = true)]`）跟介面完全無關，任何介面都能設——例如範例的
`Article.InternalNote` 就是一個 `Hidden` 的 `Text` 欄位。

它會讓欄位從 `GET /api/schema`、GraphQL schema、項目回應、查詢語法的已知欄位允許清單與可搜尋白名單，
以及對外回傳的版本快照裡消失，連帶把它從集合的 `translation.fields` 清單裡也拿掉，原始快照列仍保留這
個值。

隱藏欄位同時是多對一關聯的外鍵時，也不能用名稱指到它：GraphQL 的建立、更新與篩選輸入不列它，
`filter`、`sort`、`fields`、`facets`、`aggregate` 對它回 `Unknown field`。關聯本身不受影響
（`deep`、經 `relation.field` 的篩選與排序、對關聯做 facet），`GET /api/schema` 的關聯資訊裡
`foreignKey` 仍寫著這個鍵的名稱。

它不是一個 RBAC 邊界：更新的欄位覆蓋邏輯完全不檢查 `Hidden`，已經知道欄位名稱的呼叫端仍能正常
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
