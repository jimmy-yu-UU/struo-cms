# 6. 欄位型別與編輯介面

每一個 `[CmsField]` 掛的 `FieldInterface` 值，同時決定三件事：資料庫欄位、透過 REST 與
GraphQL 公開的中介資料，以及後台編輯器用哪個元件渲染；這一章把三者的對應關係整理成表，並說明
圍繞它們的規則與陷阱。

## 介面總覽

`FieldInterface`（`src/Struo.Domain/Metadata/Enums/FieldInterface.cs`）一共 33 個成員，前台的
`frontend/src/lib/fieldTypes/types.ts` 手動鏡射了全部 33 個，沒有一邊是從另一邊生成的。掃描器不
會依 CLR 型別推斷 `Interface`：一個 `int` 屬性掛 `[CmsField]` 卻沒寫 `Interface`，一樣是 `Text`
欄位，連帶套用 255 字元的後台輸入上限。

下面表格的「資料庫欄位」欄，`varchar(255)` 是 SqlSugar CodeFirst 對 `string` 的預設值；「長文
字」是套用 `ColumnShape.LongText` 之後的結果——PostgreSQL 與 SQLite 解析成 `text`，MySQL 是
`longtext`，SQL Server 是 `nvarchar(max)`，Oracle 是 `clob`；「JSON」代表同時設定
`IsJson = true` 與長文字，由 SqlSugar 直接（反）序列化整個 CLR 集合；「CLR 預設」代表數值、
`bool`、`DateTime`、`Guid` 這類非 `string` 型別維持 SqlSugar 對該 CLR 型別本身的對應，掛勾本身
從不去動它的長度。

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

`RichText`、`Textarea`、`Markdown`、`Code` 跟稍後的 `Json`，合稱承載內容介面——只有這五個
`string` 屬性會被加寬成長文字。其餘介面沒有格式檢查，`Email`／`Url`／`Phone` 也不例外，唯一的
門檻是 `Required`。`Markdown` 與 `Code` 都是一般文字方塊，沒有預覽或語法標示。`Password` 的輸
入不遮蔽，也不會出現在 GraphQL schema 裡。`Color` 同樣是純文字輸入，沒有色票選色器——本章最後
「新增自訂欄位編輯器」就是把它換掉的範例。

### 數字與布林

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Number` | `int`／`long`／`decimal`／`double` | CLR 預設 | `NumberField` |
| `Slider` | 數值 | CLR 預設 | `NumberField` |
| `Rating` | 數值 | CLR 預設 | `NumberField` |
| `Boolean` | `bool` | CLR 預設 | `BooleanField` |
| `Checkbox` | `bool` | CLR 預設 | `BooleanField` |

`Slider` 與 `Rating` 沿用 `Number` 的編輯器，後台沒有滑桿或星級元件；三者留白都送出 `null`。
`Checkbox` 沿用 `Boolean` 的編輯器。

### 日期時間

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Date` | `DateTime`／`DateTime?` | CLR 預設 | `DateField` |
| `Time` | `DateTime`／`DateTime?` | CLR 預設 | `DateField` |
| `DateTime` | `DateTime`／`DateTime?` | CLR 預設 | `DateField` |

三者共用同一個編輯器，差別只在只顯示日期、只顯示時間、或兩者都顯示；留白都送出 `null`。CLR 預
設是不含時區的欄位，要拿到有時區的欄位，屬性要另外掛
`[ColumnShape(ColumnShape.TimestampWithTimeZone)]`——`MediaFolder` 的 `CreatedAt`／
`UpdatedAt` 就是這樣做的。

### 選擇

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Select` | `string` | `varchar(255)` | `SelectField` |
| `MultiSelect` | `List<string>` | JSON | `MultiSelectField` |
| `Radio` | `string` | `varchar(255)` | `RadioField` |
| `CheckboxGroup` | `List<string>` | JSON | `CheckboxGroupField` |
| `Tags` | `List<TagItem>` | JSON | `TagsField` |

`Select`／`MultiSelect`／`Radio`／`CheckboxGroup` 要掛 `[CmsOptions]` 才有值可選；`Tags` 可以
搭配 `[CmsOptions]`，但不是必要，通常留自由輸入。`[CmsOptions]` 本身不在寫入時強制成員資
格——`Select`／`Radio` 接受任意字串；真正檢查選項成員資格的只有 `MultiSelect`／
`CheckboxGroup`，以及巢狀在 `Repeater` 裡的 `Select`／`Radio` 子欄位。`MultiSelect`／
`CheckboxGroup`／`Tags` 都不能設為可翻譯，細節留到[第 7 章](07-i18n.md)。

### 結構化 JSON

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Json` | `string`（原始 JSON 文字） | 長文字 | `JsonField` |
| `KeyValue` | `Dictionary<string,string>` | JSON | `KeyValueField` |
| `Repeater` | `List<TChild>` | JSON | `RepeaterField` |

`Json` 是一個裝著原始 JSON 文字的 `string` 屬性，因為承載內容而被加寬成長文字，API 層讀取時重
新解析、寫入時重新序列化；真正的 JSON 欄位是 `MultiSelect`、`CheckboxGroup`、`Tags`、
`KeyValue`、`Files`、`Repeater` 這六個結構化介面，SqlSugar 直接對 `IsJson = true` 的欄位（反）
序列化整個 CLR 集合。`KeyValue` 的鍵不能留白。`Repeater` 的 `TChild` 必須是類別而不是
`string`，且至少要宣告一個 `[CmsField]`，兩者違反都是點名該欄位的啟動期
`MetadataException`；子欄位僅限純量介面：`Text`、`Textarea`、`Markdown`、`Code`、`Slug`、
`Email`、`Url`、`Color`、`Phone`、`Number`、`Slider`、`Rating`、`Boolean`、`Checkbox`、
`Date`、`Time`、`DateTime`、`Select`、`Radio` 共 19 種，且不能設為可翻譯；巢狀 `Repeater`、
`RichText`、`File`／`Image`／`Files`、`Password`、`Hidden`、`Uuid`、`Divider`、`Json`、
`KeyValue` 都不允許。

### 媒體與檔案

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `File` | `Guid`／`Guid?` | CLR 預設 | `FileField` |
| `Image` | `Guid`／`Guid?` | CLR 預設 | `FileField` |
| `Files` | `List<Guid>` | JSON | `FilesField` |

`File` 與 `Image` 共用同一個編輯器，`Image` 多了圖片預覽；`File`／`Image` 留白送出 `null`。
`Files` 存的 id 在寫入時不會檢查是否存在，指向已被清除檔案的 id 一樣會被存下來，後台選擇器會退
回顯示原始 id。三者都沒有清單欄位格式化器：只靠這些欄位的集合，清單畫面不會顯示欄位內容。

### 顯示用與識別碼

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Hidden` | 不限 | 依屬性型別 | `ReadonlyField` |
| `Divider` | 不限 | 依屬性型別，或無 | `DividerField` |
| `Uuid` | `Guid`／`Guid?` | CLR 預設 | `ReadonlyField` |

這裡的 `Hidden` 是介面本身，用來渲染一個唯讀的顯示元件，跟下一節的
`[CmsField(Hidden = true)]` 是兩回事——後者能掛在任何介面上。`Divider` 忽略欄位值，通常另外掛
`[SugarColumn(IsIgnore = true)]`，讓它完全不落地成欄位。`Uuid` 是唯讀顯示，junction 外鍵欄位
命名錯誤時，錯誤訊息會建議改用它。

一個集合的清單畫面最多顯示六欄，只從非系統、非隱藏、且介面本身有清單欄位格式化器的欄位裡選，
`DefaultDisplayField` 符合條件時排最前面，其餘照欄位在 API 回應裡的順序；33 個介面裡有十個沒
有格式化器（`Password`、`Markdown`、`Code`、`RichText`、`Divider`、`File`、`Image`、
`Files`、`Hidden`、`Uuid`），只用這些介面的集合，清單畫面就是空的。GraphQL schema 完全排除三
個介面：`Hidden`、`Divider`、`Password`。

## `MaxLength` 的行為

`[CmsField(MaxLength = n)]` 是 CMS 層自己的輸入長度上限，跟 `[SugarColumn(Length = n)]`、跟任
何明寫的 `ColumnDataType` 都無關；掃描碰到負值的 `MaxLength`，或是掛在非 `string` 屬性上的
`MaxLength`，兩者都會在啟動時丟出 `MetadataException`，訊息各自點名是哪一種違規。

有效的 `MaxLength` 依序解出：明寫且大於零時用明寫的值；否則屬性是 `string`、介面又落在十二個
短字串介面之一時退回 255——`Text`、`Slug`、`Email`、`Url`、`Password`、`Color`、`Phone`、
`Select`、`MultiSelect`、`Radio`、`CheckboxGroup`、`Tags`；兩者都不成立就是 `null`，代表不限
長度。

後台把有效的 `MaxLength` 直接綁到原生輸入的 `maxlength` 屬性上，所以就算從未寫過
`[CmsField]`，255 的預設值一樣會在前端生效。這跟資料庫欄位寬度無關——兩者剛好都是 255，只是因
為 SqlSugar 對未加寬 `string` 的預設值也是 255。

## 必填、唯讀、隱藏與系統欄位

`Required` 在建立時一定要帶那個欄位；在更新時驗證的是合併後的 entity，不是請求本文本身，細節
見[第 5 章](05-collections.md)。`RichText` 的內容如果清理後在視覺上等於空白，會先被轉成
`null` 再檢查 `Required`，所以看起來有內容的編輯器文件仍可能未通過必填檢查，`<img>`、`<hr>`
都算有內容。`Required` 的錯誤優先順序在建立與更新之間不同——建立時先檢查 `Required`，再檢查
長度與各結構化介面自己的驗證；更新時後兩者已經先跑過送進來的本文，違反兩者的本文回報的是長度
或驗證錯誤，不是 `Required`。

`ReadOnly`（`[CmsField(ReadOnly = true)]`）讀取時正常回傳值，更新時完全被保護——更新的欄位覆
蓋邏輯會跳過每一個 `ReadOnly`／`IsSystem` 欄位，請求本文永遠動不了它。建立時的保護則看屬性能
不能為 `null`：反序列化器綁定之後會把 `ReadOnly`／`IsSystem` 屬性設回 `null`，但只有可為
`null` 的屬性能被這樣設回去——`[CmsField(ReadOnly = true)] public int Views` 這種不可為
`null` 的值型別，建立時客戶端傳的值會原樣保留，之後才真正唯讀。後台的輸入框也會獨立停用：呼叫
端自己要求停用，或欄位中介資料本身是唯讀，兩者任一成立就停用。

四個稽核欄位不用寫 `[CmsField]`：掃描器自動加上，`IsSystem = true`、`ReadOnly = true`、
`Sort = 1000`，介面依 CLR 型別挑——`CreatedAt`／`UpdatedAt` 是 `DateTime`，`CreatedBy`／
`UpdatedBy` 是 `Text`。它們在建立時的保護是另一套機制：稽核的 AOP hook 在新增時無條件覆寫全
部四個欄位；更新時只重新蓋印 `UpdatedAt`／`UpdatedBy`，`CreatedAt`／`CreatedBy` 靠一般的更新
覆蓋邏輯保護。系統欄位讀取時照常回傳，只在後台的項目表單與清單欄位裡被排除，不像 `Hidden` 那
樣從 API 消失。

`Hidden`（`[CmsField(Hidden = true)]`）跟介面完全無關，任何介面都能設——例如範例的
`Article.InternalNote` 就是一個 `Hidden` 的 `Text` 欄位。它會讓欄位從 `GET /api/schema`、
GraphQL schema、項目回應、查詢 DSL 的已知／可搜尋／可排序白名單，以及對外回傳的修訂快照裡消
失，連帶把它從集合的 `translation.fields` 清單裡也拿掉——原始快照列仍保留這個值，回復時照樣
能救回來。它不是一個 RBAC 邊界：更新的欄位覆蓋邏輯完全不檢查 `Hidden`，已經知道欄位名稱的呼
叫端仍能正常寫入；要讓欄位真的不能寫，得跟 `ReadOnly` 搭配。後台只透過 schema 與項目回應認識
欄位，一個 `Hidden` 欄位不管宣告什麼介面，後台完全看不到它，也就沒有東西可以渲染。

## 常見陷阱

**未宣告型別的 `string` 保留 `varchar(255)`。** 一個沒有明寫欄位型別、介面又不屬於承載內容五
種介面之一的 `string` 屬性，保留 SqlSugar CodeFirst 的預設 `varchar(255)`；寫入超過 255 字元
的值，在 PostgreSQL 上會以 `22001 value too long for type character varying(255)` 失敗。修
法是換成承載內容介面之一，或是明寫 `[ColumnShape(ColumnShape.LongText)]`——不要直接寫
`[SugarColumn(ColumnDataType = "text")]`：`ColumnShape` 是跨後端中立的列舉，核心程式碼本身
也不能在 `ColumnTypeMap.cs` 之外寫死廠商專屬型別字串。

**JSON 欄位介面上加 `[ColumnShape]` 會被拒絕。** 一個屬性的 `[CmsField]` 介面是六個 JSON 欄位
介面之一時，另外掛 `[ColumnShape]` 會在啟動時丟出 `InvalidOperationException`，訊息點名該屬
性與違規的介面；落在 `InitTables` 集合裡的型別——每個框架 entity 加上每個 `[CmsCollection]`
型別——這個例外在啟動時就會丟出，不用等到真的用到那張表。修法是把 `[ColumnShape]` 移除：JSON
對映本身就會加寬成長文字，也會設定 `IsJson`，單靠 shape 兩者都拿不到。`[ColumnShape]` 跟承載
內容介面併用不受影響，仍然合法。`[ColumnShape]` 也會不出聲地贏過同一屬性上明寫的
`ColumnDataType`——它解析完 shape 就直接返回，不會再去讀那個屬性。

**`JsonElement` 在 `JsonDocument` 釋放後不可用。** 用 `using var doc =
JsonDocument.Parse(raw)` 解析 `Json` 欄位存的原始文字，再把 `doc.RootElement` 回傳到
`using` 區塊之外，下一次讀取會丟出 `ObjectDisposedException`——`JsonElement` 只有在產生它的
`JsonDocument` 還活著時才有效。改用 `JsonSerializer.Deserialize<JsonElement>(raw)`，不需要
`using`，拿到的是一個可以安全持有的獨立值。

**六個 JSON 欄位介面之外，手動宣告的 `List<>` 仍要自己標 `IsJson`。** `[CmsField]` 介面落在
`MultiSelect`／`CheckboxGroup`／`Tags`／`KeyValue`／`Files`／`Repeater` 這六個之外的
`List<>` 屬性，CodeFirst hook 不會替它自動套用 JSON 對映，SqlSugar 也沒辦法直接映射一個
list，自己要寫 `[SugarColumn(IsJson = true)]`。一旦寫了這個，單獨 `IsJson` 的加寬邏輯會接手補
上欄位型別，不需要再額外處理長度；若這個屬性另外明寫了 `ColumnDataType`（例如 PostgreSQL 原生
的 `jsonb`），加寬邏輯會尊重它、不去動它——但這條路徑本模板沒有任何測試涵蓋，不要假設它會像
長文字預設值那樣在框架裡正確往返讀寫。

## 新增自訂欄位編輯器

每一個欄位編輯器元件都吃同樣的三個 prop、發同一個事件：`field`（已解析的欄位中介資料）、
`modelValue`（表單目前的值）、可選的 `disabled`，變更時發回 `update:modelValue`：

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

分派器 `FieldInput.vue` 另外轉發第四個綁定 `id`，用 fallthrough attribute 的方式傳下去，本身
不是任何編輯器自己宣告的 prop；根元素就是原生控制項的編輯器會接住這個 `id`，跟呼叫端的
`<label>` 對上，根元素是包裝用 `div` 的編輯器則接不到，需要自己處理標籤。

下面把 `Color` 介面出廠的 `TextField`（純文字輸入，沒有色票選色器）換成一個原生 color
input。

**1. 寫元件**——`frontend/src/components/fields/ColorSwatchField.vue`：

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

那一行就是整個註冊過程：`FieldInput.vue` 解析 `getFieldType(field.interface).component`，替
每個集合裡每一個 `Color` 介面欄位渲染它，不需要逐集合接線。registry 是模組層級的一個純物件字
面值，型別是一個窮舉的 `Record`，沒有註冊 API 也沒有外掛掛鉤——換編輯器就是編輯這個檔案。

`frontend/src/lib/fieldTypes/types.ts` 的 `FieldInterface` 聯集是封閉的：新增一個真正全新的
介面值，而不是像上面那樣替換既有介面的編輯器，還得同步後端的 `FieldInterface` enum 與掃描器
推理到它的每一個地方，超出單純客製化編輯器的範圍。一個沒有對到 registry 鍵的介面會退回唯讀渲
染器；避免這種情況的是 schema 合約測試——它比對送出去的 enum 快照跟前端聯集、跟 registry
鍵，雙向都檢查，後端加了新成員前端沒跟上、或是前端留了過期項目，`frontend/` 底下的
`pnpm test` 都會失敗。這份快照由一次帶 `UPDATE_SCHEMA_SNAPSHOT=1` 的後端測試重新產生，跑完要
記得清掉這個環境變數，忘記清掉之後不相關的測試會悄悄改寫快照，而不是真的檢查它。

`richText` 是唯一延遲載入的編輯器，因為它是唯一會拉進編輯器函式庫的一個，有一個測試專門守著
它維持唯一。六個介面留白時送 `null` 而不是空字串——`number`、`slider`、`rating`、
`boolean`、`checkbox`、`uuid`——因為它們的後端屬性從來不是字串，送空字串會讓 JSON 綁定以
400 失敗；文字類介面則刻意持續送 `''`，清空文字欄位才會真的清空。`file`／`image` 與
`date`／`time`／`dateTime` 各自有對應的轉換規則。

## 接下來

欄位介面決定了值怎麼存、怎麼驗證、後台怎麼編輯；同一個值在不同語言下如何各自存放，是
[第 7 章](07-i18n.md)的主題。
