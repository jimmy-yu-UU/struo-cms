# 22. 測試與 CI

這個儲存庫的測試分成好幾層，各自用不同的指令跑；這一章談每一層測試什麼、CI 又跑了哪些 job，
以及刻意不跑什麼。

## 後端測試

後端測試在 `tests/Struo.Tests`，用 xUnit，執行指令是 `dotnet test`。預設後端是 SQLite：多數
測試各自建立一個獨立的暫存檔資料庫（`SqliteTestDatabase`），測試結束就刪除，彼此不共用狀態。

選用啟用的 PostgreSQL 套件（`PostgresIntegrationTests`）只在能解析出一條連線字串時才會真的
跑：先看環境變數 `STRUO_TEST_PG_CONNECTION`，沒有才退回設定檔裡的
`Testing:PostgresConnection`，透過另一個獨立的 `ConfigurationBuilder` 讀取。這是整個應用程
式裡唯一一個 `Section__Key` 慣例失效的設定鍵：只認 `STRUO_TEST_PG_CONNECTION` 這個名稱，沒
有對應的 `Testing__PostgresConnection`。解析不到連線字串時，套件裡每一個測試都直接判定通
過，不算沒跑。

這個套件只認資料庫名稱含 `test`（大小寫不分）的連線字串，名稱對不上就拒絕連上去，避免測試
不小心接到一個真正在用的資料庫。它也停用了 Npgsql 的連線池
（`PgTestConnectionString.DisablePooling`）：這是針對 xUnit／VSTest 測試主機下量到的一次
`WSA_OPERATION_ABORTED` socket 中斷所做的隔離，記錄的是一次測量到的現象，不是「正式環境也
有這個風險」的證明——正式環境只用一條連線字串，不會呼叫建立資料庫的動作，連線池也只有一個。

### 測試套件用範例當固定物

整個後端測試套件裡，有 36 個檔案 `using Struo.Sample.Blog;`，分佈在 `Query`（23 個）、
`Metadata`（3 個）、`Persistence`（4 個）、`Revisions`（2 個）、`Search`（2 個）、`Changes`
（1 個）與 `Health`（1 個）；載入機制是一個 `[ModuleInitializer]` 手法
（`ContentAssemblyEnvBootstrap`）。範例專案不只是拿來示範，它是這些測試實際拿來查詢、驗證
的固定物：拆掉範例，這些測試要嘛跟著改寫，要嘛改用別的固定物。移除範例的完整步驟，見
[第 23 章：範例專案導覽](23-sample-walkthrough.md)。

### 幾個當契約用的測試

有幾個測試不是驗證某個功能，而是把一條規則釘死，改壞了規則本身測試就會紅。
`CodeCitationConventionTests` 檢查手冊與程式碼裡的引用是不是都指到一個建構，不是行號。
`TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` 確認 API 專
案（`Struo.Api`）沒有直接參照到任何範例組件。`OptionsValidationTests` 確認每一個要在啟動時
驗證的設定類別都被正確驗證，例如少了資料庫連線字串或啟用 OIDC 卻沒填 client id 都必須讓啟
動失敗。`CoreSchemaSnapshotTests` 把核心 schema 釘死在一份提交進版本控制的快照上，下面
〈Schema 契約〉細談。

### 測自己的搜尋提供者與寫入通知

測試替身涵蓋兩個核心讓出去的介面：`ScriptedSearchProvider` 把一個寫死的 `SearchOutcome` 接
到 `ISearchProvider`，同時記下收到的每一次 `SearchRequest`；`RecordingItemChangeListener`
記下每一次 `OnChangedAsync` 呼叫，可以選擇性地掛一個腳本化的回呼。兩個介面本身怎麼運作，見
[第 18 章：擴充點：搜尋提供者與寫入通知](18-extension-points.md)。

## 前端測試

前端測試用 Vitest，設定在 `frontend/vite.config.ts` 的 `test` 區塊：環境是 `jsdom`，
`globals`／`restoreMocks`／`clearMocks` 都開著，`setupFiles` 指到 `vitest.setup.ts`。元件與
`lib/` 層級的 `*.test.ts` 就放在被測程式碼旁邊，不集中到另一個目錄；不掛在特定元件旁邊的守
衛測試則放在 `tests/`。

三個守衛測試各釘住一件事：`tests/iconCoverage.test.ts` 確認 `ICON_MAP` 裡每個鍵都真的被原
始碼用到；`src/locales/locales.test.ts` 確認 `zh-TW`、`en` 兩份介面語言目錄的鍵集合完全對
稱；`tests/schemaContract.test.ts` 把 `schema/` 底下兩份 JSON 檔餵進真正的欄位介面
registry，驗證前端認得的欄位型別跟後端宣告的一致，下面〈Schema 契約〉細談。

`pnpm test` 只跑 Vitest，型別檢查要靠 `pnpm build`（`vue-tsc -b && vite build`）：光是單元
測試通過，不代表型別也對。

## 端到端測試

端到端測試用 Playwright，設定在 `frontend/playwright.config.ts`，分兩個專案：`core`
（`pnpm e2e`）跑 `e2e/` 底下、排除 `e2e/sample/**` 的規格，對著零個內容集合；`sample`
（`pnpm e2e:sample`）跑 `e2e/sample/**`，需要先把範例集合啟用。`pnpm e2e:all` 兩個都跑。

`webServer` 這個區塊只會啟動前端的 dev server（`pnpm dev`，`reuseExistingServer: true`），
不會替你啟動 API 或資料庫：兩個專案都假設有一份正在跑的 API 跟它背後的資料庫，接在設定好的
proxy 目標上。設定裡 `workers: 1`，`use.baseURL` 是 `http://localhost:5173`；`core` 的
`testDir` 是 `./e2e`，`sample` 的 `testDir` 是 `./e2e/sample`，兩個專案因此天生分開兩個目
錄，不用另外靠檔名規則排除。前置條件與登入用的帳密怎麼覆寫，見 `frontend/e2e/README.md`。

`sample` 專案要跑，先照[第 23 章：範例專案導覽](23-sample-walkthrough.md)的步驟把範例啟
用；`playwright test --list --project=sample` 目前列出 8 個 spec 檔、15 個測試。

在 API 與資料庫都已經跑著的前提下，本機一次跑完 `frontend/` 的單元測試、build 與 `core`
這個 e2e 專案：`cd frontend && pnpm test && pnpm build && pnpm e2e`。

## Schema 契約

schema 契約是兩個提交進版本控制的 JSON 檔：`schema/core-collections.json` 記錄七個框架集
合的 `GET /api/schema` 線上格式，`schema/interfaces.json` 記錄每一個宣告出來的欄位介面與關
聯介面成員。兩側各有一個測試釘住它們：後端 `CoreSchemaSnapshotTests`（兩個測試方法，
`CoreSchema_MatchesCommittedSnapshot` 與 `InterfaceEnums_MatchCommittedSnapshot`），前端
`schemaContract.test.ts`——後者把這兩份檔案餵進真正的 registry 跟欄位型別解析邏輯，不是自
己重新宣告一份預期值。這是第四種測試，不是第四道指令：兩邊都搭著 `dotnet test`／
`pnpm test` 一起跑，不需要另外的執行方式。

改了核心 schema 之後要重新產生快照：

```bash
UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
```

```powershell
$env:UPDATE_SCHEMA_SNAPSHOT = 1
dotnet test --filter CoreSchemaSnapshot
Remove-Item Env:UPDATE_SCHEMA_SNAPSHOT
```

PowerShell 下設完這個環境變數要記得清掉：它會留在同一個工作階段裡，悄悄關掉這一道關卡，之
後每一次 `dotnet test` 都變成在重新產生快照，不是驗證它。完整規則在 `schema/README.md`。

## 手冊自己的關卡

手冊本身也有一道關卡，在 `docs/` 底下跑：`pnpm test` 是守衛腳本自己的單元測試
（`node --test "scripts/**/*.test.mjs"`），這是 CI 裡的一個步驟，不是第六個 standing
gate。`pnpm build` 分三段：先是 `vitepress build`，解析每一個章節連結；接著
`check-rendered-chapters.mjs`，確認每一章都真的渲染出內容，不是空頁；最後
`check-table-width.mjs`，擋下超過四欄或超過 60 個顯示寬度的儲存格。三段有一段沒過，
`pnpm build` 就算失敗。

## CI 跑什麼

`.github/workflows/ci.yml` 定義六個 job。`backend`、`frontend`、`docs`、`docker` 這四個都
在推上 `main`、每一個 pull request，以及手動觸發時跑；`sonar-backend`、`sonar-frontend` 也
在同樣的時機被觸發，但多一個條件。`backend` 跟 `docker` 在儲存庫根目錄跑，`frontend` 跟
`docs` 各自進自己的子目錄。

`backend`：`dotnet restore`、`dotnet build --no-restore --configuration Release`、
`dotnet test --no-build --configuration Release --verbosity normal`，跑完整的後端測試套
件。CI 裡從來不設定 `STRUO_TEST_PG_CONNECTION`，所以那個選用啟用的 PostgreSQL 套件在這裡
一律走沒跑等於通過的路徑。

`frontend`：在 `frontend` 目錄下 `pnpm install --frozen-lockfile --ignore-scripts`，然後
`pnpm test`，然後 `pnpm build`（`vue-tsc -b && vite build`）——單元測試之外，這個 build 同
時是 CI 唯一會檢查 SPA TypeScript 型別的地方。

`docs`：在 `docs` 目錄下 `pnpm install --frozen-lockfile --ignore-scripts`，然後
`pnpm test`（守衛腳本自己的 `node --test` 套件），然後 `pnpm build`（`vitepress build`、
`check-rendered-chapters.mjs`、`check-table-width.mjs` 三段）。

`docker`：建置兩個容器映像並各自跑一次 smoke test，映像標成 `struo-api:ci`／
`struo-admin:ci`（跟讀者自己建置時用的 `:local` 是兩個標籤）。API 映像用
`Database__DbType=Sqlite`、`Database__ConnectionString=Data Source=/tmp/struo-ci.db` 啟
動，輪詢 `/health/ready` 直到健康；SPA 映像啟動後，輪詢首頁並確認回應裡有 `assets/index-`
這個片段，證明建置出來的資源真的能透過 nginx 拿到。這個 job 不需要任何 secret，一個 fork
送來的 pull request 或 Dependabot 的更新一樣能完整跑完。

`sonar-backend`、`sonar-frontend`：把分析結果送到 SonarQube Cloud，兩者都需要
`SONAR_TOKEN`，所以對 fork 的 pull request 跟 Dependabot 一律跳過，判斷式是
`github.actor != 'dependabot[bot]' && (github.event_name != 'pull_request' ||
github.event.pull_request.head.repo.full_name == github.repository)`。跟 `docker` 相反：
`docker` 不需要 secret 所以任何來源都能跑，這兩個 job 需要 secret 所以只有推者在同一個儲存
庫裡才跑。`sonar-backend` 先裝 `dotnet-sonarscanner`、`dotnet-coverage` 兩個 CLI 工具，起
手一次 `dotnet-sonarscanner begin`，接著正常建置，用 `dotnet-coverage collect` 包住
`dotnet test` 產生 `coverage.xml`，最後 `dotnet-sonarscanner end` 送出分析結果；
`sonar-frontend` 則是 `pnpm install`、`pnpm vitest run --coverage --coverage.reporter=lcov`
先產生涵蓋率報告，再用 `pnpm dlx sonarqube-scanner` 送出。

## 五個 standing gate

這個儲存庫的五個 standing gate 是：

- `dotnet build`
- `dotnet test`
- `frontend/` 底下的 `pnpm test`
- `frontend/` 底下的 `pnpm build`
- `docs/` 底下的 `pnpm build`

`docker`、`sonar-backend`、`sonar-frontend` 是額外的 job，不是額外的 gate。

## CI 刻意不跑什麼

CI 不會跑 `pnpm e2e`、`pnpm e2e:sample` 或 `pnpm e2e:all`：沒有任何一個 job 起資料庫、起
API，或呼叫 `playwright test`。端到端測試需要一份跑著的 API 跟資料庫陪著前端的 dev
server，比這裡任何一個 job 準備的環境都重，是本機、合併前的自律動作，不是自動化關卡。

CI 也不會設定 `STRUO_TEST_PG_CONNECTION`，選用啟用的 PostgreSQL 套件因此在這裡永遠走沒跑
等於通過的路徑，跟本機接了一條真正的 PostgreSQL 連線字串時不一樣。

schema 契約兩側其實搭著既有的步驟跑：`CoreSchemaSnapshotTests` 在 `backend` 的
`dotnet test` 裡，`schemaContract.test.ts` 在 `frontend` 的 `pnpm test` 裡，沒有為了它們
另外改 `ci.yml`。

## 接下來

測試與 CI 談到這裡；下一章，[第 23 章：範例專案導覽](23-sample-walkthrough.md)，逐檔案看
範例展示了哪些機制。
