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
| `Text` | 簡短純文字字串 | `string` | (a) `varchar(255)` | `TextField` (PrimeVue `InputText`) |
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
| `Number` | 數值 | `int`/`long`/`decimal`/`double`/等 | (a) 依 SqlSugar 對該數值 CLR 型別的對應而定 | `NumberField` (PrimeVue `InputNumber`) |
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

管理後台 SPA 的 `TextField.vue` 會把 `field.maxLength` 直接傳給 PrimeVue `InputText` 原生的
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

**`IsJson` 若沒有搭配 `text` 欄位會被截斷。** 原因:單獨使用 `[SugarColumn(IsJson = true)]` 會讓
CodeFirst 的欄位長度保持未設定，而 Postgres 對此的預設值是 `varchar(1)`。徵狀:序列化任何長度超過
一個字元的 JSON 值都會插入失敗 (`22001`)，即使這個欄位在測試中對 SQLite「看起來能動」也一樣
(SQLite 會忽略已宣告的長度;這個錯誤只會在對 Postgres 執行時才浮現)。修法:一律讓 `IsJson = true`
搭配明確的 `text` `DataType`——這正是框架自身的 `JsonColumnInterfaces` hook 對
`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater` 欄位自動做的事;如果你曾經在這個
慣例之外的屬性上自行設定 `[SugarColumn(IsJson = true)]`，就必須同時設定
`ColumnDataType = "text"`。

**在其 `JsonDocument` 被釋放之後，再讀取 `JsonElement` 會擲出例外。** 原因:`System.Text.Json` 的
`JsonElement` 只有在產生它的 `JsonDocument` 仍存活時才有效;用 `using var doc =
JsonDocument.Parse(raw)` 解析 `Json` 欄位的原始儲存文字，然後在那個 `using` 區塊的範圍之外回傳或
儲存 `doc.RootElement`，會在下一次讀取時擲出 `ObjectDisposedException`。修法:改用
`JsonSerializer.Deserialize<JsonElement>(raw)`——不需要 `using`，不會被釋放，得到一個可以安全持有
的獨立 `JsonElement`——這正是 `ItemProjector.Project` 在為 API 回應重新還原一個 `Json` 欄位時所做
的事。

**多值選擇欄位需要 `IsJson` *且*搭配 `text` 欄位——單獨一項並不夠。** 原因:`MultiSelect`/
`CheckboxGroup` (`List<string>`) 同時依賴這兩項設定:`IsJson = true` 讓 SqlSugar 願意 (反) 序列化
這個清單，而 `ColumnDataType = "text"` 則給它足夠的空間 (見前兩個陷阱——單獨設定其中一項，就會重現
對應的失敗)。框架自身能感知 `[CmsField]` 的 CodeFirst hook，會為這些介面自動套用這兩項設定;如果你
曾經自己手動宣告 SqlSugar attribute，而不是依賴這個慣例 (例如在該 hook 觸及不到的屬性上)，就必須把
兩項都設定好。

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
  filter、搜尋或排序。
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
- 第 8 章 [查詢 DSL](08-query-dsl.md)，涵蓋 `Searchable`/`Sortable`/`Hidden` 如何形塑 filter 與
  排序白名單。
- 第 10 章 [GraphQL API](10-graphql-api.md)，涵蓋介面對應到 SDL 型別的完整對照。
- 第 14 章 [管理後台 SPA 客製化](14-admin-spa-customization.md)，用來新增一個新的欄位編輯器。
