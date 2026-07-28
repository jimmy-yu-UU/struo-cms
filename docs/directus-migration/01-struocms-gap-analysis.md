# StruoCMS 框架待解決問題 — 對接 Directus 體系完整專案

> 目的：列出「原本 Directus 體系做得到、但 StruoCMS 目前缺失或有落差」的功能，作為**遷移前先完善 StruoCMS 本身**的工作清單。
> 建立日期：2026-07-28
> 證據來源：live `web-directus-db` schema 實查、`D:\dotnet\viitorsemi-web-backend-re`、`D:\git\ViitorSemiWeb-PrimeVue`、`src/Struo.*` 原始碼。
> 姊妹文件：[`02-migration-guide.md`](./02-migration-guide.md)（完整遷移指引）。

---

## 0. 使用方式

- 本文件是 **StruoCMS 核心/下游能力補強 backlog**，不是遷移步驟（步驟見姊妹文件）。
- 每個 GAP 標註：嚴重度、目前狀態、需要的能力、證據、驗收標準、預估工作量、屬核心(core)或下游(fork)。
- 「屬下游」= 商業邏輯，應放在 fork（StruoCMS 本就是模板，fork 後加自訂 collection/功能），**不建議污染核心**（見 `CLAUDE.md §0` YAGNI at template level）。
- 「屬核心」= 每個 downstream headless CMS 都會需要，值得補進 `src/Struo.*`。

---

## 1. 落差總覽（優先順序）

| ID | 標題 | 嚴重度 | Core/Fork | 阻斷遷移? |
|----|------|--------|-----------|-----------|
| GAP-1 | 影像即時轉換端點（on-the-fly image transform） | 🔴 High | Core（或前端 CDN 決策） | 否，但影響全站圖片 |
| GAP-2 | 關聯查詢深度上限（MaxRelationDepth=5） | 🟠 Medium | Core | 否，剛好卡上限 |
| GAP-3 | 帶 payload 的 EAV 關聯建模與查詢（products_properties） | 🟠 Medium | Fork（建模）+ Core（查詢驗證） | 否 |
| GAP-4 | Aggregate / count 查詢（facet 計數、nested list total） | 🟠 Medium | Core | 否 |
| GAP-5 | 巢狀布林查詢 / 跨 to-many 排序限制 | 🟠 Medium | Core（視需求） | 否 |
| GAP-6 | 參數化 / faceted 搜尋能力歸屬決策 | 🟠 Medium | 決策 | 否 |
| GAP-7 | select-icon（icon picker）欄位型別 | 🟡 Low | Core（可選） | 否 |
| GAP-8 | 欄位分頁/分組版面（group-tabs / notice） | 🟡 Low | Core（可選，編輯 UX） | 否 |
| GAP-9 | 多值 cast-csv 儲存格式與值轉換 | 🟡 Low | 遷移 ETL 為主 | 否 |
| GAP-10 | Directus schema → C# 實體 scaffolding 工具 | 🟢 Nice | 工具 | 否，但省大量時間 |
| GAP-11 | 下載端點：published-only + presigned + 檔名語意 parity | 🟡 Low | Fork | 否 |
| GAP-12 | 翻譯欄位型別限制核對 | 🟡 Low | 核對 | 否 |

> **沒有任何 GAP 會阻斷遷移。** 全部為「先補強可讓遷移更順」的項目。

---

## 2. 已具備、無需補強（避免重複造輪 / 誤估範圍）

先明確「StruoCMS 已能等價」的部分，避免把它們排進 backlog：

| 能力 | StruoCMS 現況 | 對應 Directus |
|------|----------------|----------------|
| 主要實體 PK = uuid | 內容實體 `Guid`(UUIDv7) | Directus 主要實體本來就是 `uuid` → **可 1:1 保留**，不需重映射 |
| 翻譯 i18n | `[CmsTranslations]` sidecar（`{Parent}Id`+`Locale`+欄位） | `*_translations`（`{parent}_id`+`languages_code`） → **同構** |
| SEO 翻譯 | 專屬 `SeoTranslation`（per-locale title/meta/og） | `seo-interface` (cast-json) → **天然吻合** |
| 媒體儲存 | `IFileStorage` S3 實作（`ForcePathStyle`，MinIO 相容）+ 檔案 sidecar Title/Alt + 軟刪除 | `directus_files` + MinIO |
| RBAC | collection 級 read/write/delete + super-admin + public role | Directus 實查：public 純唯讀 `fields=*` 無 row rule；內容表**未使用**欄位級/列級權限 → **collection 級已足夠** |
| 匿名唯讀交付 | `public` role + `Rbac:PublicReadCollections` + 已發布檔案匿名可讀 | public policy 對內容表全開唯讀 |
| M2O / O2M / M2M 關聯 | `[Navigate]`+`[CmsRelation]`；REST `deep` / GraphQL 巢狀 | list-m2m / list-o2m / select-dropdown-m2o |
| 富文字 / Markdown | RichText(TipTap，server 端 sanitize) / Markdown | input-rich-text-html / -md |
| 版本 / 軟刪除 | 逐 collection opt-in Revisions / `ISoftDeletable` | （Directus 內建，本專案未重度使用） |
| SSO | OIDC/SSO（JIT by email） | Microsoft OIDC |
| REST + GraphQL | envelope REST + HotChocolate GraphQL（filter/sort/mutation/跨關聯） | REST `/items` + GraphQL |

---

## 3. 落差明細

### 🔴 GAP-1 — 影像即時轉換端點

**目前狀態**：StruoCMS `GET /api/files/{id}/content` 只回原始 bytes（或 302 presigned）；**無縮圖/格式轉換**。
**Directus 能力**：`/assets/<id>?width=&height=&quality=&format=&fit=` 動態縮圖。
**證據**：前端 `app/utils/image-url.ts` / `app/providers/cdn.ts` 的 `buildImagePath` 直接組 Directus transform 參數；cache-bust `v=` 用檔案 `modified_on`。全站圖片（產品、banner、OG image）都走這條。
**影響**：不解決則圖片載入以原檔輸出，頻寬/效能惡化；或前端每張圖都要改。
**選項**：
- (a) **前端 CDN 模式**：啟用前端既有的 Cloudflare `CDN_BASE`（`/cdn-cgi/image/...`），StruoCMS 只需回原檔。← 最省 StruoCMS 工。
- (b) **核心加轉換端點**：`GET /api/files/{id}/content?width=&format=&fit=&quality=`，用 ImageSharp 之類即時處理 + 快取。← 提升 StruoCMS 通用性。
- (c) 預生成尺寸（上傳時產多規格）。
**驗收標準**：前端在不改 client 程式碼的前提下，`<img>` 能取得指定尺寸/格式的圖，且有快取。
**預估**：(a) 前端設定 0.5d；(b) 核心 2～3d（含快取層）。
**Core/Fork**：Core（若選 b）。

---

### 🟠 GAP-2 — 關聯查詢深度上限

**目前狀態**：`StruoQueryOptions.MaxRelationDepth` 預設 **5**，超過回 400（`src/Struo.Application/Configuration/StruoQueryOptions.cs`）。
**Directus 能力**：任意深度巢狀。
**證據**：前端 `server/api/categories/[slug].get.ts` 對 `categories.upper_id` 做 **5 層**遞迴自我關聯（麵包屑）；sitemap 亦遞迴。**剛好貼上限**。
**影響**：只要分類階層再深一層即破；且 5 層的 batched 展開需驗證 N+1 與效能。
**建議**：把 `MaxRelationDepth` 提高（如 8）或設為可配置並驗證；針對自我關聯遞迴確認 batched expander 正確。
**驗收標準**：`upper_id` 遞迴 6+ 層可正確回傳且無 N+1 爆量。
**預估**：0.5d（設定）+ 1d（效能驗證）。**Core**。

---

### 🟠 GAP-3 — 帶 payload 的 EAV 關聯建模與查詢

**目前狀態**：StruoCMS M2M junction 是**純 junction**（僅兩個 FK，見 sample `ArticleTag`）。
**Directus 資料**：`products_properties`(id, products_id uuid, properties_id uuid, **value varchar**) — junction **帶資料欄位 value**，**8,161 列**，是產品參數的核心 EAV。
**證據**：live schema 實查 + `.NET` 後端 `ProductParametricQueryBuilder` 對此表做 facet/range 查詢。
**建模方式**：不能用純 M2M，要把 `products_properties` 建成**獨立 collection**（`ProductProperty` entity：M2O→products、M2O→properties、`Value` 欄位）。StruoCMS 支援此建模。
**待驗證**：透過 StruoCMS REST/GraphQL 是否能等價表達參數化查詢（多屬性 AND、range 比較、facet 計數）。與 GAP-4、GAP-5、GAP-6 相關。
**驗收標準**：能以 StruoCMS 查詢在 `ProductProperty` 上取得「符合多個 (propertyCode, value/range) 條件的 products」與各屬性的可選值計數。
**預估**：建模 0.5d；查詢驗證 1～2d。**Fork（建模）+ Core（查詢能力驗證）**。

---

### 🟠 GAP-4 — Aggregate / count 查詢

**目前狀態**：StruoCMS list 回 `PagedResult{data,total,limit,offset}`；但**巢狀 to-many list 無 total/hasMore metadata**；GraphQL 無 aggregate 語法。
**Directus 能力**：`posts_aggregated { count { id } }`、`post_products_func { count }`、`*_aggregated`。
**證據**：前端 `server/api/news/index.get.ts`（`posts_aggregated`）、`applications/[slug].get.ts`（`post_products_func { count }`）。用於分頁總數與關聯計數。
**影響**：分頁 UI、facet 計數需要 count。
**建議**：
- 頂層 total 已有 → BFF 可改用。
- 巢狀 to-many 補 `total`/`hasMore`；或提供 count-only 查詢端點。
- faceted 計數見 GAP-6（可能交給 Meilisearch/ported service，不一定要核心做）。
**驗收標準**：BFF 能取得列表總數與（必要的）關聯計數，不需 Directus 專屬 `_aggregated` 語法。
**預估**：1～2d。**Core**。

---

### 🟠 GAP-5 — 巢狀布林查詢 / 跨 to-many 排序限制

**目前狀態**：查詢 DSL 僅單層 `_and`/`_or`（巢狀布林群組被 `QueryValidator` 拒絕）；**不支援跨 to-many 關聯排序**（`QueryValidator.cs:39`）。
**Directus 能力**：任意巢狀 filter 群組。
**影響**：視參數化查詢與前端實際 filter 複雜度而定。目前前端多為單層條件，**可能不需補**，但參數化搜尋（多屬性交集）需確認。
**建議**：先以 GAP-3 的實際查詢需求驗證；若參數化查詢需要巢狀 OR/AND 或關聯排序，再擴充 DSL。
**驗收標準**：能表達參數化搜尋所需的全部 filter 組合。
**預估**：視需求 1～3d。**Core（視需求）**。

---

### 🟠 GAP-6 — 參數化 / faceted 搜尋能力歸屬決策

**目前狀態**：StruoCMS 無內建 faceted search / facet 計數。
**現有方案**：`.NET` 後端 `ProductParametricQueryService`（EAV 上動態 SQL、facet 計數、breadcrumb）+ Meilisearch（前端目錄實際用 Meilisearch）。
**決策點**：遷移後，faceted 搜尋要
- (a) **維持 ported service + Meilisearch**（推薦，工作量小、既有邏輯可攜），StruoCMS 只當資料源；或
- (b) 讓 StruoCMS 核心提供 facet 查詢（通用性高但工程量大，且與 YAGNI 抵觸——非每個 headless CMS 都需要）。
**建議**：**(a)**。把參數化查詢與 Meilisearch 同步當作 fork 的自訂功能，StruoCMS 專注做 CMS。
**驗收標準**：明文記錄此決策於遷移指引，避免後續反覆。
**預估**：決策 0d；落實見遷移指引。**決策**。

---

### 🟡 GAP-7 — select-icon（icon picker）欄位型別

**目前狀態**：StruoCMS 無 icon picker interface。
**Directus 資料**：`social_medias.icon`（select-icon）。前端把 `_`→`-` 後給 Iconify。
**影響**：極小，單一欄位。
**建議**：用 `Text` 或帶 `[CmsOptions]` 的 `Select` 代替即可；如要完全等價再考慮新增 icon 欄位型別（含前端 registry 對應）。
**驗收標準**：後台能編輯 icon 名稱、前端能渲染。
**預估**：代替 0d；新型別 0.5d（含前端）。**Core（可選）**。

---

### 🟡 GAP-8 — 欄位分頁/分組版面（group-tabs / group-raw / presentation-notice）

**目前狀態**：StruoCMS `[CmsField(Group=...)]` 有欄位分組；但**無 tab 分頁版面、無 notice 說明區塊**。
**Directus 資料**：`group-tabs`、`group-raw`、`presentation-notice`（皆 alias/no-data，純版面）。
**影響**：僅後台編輯體驗，非資料。
**建議**：非必要。若要提升編輯 UX，可在 admin 加 tab 群組與 help/notice 區塊（StruoCMS `[CmsField(HelpText=...)]` 已可部分替代 notice）。
**驗收標準**：複雜表單可分頁/分區呈現。
**預估**：1～2d（前端為主）。**Core（可選）**。

---

### 🟡 GAP-9 — 多值 cast-csv 儲存格式與值轉換

**目前狀態**：StruoCMS 多值選擇存為 **JSON in text**（`IsJson`+text）。
**Directus 資料**：`select-multiple-dropdown` 有 **cast-csv** 變體（如 `properties.show_in`，值如 `parametric,post`），前端用 `_contains` 查詢。
**影響**：屬**遷移 ETL** 的值轉換（CSV → JSON 陣列），非框架缺陷。查詢端 `_contains` 對 JSON 陣列的語意需確認。
**建議**：ETL 時把 CSV 拆成 JSON 陣列；確認 StruoCMS 對多值欄位的 `_contains`/包含查詢語意。
**驗收標準**：`show_in` 包含某值的查詢可正確過濾。
**預估**：ETL 0.5d + 查詢確認 0.5d。**遷移 ETL 為主**。

---

### 🟢 GAP-10 — Directus schema → C# 實體 scaffolding 工具

**目前狀態**：StruoCMS collection 為 code-first，每張 Directus 表需手寫 C# 實體 + 屬性。
**痛點**：35 個 collection 手寫易錯、耗時。
**建議（非必要但高 ROI）**：寫一次性 code generator，讀 `directus_collections`/`directus_fields`/`directus_relations` 產生 `[CmsCollection]`/`[CmsField]`/`[Navigate]` 骨架 + SqlSugar 對應，再人工微調。
**驗收標準**：能自動產出可編譯的實體骨架，涵蓋欄位型別/關聯/翻譯 sidecar。
**預估**：1～2d，省下數天手工。**工具（可放 fork 的 tools/）**。

---

### 🟡 GAP-11 — 下載端點語意 parity

**目前狀態**：StruoCMS 有 `PresignedRedirect` 選項與已發布檔案匿名讀取。
**Directus 方案**：`.NET` `GET /download/<id>/<filename>` → 302 presigned S3，**只允許 published documents**，含檔名淨化 / 副檔名推斷 / Content-Disposition 跳脫（`DocumentDownloadService`）。
**落差**：published-only 判斷、以「document」而非「file」為單位（documents 有翻譯與 type）、檔名處理，屬**商業語意**。
**建議**：作為 fork 的自訂下載端點移植；StruoCMS 檔案服務提供 presigned 底層。
**驗收標準**：只有 published document 可下載、302 到 presigned、檔名/Content-Disposition 正確。
**預估**：1d。**Fork**。

---

### 🟡 GAP-12 — 翻譯欄位型別限制核對

**目前狀態**：StruoCMS 規定 MultiSelect / CheckboxGroup / Tags / KeyValue / Files / Repeater **不可 translatable**（`MetadataScanner` fail-fast）。
**風險**：若某 Directus `*_translations` 表含這類多值/JSON 欄位，建模時會 fail-fast。
**證據**：live 實查翻譯表多為 name/title/content/description/seo 等文字，**初判無衝突**，但需逐表核對欄位型別。
**建議**：ETL 前列出每個 `_translations` 表的欄位型別，確認皆為可 translatable 的純量/文字。
**驗收標準**：所有翻譯 sidecar 欄位型別通過 StruoCMS metadata 掃描。
**預估**：0.5d 核對。**核對**。

---

## 4. 建議實作順序（先框架、後遷移）

1. **決策先行**：GAP-6（faceted 搜尋歸屬 → 建議 ported service + Meilisearch）、GAP-1（圖片走 CDN 或核心轉換）。
2. **核心補強（阻礙 API parity 的）**：GAP-2（深度）、GAP-4（count/aggregate）、GAP-5（視需求擴充 DSL）。
3. **建模與驗證**：GAP-3（EAV 建模 + 參數化查詢驗證）、GAP-12（翻譯欄位核對）、GAP-9（多值值轉換）。
4. **工具/體驗（可延後）**：GAP-10（scaffolding 工具）、GAP-7 / GAP-8（欄位型別/版面）、GAP-11（下載端點，遷移期做）。

完成 1～3 後，StruoCMS 即具備承接本專案的 API/資料能力；4 為加速與體驗優化。

---

## 5. 未列入（明確排除，避免範圍膨脹）

- **UI 動態建 collection/欄位**：StruoCMS 世界觀為 code-first schema，**不打算**做成 Directus 式 UI 建表（與模板定位牴觸）。本專案 schema 穩定、開發者擁有，此限制影響低。若未來確有非開發者自助建模需求，屬另一個大型 epic，本文件不涵蓋。
- **欄位級/列級權限**：Directus 內容表實際未使用，StruoCMS collection 級 RBAC 已足夠，不補。
- **Meilisearch / 聯絡表單 email / 下載**：屬外部依賴或 fork 商業邏輯，非 StruoCMS 核心（見遷移指引 §外部依賴）。
