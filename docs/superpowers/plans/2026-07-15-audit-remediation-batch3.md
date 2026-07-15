# 2026-07-15 稽核修復 Batch 3 — 前端 MEDIUM(細部計畫)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development。母計畫:`2026-07-15-audit-remediation.md`(Global Constraints 全數適用)。審核報告:`docs/architecture-audit-2026-07-15.md`。

**範圍:** FE-2+FE-6(合一 task)→ FE-3 → FE-4 → FE-5,共 4 tasks + 批次 gate。純前端,後端零改動。
**基線:** 前端 **270** 綠(50 檔)/ 後端 732(本批不碰,不需重跑)。每 task 結束不得低於基線,且 `pnpm test` **與** `pnpm build`(vue-tsc 含測試檔)皆須綠。
**模型分工(user 2026-07-15 指示):** 實作 subagent = **opus**;審核/確認 subagent = **fable**。

## 本批共通事實(fable 確認 agent 2026-07-15 查核,post-Batch-2)

- `CollectionListView.vue`:`loadItems` :63–91,無條件 `rows.value = res.data`(:82)/`total.value`(:83);觸發源 = `onPage`(:93)、`onSort`(:99)、search debounce `searchTimer` :106–114(300ms,只 debounce 觸發不管回應)、`setMode`(:126)、`watch(name)`(:161)、`onMounted`(:170)、`runAction`(:136)。**全檔無 token/AbortController。**測試靠 `defineExpose`(:172–173)驅動。
- `components/fields/RelationPicker.vue`:`watch(search, loadOptions)` :95 **無 debounce**;`loadOptions` :41–55(`options.value=` :48);`@filter` 於 :127(MultiSelect)/:141(Select);`onMounted` :96–99 亦呼叫。`components/fields/FilesField.vue`:`watch(search, loadOptions)` :107 無 debounce;`loadOptions` :83–96(`options.value=` :92);另有 `resolve(ids)` :36–55(縮圖,不在範圍)。
- `src/lib/`:純函式 + **同目錄 `X.test.ts`**(無 `__tests__/` 目錄)。**無任何 latestWins/debounce helper 存在。**`validateItem.ts` 回 `Record<string,string>` keyed by field name。
- `ApiError` 在 **`src/api/apiClient.ts`**(非 lib):`class ApiError extends Error { status:number; code?:string; details?: ValidationDetail[] }`(:11–22);`ValidationDetail = { field:string; message:string }`(:1)。
- 伺服器端:`details` **只有** ASP.NET model-binding 驗證會產(`Program.cs:38–50`,code=`VALIDATION`,400);CMS item 驗證(`ItemService.cs:979,988` required/maxLength)丟 `QueryException` **message-only** → `BAD_USER_INPUT`。409 = `ConcurrencyConflictException`(`SqlSugarItemRepository.cs:360` version CAS)→ code=`CONFLICT`。
- **FE-4 前置 bug(fact-check 新發現):`ItemFormView.setModel`(:46–50)只 copy shared/translations/relations,丟棄 `next.version`** — 雖然 `parseItemToForm.ts:35–36` 有抽 version、`buildItemPayload.ts:57` 有 echo `model.version`,但 view 層斷鏈 → UI 更新從未帶 version = 樂觀鎖在 UI 端實際失效(last-write-wins),409 打不出來。FE-4 必須先修此鏈才有 409 可恢復。
- `ItemFormView.vue`:state :39–44(`model` reactive、`errors`、`serverError`、`notFound`);`init` :52–74(NOT_FOUND :69);`onSubmit` :76–92(**catch :87–88 全塌成 `serverError=e.message`**);`onDelete` :94–108(`confirm.require` + `deleteConfirm`)。`ItemForm.vue`:props :15–24(`errors: Record<string,string>`),shared 欄位錯誤 :47、translatable 只在 default locale :76,`watch(props.errors)` :32–34 會翻到 default locale tab。
- Router:`src/router/index.ts` 只有全域 `authGuard`(:29–32);vue-router **5.1.0**,`onBeforeRouteLeave` composable 可用。全 repo 無 `beforeunload`。
- 測試慣例:colocated `*.test.ts`;`vi.mock('vue-router', …)`、api 模組以相對路徑 `vi.mock`;PrimeVue 元件 stub(`global.stubs` 或 `vi.mock('primevue/x')`);`useConfirm` mock 成 `confirmRequire` spy;以 exposed members 驅動(`w.vm.init()`)。vitest jsdom,`vite.config.ts:13–20`。
- E2E:`frontend/e2e/*.spec.ts`,baseURL `:5173`,backend 手動跑 `:5080`(Vite proxy);per-spec `login(page)` helper;creds env `E2E_EMAIL`/`E2E_PASSWORD`;PrimeVue ConfirmDialog 按預設 "Yes"。
- Confirm 慣例:`useConfirm().require({header, message, accept})`,文案出自純 lib helper(`deleteAction.ts:7–15`)。無 i18n,UI 字串一律英文 literal。

---

## Task 1: FE-2 + FE-6 — latest-wins helper + picker debounce

**Files:**
- Create: `frontend/src/lib/latestWins.ts` + `frontend/src/lib/latestWins.test.ts`
- Create: `frontend/src/lib/debounce.ts` + `frontend/src/lib/debounce.test.ts`
- Modify: `frontend/src/views/CollectionListView.vue`(loadItems 套 latest-wins)
- Modify: `frontend/src/components/fields/RelationPicker.vue`、`frontend/src/components/fields/FilesField.vue`(search watch 套 300ms debounce + loadOptions 套 latest-wins)
- Modify: 對應既有測試檔(`CollectionListView.test.ts`、`RelationPicker.test.ts`、`FilesField.test.ts`)

**Interfaces(Produces):**
- `latestWins.ts`:`export function createLatestWins(): { next(): number; isCurrent(token: number): boolean }` — 純遞增 token 工廠;呼叫端模式:進 loader 先 `const t = lw.next()`,await 後每次寫 state 前 `if (!lw.isCurrent(t)) return`。**不用 AbortController**(YAGNI:audit 說「理想上 abort」,token 已消除狀態污染;abort 屬最佳化,不做)。
- `debounce.ts`:`export function debounce<A extends unknown[]>(fn: (...args: A) => void, ms: number): ((...args: A) => void) & { cancel(): void }` — 回傳含 `cancel()` 的 debounced 函式(unmount 清理用,呼應 FE-7 但只在**新增**的 debounce 上做,不動 CollectionListView 既有 searchTimer — FE-7 是 Batch 4 範圍)。
- `CollectionListView.loadItems`:整段 async 體套 token guard(rows/total/error/loading 的寫入全數 guard;`finally` 的 `loading=false` 亦只在 isCurrent 時執行,避免舊回應關掉新請求的 spinner)。既有 search debounce 機制**不動**。
- `RelationPicker`/`FilesField`:`watch(search, debouncedLoad)`(300ms);`onMounted`/`openDialog` 的直接 `loadOptions()` 呼叫不 debounce(首載不該等 300ms);`loadOptions` 內套 latest-wins(`options.value`/`loading`/`loadError` 寫入 guard)。`onBeforeUnmount` 呼叫 `debouncedLoad.cancel()`。

**Steps:**
- [ ] Step 1(RED): `latestWins.test.ts`/`debounce.test.ts` 純函式測試(token 遞增/isCurrent 語意;debounce 合併呼叫/cancel — vi.useFakeTimers);元件測試:mock api 回傳兩個可控 promise,先發請求 A 後發 B,讓 B 先 resolve、A 後 resolve,斷言最終 state = B 的資料(現況 A 覆蓋 → FAIL);RelationPicker/FilesField:fake timers 下連打三次 search 只發一次請求
- [ ] Step 2: 實作 helpers + 三元件接線
- [ ] Step 3: `pnpm test` + `pnpm build` 全綠(≥270 + 新增)
- [ ] Step 4: Commit `fix(frontend): latest-wins guard for list/picker fetches + debounced picker search (FE-2, FE-6)`

## Task 2: FE-3 — 伺服器端驗證 `error.details` 映回欄位錯誤

**Files:**
- Create: `frontend/src/lib/applyServerErrors.ts` + `frontend/src/lib/applyServerErrors.test.ts`
- Modify: `frontend/src/views/ItemFormView.vue`(`onSubmit` catch 接線)
- Modify: `frontend/src/views/ItemFormView.test.ts`

**Interfaces(Produces):**
- `applyServerErrors.ts`:`export function splitServerErrors(details: ValidationDetail[] | undefined, knownFields: ReadonlySet<string>): { fieldErrors: Record<string, string>; leftover: string[] }` — 純函式:`d.field` 命中 knownFields(**case-insensitive** 比對,回寫時用 meta 的正名 — ModelState key 大小寫不保證)者進 `fieldErrors`,其餘進 `leftover`。同欄多筆取第一筆(與 `validateItem` 一欄一訊息慣例一致)。
- `ItemFormView.onSubmit` catch:`e instanceof ApiError && e.details?.length` 時 → `splitServerErrors` 併入 `errors`(覆蓋式 assign,不 mutate 舊物件 — 觸發 `ItemForm` 的 `watch(props.errors)` 翻 tab),`leftover` 與無 details 情況退 `serverError` banner(leftover join;皆空則用 `e.message`)。NOT_FOUND/其餘分支不變。knownFields 來源 = `meta.value.fields` 名稱集合。
- **不動後端**:CMS 驗證(BAD_USER_INPUT message-only)本批維持 banner — 使 details 結構化是後端議題,不在 FE-3 範圍(audit 原文即如此界定)。

**Steps:**
- [ ] Step 1(RED): 純函式測試(命中/大小寫/未知欄位/空 details);view 測試:mock update 丟 `new ApiError(400,{code:'VALIDATION',message:'…',details:[{field:'title',message:'dup'}]})` → 斷言 `errors.title==='dup'` 且 serverError 空;混合(1 命中 + 1 未知)→ 欄位 + banner 各得其所(現況全進 banner → FAIL)
- [ ] Step 2: 實作 + 接線
- [ ] Step 3: `pnpm test` + `pnpm build` 全綠
- [ ] Step 4: Commit `fix(frontend): map server validation error.details onto per-field form errors (FE-3)`

## Task 3: FE-4 — version 斷鏈修復 + 409 樂觀鎖恢復路徑

**Files:**
- Modify: `frontend/src/views/ItemFormView.vue`(`setModel` 帶 version;409 分支;conflict 警示 + Reload latest)
- Modify: `frontend/src/views/ItemFormView.test.ts`

**Interfaces(Produces):**
- **前置修鏈:** `setModel` 增 `model.version = next.version`(edit 載入後 update payload 自動 echo — `buildItemPayload.ts:57` 既有,零改動)。create 模式 version 維持 undefined。
- **409 分支(`onSubmit` catch,判 `e instanceof ApiError && e.status === 409 && e.code === 'CONFLICT'`):**
  1. 重抓 `itemsApi.get(name, id)` → **只更新 `model.version` = 最新**(不覆蓋使用者已編輯內容);
  2. 設 conflict 警示 state(`conflict = ref(false)` → true):banner 文案 `This item was changed by someone else. Review your edits and save again to overwrite, or reload the latest version.`;
  3. 提供 **Reload latest** 動作(banner 上的按鈕):`setModel(parseItemToForm(重抓結果, meta))` 全量覆蓋 + 清 conflict/errors/serverError。
  4. 重抓本身失敗(如已被刪 → NOT_FOUND)→ 退 `notFound`/`serverError` 既有分支。
- conflict banner 為**獨立於 serverError 的 UI 塊**(帶動作按鈕;`ItemForm.vue` 的 serverError prop 是純文字)— 實作於 `ItemFormView.vue` template 層,或經新 optional prop 傳入 `ItemForm`;擇一,以最小 diff 為準。成功 save / Reload latest 後清 conflict。
- 409 分支**不**進 FE-3 的 details 映射(CONFLICT 無 details)。

**Steps:**
- [ ] Step 1(RED): (a) version 斷鏈:mock get 回 `version:3`,`init()` 後斷言 update 被呼叫時 payload 含 `version:3`(現況 undefined → FAIL);(b) 409:mock update 丟 `ApiError(409,{code:'CONFLICT',…})`、mock get 回 `version:7` → 斷言 `model.version===7`、conflict=true、使用者輸入未被覆蓋;再送一次成功(version 7 echo);(c) Reload latest → model 全量換成遠端值、conflict=false;(d) 409 後重抓 404 → notFound 分支
- [ ] Step 2: 實作
- [ ] Step 3: `pnpm test` + `pnpm build` 全綠
- [ ] Step 4: Commit `fix(frontend): carry item version through form + 409 conflict recovery with reload-latest (FE-4)`

## Task 4: FE-5 — dirty-state 離開守衛

**Files:**
- Create: `frontend/src/lib/formDirty.ts` + `frontend/src/lib/formDirty.test.ts`
- Modify: `frontend/src/views/ItemFormView.vue`(baseline 快照 + `onBeforeRouteLeave` + `beforeunload`)
- Modify: `frontend/src/views/ItemFormView.test.ts`

**Interfaces(Produces):**
- `formDirty.ts`:`export function snapshotModel(model: FormModel): string`(deterministic `JSON.stringify`;FormModel 由 parse/setModel 建構、鍵序穩定,直接 stringify 即可 — 不引深比較依賴)與 `export function isDirty(baseline: string, model: FormModel): boolean`。
- `ItemFormView`:
  - baseline 時機:`init` 完成 `setModel` 後、create 模式 `buildEmptyModel` 後、**成功 submit 後(navigate 前)**、Reload latest 後、delete accept 後 — 重拍 baseline(=不 dirty)。
  - `onBeforeRouteLeave`:dirty 時以 `confirm.require` 問(`header: 'Unsaved changes'`,message 出自 `formDirty.ts` 純 helper `unsavedConfirm()` — 比照 `deleteAction.ts` 文案慣例;accept → 放行,reject → 留下)— 實作為回傳 `Promise<boolean>` 的 async guard(accept/reject callback resolve)。
  - `beforeunload`:`onMounted` 掛 / `onBeforeUnmount` 卸;dirty 時 `e.preventDefault()`(瀏覽器原生對話框,無自訂文案)。
  - Cancel 按鈕走 `router.push` → 自然被 route guard 攔,不另做。
- 409 conflict state 不影響 dirty 判定(model 未變則不 dirty;通常 conflict 時本來就 dirty)。

**Steps:**
- [ ] Step 1(RED): 純函式測試(snapshot/isDirty:改 shared、改 translations 深層、改 relations 陣列、未改 → false);view 測試:mock `onBeforeRouteLeave`(vi.mock vue-router 增 export)捕捉 guard → dirty 時 guard 呼叫 confirmRequire、accept resolve true / reject resolve false;clean 時不問直接 true;成功 submit 後 guard 不問;`beforeunload` 掛卸(spy `addEventListener`)+ dirty 時 preventDefault 被呼叫
- [ ] Step 2: 實作
- [ ] Step 3: `pnpm test` + `pnpm build` 全綠
- [ ] Step 4: Commit `feat(frontend): dirty-state guard — confirm before route leave and browser unload (FE-5)`

---

## Batch 3 Gate(全批完成後)

- [ ] `pnpm test` + `pnpm build` 全綠(≥270 + 新增);後端未碰(git diff 確認 `src/`/`tests/` 零變更)
- [ ] **Live gate(真 PG backend `:5080` + `pnpm dev --host 127.0.0.1`,Playwright):**
  1. FE-4 409 全流程:兩分頁(或 API 併行改)同一 article → 後存者收 conflict banner → Reload latest 或直接再存 → 成功;psql/API 斷言 version 單調遞增
  2. FE-5 離開守衛:編輯後點導覽 → confirm 出現;reject 留原頁;accept 離開;存檔後離開不問
  3. FE-2/6 抽查:快速切 collection / picker 連打搜尋 → 清單/選項最終正確(以 network log 佐證 debounce 生效)
  4. FE-3 抽查:users API 重複 email(伺服器 409 CONFLICT)走 banner;model-binding VALIDATION details 情境若可觸發則驗欄位錯誤,不可觸發則以單元證據為準(details 僅 model-binding 產,items API 不經之 — 已知限制,記錄即可)
- [ ] E2E specs:新增/調整 `frontend/e2e/`(FE-4 conflict、FE-5 guard 至少各一 spec;既有 specs 全綠)
- [ ] 更新 `docs/architecture-audit-2026-07-15.md`:FE-2~6 標 ✅FIXED(附 commit)
- [ ] Merge to main
