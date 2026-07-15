# StruoCMS 後台前端改版 — 完整設計說明（繁體中文）

> 本文件是 [`admin-frontend-redesign.prompt.md`](./admin-frontend-redesign.prompt.md)（給 AI 代理執行用的英文技術 prompt）的**完整中文說明**。
> 英文 prompt 負責「要做什麼、怎麼做」的精確規格；本文件負責「為什麼這樣設計」的來龍去脈，供人閱讀、審查與決策。
> 兩份文件描述的是**同一套設計**，若有出入以英文 prompt 為實作依據。

---

## 1. 我們要解決的問題

StruoCMS 的後台**功能已經很成熟**——schema 驅動的內容集合、34 種欄位型別、多語系（i18n）、關聯（relations）、媒體庫、軟刪除／垃圾桶、版本歷史（後端）、RBAC 權限。**問題不在功能，而在「外觀與體感」**：目前的介面幾乎沒有經過設計，是能動但簡陋的骨架。

具體現況（也是這次改版要填補的落差）：

- **`style.css` 還是 Vite 專案的範本殘骸**：裡面是 hero 圖、`#next-steps`、`#docs`、`#app { width:1126px; text-align:center }` 這些跟後台完全無關的樣式，而且會**主動破壞**後台版面。→ 整份刪除重寫。
- **`AppShell.vue` 是最陽春的 `<header>/<nav>/<main>`**，登出鈕是原生 `<button>`，品牌是 `<span>`。→ 重建成正式的三區塊後台外殼。
- **`LoginView.vue` 完全是原生 HTML**（`<input>`、`<button>`），沒有用到任何 PrimeVue。→ 用 PrimeVue 重建。
- **`DashboardView.vue` 仍是佔位字串**（「Phase 7b 才做」）。→ 做出真正有用的儀表板。
- **媒體元件與 `ItemForm` 的每個欄位列**都是原生 HTML＋寫死顏色（例如 `#d33`）。→ 全部改用設計 token 重新皮膚化。
- **PrimeVue 使用不一致**：列表／導覽／表單外框已用 PrimeVue，但登入、媒體、欄位列還是手刻。→ 統一標準化。

**要保留的部分**：schema 驅動渲染架構、欄位型別註冊表（`lib/fieldTypes/registry.ts`）、三個 store（auth／schema／language）、API 層、路由、以及 `lib/*` 的純函式。這次只換「外皮（樣板與樣式）」，**不動邏輯**。

---

## 2. 設計目標與四個原則

你（使用者）設定的目標是：**簡潔、直觀、美觀，把學習成本降到最低，並符合 RWD**，而且盡量用 PrimeVue 自帶控件、以 PrimeVue 設計系統為基底。這轉譯成四個貫穿全案的原則：

1. **PrimeVue 優先**：所有互動控件與結構性版面都用 PrimeVue 元件；除非 PrimeVue 真的沒有對應元件，才用消費同一組 token 的最小自訂元素。不再出現原生 `<button>／<input>／<select>`。
2. **設計系統驅動**：所有顏色、間距、圓角、字級都來自**語意化 token**（以 PrimeVue Aura 為基底，用 `definePreset` 擴充）。可主題化的屬性一律不寫死 hex／px。
3. **學習成本最低 = 一致性優先**：同一個動作在每個畫面都長得一樣、行為一樣。學會一個畫面就會用全部畫面。
4. **RWD**：從 360px 手機到寬螢幕桌機都好用，任何寬度都不出現水平捲動。

---

## 3. 為什麼選這些設計決策

### 3.1 為什麼以 PrimeVue Aura 為基底、並用 `definePreset` 擴充

專案已經裝了 PrimeVue 4.5 與 Aura 主題。Aura 是乾淨、現代、中性的設計系統，天生支援亮／暗雙主題與完整 token 體系。與其自己從零刻一套視覺，不如**站在 Aura 的肩膀上**：用 `definePreset(Aura, …)` 只覆寫主色與 surface 色階，其餘沿用 Aura 的成熟預設。好處是——維護成本低、跨元件一致、未來升級 PrimeVue 也不易壞。

### 3.2 為什麼主色用 sky／indigo（藍系）而非現有的紫色

現況的 accent 是 `#aa3bff`（鮮紫）。後台是**長時間使用的工作介面**，需要的是低干擾、專業、耐看、文字對比高的主色。藍系（sky／indigo）在這點上優於高彩度紫色，也更「中性、不搶戲」，符合「內容優先」的方向。

> **這是一個可調整的決策點。** 若貴公司有品牌色（hex），只要把 preset 裡的 `primary` 50–950 色階換成該品牌色的色階即可，其餘設計不受影響。英文 prompt 已在 `preset.ts` 頂部標註此決策。

### 3.3 為什麼顏色只用來表達「狀態」，不用來裝飾

「學習成本低」的關鍵是**視覺噪音少**。因此全案只有一個主色（primary），其餘都是中性 surface；顏色只在需要傳達語意時出現：成功（success）、警告／可復原的破壞動作（warn，如軟刪除）、危險／不可復原（danger，如永久清除、還原覆蓋）、資訊（info）。全部透過 PrimeVue 元件的 `severity` 屬性表達，不手挑 hex。

### 3.4 為什麼亮／暗都要、且要「可切換＋記憶」而非只跟系統

暗色模式對長時間看螢幕的後台使用者是實用需求。但只跟隨系統偏好（`prefers-color-scheme`）不夠——使用者常想手動覆寫。因此採 **class 切換（`.app-dark`）＋ `localStorage` 記憶**：首次載入跟隨系統，之後以使用者手動選擇為準。切換入口放在頂列（topbar）。

### 3.5 為什麼是「簡潔留白、內容優先」的密度

你選擇了「簡潔留白、內容優先」而非「資訊密集」。因此預設採**舒適密度**（充足留白、少邊框、用 surface 層次取代密集格線），讓畫面呼吸、降低認知負擔。唯一例外：資料表格列數多時，可局部用 PrimeVue 的 `size="small"` 提升瀏覽效率。

---

## 4. 版面系統：後台外殼與導覽

改版後的骨架是經典且直觀的**三區塊後台外殼**（`AppShell`）：

```
┌──────────────────────────────────────────────┐
│ 頂列 Topbar（固定，約 56px）                    │  麵包屑 · 語言切換 · 主題切換 · 使用者選單
├───────────┬────────────────────────────────────┤
│ 側邊欄     │ 內容區（獨立捲動）                  │
│ Sidebar   │  ┌──────────────────────────────┐   │
│（固定、    │  │ 頁首 PageHeader（標題＋動作）│   │
│  可收合）  │  ├──────────────────────────────┤   │
│           │  │ <router-view/>               │   │
│           │  └──────────────────────────────┘   │
└───────────┴────────────────────────────────────┘
```

- **側邊欄**：沿用現有 `CollectionNav` 的 RBAC 過濾與分組邏輯，只換皮膚。展開約 260px、收合約 72px（只剩圖示，hover 顯示 Tooltip），收合狀態記憶於 `localStorage`。目前開啟中的集合會**高亮**。
- **頂列**：左側麵包屑（`Home / 集合 / 新增或編輯`）；右側是語言切換（`Select`，控制內容語系）、主題切換鈕、使用者選單（`Avatar`＋`Menu`，含 email、設定、登出）。取代現在那顆陽春登出鈕。
- **全域只掛一次** `<Toast/>` 與 `<ConfirmDialog/>`，讓每個畫面都能用統一的通知與確認流程。
- **頁首元件 `PageHeader`**：每個畫面都用它來放標題與主要動作，確保標題、間距、按鈕位置到處都一致——這是「一致性 = 低學習成本」的具體落實。

### RWD 行為（單一斷點系統）

| 斷點 | 寬度 | 行為 |
|---|---|---|
| sm | < 640px | 側邊欄變成可滑出的 `Drawer`（頂列出現漢堡鈕）；表格改為堆疊或橫向捲動；表單分頁全寬；動作收進 overflow 選單。 |
| md | 640–1024px | 側邊欄可收合成圖示軌；表單雙欄降為單欄。 |
| lg | > 1024px | 完整外殼；適合處雙欄欄位。 |

任何寬度都**不出現水平 body 捲動**；過寬的表格在自己的容器內橫向捲動。

---

## 5. 全畫面共用的互動慣例（一致性的核心）

這些慣例套用到每一個畫面，讓整套系統「手感一致」：

- **載入中**：內容區用 **Skeleton 骨架**（不是空轉的 spinner）；按鈕用 `loading` 狀態。
- **空狀態**：置中的「圖示＋一句說明＋主要動作」區塊（共用 `EmptyState` 元件）。列表空、垃圾桶空、媒體空、搜尋無結果都用它。
- **錯誤**：欄位級錯誤顯示在控件下方（來自驗證與 `ApiError.details`）；操作級錯誤用 **Toast**（紅色，帶 `error.message`）；頁面級（載入失敗／403／404）用置中狀態區塊＋重試或返回。**絕不靜默失敗，也不外洩堆疊訊息。**
- **成功回饋**：每次 新增／更新／刪除／還原／清除／上傳 都用 **Toast**（綠色）短句確認（如「文章已儲存」「已移至垃圾桶」）。
- **破壞性動作確認**：每個破壞動作前用 **ConfirmDialog**；嚴重度對應可復原性——軟刪除用 warn、永久清除／還原覆蓋用 danger 並在訊息中點名該筆資料。
- **權限感知渲染**：用 `authStore` 的 `canRead/canWrite/canDelete` 與 `isSuperAdmin` 決定顯示；使用者永遠不能做的動作直接**隱藏**，情境性不可用才用 disabled＋Tooltip。永遠不顯示點了會 403 的動作。

> 註：目前 `main.ts` 只註冊了 `ConfirmationService`，**尚未註冊 `ToastService`**。要落實上面的 Toast 慣例，需在 `main.ts` 加上 `app.use(ToastService)`——英文 prompt 已寫明。

---

## 6. 各畫面設計重點

- **登入頁**：置中的 `Card`（約 380px），品牌＋標題、`InputText`（email）＋`Password`（可切換明碼）、全寬 `loading` 送出鈕、紅色 `Message` 顯示錯誤。手機上也要看起來是刻意設計過的。
- **後台外殼／導覽**：如第 4 節；交付重建的 `AppShell`、換皮的 `CollectionNav`、新的 `PageHeader`／頂列子元件。
- **儀表板**：登入後的落地頁。做**有用但不浮誇**的內容：使用者可讀的每個集合一張 `Card`（顯示名稱、圖示、筆數），點擊進入該集合；可寫的集合給「新增」快捷鈕。不做假圖表。載入時顯示骨架。
- **集合列表**：保留所有伺服器端 `DataTable` 邏輯（延遲分頁／排序、300ms 防抖搜尋、語系、垃圾桶模式）。換皮：頁首放標題＋新增鈕；工具列放帶搜尋圖示的輸入框與 Active/Trash 切換（`SelectButton`，僅在支援軟刪除且有刪除權時顯示）；表格條紋、hover、合理欄寬、長文截斷＋Tooltip；列動作用 overflow 選單或圖示鈕（編輯／刪除／還原／永久刪除）。空／載入／錯誤都走共用狀態。
- **項目表單（旗艦畫面）**：最複雜、價值最高。保留所有邏輯（schema／語系載入、驗證、payload 組裝、深層關聯載入）。換皮結構為：頁首（標題＋刪除鈕）→ 表單卡片（內容最大寬約 880px）→ 先放非翻譯欄位（響應式欄位網格）→ 關聯區（附小標）→ 翻譯欄位收在 **Tabs（每語系一頁）**，預設語系標記，保留「出錯時跳回預設語系」行為，並可在有錯的語系分頁上加紅色 Badge 幫使用者快速定位 → 底部動作列（取消＋儲存，桌機黏底、手機堆疊；無寫入權時隱藏儲存）。
- **媒體庫**：頁首含上傳鈕；上傳拖放區用 token 重新設計（拖曳時主色提示，不再寫死 `#d33`），每檔進度用 `ProgressBar`＋狀態 `Tag`；格狀縮圖（響應式）每格 hover 浮出編輯／刪除；選取模式（供關聯檔案挑選器用）維持可用。
- **垃圾桶**：維持「列表的一種模式」設計，不另開路由；用小 `Tag` 與淡化列樣式讓垃圾桶模式視覺可辨，還原（success/secondary）與永久刪除（danger）明確區分。
- **版本歷史與還原（新畫面，有相依）**：後端已支援版本（`[CmsCollection(Revisions=true)]` 與 REST／GraphQL 端點），但**前端 `CollectionMeta` 型別目前沒有 `revisions` 旗標**、`/api/schema` 也還沒輸出它。設計上：當集合 `meta.revisions` 為真且處於編輯時，表單提供「歷史」動作（右側 `Drawer`），列出版本（編號、時間、作者）並提供「檢視快照」與「還原」（danger 確認）。**相依處理原則：需要後端在 schema 輸出 `revisions` 旗標；若需後端工作，先停下回報，不要寫死哪些集合有版本。**
- **設定／個人頁（新、輕量）**：外觀（系統／亮／暗）、內容預設語系、帳號資訊（email、是否 super-admin，唯讀）。單張 `Card` 分區即可，不過度開發（YAGNI）。
- **錯誤／403／404 狀態**：共用置中狀態區塊（圖示＋訊息＋回儀表板）。

---

## 7. 欄位型別策略（34 種 FieldInterface）

欄位元件由 `lib/fieldTypes/registry.ts` 透過 `FieldInput.vue` 解析——**這套機制保留**，只重繪每個欄位元件：統一改用指定的 PrimeVue 控件、預設滿寬、支援 `disabled/readOnly`、顯示驗證狀態、只用 token。英文 prompt 有完整對照表（含現況→目標控件），這裡摘要幾個**體驗升級**：

- `password`：純文字輸入 → **`Password`**（可切換明碼）。
- `slider`：`InputNumber` → **`Slider`**＋數值顯示。
- `rating`：`InputNumber` → **`Rating`**（星等）。
- `boolean`：`Checkbox` → **`ToggleSwitch`**（開關比勾選更直覺；`checkbox` 型別仍用單一 Checkbox）。
- `time`／`dateTime`：`DatePicker` 的 `timeOnly`／`showTime` 模式。
- `tags`：手刻輸入＋鈕 → **`AutoComplete multiple`**（自由輸入的 chip；保留 `{value,label?}[]` 序列化）。
- `richText`：保留 TipTap 與消毒器與所有標記（表格／對齊／顏色／上下標），只把工具列重繪成 token 化的按鈕群、編輯區加上以 token 描邊的表面。
- `color`：`InputText` → `ColorPicker`＋`InputText` 組合。
- `divider`：改用 PrimeVue `Divider`。
- `repeater`／`keyValue`／`files`：重繪成有層次的子面板／成對輸入列／可排序縮圖列，動作鈕統一。

**關聯**：`dropdown`（多對一）→ 可篩選 `Select`；`tagSelect`（多對多）→ `MultiSelect` chip；`treeSelect` → `TreeSelect`（沿用 `buildRelationTree`）；`relatedList`（一對多）→ 精簡內嵌 `DataTable`。

---

## 8. 交付範圍與檔案規劃

**新增**：`theme/preset.ts`、`composables/useTheme.ts`、`components/layout/PageHeader.vue`／`EmptyState.vue`／頂列與側邊欄子元件、`views/SettingsView.vue`、版本歷史 UI（相依於 schema 旗標）。
**修改（只換樣板與樣式，保留 script 邏輯）**：`AppShell`、`CollectionNav`、`LoginView`／`DashboardView`／`CollectionListView`／`ItemFormView`／`MediaLibraryView`、`ItemForm`、媒體三元件、所有 `components/fields/*`、`main.ts`（preset＋ToastService）、`types/schema.ts`（加 `revisions?: boolean`）。
**刪除／取代**：`style.css` 範本殘骸（換成 token 基底）、確認無引用後移除 `HelloWorld.vue` 與 hero／vue／vite 資產。

---

## 9. 驗收標準（宣告完成前的自我檢查）

- `pnpm build`（vue-tsc＋vite）**必須通過**——這是最硬的型別關卡（比 `pnpm test` 嚴格，過去踩過只跑 test 會漏型別的雷）。
- `pnpm test`（vitest）維持綠燈；因換皮而動到的測試只調整標記，**絕不弱化行為斷言**。
- 出貨畫面無原生 `<button>/<input>/<select>`；可主題化屬性無寫死 hex。
- `style.css` 殘骸清除；`#app` 不再是固定寬置中。
- 每個畫面、每個欄位都換皮；RBAC 閘門完整。
- Toast＋ConfirmDialog 全域各掛一次；每次寫入都有 Toast、每個破壞動作都有確認。
- **亮／暗都驗過**；RWD 在 360／768／1024／1440px 都驗過。
- API 呼叫、payload、路由、store、驗證邏輯**行為零變更**（diff 檢查邏輯檔只有樣板／樣式變動）。
- 實際跑一遍核心流程：登入 → 儀表板 → 列表 → 建立／編輯含多種欄位＋關聯＋多語分頁的項目 → 上傳媒體 → 軟刪除 → 還原。

---

## 10. 非目標（明確不做）

- 不改後端／API，**唯一例外**是 `/api/schema` 的 `revisions` 旗標（且要當成前置相依回報，不盲目實作）。
- 不引入新 UI 框架，不加 PrimeFlex／Tailwind。
- 不新增內容模型／欄位型別。
- 這一輪不做後台介面文字的多語化（UI 標籤先維持英文）。
- 儀表板不做超過簡單筆數的分析／圖表。

---

## 11. 建議施工順序

1. **先立地基**（preset、主題 composable、全域樣式、AppShell、PageHeader/EmptyState、Toast 接線），驗證 build 通過、畫面能渲染。
2. 再逐畫面換皮：登入 → 外殼／導覽 → 儀表板 → 集合列表 → **項目表單＋欄位（最大塊）** → 媒體 → 垃圾桶 → 設定 → 版本歷史（若旗標就緒） → 錯誤狀態。
3. 檔案保持精簡（專案規則：一般 200–400 行、最多 800 行），依邏輯分段 commit。
4. 每完成一個畫面就跑 `pnpm build` 與相關測試，型別關卡立刻修。
5. 碰到 `revisions` 旗標相依時**先停下回報**，不要臆測。

---

## 12. 需要你（決策者）確認的兩件事

1. **主色**：預設 sky／indigo。若有品牌色 hex，提供後即可替換色階，其餘設計不動。
2. **密度**：預設舒適留白。若偏好資料表格更緊湊，可全域改用 PrimeVue `size="small"`。

未提供時，實作將採上述預設值，並在 `preset.ts` 頂部標註，方便日後調整。
