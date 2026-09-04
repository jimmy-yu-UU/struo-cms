# 6. 國際化

## 兩個語言概念——不要混淆

StruoCMS 有兩個各自獨立的語言 (locale) 概念，兩者由完全不同的機制管理:

| 概念 | 控制的內容 | 真實來源 (source of truth) | 存放位置 |
|---|---|---|---|
| **內容語言** | 決定讀取/寫入鎖定的是可翻譯欄位的哪一個逐語言副本 (例如 `en` 版與 `zh-TW` 版的 `Title`) | `languages` 資料表 (一個真正的 `[CmsCollection]`，第 4 章) | 讀取時的 `?locale=` 查詢參數;寫入時，一個以語言為鍵值的 `translations` 物件 |
| **介面語言** | 決定管理後台 SPA 自身介面字串 (選單標籤、按鈕、驗證訊息) 顯示的語言 | `frontend/src/i18n` (vue-i18n，`legacy: false`)，透過 `UiLanguageSwitcher.vue` 切換，並保存在 `uiLocaleStore` 這個 Pinia store 中 | 完全在 client 端;絕不會送到 API，也絕不會碰到 `languages` 資料表 |

這兩者刻意互不相關:一位管理者可以在 `zh-TW` 介面外觀中瀏覽管理後台 SPA，同時並排編輯 `en` 與
`zh-TW` 的內容，反之亦然。本章的 `?locale=`/`translations` 機制，對管理後台 SPA 自身標籤要顯示成
哪一種語言完全沒有影響。

## `languages` 資料表與 `GET /api/languages`

內容語言是框架自身 `Language` entity 的資料列 (`src/Struo.Infrastructure/Localization/Language.cs`)
——`Language` 本身就是一個真正的 `[CmsCollection]` (`Group = "System"`)，所以除了它自己的讀取端點
之外，也能像其他任何集合 (collection) 一樣，透過一般的 generic items API (`/api/items/language`)
編輯它。每一筆資料列帶有 `code` (例如 `"en"`、`"zh-TW"`)、`name`、`isDefault`、`enabled` 與
`sort`。一份全新安裝會透過 `LanguageSeeder`，剛好種入兩個已啟用的語言——`en` (`isDefault = true`)
與 `zh-TW`。

`GET /api/languages` (`LanguagesController`，`[Authorize(AuthenticationSchemes =
AuthSchemes.CookieOrBearer)]`——任何已驗證的呼叫端皆可，不限管理員) 只回傳*已啟用*的語言，投影為
`{ code, name, isDefault }`:

```
$ curl -s -b cookies.txt http://localhost:5221/api/languages
{"success":true,"data":[{"code":"en","name":"English","isDefault":true},{"code":"zh-TW","name":"繁體中文","isDefault":false}]}
```

`ILanguageProvider` (`src/Struo.Infrastructure/Localization/LanguageProvider.cs`) 會逐 scope 在
記憶體中快取這組已啟用的語言，並公開 `DefaultCode()` (帶有 `isDefault` 的那一列;找不到時退回第一個
已啟用的列;萬一完全沒有任何已啟用的列，再退回字面值 `"en"`) 與 `IsEnabled(code)` (不分大小寫)。
下文所描述的每一項語言有效性檢查——不論是讀取、寫入，或是翻譯物件的鍵值——都透過同一個
`ILanguageProvider` 解析，所以停用某一列語言 (或完全不設定 `isDefault`)，會立即對每一個帶有翻譯附屬資料表的
集合生效，不需要重新啟動。

## 讓欄位可翻譯

一個欄位要變成逐語言，做法是在**翻譯附屬資料表** entity (見下文) 的屬性上設定
`[CmsField(Translatable = true)]`——絕不是設在父 entity 本身上。`Translatable` 欄位會被父資料列的
`Required` 檢查跳過 (`ItemDeserializer.Deserialize` 會先過濾掉 `!f.Translatable`，才驗證
`Required`)，而是改在附屬資料表同步邏輯內部逐語言驗證 (下一節)。`MetadataScanner.ScanTranslations`
也會把每一個附屬資料表欄位摺疊進父層的 `CollectionMetadata.Fields` 清單中 (標記為
`Translatable = true`)，所以一個可翻譯欄位*確實*可以像其他任何自有欄位一樣，透過一般的查詢 DSL
白名單做篩選/排序 (`QueryValidator.Validate` 會從所有非 `Hidden` 的 `meta.Fields` 建構這份白名單，
沒有排除 `Translatable`)——只是它是在有效查詢語言下，透過
`RelationFilterResolver.IsTranslatableField`/`ResolveTranslatableIdsAsync`
(`src/Struo.Infrastructure/Query/RelationFilterResolver.cs`，第 7 章)，改對附屬資料表而非父資料列
解析。已透過即時環境驗證:即使完全沒有帶上 `?locale=`，用可翻譯的 `title` 篩選 `file` 依然成功
(此時有效語言會退回預設值 `DefaultCode()`，即 `ItemService.QueryAsync` 的 `queryLocale` 預設值):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Btitle%5D%5B_eq%5D=alpha-report"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

## 翻譯附屬資料表 entity (完整範例)

框架自身的 `File` 集合就是一個完整、真實存在的範例:它的 `title`/`alt` 欄位是可翻譯的，所以存放在
`FileTranslation` 上——一個一般的 SqlSugar entity，本身並不是 `[CmsCollection]`——透過
`[CmsTranslations(typeof(FileTranslation))]` 連結回 `File`:

```csharp
// src/Struo.Infrastructure/Files/File.cs (excerpt)
[CmsTranslations(typeof(FileTranslation))]
[SugarColumn(IsIgnore = true)]
public List<FileTranslation> Translations { get; set; } = [];
```

```csharp
// src/Struo.Infrastructure/Files/FileTranslation.cs
[SugarTable("file_translations")]
public sealed class FileTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    // FileId/Locale carry no unique attribute here: SqlSugarClientFactory's EntityService hook reads
    // this table's [CmsTranslations] metadata through TranslationSidecarIndexPolicy and adds the
    // composite unique (fileid, locale) to the generated column model at CodeFirst time.
    // SchemaGuard.AssertCriticalConstraintsAsync re-checks the resulting index in Development.
    public Guid FileId { get; set; }
    public string Locale { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true, Sort = 1)]
    public string? Title { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Alt", Interface = FieldInterface.Text, Sort = 2)]
    public string? Alt { get; set; }
}
```

`MetadataScanner` 會從這一對類別衍生出 `TranslationMetadata`
(`src/Struo.Domain/Metadata/Models/TranslationMetadata.cs`):`ForeignKeyProperty` (掃描器的慣例是
`{ParentTypeName}Id`，所以 `File` 對應到 `FileId`)、`LocaleProperty` (`Locale`)，以及 `Fields`
(該附屬資料表自身 `[CmsField]` 的 camelCase 名稱——此處為 `title` 與 `alt`)。用來保證每個父資料列、
每個語言最多只有一筆翻譯資料列的 `FileId`+`Locale` 複合唯一鍵，並不是宣告在 entity 上的:它是由
`SqlSugarClientFactory` 的 `EntityService` hook，透過 `TranslationSidecarIndexPolicy`
(`src/Struo.Infrastructure/Persistence/TranslationSidecarIndexPolicy.cs`) 從同一份
`[CmsTranslations]` metadata 衍生而來，在 `InitTables` 讀取這兩個關鍵欄位之前，先把解析出來的群組
名稱蓋上去。fork 自己的附屬資料表也會以同樣的方式免費取得這個索引，只要在新的 CodeFirst 資料表上
掛上 `[CmsTranslations(typeof(...))]` 即可——不需要在附屬資料表 entity 本身寫任何東西。一個*既有*
的、早於這個機制就存在的附屬資料表，並不會回溯取得這個索引;那需要在 `db/migrations/` 底下寫一份
經過審查的 migration，就跟為既有資料表新增任何其他限制式一樣。唯一的例外是一台以
`Database:AutoSyncSchema=true` 執行的 Development host:它完整的 CodeFirst 同步會自行嘗試把這個
衍生出來的唯一索引加到既有資料表上，如果該資料表已經存在重複的 `(fk, locale)` 資料列，啟動就會直接
失敗——這會讓壞資料浮現出來而不是隱藏它，但在 Development 之外，仍然不能取代一份經過審查的
migration。`SchemaGuard`
(`src/Struo.Infrastructure/Persistence/SchemaGuard.cs`) 會在 Development 啟動時，針對目前設定所擁有
的每一個附屬資料表，驗證該索引確實存在。

翻譯附屬資料表上欄位的 `MaxLength` 行為，完全依循第 5 章的規則，由
`FieldValueRules.CheckMaxLengthTranslation` 逐語言套用;與父資料列欄位唯一的差異，是錯誤訊息會指名
語言:`"Field '{name}' exceeds maximum length {n} for locale '{locale}'."`

有一個結果值得明講:`File` 資料列只會透過專屬的上傳管線建立 (`FileService.UploadAsync`，第 11
章)——資料列的建立權屬於這條管線，而不屬於一般的 items API。`ItemService.CreateAsync` 會*先*
檢查一般的 `CanWrite` 授權 (先於下方的 `RequireSuperAdminForAdminOnly` 與 `File` 集合拒絕分支;
第 12 章的 `AdminOnly` 段落，記載了這個檢查順序適用於每一個集合)——一個對 `file` 完全沒有寫入
授權的呼叫端，會先看到通用的
「Write not permitted.」(`FORBIDDEN`)，根本輪不到下方這個集合專屬的拒絕。只有在通過那一關之後，
`File` 專屬的守衛才會執行:`ItemService.CreateAsync` 的 `File` 集合拒絕分支會直接以 `400 BAD_USER_INPUT`
拒絕一個一般的 `POST /api/items/file`，逐字引用原始碼中的訊息:「Files cannot be created through the
generic items API. Upload one with POST /api/files instead.」——在抵達翻譯驗證、`ReadOnly`
欄位剝除，或任何其他一般新增機制之前就先擋下。因為兩種協定共用同一個 `ItemService.CreateAsync`，
GraphQL 的 `createFile` mutation 也會被完全相同地拒絕。這第二關與 `File` 的
`fileName`/`contentType`/`size` 欄位是否為 `ReadOnly` 無關:即使一個請求本文提供了每一個必填
欄位，仍然會被拒絕，因為是*整個集合*被排除在一般新增之外，而不僅僅是它的欄位。一個透過一般 items
API *就能*建立的、帶有翻譯附屬資料表的集合 (任何你自己用 `[CmsTranslations]` 定義的集合)，並沒有
這項限制——只有 `File` 因為它專屬的上傳管線，才對建立方式做了這個特例;它逐語言的 `title`/`alt`，
之後仍然照常透過 `PUT /api/items/file/{id}` 這條一般的更新路徑編輯。

## 預設語言規則

在**新增**時，預設語言的翻譯是必填的。`ItemWriteSideSync.SyncTranslationsAsync`
(`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`) 會拒絕:缺少 `translations` 鍵、非
物件的值、空物件，或是完全沒有提到預設語言代碼的物件——全部使用一模一樣的錯誤訊息，而且會在呼叫端
能觀察到任何父資料列新增之前就先擋下 (父資料列的新增與翻譯同步都在同一個交易中執行，所以這裡的
拒絕會讓整個寫入回滾)。`File` 無法示範這條路徑 (前一節已說明為什麼它自己的新增路由完全繞過一般
API)，所以下面這兩個請求，改為對示範用 Blog 範例的 `Article` 集合 (第 16 章) 執行，暫時選用啟用
這項規則來驗證——對任何你自己宣告 `[CmsTranslations]` 的集合而言，產生的錯誤結構都是一樣的:

```
$ curl -s -X POST http://localhost:5221/api/items/article \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"status":"draft"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"A translation for the default locale 'en' is required."}}
```

```
$ curl -s -X POST http://localhost:5221/api/items/article \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"status":"draft","translations":{"zh-TW":{"title":"你好世界"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"A translation for the default locale 'en' is required."}}
```

在**更新**時，這項規則會放寬:缺少或非物件的 `translations` 內容，會被當作一次無動作的局部更新
(你可以只更新父層欄位，或只更新一個非預設語言，而不需要重新提供每一個語言)，也不會強制要求預設
語言。

不論讀取或寫入，一個存在但未啟用的語言鍵值，都會以相同的方式被拒絕，方向並不重要:

```
$ curl -s -X PUT http://localhost:5221/api/items/file/<id> \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"translations":{"fr":{"title":"bonjour"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown or disabled locale 'fr'."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?locale=fr-FR"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown or disabled locale 'fr-FR'."}}
```

在已啟用語言檢查執行之前，語言代碼還會先做字元集驗證 (`ItemService.ValidateLocale`、
`LocaleFormat.IsValid`):它必須符合 `[A-Za-z0-9_-]{1,35}`，藉此堵住透過 `?locale=` 查詢參數進行
SQL 特殊字元注入的管道。

還有兩項寫入端規則，補完附屬資料表的驗證，兩者都是逐語言範圍的:

- 某個語言物件內出現的欄位名稱，若不是附屬資料表自身的可翻譯欄位之一，會被拒絕:`"Field '{name}'
  is not a translatable field of '{collection}'."`
- 一個標記為 `Required` 的可翻譯欄位 (在*附屬資料表* entity 自己的 `[CmsField]` 上)，必須在所提供
  的每一個語言中都存在且非空，不只是預設語言:`"Required translation field '{name}' is missing
  for locale '{locale}'."`。`File` 自己的可翻譯欄位 (`title`/`alt`) 並非 `Required`，所以這一項
  改為對範例的 `ArticleTranslation.Title` (`Required = true`) 進行驗證:

```
$ curl -s -X POST http://localhost:5221/api/items/article \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"status":"draft","translations":{"en":{"body":"no title here"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Required translation field 'title' is missing for locale 'en'."}}
```

```
$ curl -s -X PUT http://localhost:5221/api/items/file/<id> \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"translations":{"en":{"bogus":"x"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'bogus' is not a translatable field of 'file'."}}
```

## 逐語言 SEO

`SeoTranslation` (`src/Struo.Domain/Seo/SeoTranslation.cs`) 是一個抽象基底類別，翻譯附屬資料表
entity 可以繼承它，藉此取得三個現成的 `Group = "SEO"` 欄位——`SeoTitle` (`Text`)、
`SeoMetaDescription` (`Textarea`)、`SeoOgImageId` (`Image`)——它自己沒有任何 `[SugarColumn]`
(Domain 層維持不依賴任何套件;實際套用的是具體附屬資料表自己的 `[SugarTable]`/欄位)。SEO 刻意設計成
僅限逐語言:沒有可以退回使用的父層 SEO 慣例。範例的 `ArticleTranslation`
(`samples/Struo.Sample.Blog/ArticleTranslation.cs`) 就是這種繼承方式出貨後的實際範例。

## 翻譯在 REST 與 GraphQL 回應中如何呈現

**REST**:任何帶有翻譯附屬資料表的集合，其每一筆投影出的資料列一律帶有一個 `translations` 物件，
由 `TranslationOverlay.ApplyAsync` (`src/Struo.Application/Query/Read/TranslationOverlay.cs`)
附加上去——這與 `fields=` 投影 (第 8 章介紹) 彼此獨立，因為這個 overlay 是在 `ItemProjector`
自有欄位選取之後才執行的。沒有帶 `?locale=` 時，每一個有資料列存在的已啟用語言都會被納入，以語言
代碼為鍵值:

```
$ curl -s -b cookies.txt http://localhost:5221/api/items/file/<id>
{"success":true,"data":{"id":"...", "fileName":"alpha-report.txt", ...,
  "translations":{"en":{"title":"alpha-report","alt":null},"zh-TW":{"title":"alpha 報告","alt":"Alpha 報告圖示"}}}}
```

帶上 `?locale=` 時，這個對應表會縮小到只剩那一個語言——如果該父資料列在這個語言還沒有任何資料列，
就是一個空物件 `{}` (不是缺少這個鍵值，也不是 `null`)，以下是在這個檔案還沒有加入任何 `zh-TW`
翻譯之前的情況:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?locale=zh-TW"
{"success":true,"data":{"id":"...", "fileName":"alpha-report.txt", ..., "translations":{}}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?locale=en"
{"success":true,"data":{"id":"...", "fileName":"alpha-report.txt", ..., "translations":{"en":{"title":"alpha-report","alt":null}}}}
```

**GraphQL**:帶有附屬資料表的集合，其物件型別上會多出一個 `translations: [Translation!]` 欄位，
這裡共用的 `Translation` 型別 (`src/Struo.Api/GraphQl/StruoTypeModule.cs`) 是
`{ locale: String!, fields: Any! }`——`fields` 是框架通用的 JSON scalar (SDL 名稱為 `Any`)，把該
語言的欄位對應表存成一個原始物件，而不是逐集合的型別化結構:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"{ file(id: \"<id>\") { id fileName translations { locale fields } } }"}'
{"data":{"file":{"id":"<id>","fileName":"alpha-report.txt","translations":[
  {"locale":"en","fields":{"title":"alpha-report","alt":null}},
  {"locale":"zh-TW","fields":{"title":"alpha 報告","alt":"Alpha 報告圖示"}}
]}}}
```

透過 GraphQL 寫入翻譯時，用的是一個專屬的、逐集合的**型別化**輸入，而不是通用的對應表:
`CollectionSchemaBuilder` 會產生一個 `XTranslationFieldsInput` (每一個可翻譯自有欄位對應一個輸入
欄位) 與一個 `XTranslationInput { locale: String!, fields: XTranslationFieldsInput! }`，每一個
新增/更新 mutation 的輸入型別，都帶有由這些型別建構出的 `translations: [XTranslationInput!]`——第
10 章完整涵蓋 GraphQL mutation。

## 管理後台語言分頁與完成度指示

管理後台 SPA 的 `ItemForm.vue`，只有在集合帶有可翻譯欄位時，才會渲染分頁列
(`v-if="fields.translatable.length"`，位於 `<Tabs>` 元素上)——`GET /api/languages` 回傳的每一筆
資料列 (透過 `languageStore` 這個 Pinia store) 對應一個分頁，即使只啟用了單一語言也一樣。至於每個
分頁旁那個小小的「圓點」，把關條件則更窄:只有在已啟用語言超過一個、*且*集合帶有可翻譯欄位時，才會
渲染 (`ItemForm.vue` 的 `showDots` computed property)——一個只有單一語言、卻帶有可翻譯欄位的安裝，
仍然會顯示一個 (沒有圓點的) 分頁。每個圓點的填滿狀態，來自 `hasLocaleContent`
(`frontend/src/lib/localeCompleteness.ts`):

```ts
export function hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => isNonEmpty(values[f.name]))
}
```

只要某個語言*任何一個*可翻譯欄位存有非空值，該語言的圓點就會被填滿——一個修剪後非空的字串、一個
非空陣列，或任何其他非 null/非 undefined 的值 (數字包括 `0`、布林值包括 `false`，都算作「有內
容」)。這是一個純粹在 client 端、於記憶體中計算的訊號，來自已經載入的表單模型;它**不是**一項
有效性檢查，也與上述在伺服器端強制執行的逐語言 `Required` 規則毫無關係——一個語言可以顯示圓點
已填滿，卻仍然無法通過該項伺服器檢查 (例如填了一個非必填欄位，卻把必填欄位留白)，反之，在任何
編輯發生之前的第一次載入時，也可能出現相反的情況。

## 接下來該去哪

- 第 5 章 [欄位型別與介面](05-field-types.md)，取得本章附屬資料表欄位所依據的 `Translatable`
  欄位選項與儲存規則。
- 第 7 章 [關聯](07-relations.md)，說明一個關聯的*目標*資料列如何被解析——附屬資料表上一個可翻譯
  的 `Image`/`File` 欄位，會用與父層關聯相同的方式解析成它的 `file` 資料列。
- 第 8 章 [查詢 DSL](08-query-dsl.md)，說明可翻譯欄位如何在有效查詢語言下被篩選/排序。
- 第 9 章 [REST API](09-rest-api.md)，取得完整的請求/回應信封格式，以及上方每一個寫入範例都用到
  的 `X-Struo-CSRF` 標頭。
- 第 10 章 [GraphQL API](10-graphql-api.md)，完整涵蓋 GraphQL mutation 的型別化翻譯輸入。
