# 25. Fork 之後要做的事

這一章是 fork 完 StruoCMS 之後的一份清單：有幾樣東西帶著模板作者的名字、金鑰存放區或回報管道，
fork 要換成自己的。每一項都寫了做什麼、為什麼、怎麼做，不必照順序，用得到哪項做哪項。

## 1. LICENSE：保留 MIT 聲明，加上自己的著作權行

根目錄的 `LICENSE` 是 MIT。MIT 要求每一份副本都附上原本的著作權聲明與授權全文，所以不要刪掉
原來那一行。

在原有的 `Copyright (c) …` 之下加一行自己的，例如 `Copyright (c) 2027 Your Name`。fork 加進
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

## 4. `samples/Blog`：刪掉

`samples/Struo.Sample.Blog` 是學習用的範例，不是你的專案的一部分。看完就刪；它不只是一個目錄，
測試專案也拿它的集合當 fixture，所以要按清單清乾淨。完整步驟在
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

登入 cookie 叫 `struo.session`。如果你的正式網站是某個共用上層網域底下的子網域（例如
`app.example.com`，而同一個上層網域下還有別人控制的子網域），那些兄弟子網域可以替整個上層網域
寫入同名的 cookie，蓋掉或塞入你的 session cookie。

`__Host-` 前綴讓瀏覽器拒收這類 cookie：帶這個前綴的 cookie 必須有 `Secure`、`Path=/`，而且不能
帶 `Domain`，所以兄弟子網域寫不進來。這個專案的 cookie 已經是 `Path=/`、沒有 `Domain`，缺的只
有名稱。

名稱是 `src/Struo.Api/Auth/AuthSchemes.cs` 裡的常數 `SessionCookieName`，沒有設定鍵。把它改成
`__Host-struo.session` 即可；CSRF 與 session 輪替的 middleware 讀的是同一個常數。有兩件事要先
知道：

- 前綴要求 `Secure`，所以整條路都要走 HTTPS。把 `Auth:Cookie:RequireSecure` 設成 `false` 的環境
  （見[第 20 章](20-deployment.md)〈Cookie 只在 HTTPS 下送回〉）瀏覽器會直接拒收這個 cookie。
- cookie 改名之後，所有已登入的人都會被登出一次。

## 8. 圖片或 API 在另一個來源：`CSP_IMG_EXTRA` 與 `CSP_CONNECT_EXTRA`

後台映像送出的 `Content-Security-Policy` 只放行自己的來源。圖片放在別的來源（CDN、儲存桶的
presigned URL、另一個主機上的 `Branding:LogoUrl`）時，瀏覽器會擋掉；SPA 直接呼叫另一個來源的
API 也一樣。

映像建置之後，用環境變數 `CSP_IMG_EXTRA`（附加到 `img-src`）與 `CSP_CONNECT_EXTRA`（附加到
`connect-src`）補上，值是以空白分隔的來源，例如 `https://cdn.example.com`。完整說明見
[第 20 章：部署](20-deployment.md)的〈後台 SPA 映像〉。

## 9. 後台前面還有一層反向代理：`ForwardedHeaders`

API 預設不信任 `X-Forwarded-For`、`X-Forwarded-Proto`、`X-Forwarded-Host`。API 前面有反向代理
時，API 看到的客戶端位址就是代理自己，登入的 IP 限流因此失效，OIDC 在 TLS 於代理終止時也會組出
`http` 的 redirect URI。

設 `ForwardedHeaders__Enabled=true`，並用 `ForwardedHeaders__KnownProxies__0` 或
`ForwardedHeaders__KnownNetworks__0` 列出你的代理。後台映像裡的 nginx 本身就是一層代理，再往前
加一層時有兩跳，API 只認其中一跳，細節與外層代理要設的標頭見
[第 20 章：部署](20-deployment.md)的〈反向代理後面要啟用 `ForwardedHeaders`〉那一節。
