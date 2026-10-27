# 18. 擴充點

呼叫端的 `search=` 該怎麼回答，資料寫入之後要不要順便做點什麼，GraphQL schema 裡要有什麼，核心把這幾件事
各自留成一個接口，fork 接上自己的實作就好，不必碰核心的查詢或寫入路徑本身。這一章談 fork 的服務註冊在
哪裡、`ISearchProvider` 與 `IItemChangeListener` 各自的契約、什麼時候被呼叫、失敗時會發生什麼事，怎麼用寫入攔截器與刪除處理器替項目寫入加上商業規則，怎麼帶來自己的檔案儲存，怎麼用 `StruoDomainException` 定義自己的、給客戶端看的錯誤，以及
`ConfigureGraphQl` 怎麼擴充 GraphQL schema。

`search=` 本身的語法與內建的 LIKE 掃描，見
[第 10 章：查詢：過濾、排序、分頁](10-query-basics.md)；一次寫入的版本紀錄與軟刪除語
意，見[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)。

## 兩個介面在架構裡的位置

`ISearchProvider` 與 `IItemChangeListener` 是[第 2 章：系統架構](02-architecture.md)「什
麼可以換、什麼不能換」列出的其中兩個讓出去的介面，跟 `IFileStorage` 站在同一層。核心只
內建一個永遠不處理搜尋的 `NullSearchProvider`，以及一個負責派送、但預設沒有任何監聽者
的 `ItemChangeNotifier`；fork 要接手，就是實作介面本身、註冊進去，不用改核心一行程式
碼。

## 你的註冊寫在哪裡

fork 的 `Program.cs` 歸你所有。核心用兩個呼叫組裝自己：`builder.AddStruoCms()` 註冊所有服務，
`app.UseStruoCmsAsync()` 建好請求管線並跑啟動檢查。你自己的服務註冊在兩者之間，也就是
`AddStruoCms()` 之後。下面是隨附的 `src/Struo.Api/Program.cs` 的節錄，只留下這裡要談的幾行；
完整的檔案還有啟動用的 bootstrap logger 與啟動錯誤的處理：

```csharp
var cli = MigrationCli.Parse(args);
var builder = WebApplication.CreateBuilder(cli.HostArgs);

builder.AddStruoCms();
builder.Services.AddScoped<ISearchProvider, MySearchProvider>();
builder.Services.AddScoped<IItemChangeListener, MyIndexer>();

var app = builder.Build();
if (await app.TryRunStruoCommandAsync(cli)) return;
await app.UseStruoCmsAsync();
await app.RunAsync();
```

`AddStruoCms()` 執行時就會讀設定，用來掃描 `Struo:ContentAssemblies`。自己的設定來源（例如
Key Vault）要在呼叫之前加進 `builder.Configuration`，否則掃描看不到。

在 `AddStruoCms()` 之後註冊的，一律贏過核心的預設。下列預設，核心用的是 `TryAdd`，所以寫在
呼叫之前的註冊也會被保留：`ISearchProvider`、`IItemChangeNotifier`、`IHtmlSanitizer`、
`IPasswordHasher`、`IFileAccessPolicy`、`IFileStorage`。

其中三個有契約，替換的實作要遵守：

- `IPasswordHasher` 要能驗證已存的 Argon2id 雜湊，而且比對要是常數時間。
- `IHtmlSanitizer` 是防止富文字欄位被注入指令碼的唯一防線。
- `IFileAccessPolicy` 是檔案下載的權限檢查。

## 搜尋提供者 `ISearchProvider`

`ISearchProvider` 是搜尋這一側的擴充點：fork 接上 Meilisearch、Elasticsearch、
PostgreSQL 全文搜尋，或任何其他引擎，讓 `search=` 改由這個引擎回答。核心拿到的是一組乾
淨的候選 id，不用知道背後接的是哪一種索引。

### 契約

`ISearchProvider` 只有一個方法：
`Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)`。

`SearchRequest` 帶的成員：

- `Collection`：集合的正式 camelCase 名稱，不是呼叫端在路由上打的那個拼法。REST 路由不
  分大小寫比對，提供者看到的永遠是同一種寫法。
- `Term`：`search=` 的原始值，不裁切、不轉小寫。
- `Locale`：這次查詢實際生效的語言，有帶 `locale=` 就用它，沒帶就用站台預設，對沒有翻
  譯的集合一樣會算出來。
- `SearchableFields`：核心自己算出來、`Searchable` 且非 `Hidden` 的欄位清單，只是個提
  示，提供者可以完全不理會，改用自己索引的欄位。

答案只有兩種狀態：`SearchOutcome.NotHandled`，或是用 `SearchOutcome.Candidates(ids)`
建出來的候選結果，讀得到的是 `Handled`（布林值）跟 `Ids`（可為 `null` 的字串清單）兩個
屬性。`NotHandled` 代表核心內建的 LIKE 掃描照常跑，等於完全沒有提供者介入；
`Candidates(ids)` 則是把 root id 以字串形式整批交出來，直接取代 LIKE 搜尋，`search=`
這個詞這時不會再被拿去做任何比對。

空的候選清單是一次「有處理、零筆命中」的搜尋，不是退回 LIKE：提供者真的什麼都沒找到，
也該回 `Candidates([])`，不是 `NotHandled`。把 `null` 傳給 `Candidates` 會直接丟例
外，這兩種狀態刻意不共用一個可為 `null` 的參數。核心內建的 `NullSearchProvider` 永遠
回傳 `NotHandled`，所以沒有 fork 自己的提供者時，`search=` 走的是內建的 LIKE 掃描。

### 何時被問

核心只在列表請求裡問這個提供者，而且只在 `search=` 非空白時才問，每個請求只問一次；單
筆 `GET` 從來不會問它，關聯展開也不會。

### 與其他條件怎麼組合

提供者回的候選集跟 `filter` 用 AND 組合，一列要同時滿足 `filter`、又落在候選集裡才算
數；候選集一樣受 `deleted=` 模式約束，落在垃圾桶裡的列不會被候選集偷渡出來。候選集本身
的順序不影響結果排序，`sort=`（或沒帶 `sort=` 時的預設順序）才決定順序。

權限不是逐列在這裡把關的問題：集合層級的讀取檢查在提供者被問之前就已經跑過，候選 id 本
身沒有逐列授權可言；`Hidden` 是欄位層級的事，跟哪些列有資格進候選集無關。列表本身、每
一個 facet 與彙總，共用同一次解析出來的候選集，細節見
[第 11 章：查詢：投影、深度展開、facet 與彙總](11-query-advanced.md)。

### id 是信任邊界

提供者回來的每一個 id，最後都會變成 SQL 的 `id IN (…)` 條件，而且是用型別化的 SQL 字面
值組成，不是參數化查詢，因此是信任邊界，不是查詢字串上驗證過的使用者輸入：每個 id 先被
解析成這個集合主鍵的 CLR 型別，只支援 `Guid` 或整數主鍵（`long`、`int`、`short`），其
他鍵型別一律拒絕。候選 id 數量上限是 `Query:MaxSearchCandidates`，鍵名見
[第 4 章：設定參考](04-configuration.md)。

解析不了的 id、不支援的鍵型別，或是超過上限，都是提供者自己的契約違反，一律回 `500`
`INTERNAL_SERVER_ERROR`，不是 `400`，因為犯錯的是提供者，不是呼叫端。每一次這種
`500`，伺服器端都會在 Error 等級記一筆，操作者看得到問題，呼叫端看到的仍然是遮罩過的通
用訊息。

重複的 id 會被悄悄去重。候選 id 本身完全不經過查詢驗證器，那是提供者的輸出，不是解析
過的使用者輸入；驗證器反而會在處理請求一開始，先清空任何從外部混進來的候選欄位，當作多
一層防禦。

### 引擎掛了怎麼辦

提供者背後的引擎整個答不出來——連不上、逾時、索引不存在——都該拋
`SearchUnavailableException`：請求會失敗，REST 看到 `503` `SEARCH_UNAVAILABLE`（見
[第 12 章：REST API 慣例](12-rest-conventions.md)），GraphQL 看到同一個代碼，但傳輸層
的狀態留在 `200`（見[第 14 章：GraphQL API](14-graphql.md)）。

這個例外的訊息可能帶著內部主機名稱，呼叫端因此只看得到固定的通用訊息，真正的原因只寫
進伺服器端紀錄。想優雅降級的提供者，也可以自己接住例外、回 `NotHandled`，退回內建的
LIKE 掃描，兩條路都合法，這個介面不逼哪一種。

### 註冊與生命週期

核心自己的預設註冊用的是「沒人註冊過才註冊」，所以 fork 的註冊永遠贏，不管寫在
`AddStruoCms()` 之前還是之後：之前贏，是因為核心的預設接著就不會生效；之後贏，是因為
後面註冊的才是最後被解析到的那個。

`ISearchProvider` 跟下一節的 `IItemChangeListener`，註冊都寫在
一個 API 專案（`Struo.Api`）會參照到的組件裡即可，不需要另外列進 `Struo:ContentAssemblies`，
那份清單只掃描 `[CmsCollection]` 型別，不是擴充點的登記表。

生命週期用 scoped 或 transient，不要用 singleton，除非提供者真的完全無狀態：一個
singleton 若在建構式裡抓住某個 scoped 相依，不是啟動時就被範圍驗證擋下來，就是同一個
實例被整個應用程式生命週期重複沿用，而不是每個請求各自一份。這個位置永遠只解析出一個
提供者。

索引本身要跟著資料一起更新，不是這個介面負責的事，讓 fork 知道「這一筆資料動了」的，是
下一節的 `IItemChangeListener`。

### 一個最小實作

下面是一個最小實作，`IMyIndex` 代表 fork 自己的引擎客戶端，框架裡沒有這個型別。

```csharp
using Struo.Application.Search;

public sealed class MySearchProvider(IMyIndex index) : ISearchProvider
{
    public async Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        if (request.Collection != "article") return SearchOutcome.NotHandled;

        IReadOnlyList<string> ids = await index.LookupAsync(request.Term, request.Locale, ct);
        return SearchOutcome.Candidates(ids);   // 空清單同樣是「已處理、零筆命中」的搜尋結果
    }
}
```

集合名稱與索引查詢是 fork 自己要換掉的兩處：其他集合直接回 `NotHandled`，`search=` 就
照樣走內建的 LIKE 掃描。

註冊寫在你自己的 `Program.cs`，`AddStruoCms()` 之後：

```csharp
builder.Services.AddScoped<ISearchProvider, MySearchProvider>();
```

## 寫入通知 `IItemChangeListener`

`IItemChangeListener` 是寫入這一側的擴充點，資料被建立、更新、丟進垃圾桶、復原或清除之
後，fork 可以借這個介面做自己的事：同步一份搜尋索引、發一個 webhook、清一個快取，核心
的寫入路徑本身完全不用知道下游是什麼。

### 契約

`IItemChangeListener` 只有一個方法：
`Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default)`。

`ItemChange` 是一個 record，成員是 `Collection`、`Id`、`Kind`；`Kind` 的值與各自的
發出時機，見〈涵蓋哪些寫入〉的表。

### 時機與失敗語意

監聽者只在交易真的 commit 之後才會被叫到，讀回同一列看到的一定是已經寫進去的狀態，不
會看到還可能回滾的中間值。核心內建的派送器 `ItemChangeNotifier` 按註冊順序依序叫每一個
監聽者，各自包在自己的 try/catch 裡：某個監聽者丟例外，會在 Error 等級記一筆，帶上它自
己的型別名稱、這批變更的筆數，以及依 kind 分類的統計；下一個監聽者照樣會跑，派送器本身
永遠不會把這個例外丟出去，也沒有重試。

需要撐過當機的 fork，得在自己的監聽者裡面做一層耐久的佇列，這不是這個介面提供的。

派送時永遠用 `CancellationToken.None`，不是這次請求自己的 token，因為寫入已經 commit
了，呼叫端中途斷線不該連帶跳過通知。派送是同步等待的，寫在回應送出之前：一個監聽者慢，
這一筆寫入的回應就跟著慢。這也是為什麼真正慢的下游該在監聽者內部排隊處理，而不是原地同
步呼叫，理由是延遲，不是耐久性。

一次寫入不管牽動幾筆資料，只換來一次呼叫，裡面帶著這次寫入涉及的每一筆變更。監聽者順利
跑完，不代表呼叫端最後看到的就是成功——刪除或清除一個 `user` 之後，核心接著還會撤銷這
個使用者的所有工作階段，這一步照樣可能讓整個請求失敗；復原則是在通知監聽者之後才重新讀
一次這一列，拿來組回應。

### 涵蓋哪些寫入

| Kind | 何時發出 |
|---|---|
| `Created` | 建立一筆項目 |
| `Updated` | 更新一筆項目，或還原某個版本，機制上就是另一次更新 |
| `Trashed` | 丟進垃圾桶，且確實有列被動到 |
| `Restored` | 復原，且確實有列被動到 |
| `Purged` | 清除目標本身，以及沿著級聯刪除到的每一列 |

丟進垃圾桶或復原，若目標本來就已經處在同一個狀態，已經在垃圾桶裡的項目再丟一次、已經上
線的項目再復原一次，在 SQL 層級就是零列受影響，不會補發一次通知。

清除還會牽動目標本身之外的列：任何指向它、外鍵會被設成 `null` 的現存列，以及任何因為
junction 列被清掉而失去一段多對多關聯的現存父列，都會各自收到一次 `Updated`，清掉一個
標籤，牽動到的每一篇現存文章都算，即使文章本身那一列從頭到尾沒被改寫過。已經在垃圾桶裡
的子列若外鍵也會被設成 `null`，則直接跳過不計：它本來就不在任何索引裡，之後若被復原，
會有自己的一次 `Restored`。

牽動越多列的清除，這一次的通知批次就跟著越大，而且同一筆資料若在同一次清除裡被牽動到不
只一次——比如先被外鍵設 `null`、後面又真的被清除——最後只會出現一次，並且一律以
`Purged` 為準。

`Collection` 跟搜尋請求一樣是集合的正式名稱；`Id` 是主鍵的字串形式，`Guid` 一律用小
寫。一般集合的寫入，不管走 REST 還是 GraphQL，最後都落在同一段核心邏輯上，兩種協定發出
的通知因此完全一樣；上傳、丟進垃圾桶、復原、清除這四條檔案自己的寫入路徑，也各自對應到
`Created`、`Trashed`、`Restored`、`Purged` 其中一種，見
[第 15 章：檔案、媒體與圖片轉換](15-files-and-media.md)。

### 不涵蓋哪些

帳號、憑證、權杖、密碼與登入相關的端點，還有網站設定，是各自專屬的 store 直接處理寫
入，完全繞過一般集合的寫入路徑，因此不會觸發這個介面；一般集合寫入牽動的多對多關聯，只
有主動被改的那一側算數，被牽動的另一側不會另外算一次通知；透過這個 API 以外的管道寫進
資料庫，例如 fork 自己的 ETL 或直接接資料庫，同樣不會經過這裡。

`user`、`role`、`permission`、`userRole` 這幾個身分集合，如果改成透過通用的項目 API
去寫（適合超級管理員），走的就是跟其他集合一樣的路徑，一樣會通知：差別只在用哪一條
API 寫，不在這幾個集合本身。

### 註冊、成本與遞迴

派送器本身永遠都在，不用額外註冊；監聽者不是——fork 可以註冊任意數量，它們解析成一個集
合，不像搜尋提供者是單一位置，註冊順序只決定被叫到的順序，不決定誰會被叫到。註冊用
scoped；若堅持用 singleton，建構式裡就不能抓一個 scoped 相依，否則一樣會在啟動時被擋下
來，或者同一個實例被整個應用程式生命週期重複沿用。

監聽者一個都沒註冊時，建立、更新、丟進垃圾桶、復原都不會比單純的寫入多做事，派送器一看
清單是空的就直接回傳。清除是唯一的例外：不管有沒有人在聽，清除本身都得先算出這次牽動到
哪些現存的關聯列——每個指向它、外鍵會被設成 `null` 的關聯多付一次型別化讀取，每個指向它
的多對多 junction 多付兩次——這幾筆額外的查詢是清除自己要付的成本，不是為了監聽者才多
跑的。

這個介面只負責發出通知，不負責確認下游真的收到：核心不會重試失敗的這一次呼叫，下游系統
可能掛掉的 fork，得自己做健康檢查跟對帳。監聽者若自己又透過一般寫入路徑寫了別的東西——譬
如另外寫一筆稽核紀錄——那筆寫入本身一樣會觸發新一輪通知，這裡沒有防止遞迴的機制，設計
監聽者時得自己避開會形成循環的寫法，而不是假設派送器會擋下來。

### 一個最小實作

下面這個實作把 `Trashed` 跟 `Purged` 一起處理：

```csharp
using Struo.Application.Changes;

public sealed class MyChangeListener(IMyIndex index) : IItemChangeListener
{
    public async Task OnChangedAsync(IReadOnlyList<ItemChange> changes, CancellationToken ct = default)
    {
        foreach (var change in changes)
        {
            // 丟進垃圾桶的列會被每一次讀取濾掉，若索引還留著它們，
            // 搜尋結果就會出現 API 已經不會再回傳的列，所以把 Trashed 當 Purged 處理。
            if (change.Kind is ItemChangeKind.Purged or ItemChangeKind.Trashed)
                await index.DeleteAsync(change.Collection, change.Id, ct);
            else
                await index.UpsertAsync(change.Collection, change.Id, ct);
        }
    }
}
```

`IMyIndex` 的兩個呼叫換成自己的下游動作即可；`change.Collection` 與 `change.Id` 就是
這次寫入的那一列。

一個應用程式可以註冊任意數量的監聽者：

```csharp
builder.Services.AddScoped<IItemChangeListener, MyChangeListener>();
```

## 寫入攔截器 `IItemWriteInterceptor`

`IItemWriteInterceptor` 是項目寫入上放商業規則的擴充點：產生 slug、檢查一個欄位與另一個欄位的關
係、寫一筆稽核紀錄、讓某個集合只能新增不能改。它掛在通用的寫入路徑裡，所以 REST 與 GraphQL 都適
用，而你不必改 `ItemService`。核心自己對 `file`、`user`、`language` 的規則，也是用同樣的方式註冊
的。

### 契約

攔截器有一個 `bool AppliesTo(string collection)` 和三個掛鉤。`AppliesTo` 收到的是集合的正式名稱，
和 `ISearchProvider` 看到的拼法相同。每個掛鉤預設都是空操作，只覆寫你要的那幾個。

| 掛鉤 | 何時執行 | 丟例外 |
|---|---|---|
| `BeforeWriteAsync` | 交易之外，body 合併進實體之後 | 拒絕這次寫入 |
| `InTransactionAsync` | 交易之內，列、關聯與翻譯都寫完之後 | 整個寫入回滾 |
| `AfterCommitAsync` | commit 之後、通知監聽者之前 | 在 commit 之後才浮出 |

`InTransactionAsync` 在擷取版本紀錄之前執行。三個掛鉤都收到一個 `ItemWriteContext`，成員如下：

- `Operation`：`Create`、`Update`、`Revert`、`Trash`、`Restore` 或 `Purge`。還原版本是用過去的快照
  重做一次更新，標成 `Revert`。
- `Meta`：集合的中繼資料。
- `Entity`：正在寫的實體。建立、更新、還原時，它是 body 合併進實體之後的樣子，可以在
  `BeforeWriteAsync` 裡改。丟進垃圾桶、復原、清除時，它是既有的那一列。
- `Id`：字串形式的項目 id。建立時的 `BeforeWriteAsync` 裡是 `null`，因為列還不存在。
- `Existing`：更新與還原時，合併之前存著的那一列，掛鉤可以拿來比較新舊。只有在有攔截器適用於該集合
  時才會去讀，其他操作則是 `null`。
- `BodyKeys` 與 `Body`：請求 body 的頂層屬性名稱，以及 body 本身。丟進垃圾桶、復原、清除時，
  `BodyKeys` 是空的、`Body` 是 `null`。
- `Changed`：在 `AfterCommitAsync` 裡，丟進垃圾桶碰到已經在垃圾桶的列、復原碰到已經上線的列時為
  `false`。

丟進垃圾桶、復原、清除會帶著既有的列、沒有 body 去跑 `BeforeWriteAsync`，不存在的 id 根本不會到達
攔截器。它們的 `InTransactionAsync` 只在那一列真的被丟進垃圾桶、復原或清除時才跑；`AfterCommitAsync`
則只要那一列存在就跑，只在乎真實變更的話，要自己看 `Changed`。

### 順序與失敗

適用於該集合的攔截器按註冊順序執行，核心的排在最前。同一次寫入裡，掛鉤的順序是：每個攔截器的
`BeforeWriteAsync`、寫入交易（每個 `InTransactionAsync` 在裡面）、每個 `AfterCommitAsync`，最後是
`IItemChangeListener` 的通知。下面的刪除處理器路徑上，是處理器自己通知監聽者，所以 `AfterCommitAsync`
排在那次通知之後。

`BeforeWriteAsync` 或 `InTransactionAsync` 丟出 `StruoDomainException` 或 `QueryException`，回應
的是它自己帶的 4xx，跟丟出驗證錯誤一樣。核心自己的例外對應方式跟其他地方相同：`ConflictException`
是 `409`，`PermissionDeniedException` 是 `401` 或 `403`，`QueryException` 是 `400`；API 沒有對應的例外
才是 `500`。`InTransactionAsync` 裡丟例外會讓寫入回滾。

`AfterCommitAsync` 不一樣，因為這時寫入已經 commit 了。某個攔截器丟例外，後面的 `AfterCommitAsync`
掛鉤就不跑，監聽者仍然會被通知，然後例外才傳回給呼叫端。呼叫端看到錯誤，但資料其實已經存下來了。它
的 `CancellationToken` 與請求無關，客戶端中途斷線不會讓它提早結束。拿它做清快取之類的事，並且避免讓
它丟例外。

### 在驗證順序裡的位置

建立時，body 的檢查順序是：權限、`Creatable` 檢查、body 形狀、`MaxLength` 與各欄位的驗證器，然後是你
的 `BeforeWriteAsync`，最後才是必填檢查。純量的必填欄位在你的掛鉤之後才檢查，所以掛鉤可以把它填起
來，下面的 slug 範例靠的就是這點。列表型別的必填欄位在掛鉤之前就檢查了，攔截器填不了它。

掛鉤設進去的值不會再被 `MaxLength` 或欄位驗證器檢查，所以掛鉤得自己產出合法的值；只有純量必填檢查
看得到它。

### 哪些不會跑攔截器

- 專屬端點，例如 `POST /api/files` 與 `POST /api/users`，不經過 `ItemService`，不跑攔截器。核心對
  `file`、`user` 的規則只在走通用路徑時才會生效。
- 級聯清除所帶走的子列，刪除時不跑攔截器。它們仍然會到達 `IItemChangeListener`，要對它們做反應就
  在那裡。

### Slug 範例

這個攔截器用標題替新的 `article` 填 slug。`Article` 是你自己的集合類別，有 `Title` 與可設定的
`Slug`。

```csharp
using System.Text.RegularExpressions;
using Struo.Application.Query;

public sealed class ArticleSlugInterceptor : IItemWriteInterceptor
{
    public bool AppliesTo(string collection) => collection == "article";

    public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation != ItemWriteOperation.Create) return Task.CompletedTask;

        var article = (Article)context.Entity;
        if (string.IsNullOrWhiteSpace(article.Slug))
            article.Slug = Regex.Replace(article.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return Task.CompletedTask;
    }
}
```

註冊寫在你自己的 `Program.cs`，`AddStruoCms()` 之後。註冊是累加的：同一個集合可以有任意數量的攔截
器。

```csharp
builder.AddStruoCms();
builder.Services.AddScoped<IItemWriteInterceptor, ArticleSlugInterceptor>();
```

## 刪除處理器 `ICollectionDeleteHandler`

攔截器是替刪除加料，不能取代它。集合的刪除若帶著通用流程表達不了的副作用，例如要移除一個 blob，就
改用 `ICollectionDeleteHandler`。核心的 `file` 集合用的就是這個。

它有 `AppliesTo(string collection)`、`TrashAsync(string id, CancellationToken ct)` 與
`PurgeAsync(string id, CancellationToken ct)`，後兩者在那一列存在時回傳 true。選用的
`IsValidId(string id)` 對不可能是列 id 的字串回 false，刪除就直接回找不到，不去讀取。註冊的處理器
裡，第一個適用的勝出；核心的 `file` 處理器是在 `AddStruoCms()` 裡註冊的，所以你替 `file` 另外註冊
的處理器永遠不會被執行。

`ItemService` 先檢查權限與那一列是否存在，執行攔截器的 `BeforeWriteAsync`，再呼叫處理器。從這裡開
始交易與變更通知都歸處理器管，所以不會跑 `InTransactionAsync`。處理器回傳 true 時，
`AfterCommitAsync` 會以 `Changed` 為 true 執行。處理器沒辦法回報「這次丟進垃圾桶什麼都沒做」。

處理器取代了整個丟進垃圾桶或清除的動作，所以它必須自己在自己的交易裡移除那一列，並通知變更監聽者；
`ItemService` 之後不會再做這兩件事。

```csharp
public sealed class AttachmentDeleteHandler(
    IItemRepository repository, IItemChangeNotifier notifier, IMyBlobs blobs) : ICollectionDeleteHandler
{
    public bool AppliesTo(string collection) => collection == "attachment";

    public bool IsValidId(string id) => Guid.TryParse(id, out _);

    public async Task<bool> TrashAsync(string id, CancellationToken ct) => await PurgeAsync(id, ct);

    public async Task<bool> PurgeAsync(string id, CancellationToken ct)
    {
        var removed = await repository.InTransactionAsync(
            () => repository.DeleteAsync("attachment", id, ct), ct);
        if (!removed) return false;

        await notifier.NotifyAsync([new ItemChange("attachment", id, ItemChangeKind.Purged)], CancellationToken.None);
        await blobs.RemoveAsync(id, ct);
        return true;
    }
}
```

`IItemRepository` 與 `IItemChangeNotifier`（命名空間 `Struo.Application.Changes`，連同 `ItemChange`、
`ItemChangeKind`）是核心自己的服務。`IMyBlobs` 代表你自己的儲存客戶端。處理器用同樣的方式註冊：

```csharp
builder.Services.AddScoped<ICollectionDeleteHandler, AttachmentDeleteHandler>();
```

## 不能建立的集合

`[CmsCollection(Creatable = false)]` 讓通用的項目 API 在檢視 body 之前，就對該集合的建立回 `400`。
核心把它設在 `file` 與 `user`，它們的列來自 `POST /api/files` 與 `POST /api/users`。旗標本身見
[第 5 章：集合](05-collections.md)，`/api/schema` 怎麼以 `creatable` 露出它，見
[第 13 章](13-rest-endpoints.md)。

## 檔案儲存：`IFileStorage`

`file` 集合背後的位元組儲存是 `IFileStorage`。核心附帶 local 與 S3 兩個後端，由 `Struo:Files:Backend`
選擇。要把檔案存到別處，就把後端設成 `custom`，並在 `AddStruoCms()` 之後註冊你的實作。這個模式下核心不會
自己建任何儲存，沒註冊時啟動會失敗，訊息會點出那個鍵。記住契約：找不到 key 要丟 `FileBlobNotFoundException`，
不能丟後端自己的例外；`ListAsync` 要串流每一個 blob；`Describe()` 是 `files:sweep` 印出的那一行，不能含
任何憑證。下面是 Azure Blob 儲存的最小示意（`Azure.Storage.Blobs` 套件屬於你的 fork，不屬於核心）。它沒有測試過：
容器必須事先存在，也沒有做預簽章網址（SAS）：

```csharp
using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Struo.Application.Files;
using Struo.Domain.Query;

public sealed class AzureBlobFileStorage(BlobContainerClient container) : IFileStorage
{
    public bool SupportsPresignedUrls => false;

    public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
        await container.GetBlobClient(key).UploadAsync(
            content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            throw new FileBlobNotFoundException(key);   // 契約：找不到 key 時，不能丟出後端自己的例外
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);

    public async IAsyncEnumerable<StoredBlob> ListAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
            yield return new StoredBlob(blob.Name, blob.Properties.LastModified?.UtcDateTime ?? DateTime.MaxValue);
    }

    // files:sweep 會印出這一行：只寫容器與帳號，絕不放金鑰或連線字串。
    public string Describe() => $"azure blob, container {container.Name}, account {container.AccountName}";
}
```

在設定裡把 `Struo:Files:Backend` 設成 `custom`（例如 `Struo__Files__Backend=custom`），並在 `Program.cs` 於
`AddStruoCms()` 之後註冊這個儲存：

```csharp
builder.AddStruoCms();
builder.Services.AddSingleton(new BlobContainerClient(builder.Configuration["Azure:Storage:ConnectionString"], "media"));
builder.Services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
```

內建後端的行為與 `files:sweep`，見[第 15 章](15-files-and-media.md)。

## 自己的錯誤：`StruoDomainException`

fork 自己、給客戶端看的錯誤，繼承 `StruoDomainException`，它帶著代碼、400 到 499 的狀態與訊息。REST 與
GraphQL 都原樣回傳。怎麼定義，以及訊息與代碼的規則，見[第 12 章](12-rest-conventions.md#自己的錯誤代碼)。

## GraphQL schema：`ConfigureGraphQl`

第三個接口不是介面，而是 builder 上的一個掛鉤。`ConfigureGraphQl` 會在核心 schema 註冊之後、伺服器建立
之前，收到 HotChocolate 自己的 `IRequestExecutorBuilder`，fork 因此可以加查詢與變更欄位，以及自己的
物件型別。它直接露出 HotChocolate 的 API；能做什麼，就看 HotChocolate 允許什麼。

```csharp
using HotChocolate.Types;

public sealed record BuildInfo(string Version);

[ExtendObjectType("Query")]
public sealed class BuildInfoQuery
{
    public BuildInfo GetBuildInfo() => new("1.4.2");
}
```

在 `Program.cs` 透過 `AddStruoCms()` 的回呼註冊：

```csharp
builder.AddStruoCms(cms => cms.ConfigureGraphQl(graphQl => graphQl
    .AddTypeExtension<BuildInfoQuery>()));
```

之後 `{ buildInfo { version } }` 就和產生出來的欄位一起由 `/graphql` 提供。

你加在 `Mutation` 上的欄位，要自己保護。CMS 的登入檢查與權限是掛在它產生出來的欄位上，不是掛在
`Mutation` 型別上，所以 fork 自己的欄位，除非自己檢查，匿名呼叫端也能執行。你的動作也是在核心之後執行，
所以你在 builder 上改的設定（introspection、成本與深度規則、相依性注入的範圍）會取代核心的。CSRF 防護是
HTTP 中介層，照樣適用。要求已登入呼叫端的 mutation 長這樣：

```csharp
[ExtendObjectType("Mutation")]
public sealed class BuildInfoMutation
{
    public string RefreshBuildInfo([Service] IHttpContextAccessor accessor) =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? "refreshed"
            : throw new GraphQLException("Sign in to run this mutation.");
}
```

名稱不能和產生出來的 schema 衝突；衝突時啟動會失敗，訊息是 `GraphQL name collision`。查詢深度與成本的
上限是 `GraphQl:MaxExecutionDepth`、`GraphQl:MaxFieldCost` 與 `GraphQl:MaxTypeCost`，比較重的 fork 查詢
可能需要調高。見[第 14 章：GraphQL API](14-graphql.md)。

## 接下來

註冊的位置、搜尋提供者、寫入通知、寫入攔截器與刪除處理器、檔案儲存、領域錯誤跟 GraphQL 的掛鉤就談到這裡；下一章，
[第 19 章：後台客製化](19-admin-customization.md)，回到前端，談後台的哪些外觀跟行為只
靠設定就能換掉，哪些才真的需要碰 `frontend/src`。
