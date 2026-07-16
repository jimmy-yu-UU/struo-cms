# 2026-07-16 Batch 3b hotfix — 空白 DateTime 400 + e2e TipTap 重寫(細部計畫)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development。母計畫 Global Constraints 全數適用(`2026-07-15-audit-remediation.md`)。來源:Batch 3 live gate 新發現 1 與 3(`docs/architecture-audit-2026-07-15.md` Batch 3 條目;完整 repro 於 `.superpowers/sdd/gate-e2e-report.md`)。

**範圍:** 2 tasks。純前端 + e2e,後端零改動。
**基線:** 前端 **309** 綠(54 檔)+ build 綠;後端 732 不碰。e2e 現況:10 綠 / 2 紅(items/relations,stale TipTap 假設)。
**模型分工:** 實作 = **opus**;審核 = **fable**。

## 已驗證事實(Batch 3 gate agent + API repro)

- 根因(前端):`fieldTypes/registry.ts:198-200` — `date`/`time`/`dateTime` 用 `def({component: DateField})` 預設 `empty=''` + identity serialize → 空值送 `""`;`buildItemPayload.ts` update 模式送**全部** shared 欄位 → `"PublishedAt": ""` 出線。
- 後端 `ItemService.cs:911-920` `Deserialize` 對 `DateTime?` 收 `""` 丟 `JsonException` → 400 "Request body could not be parsed."。**API 實證:`"PublishedAt": null` → 200 OK** — 後端不需要動,修前端序列化即可。
- create 路徑:`buildItemPayload` create 模式 skip empty → 未受影響;修 serialize 對 create 無行為變化。
- dirty-guard 互動:serialize 只影響 payload、不動 model/snapshot → FE-5 baseline 無影響。
- e2e 紅因:`items.spec.ts`/`relations.spec.ts` 以 `.locator('textarea')` 填 Body — Body 已是 TipTap contenteditable。`conflict.spec.ts`/`unsaved-guard.spec.ts` 有現成 TipTap-aware 寫法可援用;FE-4 的 conflict spec 因本 bug 以 category 驗證,修復後應換回 article(audit 原文即指定 article)。

## Task 1: date/time/dateTime 空值序列化 `""` → `null`

**Files:**
- Modify: `frontend/src/lib/fieldTypes/registry.ts`(`date`/`time`/`dateTime` 三個 def 加 `serialize`)
- Test: registry 既有測試檔(若無 serialize 案例則新增;另在 `buildItemPayload` 測試層加一條 update-mode 斷言)

**Interfaces(Produces):**
- 三個 datetime 系 field type 的 `serialize`:`v === '' || v == null` → `null`,其餘原樣(合法 ISO 字串不動)。`empty`/`parse`/`defaultValue` 不變(表單顯示語意不動,只修出線 payload)。
- 驗收斷言:`buildItemPayload(…, mode:'update')` 對空 dateTime 欄位產出 `field: null`(非 `""`);非空值原樣通過。

**Steps:**
- [ ] Step 1(RED): registry serialize 單元測試(`''`→null、`null`/`undefined`→null、ISO 字串原樣)+ buildItemPayload update-mode 空 dateTime → null(現況 `""` → FAIL)
- [ ] Step 2: 實作三處 serialize
- [ ] Step 3: `pnpm test` + `pnpm build` 全綠(≥309)
- [ ] Step 4: Commit `fix(frontend): serialize empty date/time/dateTime as null — unblock Article update with blank Published At`

## Task 2: e2e TipTap 重寫 + conflict spec 換回 article

**Files:**
- Modify: `frontend/e2e/items.spec.ts`、`frontend/e2e/relations.spec.ts`(TipTap contenteditable 填寫;沿用 conflict/unsaved-guard spec 的 locator 寫法;必要時抽共用 helper 至 `frontend/e2e/` 內)
- Modify: `frontend/e2e/conflict.spec.ts`(驅動 collection 由 category 換回 **article**,證明 Task 1 修復 + FE-4 於 audit 指定 collection 成立;Published At 留空)
- 不動 `frontend/src`、不動後端

**Steps:**
- [ ] Step 1: 重寫兩紅 spec(TipTap-aware);conflict.spec 換 article
- [ ] Step 2: live 全套 `npx playwright test` → **全綠(12/12)**;失敗即回報,不得為求綠弱化斷言
- [ ] Step 3: Commit `test(e2e): TipTap-aware items/relations specs + conflict spec on article (batch3b)`

## Batch 3b Gate

- [ ] `pnpm test` + `pnpm build` 全綠(≥309 + 新增);後端零 diff
- [ ] Live(真 PG `http://127.0.0.1:5080` Development + `pnpm dev --host 127.0.0.1`):
  1. UI 手動/腳本:Article「Published At」留空 → Save → 200(bug 修復實證)
  2. e2e 全套 12/12 綠(含 conflict on article)
- [ ] `docs/architecture-audit-2026-07-15.md` Batch 3 條目的發現 (a)/(c) 標已修(附 commit)
- [ ] Merge to main
