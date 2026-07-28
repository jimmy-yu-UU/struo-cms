# StruoCMS Directus-Parity 核心強化 — 設計 Spec

> 建立日期：2026-07-28
> 狀態：設計核准，待寫實作計畫
> 背景文件：[`docs/directus-migration/01-struocms-gap-analysis.md`](../../directus-migration/01-struocms-gap-analysis.md)、[`02-migration-guide.md`](../../directus-migration/02-migration-guide.md)
> 目標讀者：StruoCMS 核心維護者

---

## 1. 目的與範圍

### 目的
在「把 ViitorSemi 的 Directus 11 體系遷移到 StruoCMS」**之前**，先補強 StruoCMS 核心（`src/Struo.*`），使其具備承接該專案所需的查詢/媒體能力，並以真實資料驗證設計正確。

### 本輪範圍（In Scope）
- **P0** 驗證 Spike（用完即丟）— 以真實 Directus 子集驗證查詢需求
- **P1** 關聯查詢深度可配置（GAP-2）
- **P2** 圖片即時轉換端點（GAP-1）— 採 **NetVips / libvips**
- **P3** Aggregate / Count 查詢（GAP-4）
- **P4** 查詢 DSL 擴充（GAP-5）— **條件性**，範圍由 P0 決定

### 非範圍（Out of Scope，明確排除）
- **Fork / 遷移工作**：collection 建模、ETL、`.NET` 匯入後端移植、前端 BFF 改寫 → 屬後續「遷移計畫」。
- **Faceted / 參數化搜尋核心化**（GAP-6）→ 已決策留在下游 fork + Meilisearch；核心只提供通用 count/aggregate。
- **體驗類欄位/版面**（GAP-7 icon、GAP-8 group-tabs/notice）→ 延到下一輪優化。
- **UI 動態建 collection/欄位** → 與 code-first 模板定位牴觸，永久排除。
- **欄位級/列級 RBAC** → Directus 內容表未使用，collection 級已足夠。

### 已定決策（本輪據以設計）
| 決策 | 選定 | 理由 |
|---|---|---|
| 本輪範圍 | 核心框架 + 驗證 spike | 先完善框架，spike 避免閉門造車 |
| 圖片轉換（GAP-1） | 核心端點，引擎用 **NetVips (libvips)** | 效能/記憶體最佳、最貼近 Directus `sharp`；授權對 fork 零負擔（見 §7） |
| Faceted 搜尋（GAP-6） | Fork + Meilisearch 擁有；核心只做 count/aggregate | YAGNI，核心不含半導體目錄專用邏輯 |
| 工作結構 | Spike 先行；GAP-1/2 獨立軌並行 | 少做白工、起步不慢 |
| StruoCMS 授權 | 維持 **MIT** 不變 | NetVips=MIT、libvips=LGPL-2.1 動態連結不影響（見 §7） |

---

## 2. 全域原則

- **TDD**：每個 phase 先寫失敗測試 → 實作 → 通過 → 重構。
- **Live PG 驗證閘**：DB 相關功能必經 live Postgres 驗證（SQLite 綠 ≠ PG 正確）；backend live verify port `:5221`。
- **設定走 Options + appsettings**：所有可調參數用 Options pattern + appsettings（`Section__Key` env override），安全預設；prod Docker→K8s 免重編即可調。
- **核心零污染**：不引入 `samples/*` 或商業模型依賴；`FrameworkEntityTypes` 不因本輪新增內容表。
- **NuGet 版本**：一律 `dotnet add package` 取得最新，禁止手寫版本號；集中於 `Directory.Packages.props`。

---

## 3. Phase 設計

### P0 — 驗證 Spike（用完即丟）

**目標**：在動 P3/P4 前，用真實資料釘死查詢需求；驗證 P1/P2 假設。

**隔離與安全**
- 來源 `web-directus-db`（容器 `postgresql-db-1`）**僅 SELECT**，嚴禁寫入。
- 目標為**獨立 scratch DB**（全新 Postgres DB/schema）。
- 臨時 content 專案置於 `spike/`（不納入主 solution 建置產物；結束後 park 或刪除）。

**建模（最小集合，uuid 保留）**
- `Category`（uuid PK、自我 M2O `UpperId`、`CategoryTranslation`）
- `Property`（uuid、`Code`、`Type`、`ShowIn` 多值）
- `Product`（uuid、`PartNumber`、M2O category/…、`ProductTranslation`）
- `ProductProperty`（EAV：M2O product + M2O property + `Value` string）

**ETL**：一次性 .NET console，SqlSugar 讀 Directus 表 → 寫 scratch StruoCMS 實體，保留主要實體 uuid。

**探針查詢（記錄實際可行的 REST `deep` / GraphQL 形狀與缺口）**
1. 分類麵包屑 `UpperId` **遞迴 5 層**：能否表達？有無 N+1？→ 定 P1 深度值。
2. EAV 參數化：「products 同時滿足多個 `(propertyCode, value/range)` 條件」→ 現有 DSL（跨關聯 filter + 單層布林）能否表達？缺什麼？→ **定 P4 是否需要、需要什麼**。
3. 列表計數 + 巢狀 to-many 是否需 `total`/`hasMore` → 定 P3 具體形狀。
4. 各 translation 欄位型別是否全數通過 metadata 掃描（GAP-12 收尾）。

**產出**：`docs/directus-migration/spike-findings.md` — 釘死 P3/P4 範圍與 P1 深度值的結論文件。

**驗收**：findings 文件完成且回答上述 4 問；scratch DB 與臨時碼清除或標記 park。

---

### P1 — 關聯查詢深度可配置（GAP-2，獨立軌）

**現況**：`src/Struo.Application/Configuration/StruoQueryOptions.cs` 的 `MaxRelationDepth` 預設 5，硬編碼；前端麵包屑遞迴剛好 5 層貼上限。

**變更**
- `MaxRelationDepth` 可經 `Struo:Query:MaxRelationDepth` 配置；**預設提高到 6**（容納 5 層再留 1 層餘裕）。
- 保留超限回 400（`QueryValidator`）。
- 驗證 `RelationExpander` 對**自我關聯遞迴**批次展開正確、維持 N+1-safe。

**測試**
- 深度邊界：depth = N 通過、N+1 回 400。
- 自我關聯遞迴（`UpperId`）多層正確回傳。
- N+1 斷言：遞迴展開的 SQL 查詢次數與關聯節點數成線性、非指數。

**驗收**：live PG 上 6 層 `UpperId` 遞迴正確且查詢次數受控；`MaxRelationDepth` 可由 appsettings 覆寫。

---

### P2 — 圖片即時轉換端點（GAP-1，獨立軌）

**現況**：`src/Struo.Api/Controllers/FilesController.cs` 的 `GET /api/files/{id}/content` 只回原檔 bytes 或 302 presigned；無轉換。前端依賴 Directus `/assets/<id>?width=&fit=&format=&quality=`。

**變更**
- 擴充 `GET /api/files/{id}/content`，接受 query：`width`、`height`、`format`（webp/jpeg/png/avif…）、`fit`（cover/contain/inside…）、`quality`。
- 新增 `IImageTransformer` / `NetVipsImageTransformer`（引擎 **NetVips**，套件 `NetVips` + `NetVips.Native.*` 由 `dotnet add package` 取得最新）。
- **快取層**：轉換結果依 `(fileId, 參數正規化, 檔案版本/modified)` 快取（`IImageVariantCache`，backend 可為本地磁碟或物件儲存，走 Options）；檔案更新 / 軟刪 / purge 時失效。
- Options `Struo:Files:ImageTransform`：`Enabled`、`MaxWidth`、`MaxHeight`、`AllowedFormats`、`DefaultQuality`、`Cache{ Backend, Path/Prefix, MaxEntries/TTL }`。

**安全 / 韌性**
- 參數 **clamp** 到 `MaxWidth`/`MaxHeight`（防 resize bomb）；`format` allowlist；僅對 image content-type 生效，非圖片 → 直通回原檔。
- 快取 key 正規化，防路徑穿越。
- 授權沿用既有政策（已發布匿名可讀；draft/trashed 需 `CanRead("file")`，維持 404 不洩存在性）。
- 轉換失敗 → 記錄並安全回退（回原檔或 500 + envelope error），不得讓例外外洩內部細節。

**測試**
- 尺寸/格式轉換正確（斷言輸出寬高與 MIME）。
- 快取命中（第二次不重算，可用 log/計數驗證）。
- 參數 clamp（超過 Max 被夾住）、非法 format 400、非圖片直通。
- 授權 parity（與原 content 端點一致：published 匿名、draft 404）。
- Live 驗證（含實際 MinIO 檔案）。

**驗收**：前端不改 client 程式碼即可經 `?width=&format=…` 取得轉換圖且有快取；dev/prod 皆可用、不依賴外部 CDN。

---

### P3 — Aggregate / Count（GAP-4，spike 驅動）

**現況**：頂層列表回 `PagedResult{data,total,limit,offset}`；**巢狀 to-many list 無 total/hasMore**；GraphQL 無 aggregate 語法。前端用 Directus `*_aggregated{count}` / `*_func{count}`。

**變更（依 P0 findings 收斂）**
- **巢狀 to-many list 補 `total` /（必要時）`hasMore`** 於 REST `deep` 回應與 GraphQL list 欄位。
- 確保頂層 `total` 足以取代 Directus `_aggregated`（BFF 改寫時對接）。
- 視 P0：必要時提供 **count-only** 查詢選項（省投影，只回計數）。

**測試**
- 巢狀列表 `total` 在有/無 filter 下正確。
- count-only（若做）正確且不投影資料列。
- REST 與 GraphQL 兩路一致。

**驗收**：BFF 可在**不使用** Directus 專屬 `_aggregated`/`_func` 語法下，取得列表總數與必要的關聯計數。

---

### P4 — 查詢 DSL 擴充（GAP-5，條件性，spike 驅動）

**現況**：查詢 DSL 僅單層 `_and`/`_or`（巢狀布林被 `QueryValidator` 拒）；不支援跨 to-many 排序。

**變更（範圍由 P0 決定，可能縮小或跳過）**
- 候選：巢狀布林群組（`_and`/`_or` 巢狀），或 EAV 多條件跨 to-many 的 AND 語意支援。
- **若 P0 證明現有 DSL 已足以表達 ViitorSemi 全部查詢 → 本 phase 縮小為文件記錄或跳過**，不為未來假想需求擴充（YAGNI）。
- 任何擴充維持既有安全防護：欄位 allowlist、`MaxFilterConditions`、hidden 欄位排除、DSL 不洩 ORM 內部。

**測試**：針對實際加入的能力補單元 + live PG 測試；退化測試確保既有查詢不受影響。

**驗收**：ViitorSemi 遷移所需的全部 filter 組合可由 StruoCMS 查詢表達，且未過度工程化。

---

## 4. 元件與邊界

| 元件 | 職責 | 依賴 | 檔案（預期） |
|---|---|---|---|
| `StruoQueryOptions` | 查詢參數設定（含 MaxRelationDepth） | — | `src/Struo.Application/Configuration/` |
| `RelationExpander`（驗證） | 批次關聯展開、N+1-safe、遞迴 | Query engine | `src/Struo.Application/Query/…` |
| `IImageTransformer` / `NetVipsImageTransformer` | 影像 decode→resize→encode | NetVips | `src/Struo.Infrastructure/Files/` |
| `IImageVariantCache` | 轉換結果快取/失效 | FileStorage/local | `src/Struo.Infrastructure/Files/` |
| `FilesController`（擴充） | content 端點 + transform 參數 | 上述 | `src/Struo.Api/Controllers/` |
| Aggregate/count（P3） | 巢狀 total、count-only | Query engine、GraphQL builder | `src/Struo.Application/Query/…`、`src/Struo.Api/GraphQl/…` |

每個單元：單一職責、介面清晰、可獨立測試。

---

## 5. 資料流與錯誤處理

- **圖片請求**：`GET /api/files/{id}/content?width=…` → 授權檢查 → 參數 clamp/驗證 → 查快取（命中即回）→ 未命中則 NetVips 轉換 → 寫快取 → 回應；失敗 → log + 安全回退。
- **深度超限**：查詢解析階段擋下 → 400 + envelope error。
- **參數錯誤**：400 + envelope；不洩內部細節。
- **錯誤原則**：邊界驗證、明確訊息、server 端記錄完整 context、絕不靜默吞錯。

---

## 6. 測試策略

- **單元**：查詢邏輯、參數 clamp、快取 key 正規化（SQLite/純邏輯可）。
- **整合**：圖片轉換（真實 bytes、MinIO）、REST + GraphQL 契約。
- **Live PG 閘**：P1/P3/P4 的 DB 行為在 live Postgres 驗證後才算完成。
- **退化**：既有查詢/端點行為不變。
- 目標涵蓋率沿用專案標準。

---

## 7. NetVips / LGPL-2.1 合規處理

**結論**：採 NetVips 不影響 StruoCMS 維持 **MIT**，且在自架 SaaS 用法下義務近乎於零。

- **授權組成**：NetVips wrapper = MIT；libvips native = LGPL-2.1（經 NuGet native asset **動態連結**）。
- **不觸發開源你的碼**：LGPL 弱 copyleft 只作用於 libvips 本身；動態連結的呼叫方非衍生作品 → StruoCMS 可維持 MIT/專有。LGPL-2.1 **無 SaaS/網路條款**（非 AGPL），自架網站不算散布。
- **要做的（輕量，且僅「散布二進位給第三方」時才需完整履行）**：
  1. 建立 `THIRD-PARTY-NOTICES`：列 libvips 著作權聲明 + LGPL-2.1 全文 + 上游源碼與版本連結。
  2. 維持**動態連結**、不鎖死使其可被替換（NetVips 預設即滿足）。
  3. 不修改 libvips 源碼（只用預編譯 binary）。
  4. docs 加一句部署/授權說明。
- **模板層**：repo 本身不散布 libvips binary（各 fork 由 NuGet 還原）；fork 若交付二進位自行補 notice。傳染性為零，fork 可自由選 MIT/專有。
- **免責**：非律師意見；若未來對外交付 image/on-prem，建議法務覆核散布情境。

---

## 8. 依賴與風險

| 項目 | 風險 | 緩解 |
|---|---|---|
| NetVips native 相依 | 容器需正確 native asset | 用官方 `NetVips.Native.*`；Docker 驗證 Linux 載入 |
| P4 範圍不確定 | 過度或不足工程 | 由 P0 findings 收斂；YAGNI |
| 圖片快取失效 | 檔案更新後回舊圖 | 快取 key 綁檔案版本/modified；更新/purge 失效 |
| resize bomb | 惡意大尺寸耗資源 | 參數 clamp + 格式 allowlist |
| Live PG 驗證環境 | 忘記跑 live | 明列為各 phase 驗收閘 |

---

## 9. 執行順序

```
P0 (spike) ──┬─→ P3 (aggregate/count)  ─→ P4 (DSL，條件性)
             └─(findings 釘死 P3/P4)
P1 (depth)   ── 獨立並行 ──
P2 (image)   ── 獨立並行 ──
```

- P1、P2 不依賴 spike，可與 P0 並行起步。
- P3 依 P0 findings；P4 依 P0 判定是否需要。
- 各 phase：brainstorm(本 spec 已含) → 寫計畫(TDD) → 執行 → 驗證(含 live PG)。

---

## 10. 整體驗收

- [ ] P0 findings 完成，釘死 P3/P4 範圍與 P1 深度；scratch 清除。
- [ ] P1 深度可配置、預設 6、遞迴正確無 N+1（live PG）。
- [ ] P2 前端零改 client 即可經參數取得轉換圖 + 快取；dev/prod 皆可、不依賴外部 CDN；授權 parity。
- [ ] P3 巢狀 total / count 可取代 Directus `_aggregated`（REST + GraphQL，live PG）。
- [ ] P4 ViitorSemi 所需 filter 全可表達，或明文記錄現有 DSL 已足。
- [ ] `THIRD-PARTY-NOTICES` 建立、StruoCMS 維持 MIT。
- [ ] 全程 TDD、DB 功能經 live Postgres 驗證。
