# 14. 管理後台 SPA 客製化

管理後台 SPA(`frontend/`)是一個 Vue 3 + PrimeVue + Pinia 應用程式，幾乎完全由 API 在
`GET /api/schema` 曝光出來的中介資料所驅動。本章談的是真正屬於程式碼、而非中介資料的那一部分：主題設計、
i18n、欄位編輯器、品牌設定，以及開發伺服器如何連到 API——還有這些各自位於 `frontend/src` 之中的哪個
位置。

## 何時該客製化，何時中介資料就已足夠

大部分看起來像「管理 UI 工作」的事，其實都不是。第 4 章展示了：光是新增一個帶有 `[CmsField]` 的
`[CmsCollection]`，就足以讓一個完整的集合 (collection)——側邊欄項目、以 schema 驅動欄位的分頁列表，
以及一個新增/編輯表單——出現，完全不需要碰任何 Vue 程式碼。第 2 章「完全沒有 `Content` 導覽群組」(零
集合時)與第 4 章「確認管理後台 SPA 的側邊欄現在會顯示一個含有你的集合的 `Content` 導覽群組」(一旦有
集合存在後)，描述的正是同一套由 schema 驅動的渲染機制，只是前後對照。管理後台 SPA 從不硬編碼任何集合
的欄位、欄或標籤；schema 端點是這些資訊唯一的來源。

只有在需求無法表達為中介資料時，才需要伸手進入 `frontend/src`：

- 一個欄位需要一種 33 種出貨介面(第 5 章的表格)都沒有提供的編輯體驗——例如一個真正的色票選色器，取代
  `Color` 目前純文字輸入的做法——取代或擴充欄位型別註冊表中的一筆項目(下方)。
- 品牌需要的不只是一個名稱與一個 logo(自訂 CSS、額外的 topbar 內容)——這屬於主題設計。
- 管理 UI 本身需要說另一種人類語言——這屬於 i18n。
- 某個工作流程完全不符合一般的列表/表單模式(一個儀表板小工具、一個客製化精靈)——這需要一個新的
  view 或元件，就跟任何 Vue 應用程式一樣。

其他一切——新集合、新欄位、關聯、驗證、權限——都屬於第 4/5/7/12 章的範疇，完全不需要改動
`frontend/src` 底下的任何東西。

## `frontend/src` 目錄地圖

| 目錄 | 內容 |
|---|---|
| `api/` | 每個 REST 資源各有一個薄模組——`apiClient.ts` 是共用、能理解信封格式的 fetch 包裝器；`itemsApi.ts`、`schemaApi.ts`、`filesApi.ts`、`languagesApi.ts`、`rbacApi.ts`、`settingsApi.ts`、`appConfigApi.ts`——都是型別化呼叫，不含任何商業邏輯。 |
| `assets/` | `theme.css`——OKLch 調色盤 token(外加少數 `--legacy-*` 前綴的 token)，供尚未遷移離開 PrimeVue 的畫面讀取；原本同一個檔案裡的 shell/版面 CSS(`.shell`、`.topbar`、`.sidebar`、`.nav-item`……)已經在那些畫面改用 Tailwind utility 之後被刪除。`tokens.css`——Tailwind v4 的進入點(`@import "tailwindcss"`)，以及已遷移畫面使用的 shadcn 語意 token 層(`--background`、`--primary`、`--radius`……)。 |
| `components/` | 直接位於 `components/` 底下的 `ItemForm.vue`(生成出來的項目表單)，加上 `ui/`(供應商 shadcn 原子元件——`button`、`table`、`select`、`dialog`、`sidebar`……——生成輸出；**唯讀**，不得編輯、也不得對它 `:deep()`)、`data/`(`DataTable`、`SortableHeader`、`DataTablePagination`、`FilterBuilder`——`CollectionListView` 賴以建構的 TanStack-table 列表基元)、`fields/`(每個欄位介面各一個編輯器元件，第 5 章)、`common/`(`PageHeader`，以及 `ListToolbar`/`TableFooter`——`MediaLibraryView` 仍在使用，但 `CollectionListView` 已改用 `data/` 的基元)、`shell/`(topbar、側邊欄導覽項目、主題切換器、UI 語言切換器、品牌標誌)、`media/`、`revisions/`、`rbac/`。 |
| `composables/` | 跨切面的響應式邏輯，例如 `useConfirm.ts`。 |
| `i18n/` | `index.ts`——`vue-i18n` 執行個體(`legacy: false`)，接到 `locales/`。 |
| `layouts/` | `AppShell.vue`——每個已驗證路由都渲染於其中的 topbar + 側邊欄 + 內容格線。 |
| `lib/` | 不依賴框架的輔助函式：`fieldTypes/`(欄位型別註冊表，下方)，加上 view 與欄位元件共用的格式化/驗證/查詢輔助函式。 |
| `locales/` | `en.ts` / `zh-TW.ts`——管理 UI 自身的訊息目錄，有別於內容語言(第 6 章)。 |
| `router/` | `index.ts`(路由)、`guard.ts`(驗證/權限導覽守衛)。 |
| `stores/` | Pinia store：`authStore`、`appConfigStore`、`schemaStore`、`themeStore`、`uiLocaleStore`、`sidebarStore`、`languageStore`。 |
| `theme/` | `preset.ts`(自訂的 PrimeVue Aura preset)；`resolveInitialTheme.ts` / `resolveInitialUiLocale.ts`(首次繪製時的 `localStorage`/media-query 解析，在任何 store 存在之前就會被讀取)。 |
| `types/` | `schema.ts`——後端 DTO 的 TypeScript 對應(`FieldMeta`、`CollectionMeta`、`RelationMeta`……)。 |
| `views/` | 每個路由各一個元件：`DashboardView`、`CollectionListView`、`ItemFormView`、`MediaLibraryView`、`SettingsView`、`LoginView`。 |

## Design token 與佈景主題

有兩層需要協同運作，而且必須同步變更才能讓一次重新換主題保持一致：

1. **`frontend/src/assets/theme.css`**——單純的 CSS 自訂屬性，核心調色盤(`--bg`、`--surface`、
   `--fg`、`--muted`、`--border`、`--accent`)採用 OKLch，加上以純十六進位表示的
   `--success`/`--warn`/`--danger`(淺色為 `#16a34a`/`#d97706`/`#dc2626`，深色為
   `#4ade80`/`#fbbf24`/`#f87171`——不是 OKLch)，半徑、陰影、`--sidebar-w`，在 `:root` 上為淺色宣告
   一次，並在 `.app-dark` 上為深色重新宣告一次。同一個檔案中所有的 shell/版面 CSS(`.shell`、
   `.topbar`、`.sidebar`、`.nav-item`……)都讀取這些變數——絕不硬編碼任何顏色。
2. **`frontend/src/theme/preset.ts`**——一個 PrimeVue `definePreset(Aura, …)`(`StruoPreset`)，把
   PrimeVue 自身的語意 token(`primary`、`surface`，以及逐色彩配置的 `color`/`hoverColor`/
   `activeColor`)對應到**同一組**調色盤上(主色用 `sky`，表面色用 `slate`)，這樣 PrimeVue 自己的
   元件(按鈕、輸入框、對話框)就會與 `theme.css` 手工設計的 shell 保持一致，而不會與它產生落差——該
   檔案自己的註解就明白說明了這一點(「與 assets/theme.css 相同的調色盤，讓兩層一起翻轉」)。

兩者都在 `frontend/src/main.ts` 中註冊一次：

```ts
app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })
```

`darkModeSelector: '.app-dark'` 是這兩層之間的連結：`themeStore.apply()` 會在 `<html>` 上切換
`.app-dark` 這個 class，同時翻轉 `theme.css` 的自訂屬性(一次單純的 CSS 選擇器比對)與 PrimeVue 自身
的深色模式 token 集合(它自己的 `darkModeSelector` 機制)——一個 class，兩套系統，沒有任何東西需要
另外手動保持同步。初始模式會在 Pinia/Vue 都還不存在之前，於 `theme/resolveInitialTheme.ts` 中解析：
先看 `localStorage` 中有沒有已儲存的 `struo.theme`，否則看 `prefers-color-scheme`，否則使用淺色模式。

要為一個仍在 PrimeVue 上的畫面重新換主題：編輯 `preset.ts` 中的 `struoPresetConfig` 語意 token(把
`sky`/`slate` 換成不同的 PrimeVue 調色盤 token，或手寫 OKLch 值)，並同步編輯 `theme.css` 的
`:root`/`.app-dark` 區塊中對應的自訂屬性。

對於已經遷移到 Tailwind/shadcn 的畫面，要改編輯的 token 層是**`frontend/src/assets/tokens.css`**——
shadcn 的語意自訂屬性(`--background`、`--foreground`、`--primary`、`--radius`……)，在 `:root` 上為淺色
宣告一次，並在 `.app-dark`(與 `preset.ts`/`theme.css` 相同的切換 class)上為深色重新宣告一次。
**`frontend/src/components/ui/` 是供應商生成的唯讀輸出**(不得編輯、也不得對它 `:deep()`)——重新換
主題要改的是 token 層(`tokens.css`)，或是一個位於 `ui/` 之外、組合其原子元件的包裝元件，絕不是
`ui/` 內部的檔案。

第 3 章的 `Branding:Name`/`Branding:LogoUrl` 只會觸及產品名稱與 logo，永遠不會觸及色彩調色盤——調色盤
是原始碼中的樣板預設值，而不是一個逐部署的設定鍵。

## 覆寫 PrimeVue 內建的樣式

**注意：** `theme.css` 中一條相同特異度 (specificity) 的規則，並不保證能穩定勝過 PrimeVue 元件自身於
執行期注入的樣式。PrimeVue 是以它自己的樣式表出貨元件 CSS，而不是作為 `theme.css` 層疊的一部分——
`theme.css` 中一條裸的 `.p-select { … }`，在特異度上與 PrimeVue 自己的 `.p-select` 規則打平，而誰
勝出就取決於注入/來源順序，而不是意圖。這個問題已經在這個程式碼庫中發生過一次，就在 UI 語言切換器上：
`theme.css` 曾經有一條 `.topbar .lang-switcher { display: none; }` 規則，其註解寫明它需要「0,2,0 的
特異度：必須勝過 PrimeVue 執行期注入的 `.p-select{display:inline-flex}`」。`UiLanguageSwitcher.vue`
後來已經從 PrimeVue 的 `Select` 改用 shadcn/reka-ui 的版本(`@/components/ui/select`)，並改成在自己的
trigger 上用一個 Tailwind utility(`max-[520px]:hidden`)在最窄的視窗寬度下隱藏自己——一旦元件本身
不是 PrimeVue 的，就不需要打這場特異度戰了。

**這個技巧、這一節，都只適用於仍在 PrimeVue 上的畫面。** 一個已經遷移到 Tailwind/shadcn 的畫面，
本來就沒有 PrimeVue class 可打；用純 Tailwind utility 或 `tokens.css` 來設計樣式即可。對於仍在
PrimeVue 上的畫面，**用一個複合選擇器來拉高特異度**，而不是一個裸的 PrimeVue class——這個程式碼庫裡
現在還活著的模式，是在一個 `<style scoped>` 區塊之中，把一個 component-scoped 的 `:deep()` 搭配一個
真正的祖先 class：`frontend/src/components/ItemForm.vue` 的
`.field :deep(.p-select), .field :deep(.p-multiselect), .field :deep(.p-treeselect) { width: 100%;
max-width: 480px; }`，或是 `LoginView.vue` 的
`.field :deep(.p-inputtext), .field :deep(.p-password) { … }`。`:deep()` 本身並不會拉高特異度——
要把它搭配一個祖先 class 才行。(同一個想法也可以不用 `:deep()`，改成在未 scoped 的 `theme.css` 中寫
一個普通複合選擇器，用於不在 `<style scoped>` 區塊之內的規則——上面那條已刪除的
`.topbar .lang-switcher` 規則就是那個變體——但這個程式碼庫目前已經沒有這個變體存活的範例了。)

在這裡要避免使用 `!important`：它贏得了眼前這一次覆寫，卻讓*下一次*覆寫——不管是你自己的還是某個 fork
的——在更糟的一層上打同一場仗。

## UI 語言與 i18n 命名空間

管理 UI 自身的介面語言(選單標籤、按鈕、toast、驗證訊息)與內容語言(第 6 章的 `languages` 資料表 /
`Translatable` 欄位)完全獨立——它從不觸及 API。它是在 `frontend/src/i18n/index.ts` 中啟動的
`vue-i18n`(`legacy: false`)，在 `frontend/src/locales/` 底下註冊了兩個訊息目錄：`en.ts` 與
`zh-TW.ts`(預設值；`fallbackLocale: 'en'`)。每個目錄都是一個單純的巢狀物件——`common`、`nav`、
`dashboard`、`collectionList`、`itemForm`、`media`、`revisions`、`rbac`、`settings`、`fields`(本身
還巢狀了一個給 TipTap 工具列用的 `richtext` 子命名空間)之類的命名空間——而且所有檔案都必須宣告相同
的鍵；若某個鍵在另一個語言中缺漏，`en` 就是回退來源。

目前使用中的語言是一個 `UiLocale`(`'zh-TW' | 'en'`，`frontend/src/theme/resolveInitialUiLocale.ts`)，
會在任何 store 存在之前先被解析(`localStorage['struo.uiLocale']`，否則使用寫死的預設值
`'zh-TW'`)，接著在執行期由 `uiLocaleStore` 這個 Pinia store 擁有：`set(locale)` 會更新
`i18n.global.locale.value`，設定 `<html lang>`，並把選擇持久化回 `localStorage`。
`UiLanguageSwitcher.vue` 是唯一會呼叫它的地方，由 shadcn/reka-ui 的 `Select` 驅動
(`@/components/ui/select`，不是 PrimeVue 的)，其兩個選項分別讀取
`t('lang.zh-TW')` / `t('lang.en')`。

**要新增一個 UI 語言**(例如日文)：

1. 新增 `frontend/src/locales/ja.ts`，匯出與 `en.ts` 相同的鍵結構——每一個命名空間、每一個鍵。沒有任何
   自動化的完整性檢查；缺漏的鍵會靜默回退到 `en` 的值。
2. 在 `frontend/src/i18n/index.ts` 的 `messages` 映射中註冊它：
   `messages: { 'zh-TW': zhTW, en, ja }`。
3. 把 `theme/resolveInitialUiLocale.ts` 中的 `UiLocale` 放寬成 `'zh-TW' | 'en' | 'ja'`，以及它的驗證
   檢查(`saved === 'zh-TW' || saved === 'en' || saved === 'ja'`)。
4. 把這個選項加進 `UiLanguageSwitcher.vue` 的 `options` 陣列，並在每一個語言檔案中加入一個 `lang.ja`
   鍵——每個目錄都要為切換器自己的標籤命名每一個語言，包括自己。

## 新增一個自訂欄位編輯器

第 5 章已經用 `Color` 作為一個範例，說明其出貨的編輯器(`TextField`——「純文字輸入，沒有色票選色器」)
是刻意做得很陽春的欄位介面。從頭到尾替換它，正好展示了完整的註冊合約：註冊一個元件、接收欄位目前的值、
發出一個變更事件。

每一個欄位編輯器元件都遵循相同的三個 prop 與一個事件(見
`frontend/src/components/fields/TextField.vue` / `BooleanField.vue`)：

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

`field` 是來自 `frontend/src/types/schema.ts` 的已解析 `FieldMeta`(label、`maxLength`、
`options`……)，`modelValue` 是該欄位在表單狀態中目前的值，`disabled` 則在欄位為唯讀或表單正在送出時
被向下傳遞。`FieldInput.vue`——每一個生成出來的表單實際渲染時所用的分派器——會透過註冊表查找該元件，
並轉發這三者，因此一個新編輯器完全不需要知道自己是被渲染在一個生成出來的表單之中。

**1. 撰寫元件**——`frontend/src/components/fields/ColorSwatchField.vue`：

```vue
<script setup lang="ts">
import ColorPicker from 'primevue/colorpicker'
import InputText from 'primevue/inputtext'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// PrimeVue's ColorPicker works in bare hex ("ff0000"); the stored/API value is "#ff0000".
function onPick(hex: string): void {
  emit('update:modelValue', `#${hex}`)
}
</script>

<template>
  <div class="color-swatch-field">
    <ColorPicker
      :model-value="(modelValue as string)?.replace(/^#/, '') ?? ''"
      :disabled="disabled"
      @update:model-value="onPick"
    />
    <InputText
      :model-value="(modelValue as string)"
      :disabled="disabled"
      :maxlength="field.maxLength ?? undefined"
      @update:model-value="(v) => emit('update:modelValue', v)"
    />
  </div>
</template>

<style scoped>
.color-swatch-field { display: flex; align-items: center; gap: 8px; }
</style>
```

**2. 註冊它**——在 `frontend/src/lib/fieldTypes/registry.ts` 中，替換 `color` 項目的元件。那筆項目
的其他一切——`def({ … })` 的預設值、剖析與序列化行為，以及它的 `asString` 欄清單格式化函式——對於一個
純字串欄位而言仍然正確，所以只需要改變元件：

```ts
import ColorSwatchField from '../../components/fields/ColorSwatchField.vue'
// …
color: def({ component: ColorSwatchField, listColumn: asString }),
```

那一行就是整個註冊過程：`FieldInput.vue` 會解析
`getFieldType(field.interface).component`，並針對每一個集合中每一個 `Color` 介面欄位渲染它，
不需要任何逐集合的接線。

`frontend/src/lib/fieldTypes/types.ts` 中的 `FieldInterface`/`ALL_FIELD_INTERFACES` 是**第 5 章已經
記載過的 33 種介面所組成的封閉聯集**——它自己的註解寫著必須「保持與後端同步」，而且它本身無法單靠前端
就擴充：新增一個真正全新的介面值(相對於像上面那樣替換一個既有介面的編輯器)也意味著要新增到後端的
`FieldInterface` enum，以及 `MetadataScanner` 推理到它的每一個地方，這已經超出了單純管理 SPA 客製化
的範圍。

## 品牌設定：應用程式內的站台設定 相對於 `appsettings` 預設值

有兩個獨立的層級，會設定登入頁面、topbar(`BrandMark.vue`)以及瀏覽器分頁標題所顯示的產品名稱與 logo：

- **`appsettings.json` 的 `Branding:Name` / `Branding:LogoUrl`**(第 3 章)是部署期的預設值，只在
  啟動時讀取一次。
- **應用程式內的編輯器**——`SettingsView.vue`，一個僅限超級管理員使用的「站台設定 → 品牌」表單——會
  呼叫 `PUT /api/settings/branding`(`frontend/src/api/settingsApi.ts`：`{ brandName, logoFileId }`
  →`{ brandName, brandLogoUrl }`)，並儲存在單例的 `site_settings` 資料庫資料列中。

匿名的 `GET /api/config`——在 `main.ts` 中於應用程式掛載之前、啟動時就先被抓取一次，因此品牌設定永遠
不會閃現——會逐欄位解析出實際生效的品牌：若存在已儲存的 `site_settings` 值，就以它為準，否則回退到
`appsettings.json` 的預設值。`appConfigStore.brandName` / `brandLogoUrl` 保存的是 `/api/config`
回傳的內容；`BrandMark.vue` 在設定了 `brandLogoUrl` 時渲染 logo 圖片，否則從 `brandInitial` 渲染一個
單字母的標誌。`main.ts` 也會讓瀏覽器分頁標題與 `appConfig.brandName` 保持響應式同步(是一個
`watch`，不是一次性設定)，因此一次應用程式內的改名會立即生效，不需要重新載入——重新啟動 API 行程只有
在要變更*部署期預設值*時才需要，依第 3 章所述。

## 開發代理伺服器設定，以及把 SPA 指向另一個 API

`frontend/vite.config.ts` 的開發伺服器會把同源的 `/api` 請求代理到 API：

```ts
server: {
  port: 5173,
  host: '127.0.0.1',
  proxy: { '/api': { target: 'http://localhost:5221', changeOrigin: true } },
}
```

這就是為什麼第 2 章的走查完全不需要任何 CORS 設定：瀏覽器永遠只會與 `http://localhost:5173`
通訊，Vite 會在伺服器端把 `/api/*` 轉發到 `5221` 上的 API。要把開發用 SPA 指向另一個 API 執行個體——
不同的埠、一台遠端開發機、一個容器——就在這裡改變 `target`。這個同源模式並沒有另一個獨立的前端端
base-URL 設定：`frontend/src/api/apiClient.ts`(以及 `filesApi.ts`/`richTextImages.ts`)永遠回退到
相對的 `/api/...` 路徑，並完全仰賴這個代理(或在正式環境中，仰賴 SPA 與 API 是從同一個來源提供)來
連到後端。

### `VITE_API_BASE_URL`：真正的跨來源(SPA 與 API 位於不同來源)

上面的代理只有在 SPA 與 API 是從同一個來源提供時才有效(直接如此，或透過開發代理代為扮演一個來源)。
對於一個 SPA 真正是從與 API 不同來源提供的部署，請在 `frontend/.env` 中把 `VITE_API_BASE_URL` 設為
API 的完整來源(例如 `https://api.example.com`)(複製已受版控的 `frontend/.env.example`，它記載了
同一組預設值/覆寫的區分)。`apiClient.ts`、`filesApi.ts` 與 `richTextImages.ts` 都會各自讀取
`import.meta.env.VITE_API_BASE_URL`，若未設定則回退到 `/api`——這是一個 Vite 建置期變數，因此變更它
需要重新建置/重新啟動開發伺服器，或一次新的正式環境建置，而不只是重新整理頁面。

這個模式也需要一個對應的後端變更：設定 `Struo:Cors:AllowedOrigins`(例如
`Struo__Cors__AllowedOrigins__0=https://app.example.com`)來允許 SPA 的來源
(`src/Struo.Api/Auth/CorsWiring.cs` 會讀取這個鍵；空白/缺席代表不允許任何來源)。設定任何一個允許的
來源，也會把驗證 cookie 從 `SameSite=Lax` 翻轉成 `SameSite=None`，**並**強制開啟 `Secure`
(`src/Struo.Api/Auth/AuthWiring.cs`)——瀏覽器只有在 HTTPS 之下才會認可 `SameSite=None`，因此在這個
模式下，SPA 與 API 都必須以 HTTPS 提供服務；它在純 HTTP 之下無法運作。

## 接下來該去哪

- 第 4 章 [定義一個集合](04-defining-a-collection.md) 與第 5 章
  [欄位型別與介面](05-field-types.md)，涵蓋驅動管理後台 SPA 大部分內容、且完全不需要這裡任何程式碼的
  中介資料。
- 第 6 章 [國際化](06-internationalization.md)，涵蓋內容語言，與這裡所涵蓋的 UI 語言有所區別。
- 第 15 章 [部署、維運與測試](15-deployment-operations-testing.md)，涵蓋在正式環境建置與提供這個
  SPA，以及它自己的測試層(`pnpm test`、`pnpm e2e`)。
