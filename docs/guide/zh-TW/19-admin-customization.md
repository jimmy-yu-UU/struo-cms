# 19. 後台客製化

後台是一個 Vue SPA，大多數客製化需求靠 metadata 就能滿足；這一章談的是剩下那一小塊，要
改 `frontend/src` 底下的哪個檔案，改了又會有什麼後果。

## 先問 metadata 夠不夠

一個掛好 `[CmsCollection]` 與 `[CmsField]` 的類別，本身就能生出側欄項目、schema 驅動的
分頁清單，以及一個新增／編輯表單，全程不用碰一行 Vue。後台的欄位、清單欄與標籤全部來自
`GET /api/schema`，沒有第二個來源。

顯示名稱、`Icon`（合法值是 `ICON_MAP` 的鍵，查不到的名稱一律回退成通用檔案圖示，見
[第 5 章：定義集合](05-collections.md)）、欄位介面（見
[第 6 章：欄位型別與編輯介面](06-field-types.md)）與 `Hidden`，都是這張 schema 管得到的
metadata，改 metadata 就換得掉，不用碰 `frontend/src`。

真正需要伸手進 `frontend/src` 的，是 metadata 表達不了的需求：某個欄位需要一種現成介面都
沒有的編輯器；品牌要換的不只是名稱跟 Logo；後台介面本身要能說另一種人類語言；或是某個工作
流程整個不合清單／表單這套通用樣板。除此之外——新增集合、欄位、關聯、驗證、權限——都是
[第 5 章](05-collections.md)、[第 6 章](06-field-types.md)、[第 8 章：關聯](08-relations.md)
與[第 17 章：角色與權限](17-roles-and-permissions.md)的地盤，後台不用跟著改一行。

## `frontend/src` 地圖

根目錄還有 `App.vue` 與 `main.ts` 兩個檔案，其餘都是子目錄；每個模組旁邊通常擺著同名的
`.test.ts`，測試檔跟著模組放。

- `api/`：一個 REST 資源一個模組，外加共用的、看得懂信封格式的 fetch 包裝器。
- `assets/`：整個後台外觀的來源，`tokens.css` 與 `theme.css`，下一節細談。
- `components/`：`ItemForm.vue` 在這一層，其餘按用途分子目錄，vendored 的 `ui/`（見
  〈重新設計 `ui/` 元件〉）也在這裡。
- `composables/`：跨元件共用的邏輯：`useConfirm.ts`、`useToast.ts`。
- `i18n/`：`vue-i18n` 實例，接到 `locales/`。
- `layouts/`：`AppShell.vue`，登入後每個路由共用的頂列／側欄／內容版面。
- `lib/`：不依賴 Vue 的輔助程式，包含 `fieldTypes/` 底下的欄位介面 registry。
- `locales/`：後台介面自己的訊息目錄，`en.ts` 與 `zh-TW.ts`，跟內容語言是兩回事。
- `router/`：路由表、驗證與權限的導覽守衛，外加一段 chunk 404 時重新整理頁面的補救邏
  輯，以及路由 meta 的型別宣告。
- `stores/`：Pinia store：`authStore`、`appConfigStore`、`schemaStore`、`themeStore`、
  `uiLocaleStore`、`languageStore`、`confirmStore`。
- `theme/`：第一次畫面渲染前就要決定的主題與介面語言，讀 `localStorage` 與媒體查詢，
  這時 Pinia 跟 Vue 都還沒起來。
- `types/`：後端 DTO 的 TypeScript 對應，以及表單元件自己需要的型別。
- `views/`：一個路由一個元件：dashboard、集合清單、表單、媒體庫、設定、登入。

下面幾節分別細談 `assets/`（design token）、`lib/` 裡的圖示表、`components/ui/`、
`components/fields/` 底下的富文本編輯器，以及 `locales/`；其餘目錄本章不再展開。

## Design token 與主題

`tokens.css` 與 `theme.css` 兩份樣式表合作，靠同一個根元素上的 `.app-dark` class 切換深
淺色，沒有第二份要另外同步的東西。

`tokens.css` 是 Tailwind 的進入點，也是 shadcn 的語意化 token 層（`--background`、
`--foreground`、`--primary`、`--radius`、側欄用的 token 等等），分別在 `:root` 宣告一
次、在 `.app-dark` 再宣告一次深色版；一個 `@theme inline` 區塊把每個 token 對應到
Tailwind 的 utility class（`bg-background`、`text-primary` 之類），vendored 的 `ui/` 元
件跟第一方元件吃的是同一份對應。

`tokens.css` 也把 shadcn 原本認的 `.dark` 深色慣例，重新指向這個專案的 `.app-dark`，而不
是改名遷就 shadcn；這個 class 名稱對 e2e 測試、還有 Vue 掛載前那段先畫一次的腳本，都是吃
緊的依賴，不能隨便換掉。

`theme.css` 是沒有包在任何 Tailwind layer 裡的全域樣式：頁面／表面／前景、狀態、陰影、
遮罩與字型這些自訂屬性，第一方的 scoped CSS 會讀它們，外加 `html`／`body` 的 reset 與切
換主題時的過場動畫規則。因為這些規則整段都不在任何 layer 裡，同一個元素上不管 utility
class 的優先度多高，未包 layer 的宣告一律贏過包了 layer 的，除非包 layer 那邊自己掛了
`!important`，才會反過來——一段手寫 CSS 覆寫悄悄沒生效，十之八九是踩到這一條。

兩份樣式表都在 SPA 進入點 `main.ts` 匯入一次，`tokens.css` 在前，第三行還匯入了 toast 套
件自己的樣式表（它自己的套件不會匯入自己）。主題 store 負責在根元素切換 `.app-dark`，一
步同時翻轉兩份檔案裡的自訂屬性，不用另外通知誰，並且把選擇存下來；初始模式在 Pinia 跟 Vue
都還沒起來前就決定好：先看 `localStorage` 裡存的 `struo.theme`，沒有就看
`prefers-color-scheme`，再沒有就是淺色。

要換主題色，改 `tokens.css` 的 `:root`／`.app-dark` 裡的語意化自訂屬性（vendored 元件跟
utility class 都讀這一份契約）；牽動側欄、麵包屑這類第一方外殼時，`theme.css` 對應的屬性
也要一起改。

`Branding:Name` 跟 `Branding:LogoUrl` 只換得到品牌名稱跟 Logo，換不到色票：色票是原始碼
裡的預設值，不是逐次部署可調的設定鍵。深色模式下卡片跟頁面背景會收斂成同一個值，所以卡片
再往下沉一階的表面色，在 `tokens.css` 裡自己有一個 token，不是靠 `theme.css` 算出來的。

## 新增側欄圖示

`ICON_MAP` 把固定的鍵對應到 Lucide 元件；集合宣告的 `Icon` 值交給 `resolveIcon` 查表，
查表接受鍵本身，也接受舊式的 `pi …`／`pi-…` class 字串，會從裡面挑出真正的 token。查不到
的名稱一律回退成通用檔案圖示，不會讓側欄壞掉。鍵按用途分組：

- **導覽／外殼：** `th-large`、`images`、`cog`、`bars`、`user`、`sign-out`、`sun`、
  `moon`、`angle-down`、`angle-left`、`angle-right`、`chevron-left`
- **動作：** `plus`、`search`、`pencil`、`eye`、`trash`、`undo`、`check`、`times`、
  `upload`、`copy`、`history`、`replay`
- **排序／對齊：** `arrow-up`、`arrow-down`、`align-left`、`align-center`、
  `align-right`、`align-justify`
- **富文本清單／表格：** `list`、`sort-numeric-down`、`table`
- **檔案／媒體種類：** `file`、`file-edit`、`file-pdf`、`file-word`、`file-excel`、
  `image`、`video`、`volume-up`、`folder`、`folder-plus`
- **`Icon` 屬性直接會發的語意化名稱：** `article`、`tag`、`megaphone`

加一個側欄圖示：在 `frontend/src/lib/icons.ts` 匯入圖示元件、在 `ICON_MAP` 加一個鍵，再
把這個鍵填進集合的 `[CmsCollection(Icon = ...)]`。這張表不是隨便就能加：一項覆蓋率測試
會逐一檢查每個鍵，是不是真的被原始碼裡某個 token、`Icon` 屬性發出的語意化名稱，或是執行
期組出來的 class 用到。貿然加一個沒人用的鍵，前端測試會紅。

## 重新設計 `ui/` 元件

`frontend/src/components/ui/` 是 vendored、產生出來的唯讀輸出，有 33 個元件目錄，例如
`button`、`dialog`、`table`、`select`、`sidebar`。不要直接改裡面的檔案，也不要用深層選
擇器伸進去：Vue 的 scope id 只會落在子元件的**根**元素上，不會落在它內部渲染出來的節點
上，深層選擇器連著都碰不到內部節點，唯一碰得到內部節點的路徑是 token 層，因為那些內部節
點本身就在讀這些自訂屬性。

重新配色的地方永遠往外挪一層，依序是：token 層，適用任何已經曝露成語意化自訂屬性的東
西，一次改動同時打到每個用到它的地方；使用點的 `class` prop，適用能寫成 utility class
的東西；或是自己寫一個把它包起來的元件，適用前兩者都表達不了的東西。

每個會渲染出樣式的 vendored 元件，都把自己的 `class` prop 併進共用的 class 輔助函式，所
以在使用的地方多傳一些 utility class，改的只是那一次使用，不會動到 `ui/`。包裝元件自己的
scoped style 同樣沒包在 layer 裡，所以是可靠的覆寫位置（理由見〈Design token 與主題〉）。

避免用 `!important`：它贏得了眼前這一次覆寫，卻把下一次——不管是自己還是 fork——留在同
一個戰場上，處境更差。

## 富文本編輯器

`richText` 欄位的互動介面各管一件事：工具列，永遠看得到的完整指令集；泡泡選單，選取文字
時的行內格式；右鍵選單，表格或圖片上的結構性操作；node view，物件自己的直接操作把手；以
及 slash 選單，鍵盤驅動的區塊插入。客製化真正碰得到的是指令登記表、連結對話框、圖片尺寸
與排版樣式。

指令本身收在一張登記表：`richTextCommands.ts` 的陣列，一筆是
`{ id, labelKey, group, icon, glyph, glyphTag, isActive, run }`。`group` 的值是
`inline`、`align`、`block`、`insert`、`history`。

工具列把整張表原樣渲成按鈕，外加三個因為形狀對不上（標題層級、顏色、插入表格）沒進表的
控制項；泡泡選單只挑 `inline` 這一群，只在有文字選取時出現。往登記表加一筆
`group: 'block'` 或 `group: 'insert'` 的指令，工具列跟 slash 選單會一起拿到，不用逐一
接線；泡泡選單只吃 `inline`，拿不到這兩群。

slash 選單的清單是四段接起來的固定順序：標題層級（`richTextHeadings.ts` 的
`HEADING_LEVELS`，是 H2 到 H6）、登記表裡 `group: 'block'` 的項目、一筆手寫的表格項目，
最後是 `group: 'insert'` 的項目。工具列的表格控制項插入使用者自己選的尺寸，跟登記表無
關，形狀對不上單一指令；手寫的這一筆固定插入帶表頭列的 3×3 表格。別名必須寫成小寫
ASCII：查詢字會先轉小寫、別名卻照原樣比對，別名裡有大寫就永遠不會命中。

手寫項目多兩個地方要顧：`labelKey` 沒出現在 `EN_LABELS`，錯不在載入時，而是 slash 選單
一開就丟例外——`EN_LABELS` 在模組載入時就從標題層級、登記表與表格項目組好，候選清單每敲
一個字就重算一次；另外，它的英文標籤（`enLabel`）要用共用的解析器算，不能像顯示用的
`label` 一樣直接寫 `t(key)`——那樣寫得出來，卻把英文名稱綁死在目前的介面語言，離開英文
介面時，靠英文名稱比對就悄悄失效。

`/` 什麼時候會開、鍵盤怎麼操作，是 `richTextSlashExtension.ts` 的事。

連結對話框（`RichTextLinkDialog.vue`）是工具列跟泡泡選單的連結按鈕共用的同一個彈窗，不
是瀏覽器原生的 prompt，兩邊都靠同一個共用的 `link` 指令觸發。使用者填的是網址跟一個「另
開分頁」勾選框；選取範圍原本就在連結裡時，多一個移除按鈕。對話框只決定 `target`，`rel`
是伺服器端的 sanitizer 依 `target` 推出來的，規則見[第 6 章](06-field-types.md)。

圖片尺寸的調整是兩步：先點一下選取這張圖，再拖曳八個控點之一（四角加四邊中點），控點只
在這張圖是目前選取時才出現。

拖曳的行為（最小寬度、八個方向、比例鎖定）是上游圖片擴充功能自己的設定，這裡自己加的是
控點什麼時候出現、長什麼樣，寫在 `RichTextInput.vue` 自己的 scoped style 裡；控點是
10px 見方的拖曳熱區，比 WCAG 2.2 建議的 24×24 小，想放大熱區，改的也是這個區塊。拖曳一
次會同時想寫寬跟高兩個維度，但欄位把 `height` 屬性覆寫成不序列化，最後存下來的 `<img>`
只有寬度，沒有高度，高度照寬度的比例反推。

編輯區塊本身掛 `class="prose dark:prose-invert"`，直接吃排版外掛的預設樣式，用意是編輯
時盡量貼近文章發布後的樣子；要換掉這層外觀，改的是 `tokens.css` 裡外掛註冊處的
`--tw-prose-*` 自訂屬性，而且要寫成沒包 layer 的一般規則，理由跟〈Design token 與主題〉
一節「未包 layer 贏過包了 layer」是同一條。

表格的表頭是唯一交給伺服器補的落差：編輯器的 schema 沒有 `thead` 節點，sanitizer 事後處
理時，只要一個表格第一列的每一格都是 `<th>`、又還沒有 `<thead>`，就把整個第一列包進
`<thead>`；這一步涵蓋每一次 `RichText` 欄位的寫入，不只是經過編輯器的內容，也沒有回填，
已經存好的內容要等下一次重新存檔才補上。

這個判斷只認表格的第一列，但編輯器的表格右鍵選單能把任何一列標成表頭列——標在第一列以外
的表頭列，存下來的 HTML 仍然待在 `<tbody>` 裡，sanitizer 不會挪動表格內容，編輯器自己模
仿表頭樣式的 CSS 也只蓋第一列，那一列在編輯器裡看起來就是普通列，這是它發布後真正的樣
子，不是編輯器畫錯。

這裡有兩個地方看起來只是打開一個選項，實際上會把資料整段吃掉，因為 sanitizer 丟掉不在白
名單裡的標籤時，是連整個子樹一起丟：表格擴充功能有一個「產生包裝用 `<div>`」的選項，打開
它，儲存時每一個表格都會被整段砍掉，不是拆開保留內容；這個選項留著關掉，除非先把 `div`
加進 sanitizer 的白名單。

想在 slash 選單加 H1 也一樣，標籤白名單從 `h2` 起跳，因為頁面標題本身就是 H1，寫進去的
`<h1>` 連同文字整段被砍——真要加，得先動 sanitizer 的白名單，再放寬標題層級的型別，並且
在兩份 locale 檔各補一個 label key。

想改什麼、開哪個檔：

- 指令與工具列／泡泡選單：`richTextCommands.ts`
- slash 選單的項目跟別名：`richTextSlashCommands.ts`
- 標題層級：`richTextHeadings.ts`
- 連結對話框：`RichTextLinkDialog.vue`
- 表格與圖片右鍵動作：`richTextTableActions.ts`／`richTextImageActions.ts`
- 圖片縮放行為與控點樣式：`RichTextInput.vue`
- 排版樣式：`tokens.css`

除了 `tokens.css`，其餘檔案都在 `frontend/src/components/fields/` 底下。

## UI 語言

後台介面自己的語言，跟內容語言各自獨立；選定的語言不會送到 API，可選的集合則來自
`GET /api/config`，見
[第 7 章：多語內容](07-i18n.md)。它是 `vue-i18n` 的非 legacy 模式，訊息目錄集中登錄在
`src/locales/index.ts` 的 `catalogs` 物件裡，出貨兩份：`zh-TW.ts` 與 `en.ts`，各是一個巢狀物件，頂
層按畫面分命名空間（`nav`、`login`、`itemForm`、`settings` 之類），`fields` 底下另有一個 `richtext`
子命名空間。`UiLocale` 型別、i18n 的訊息表、切換器的選項、鍵集合對稱的測試，全部從這個登錄表推
導。

啟用哪些目錄、預設哪一個，由 `AdminUi:Locales` 與 `AdminUi:DefaultLocale` 決定（見
[第 4 章：設定參考](04-configuration.md)），經 `GET /api/config` 送到前端；前端只保留登錄表裡有的
代碼。生效的語言在 `/api/config` 回來之後、掛載之前決定：`localStorage` 裡的 `struo.uiLocale` 若在
啟用集合內就用它，否則用預設；i18n 的 fallback 也指向預設。一個 Pinia store 的 `set()` 同時切換
i18n、更新 `<html>` 的 `lang` 屬性、把選擇存下來，並拒絕不在啟用集合內的值。App shell 裡的
`UiLanguageSwitcher` 是唯一會呼叫它的元件，選項就是啟用集合，只有一個語言時整個元件不渲染。

加一個介面語言：

1. 新增一份跟既有目錄同樣鍵結構的目錄檔。
2. 在 `src/locales/index.ts` 的 `catalogs` 登錄它。
3. 在每一份目錄補一個 `lang.<code>` 鍵（切換器的選項文字）。
4. 把代碼加進 `AdminUi:Locales`，需要的話也設成 `AdminUi:DefaultLocale`。

有一項單元測試會把登錄表裡**每一份**目錄的鍵集合互相比對，一邊加了鍵另一邊沒跟上就會讓前端測試失
敗；執行期缺一個鍵則是退回到預設語言的目錄，不會直接壞掉。

## 品牌設定

品牌名稱與 Logo 分兩層：站台設定裡編輯出來的值贏，`Branding:Name`／`Branding:LogoUrl`
只是設定檔那邊的部署期預設，鍵名見[第 4 章：設定參考](04-configuration.md)的 `Branding`
一節。

站內編輯器是 Site Settings 裡一張只有 super-admin 看得到的表單，送出品牌名稱跟 Logo 的
檔案 id，收回名稱跟解析好的 Logo 網址。匿名的 `GET /api/config` 逐欄位算出這組有效值，
伺服器端快取 30 秒，一存檔就立刻清掉這個快取鍵，所以一次刻意的修改不會被 TTL 拖慢。存檔
時會擋下沒上線的 Logo 檔案；設定端點每次重算有效值時也會再確認一次這個檔案還在上線狀
態，所以檔案之後被下架或丟進垃圾桶，端點會退回部署期的預設值，不會回一個失效的網址。

後台在掛載前就先拉一次這份設定，存進一個 store，品牌標記讀得到網址就顯示 Logo 圖片，讀
不到就顯示單字母標記；瀏覽器分頁標題會用一個 watcher 持續跟著品牌名稱走，不是設定一次就
不管，站內改名立刻生效，重啟 API 只有在要改部署期的預設值時才需要。

## 自訂欄位編輯器

換掉某個介面預設用的欄位編輯器元件，是把 `frontend/src/lib/fieldTypes/registry.ts` 裡
一筆項目的元件換掉，其他部分照樣正確；完整流程在
[第 6 章：欄位型別與編輯介面](06-field-types.md)。

## 開發代理與 `VITE_API_BASE_URL`

開發時，`frontend/vite.config.ts` 的 `server` 區塊（節錄）決定 dev server 怎麼跑：

```ts
  server: {
    port: 5173,
    host: '127.0.0.1',
    proxy: { '/api': { target: 'http://localhost:5221', changeOrigin: true } },
  },
```

`host` 釘死 IPv4 loopback，是因為某些 Windows 環境下 `localhost` 會先解析成 IPv6
loopback，Vite 因此只綁 `[::1]`，不釘的話瀏覽器打 `http://localhost:5173` 反而連不上；
`proxy` 把同源的 `/api` 轉發到 `:5221` 上的 API，這也是為什麼
[第 3 章：快速開始](03-getting-started.md)的走法完全不用另外設定 CORS，瀏覽器眼裡只有
Vite 這一個來源。

想讓 dev SPA 接另一個 API 實例，改這裡的 `target` 就好，同源這個模式沒有另一個前端專屬
的 base URL 設定。

真要讓 SPA 部署在跟 API 不同的來源，才需要在 `frontend/.env`（從追蹤中的
`frontend/.env.example` 複製）設 `VITE_API_BASE_URL` 為 API 的完整來源。這個變數只在三
個地方被讀：`apiClient.ts`、`filesApi.ts`、`richTextImages.ts`，各自沒讀到就退回相對路
徑 `/api`。它是 Vite 的建置期變數，改了要重開 dev server 或重新建置，單純重新整理頁面沒
用。

這個模式也需要後端配合：`Struo:Cors:AllowedOrigins` 得列出 SPA 的來源；設了之後 session
cookie 與 HTTPS 的要求跟著改變，兩邊都得走 HTTPS，見
[第 4 章：設定參考](04-configuration.md)。

## 接下來

後台客製化談到這裡；下一步是把整個系統部署到正式環境，見
[第 20 章：部署](20-deployment.md)。
