# Admin UX Batch C 設計 — 媒體資料夾 (#5) + 檔名帶入 Title (#7) + 移除完整編輯連結 (#6 剩餘)

> 來源:`docs/admin-ux-issues-backlog.md`(權威原文)。
> 使用者已確認的決策:巢狀樹、**禁止刪除非空資料夾**、FilePicker 一起支援、Title 去副檔名、
> 媒體庫採 **Drive 式資料夾卡片** 導航。

## 目標

1. **#5** 媒體庫資料夾:純系統整理用,與實體儲存(S3 key)完全解耦;巢狀;媒體庫內建立/改名/刪除/導航;檔案可移動到資料夾。
2. **#7** 上傳時自動以檔名(去副檔名)帶入預設語系的 Title。
3. **#6 剩餘** 移除 MediaDetailDialog 指向 System>>File 項目表單的「完整編輯」連結。

## 範圍外(明確不做)

- #12 File 軟刪除(後續獨立批次)。
- 檔案多選批次移動(follow-up)。
- 資料夾層級權限(沿用 collection 級 RBAC)。
- 搜尋限定於目前資料夾(搜尋一律全域,見下)。

---

## 後端設計

### 1. 新核心實體 `MediaFolder`

位置 `src/Struo.Infrastructure/Files/MediaFolder.cs`,加入 `FrameworkEntityTypes.All`。

- `[SugarTable("media_folders")]`
- `[CmsCollection("MediaFolder", Group = "System", DefaultDisplayField = nameof(Name), Hidden = true)]`
  — 不進側欄,媒體庫是唯一 UI;CRUD 走通用 `/api/items/mediafolder`,不新開 controller。
- 繼承 `AuditableEntity`(Guid/UUIDv7 PK、audit 欄位、樂觀鎖 `Version`)。
- `Name`:`string`,`[CmsField(Text, Searchable, Required)]`。
- `ParentId`:`Guid?` 自引用 FK;`[Navigate(OneToOne)] Parent` + `[CmsRelation(Interface = TreeSelect, DisplayTemplate = "{Name}")]`(比照 sample `Category` 模式);`parentid` 建索引。
- RBAC:標準獨立 collection 權限 `mediafolder` r/w/d(super-admin 直通)。前端以
  `canWrite('mediafolder')` / `canDelete('mediafolder')` 閘資料夾管理 UI。

### 2. `File.FolderId`

- `Guid?` 可空 FK → `media_folders`,**可寫**(File 其他欄位多為 ReadOnly,此欄位不是);
  `[Navigate] Folder` + `[CmsRelation(DisplayTemplate = "{Name}")]`。
- 移動檔案 = 一般 `PUT /api/items/file/{id}` 更新 `folderId`;QueryValidator 自動放行
  ManyToOne FK,前端用 `filter[folderId][_eq]=<id>` / `[_null]` 過濾,不需改驗證層。

### 3. 伺服器端完整性守衛(需求層級,機制由實作驗證細節)

- **刪除守衛**:資料夾內含檔案或子資料夾時,刪除必須被伺服器拒絕,回結構化錯誤
  (envelope `error.code`,如 `FOLDER_NOT_EMPTY`),前端顯示友善 toast。
  首選機制:框架 relation `OnDelete` 語意 — 若現有 enum 無 `Restrict` 則**新增為通用框架能力**
  (每個 downstream 都用得到;`File.Folder` 與 `MediaFolder.Parent` 皆宣告 Restrict)。
  若通用路徑成本過高,fallback = 針對 mediafolder 的服務層守衛,並記錄為技術債。
- **循環守衛**:更新/建立時 `ParentId` 不得指向自身或自身子孫(避免 A→B→A)。
  首選機制:ItemService 寫入路徑針對「自引用 ManyToOne」的 metadata 驅動通用檢查
  (sample `Category` 同樣受惠;目前僅前端 `excludeId` 防護,API 直呼叫可繞過)。

### 4. 上傳進資料夾

`POST /api/files` 增加**可選** multipart 欄位 `folderId`(Guid);有值時驗證資料夾存在
(不存在 → 400),寫入 `File.FolderId`。媒體庫在資料夾內上傳時帶入目前資料夾。

### 5. #7 檔名帶入 Title

`FileService.SaveAsync` 於同一寫入交易內種入一筆 `file_translations`:
`{ FileId, Locale = 預設語系, Title = Path.GetFileNameWithoutExtension(fileName), Alt = null }`。

- 預設語系 = languages 表的 default 旗標(實作時確認欄位名);查無語言/無 default → 安靜跳過種入(上傳不得因此失敗)。
- 後端做(非前端)→ API 直傳、任何上傳入口行為一致。

### 6. Migration `002-media-folders.sql`

- 冪等(`CREATE TABLE IF NOT EXISTS`、guarded `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`、
  `CREATE INDEX IF NOT EXISTS`)、forward-only。
- 內容:`media_folders` 表(新時間欄位用 **timestamptz**,per README DB-7)、
  `files.folderid uuid NULL`、`parentid`/`folderid` 索引。
- dev InitTables(entity CodeFirst)與本 migration 必須 schema 對齊(DB-16 parity 教訓)。

---

## 前端設計

### 1. 媒體庫 Drive 式資料夾導航(`MediaLibraryView`)

- 狀態:`currentFolderId: string | null`(null = 根層)。
- 資料夾清單:`itemsApi.list('mediafolder', ...)` 一次載入全部(數量小),前端組樹/找子層與麵包屑祖先鏈。
- **資料夾卡片**顯示在檔案網格上方(list 模式為上方列),點擊進入;麵包屑
  `媒體庫 / 資料夾A / 資料夾B` 可點回上層。
- 檔案查詢:目前資料夾 → `filter[folderId][_eq]`;根層 → `[_null]`(根層顯示根資料夾卡片 + 未分類檔案)。與現有型別過濾(全部/圖片/影音)、排序、分頁自然疊加。
- **搜尋為全域**:搜尋字串非空時隱藏資料夾卡片、忽略資料夾過濾,顯示跨資料夾扁平結果(Drive 行為)。
- 資料夾 CRUD:工具列「新增資料夾」(建立於目前層級,名稱 dialog);卡片動作:改名、刪除
  (confirm → 伺服器拒絕非空 → toast)。以 `mediafolder` 權限閘 UI。
- 移動檔案:`MediaDetailDialog` 新增「資料夾」TreeSelect 欄位(含「未分類」= null;
  `buildRelationTree` 重用)隨表單儲存。不做卡片/列上的獨立「移至資料夾」動作——
  MediaGrid tile 是 `<button>`,巢狀互動元件無效 HTML,且 FilePicker 共用該元件,
  重構回歸風險不成比例(使用者已確認 2026-07-24)。
- 上傳:`MediaUploadDialog` 傳入 `currentFolderId`,dropzone 上傳時帶 `folderId`。

### 2. FilePicker 資料夾過濾

`FilePicker.vue` 清單上方加資料夾 TreeSelect 過濾(全部檔案(預設)/ 未分類 / 各資料夾),
與搜尋疊加;預設行為不變。項目表單與 Settings logo 同步受惠。

### 3. #6 連結移除

`MediaDetailDialog.vue`:刪除 `onOpenEditor`(L159-162)、footer 按鈕(L232)、未用的
`useRouter` import,及 `media.openInEditor` i18n key(en/zh-TW),同步更新測試。

### 4. i18n

新增 `media.*` 資料夾相關 key(zh-TW/en 成對,locale completeness 測試強制)。

---

## 錯誤處理

- 刪除非空資料夾:伺服器 4xx + `error.code`,前端 toast(i18n)。
- 移動/更新撞版本:沿用現有 409 `VERSION_CONFLICT` 恢復模式。
- 上傳帶不存在的 `folderId`:400,dropzone 顯示該檔錯誤列。
- 資料夾清單載入失敗:媒體庫退化為扁平列表(現行為)+ toast,不阻擋檔案瀏覽。

## 測試策略(TDD)

- **後端 xUnit**:MediaFolder CRUD(通用 items API)、刪除守衛(空/含檔案/含子資料夾)、
  循環守衛(自身/子孫)、upload `folderId`(有效/無效/省略)、Title 種入
  (一般/無 default 語系跳過/去副檔名)、FolderId 過濾查詢。
- **前端 vitest**:資料夾卡片導航與麵包屑、根層/資料夾過濾組查詢、全域搜尋切換、
  資料夾 CRUD dialog、移至資料夾、FilePicker 過濾、MediaDetailDialog(新欄位+按鈕移除)、i18n key 對齊。
- **e2e(Playwright, live PG+MinIO)**:建資料夾 → 資料夾內上傳(驗 Title 自動帶入)→
  移動檔案 → 麵包屑導航 → 刪除非空被拒 → 清空後刪除成功。

## 驗收門檻

後端 xUnit 全綠、vitest 全綠、`pnpm build`(vue-tsc)過、live PG+MinIO e2e 既有 17/17 +
新 spec 全過、live 手動 smoke(資料夾流程 + Title 帶入 + 連結已移除)。

## 執行方式

subagent-driven development:實作 = Sonnet subagent、複核 = Fable subagent;
brainstorm → 本 spec → writing-plans → execute → verify。
