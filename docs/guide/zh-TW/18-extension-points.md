# 18. 擴充點：搜尋提供者與寫入通知

呼叫端的 `search=` 該怎麼回答，資料寫入之後要不要順便做點什麼，核心把這兩件事各自留成
一個介面，fork 接上自己的實作就好，不必碰核心的查詢或寫入路徑本身。這一章談
`ISearchProvider` 與 `IItemChangeListener` 各自的契約、什麼時候被呼叫、失敗時會發生什
麼事，以及怎麼註冊。

`search=` 本身的語法與內建的 LIKE 掃描，見
[第 10 章：查詢：過濾、排序、分頁](10-query-basics.md)；一次寫入的版本紀錄與軟刪除語
意，見[第 9 章：版本紀錄與軟刪除](09-revisions-and-trash.md)。

## 兩個介面在架構裡的位置

`ISearchProvider` 與 `IItemChangeListener` 是[第 2 章：系統架構](02-architecture.md)「什
麼可以換、什麼不能換」列出的其中兩個讓出去的介面，跟 `IFileStorage` 站在同一層。核心只
內建一個永遠不處理搜尋的 `NullSearchProvider`，以及一個負責派送、但預設沒有任何監聽者
的 `ItemChangeNotifier`；fork 要接手，就是實作介面本身、註冊進去，不用改核心一行程式
碼。

## 搜尋提供者 `ISearchProvider`

`ISearchProvider` 是搜尋這一側的擴充點：fork 接上 Meilisearch、Elasticsearch、
PostgreSQL 全文搜尋，或任何其他引擎，讓 `search=` 改由這個引擎回答。核心換到的是一組乾
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

這個例外的訊息可能帶著內部主機名稱，呼叫端因此只看得到固定的通用訊息（見
[第 12 章：REST API 慣例](12-rest-conventions.md)），真正的原因只寫進伺服器端紀錄。想
優雅降級的提供者，也可以自己接住例外、回 `NotHandled`，退回內建的 LIKE 掃描，兩條路都
合法，這個介面不逼哪一種。

### 註冊與生命週期

核心自己的預設註冊用的是「沒人註冊過才註冊」，所以 fork 的註冊永遠贏，不管寫在
`AddStruoData()` 之前還是之後：之前贏，是因為核心的預設接著就不會生效；之後贏，是因為
後面註冊的才是最後被解析到的那個。`ISearchProvider` 跟下一節的 `IItemChangeListener`，
註冊都寫在一個 API 專案（`Struo.Api`）會參照到的組件裡即可，不需要另外列進
`Struo:ContentAssemblies`，那份清單只掃描 `[CmsCollection]` 型別，不是擴充點的登記
表。

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

註冊寫在 `AddStruoData()` 之後：

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

`ItemChange` 成員是 `Collection`、`Id`、`Kind`。`Kind` 的值與各自的發出時機見下一節的
表。

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
哪些現存的關聯列——每個 inbound 的 set-null 關聯多付一次型別化讀取，每個 inbound 的多
對多 junction 多付兩次——這幾筆額外的查詢是清除自己要付的成本，不是為了監聽者才多跑
的。

這個介面只負責發出通知，不負責確認下游真的收到：核心不會重試失敗的這一次呼叫，下游系統
會掛掉的 fork，健康檢查跟對帳要自己做。監聽者若自己又透過一般寫入路徑寫了別的東西——譬
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

## 接下來

搜尋提供者跟寫入通知就談到這裡；下一章，
[第 19 章：後台客製化](19-admin-customization.md)，回到前端，談後台的哪些外觀跟行為只
靠設定就能換掉，哪些才真的需要碰 `frontend/src`。
