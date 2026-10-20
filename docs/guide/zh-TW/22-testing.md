# 22. 測試與 CI

這個專案的測試分成好幾層，各自用不同的指令跑；這一章談每一層測試什麼、CI 又跑了哪些 job，
以及刻意不跑什麼。

## 後端測試

後端測試在 `tests/Struo.Tests`，用 xUnit，執行指令是 `dotnet test`。預設後端是 SQLite：單元
測試從 `TestBackend.CreateUnitDatabase()` 拿資料庫，沒有切換後端時那是一個暫存的 SQLite 檔，
測試結束就刪除，彼此不共用狀態。`tests/Struo.Tests/Support/` 以外的測試直接用
`SqliteTestDatabase` 的話，`SqliteTestDatabaseUsageTests` 會失敗，所以每個測試都跟著後端切換走。

這些單元測試用 CodeFirst 從 entity 建表。fork 自己的測試 entity 宣告了唯一群組索引時，名稱要
控制在 63 個字元以內，見[第 21 章〈各後端的差異〉](21-schema-and-upgrades.md#各後端的差異)。

### 實機後端的測試

四條測試連線各自打開一個後端的實機測試：`Testing:PostgresConnection`、
`Testing:SqlServerConnection`、`Testing:MySqlConnection` 與 `Testing:MariaDbConnection`。每個鍵
先看自己的環境變數，例如 `STRUO_TEST_PG_CONNECTION`，沒有才讀設定檔。這四個鍵不吃
`Section__Key` 覆寫規則，鍵與變數的對照見[第 4 章：設定參考](04-configuration.md)的 `Testing`
一節；讀取由測試自己的 `ConfigurationBuilder` 做，不經過應用程式的選項繫結。

設定了連線的後端會跑這幾組測試：

- migration 的實機測試，例如 `CoreMigrationsLiveTests`：套用核心 migration，再用
  `SchemaChecker` 檢查結果。
- `LiveRepositoryTests`：每個設定了連線的後端都跑，不受 `STRUO_TEST_BACKEND` 影響。
- `PostgresOnlyTests`：只在 PostgreSQL 上成立的測試。
- `TestDatabaseLiveTests`：測試資料庫本身的建立與清理。

連線留空時，那個後端的實機測試每一個都直接判定通過，而不是標成略過——測試報告上的綠燈，不代
表真的連過那個資料庫，要看每個測試花的時間來判斷。

測試只認資料庫名稱含 `test`（大小寫不分）的連線字串，名稱對不上就拒絕連上去，免得測試接到一
個正在用的資料庫。設定的資料庫要事先建好，登入帳號要有建立與刪除資料庫的權限：
`LiveRepositoryTests` 這類測試會在同一台伺服器上自己建資料庫，結束時刪掉。

`PgTestConnectionString.DisablePooling` 另外把 Npgsql 的連線池關掉，隔開測試主機底下看過的
socket 中斷；那是測試主機上量到的現象，不是正式環境也有這個風險的證據。

### 把整套測試搬到實機後端

環境變數 `STRUO_TEST_BACKEND` 選整套測試跑在哪個後端：`Sqlite`（預設）、`PostgreSQL`、
`SqlServer`、`MySql` 或 `MariaDb`，大小寫不分。值不對，或選了實機後端卻沒設它的連線，整個測試
執行直接失敗，訊息點名要設什麼。

```bash
STRUO_TEST_BACKEND=MySql dotnet test
```

這個切換搬動兩種測試資料庫：

- **API 測試主機**（`ApiFactory` 與 CORS 的測試主機）：每個主機各自建一個資料庫，用完自己刪掉。
- **單元測試**：從每個後端一個的資料庫池裡租用，第一次讀它的連線字串或名稱時才建立。每次租到
  的都是空的；歸還時刪掉所有資料表。池裡的資料庫叫 `<名稱>_unit_<序號>_<執行 id>`。

MySQL 與 MariaDB 的測試資料庫建成 utf8mb4。MySQL 要用伺服器預設的嚴格 `sql_mode`：非嚴格模式
會悄悄截斷或替換值，把長度與字元集的錯誤藏起來。

只在 SQLite 上才有意義的測試掛 `[SqliteOnlyFact]` 或 `[SqliteOnlyTheory]`，在實機後端上略過，
略過理由以 `SQLite-only:` 開頭。

池裡的資料庫在測試執行結束時刪除。中途當掉或被終止的執行可能留下資料庫；MySQL 與 MariaDB 上，
被丟棄的資料庫在佔著它的連線關閉之前也會留著。跑完實機測試後，這行指令刪掉所選後端上其他執行留下的
資料庫：

```bash
STRUO_TEST_BACKEND=<後端> STRUO_TEST_DROP_LEFTOVERS=1 dotnet test --filter "FullyQualifiedName~Drop_leftover_databases_on_request"
```

它會強制刪除其他執行的資料庫，同一台伺服器上還有別的測試在跑時不要用。

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
格擋在外面；`sample` 的 `testDir` 直接指到 `./e2e/sample`，不必再排除什麼。資料庫要先跑過
`migrate`（或設 `Database:MigrateOnStartup=true`），登入用的 bootstrap 管理員才會種好；其餘前置
條件與帳密怎麼覆寫，見 `frontend/e2e/README.md`。

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
件。CI 不設定任何 `STRUO_TEST_*` 連線，也不設 `STRUO_TEST_BACKEND`，所以這裡只跑 SQLite，每
一組實機測試都直接判定通過。

`frontend`：在 `frontend` 目錄下 `pnpm install --frozen-lockfile --ignore-scripts`，然後
`pnpm test`，然後 `pnpm build`（`vue-tsc -b && vite build`）。單元測試之外，這個 build 還
做完整的正式環境建置：`pnpm test` 不碰型別，SPA 的 TypeScript 型別是在這一步被檢查的。

`docs`：在 `docs` 目錄下 `pnpm install --frozen-lockfile --ignore-scripts`，然後
`pnpm test`（守衛腳本自己的 `node --test` 套件），然後 `pnpm build`（`vitepress build`、
`check-rendered-chapters.mjs`、`check-table-width.mjs` 三段）。

`docker`：建置兩個容器映像並各自跑一次 smoke test，映像標成 `struo-api:ci`／
`struo-admin:ci`（跟你在[第 20 章：部署](20-deployment.md)裡建置時用的 `:local` 不是同一
個標籤），接著起整套 Compose 環境再測一次。

API 映像先用 `Database__DbType=Sqlite`、
`Database__ConnectionString=Data Source=/app/App_Data/struo-ci.db`，對具名 volume
`struo-ci-data` 上的 SQLite 檔跑一次 `migrate`，再掛同一個 volume 啟動 API，輪詢
`/health/ready` 直到健康；SPA 映像啟動後，輪詢首頁並確認回應裡有 `assets/index-` 這個片段，
證明建置出來的資源真的能透過 nginx 拿到。

建置完成後，Trivy 會立刻掃描兩個映像；只要有上游已提供修補版本的 HIGH 或 CRITICAL 漏洞，job
就會失敗。尚無修補版本的項目會被忽略，所以你無從處理的 CVE 不會擋住合併。同一份結果也會以
SARIF 上傳到儲存庫的 Security 分頁（code scanning）；有些情況會略過上傳（見下），但阻擋用的
掃描到處都會跑。

SARIF 上傳只在公開儲存庫的 push 與同儲存庫的 pull request 上執行。私有儲存庫會略過，因為那裡的
code scanning 需要 GitHub Advanced Security，沒有的話上傳會失敗。已啟用 code scanning 的私有儲存庫
可以把 `sarif-gate` 步驟裡的 `github.event.repository.private` 條件拿掉。

Compose 那一段跑 `docker compose up --wait`，映像由同樣兩份 Dockerfile 另外建置；接著檢查
8081 埠上 API 的 `/health/ready`、8080 埠上後台首頁有沒有同一個 `assets/index-` 片段，再透過
nginx 打一次匿名的 `/api/config`。最後一定跑 `docker compose down --volumes`，有檢查失敗就先
印出 `docker compose logs`。

這個 job 不需要任何 secret，一個 fork 送來的 pull request 或 Dependabot 的更新一樣能完整
跑完。

`sonar-backend`、`sonar-frontend` 把分析結果送到 SonarQube Cloud，兩個 job 都要
`SONAR_TOKEN` secret，組織與專案 key 則讀自儲存庫變數 `SONAR_ORGANIZATION`、
`SONAR_BACKEND_PROJECT_KEY`、`SONAR_FRONTEND_PROJECT_KEY`；沒有 `SONAR_ORGANIZATION` 的儲存庫
兩個 job 都跳過。Dependabot 送來的更新一律跳過，pull request 也只有在來源分支跟目標在同一個
儲存庫時才跑。

`sonar-backend` 先依 `.config/dotnet-tools.json` 釘住的版本還原 `dotnet-sonarscanner`、
`dotnet-coverage` 兩個 CLI 工具，跑一次 `dotnet dotnet-sonarscanner begin`，接著正常建置，
用 `dotnet dotnet-coverage collect` 包住 `dotnet test` 產生 `coverage.xml`，最後
`dotnet dotnet-sonarscanner end` 送出分析結果。

`sonar-frontend` 則是同一道 `pnpm install --frozen-lockfile --ignore-scripts`、
`pnpm vitest run --coverage --coverage.reporter=lcov` 先產生涵蓋率報告，再用
SonarSource 的 `sonarqube-scan-action` 送出，這個 action 以 commit SHA 釘住，
由 Dependabot 更新。

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

CI 也不會設定任何 `STRUO_TEST_*` 連線，實機測試因此在這裡永遠是直接判定通過，跟本機接了真正的
資料庫連線時不一樣。

schema 契約不需要自己的 job：兩側各自搭在 `backend` 的 `dotnet test` 與 `frontend` 的
`pnpm test` 裡。

## 接下來

測試與 CI 談到這裡；下一章，[第 23 章：範例專案導覽](23-sample-walkthrough.md)，逐檔案看
範例展示了哪些機制。
