# Directus → StruoCMS 遷移指引（ViitorSemi 專案）

> 目的：完整保留本次分析，作為未來把 ViitorSemi 的 Directus 體系整套遷移到 StruoCMS 的權威參考，避免重新研究。
> 建立日期：2026-07-28
> 前置文件：先讀 [`01-struocms-gap-analysis.md`](./01-struocms-gap-analysis.md)（框架需先補強的落差）。
> 證據來源：live `web-directus-db`（唯讀實查）、`D:\dotnet\viitorsemi-web-backend-re`、`D:\git\ViitorSemiWeb-PrimeVue`、`src/Struo.*`。

---

## 1. 系統全貌

ViitorSemi 現行體系由 **4 個系統 + 3 個外部依賴** 組成：

```
┌─────────────────────────────────────────────────────────────┐
│ Nuxt 4 前端 (D:\git\ViitorSemiWeb-PrimeVue)                    │
│  client → 自家 /api/** (Nitro BFF)                            │
│         server/ ── GraphQL/REST ─→ Directus 11               │
│                 ── $fetch ───────→ .NET 匯入後端 (contact/download) │
│                 ── proxy ────────→ Meilisearch (目錄/搜尋)     │
└─────────────────────────────────────────────────────────────┘
        │                    │                      │
   Directus 11          .NET 後端              Meilisearch
   (web-directus)   (viitorsemi-web-backend-re)   (3 indexes)
        │                    │
        └──── 同一個 Postgres (web-directus-db) ────┘   ← 後端直接讀寫 Directus 實體 schema
                     │
                MinIO (web-directus-bucket)  ← 檔案 blob
```

**關鍵架構事實：**
1. **前端不直接打 Directus**：走 Nuxt BFF（`server/api/**`），Directus 耦合封在 `server/` 內 → **換後端只需改 `server/`，client/畫面零改動**。
2. **.NET 後端不透過 Directus API**：**直接讀寫 Directus 的實體 Postgres schema**（domain entity 就是 Directus 表），無 API 抽象層。
3. **外部依賴**（Meilisearch 索引、聯絡表單 email 落點、檔案下載）**不屬 CMS**，遷移需一併保留餵資料來源。

---

## 2. 來源資料清單（live 實查 2026-07-28）

**35 個使用者 collection**，主要資料量與 PK 型別：

| Collection | 列數 | PK 型別 | 備註 |
|---|---|---|---|
| products | 812 | **uuid** | 產品主檔 |
| products_translations | 2,102 | integer | sidecar |
| **products_properties** | **8,161** | integer | **EAV：products_id + properties_id + value(varchar)** |
| products_documents | — | — | M2M junction |
| categories | 28 | **uuid** | **含 `upper_id` 自我關聯（遞迴階層）** |
| categories_translations | 84 | integer | |
| categories_properties | — | — | junction（分類↔屬性） |
| properties | 23 | **uuid** | 參數定義（type: select/range, `show_in` cast-csv） |
| properties_translations | 23 | integer | |
| product_packages | 83 | **uuid** | 封裝，含 `image`→directus_files |
| product_groups | 72 | **uuid** | 含 `datasheet_document_id` |
| documents | 607 | **uuid** | 含 `file`→directus_files、`document_type_id` |
| documents_translations | 607 | integer | |
| document_types (+_translations) | — | uuid/int | |
| posts | 18 | **uuid** | 新聞/文章，含 post_type_id、post_category_id |
| posts_translations | 50 | integer | |
| posts_products / posts_documents / posts_properties | — | — | M2M junctions |
| post_types (+_translations) | — | uuid/int | |
| product_statuses (+_translations) | — | uuid/int | 產品狀態（code + 名稱） |
| competitor_part | 3,205 | **uuid** | 競品交叉對照 |
| competitor_part_products | 3,306 | — | junction |
| hero_swiper (+_translations) | 2 | **uuid** | 首頁 banner，含 background_image_id |
| site_settings (+_translations) | 1 | **uuid** | 單例站台設定 |
| contact_datas (+_translations) | 28 | **integer** | 聯絡資訊 |
| social_medias | 6 | **uuid** | 社群連結（icon = select-icon） |
| languages | 3 | code | en-US / zh-TW / zh-CN |
| directus_files | 1,299 | uuid | 檔案 metadata（blob 在 MinIO） |
| ai_prompts | 0 | — | 空，可略 |

> **PK 決定性結論**：**主要實體 PK 全是 `uuid`** → 可原樣保留、FK 不斷、前端引用 id/asset 不失效。僅 integer 型的 sidecar/junction/contact_datas 需重新產生 key（它們只被 FK 參照、不外露）。

---

## 3. 目標建模：Collection 對應表

每張 Directus collection → StruoCMS `[CmsCollection]` C# 實體。建議放在下游 fork（如 `samples/` 旁的 `Viitorsemi.Content` 專案，列入 `Struo:ContentAssemblies`），**不進核心**。

| Directus collection | StruoCMS 實體 | 關鍵欄位/關聯 | 註記 |
|---|---|---|---|
| products | `Product` | part_number, delivery_mode, base/box/carton/moq(qty 皆 varchar), M2O: category/package/group/status/datasheet_document/image | PK 保留 uuid |
| products_translations | `ProductTranslation` | features, seo(→SeoTranslation) | sidecar |
| products_properties | `ProductProperty` | **M2O products + M2O properties + Value(string)** | **EAV 獨立 collection**（GAP-3） |
| categories | `Category` | slug, status(bool), clickable, **M2O self `UpperId`** | 遞迴階層（GAP-2） |
| categories_translations | `CategoryTranslation` | name | |
| categories_properties | `CategoryProperty` | M2O category + M2O property (+sort?) | junction |
| properties | `Property` | code, type('select'\|'range'), status, **show_in(多值 cast-csv)**, sort | GAP-9 值轉換 |
| properties_translations | `PropertyTranslation` | name | |
| product_packages | `ProductPackage` | name, image→file, 各種 qty | |
| product_groups | `ProductGroup` | M2O datasheet_document, O2M products | |
| documents | `Document` | M2O file, M2O document_type, status | 下載來源 |
| documents_translations | `DocumentTranslation` | name, description | |
| document_types(+_tr) | `DocumentType`(+Tr) | slug, name | |
| posts | `Post` | slug, status, sort, published_at, M2O post_type/post_category, M2M products/documents/properties | |
| posts_translations | `PostTranslation` | title, content, seo | |
| post_types(+_tr) | `PostType`(+Tr) | slug, name | |
| product_statuses(+_tr) | `ProductStatus`(+Tr) | code, name | |
| competitor_part | `CompetitorPart` | competitor_part_number, manufacturer, specs(json), M2M products | 商業（cross-reference） |
| competitor_part_products | `CompetitorPartProduct` | junction | |
| hero_swiper(+_tr) | `HeroBanner`(+Tr) | status, text_align, jump_url, background_image, title, description | |
| site_settings(+_tr) | `SiteSetting`(+Tr) | selection_guide_document, company_name, intro, about_us | 單例 |
| contact_datas(+_tr) | `ContactData`(+Tr) | type, email, phone, fax, name, address, country | PK integer→新 uuid |
| social_medias | `SocialMedia` | name, status, link, icon | icon 用 Text/Select（GAP-7） |
| languages | 對映 StruoCMS `Language` collection | code, name | 已有核心表 |

---

## 4. 欄位型別對應表（Directus interface → StruoCMS FieldInterface）

實查 `directus_fields` 用到的所有 interface：

| Directus interface (special) | StruoCMS FieldInterface | 對應 | 註記 |
|---|---|---|---|
| input | Text | ✅ | |
| input (uuid) | Uuid | ✅ | |
| input-multiline | Textarea | ✅ | |
| input-rich-text-html | RichText | ✅ | server 端 sanitize |
| input-rich-text-md | Markdown | ✅ | |
| boolean (cast-boolean) | Boolean | ✅ | |
| datetime | DateTime | ✅ | |
| datetime (date-created/date-updated) | 稽核欄位 | ✅ | → AuditableEntity |
| select-dropdown-m2o (user-created/updated) | 稽核 user 欄位 | ✅ | → CreatedBy/UpdatedBy |
| file / file-image (file) | File / Image | ✅ | |
| select-dropdown | Select | ✅ | 配 `[CmsOptions]` |
| select-multiple-dropdown (cast-json) | MultiSelect | ✅ | |
| select-multiple-dropdown (**cast-csv**) | MultiSelect | ⚠️ | 存法差異，ETL 轉 JSON（GAP-9） |
| list (cast-json) | Repeater | ✅ | 注意 StruoCMS repeater 淺層限制 |
| list-m2m | M2M 關聯 | ✅ | |
| list-o2m | O2M 關聯 | ✅ | |
| select-dropdown-m2o (m2o) | M2O 關聯 | ✅ | |
| translations | sidecar 翻譯 | ✅ | 同構 |
| seo-interface (cast-json) | SeoTranslation | ✅ | 專屬支援 |
| select-icon | — | 🔶 | 用 Text/Select 代替（GAP-7） |
| group-tabs / group-raw / presentation-notice | 欄位 Group / — | 🔶 | 純版面，非資料（GAP-8） |

**結論：實際使用的資料型欄位 100% 有對應**；兩個缺口（icon、tab/notice）皆為編輯體驗裝飾，不阻斷。

---

## 5. 關聯對應（含特殊案例）

Directus 用到的關聯種類與 StruoCMS 表達：

| 案例 | Directus | StruoCMS 做法 |
|---|---|---|
| 一般 M2O | products.category_id → categories 等 | `Guid? CategoryId` + `[Navigate]` nav |
| O2M 反向 | product_groups.products | nav list `[Navigate]` |
| **純 M2M** | posts_products, posts_documents, products_documents, product_groups_documents, product_packages_documents | junction 類別 + `[Navigate(typeof(J), fkA, fkB)]` |
| **帶 payload EAV** | **products_properties (value)** | **獨立 collection** `ProductProperty`（2×M2O + Value），非純 M2M（GAP-3） |
| junction 帶 sort | categories_properties, posts_properties | 建 junction collection（可含 sort 欄位） |
| **自我遞迴** | **categories.upper_id → categories** | M2O self；前端遞迴 5 層 → 確認 `MaxRelationDepth`（GAP-2） |
| 檔案關聯 | products.image, documents.file, hero_swiper.background_image_id → directus_files | M2O → StruoCMS `File` |

---

## 6. i18n 對應

- **模式同構**：Directus `*_translations`(`{parent}_id` + `languages_code` + 欄位) ↔ StruoCMS `[CmsTranslations]` sidecar(`{Parent}Id` + `Locale` + `[CmsField]`)。
- **語言**：en-US / zh-TW / zh-CN → StruoCMS `Language` collection（Code/Name/IsDefault/Enabled/Sort）。以 en-US 為 default。
- **前端行為**：前端**一次抓全部翻譯陣列**（`translations{ languages_code, ... }`），client 端選 locale + 英文 fallback（`pickTranslation`/`getTranslationText`）。**無 `?lang=` 過濾**。
  → StruoCMS 需能回傳**完整 translations 陣列含 locale 判別欄位**（StruoCMS 已把翻譯以 `[Translation!]` map 暴露；BFF 改寫時對齊此形狀，見 §9）。
- **核對**：確認無翻譯欄位使用 StruoCMS 不可 translatable 的型別（MultiSelect/Tags/KeyValue/Files/Repeater）——初判無（GAP-12）。

---

## 7. 媒體 / 檔案對應

| 項目 | Directus | StruoCMS |
|---|---|---|
| metadata 表 | directus_files（1,299 列，uuid PK） | `File`（uuid PK，可保留原 id） |
| blob 儲存 | MinIO bucket `web-directus-bucket`（path-style, endpoint :9000） | `S3FileStorage`（`ForcePathStyle=true`，指同一 MinIO/bucket 即可） |
| per-locale alt/title | （Directus 檔案 metadata） | `FileTranslation`(Title/Alt per locale) |
| 服務端點 | `/assets/<id>` + transform params | `GET /api/files/{id}/content`（原檔/302） |
| **即時縮圖** | `?width=&fit=&format=&quality=` | **無 → GAP-1**（前端 CDN 或核心加轉換） |
| 前端引用 | file `{id, filename_download, filesize, created_on, modified_on, uploaded_on}`；cache-bust 用 `modified_on` | StruoCMS 需暴露等價欄位或 BFF 映射 |

**遷移策略**：保留 directus_files 的 uuid → StruoCMS `File.Id`；blob 續用同一 MinIO bucket（免搬檔）；圖片轉換走 GAP-1 決策。

---

## 8. 身分 / RBAC / 安全對應

**Directus 現況（實查 policies/permissions）：**
- **public policy**（no-role）：對所有內容表 **read only、`fields=*`、無 row rule** → 純匿名唯讀。
- **Editor policy**：內容表全 CRUD、幾乎全 `fields=*`、內容表無 row-level 規則（僅 directus 系統表有零星 PARTIAL/ROWRULE）。
- **Administrator**：全開。
- 後台登入：**Microsoft OIDC**。

**StruoCMS 對應：**
| Directus | StruoCMS |
|---|---|
| public 匿名唯讀 | `public` role + `Rbac:PublicReadCollections`（列入所有前端讀取的 collection）+ 已發布檔案匿名可讀 |
| Editor 全 CRUD | 一般 role 授予各 collection read/write/delete |
| Administrator | `IsSuperAdmin` role |
| Microsoft OIDC | StruoCMS OIDC/SSO（JIT by email，設定 Microsoft issuer/client） |
| 欄位級/列級權限 | **不需要**（Directus 內容表未使用）→ collection 級 RBAC 已足夠 |

**安全升級點：**
- `.NET` 後端目前用**單一靜態 X-Api-Key + 共用匯入密碼、無 per-user 身分**（匯入蓋 `DefaultUserId`）→ 遷移後改用真實 user/role/bearer，是**明確升級**。
- `directus.env` 內含**明文密鑰**（DB 密碼 `qaz@1234`、MS client secret、MinIO `minioadmin/minioadmin`）→ 遷移是**輪替所有密鑰**的時機；StruoCMS 用 appsettings + env override（`Section__Key`），機密勿進 repo。

---

## 9. API 契約：前端 BFF 路由改寫計畫

前端 14 條 `server/api/*` 路由對 Directus 慣用語高度依賴。**能力足夠、契約不相容**，逐條改寫對接 StruoCMS 原生契約（client 端不動）。

| BFF 路由 | 現用 Directus 慣用語 | StruoCMS 改寫要點 |
|---|---|---|
| news/index | `posts` filter `post_category_id.slug _eq`, `status _eq published`, `posts_aggregated{count}` | 跨關聯 filter（dotted path）+ 用 list `total` 取代 aggregate（GAP-4） |
| news/[slug] | 深層 posts + post_products.products_id + property `show_in _contains post` | 多層 deep（≤5）+ EAV 查詢 + 多值包含查詢 |
| posts/[slug] | 輕量 posts | 直接映射 |
| categories/[slug] | **upper_id 遞迴 5 層** + category_properties `show_in _contains parametric` | 確認深度（GAP-2）+ 多值查詢 |
| products/[part_number] | 巨型深層 products 查詢（package/group/status/properties/documents + translations） | 多層 deep + EAV + 翻譯陣列 |
| products/parametric | categories.category_properties facet metadata | facet 定義查詢 |
| applications/[slug] | categories + posts.post_products 分頁 + `post_products_func{count}` | count（GAP-4） |
| documents | documents filter by type/status, nested products/file/type | 跨關聯 filter + deep |
| site-settings | site_settings `limit:1` + translations | 單例查詢 + 翻譯 |
| hero-banners | hero_swiper `status published`, `sort`, background_image | 直接映射 |
| social-medias | social_medias `sort` | 直接映射 |
| contact-list | contact_datas `status true` + translations | 直接映射 |
| categories (REST) | `/items/categories` field-selection + `limit:-1` | StruoCMS list（無 `-1`，用大 limit 或全量端點） |
| __sitemap__ | posts/products 分頁 1000/page + categories 遞迴 | 分頁 + 遞迴 |

**Directus 慣用語 → StruoCMS 對照：**
- filter `_eq/_neq/_nnull/_contains` → StruoCMS REST `_eq/_neq/_nnull/_contains`（相近）；GraphQL 用無底線 `eq/contains`。
- `sort:["-sort","-id"]` → StruoCMS `sort=-sort,-id`。
- `*_aggregated{count{id}}` / `*_func{count}` → 用列表 `total`（GAP-4）。
- status 語意**逐表不同**（`"published"` 字串 / `true` 布林 / `status_id.code`）→ 建模時保留對應欄位型別與值。
- `/assets/<id>?transform` → GAP-1 決策。

**改寫路徑選擇：**
- **(A) 改寫 BFF（推薦）**：只動前端 `server/`，範圍可控。
- (B) StruoCMS 加 Directus 相容層（`_aggregated`、`/assets` transform、Directus filter 別名）：工程量大，不建議，除非要無痛替換。

---

## 10. .NET 匯入後端移植計畫

`viitorsemi-web-backend-re` = Directus 旁的商業邏輯服務（**非 CMS**），與 StruoCMS **同技術棧**（.NET 10 + SqlSugar + Clean Architecture）。**可直接成為 StruoCMS 的下游 fork 自訂功能**。

| 功能區 | 內容 | 移植方式 |
|---|---|---|
| **Excel 匯入** | 6 種 importer（products/packages/properties/documents/product-groups/cross-references）+ MiniExcel + 檔內去重 guard + 批次交易 + 多語 upsert + 關聯 by slug/part-number 解析 | 移植為 fork 的自訂 controller/service；repository 從「Directus 實體 schema」**重指向 StruoCMS 實體** |
| **參數化搜尋** | `ProductParametricQueryBuilder`（EAV facet/range 查詢、facet 計數、breadcrumb） | 移植；基於 StruoCMS `ProductProperty` collection 或原生 SqlSugar（GAP-3/6） |
| **Meilisearch 同步** | 3 索引（global_search/products_catalog/cross_references）、優先分數、動態 catalog 屬性、targeted 重同步 | 保留 Meilisearch；同步來源改讀 StruoCMS 資料 |
| **cross-reference** | 競品↔本廠零件對照、spec 衝突偵測、get-or-create 競態處理 | 移植（`CompetitorPart` collection + 自訂邏輯） |
| **文件下載** | published-only、presigned S3、檔名淨化/Content-Disposition | 移植為 fork 端點（GAP-11） |

**移植核心工作**：商業邏輯（importer、query builder、mapper、duplicate guard）本身**可攜**；**每個 repository 需重寫**對接 StruoCMS 資料存取層（原本直接吃 Directus 表）。

---

## 11. 外部依賴處置

| 依賴 | 現況 | 遷移後 |
|---|---|---|
| **Meilisearch** | 3 索引，被前端目錄/搜尋用（vue-instantsearch），由 .NET 後端餵資料 | **保留**；同步服務改讀 StruoCMS。**不遷移則目錄/搜尋壞** |
| **聯絡表單** | 前端 `POST /api/contact` → .NET `POST /api/contact`（**現有後端未見 SMTP，需確認 email 實際落點**） | 移植為 fork 端點；確認寄送機制（SMTP/第三方） |
| **檔案下載** | 前端 `/download/<id>/<filename>` → .NET 302 presigned | 移植（GAP-11） |
| **MinIO** | bucket `web-directus-bucket` | 續用同一 bucket，免搬 blob |
| **Microsoft OIDC** | Directus 後台登入 | StruoCMS OIDC 設定對接 |

**已知 dead code（可忽略）**：前端 `backend-parametric.get.ts` 代理與 `/backend/api/**` route **無 consumer**（目錄實際走 Meilisearch）。

---

## 12. ETL / 資料搬遷策略

1. **只讀來源**：對 `web-directus-db` **僅 SELECT**（嚴禁改動來源）。
2. **保留 uuid**：主要實體 id 原樣寫入 StruoCMS（products/categories/documents/posts/properties/packages/groups/competitor_part/hero_swiper/site_settings/social_medias/directus_files）→ FK 與前端引用不失效。
3. **重生 integer key**：translations / products_properties / contact_datas 等 integer PK 重新產生（只被 FK 參照，不外露），並維持 FK 對映。
4. **載入順序（遵守 FK）**：languages → directus_files → properties/product_statuses/document_types/post_types → categories(先無 upper_id 再回填自我關聯) → product_packages/product_groups → documents → products → 各 translations → 各 junction/EAV(products_properties, *_documents, posts_*, competitor_part_products) → site_settings/hero_swiper/contact_datas/social_medias。
5. **值轉換**：cast-csv 多值（`show_in`）CSV→JSON 陣列（GAP-9）；status 值對映各自型別；稽核欄位 `user_created/date_created/...` → StruoCMS AuditableEntity。
6. **blob**：MinIO bucket 續用，不搬。
7. **驗證**：逐表列數比對、抽樣欄位比對、關聯完整性（無孤兒 FK）、翻譯覆蓋率。

**建議工具**：GAP-10 的 scaffolding 產實體骨架；ETL 以一次性 .NET console（同 SqlSugar，來源接 Directus 表、目標接 StruoCMS 實體）。

---

## 13. 切換（cutover）與驗證清單

**打樣（不動現行系統）：**
- [ ] StruoCMS fork 建 6 張高風險表（products/categories/properties/products_properties/documents/translations）+ uuid-preserving ETL 到獨立 DB。
- [ ] 驗證 **EAV 參數化查詢**（多屬性 AND + range + facet 計數）可等價表達（GAP-3/4/5/6）。
- [ ] 驗證 **categories.upper_id 遞迴 5 層**（GAP-2）。
- [ ] 改寫 1～2 條 BFF 路由（news 列表 + product/[part_number]）指向 StruoCMS，確認 client 零改動可渲染。

**全量遷移驗收：**
- [ ] 35 collection 全建模且通過 metadata 掃描（GAP-12 翻譯欄位核對過）。
- [ ] 14 條 BFF 路由全改寫並回傳等價資料。
- [ ] 圖片轉換方案落地（GAP-1）。
- [ ] .NET 後端 5 功能區移植（匯入/參數化/Meilisearch/cross-ref/下載）。
- [ ] Meilisearch 同步改讀 StruoCMS，目錄/搜尋正常。
- [ ] 聯絡表單 email 落點確認並運作。
- [ ] RBAC：匿名唯讀對齊 public policy；後台 OIDC 登入可用。
- [ ] 密鑰全數輪替，未進 repo。
- [ ] 資料驗證（列數/抽樣/FK/翻譯覆蓋）全過。

---

## 14. 風險與待決策

| 項目 | 決策 | 建議 |
|---|---|---|
| 圖片即時轉換 | 前端 CDN vs 核心轉換 vs 預生成 | 先用前端 Cloudflare CDN 模式（最省），中期評估核心轉換 |
| faceted 搜尋歸屬 | ported service+Meilisearch vs 核心 facet | **ported service + Meilisearch**（YAGNI，不進核心） |
| BFF 改寫 vs 相容層 | 改前端 server vs StruoCMS 加 Directus 相容 | **改 BFF**（範圍可控） |
| 聯絡表單 email | 落點未明 | 遷移前確認現行寄送機制 |
| schema 動態建模 | StruoCMS code-first，無 UI 建表 | 本專案 schema 穩定、開發者擁有，接受此限制 |

---

## 15. 附錄：連線與環境事實（機密請勿外流 / 遷移後輪替）

- **來源 DB**：Postgres 容器 `postgresql-db-1`（`postgres:13.2-alpine`，host `:5432`），DB `web-directus-db`，user `postgres`。**僅唯讀**。
- **Directus**：容器 `web-directus-directus-1`（directus/directus:11），compose 於 `D:\docker\web-directus`，env `directus.env`（含明文 DB/MS/MinIO 密鑰 → 遷移時輪替）。
- **MinIO**：endpoint `:9000`，bucket `web-directus-bucket`，path-style。
- **語言**：en-US（default）/ zh-TW / zh-CN。
- **前端**：Nuxt 4，`DIRECTUS_URL`(:8055)、`BACKEND_API_URL`(:5208)、`MEILISEARCH_URL`(:7700)、`S3_PUBLIC_URL`（dev = Directus /assets）。
- **.NET 後端**：`viitorsemi-web-backend-re`（.NET 10, SqlSugarCore 5.1.4.214, MeiliSearch 0.18.0, AWSSDK.S3, MiniExcel），直連同一 Postgres。
- **StruoCMS 目標**：`src/Struo.*` 核心 + 下游 fork（列入 `Struo:ContentAssemblies`）；prod schema 走 `db/migrations/NNN-*.sql`（Postgres），dev 用 InitTables。

> 相關記憶：後端 live verify port `:5221`；DB 功能需 live Postgres 驗證（SQLite 綠燈 ≠ PG 正確）。
