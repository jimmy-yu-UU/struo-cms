# StruoCMS 部署／維運注意事項

> 本檔集中記錄**部署與環境相關的注意事項**（configuration、拓撲相依行為、一次性遷移動作），方便未來寫正式部署文件與查閱。
> 來源：2026-07-21 審核 Batch 1/2 修復過程中揭露的維運相依項。新項目請往下追加並註明來源 commit／audit ID。
>
> 原則（使用者定調）：**所有部署／可調設定一律走 appsettings + 環境變數覆寫（`Section__Key`），不寫死在程式碼**，安全預設。

---

## 1. 登入限流（SEC-7）— 依部署拓撲設定

app 內建了一個**只針對 `POST /api/auth/login`** 的限流器（.NET `AddRateLimiter`，fixed-window，per-IP）。它保護「暴力破解」與「Argon2id CPU 放大」兩個面向。**不是**全站限流；一般流量/DDoS 由邊緣層負責。

### 設定鍵（appsettings，可用環境變數覆寫）
```jsonc
"RateLimiting": {
  "Login": {
    "Enabled": true,        // 預設 true（安全預設）
    "PermitLimit": 5,       // 每視窗允許次數
    "WindowSeconds": 60     // 視窗秒數
  }
}
```
環境變數覆寫範例：`RateLimiting__Login__Enabled=false`、`RateLimiting__Login__PermitLimit=10`。
**開關於啟動時讀取，改值需重啟 process/pod。**

### 各拓撲怎麼設

| 部署方式 | `Enabled` | per-IP 限流由誰做 | 備註 |
|----------|-----------|------------------|------|
| 直連 / 單機 / dev | `true`（預設） | app 層 | 開箱即用 |
| **反向代理（同機 nginx 等）** | `true`（若要用 app 層） | app 層 | ⚠️ 需設 `UseForwardedHeaders`，否則見下方陷阱 |
| **Docker → K8s 多-pod** | **`false`** | **ingress / edge / WAF** | 建議做法；見下方說明 |

### ⚠️ 陷阱一：反向代理後的「來源 IP」
限流器依 `Connection.RemoteIpAddress` 分區。在代理後面，這個值是**代理連進來的 IP**（同機 nginx = loopback `127.0.0.1`／`::1`；K8s = ingress pod IP／Pod CIDR），**不是**真實用戶 IP，也**不是**伺服器外網 IP。若不處理：
- 所有用戶塌縮成同一個桶 → 每分鐘 5 次就把**全站登入**鎖死（限流器反成 DoS）。

若要在**代理後仍用 app 層限流**，需在 `Program.cs` pipeline（`UseRateLimiter` 之前）加上 `UseForwardedHeaders`，並用 **`KnownProxies`（單機 = loopback）或 `KnownNetworks`（K8s = Pod CIDR，如 `10.244.0.0/16`）** 指定信任來源。之所以要信任名單：`X-Forwarded-For` 是可偽造的 header，middleware 只在「連線來自已知代理」時才採信它、還原真實 IP，否則攻擊者能偽造 IP 繞過限流。
> 目前程式**未**內建 `UseForwardedHeaders`（值取決於部署，屬部署時決定）。若採 K8s=`Enabled:false` 走邊緣，則**完全不需要**這套設定。

### ⚠️ 陷阱二：多-pod 下 app 層限流不是「全域」
內建限流器狀態在**記憶體、每個 pod 各算**。N 個 pod 在 LB 後 ≈ N × 限額，且忽高忽低。→ **多-pod 環境請把 per-IP 限流放到 ingress/edge**（邊緣天生看得到真實 client IP、位於所有 pod 之前，是唯一能真全域的層）。
- 若未來需要「app 層的精準全域限流」，可改 **Redis-backed 分散式 limiter**（本專案 Phase 6 已有 Redis）；本輪未實作。
- 提醒：連 ingress-nginx 的 `limit-rpm` 也是每個 controller pod 各算；真全域限流一律需要共享狀態（Redis/memcached）或託管邊緣（Cloudflare/WAF）。

### 429 回應格式
超限回標準 envelope：`{"success":false,"error":{"code":"TOO_MANY_REQUESTS","message":"..."}}`（camelCase）+ `Retry-After` header（秒）。

---

## 2. `/api/config` 快取（SEC-7）與 logo 生命週期（SEC-10）

- 匿名 `GET /api/config` 加了 `IMemoryCache`，**30 秒 TTL**。
- **品牌儲存**（`PUT /api/settings/branding`）成功後會**立即 evict** 該快取 → 品牌變更即時反映。
- **殘留（已接受）**：刪除 logo 檔案時**不會** evict 該快取（Infrastructure 依分層規則不可引用 Api 層的快取 key）。因此 logo 檔被刪後，匿名登入頁最多有 **≤30 秒**仍指向舊 URL 的破圖窗口（原本是「永遠」破圖，已大幅縮小）。`ConfigController` 產 logo URL 前會重新確認檔案存在且 published，否則回退 appsettings 的 `Branding:LogoUrl`。
- `IMemoryCache` 為單機/單 pod 快取；多-pod 下各 pod 各有一份（30s TTL 內可能不一致，對 bootstrap config 可接受）。

---

## 3. 資料庫 / Migrations（來源：Batch 1 DB-12/DB-13、backlog BL-5）

- **Reviewed `*.sql` migrations 在所有環境都會跑**（由 `Database:MigrationsPath` 設定驅動；prod 套用已審核腳本正是其用途）。非 PostgreSQL 後端為 no-op。
- **DB-12 一次性動作（既有 dev/prod 環境）**：`site_settings.updatedat` 的 entity attribute 已改 `timestamptz`，但 SqlSugar InitTables **不回溯 ALTER 既有欄位**。由舊 InitTables 建的既有表仍是 `timestamp without time zone`，需一次性：
  ```sql
  ALTER TABLE site_settings
    ALTER COLUMN updatedat TYPE timestamptz USING updatedat AT TIME ZONE 'UTC';
  ```
  對「全新建表」與 prod（migration 012 本就建 timestamptz）皆已正確，無需此動作。
- **DB-13**：identity unique 約束由 `013-identity-unique-constraints.sql` 建立（`CREATE UNIQUE INDEX IF NOT EXISTS`，冪等可重跑）。非 dev-provisioned 的 prod DB 必須跑過此 migration，否則 email 可重複、bearer token 查詢無索引。
- **BL-5（backlog，建議）**：dev 未設 `Database:MigrationsPath` → migration 腳本在 dev 從不排練，未來 ALTER 型腳本首跑就在 prod。建議 dev 設 `"db/migrations"`（InitTables 先跑、腳本冪等，安全）。
- **DB-14 決策**：DB **無 FK constraint**，完整性全靠 app-side purge pipeline + 交易。缺口清單見 `docs/audit-2026-07-21-remediation-tasklist.md` DB-14 條目。**若未來引入繞過 purge pipeline 的寫入路徑，須重新評估。**

---

## 4. 一般環境需求（dev 實測）

- PostgreSQL（runtime，唯一正式支援）；SQLite 僅測試用。
- MinIO / S3 相容儲存（檔案）。dev 下 MinIO presigned 若為 `https://localhost:9000` 而前端走 http，縮圖會失敗（僅縮圖降級，非功能性）。
- Redis（Phase 6：session ticket store；未來若做分散式限流亦用它）。
- Cookie-auth 的寫入請求需帶 header `X-Struo-CSRF`（存在即可，presence-only）。
