# 5. 欄位型別與介面

每一個 `[CmsField]` 都會指名一個 `FieldInterface`
(`src/Struo.Domain/Metadata/Enums/FieldInterface.cs`)。這一個 enum 值會同時驅動三件各自獨立實作
的事，本章就是要談這三件事:

1. **資料庫欄位**——在 CodeFirst 建表時，由 `SqlSugarClientFactory` 的 `EntityService` hook
   (`src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`) 決定，它會檢查每個屬性的
   `[CmsField]` 介面，來決定是否加寬欄位或標記為 JSON。
2. **透過 REST/GraphQL 公開的 CMS 層級中介資料**——由 `MetadataScanner.BuildField`
   (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`) 建構，它會解析有效的 `MaxLength`，
   並驗證各介面特有的限制條件 (選項、Repeater 子欄位)。
3. **管理後台 SPA 編輯器**——由 `frontend/src/lib/fieldTypes/registry.ts` 選擇，這是一個單一的
   對應表，把每一個 camelCase 介面名稱對應到一個 Vue 欄位元件。它的搭配檔案 `types.ts` 明確聲明這個
   union「對應到後端的 `Struo.Domain.Metadata.Enums.FieldInterface`……必須與該 enum 保持同步」。

在進入表格之前，有一個不對稱之處值得指出:`Json` 與其他六個結構化介面
(`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater`) 最終都會落在一個 `text` 欄位
中，但原因並不相同。後面那六個屬於 `JsonColumnInterfaces`——SqlSugar 被告知 `IsJson = true`，因而
自動 (反) 序列化 CLR 的 `List<>`/`Dictionary<>`。`Json` 則刻意**不屬於**這一類:它是一個存放原始
JSON 文字的一般 `string` 屬性，之所以被加寬到 `text`，純粹是因為它承載內容;API 層會在讀取時手動把
這段原始文字重新解析為 `JsonElement` (`ItemProjector.Project`)，並在寫入時手動重新序列化它
(`ItemDeserializer.Deserialize`)——對這一個介面而言，完全不涉及 SqlSugar 層級的 JSON 魔法。

## 全部 33 個值的完整表格

「(a)」= SqlSugar 對該 CLR 型別的預設 CodeFirst 對應 (`string` → `varchar(255)`，除非被加寬或明確
覆寫)。「(b)」= 因為該介面承載內容，而被自動加寬為 `text`。「(c)」= 以 JSON 文字形式儲存
(`IsJson = true`，`text` 欄位);SqlSugar 會自動 (反) 序列化該 CLR 集合。

| 介面 | 用途 | 預期的 CLR 型別 | 產生的欄位 | 管理後台編輯器 |
|---|---|---|---|---|
| `Text` | 簡短純文字字串 | `string` | (a) `varchar(255)` | `TextField` (vendored `ui/input`) |
| `Textarea` | 多行純文字字串 | `string` | (b) `text` | `TextareaField` |
| `RichText` | 已清理 (sanitize) 的 HTML | `string` | (b) `text` | `RichTextField` (TipTap) |
| `Markdown` | Markdown 原始文字 | `string` | (b) `text` | `TextareaField`——純文字區域，沒有即時預覽或工具列 |
| `Code` | 原始碼文字 | `string` | (b) `text` | `TextareaField`——純文字區域，沒有語法標示 |
| `Slug` | URL 安全的短字串 | `string` | (a) `varchar(255)` | `TextField` |
| `Email` | 電子郵件地址 | `string` | (a) `varchar(255)` | `TextField`——除了 `Required` 之外，不做任何電子郵件格式檢查 |
| `Url` | URL | `string` | (a) `varchar(255)` | `TextField` |
| `Password` | 機密值 | `string` | (a) `varchar(255)` | `TextField`——一般 (未遮罩) 的輸入欄位;完全排除在 GraphQL schema 之外 |
| `Color` | 顏色值 | `string` | (a) `varchar(255)` | `TextField`——一般文字輸入欄位，沒有色票選擇器 |
| `Phone` | 電話號碼 | `string` | (a) `varchar(255)` | `TextField` |
| `Number` | 數值 | `int`/`long`/`decimal`/`double`/等 | (a) 依 SqlSugar 對該數值 CLR 型別的對應而定 | `NumberField` (vendored `ui/number-field`) |
| `Slider` | 數值 | numeric | (a) 同上 | `NumberField`——與 `Number` 相同的元件，沒有滑桿 (slider) 元件 |
| `Rating` | 數值 | numeric | (a) 同上 | `NumberField`——與 `Number` 相同的元件，沒有星級 (star) 元件 |
| `Boolean` | 真/假 | `bool` | (a) boolean | `BooleanField` |
| `Checkbox` | 真/假 | `bool` | (a) boolean | `BooleanField`——與 `Boolean` 相同的元件 |
| `Date` | 日曆日期 | `DateTime`/`DateTime?` | (a) SqlSugar 對 `DateTime` 的預設對應 | `DateField`，僅日期選擇器 |
| `Time` | 一天中的時間 | `DateTime`/`DateTime?` | (a) 同上 | `DateField`，僅時間選擇器 |
| `DateTime` | 日期與時間 | `DateTime`/`DateTime?` | (a) 同上 | `DateField`，日期 + 時間選擇器 |
| `Select` | 從固定清單中單選 | `string` | (a) `varchar(255)` | `SelectField`——需要 `[CmsOptions]` |
| `MultiSelect` | 從固定清單中複選 | `List<string>` | (c) JSON in `text` | `MultiSelectField`——需要 `[CmsOptions]` |
| `Radio` | 從固定清單中單選 | `string` | (a) `varchar(255)` | `RadioField`——需要 `[CmsOptions]` |
| `CheckboxGroup` | 從固定清單中複選 | `List<string>` | (c) JSON in `text` | `CheckboxGroupField`——需要 `[CmsOptions]` |
| `Tags` | 自由格式的多值標籤 | `List<TagItem>` | (c) JSON in `text` | `TagsField`——允許使用 `[CmsOptions]`，但非必要 |
| `Json` | 任意 JSON 值 | `string` (原始 JSON 文字) | (b) `text`——見上方說明，並非 `IsJson` | `JsonField` |
| `KeyValue` | 自由格式的字串對應表 | `Dictionary<string, string>` | (c) JSON in `text` | `KeyValueField` |
| `Repeater` | 可重複的子物件 | `List<TChild>`，`TChild` 是一個帶有自己 `[CmsField]` 的一般類別 | (c) JSON in `text` | `RepeaterField` |
| `File` | 單一檔案參照 | `Guid`/`Guid?` | (a) `uuid` | `FileField` (一般選擇器) |
| `Image` | 單一圖片參照 | `Guid`/`Guid?` | (a) `uuid` | `FileField` (帶圖片預覽的選擇器) |
| `Files` | 多個檔案參照 | `List<Guid>` | (c) JSON in `text` | `FilesField` |
| `Hidden` | 從來不打算讓管理後台可編輯或透過 GraphQL 公開的自有欄位 | 掃描器不強制檢查 | (a) 依 CLR 型別對應而定 | `ReadonlyField`——僅供顯示;排除在 GraphQL schema 之外 |
| `Divider` | 欄位之間的視覺分隔線 | 掃描器不強制檢查;元件會忽略欄位的值 | (a) 依 CLR 型別對應而定 (或搭配 `[SugarColumn(IsIgnore = true)]` 時完全沒有) | `DividerField`——渲染一個靜態的 `<hr>` |
| `Uuid` | 唯讀的識別碼顯示 | `Guid`/`Guid?` | (a) `uuid` | `ReadonlyField` |

這張表的 33 個資料列，恰好對應 33 個 `FieldInterface` enum 成員，每個各出現一次，依宣告順序排列，
並直接對照 `src/Struo.Domain/Metadata/Enums/FieldInterface.cs` 驗證過。

Repeater 的子欄位被限制在一份較小、僅限純量 (scalar) 型別的允許清單中——
`Text`/`Textarea`/`Markdown`/`Code`/`Slug`/`Email`/`Url`/`Color`/`Phone`/`Number`/`Slider`/`Rating`/
`Boolean`/`Checkbox`/`Date`/`Time`/`DateTime`/`Select`/`Radio`——而且不能是 `Translatable`;否則
`MetadataScanner.BuildRepeaterChildFields` 會讓掃描失敗。把一個 `Repeater` 巢狀嵌入另一個
`Repeater`，以及 `RichText`/`File`/`Image`/`Files`/`Password`/`Hidden`/`Uuid`/`Divider`/`Json`/`KeyValue`
子欄位，都會以同樣的方式被拒絕。

**`RichText` 欄位裡連結的 `target` 屬性，也能有條件地在清理後存活。**
清理器 `GanssHtmlSanitizer` (`src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs`) 從不採信
client 提供的 `rel`——它每次寫入都會依 `target` 重新推導 `rel`，所以最後儲存下來的內容只由「這個
連結要在哪個分頁開啟」這一件事決定:

| 開啟方式 | 儲存下來的 HTML |
|---|---|
| 新分頁 | `<a href="…" target="_blank" rel="noopener">` |
| 同分頁 | `<a href="…">` |

比對方式是精確且區分大小寫的:只有字面上完全等於 `_blank` 的值才算新分頁。像 `_Blank` 或
`_BLANK` 這種——瀏覽器自己會把它們當成跟 `_blank` 一樣——在這裡反而會被當成同分頁處理，跟一個
未經清理的頁面實際上會有的行為正好相反;這是刻意選擇的保守 (fail-closed) 做法，不是疏漏。無論送
進來的是什麼，儲存的連結上永遠不會出現 `nofollow` 或 `noreferrer`;`href` 本身就被拒絕 (不允許的
scheme) 的連結，也不會保留 `target` 或 `rel` 這兩個屬性中的任何一個。後台 SPA 的富文本連結對話框
(見第 14 章) 是唯一會設定 `target` 的編輯器介面;直接呼叫 API 寫入或匯入內容，一樣受同一條規則
約束。

這個 `rel` 推導規則是一個政策選擇，不是不可更動的不變量:`GanssHtmlSanitizer.cs` 自己的處理常式裡
完整說明了防範 reverse-tabnabbing 的理由，fork 若想要不同的值 (例如 `noreferrer`)，應該直接在那裡
修改，而不是改對話框或清理器的允許清單。

**`RichText` 欄位裡圖片的 `width` 屬性，也能在清理後存活——但只能以一個單純的像素數值存活。**
`GanssHtmlSanitizer` 先把 `width` 整體加進允許清單 (`AllowedAttributes` 沒有「限定某個標籤」這種
概念，所以讓這個屬性名稱存在的那一筆設定，會讓它在每一個被允許的標籤上都可用)，再在同一個推導
`rel` 的 `PostProcessNode` 處理常式裡把它收窄:這個屬性只在 `<img>` 上存活，而且只有當它的值完全
符合 `\A[1-9][0-9]{0,4}\z` 時才算數。這接受的是一到五位、沒有前導零的 ASCII 數字——
也就是從 `1` 到 `99999` 的單純像素數值——其餘一切都會被拒絕:`0` (當寬度沒有意義)、百分比
(`40%`)、小數 (`480.5`)、負數 (`-5`)、帶前導或後綴空白的值 (`" 480"`)、空字串，以及六位數的值
(`100000`);其中有些 (尤其是前導空白) 一個粗略的整數解析器反而會接受。在 `<img>` 以外的任何元素
上——`<table>`、`<td>`、`<p>`、`<span>`，甚至 `<a>`——`width` 一律被剝除，不管有沒有列在允許清單
裡。跟上面的 `rel` 推導一樣，這條規則約束每一個寫入路徑，不因產生 HTML 的介面而異:直接呼叫 API
寫入，或匯入工具把一個原始 `width` 存到 `<img>` 上，一樣要通過完全相同的正則表達式，不會比較寬鬆。

`height` 則完全沒有被加進允許清單，所以無論送進來的是什麼值，它永遠不會出現在儲存的 HTML 裡——
不論是編輯器、直接呼叫 API 寫入，還是匯入工具，規則都一樣，不因產生 HTML 的介面而異。這是刻意的
選擇，不是疏漏:fork 的前台不保證會把儲存下來的 `width` 配上 CSS 的 `height: auto`，而如果同時把
兩個維度都送進一個只設了 `max-width: 100%` 上限的渲染器，瀏覽器排版出來的容器就會被撐開，圖片本身
卻縮放去符合寬度，結果讓長寬比走樣。fork 若要渲染 `RichText` 輸出，應該給 `img` 元素一條單純的
`max-width: 100%` 規則，且不要再加任何同時約束 `height` 的規則——讓儲存下來的 `width` 只當作上限，
剩下的交給瀏覽器自己依原始比例縮放。

這個收窄規則是集中在一個地方執行的政策選擇，不是不變量:`GanssHtmlSanitizer.cs` 的類別摘要註解
裡，分別說明了 width 收窄背後「限定標籤、只收純數字」的理由，以及完全不收 `height` 背後的長寬比
理由。fork 若想接受百分比寬度，或連 `height` 一起儲存，應該直接在那裡修改。

## `MaxLength` 的行為

`[CmsField(MaxLength = n)]` 是一個 **CMS 層級**的輸入長度上限 (以 UTF-16 code unit 計算)——與實際
的資料庫欄位寬度 (`[SugarColumn(Length = n)]`) 或明確設定的 `[SugarColumn(ColumnDataType = ...)]`
無關。如果 `MaxLength` 是負數，或是在非 `string` 屬性上設定了它 (`> 0`)，`MetadataScanner.BuildField`
會在啟動時擲出 `MetadataException`。

以 `FieldMetadata.MaxLength` 形式公開的有效值 (並由此傳遞給管理後台 SPA 與任何 API client) 依以下
順序解析:

1. 明確設定的 `MaxLength`，如果有設定的話 (`> 0`)。
2. 否則，如果屬性是 `string` **且**介面是十二個「短字串」介面之一，則為 `255`:`Text`、`Slug`、
   `Email`、`Url`、`Password`、`Color`、`Phone`、`Select`、`MultiSelect`、`Radio`、`CheckboxGroup`、
   `Tags`。(`MultiSelect`、`CheckboxGroup` 與 `Tags` 只有在你不尋常地給它們一個一般 `string` 屬性、
   而非它們平常使用的 `List<string>`/`List<TagItem>` 時才有意義;`Radio` 就跟 `Select` 一樣，通常
   本來就*是*一個一般的 `string`——見上方的表格。)
3. 否則為 `null` (無限制)——每一個承載內容的介面 (`Textarea`、`RichText`、`Markdown`、`Code`、
   `Json`) 以及每一個非 `string` 欄位皆是如此。

管理後台 SPA 的 `TextField.vue` 會把 `field.maxLength` 直接傳給 vendored `ui/input` 原生的
`maxlength` 屬性，所以即使從未有任何 `[CmsField]` 明確設定過它，這個預設值 `255` 仍會在 client 端
被強制執行為一個硬性的輸入上限。它與資料庫欄位的實際寬度毫無關係——這兩個機制之所以剛好都用 `255`
這個數字，只是因為 SqlSugar 自己對於未加寬 `string` 欄位的 CodeFirst 預設值也剛好是
`varchar(255)`。

## 常見陷阱

**未宣告型別的 `string` 會變成 `varchar(255)`，所以長文字欄位需要明確指定欄位型別。** 原因:一個
沒有 `[SugarColumn(ColumnDataType = ...)]`、且介面不屬於五個承載內容介面
(`RichText`/`Textarea`/`Markdown`/`Code`/`Json`) 之一的 `string` 屬性，會沿用 SqlSugar CodeFirst
的預設值 `varchar(255)`。徵狀:插入一個超過 255 字元的值，在 Postgres 上會失敗，錯誤是 `22001
value too long for type character varying(255)`。修法:使用五個承載內容介面之一 (會自動加寬為
`text`)，或明確加上 `[SugarColumn(ColumnDataType = "text")]`——就像 `SiteSettings.BrandName` 與
`Revision.Snapshot` 那樣做 (`src/Struo.Infrastructure/Settings/SiteSettings.cs`、
`src/Struo.Infrastructure/Revisions/Revision.cs`)。

**`IsJson` 若沒有搭配 `text` 欄位會被截斷——現在已由 hook 解決。** 原因:這曾經是一個真實的陷阱:
單獨使用 `[SugarColumn(IsJson = true)]`，在下方 `JsonColumnInterfaces` 慣例觸及不到的屬性上，會讓
CodeFirst 的欄位長度保持未設定，而 Postgres 對此的預設值是 `varchar(1)`;序列化任何長度超過一個
字元的 JSON 值都會插入失敗 (`22001`)，而同一個欄位在測試中對 SQLite「看起來能動」，因為 SQLite 會
忽略已宣告的欄位長度——這個落差只會在對真正的 Postgres 執行個體時才浮現。現在的情況是:
`SqlSugarClientFactory` 的 `EntityService` hook
(`src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`) 會自行把單獨的 `IsJson` 欄位加寬
為同樣的 `text` 型別欄位，所以單獨的 `[SugarColumn(IsJson = true)]` 在任何後端上都不再會截斷。你
仍然需要自己手動處理的唯一情況是:為屬性明確指定 `ColumnDataType`——例如
`[SugarColumn(IsJson = true, ColumnDataType = "text")]`，自己直接釘死型別而不讓 hook 加寬——此時
它會優先生效;hook 只在該 attribute 自身的 `ColumnDataType` 未設定時才會加寬。像 `jsonb` 這種
Postgres 原生型別本模板並未測試;不要假設它能透過與 `text` 預設值相同的路徑正確往返讀寫。

**JSON 欄位上加 `[ColumnShape]` 會被拒絕。** 原因:CodeFirst hook 解析明寫的 `[ColumnShape]`
(`src/Struo.Infrastructure/Persistence/ColumnShape.cs`) 之後就會早退，不會走到它的 JSON 欄位分支;
因此一個同時帶著兩者的屬性原本會保留 shape 的欄位型別、並丟掉 `IsJson`——而在 shape 宣告為
`LongText` (此處唯一合理的選擇) 時，兩條路對 JSON 欄位介面會解析出同一個 `text`，所以丟掉
`IsJson` 曾是這個組合*唯一*的效果，正好重現上面那個截斷陷阱——`[ColumnShape]` 分支會在單獨
`IsJson` 的加寬邏輯執行之前就提前返回，所以那個加寬邏輯永遠沒有機會拯救同時帶有兩者的屬性。徵狀:擲出
`InvalidOperationException`，訊息會指名該屬性與違規的介面;凡是落在 `InitTables` 集合裡的型別——
每一個框架 entity 加上每一個 `[CmsCollection]` 型別——都會在**啟動時**擲出，該資料表是否已存在
無關緊要:`DatabaseInitializer.CreateMissingTables` 會為每個型別向 `EntityMaintenance` 詢問表名以
算出缺表集合，而光是建出那個 `EntityInfo` 就會對每個屬性跑過 hook。只有落在該集合*之外*的
entity——fork 自己的非 collection entity，直接透過 `ISqlSugarClient` 使用——才會改成在第一次使用
時才失敗。修法:把 `[ColumnShape]` 從該屬性上移除——JSON 欄位的對映本來就會把欄位加寬為 `text`
*並且*設定 `IsJson`，shape 沒有帶來任何東西。這只適用於六個 `JsonColumnInterfaces`
(`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater`);`[ColumnShape]` 與承載內容介面
(`RichText`/`Textarea`/`Markdown`/`Code`/`Json`) 併用是合法的，也未改變，因為那裡兩條路對 `text`
的結論一致。

**在其 `JsonDocument` 被釋放之後，再讀取 `JsonElement` 會擲出例外。** 原因:`System.Text.Json` 的
`JsonElement` 只有在產生它的 `JsonDocument` 仍存活時才有效;用 `using var doc =
JsonDocument.Parse(raw)` 解析 `Json` 欄位的原始儲存文字，然後在那個 `using` 區塊的範圍之外回傳或
儲存 `doc.RootElement`，會在下一次讀取時擲出 `ObjectDisposedException`。修法:改用
`JsonSerializer.Deserialize<JsonElement>(raw)`——不需要 `using`，不會被釋放，得到一個可以安全持有
的獨立 `JsonElement`——這正是 `ItemProjector.Project` 在為 API 回應重新還原一個 `Json` 欄位時所做
的事。

**多值選擇欄位需要 `IsJson`——`text` 欄位由同一個 hook 解決。** 原因:`MultiSelect`/
`CheckboxGroup` (`List<string>`) 同時依賴這兩項設定:`IsJson` 讓 SqlSugar 願意 (反) 序列化這個
清單，而加寬為 `text` 型別的 `DataType` 則給它足夠的空間——單獨宣告 `text` 型別而不搭配 `IsJson`，
仍然會重現上面對應的失敗，但單獨的 `IsJson` 不會 (同一個 hook 會把它加寬)。框架自身能感知
`[CmsField]` 的 CodeFirst hook，會為每一個 `JsonColumnInterfaces` 成員
(`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater`) 自動套用這兩項設定，所以除了
選擇介面本身之外，這些介面完全不需要手動宣告。如果你自己的屬性落在這個集合之外——例如一個 hook 不會
透過 `JsonColumnInterfaces` 路由到的手動宣告 `List<>`——你仍然需要自己寫上
`[SugarColumn(IsJson = true)]`，但一旦這麼做，上一個陷阱提到的單獨 `IsJson` 加寬邏輯就會自動接手
補上 `DataType`;如果該屬性明確指定了 `ColumnDataType`，則會如同上一個陷阱一樣優先採用該值。

## 唯讀、隱藏與系統欄位

**`ReadOnly` (`[CmsField(ReadOnly = true)]`)**——讀取時會正常回傳其值。更新時則完全受到保護:
`ItemService.UpdateCoreAsync` 的欄位覆蓋 (overlay) 邏輯會直接跳過每一個 `ReadOnly`/`IsSystem`
欄位，所以無論其 CLR 型別為何，更新的請求內容永遠無法把一個 `ReadOnly` 欄位的值搬移到既有的 entity
上。新增時，`ItemDeserializer.Deserialize` 會在反序列化請求內容之後，立刻把繫結到的
`ReadOnly`/`IsSystem` 屬性設為 null——但根據它自己的註解，「只有可為 null 的屬性能被設為 null」
(`canBeNull = !pi.PropertyType.IsValueType || Nullable.GetUnderlyingType(...) is not null`):一個
由不可為 null 的實值型別 (value type) 支撐的 `ReadOnly` 欄位 (例如
`[CmsField(ReadOnly = true)] public int Views`)，在新增時會保留 client 提供的任何值，因為沒有辦法
把它設回 null。管理後台 SPA 的 `FieldInput.vue` 也會停用渲染出的輸入欄位
(`props.disabled === true || props.field.readOnly`)，這與上述兩個伺服器端機制彼此獨立。

**`Hidden` (`[CmsField(Hidden = true)]`)**——與欄位使用哪一種 `FieldInterface` 無關 (範例中的
`Article.InternalNote` 是一個帶有 `Hidden = true` 的 `Text` 欄位)。其效果，全都位於
`src/Struo.Application` 中:

- 完全從 `GET /api/schema` / `GET /api/schema/{collection}` 中移除
  (`SchemaService.WithoutHiddenFields`)——對任何做 schema 探索的 API/GraphQL client 而言都不可見，
  包括管理後台 SPA 自己。
- 排除在 GraphQL schema 之外 (`CollectionSchemaBuilder` 會跳過每一個 `f.Hidden` 欄位)。
- 排除在投影出的項目回應之外 (`ItemProjector` 會跳過 `field.Hidden`)——
  `GET /api/items/{collection}/{id}` 永遠不會回傳它的值。
- 排除在查詢 DSL 的已知欄位、可搜尋欄位與可排序欄位白名單之外 (`QueryValidator`)——無法對它做
  篩選、搜尋或排序。
- 在任何對外回傳的版本快照中會被遮蔽 (`RevisionSnapshotRedactor`)，即使原始的版本紀錄資料列仍然
  完整保留該值，以便還原時能夠恢復它。
- **不會**被排除在 `ItemService.UpdateCoreAsync` 的寫入覆蓋邏輯之外——一個已經知道欄位名稱的
  client，仍然可以透過一般的 `PUT`/`POST` 設定它。只是它永遠看不到這個值被回傳，也永遠無法透過
  schema introspection 或一般的項目回應發現這個欄位的名稱。

因為管理後台 SPA 只能透過上述的 schema/項目回應得知欄位資訊，一個 `Hidden` 欄位的中介資料與值完全
不會傳到它那裡——不論該欄位宣告的是哪一種介面，`frontend/src/lib/fieldTypes/registry.ts` 都沒有任何
東西可以渲染。

**系統欄位**——來自 `IAuditable`/`AuditableEntity` 的 `CreatedAt`、`CreatedBy`、`UpdatedAt`、
`UpdatedBy` 完全不需要任何 `[CmsField]`:`MetadataScanner.BuildSystemField` 會自動加入它們，設定
`IsSystem = true`、`ReadOnly = true`、`Sort = 1000` (排在每一個已宣告欄位之後)，並依屬性的 CLR
型別挑選一個 `Interface`——`CreatedAt`/`UpdatedAt` (兩者都是 `DateTime`) 用 `DateTime`，
`CreatedBy`/`UpdatedBy` (兩者都是 `Guid?`，這會在 `BuildSystemField` 的 `DateTime`/`DateTime?`
檢查中落空，退回 `Text` 這個預設值) 用 `Text`。與一般的 `ReadOnly` 欄位不同，這四個欄位在新增時也
完全受到保護，即使 `CreatedAt`/`UpdatedAt` 是不可為 null 的 `DateTime`——但依操作種類，是靠兩種不同
的機制:`AuditAop.Register` 的 `DataExecuting` hook 會在新增時無條件覆寫這四個欄位，與
`ItemDeserializer` 那個受可為 null 與否所把關的剔除邏輯彼此獨立;更新時，同一個 hook 只會重新戳記
`UpdatedAt`/`UpdatedBy` (它的 `UpdateByObject` 分支沒有處理 `CreatedAt`/`CreatedBy` 的情況)，所以
這兩個欄位改由更新時保護其他每一個 `ReadOnly`/`IsSystem` 欄位所用的同一種方式來保護——也就是
`ItemService.UpdateCoreAsync` 的欄位覆蓋跳過邏輯，它完全不會從傳入的內容中複製它們。它們在讀取時會
像其他任何欄位一樣被回傳 (`ItemProjector` 不會跳過 `IsSystem`)，並且會被排除在管理後台項目表單與
集合 (collection) 清單欄位之外 (`frontend/src/lib/splitFields.ts` 與
`frontend/src/lib/selectListColumns.ts` 都會把 `isSystem` 過濾掉)——所以，與 `Hidden` 欄位不同，
它們透過 API 仍然完全可見;只是出貨的管理後台 SPA 從來不會渲染它們。

## 新增自訂欄位編輯器

介面對應到元件的對應表完全存放在 `frontend/src/lib/fieldTypes/registry.ts` 中，鍵值是
`frontend/src/lib/fieldTypes/types.ts` 中宣告的 `FieldInterface` union——這個 union 自己的註解說
它必須與上方的後端 enum 保持同步。打造一個真正全新的欄位編輯器，或替換其中一個出貨的元件，是第 14
章的主題。

## 接下來該去哪

- 第 4 章 [定義一個集合](04-defining-a-collection.md)，涵蓋 `[CmsField]` 的其他選項，以及一個集合
  如何把自己的欄位串連在一起。
- 第 6 章 [國際化](06-internationalization.md)，涵蓋 `Translatable` 欄位。
- 第 8 章 [查詢 DSL](08-query-dsl.md)，涵蓋 `Searchable`/`Sortable`/`Hidden` 如何形塑 `filter` 與
  `sort` 白名單。
- 第 10 章 [GraphQL API](10-graphql-api.md)，涵蓋介面對應到 SDL 型別的完整對照。
- 第 14 章 [管理後台 SPA 客製化](14-admin-spa-customization.md)，用來新增一個新的欄位編輯器。
