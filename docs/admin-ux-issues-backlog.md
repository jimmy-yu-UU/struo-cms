# Admin UX 12 項回報 — 原文備份與追蹤 (2026-07-23 使用者回報)

> 目的：使用者原始回報的完整原文 + 各項狀態。此清單是後續 Batch C 等工作的唯一權威來源
> （先前曾因只存在對話中而遺失細節——一律以本文件為準）。

## 原始回報（逐字保留）

1. darkmode切換沒有過度動畫，而是直接切，需改進
2. 列表項目右邊應該要有編輯及刪除按鈕，而不是只有刪除，點選編輯才進入編輯介面，直接點列不應該進入
3. Permission修改控制太不方便了，應該要是在Role裡面編輯他有哪些權限，而不是單獨去一個一個新增設置，這很蠢
4. 很多關聯資料只顯示Id，這樣根本沒辦法給人讀，像是User和UserRole的關聯，且User介面中應該就要顯示及可以編輯這個用戶的權限，而不是到UserRole裡面一個一個添加，和前一項本質上是一個問題，使用極度不方便
5. 媒體庫希望可以有資料夾功能，這個資料夾只是用來在系統中整理，和實際儲存不一定關聯(部分S3-Compatible似乎並不完全支援子資料夾)
6. 媒體庫上傳圖片後，卻無法看見上傳圖片的預覽圖，查看詳情中也是死圖，另外為什麼這邊還有一個完整編輯介面指向System >> File? 這完全多此一舉，不需要再額外的地方，媒體庫就是檔案列表，就應該要可以直接編輯，而不是連到一個左邊列表中甚至不存在的Collection中去編輯
7. 上傳圖片或檔案時，應該要自動帶入當前檔名到檔案的Title，會比較方便，這個需要優化
8. 為什麼我在Language中新增了一個語言，再進其他collection去修改內容，但卻發現沒有新增語系的介面?重新整理就有了，代表狀態並不會自動更新，這個要優化，順帶一提我新增的是簡體中文
9. 翻譯欄位之間為什麼沒有間距? 介面還需要額外優化
10. 媒體庫dialog可以放大到70-80%以上，方便使用者托拽檔案進入，介面需優化
11. 為什麼Collection中列表我隨便修改某一項的資料，儲存後回到列表排序就不一樣了? 我修改的是第一筆，但修改後就跑到中間去了，需審視排序邏輯
12. Collection中的使用中、回收桶具體作用和差異是什麼?目前頁面上面看不出區別點，如果是用刪除來控管的話，那這個跟status的archived本身功能是重複且衝突的吧? 需審視及優化這部分的核心設計

## 狀態總表

| # | 摘要 | 狀態 | 批次 / commit |
|---|------|------|--------------|
| 1 | 深色模式過渡動畫 | ✅ 完成 | Batch A (a521820) |
| 2 | 列表明確編輯/刪除按鈕、列點擊不導航 | ✅ 完成 | Batch A |
| 3 | Role 內編輯權限（矩陣） | ✅ 完成 | Batch B (651c1f1) + B.1 (ac8bbe8) |
| 4 | 關聯顯示名稱、User 內編輯角色 + 有效權限 | ✅ 完成 | Batch B + B.1 |
| 5 | **媒體庫資料夾（純系統整理，與實體儲存解耦）** | ✅ 完成 | **Batch C**：巢狀 `MediaFolder` 核心實體 + `File.FolderId`；Drive 式卡片 + 麵包屑 + 全域搜尋；資料夾 CRUD（非空刪除由框架 `OnDelete.Restrict` 守衛→409）；自引用循環守衛；FilePicker 資料夾過濾 |
| 6 | 死圖（✅ Batch A 修）；**媒體庫詳情移除指向 System>>File 的完整編輯介面連結** | ✅ 完成 | Batch A（死圖）+ Batch B（側欄 Hidden File）+ **Batch C**（MediaDetailDialog 完整編輯連結移除；改以詳情內資料夾 TreeSelect 移動檔案） |
| 7 | **上傳自動帶入檔名到 Title** | ✅ 完成 | **Batch C**：上傳交易內種入預設語系 Title（去副檔名，dotfile fallback，clamp 255） |
| 8 | Language 新增後語系 tab 即時更新 | ✅ 完成 | Batch A |
| 9 | 翻譯欄位間距 | ✅ 完成 | Batch A |
| 10 | 媒體 dialog 放大 ~78vw | ✅ 完成 | Batch A |
| 11 | 列表排序穩定（createdAt DESC + PK tiebreak） | ✅ 完成 | Batch A |
| 12 | 回收桶視覺區別（✅ Batch A 修）；**核心設計審視：File 軟刪除（使用者已核可做法），並釐清 soft-delete 與 sample `status=archived` 的語意分工** | 🔶 部分 | **獨立批次（後端）** |

## 追加批次（使用中發現，非原始 12 項）

- **B.1**（ac8bbe8）：統一離開守衛（雙對話框）、`?roles=` 即時有效權限預覽、建立模式權限矩陣。
- **B.2**（16bef0a）：PrimeVue `<a href="#">` 預設 hash 導航取消守衛暫停中的導航（麵包屑/登出卡死）→ command 內 preventDefault 先行。
- **Batch C**（branch `admin-ux-batch-c`）：#5 媒體資料夾 + #7 檔名帶入 Title + #6 連結移除。實作重點：`MediaFolder` 核心實體（Hidden、走通用 items API）、`File.FolderId`、migration `002-media-folders.sql`、metadata 驅動自引用循環守衛、上傳 folderId + Title 種入。**最終全分支複核抓到 Critical C-1**（items API 不投影 M2O FK 到非 deep 回應，前端卻在讀 → 詳情頁 Save 會默默移走檔案 + 資料夾樹平鋪）→ 改用 `deep=parent/folder` 修畢。Live 驗證：後端 API 契約全證、e2e 18/18(含 C-1 回歸)、DB-16 parity 確認。

## 剩餘工作規劃順序（使用者確認過的分組）

1. **#12 後端**：File 軟刪除（框架 `ISoftDeletable` 掛上 File entity；已獲使用者核可）

## 已知 deferred minors（非阻塞，順手時處理）

- B.1：面板 reload 無 latest-wins token（FE-14 模式）、OpenAPI `roles` 參數不可見、矩陣勾了又取消 phantom-dirty、hint 在 loadFailed 仍顯示、public-floor 測試 vacuous-loop、ItemFormView 測試 i18n missing-key 噪音
- B：role 刪除遺留孤兒 permission/user_roles 列（FK 由 audit 決策延後；候選 follow-up = role-delete cascade）、矩陣併發 last-writer-wins、矩陣 checkbox a11y、audit 欄位（CreatedBy/UpdatedBy）名稱解析
