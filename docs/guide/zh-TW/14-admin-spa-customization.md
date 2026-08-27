# 14. 管理後台 SPA 客製化

管理後台 SPA(`frontend/`)是一個 Vue 3 + Tailwind v4 + shadcn-vue + Pinia 應用程式，幾乎完全由 API 在
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
| `assets/` | `theme.css`——這個應用程式沒有分層 (unlayered) 的全域樣式層：第一方 scoped CSS 所讀取的頁面/表面/前景色以及狀態/陰影/遮罩/字型自訂屬性(`--bg`、`--surface`、`--fg`、`--warn`、`--danger`、`--shadow-*`、`--overlay`、`--font`、`--mono`……)，加上 `html`/`body` 重設、主題切換轉場規則，以及 `.app-breadcrumb`。`tokens.css`——Tailwind v4 的進入點(`@import "tailwindcss"`)，以及供應商 `ui/` 元件與 Tailwind utility 兩者共同讀取的 shadcn 語意 token 層(`--background`、`--primary`、`--radius`……)。 |
| `components/` | 直接位於 `components/` 底下的 `ItemForm.vue`(生成出來的項目表單)，加上 `ui/`(供應商 shadcn 原子元件——`button`、`table`、`select`、`dialog`、`sidebar`……——生成輸出；**唯讀**，不得編輯、也不得對它 `:deep()`)、`data/`(`DataTable`、`SortableHeader`、`DataTablePagination`、`FilterBuilder`——`CollectionListView` 賴以建構的 TanStack-table 列表基元)、`fields/`(每個欄位介面各一個編輯器元件，第 5 章)、`common/`(`PageHeader`、`ListToolbar`——都是純 Tailwind/shadcn 元件；`MediaLibraryView` 使用 `ListToolbar` 作為搜尋框與篩選插槽，而 `CollectionListView` 則是建構在 `data/` 的基元之上)、`shell/`(topbar、側邊欄導覽項目、主題切換器、UI 語言切換器、品牌標誌)、`media/`、`revisions/`、`rbac/`。 |
| `composables/` | 跨切面的響應式邏輯，例如 `useConfirm.ts`。 |
| `i18n/` | `index.ts`——`vue-i18n` 執行個體(`legacy: false`)，接到 `locales/`。 |
| `layouts/` | `AppShell.vue`——每個已驗證路由都渲染於其中的 topbar + 側邊欄 + 內容格線。 |
| `lib/` | 不依賴框架的輔助函式：`fieldTypes/`(欄位型別註冊表，下方)，加上 view 與欄位元件共用的格式化/驗證/查詢輔助函式。 |
| `locales/` | `en.ts` / `zh-TW.ts`——管理 UI 自身的訊息目錄，有別於內容語言(第 6 章)。 |
| `router/` | `index.ts`(路由)、`guard.ts`(驗證/權限導覽守衛)。 |
| `stores/` | Pinia store：`authStore`、`appConfigStore`、`schemaStore`、`themeStore`、`uiLocaleStore`、`sidebarStore`、`languageStore`。 |
| `theme/` | `resolveInitialTheme.ts` / `resolveInitialUiLocale.ts`——首次繪製時的 `localStorage`/media-query 解析，在任何 store 存在之前就會被讀取。 |
| `types/` | `schema.ts`——後端 DTO 的 TypeScript 對應(`FieldMeta`、`CollectionMeta`、`RelationMeta`……)。 |
| `views/` | 每個路由各一個元件：`DashboardView`、`CollectionListView`、`ItemFormView`、`MediaLibraryView`、`SettingsView`、`LoginView`。 |

## Design token 與佈景主題

有兩層需要協同運作，而且兩者都在同一個 `.app-dark` 切換 class 上翻轉——沒有任何東西需要另外手動
保持同步：

1. **`frontend/src/assets/tokens.css`**——Tailwind v4 的進入點(`@import "tailwindcss"`)，以及 shadcn
   的語意自訂屬性(`--background`、`--foreground`、`--primary`、`--radius`、`--sidebar-*`……)，在
   `:root` 上為淺色宣告一次，並在 `.app-dark` 上為深色重新宣告一次。`@theme inline` 會把每一個屬性
   對應到 `src/components/ui/` 與第一方元件共同使用的 Tailwind utility class(`bg-background`、
   `text-primary`……)上。
2. **`frontend/src/assets/theme.css`**——這個應用程式沒有分層 (unlayered) 的全域樣式層：第一方
   scoped CSS 所讀取的頁面/表面/前景色以及狀態/陰影/遮罩/字型自訂屬性(`--bg`、`--surface`、`--fg`、
   `--warn`、`--danger`、`--shadow-*`、`--overlay`、`--font`、`--mono`)，加上 `html`/`body` 重設、
   主題切換轉場規則，以及 `.app-breadcrumb`。它會在 `tokens.css` 之後載入，而且其中沒有任何一條規則
   位於 Tailwind 的 `@layer` 之中，因此它們會勝過套用在同一個元素上的任何 utility class，無論特異度
   高低——沒有分層的宣告會贏過有分層的宣告(除非有分層那一方帶有 `!important`，那會反過來)，這正是
   手寫 CSS 覆寫最常見的「靜默失效」原因。

兩者都在 `frontend/src/main.ts` 中匯入一次：

```ts
import './assets/tokens.css'
import './assets/theme.css'
```

`themeStore.apply()` 會在 `<html>` 上切換 `.app-dark` 這個 class，一次翻轉兩個檔案的自訂屬性(一次
單純的 CSS 選擇器比對)——不需要另外通知任何東西模式已經改變。初始模式會在 Pinia/Vue 都還不存在之前，
於 `theme/resolveInitialTheme.ts` 中解析：先看 `localStorage` 中有沒有已儲存的 `struo.theme`，否則
看 `prefers-color-scheme`，否則使用淺色模式。

要為整個 SPA 重新換主題，請編輯 `tokens.css` 中 `:root`/`.app-dark` 區塊裡的語意自訂屬性——這是每個
供應商 `ui/` 元件與 Tailwind utility 都讀取的契約——如果這次變更也牽涉到側邊欄或麵包屑之類的第一方
外框元素，就同時編輯 `theme.css` 中對應的屬性。

第 3 章的 `Branding:Name`/`Branding:LogoUrl` 只會觸及產品名稱與 logo，永遠不會觸及色彩調色盤——調色盤
是原始碼中的樣板預設值，而不是一個逐部署的設定鍵。

### 編輯器的互動表面

`richText` 欄位（`frontend/src/components/fields/RichTextInput.vue` 以及它周邊的檔案）有五個各自獨立
的互動表面。每一個都只負責一件事，底下幾節會逐一說明每個表面負責什麼、為什麼——先弄清楚這個分工，
之後才判斷得出一個新功能該加在哪個表面上。

| 表面 | 負責 |
|---|---|
| 工具列 | 完整、隨時可見的整組指令 |
| Bubble menu | 行內格式，在目前的文字選取範圍上提供 |
| 右鍵選單 | 對游標所在的表格或圖片做結構操作 |
| Node view | 物件自己的直接操作介面 |
| Slash 選單 | 鍵盤驅動的區塊插入 |

**工具列**是最完整的表面：它把 `richTextCommands.ts` 整份指令登記表按順序渲染成按鈕——每一個行內
標記、對齊方式、區塊轉換、連結指令、水平線、圖片，以及復原/重做——再加上三個從來沒有加入那份登記表
的控制項：標題層級下拉選單、文字顏色選取器（`RichTextColorMenu.vue`）、以及帶有自己的尺寸網格與自訂
尺寸對話框的插入表格彈出視窗。這三個都放不進登記表那個單一的 `run(editor, ctx)` 函式裡——下拉選單、
調色盤、帶尺寸的網格，各自都需要自己的一套介面。工具列一直顯示在畫面上，不需要有選取範圍才存在。

**Bubble menu** 刻意窄很多：它把同一份登記表篩選成 `group` 為 `'inline'` 的指令
（`RichTextBubbleMenu.vue` 的 `INLINE_COMMANDS`），所以它絕不可能帶有工具列沒有的指令，而且只有在有
文字選取範圍時才會出現，浮動在那個選取範圍上，不需要使用者把視線移開它。它存在純粹是為了方便——不
必跑一趟工具列就能套用一個標記——而不是提供工具列沒有的東西。

**右鍵選單**是比較特別的一個：它完全不讀那份共用的指令登記表。在表格上按右鍵，提供的是
`richTextTableActions.ts` 裡的列/欄/表頭/刪除動作；直接在圖片上按右鍵，提供的是
`richTextImageActions.ts` 裡的替代文字/刪除動作。出現哪一種選單，是由實際點擊的對象決定的——就算
圖片位於表格的儲存格裡，拿到的仍然是圖片選單——而不是由某種更籠統的「目前所在區塊」決定。

**Node view** 是物件自己的操作介面，直接渲染成物件的一部分，而不是另外一個選單或按鈕。目前唯一的
例子是圖片的八個縮放控制點：拖曳其中一個是直接作用在圖片本身的指標手勢，沒有任何工具列、選單或
extension 在背後派送指令。機制細節請見下面的〈在編輯器裡調整圖片大小〉。

**Slash 選單**是鍵盤路徑，詳見圖片縮放這節之後的獨立小節：輸入 `/` 會打開一份可插入區塊的清單，
不必離開鍵盤。它不是工具列的篩選版——它有自己另外組出來的項目清單，因為「寫作時值得用鍵盤直接
搆到的東西」（標題、區塊轉換、表格、插入水平線或圖片）跟「工具列上的每一顆按鈕」本來就不是同一組
東西。

把這五個表面分開，是為了讓「新功能該加在哪裡」變成一個機械式的判斷，而不是憑感覺。一個新的格式
標記只要加進登記表、標上 `group: 'inline'`，就會同時出現在工具列與 bubble menu 上，兩邊都不用手動
改。一個新增的、對既有表格或圖片儲存格的操作，該加進那個物件自己的動作清單，而不是共用的登記表。
物件本身的一種新的直接操作方式，該加進 node view。使用者應該不必把手從鍵盤移開就能搆到的區塊，該
加進 slash 選單自己的項目清單（下面在調整圖片大小那節之後會接著講）。把功能加在錯的表面上，結果不
是在別處重複了一份已經存在的東西，就是明明該有鍵盤路徑，卻只能用滑鼠搆到。

### 富文本編輯器的排版樣式

`richText` 欄位的可編輯區域(`frontend/src/components/fields/RichTextInput.vue`)在其 ProseMirror
內容元素上帶有 `class="prose dark:prose-invert"`，以 `@tailwindcss/typography` 的原廠預設值呈現——
沒有自訂 CSS 檔、自訂 class，也沒有自己額外的 `--tw-prose-*` 對映。目的是讓編輯畫面貼近一篇渲染出來的
文章，而不是未套樣式的純 HTML。

這是一項建議，不是要求，選擇權留給前台開發者：如果你自己的前台也用 Tailwind 與
`@tailwindcss/typography` 來渲染內容，編輯器的呈現就會與已發布的文章高度一致，因為兩邊是在同一份
儲存的 HTML 上套用同一個 plugin 的預設值。如果你的前台採用其他渲染方案，可以在
`frontend/src/assets/tokens.css`——這個 plugin 本身註冊的地方(`@plugin "@tailwindcss/typography"`)——
覆寫 `.prose` 上的 `--tw-prose-*` 這些自訂屬性，讓編輯器的外觀靠近你自己的排版。請把覆寫寫成一般規則，
不要放進 `@layer` 或 `@utility` 區塊：這個 plugin 自己的 `.prose` 規則是放在 `@layer utilities` 裡的，
跟前面 `theme.css` 那條「沒有分層的宣告會贏過有分層的宣告」一樣，無論特異度高低，一般規則都會勝過它，
放進分層裡就會靜默失效。這些屬性是 `@tailwindcss/typography` 自己的機制(請參閱該套件的文件)，不是 StruoCMS
定義或保證的契約。

編輯器與已渲染文章之間有三項刻意保留的落差，它們是接受的代價，而不是缺陷：

- **深色模式。** `dark:prose-invert` 只保證深色模式下的編輯器維持可讀，並沒有針對任何特定前台的
  深色主題調校。這個 plugin 預設值真正忠實的預覽，是淺色主題。
- **字體。** 編輯器繼承後台 SPA 自己的字體堆疊。模板刻意不替 fork 猜一個字體。
- **背景。** 編輯區坐在後台的卡片表面上，不是坐在站台的頁面背景上——前台在文章周圍放的頁面外框，
  不在這個模板的掌控範圍內。

表格表頭儲存格原本是這裡的第四項落差，現在不再是了，而且原因值得說明，因為它不是本節其他幾項那種
「兩邊套用同一個 plugin 預設值」的情況。TipTap 表格擴充功能的 schema 裡沒有 `thead` 節點，所以表頭列
一進到編輯器就一律變成 `tbody` 裡的 `th` 元素——不管表格當初是怎麼建出來的，編輯器本身都做不出
`thead`。StruoCMS 改在伺服器端、於寫入當下把這個落差補起來：清理器(sanitizer)的後處理步驟
(`TableHeadNormalizer`，位於 `src/Struo.Infrastructure/Security/TableHeadNormalizer.cs`)只要一張
表格的首列每一格都是 `th`、而且這張表格還沒有 `thead`，就把那一列包進 `thead`。因為這是清理流程的
一部分，它涵蓋所有經由 StruoCMS 內容寫入流程的 `RichText` 欄位寫入——編輯器、直接呼叫 API、GraphQL
mutation、透過 API 匯入——不只是經過 TipTap 產生的內容。從那之後，前台渲染時讀到的儲存 HTML 就帶有真正的 `thead`，
`@tailwindcss/typography` 的 `thead th` 規則就能如預期匹配到它。這個 normalizer 出現之前就已經存在的
內容，維持原本的樣子，要等到下一次重新儲存才會補上——不會回填。

編輯器仍然無法顯示這個 `thead`——它的 schema 根本沒有地方放這個節點——所以後台 SPA 另外有一條 CSS
規則，把這個 plugin 的表頭樣式鏡射到 TipTap 實際產生的、`tbody` 裡的 `th` 標記上，範圍限定在跟伺服器同一種
「首列且全部是 `th`」的情況。兩邊現在是靠兩套各自獨立的機制才走到同樣的外觀，不再只有一套：這裡的
一致性是被主動維持出來的，不是編輯器與前台渲染同一份儲存字串自然帶來的副作用。如果 fork 大幅改了自己
表格的樣式，編輯器的表頭樣式不會跟著變，因為後台那條規則鏡射的是 typography plugin 自己的預設值，
不是 fork 換上去的那一套。

「首列」這個條件是逐字生效的，因此有一個隨之而來的限制：表格右鍵選單的表頭列切換可以把任何一列標成
表頭列，不限於首列；只要那個表頭列不是首列，伺服器就會把它留在 `tbody` 裡。normalizer 刻意不會重新
排列表格內容，所以把表格中間的一列包進 `thead` 並不是一個選項——這正是已發布輸出在那裡仍然沒有
`thead` 的原因，也是編輯器的 CSS(只為伺服器同樣會包起來的那一列上樣式)同樣把那一列留成無樣式的
原因。編輯器把那一列渲染成無樣式，不是缺陷，而是如實反映：那一列發布之後就是長那樣。

以上任何一點都不代表編輯器是正式渲染結果的精確預覽：前台對 Tailwind、對 typography plugin 的客製化，
或是完全不採用這兩者的渲染方案，都不在這個模板的掌控範圍內。

### 連結對話框，以及既有連結會發生的事

工具列與泡泡選單(bubble menu)上的「連結」按鈕，現在打開的是同一個 modal 對話框
(`RichTextLinkDialog.vue`)，而不是瀏覽器自帶的 `window.prompt`，兩者共用同一個 `link` command
(`frontend/src/components/fields/richTextCommands.ts`)——每個編輯器只有一個對話框實例，兩個進入點
都是重複使用它。除了網址欄位之外，對話框還多了一個「在新分頁開啟」的核取方塊；當選取範圍本來就在
一個連結之內時，還會多一顆「移除連結」按鈕。確認對話框時，會把連結 mark 的 `target` 設成 `_blank`
或清掉它；對話框本身從來不會設定 `rel`——真正決定儲存下來的 `rel` 是什麼的，是第 5 章 `RichText`
那張契約表，而它是伺服器依實際送到清理器的 `target` 值推導出來的。

這個伺服器端推導的規則，對這個對話框出現之前就已經存在的連結會有一個後果：那些連結帶著
`rel="noopener noreferrer"`，卻完全沒有 `target`。清理流程每次都是整個 `RichText` 欄位的值一起
重新清理一次，不是只清理使用者實際動過的那一小段，所以只要這份內容再被存一次——即使沒有人動過那條
連結本身——清理器看到它沒有 `target`，就會套用跟其他同分頁連結一樣的規則，把它沒有 `target` 可以
支撐的 `rel` 拿掉。那條連結就這樣悄悄變成 `<a href="…">`。因為這些連結本來就是在同一個分頁開啟，
`noopener` 對它們原本就沒有作用；真正改變的是它們不再抑制那次點擊的 Referer 標頭。這裡不會回填——
跟本章前面談 `thead` 時的立場一樣——所以一個累積了大量既有連結的 fork，應該預期這個 `rel` 會一次
存檔、一次存檔地逐漸消失，而不是一次性全部消失。

### 在編輯器裡調整圖片大小

調整 `richText` 欄位裡插入的圖片大小要兩個步驟:先選取那張圖片——點一下就夠了——然後拖曳出現的八個控
制點中的任何一個,也就是四個角落加上四個邊的中點。控制點只在那張圖片是編輯器目前的選取對象時才顯
示;游標在別的地方時,圖片上一個控制點也沒有。這個「先選取」的前提來自本 repo 的 CSS，不是上游的行
為，負責的那條規則在下面說明。至於拖曳本身，就不是 StruoCMS 自己寫的專屬 node view:它完全來自上
游——`@tiptap/extension-image` 自己的 `resize` 選項，在 `RichTextInput.vue` 裡設定在 `Image` 擴充
功能上 (`resize: { enabled: true, minWidth: 40, alwaysPreserveAspectRatio: true, directions:
[...全部八個] }`),換上 `@tiptap/core` 的 `ResizableNodeView` 來處理每一個圖片節
點。`alwaysPreserveAspectRatio: true` 讓每一次拖曳都鎖定圖片自己的長寬比——上游自己的預設值只有按
住 Shift 時才會這樣做——而且這對八個控制點的效果完全一樣:抓住哪一個控制點只決定長寬比計算時哪個軸
是主軸，不會決定長寬比要不要鎖定。位置也一樣不受影響:`ResizableNodeView` 對任何一個控制點都不會重
新定位元素本身，所以每一次拖曳都是從圖片的左上角開始放大或縮小——往內拖左邊的控制點,並不會把右邊固
定住再往左長,不是一般設計工具那種控制點的行為。`minWidth` 則避免某個控制點把圖片拖成一個小到無法
操作的目標。

圖片永遠不可能被拖曳超過欄位本身的文字量測欄寬——顯示出來的寬度(以及放開之後儲存下來的寬度)
就是在那裡被限制住的。這是在實際執行中的後台量測出來的:在一個約 603px 的欄寬裡把 inline width
強制設成 2000px,圖片渲染出來是 603px,而 `ResizableNodeView` 實際提交的那個數字
`offsetWidth` 讀到的也是 603。真正做出這個限制的是 Tailwind preflight 自己的
`img { max-width: 100% }`,不是 `RichTextInput.vue` 宣告的任何一條規則——一個 fork 如果拿掉
preflight、或是針對編輯器內容覆寫掉那條規則,就會失去這個限制,並且會把拖曳出來的數字直接存
下去。同樣量測過、給走上那條路的 fork 的資訊:八個縮放控制點會跟著圖片一起跑出去,超過
`AppShell` 加在內容欄上的 `overflow-x-clip`,於是一張被拖過頭的圖片就再也拖不回來了。維持原狀
的話,對一個有固定閱讀欄寬的欄位來說,這是正常的 WYSIWYG 行為,不是 bug:編輯器在拖曳時顯示的
樣子,就是發布之後會渲染出來的樣子。

這個限制對 fork 造成的後果是:儲存下來的 `width` 永遠不可能超過後台自己的 prose 量測欄寬——在預設
的 65ch 之下大約是 603px。如果你發布用的文章模板比後台的閱讀欄寬更寬,那就沒有任何一條編輯器上的
路徑可以做出比那更寬的圖片:使用者拖不過那個欄寬,所以唯二的辦法是把編輯器自己的量測欄寬改寬,或
是直接透過 API 寫入 `width` 屬性。

本 repo 提供的是控制點的外觀、它們什麼時候顯示，再加上它定位的其中一部分。`ResizableNodeView` 在
自己的 constructor 裡就附加每一個控制點，而且只有在編輯器不再可編輯時才會移除它們——用絕對定位加
上 `data-resize-handle` 屬性——但完全沒有設定它自己的大小、背景色或游標，所以沒有 CSS 的話，每個控
制點雖然存在於 DOM 裡，卻是 0×0、看不見也點不到。`RichTextInput.vue` 自己的 `<style scoped>` 區塊
提供了那份樣式，鎖定 `[data-resize-handle]` 這個屬性選擇器 (大小、背景色、圓角、八個方向各自的游
標樣式)。那個區塊裡有三條規則做的不只是裝飾，fork 應該知道每一條各撐著什麼。

第一條把所有 `[data-resize-container]` 沒有帶 `.ProseMirror-selectednode` 的控制點藏起來——也就是
上面說的那個「先選取」前提。它用的是 `display: none` 而不是任何透明度處理，這是刻意的:隱藏狀態必
須連命中測試都退出，而不只是看不見，而做到這件事的正是把那個框整個拿掉——一個什麼都不顯示、卻仍然
會在自己中心點被命中測試打到的控制點，比原本那個視覺缺陷更糟。把這條規則刪掉，可編輯欄位裡的每一
張圖片就會回到不管游標在哪裡都帶著八個活的控制點。

第二條給四個邊控制點在長軸上一個 `auto` 外距，那正是把它們放到各自邊中點上的東西。那個外距不是裝
飾性的:上游把一個邊控制點長軸的兩端都寫成行內樣式，所以把它拿掉，那四個控制點就會各自塌回旁邊的角
上，這個欄位提供的就變成四個可抓取的位置，而不是八個。

第三條把每一個控制點往外拉自己尺寸的一半，讓它跨在自己抓的那條邊上，而不是浮在圖片裡面——在那個控
制點被釘住的那幾側寫上 `calc(var(--resize-handle-size) / -2)` 的負外距。角落控制點兩個軸都要，邊
控制點只在自己的短軸上要。若改寫在邊控制點的長軸上，就會取代掉上一條規則的 `auto` 外距，讓那個控
制點塌回角上——這個陷阱值得特別點名，因為那個改動看起來很對稱。

選取圖片同時也會畫出一條外框線，來自另一條寫在
`[data-resize-container].ProseMirror-selectednode` 上的規則。fork 若想要不同的控制點樣式，就改那
個檔案裡的那些規則——沒有另外一個獨立的控制點元件可以替換，而那幾條 auto 外距必須在改動之後留下
來。

在動那個樣式區塊之前，有一個耦合要先知道:它同時也把 `[data-resize-container]` 的上下外距寫死成
`2em`，並且把 `@tailwindcss/typography` 加在 `<img>` 本身上的外距歸零。這是必要的，否則控制點會落
在一個被圖片外距撐大的框上，而不是落在圖片自己的角上;而 `2em` 就是那個外掛 `base` modifier 的
值——也就是一個沒有加尺寸後綴的 `prose` class 會解析到的值，正是這個編輯器今天套用的那一個。把編輯
器換成帶尺寸的變體(`prose-sm`、`prose-lg`⋯)會改變外掛自己的圖片外距，卻不會改變這個寫死的數字，
於是兩者會悄悄地對不起來，編輯器開始顯示發布後不會出現的圖片間距。

縮放圖片是純指標操作，這個欄位裡任何地方都沒有等效的鍵盤路徑。把鍵盤使用者擋在外面的不是那個選取
步驟——從上一段用方向鍵往下移到圖片上，確實會讓它進入 node-selected 狀態、控制點也確實會出現，這是
在實際執行中的後台驗證過的。擋住的是拖曳。控制點就只是拖曳目標而已——上游在每一個控制點上綁的是
`mousedown` 與 `touchstart`，而它另外加在 document 上的 `keydown` 只用來追蹤一次進行中的拖曳裡
Shift 鍵的狀態——而下面那個圖片右鍵選單只提供「編輯替代文字」與「刪除圖片」，這個欄位裡也沒有任何
一個對話框接受寬度輸入。因此一個只用鍵盤的使用者根本無法設定圖片寬度。控制點也很小:10px 見
方(`0.625rem`)，低於 WCAG 2.2 SC 2.5.8(Target Size (Minimum)，AA 等級)要求的 24×24 CSS 像素下限，
而且既沒有鍵盤路徑也沒有選單路徑可以退而求其次;觸控使用者拿到的也是同一個 10px 的目標。跨在邊上並
不會改變這個數字——同一個框不管置中在哪裡都一樣大——但它把其中一半移出了圖片，所以每個目標大約有
5px 疊在圖片上，另外 5px 落在圖片旁邊的頁面上。這兩點都是本模板出貨現況的既述限制，不是 fork 必須
沿用的性質。fork 可以在不改變視覺的前提下把可抓取範圍加大——在 `[data-resize-handle]` 上加一個透明
的 `::before`，放大並對齊置中於那個 10px 的圓點——但要拿一張小圖片實際檢查結果，因為每一個控制點都
置中在圖片自己的邊上，一旦圖片本身沒有比放大後的範圍大多少，就沒有任何東西能讓那些範圍彼此分
開。fork 也可以在圖片右鍵選單裡加一個寬度控制項。這兩者不能互相取代: 把目標加大對鍵盤使用者毫無幫
助，而一個選單控制項也完全不改善目標尺寸。

`ResizableNodeView` 還會把 `<img>` 包進兩層容器 `<div>` 裡(`[data-resize-container]` 包住
`[data-resize-wrapper]`,控制點元素則是 `<img>` 在 wrapper 裡的兄弟節點),用來容納控制點並在拖曳
過程中管理版面。這只存在於運作中的 ProseMirror 檢視畫面裡——永遠不會出現在 `getHTML()` 的輸出中，
也永遠不會出現在送進清理器、或最終儲存下來的 HTML 裡。儲存下來的 `RichText` 值裡的 `<img>` 從來
不會被包起來。

拖曳控制點也會把 `height` 寫進圖片節點自己的屬性裡，因為上游的 `onCommit` 每次調整大小之後都會
一併寫入兩個維度。`RichTextInput.vue` 把 `height` 的 `addAttributes()` 覆寫成 `rendered: false`，
讓它不會進到儲存的 HTML 裡——`getHTML()` 從一開始就不會序列化它。這跟第 5 章清理器剝除 `height`
不是重複做同一件事:清理器要防的是任何輸入路徑上、不論是不是惡意送進來的 `height`;而這個覆寫
存在的理由，是讓 `getHTML()` 的輸出本來就已經跟清理器最終會產生的結果一致——如果沒有它，這個
欄位自己比對外部變更的邏輯，每次更新都會看到一個只差在 `height` 的假差異，然後毫無理由地重置
游標位置。

那個 node view 裡有一個上游的缺陷值得明講，因為不小心發現它的時候看起來會很嚇人。
`ResizableNodeView` 在建構子裡以
`this.editor.on('update', this.handleEditorUpdate.bind(this))` 註冊了一個編輯器的 `'update'`
監聽器，又在 `destroy()` 裡以 `this.editor.off('update', this.handleEditorUpdate.bind(this))`
移除一個——實際讀了安裝的 `@tiptap/core@3.30.2`，`src/lib/ResizableNodeView.ts`。每一次呼叫
`.bind()` 都會回傳一個全新的函式物件，而 `off` 是以函式物件本身的識別 (identity) 來過濾回呼清單的
(`src/EventEmitter.ts`)，所以它移除掉的永遠不會是建構子加上去的那一個:編輯器建立的每一個圖片
node view，都會留下一個 `'update'` 監聽器直到那個編輯器生命週期結束為止。fork 不需要為此做任何
處理。累積的範圍受限於單一編輯器而不是整個工作階段——`Editor.destroy()` 會呼叫
`removeAllListeners()`，那會直接把整張回呼表清空，而 `RichTextInput.vue` 在 `onBeforeUnmount`
裡就會銷毀它的編輯器——而在那之前，一個殘留的監聽器也不會造成任何可觀察到的結果，因為
`handleEditorUpdate` 除非可編輯狀態真的改變了，否則會立刻返回，就算執行了也只會動到它自己那個
早已從文件上卸下的容器。唯一會讓它變成問題的做法，是不再於卸載時銷毀編輯器——那會把這個監聽器、
以及編輯器持有的其他每一個監聽器，都變成真正的洩漏。

在這個欄位裡按右鍵，現在會依點擊位置開啟兩種不同的右鍵選單。直接點在 `<img>` 上得到的是圖片選單
(編輯替代文字、刪除圖片);點在表格內其他任何地方，得到的是表格自己的右鍵選單。圖片的判斷先於
表格執行，所以即使圖片位於表格的儲存格裡，得到的仍然是圖片選單，不是那個儲存格的選單。兩種選單
其實是同一個 `RichTextContextMenu.vue` 元件，只是渲染不同的動作清單——圖片的動作定義在
`richTextImageActions.ts` 裡，跟表格的 `richTextTableActions.ts` 對應。

還有一個值得明講的表格細節，因為不講清楚的話它看起來會像是個疏漏:TipTap 的表格擴充功能會替這個
編輯器做得出來的每一個表格都渲染出一個 `<colgroup>`，而 `GanssHtmlSanitizer` 每次都會把它剝掉，因
為 `colgroup` 從來沒有被加進標籤允許清單。在這裡的設定之下,`renderHTML` 回傳的是
`["table", attrs, colgroup, ["tbody", 0]]`。這個形狀並不是字面上無條件的——實際讀了安裝的
`@tiptap/extension-table@3.30.2`:當擴充功能的 `renderWrapper` 選項打開時，回傳值會再被包進一層
`<div class="tableWrapper">`，而它的 `createColGroup` 輔助函式對一個沒有第一列的表格節點根本不會
回傳 colgroup——但這兩個條件在這裡都已經被定死:`renderWrapper` 預設為 `false` 且維持預設，而
`table` 節點自己的 content 運算式是 `tableRow+`，所以文件裡的表格永遠有第一列。這個表格擴充功能設定的是 `resizable: false`，所以這個編輯器本來就不會讓使用者
設定每一欄各自的寬度;在這個設定下，TipTap 每次都吐出來的那個 `<colgroup>` 不承載任何資訊，剝掉它
不會損失任何東西。這是刻意接受的現況，不是一個該靠把 `colgroup` 加進允許清單來「修好」的錯誤——
真的加了，只會開始儲存這個編輯器根本沒有辦法有意義地產生出來的欄寬資料。

那個 `renderWrapper` 選項值得一個明確的警告，因為把它打開不是改變標記而已，而是會安靜地摧毀內容。
在 `renderWrapper: true` 之下，`getHTML()` 會在每個表格外面吐出一層 `<div class="tableWrapper">`;
`div` 不在 `GanssHtmlSanitizer` 的 `AllowedTags` 裡，而清理器的 `KeepChildNodes` 維持在預設的
`false`，那會把一個不被允許的元素連同它整個子樹一起丟掉。於是儲存時被移除的是值裡的每一個表格，
而不只是那層外包裝。請維持 `renderWrapper` 關閉，或是先把 `div` 加進允許清單。

### Slash 指令

在 `richText` 欄位裡輸入 `/`，會打開一份鍵盤驅動的可插入區塊清單，實作在 `richTextSlashExtension.ts`
裡，建立在上游自己的 `@tiptap/suggestion` 之上。觸發規則是上游的，不是 StruoCMS 自己訂的：只有當 `/`
前面那個字元——在結束於游標位置的那一個 text node 裡面，而不是整個區塊裡——是空白或什麼都沒有時，
`/` 才會打開選單（`allowedPrefixes` 維持上游預設值 `[' ']`，沒有動它）。這正是為什麼「and/or」或正在
輸入中的一段 URL 路徑不會打開選單：緊接在 `/` 前面的那個字元，兩種情況下都是一個普通字元，不是空白，
也不是一個 text node 的開頭。

「什麼都沒有」這件事，比聽起來還要窄，而且值得明講成一個已知限制，而不是刻意設計：因為上游量測的邊界
是 text node 而不是區塊，一個剛好打在某個「非 inclusive」的 mark 結束、普通文字接續起來那個位置的
`/`，一樣會打開選單，因為那個位置正是一個全新 text node 的 offset 0，前面沒有任何字元可以讓前綴檢查
拒絕它。連結是真正會踩到這一點的例子：緊接在 `<a>閱讀更多</a>` 之後、中間沒有空格地輸入 `/`，會打開
選單，因為這個編輯器設定的 `Link` mark 是非 inclusive 的（它的 `autolink` 選項是 `false`，而
`inclusive()` 原封不動回傳這個選項的值）。粗體的行為不一樣，因為它沒有覆寫這個設定，於是落回
ProseMirror 自己的預設值 `inclusive: true`：緊接在粗體文字之後輸入 `/`，那個 `/` 會留在同一個粗體
text node 裡，而不是另外開一個新的，所以單靠這樣不會打開選單——只有先把 mark 解除、再輸入 `/`，才會
碰到連結會直接碰到的那種情況。這是 `@tiptap/suggestion` 比對前綴的方式，加上每個 mark 自己的
`inclusive` 設定共同帶來的結果，不是 StruoCMS 這個 extension 加上去的規則。這個限制目前就是照現狀
接受，沒有被繞掉——如果 fork 想要拿掉它，可以把 extension 自己的 `allow` callback 改成去檢查那個 `/`
自己所在位置、在「區塊」裡前面那個字元（`state.doc.resolve(range.from)` 本身就同時帶著那個區塊，以及
那個位置在裡面的 offset——`range.from` 是 `/` 所在的位置，不是游標，一旦後面開始輸入查詢字串，兩者
就會分開），而不是只依靠上游那個以 text node 為範圍的前綴檢查，但 StruoCMS 目前沒有這樣做。

**項目清單從哪裡來。** `buildSlashItems`（`richTextSlashCommands.ts`）依序從三個來源組出這份選單：
`richTextHeadings.ts` 的 `HEADING_LEVELS`（H2 到 H6）；`richTextCommands.ts` 指令登記表裡篩選出
`group: 'block'` 的項目（項目符號清單、編號清單、引用區塊、程式碼區塊）；以及一個手寫的表格項目之
後，同一份登記表篩選出 `group: 'insert'` 的項目（水平線、圖片）。這個表格項目——它插入的是一張帶
表頭列的 3×3 表格，`insertTable({ rows: 3, cols: 3, withHeaderRow: true })`——必須手寫，是因為工具列
上的表格控制項是一個帶尺寸的網格彈出視窗加上一個自訂尺寸對話框，不是單一一個指令，所以它從來沒有
加進另外兩個來源讀取的那份登記表裡。這樣選單總共有十二個項目，順序固定為：標題 2 到標題 6、項目符號
清單、編號清單、引用區塊、程式碼區塊、表格、水平線、插入圖片。

fork 要新增一個項目，該改哪個檔案取決於它屬於哪個來源：多一個標題層級，改 `HEADING_LEVELS`——但光這
樣還不夠讓它安全上線：`HeadingLevel` 是一個 `2 | 3 | 4 | 5 | 6` 的 union，需要先放寬；它的 label 來自
一個 `fields.richtext.heading${level}` 的 locale key，兩份語言檔都要跟著補上；而 heading 1 更是完全
不能這樣加——`GanssHtmlSanitizer` 的標籤允許清單特意從 `h2` 開始，正是因為頁面標題本身就是 H1，而
`KeepChildNodes` 維持在預設的 `false`，所以透過這個欄位寫進去的
`h1` 存檔時會連同它的文字一起被剝掉，而不只是被拆開標籤。相對地，一個新的區塊轉換或插入指令，乾淨地
加進 `richTextCommands.ts` 的登記表、標上 `group: 'block'` 或 `group: 'insert'`，就會自動被選單撿起
來，`richTextSlashCommands.ts` 完全不用動。像表格項目那樣沒辦法表達成單一登記表指令的東西，就得直接
寫進 `buildSlashItems` 自己的項目清單裡——包括它自己的別名，因為手寫的項目不在下面說的那份別名表
範圍內。

**別名。** 輸入在 `/` 後面的查詢字串，是以子字串比對同時比對項目翻譯後的 label 與它的別名
（`filterSlashItems`），所以就算完全不輸入別名，光靠 label 本身也能搆到一個項目——例如 `/編號`
單靠 label 就能找到「編號清單」，即使它的三個別名（`ol`、`number`、`ordered`）沒有一個包含這個查詢
字串。別名存在的目的是讓這件事更快。標題的別名是就地寫的（`h2`…
`h6`、`heading2`…`heading6`）；表格項目唯一的別名（`table`）也是跟著它一起手寫在 `buildSlashItems`
裡；其他每個從登記表衍生出來的項目，別名都來自 `richTextSlashCommands.ts` 自己的別名表：

| 項目 | 別名 |
|---|---|
| `bulletList` | `ul`、`bullet`、`list` |
| `orderedList` | `ol`、`number`、`ordered` |
| `blockquote` | `quote`、`blockquote` |
| `codeBlock` | `code`、`pre` |
| `hr` | `hr`、`divider`、`rule` |
| `image` | `img`、`image`、`picture` |

別名刻意是小寫的 ASCII 識別字，不是翻譯字串，這是刻意的：它們存在的理由，就是讓使用者可以緊接在 `/`
後面直接打字搆到一個指令，而對一個 zh-TW 輸入法來說，那正是還沒有組出任何字的那一刻——輸入法還停在
純 ASCII 狀態。把別名翻譯掉，就會拿掉它存在的唯一理由。比對只有在 label 與查詢字串這一側才不分大小
寫——輸入的查詢字串會先轉成小寫再比對，但別名本身是照原樣比對的——所以上面那張表對 fork 想加的任何
別名都是一個真實的限制：別名本身就得已經是小寫，否則查詢字串的大寫版本永遠比對不到它。

**什麼情況下不會觸發。** 在程式碼區塊裡，選單永遠不會打開：extension 自己的 `allow` callback 會檢查
游標所在節點的父節點是不是 `codeBlock`，是的話就拒絕這次比對——這是 StruoCMS 自己加上去的規則，不是
上游本來就有的行為。在唯讀欄位裡，選單同樣不會打開，但原因不一樣：`@tiptap/suggestion` 自己的
`apply()` 把整個比對兼打開選單的邏輯，都掛在 `editor.isEditable` 這個條件底下，所以一個停用的欄位，
它的編輯器根本走不到「要不要打開選單」這個判斷點。StruoCMS 這個 extension 自己完全沒有寫唯讀相關
的檢查——因為不需要。

**鍵盤行為。** 選單打開時，方向鍵下與方向鍵上會移動目前反白的項目，走到清單任何一端會繞回另一端；
Enter 會執行反白的項目。Esc 會關閉選單：上游自己的 `handleKeyDown` 在 Esc 上一樣會先呼叫 extension
自己的按鍵處理函式，跟其他任何按鍵一樣，但接下來不管那個函式回傳什麼，都會直接派送退出的
transaction、回傳 `true`——所以就算在 extension 自己的處理函式裡加一段 Esc 分支，那段分支會被執行，
卻只可能再派送一次多餘的退出，改變不了選單會不會關閉。Tab 是刻意放著不管的——extension 的按鍵處理
函式對它回傳 `false`——所以它會照瀏覽器原本的行為往下走，而不是被攔截當成確認選取的手段。如果攔下
Tab，只因為畫面上剛好有個 `/`，就會把這個欄位從表單原本的 tab 順序裡拉出來。

## 重新設計供應商 `ui/` 元件的樣式

**`frontend/src/components/ui/` 是供應商生成的唯讀輸出——絕不編輯它，也絕不對它 `:deep()`。** 重新換
主題要往上一層做，有三個地方：

- **Token 層**(`frontend/src/assets/tokens.css`)：適用於任何已經以語意自訂屬性形式公開的東西——顏色、
  半徑、陰影。每個供應商元件與 Tailwind utility 都讀取同一個 token，所以改一次就能同時觸及所有使用者。
- **在使用處帶入 `class` prop**：適用於任何能表達為 Tailwind utility class 的東西。每個會渲染出樣式化
  標記的供應商 `ui/` 元件都會透過 `cn()` 合併它的 `class` prop(`frontend/src/components/ui/input/Input.vue`
  展示了這個模式)，因此在使用該元件的地方帶入 utility class，就能只重新設計那一個用法的樣式，而不需要
  碰 `ui/`——`frontend/src/components/fields/FilePicker.vue` 與 `FilesField.vue` 都是這樣做的，兩者都對
  `DialogScrollContent` 帶入一個 `class`，讓對話框寬度變成 `min(78vw,1300px)`，並在螢幕寬度低於 960px
  時收窄為 `95vw`。
- **一個位於 `ui/` 之外的包裝元件**：適用於以上兩者都無法表達的東西——某個特定用法上的固定寬度、額外
  間距、一次性的版面微調。包裝元件自己的 `<style scoped>` 區塊是沒有分層的 CSS，而由於 Tailwind v4 把
  每一個 utility class 都放進 `@layer utilities` 之中，套用在同一個元素上的沒有分層宣告會勝過
  utility，無論特異度高低(除非那個 utility 帶有 `!important`，那會反過來)——這正是為什麼包裝元件的
  scoped 樣式才是放這類覆寫的可靠位置。不過 Vue 的 scope id 只會落在子元件自己的根元素上，並不會落在
  它內部渲染出來的東西上，因此包裝元件的 scoped 樣式能觸及供應商元件的根元素——與上面 `class` prop 所
  落點的同一個節點——但僅止於此；要觸及該元件在根元素內部渲染出來的節點，仍然需要本節已經禁止的
  `:deep()`。真正能觸及內部的唯一路徑是 token 層，因為內部節點是自己讀取那些自訂屬性。

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
(`@/components/ui/select`)，其兩個選項分別讀取
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
