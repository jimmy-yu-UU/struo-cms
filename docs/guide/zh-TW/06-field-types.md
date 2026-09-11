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

- `varchar(255)`：SqlSugar CodeFirst 對 `string` 的預設寬度。
- 長文字：套用 `ColumnShape.LongText` 之後的結果，各後端有自己的對應型別。
- JSON：長文字加上 `IsJson = true`，整個 CLR 集合由 SqlSugar 直接序列化與還原。
- CLR 預設：數值、`bool`、`DateTime`、`Guid` 照 SqlSugar 原本的對應，長度不會被動到。
- `ColumnShape` 只有兩個成員：`LongText` 與 `TimestampWithTimeZone`，沒有數值形狀。

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

這五個介面的 `string` 屬性會自動加寬成長文字：`Textarea`、`RichText`、`Markdown`、
`Code`，以及後面的 `Json`；其他 `string` 介面都停在 `varchar(255)`。除了 `Required` 與
`MaxLength` 之外沒有任何格式檢查，`Email`／`Url`／`Phone` 也不例外。`Markdown` 與 `Code`
都是一般文字方塊，沒有預覽或語法標示。`Password` 的輸入不遮蔽，也不會出現在 GraphQL schema
裡。`Color` 同樣是純文字輸入，沒有色票選色器。

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
[第 5 章：定義集合](05-collections.md)。寫入時真正檢查值有沒有在選項清單裡的只有
`MultiSelect`、`CheckboxGroup`，以及 `Repeater` 子欄位裡的 `Select`、`Radio`；`Select`、
`Radio` 自己接受任意字串。

### 結構化 JSON

| 介面 | CLR 型別 | 資料庫欄位 | 後台編輯器 |
|---|---|---|---|
| `Json` | `string`（原始 JSON 文字） | 長文字 | `JsonField` |
| `KeyValue` | `Dictionary<string,string>` | JSON | `KeyValueField` |
| `Repeater` | `List<TChild>` | JSON | `RepeaterField` |

`Json` 是一個裝著原始 JSON 文字的 `string` 屬性，API 層讀取時重新解析、寫入時重新序列化。
真正的 JSON 欄位是 `MultiSelect`、`CheckboxGroup`、`Tags`、`KeyValue`、`Files`、`Repeater`
這六個結構化介面，SqlSugar 直接對 `IsJson = true` 的欄位（反）序列化整個 CLR 集合；
`KeyValue` 的鍵不能留白。

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

## `MaxLength` 的行為

`MaxLength` 只管後台輸入，以 UTF-16 code unit 計，跟 `[SugarColumn(Length = n)]` 或明寫的
`ColumnDataType` 都不相干。負值，或掛在非 `string` 屬性上，都會在啟動時丟出
`MetadataException`。有效的 `MaxLength` 依序解出：

1. 明寫且大於零，用明寫的值。
2. 否則屬性是 `string`，介面又落在十二個短字串介面之一時退回 255：`Text`、`Slug`、
   `Email`、`Url`、`Password`、`Color`、`Phone`、`Select`、`MultiSelect`、`Radio`、
   `CheckboxGroup`、`Tags`。
3. 兩者都不成立，是 `null`，代表不限長度。

後台把有效的 `MaxLength` 直接綁到原生輸入的 `maxlength` 屬性上，所以就算從未寫過
`MaxLength`，255 的預設值一樣會在前端生效。這跟資料庫欄位寬度無關——兩者剛好都是 255，只是
因為 SqlSugar 對未加寬 `string` 的預設值也是 255。

## 必填、唯讀、隱藏與系統欄位

`Required` 在建立時一定要帶那個欄位；在更新時驗證的是合併後的 entity，不是請求本文本身，細
節見[第 5 章：定義集合](05-collections.md)。

`RichText` 的內容如果清理後在視覺上等於空白，會先被轉成 `null` 再檢查 `Required`，所以看起
來有內容的編輯器文件仍可能未通過必填檢查，`<img>`、`<hr>` 都算有內容。

建立時先報 `Required`；更新時如果本文同時違反長度或結構驗證，先報的是那些錯誤。

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

- 寫入本文裡不認識的鍵會被直接丟掉、不會報錯，打錯欄位名照樣成功回應，只是值沒存進去。
- `MultiSelect`／`CheckboxGroup` 去重、保留第一個；`Tags` 空白值拒絕、重複丟掉。
- `Files` 丟掉 `Guid.Empty` 與重複；`Repeater` 丟掉整列空白的列，但錯誤訊息的列號仍以送進
  來的順序（含被丟掉的）從 1 起算。

## 常見陷阱

**寫入長文字時 PostgreSQL 報 `22001 value too long`。** 一個沒有明寫欄位型別、介面又不在前
面那五個之列的 `string` 屬性，保留 SqlSugar CodeFirst 的預設 `varchar(255)`；寫入超過 255
字元的值，在 PostgreSQL 上會以 `22001 value too long for type character varying(255)` 失敗。
修法是換成這五個介面之一，或是明寫 `[ColumnShape(ColumnShape.LongText)]`——不要直接寫
`[SugarColumn(ColumnDataType = "text")]`：`ColumnShape` 在每個後端都成立。

**JSON 欄位介面上加 `[ColumnShape]` 會被拒絕。** 屬性的 `[CmsField]` 介面若是六個 JSON 介面之一，另
外掛 `[ColumnShape]` 會在啟動時丟出 `InvalidOperationException`，訊息點名屬性與介面；`InitTables` 集
合裡的型別在啟動時就失敗，集合外的則要等到第一次用到那張表才失敗。修法是移除 `[ColumnShape]`：JSON
對映本身就會加寬成長文字並設定 `IsJson`，單靠 shape 兩者都拿不到。`[ColumnShape]` 跟長內容的五個介面
（`Textarea`、`RichText`、`Markdown`、`Code`、`Json`）併用合法、不受影響。

同一個屬性上同時有 `[ColumnShape]` 與明寫的 `ColumnDataType` 時，`ColumnDataType` 會被忽略，
而且不會有任何警告。

**`JsonElement` 在 `JsonDocument` 釋放後不可用。** 用 `using var doc =
JsonDocument.Parse(raw)` 解析 `Json` 欄位存的原始文字，再把 `doc.RootElement` 回傳到
`using` 區塊之外，下一次讀取會丟出 `ObjectDisposedException`——`JsonElement` 只有在產生它的
`JsonDocument` 還活著時才有效。改用 `JsonSerializer.Deserialize<JsonElement>(raw)`，不需要
`using`，拿到的是一個可以安全持有的獨立值。

**手動宣告的 `List<>` 屬性要自己標 `IsJson`。** `[CmsField]` 介面落在 `MultiSelect`／
`CheckboxGroup`／`Tags`／`KeyValue`／`Files`／`Repeater` 以外的 `List<>` 屬性，CodeFirst hook
不會替它套用 JSON 對映——SqlSugar 無法直接映射 list，得自己寫 `[SugarColumn(IsJson = true)]`。
寫了之後，單獨 `IsJson` 的加寬邏輯會接手補上欄位型別；若另外明寫 `ColumnDataType`（例如
PostgreSQL 原生的 `jsonb`），加寬邏輯會尊重它、不去動它——但這條路徑沒有測試涵蓋，效果不保證。

反過來，介面本身就是六個 JSON 介面之一的屬性，hook 會無條件把它設成 `IsJson` 加長文字，不
會讀你寫的 `ColumnDataType`——在 `MultiSelect` 欄位上釘 `jsonb` 不會生效，也不會有警告。

## 新增自訂欄位編輯器

下面把 `Color` 介面預設的 `TextField` 換成一個原生 color input。每一個欄位編輯器元件都吃同
樣的三個 prop、發同一個事件：`field`（已解析的欄位 metadata）、`modelValue`（表單目前的值）、
可選的 `disabled`，變更時發回 `update:modelValue`：

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

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
