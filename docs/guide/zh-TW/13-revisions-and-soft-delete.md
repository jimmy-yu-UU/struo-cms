# 13. 版本紀錄與軟刪除

有兩項獨立、選用啟用的寫入端功能，都內建在一般的 `ItemService` 之中：一是逐集合 (collection) 的版本
紀錄，會為每一次建立/更新/刪除/還原/回復操作留下快照；二是逐集合的軟刪除，預設會把一列資料移入回收桶，
而不是直接銷毀。這兩項功能對任何集合都不是預設啟用的，而且彼此是獨立的選用項目——一個集合可以兩者皆無、
只有其中一個，或(原則上)兩者兼具。第 8 章與第 9 章已經記載了它們的傳輸層外觀(`deleted=`、
`.../revisions` 端點)；本章涵蓋的是機制本身、一份快照實際包含什麼、還原會恢復什麼、不會恢復什麼，以及
當一個集合兩項功能兼具時，兩者如何互動。

## 逐集合啟用版本紀錄

`[CmsCollection("X", Revisions = true)]`
(`src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs:29-35`)就是整個選用啟用的方式：
該集合上每一次成功的建立/更新，都會把該項目寫入後狀態的完整快照附加到框架的 `revisions` 資料表，而任何
過去的版本之後都能透過還原被重新套用。宣告它不需要額外成本——不用實作介面，也不用在實體上新增欄位——因為
版本紀錄的資料列存放在一個共用資料表中，以 `(collectionName, itemId, revisionNumber)` 為鍵，而不是掛
在實體本身上。

**七個核心 framework 集合(`language`、`permission`、`role`、`user`、`userRole`、`file`、
`mediaFolder`)沒有一個宣告 `Revisions = true`**——已直接對照原始碼確認(`src/` 底下完全找不到任何
`Revisions = true` 的出現)。範例 Blog 的 `Article` 集合(第 16 章)則有：
`[CmsCollection("Article", ..., Revisions = true)]`，而且它還額外實作了 `ISoftDeletable`——下方的
走查會用它來展示完整的建立/更新/列表/檢視/還原循環，針對一個真實、正在執行的集合，其運作方式與任何一個
以同樣方式選用啟用的集合完全相同。這個範例出貨時是**停用**的(`Struo:ContentAssemblies` 為空，
`Struo.Api` 沒有專案參照指向它)——第 16 章涵蓋如何啟用它，本走查假設這件事已經完成。

## 快照包含什麼，何時被擷取

一份快照是由 `RevisionSnapshotBuilder.BuildAsync`
(`src/Struo.Application/Query/RevisionSnapshotBuilder.cs`)從**已經持久化**的實體建構出來的，就在
該資料列本身寫入之後，而且刻意是完整保真的——沒有 RBAC 欄位過濾、沒有隱藏欄位跳過——因為一次還原必須能
夠恢復整個項目狀態，無論之後是誰在檢視這筆版本紀錄。它會組出一個單一的 JSON 物件，內容包含：

- `id`，以及對於一個 `AuditableEntity` 而言的 `version`(僅供參考而存在；在還原重新套用快照之前會再度
  被剝除——見下方)。
- 每一個自身的 `[CmsField]`，**除了**系統管理欄位(`IsSystem`)與可翻譯欄位(後者改為存放在
  `translations` 之下)——一個 `Json` 介面欄位的原始字串，會被解析回一個結構化的 `JsonElement`，因此
  它會序列化成 JSON，而不是一個帶引號的字串。
- 每一個多對一關聯的外鍵 id，以其 camelCase 名稱表示(例如 `folderId`)——這些是透過 `[CmsRelation]`
  宣告的，而不是 `[CmsField]`，因此上面那個自身欄位的處理並不會涵蓋它們。
- 每一個多對多關聯，以該關聯名稱下的一個**排序過的 id 陣列**表示(若該關聯宣告了排序屬性，則依此排序)。
- `translations`：`{ locale: { camelField: value } }`，涵蓋該項目擁有翻譯資料列的每一個語言——是完整
  的附屬資料表狀態，而不只是查詢當下生效的那個語言。

這正好就是 `ItemService.UpdateAsync` 所接受的請求本文形狀——這正是重點所在：一份快照在還原時，會走過
*正常的寫入路徑*重新套用，而不需要一個特殊處理的還原程序。建立一個 `article` 並檢視它的第一筆版本紀錄，
可以即時看到這個形狀——`categoryId`(一個 M2O 外鍵，不在 `[CmsField]` 之中)與 `tags`(一個 M2M id
陣列)即使這個項目兩者都未設定，仍然存在；而 `translations.en` 帶有完整的附屬資料表資料列：

```
$ curl -s -X POST http://localhost:5221/api/items/article -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt -d '{
      "status": "draft",
      "internalNote": "secret-note-v1",
      "translations": { "en": { "title": "Original Title", "body": "Original body text.", "internalSlug": "original-slug-v1" } }
    }'
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":0,"status":"draft", ...}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions/1"
{"success":true,"data":{"revisionNumber":1,"operation":"create","createdAt":"2026-07-29T08:47:18.943677","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b","snapshot":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":0,"status":"draft","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"categoryId":null,"tags":[],"translations":{"en":{"title":"Original Title","body":"Original body text.","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null}}}}}
```

(注意 `internalNote`——上面建立請求中設定的——並未出現在這份快照回應的任何地方，`translations.en`
也沒有 `internalSlug` 這個鍵。這兩個欄位都是 `Hidden`；原因見下方的「遮蔽」一節，那裡也會證明這個值
確實有被擷取下來。)

**擷取發生在與它所描述的寫入相同的資料庫交易之中**
(`ItemService.CreateAsync`/`UpdateCoreAsync`，`src/Struo.Application/Query/ItemService.cs`)：

| 操作 | 擷取位置 | 記錄的 `operation` 值 |
|---|---|---|
| 建立 | 在 `CreateAsync` 的 `repository.InTransactionAsync` 之中，緊接在 M2M/翻譯同步之後(`ItemService.cs:129-133`) | `"create"` |
| 更新 | 在 `UpdateCoreAsync` 的 `repository.InTransactionAsync` 之中，相同位置(`ItemService.cs:214-218`) | `"update"`(或 `"revert"`——見下方) |
| 移入回收桶(軟刪除) | 透過 `CaptureRevisionAsync`，與那次原子性回收桶 UPDATE 位於同一個交易之中，且僅在它確實影響到某一列資料時才會執行(`ItemService.cs:257-297`) | `"delete"` |
| 還原(回收桶) | 透過 `CaptureRevisionAsync`，與那次原子性還原 UPDATE 位於同一個交易之中(`ItemService.cs:320-341`) | `"restore"` |
| 還原(版本紀錄) | 透過 `UpdateCoreAsync` 以一般更新的形式重新套用該快照(見下方)，而這本身又會擷取一筆新的快照 | `"revert"` |

把擷取動作放進與該次寫入相同的交易之中，代表一筆版本紀錄絕不可能存在於一次本身已經回滾的寫入之下——而且
對於移入回收桶/還原而言，這個擷取動作還取決於底層那個原子性的 `WHERE deletedat IS NULL`/`IS NOT NULL`
更新是否確實影響到某一列資料，因此兩個對同一列資料競速的並行回收桶/還原呼叫，不會各自記錄下一筆偽造的
重複版本紀錄(與第 9 章樂觀並行控制一節針對一般寫入路徑所描述的，是同一種能防止 TOCTOU 的模式)。

`SqlSugarRevisionStore.CaptureAsync`
(`src/Struo.Infrastructure/Revisions/SqlSugarRevisionStore.cs`)會為該
`(collection, itemId)` 組合把 `RevisionNumber` 指定為 `max(existing) + 1`——之所以不會有競速問題，是
因為到擷取執行的時候，單一項目的寫入路徑早已被序列化——並從目前的請求標記 `CreatedAt`/`CreatedBy`。
`revisions` 資料表本身(`src/Struo.Infrastructure/Revisions/Revision.cs`)是一個純粹的框架資料表，
**不是**一個 `[CmsCollection]`——它永遠無法透過一般的 item API 被瀏覽或 CRUD——只能附加(資料列只會被
插入，永遠不會被更新)，也沒有自己的 `ISoftDeletable`/稽核形狀。

## 快照中隱藏欄位的遮蔽

因為一份快照會擷取一個項目的**全部**內容，包括任何標示 `Hidden` 的欄位(第 12 章)，一份交給外部呼叫端
的快照絕不能洩漏其中任何一個。`RevisionSnapshotRedactor.RedactHidden`
(`src/Struo.Application/Query/RevisionSnapshotRedactor.cs`)會產生一份經過遮蔽的**副本**——省略任何
其欄位中介資料為 `Hidden` 的頂層鍵，並且在 `translations.{locale}` 之內，省略任何同時屬於 `Hidden` 與
`Translatable` 的欄位鍵——其他每一個值(巢狀物件、陣列、數字、布林值、null)則原封不動地複製過去。這種
遮蔽**只**套用在外部的單筆版本紀錄讀取路徑上(`ItemService.GetRevisionAsync`，REST 的
`GET .../revisions/{n}` 與 GraphQL 的 `{collection}Revision`)——`RevertAsync` 則刻意直接從存放區讀取
**原始、未遮蔽**的快照，因為一次還原必須能夠恢復一個 `Hidden` 欄位的值(例如一個有版本紀錄的集合，若有
一個外形像憑證的隱藏欄位，一次還原必須真正恢復該憑證，而不是把它清空)。因此一個隱藏值只會透過還原路徑
的效果(改寫現行資料列)離開這個行程，絕不會透過一次快照回應本文離開。

`Article.InternalNote`(頂層)與 `ArticleTranslation.InternalSlug`(逐語言)都是 `Hidden`。把上面
那篇文章更新成不同的 `internalNote`/`internalSlug` 值之後，再重新讀取版本紀錄 1 的快照，仍然看不到
這兩個鍵——遮蔽對每一筆過去的版本紀錄都一視同仁，而不只是對最新的那一筆：

```
$ curl -s -X PUT http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27 \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt -d '{
      "status": "published",
      "internalNote": "secret-note-v2-CHANGED",
      "translations": { "en": { "title": "Updated Title", "body": "Updated body text.", "internalSlug": "updated-slug-v2-CHANGED" } }
    }'
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":1,"status":"published", ...}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions/2"
{"success":true,"data":{"revisionNumber":2,"operation":"update","createdAt":"2026-07-29T08:47:40.66141","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b","snapshot":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":1,"status":"published","publishedAt":null,"heroImageId":null,"regions":[],"audiences":[],"keywords":[],"attributes":null,"meta":{},"gallery":[],"faqs":[],"categoryId":null,"tags":[],"translations":{"en":{"title":"Updated Title","body":"Updated body text.","seoTitle":null,"seoMetaDescription":null,"seoOgImageId":null}}}}}
```

兩份被遮蔽的快照都沒有顯示 `internalNote` 或 `translations.en.internalSlug`——但這些值確實有被真正
擷取下來，而不是被丟棄：還原到版本紀錄 1(下一節)會把 `internalNote` 恢復成 `"secret-note-v1"`，
把 `internalSlug` 恢復成 `"original-slug-v1"`，這一點已直接對照資料庫確認，即使這兩個值從未透過任何
API 回應曝光過。

## 列出、檢視與還原版本紀錄

**REST**(第 9 章，完整端點表)——`GET /api/items/{collection}/{id}/revisions`(列表，最新在前，
只有 metadata：`revisionNumber`、`operation`、`createdAt`、`createdBy`)、`GET .../revisions/{n}`
(單筆版本紀錄，附上經過遮蔽的 `snapshot`)、`POST .../revisions/{n}/revert`(套用它)。這三者分別都
需要該集合一般的 `CanRead`/`CanWrite` 授權——版本紀錄並沒有專屬的額外權限層級。列出目前為止 `article`
的兩筆版本紀錄(最新在前)：

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions"
{"success":true,"data":[{"revisionNumber":2,"operation":"update","createdAt":"2026-07-29T08:47:40.66141","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b"},{"revisionNumber":1,"operation":"create","createdAt":"2026-07-29T08:47:18.943677","createdBy":"019fa8b2-4d09-7155-b641-2c3e2519233b"}]}
```

**GraphQL**(第 10 章，`RevisionResolvers.cs`)——當一個集合宣告 `Revisions = true`，
`StruoTypeModule` 會加入 `{collection}Revisions(id: ID!): [Revision!]!`、`{collection}Revision(id:
ID!, revisionNumber: Int!): Revision`，以及一個 `revert{X}(id: ID!, revisionNumber: Int!): X`
mutation，全部都不需要任何特定集合的 GraphQL 程式碼即可生成——共用的 `Revision` 型別是
`{ revisionNumber, operation, createdAt, createdBy, snapshot }`。透過內省 `article` 自己生成的
schema 確認——`Query` 上有 `articleRevisions`/`articleRevision`，`Mutation` 上有
`revertArticle`，與一般生成的 `article`/`articles`/`createArticle`/`updateArticle`/`deleteArticle`/
`restoreArticle` 並列——並實際呼叫了那個生成出來的查詢欄位：

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"{ articleRevisions(id: \"019fad0e-8904-7ac7-a20e-796f1c50ea27\") { revisionNumber operation createdAt } }"}'
{"data":{"articleRevisions":[{"revisionNumber":2,"operation":"update","createdAt":"2026-07-29T08:47:40.66141Z"},{"revisionNumber":1,"operation":"create","createdAt":"2026-07-29T08:47:18.943677Z"}]}}
```

這一切都沒有任何特定集合的 resolver 程式碼——它純粹是從實體 `[CmsCollection]` attribute 上的
`Revisions = true` 生成出來的，與上面的 REST 端點完全一致。

**管理後台 Drawer**——管理後台 SPA 的 `RevisionHistoryDrawer.vue` 元件
(`frontend/src/components/revisions/RevisionHistoryDrawer.vue`)透過
`itemsApi.listRevisions`/`getRevision`/`revert`(`frontend/src/api/itemsApi.ts:63-70`)呼叫上面的
REST 端點(而非 GraphQL)——一個列表檢視、一個快照詳情檢視(`RevisionSnapshotView.vue`)，以及一個接到
項目表單的還原動作。它只是上述三個 REST 端點之上的一層薄客戶端；除了 `ItemService` 已經強制執行的內容
之外，它本身沒有任何伺服器端行為。

## 還原會恢復什麼，不會恢復什麼

`RevertAsync`(`ItemService.cs:366-388`)會讀取目標版本紀錄的原始快照，剝除其中的 `version` 鍵(這樣
還原就不會回顯一個現已過期的樂觀並行控制 token，因而對目前這一列資料產生虛假的 `409`)，並透過與一般
`PUT` 完全**相同**的 `UpdateCoreAsync` 路徑重新套用結果，只是標記為 `operation = "revert"` 而非
`"update"`。在上面那次把 `status` 改成 `"published"`、每個欄位都改成各自 `"…-CHANGED"` 值的更新之後，
把 `article` 還原回版本紀錄 1(它原始的 `create` 快照)：

```
$ curl -s -X POST http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions/1/revert \
    -H "X-Struo-CSRF: 1" -b cookies.txt
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":2,"status":"draft", ...}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27"
{"success":true,"data":{"id":"019fad0e-8904-7ac7-a20e-796f1c50ea27","version":2,"status":"draft", ...,"translations":{"en":{"title":"Original Title","body":"Original body text.", ...}}}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/article/019fad0e-8904-7ac7-a20e-796f1c50ea27/revisions"
{"success":true,"data":[{"revisionNumber":3,"operation":"revert","createdAt":"2026-07-29T08:47:59.288629", ...},{"revisionNumber":2,"operation":"update", ...},{"revisionNumber":1,"operation":"create", ...}]}
```

`status` 與 `translations.en.title`/`body` 都回到它們版本紀錄 1 的值，`version` 向前推進
(1 → 2，而不是回到 0)，版本紀錄列表則成長到三筆，最新的一筆是 `"revert"`——歷史紀錄 1/2/3 全部
仍然存在。而且，直接從資料庫讀取(絕不透過任何 API，因為兩者都是 `Hidden`)——證明一個 `Hidden` 欄位
的值確實被還原恢復了，而不是被單純保留原樣或清空：

```
$ docker exec struo-postgres psql -U struo -d struo -c \
    "select status, internalnote from articles where id='019fad0e-8904-7ac7-a20e-796f1c50ea27';"
 status | internalnote
--------+----------------
 draft  | secret-note-v1

$ docker exec struo-postgres psql -U struo -d struo -c \
    "select title, internalslug from article_translations where articleid='019fad0e-8904-7ac7-a20e-796f1c50ea27' and locale='en';"
      title      |   internalslug
------------------+-------------------
 Original Title   | original-slug-v1
```

兩個隱藏的值都回到了版本紀錄 1 的內容(`"secret-note-v1"` / `"original-slug-v1"`)——不是中途那次更新
所設定的 `"…-CHANGED"` 值，也不是 null。以下是直接因為重複使用一般更新路徑而衍生出的後果：

- 一次還原是**附加**一筆新的 `"revert"` 版本紀錄，而不是刪除或倒轉歷史——時間軸只會向前累加；除了再還原
  到更早的一個編號之外，沒有其他方式能「撤銷一次還原」。
- 一次還原就其他任何目的而言，**就是**一次普通的寫入：它會經過相同的 `CanWrite`(若為 `AdminOnly` 則
  另需超級管理員)檢查、相同的樂觀並行控制機制，並產生與任何其他更新相同的 `200`(附帶更新後項目)回應
  形狀。
- 在一次還原期間的多對多同步，**能夠容忍**一列自快照擷取以來已被移入回收桶的目標資料列
  (`includeDeleted: operation == "revert"`，`ItemService.cs:210-212`)——其他每一條寫入路徑對此都
  維持嚴格。因此一次還原到一個參照著某個已移入回收桶關聯資料列的快照，會恢復該參照，而不是直接失敗。
- 一次還原恰好恢復快照所擷取的內容：自身欄位、M2O/M2M 關聯狀態，以及所有語言的翻譯。它**不會**恢復
  建構器基於設計而排除的任何東西——系統管理欄位，或該項目的軟刪除狀態(`DeletedAt`/`DeletedBy` 完全
  不是快照的一部分，因為 `RevisionSnapshotBuilder` 只走訪 `[CmsField]`/關聯/翻譯)——而且它也不會回溯
  改變任何*其他*項目的狀態，即使是被還原項目所關聯到的項目也一樣。

## 軟刪除

### 以 `ISoftDeletable` 選用啟用

`ISoftDeletable`(`src/Struo.Domain/Auditing/ISoftDeletable.cs`)是一個不依賴任何套件的標記介面——
`DateTime? DeletedAt` + `Guid? DeletedBy`——與 `IAuditable`「不需要任何框架 attribute」的設計理念
相同。一個實作它的實體會被軟刪除(其 `DeletedAt`/`DeletedBy` 被標記)，而不是被一般的 `DELETE` 直接
物理移除；`DeletedAt` 為 `null` 代表這一列資料是現行有效的。`File`
(`src/Struo.Infrastructure/Files/File.cs`)是**唯一**實作它的框架集合——第 11 章是具體、針對檔案的
走查(上傳/移入回收桶/還原/清除)；本節則是一般性的機制。

### 全域查詢過濾器

這道底線只會在 `SqlSugarClient` 建構時註冊一次，套用到連線範圍所建立的每一個內部內容
(`SqlSugarClientFactory.cs:140-150`)：

```csharp
db.QueryFilter.AddTableFilter<ISoftDeletable>(e => e.DeletedAt == null);
```

這會套用到**每一個**針對 `ISoftDeletable` 實體的 `Queryable<T>`，不需要任何逐呼叫點的程式碼——列表、
取得單筆、深度展開、跨關聯 id 解析、M2M 存在性檢查，以及入站的 `OnDelete.Restrict` 檢查(第 7 章)都
會靜默地預設排除一列已被移入回收桶的資料。一次真正需要已移入回收桶資料列的讀取(`?deleted=only|with`、
還原、清除)會針對那單一次查詢明確清除這個過濾器(`.ClearFilter<ISoftDeletable>()`，
`SqlSugarItemRepository.cs`)，而不是讓這道底線在全域層級變成可選擇退出——預設移入回收桶，是失敗時該
偏向的安全方向。

移入回收桶/還原這兩個寫入動作本身(`SoftDeleteAsync`/`RestoreAsync`，
`src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs:580-663`)都以單一原子性的
`UPDATE ... WHERE deletedat IS [NOT] NULL` 執行，而不是先讀取再寫入——因此把一列已經在回收桶中的資料
再次移入回收桶(或還原一列已經是現行有效的資料)，在 SQL 層級是一個無操作(影響零列資料)，而不是一場
兩個並行呼叫端都可能各自「贏得」的競賽。對於一個 `AuditableEntity` 而言，同一個 `UPDATE` 也會推進
`Version`(`version = version + 1`)，因此樂觀並行控制的歷史會持續前進，一個持有過期 `version` 的
客戶端，對一列已被移入回收桶或已還原的資料，會正確地收到 `409`。

### `DELETE` 相對於 `?purge`，以及還原

`ItemsController.Delete` 對於任何 `meta.SoftDelete` 為 true 的集合，預設會移入回收桶(軟刪除)；
`?purge=true` 則會強制改為一次永久的硬刪除，走過該集合實際的刪除管線(串聯/限制檢查、關聯清理)，而不是
軟刪除的 UPDATE。一個**完全沒有**軟刪除層級的集合(七個框架集合中的六個)無論查詢字串為何，一律都會
清除——沒有部分/軟性狀態可以停留。`POST .../restore` 會以相同方式清除 `DeletedAt`，適用於任何可軟刪除
的集合；第 11 章即時展示了這整套端到端流程，針對 `file`(移入回收桶 → `deleted=only` → 還原 → 回到
一般列表中；接著第二個檔案被移入回收桶並透過 `?purge=true` 永久清除，確認即使在 `?deleted=with` 下
也不存在)。

### `deleted=exclude|only|with` 過濾器，以及誰可以使用它

`DeletedFilter`(`src/Struo.Domain/Query/DeletedFilter.cs`，第 8 章)有三個值——`Exclude`(預設值，
與上方的全域底線一致)、`Only`、`With`——會從 `?deleted=` 讀取，同時適用於 `GET` 列表/查詢 action 與
單筆項目的 `GET`。要求任何非 `Exclude` 的值，需要該集合的**刪除**權限，而不僅僅是讀取權限——
`DeletedAccessGuard.EnsureCanViewDeleted`(`src/Struo.Application/Query/DeletedAccessGuard.cs`)由
`ItemsController` 本身強制執行(GraphQL resolver 亦然，第 10 章)，而不是在
`ItemService.QueryAsync`/`GetAsync` 內部——後兩者永遠只檢查 `CanRead`。這是一道刻意設計得更嚴格的關卡：
查看哪些資料列已被移入回收桶，被視為比查看現行資料集更敏感，因為一列已刪除資料的身分本身，就可能是一個
普通讀取者不該擁有的資訊。第 8 章即時展示了針對 `file` 的即時 PostgreSQL 三向切分
(`exclude`/`only`/`with`)；第 11 章的移入回收桶/還原/清除走查，則針對本章新建立的資料練習了相同的
過濾器值。

## 回收桶與版本紀錄之間的互動

這兩項功能都是獨立的選用項目，而且**沒有任何核心框架集合同時具備兩者**——`file` 可軟刪除但沒有版本
紀錄；沒有任何框架集合宣告 `Revisions = true`(見上方)。範例 Blog 的 `article` 集合(第 16 章)確實
兩者兼具——它實作了 `ISoftDeletable`，也宣告了 `Revisions = true`——但以下這種特定的回收桶/還原/版本
紀錄互動，是根據原始碼描述的，而不是在本章即時演練過的，因為它落在上面所展示的建立/更新/列表/檢視/還原
循環之外：

- 當被移入回收桶/還原的集合**同時**符合 `meta.SoftDelete` 與 `meta.Revisions` 皆為 true 時，
  `ItemService.DeleteAsync`/`RestoreAsync` 會呼叫 `CaptureRevisionAsync`(`ItemService.cs:297`、
  `:341`)——一筆 `"delete"`/`"restore"` 版本紀錄會被記錄在與那次原子性移入回收桶/還原 `UPDATE`
  完全相同的交易之中，使用上方描述的同一個受影響列數關卡(因此把一列已經在回收桶中的資料再次移入回收桶
  這種無操作，同樣不會記錄一筆偽造的版本紀錄)。
- 對一個同時可軟刪除且有版本紀錄的集合執行**清除**(`?purge=true`)，完全不會經過這條路徑——它是透過
  該集合一般的刪除管線進行的硬刪除，而不是軟刪除 `UPDATE`，因此一次清除不會(僅針對移入回收桶才會)
  記錄一筆 `"delete"` 版本紀錄。`IRevisionStore.DeleteForItemAsync` 的存在，正是為了移除一個被清除
  項目的整組版本紀錄歷史，這樣一個已被永久刪除的項目就不會留下孤兒、未經 RBAC 保護的快照歷史——它的
  預設實作刻意直接拋出例外，而不是靜默地什麼都不做，因此一個忘了接上這條路徑的存放區，會直接大聲失敗，
  而不是洩漏歷史。
- 還原一個已被移入回收桶且有版本紀錄的項目的歷史，機制上只是另一次 `UpdateCoreAsync` 呼叫——它本身
  並不會把該項目從回收桶中恢復(如上所述，`DeletedAt` 不是快照的一部分)，因此若目標是讓一個項目在某個
  過去的欄位狀態下完全恢復現行有效，一次針對目前已在回收桶中資料列的還原之後，仍然需要一次明確的
  `restore`。

## 接下來該去哪

- 第 8 章 [查詢 DSL](08-query-dsl.md)，涵蓋 `DeletedFilter`/`deleted=` 作為一般查詢 DSL 概念，並針對
  `file` 進行了即時驗證。
- 第 9 章 [REST API](09-rest-api.md)，涵蓋完整的 `.../revisions*` 與檔案移入回收桶/還原/清除端點表、
  狀態碼，以及路由限制。
- 第 10 章 [GraphQL API](10-graphql-api.md)，涵蓋一個有版本紀錄的集合會自動獲得的
  `{collection}Revisions`/`{collection}Revision`/`revert{X}` GraphQL 介面。
- 第 11 章 [檔案、媒體與圖片轉換](11-files-and-media.md)，涵蓋針對 `file` 的即時、具體移入回收桶/
  還原/清除走查——本章軟刪除一節以抽象方式描述的唯一一個框架集合。
- 第 12 章 [認證、SSO 與 RBAC](12-auth-and-rbac.md)，涵蓋 `RevisionSnapshotRedactor` 所讀取的
  `Hidden` 欄位旗標，以及本章這些防護所建立於其上的 `CanRead`/`CanWrite`/`CanDelete` 授權。
