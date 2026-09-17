# 22. 測試與 CI

這個專案的測試分成好幾層，各自用不同的指令跑；這一章談每一層測試什麼、CI 又跑了哪些 job，
以及刻意不跑什麼。

## 後端測試

後端測試在 `tests/Struo.Tests`，用 xUnit，執行指令是 `dotnet test`。預設後端是 SQLite：多數
測試各自建立一個獨立的暫存檔資料庫（`SqliteTestDatabase`），測試結束就刪除，彼此不共用狀態。

選用的 PostgreSQL 整合測試（`PostgresIntegrationTests`）只在能解析出一條連線字串時才會真的
跑：先看環境變數 `STRUO_TEST_PG_CONNECTION`，沒有才退回設定裡的鍵。鍵名，以及它是整個設定
面唯一一個不吃 `Section__Key` 覆寫規則的鍵，見[第 4 章：設定參考](04-configuration.md)的
`Testing` 一節；讀取由測試自己的 `ConfigurationBuilder` 做，不經過應用程式的選項繫結。

解析不到連線字串時，這組測試每一個都直接判定通過，而不是標成略過——測試報告上的綠燈，不代
表真的連過一次 PostgreSQL。

這組測試只認資料庫名稱含 `test`（大小寫不分）的連線字串，名稱對不上就拒絕連上去，免得測試
接到一個正在用的資料庫。`PgTestConnectionString.DisablePooling` 另外把 Npgsql 的連線池關
掉，隔開測試主機底下看過的 socket 中斷；那是測試主機上量到的現象，不是正式環境也有這個風險
的證據。

### 測試套件用範例當 fixture

整個後端測試套件裡有 36 個檔案 `using Struo.Sample.Blog;`，大半在 `Query` 底下，其餘散在
`Metadata`、`Persistence`、`Revisions`、`Search`、`Changes` 與 `Health`；載入靠
`ContentAssemblyEnvBootstrap` 這個 `[ModuleInitializer]`。範例專案不只是拿來示範，它是這些
測試實際拿來查詢、驗證的 fixture：拆掉範例，這些測試要嘛跟著改寫，要嘛改用別的 fixture。移
除範例的完整步驟，見[第 23 章：範例專案導覽](23-sample-walkthrough.md)。

### 幾個當契約用的測試

有幾個測試不是驗證某個功能，而是把一條規則釘死，改壞了規則本身測試就會紅。

- `CodeCitationConventionTests`：手冊與程式碼裡的引用都要指到一個建構，不是行號。
- `TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples`：
  API 專案（`Struo.Api`）沒有直接參照到任何範例組件。
- `OptionsValidationTests`：驅動真正的啟動流程，確認設定錯誤會在啟動當下就炸開、而不是拖到
  第一個請求：少了資料庫連線字串、`Query:MaxLimit` 設成 0、啟用 OIDC 卻沒填 client id，三種
  情況都必須讓啟動失敗。
- `CoreSchemaSnapshotTests`：一個快照測試，把核心 schema 釘死在一份提交進版本控制的快照
  上，下面〈Schema 契約〉細談。

### 測自己的搜尋提供者與寫入通知

框架留給 fork 接手的兩個介面，測試套件各有一個替身：`ScriptedSearchProvider` 把一個腳本化
的 `SearchOutcome` 接到 `ISearchProvider`，同時記下收到的每一次 `SearchRequest`；
`RecordingItemChangeListener` 記下每一次 `OnChangedAsync` 呼叫，可以選擇性地掛一個腳本化的
回呼。兩個介面本身怎麼運作，見[第 18 章：擴充點：搜尋提供者與寫入通知](18-extension-points.md)。

## 前端測試

前端測試用 Vitest，設定在 `frontend/vite.config.ts` 的 `test` 區塊：環境是 `jsdom`，
`globals`／`restoreMocks`／`clearMocks` 都開著，`setupFiles` 指到 `vitest.setup.ts`。這一節
的指令都在 `frontend/` 底下跑。元件與 `lib/` 層級的 `*.test.ts` 就放在被測程式碼旁邊，不集
中到另一個目錄；跨檔案的守衛測試多半放在 `tests/`，語系包那一個則跟著 `src/locales/` 走。

三個守衛測試各釘住一件事：

- `tests/iconCoverage.test.ts`：掃過 `src/`，確認原始碼裡出現的每一個 `pi-` 圖示 token 在
  `ICON_MAP` 裡都有對應的鍵。
- `src/locales/locales.test.ts`：`zh-TW`、`en` 兩份介面語言目錄的鍵集合完全對稱。
- `tests/schemaContract.test.ts`：把 `schema/` 底下兩份 JSON 檔餵進真正的欄位介面
  registry，驗證前端認得的欄位型別跟後端宣告的一致，下面〈Schema 契約〉細談。

`pnpm test` 只跑 Vitest，型別檢查要靠 `pnpm build`（`vue-tsc -b && vite build`）：光是單元
測試通過，不代表型別也對。

## 端到端測試

端到端測試用 Playwright，設定在 `frontend/playwright.config.ts`，分兩個專案：
`core`（`pnpm e2e`）跑 `e2e/` 底下、排除 `e2e/sample/**` 的規格，在沒有任何內容集合的情況
下跑；`sample`（`pnpm e2e:sample`）跑 `e2e/sample/**`，需要先把範例集合啟用。`pnpm e2e:all`
兩個都跑。

`webServer` 這個區塊只會啟動前端的 dev server（`pnpm dev`，`reuseExistingServer: true`），
不會替你啟動 API 或資料庫：兩個專案都假設有一份正在跑的 API 跟它背後的資料庫，接在設定好的
proxy 目標上。

設定裡 `workers: 1`，`use.baseURL` 是 `http://localhost:5173`；`core` 的 `testDir` 是
`./e2e`，範圍本來就涵蓋 `e2e/sample/`，所以它靠 `testIgnore: '**/e2e/sample/**'` 把範例規
格擋在外面；`sample` 的 `testDir` 直接指到 `./e2e/sample`，不必再排除什麼。登入用的
bootstrap 管理員要先種好；其餘前置條件與帳密怎麼覆寫，見 `frontend/e2e/README.md`。

`sample` 專案要跑，先照[第 23 章](23-sample-walkthrough.md)的步驟把範例啟用。用 `--list` 跑
`sample` 這個專案會列出 8 個 spec 檔、15 個測試。

在 API 與資料庫都已經跑著的前提下，本機一次跑完 `frontend/` 的單元測試、build 與 `core`
這個 e2e 專案：

```
cd frontend && pnpm test && pnpm build && pnpm e2e
```

## Schema 契約

schema 契約是兩個提交進版本控制的 JSON 檔：`schema/core-collections.json` 記錄七個框架集
合的 `GET /api/schema` 傳輸格式，`schema/interfaces.json` 記錄每一個宣告出來的欄位介面與關
聯介面成員。

兩側各有一個測試釘住它們：後端 `CoreSchemaSnapshotTests`（兩個測試方法，
`CoreSchema_MatchesCommittedSnapshot` 與 `InterfaceEnums_MatchCommittedSnapshot`），前端
`schemaContract.test.ts`——後者把這兩份檔案餵進真正的 registry 跟欄位型別解析邏輯，不是自
己重新宣告一份預期值。這是第四種測試，不是第四道指令：兩邊都搭著 `dotnet test`／
`pnpm test` 一起跑，不需要另外的執行方式。

改了核心 schema 之後要重新產生快照，Bash 與 PowerShell 各一組：

```bash
UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
```

```powershell
$env:UPDATE_SCHEMA_SNAPSHOT = 1
dotnet test --filter CoreSchemaSnapshot
Remove-Item Env:UPDATE_SCHEMA_SNAPSHOT
```

PowerShell 下設完這個環境變數要記得清掉：它會留在同一個終端機工作階段裡，悄悄關掉這一道關
卡，之後每一次 `dotnet test` 都變成在重新產生快照，不是驗證它。完整規則在 `schema/README.md`。

## 手冊自己的關卡

這道關卡是 `docs/` 底下的 `pnpm build`，分三段：先是 `vitepress build`，解析每一個章
節連結；接著 `check-rendered-chapters.mjs`，確認每一章都真的渲染出內容，不是空頁；最後
`check-table-width.mjs`，擋下超過四欄的表格，以及顯示寬度超過 60 的儲存格。三段有一段沒
過，`pnpm build` 就算失敗。

同一個目錄下的 `pnpm test` 跑的是守衛腳本自己的單元測試，指令是
`node --test "scripts/**/*.test.mjs"`；CI 會跑這一步，但它是步驟，不是關卡。

## CI 跑什麼

`.github/workflows/ci.yml` 定義六個 job。`backend`、`frontend`、`docs`、`docker` 這四個都
在推上 `main`、每一個 pull request，以及手動觸發時跑；`sonar-backend`、`sonar-frontend` 也
在同樣的時機被觸發，但多一個條件。`backend` 跟 `docker` 在儲存庫根目錄跑，`frontend` 跟
`docs` 各自進自己的子目錄。

`backend`：`dotnet restore`、`dotnet build --no-restore --configuration Release`、
`dotnet test --no-build --configuration Release --verbosity normal`，跑完整的後端測試套
件。CI 裡從來不設定 `STRUO_TEST_PG_CONNECTION`，所以那組選用的 PostgreSQL 整合測試在這裡
一律直接判定通過。

`frontend`：在 `frontend` 目錄下 `pnpm install --frozen-lockfile --ignore-scripts`，然後
`pnpm test`，然後 `pnpm build`（`vue-tsc -b && vite build`）。單元測試之外，這個 build 還
做完整的正式環境建置：`pnpm test` 不碰型別，SPA 的 TypeScript 型別是在這一步被檢查的。

`docs`：在 `docs` 目錄下 `pnpm install --frozen-lockfile --ignore-scripts`，然後
`pnpm test`（守衛腳本自己的 `node --test` 套件），然後 `pnpm build`（`vitepress build`、
`check-rendered-chapters.mjs`、`check-table-width.mjs` 三段）。

`docker`：建置兩個容器映像並各自跑一次 smoke test，映像標成 `struo-api:ci`／
`struo-admin:ci`（跟你在[第 20 章：部署](20-deployment.md)裡建置時用的 `:local` 不是同一
個標籤）。

API 映像用 `Database__DbType=Sqlite`、
`Database__ConnectionString=Data Source=/tmp/struo-ci.db` 啟動，輪詢 `/health/ready` 直到
健康；SPA 映像啟動後，輪詢首頁並確認回應裡有 `assets/index-` 這個片段，證明建置出來的資源
真的能透過 nginx 拿到。這個 job 不需要任何 secret，一個 fork 送來的 pull request 或
Dependabot 的更新一樣能完整跑完。

`sonar-backend`、`sonar-frontend` 把分析結果送到 SonarQube Cloud，兩個 job 都要
`SONAR_TOKEN`：Dependabot 送來的更新一律跳過，pull request 也只有在來源分支跟目標在同一個
儲存庫時才跑。

`sonar-backend` 先裝 `dotnet-sonarscanner`、`dotnet-coverage` 兩個 CLI 工具，跑一次
`dotnet-sonarscanner begin`，接著正常建置，用 `dotnet-coverage collect` 包住 `dotnet test`
產生 `coverage.xml`，最後 `dotnet-sonarscanner end` 送出分析結果。

`sonar-frontend` 則是同一道 `pnpm install --frozen-lockfile --ignore-scripts`、
`pnpm vitest run --coverage --coverage.reporter=lcov` 先產生涵蓋率報告，再用
`pnpm dlx sonarqube-scanner` 送出。

## 五個 standing gate

這個專案的五個 standing gate 是：

- `dotnet build`
- `dotnet test`
- `frontend/` 底下的 `pnpm test`
- `frontend/` 底下的 `pnpm build`
- `docs/` 底下的 `pnpm build`

改到哪一邊就跑那一邊；一次改動同時碰到後端、前端、手冊裡不只一邊時，五個就全跑一次再推。
`docs/` 的 `pnpm test`、`docker`、`sonar-backend`、`sonar-frontend` 也在 CI 裡跑，但都不算
standing gate。

## CI 刻意不跑什麼

CI 不會跑 `pnpm e2e`、`pnpm e2e:sample` 或 `pnpm e2e:all`：沒有任何一個 job 起資料庫、起
API，或呼叫 `playwright test`。端到端測試需要一份跑著的 API 跟資料庫陪著前端的 dev
server，比這裡任何一個 job 準備的環境都重，是本機、合併前的自律動作，不是自動化關卡。

CI 也不會設定 `STRUO_TEST_PG_CONNECTION`，那組選用的 PostgreSQL 整合測試因此在這裡永遠是
直接判定通過，跟本機接了一條真正的 PostgreSQL 連線字串時不一樣。

schema 契約不需要自己的 job：兩側各自搭在 `backend` 的 `dotnet test` 與 `frontend` 的
`pnpm test` 裡。

## 接下來

測試與 CI 談到這裡；下一章，[第 23 章：範例專案導覽](23-sample-walkthrough.md)，逐檔案看
範例展示了哪些機制。
