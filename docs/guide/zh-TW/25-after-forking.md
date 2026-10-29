# 25. Fork 之後要做的事

這一章是 fork 完 StruoCMS 之後的一份清單：有幾樣東西帶著模板作者的名字、金鑰存放區或回報管道，
fork 要換成自己的。每一項都寫了做什麼、為什麼、怎麼做，不必照順序，用得到哪項做哪項。

## 1. LICENSE：保留 MIT 聲明，加上自己的著作權行

根目錄的 `LICENSE` 是 MIT。MIT 要求每一份副本都附上原本的著作權聲明與授權全文，所以不要刪掉
原來那一行。

在原有的 `Copyright (c) …` 之下加一行自己的，例如 `Copyright (c) <year> Your Name`。fork 加進
來的程式碼是你的，原本的部分仍屬於原作者。

## 2. `UserSecretsId`：重新產生

`src/Struo.Api/Struo.Api.csproj` 裡寫死了一個 `UserSecretsId`。同一台機器上，每個帶著這個 ID
的 clone 都讀寫同一個 user-secrets 存放區，兩個專案的連線字串與 OIDC secret 就會互相覆蓋。

刪掉 csproj 裡的 `<UserSecretsId>` 元素，再執行：

```bash
dotnet user-secrets init --project src/Struo.Api
```

指令會寫入一個新的 GUID。已經用舊 ID 存過的 secret 不會跟過去，要重新設定。

## 3. SonarQube Cloud：設定自己的變數與 secret

`.github/workflows/ci.yml` 的 `sonar-backend`、`sonar-frontend` 兩個 job 從儲存庫變數讀組織與專案
鍵：`SONAR_ORGANIZATION`、`SONAR_BACKEND_PROJECT_KEY`、`SONAR_FRONTEND_PROJECT_KEY`，token 則是
secret `SONAR_TOKEN`。變數與 secret 都在儲存庫的 Settings → Secrets and variables → Actions 裡設定。

`SONAR_ORGANIZATION` 沒設時，兩個 job 都會跳過，所以不用 Sonar 的 fork 什麼都不必做。一旦設了
它，就要把另外三個一起設齊，否則 job 會在缺值的情況下失敗。fork 的 pull request 與 Dependabot
的 pull request 讀不到 secret，這兩種情況 job 一律跳過。

## 4. `samples/Struo.Sample.Blog`：刪掉

`samples/Struo.Sample.Blog` 是學習用的範例，不是你的專案的一部分。看完就刪；它附帶自己的測試專案，
solution 檔與 `frontend/e2e/sample/` 的規格也都指向它，所以要按清單清乾淨。`tests/` 不用動。完整步驟在
[第 23 章：範例專案導覽](23-sample-walkthrough.md)的〈完全移除範例〉。

## 5. `SECURITY.md`：換成自己的回報管道

根目錄的 `SECURITY.md` 寫的是這個模板的漏洞回報方式：透過這個儲存庫的 GitHub「Report a
vulnerability」私下回報。fork 不是同一個儲存庫，這份文件對你的使用者是錯的。

改寫成你自己的管道：在 fork 的 Settings → Code security 開啟 private vulnerability reporting，
並把支援的版本範圍與回覆時限改成你能做到的。

## 6. 後台預設語言

後台 UI 預設是 `en`。要換成別的語言，在建置時設 `VITE_DEFAULT_UI_LOCALE`：開發伺服器讀
`frontend/.env`，映像建置讀你自己加進版控的 `frontend/.env.production`。使用者在語言切換器選過的
語言，永遠優先。怎麼註冊新語言與這個變數的細節，見
[第 19 章：後台客製化](19-admin-customization.md)的〈UI 語言〉。

## 7. 正式網域在共用的上層網域下：考慮 `__Host-` cookie 前綴

登入 cookie 預設叫 `struo.session`。如果你的正式網站是某個共用上層網域底下的子網域（例如
`app.example.com`，而同一個上層網域下還有別人控制的子網域），那些兄弟子網域可以替整個上層網域
寫入同名的 cookie，蓋掉或塞入你的 session cookie。

`__Host-` 前綴讓瀏覽器拒收這類 cookie：帶這個前綴的 cookie 必須有 `Secure`、`Path=/`，而且不能
帶 `Domain`，所以兄弟子網域寫不進來。這個專案的 cookie 已經是 `Path=/`、沒有 `Domain`，缺的只
有名稱。

把 `Auth:Cookie:Name` 設成 `__Host-struo.session`（例如 `Auth__Cookie__Name=__Host-struo.session`）即可；
CSRF 與 session 輪替的 middleware 跟著它走。啟動時會檢查這個名稱，cookie 不是一律 `Secure`、或帶了
`Domain` 或別的 `Path` 時，啟動失敗，訊息會點出這個鍵。
[第 13 章](13-rest-endpoints.md)與[第 16 章](16-authentication.md)裡寫的字面 `struo.session`
也會跟著變。先知道三件事：

- 前綴要求 `Secure`，但只有面對瀏覽器的來源必須是 HTTPS。Development 之外，不論 API 自己看到的是
  什麼協定，cookie 都帶 `Secure`，所以 nginx 到 API 之間仍可以走純 http。
  `RequireSecure=false` 等於 `SameAsRequest`，名稱帶前綴時，啟動會失敗，訊息會點出
  `Auth:Cookie:Name`。
- 這個名稱在 Development 也生效，而 Development 的 `Auth:Cookie:RequireSecure` 解析成
  `SameAsRequest`，所以在本機執行與 e2e 的主機設 `Auth__Cookie__RequireSecure=true` 之前，啟動都會失敗。
  設了之後 cookie 就帶 `Secure`，Chrome 與 Firefox 接受 `http://localhost` 上的 `Secure` cookie；不接受的
  瀏覽器（Safari）本機就得走 HTTPS。
- cookie 改名之後，所有已登入的人都會被登出一次。

## 8. 圖片或 API 在另一個來源：`CSP_IMG_EXTRA` 與 `CSP_CONNECT_EXTRA`

後台映像送出的 `Content-Security-Policy` 只放行自己的來源。圖片放在別的來源（CDN、儲存桶的
presigned URL、另一個主機上的 `Branding:LogoUrl`）時，瀏覽器會擋掉；SPA 直接呼叫另一個來源的
API 也一樣。

容器啟動時，用環境變數 `CSP_IMG_EXTRA`（附加到 `img-src`）與 `CSP_CONNECT_EXTRA`（附加到
`connect-src`）補上。nginx 範本在那時才渲染，不必重新建置映像。值是以空白分隔的來源，例如
`https://cdn.example.com`。完整說明見[第 20 章：部署](20-deployment.md)的〈後台 SPA 映像〉。

## 9. 後台前面還有一層反向代理：`ForwardedHeaders`

API 預設不信任 `X-Forwarded-For`、`X-Forwarded-Proto`、`X-Forwarded-Host`。不信任的話，API 看到的
客戶端位址就是直接呼叫它的那一端，啟用登入的 IP 限流時會因此失準，TLS 在代理終止時 OIDC 也會組出
`http` 的 redirect URI。

設 `ForwardedHeaders__Enabled=true`，並用 `ForwardedHeaders__KnownProxies__0` 或
`ForwardedHeaders__KnownNetworks__0` 列出 API 自己的那一層代理。API 前面是後台映像時，那一層就是
後台 nginx 容器：API 看到的客戶端是那個容器，所以要列容器的位址（或專用網路的範圍），而不是你外層的
代理。外層代理多出來的是第二跳，API 不會採信。細節與外層代理要設的標頭見
[第 20 章：部署](20-deployment.md)的〈反向代理後面要啟用 `ForwardedHeaders`〉那一節。

## 10. 單一登入提供者：加上你的提供者並登記重新導向網址

範本在 `src/Struo.Api/appsettings.json` 的 `Oidc:Providers` 底下，隨附 `microsoft` 與 `google` 兩筆停用
的項目。用得到的留著，用不到的刪掉，使用者會用到的其他 OpenID Connect 提供者，每個再加一筆。每一筆都要
有自己的 id、`DisplayName`、`Authority`、`ClientId` 與 `ClientSecret`；密鑰用
`Oidc__Providers__<id>__ClientSecret` 這類環境變數提供，絕不寫進提交的檔案。

提供者的按鈕會顯示 `Icon` 指名的圖示，內建的代號是 `microsoft` 與 `google`。要用別的品牌，就在
`frontend/src/lib/ssoIcons.ts` 的 `ICONS` 表加一筆 SVG，把它的名稱設成該項目的 `Icon`，再重新建置後台。
沒有對應的項目時，按鈕顯示通用的鑰匙圖示。

到每個提供者的主控台登記重新導向網址：你的正式環境來源加上該提供者的回呼路徑，就是它的 `CallbackPath`，項目沒設時
是 `/signin-oidc-<id>`。測試環境的來源同樣要登記，開發時再登記 `http://localhost:5173` 加上同一條路徑。
`/api/` 之外的自訂 `CallbackPath`，還要在 `frontend/nginx/default.conf.template` 加一個 `location` 區塊，開發伺服器則要在 `frontend/vite.proxy.ts` 加一筆。
各提供者的設定步驟見[第 16 章：認證與 SSO](16-authentication.md)。

## 11. `Program.cs`：從現在起歸你

`src/Struo.Api/Program.cs` 屬於你的 fork。範本自己的組裝放在 `src/Struo.Api/Hosting/`，藏在
`builder.AddStruoCms()` 與 `app.UseStruoCmsAsync()` 後面，所以上游不會再在 `Program.cs` 裡改動
組裝的內容。這個檔案本身由你自己改。

自己的服務在 `Program.cs` 裡、`AddStruoCms()` 之後註冊，那裡的註冊贏過核心的預設。核心用 `TryAdd`
註冊的服務，寫在 `AddStruoCms()` 之前的註冊也會被保留。設定來源要在呼叫之前加。可以接上的擴充點見
[第 18 章：擴充點](18-extension-points.md)。

## 12. 測試：拿掉模板的守衛，加上自己的專案

`tests/Struo.Tests.Template` 放的是模板自己的儲存庫守衛：文件慣例、模板不變式，以及 CI、Docker 與
健康檢查的慣例。它們檢查的是這個儲存庫的檔案，不是你的產品。可以把專案資料夾連同 `StruoCMS.slnx`
裡那一行整個刪掉，也可以留下想要的守衛。沒有別的東西參照它。

自己的測試寫在自己的專案裡，參照 `Struo.Tests.Support` 與你的內容專案。測試套件的版本集中在
`Directory.Packages.props`，所以 `PackageReference` 不寫版本。以下假設專案在 `tests/MyFork.Tests`，內容專案在
`src/MyFork.Content`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Struo.Tests.Support\Struo.Tests.Support.csproj" />
    <ProjectReference Include="..\..\src\MyFork.Content\MyFork.Content.csproj" />
  </ItemGroup>
</Project>
```

有兩件設定屬於你的專案，跟 `tests/Struo.Tests` 一樣。`TestFramework` 屬性指向
`StruoTestFramework`；`[ModuleInitializer]` 透過 `TestHostEnvironment.Pin` 釘住你的內容組件，這要
在任何主機建起來之前執行。繼承 `StruoApiFactory` 的主機類別列出公開可讀的集合：

```csharp
using System.Runtime.CompilerServices;
using Struo.Tests.Support;
using Xunit;

[assembly: TestFramework("Struo.Tests.Support.StruoTestFramework", "Struo.Tests.Support")]

namespace MyFork.Tests;

internal static class ContentAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void Pin() => TestHostEnvironment.Pin("MyFork.Content");
}

public sealed class MyApiFactory : StruoApiFactory
{
    protected override IReadOnlyList<string> PublicReadCollections => ["post"];
}

[CollectionDefinition("MyApi")]
public sealed class MyApiCollection : ICollectionFixture<MyApiFactory>;
```

每個主機都會跑 `Program.cs`，它會重設靜態的 Serilog `Log.Logger`，所以用到主機的測試類別不能
平行執行。把它們都放進這個序列集合：標上 `[Collection("MyApi")]`，並從建構函式取得 factory。

類別庫的說明見[第 22 章](22-testing.md)。它的公開 API 有變動，會寫在[版本紀錄](changelog.md)。

## 13. Bootstrap 管理員：設密碼，並改掉預設 email

核心的 seed 會在空資料庫上建立一個管理員，任何環境都需要 `Auth__BootstrapAdmin__Password`，不會替你產生。
請設在跑 `migrate` 的環境裡，Compose 則用 `STRUO_ADMIN_PASSWORD`。預設 email `admin@admin.com` 可以用，但太
容易被猜到，請把 `Auth__BootstrapAdmin__Email` 設成你自己的地址。你自己會跑核心 migration 的測試主機同樣需要
密碼；範本的主機用的是 `Struo.Tests.Support` 裡的 `TestBootstrapAdmin.Password`。見
[第 20 章：部署](20-deployment.md)。
