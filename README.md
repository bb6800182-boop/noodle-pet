# 拉麵（noodle-pet）v1.1.0

拉筋的麵：待在桌面上的小桌寵，連續用電腦一段時間就叫你起來伸展、喝水，還會親自示範動作、幫你計時；也會照你設的時間提醒你，或看 Google 月曆提醒你開會。
角色、動作示範與說明改寫自 [Noodle](https://tonitherigatoni.com/)（MIT），Windows 版另外做成桌寵；喝水動作是另外畫的。

## 使用

- 執行 `dist\noodle-pet.exe`（不用安裝；需要 Windows 10/11 內建的 .NET Framework 4.8）。
- **左鍵點桌寵**：打招呼、看還有幾分鐘；提醒出現時點牠就開始；做動作時點牠展開步驟。
- **拖曳**：抓著會吊在半空中；「走來走去」模式放手會掉回工作列。
- **右鍵點桌寵**（或系統匣圖示）：現在做一組、切換固定位置／走來走去、暫停、收起、示範、設定。
- 伸展提醒出現後：「開始」跟著做並計時、「晚點」延後、沒理它 20 秒會從泡泡上緣探頭，一分鐘後自己收起來。
- **喝水**：預設每 60 分鐘（用電腦的時間）牠會拿杯水在旁邊喝，按「喝了」算一杯。
- **自訂提醒**：設定 → 自訂提醒，指定時間和重複（每天／平日／自選星期／單次），到時間桌寵出來說一句話，按「知道了」才收。
- **Google 月曆**：開會時先不叫你伸展、喝水；活動開始前 10 分鐘（可改）跳出來提醒。
- 離開鍵盤滑鼠 5 分鐘就算休息過，伸展時間重新算；通話中（麥克風或鏡頭使用中）、全螢幕時不打擾。
- 暫停提醒、安靜時段只管伸展和喝水；自訂提醒和行事曆提醒照常出現（全螢幕時等你離開全螢幕再說，桌寵收起來時改用 Windows 通知）。

## 設定

設定視窗（右鍵 → 設定…）改了就自動存檔、立即套用。資料都在 `%USERPROFILE%\.noodle-pet\`：

| 檔案 | 內容 |
| --- | --- |
| `config.json` | 設定與自訂提醒（也可以手動改，存檔後幾秒內生效） |
| `state.json` | 位置、模式、輪替進度、暫停狀態、提醒到哪一次 |
| `log.jsonl` | 每次伸展／喝水提醒一行：done／snoozed／ignored／away／stopped |
| `error.log` | 出錯時才有 |

Google 月曆的連動設定檔 `calendar-link.json` 放在**專案資料夾**（exe 在 `dist\` 裡時就是它的上一層），跟著 Dropbox 同步到每台電腦；裡面有金鑰，已排除在 git 之外。

## Google 月曆連動（選配）

設定 → Google 月曆，照三個步驟：

1. 「複製程式碼」：把 `gas/calendar-relay.gs` 填好這組連結專用的金鑰，放進剪貼簿（不進剪貼簿歷程記錄、不同步到雲端）。
2. 用要讀行事曆的 Google 帳號打開 https://script.new，貼上、儲存，部署成「網頁應用程式」（執行身分：我；誰可以存取：所有人），授權讀取日曆。建議照程式碼最上面的說明，把權限限縮成 `calendar.readonly`。
3. 把 `…/exec` 網址貼回設定，按「連結」，再勾要讀的行事曆（預設只讀主要行事曆）。

- 只讀不寫。Apps Script 沒有正確金鑰一律拒絕；桌寵只把金鑰送到 `script.google.com`，轉址只接受 `googleusercontent.com`。
- 每 5 分鐘在背景抓一次「前 6 小時到後 36 小時」的活動；全天活動、標成「有空」、回覆不參加的都不算開會。
- 要換金鑰：「解除連結」後重新複製程式碼，到 Apps Script 換掉程式碼並部署新版本。

## Claude Code 連動（選配）

設定 → Claude Code → 「連結 Claude Code…」：會先顯示 `~/.claude/settings.json` 要改的地方，確認後才寫入，原檔備份成 `settings.json.bak-時間`。
加入的是三個 http hook（UserPromptSubmit、Stop、StopFailure），只打本機 `127.0.0.1:47615`；Claude 忙超過 15 秒、而且你已經坐了 20 分鐘以上，桌寵就提早叫你。
有用同步腳本管理 `.claude` 設定的話，記得也同步這次的變更。

## 重新建置

```
build.cmd                     :: 編譯 dist\noodle-pet.exe（用 Windows 內建的 csc，不用裝任何東西）
build.cmd snapshot            :: 開發用：dist\noodle-snapshot.exe 把泡泡與設定頁畫成 PNG 檢查版面
node tools\render-sprites.mjs "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" assets\figures assets
                              :: 換角色素材後重畫精靈圖（用 Edge 無頭模式，不用 npm 套件）
                              :: 第 4 個參數只畫某些角色（* = 全部），第 5 個參數只重畫某些動作，例如 * drink
node tools\make-drink.mjs assets\figures
                              :: 從各角色的 idle.svg 重新產生喝水動作 drink.svg
```

## 專案結構

- `src/`：C# 原始碼與 XAML（Theme.xaml 是設計 token，對應 `DESIGN.md`）
- `gas/`：Google 月曆中繼的 Apps Script（金鑰由設定視窗填入）
- `assets/figures/`：Noodle 的原始 SVG（5 個角色 × 12 個動作）＋ 喝水動作 `drink.svg`
- `assets/sprites/`：畫好的精靈圖（相同影格已合併，靜止時幾乎不耗 CPU）
- `tools/`：精靈圖產生器、喝水動作產生器、截圖檢查工具
- `LICENSE-noodle.txt`：Noodle 的 MIT 授權

## 沒有做的

- Outlook 行事曆（目前只接 Google 月曆）
- Mac 版的選單列垂降（這裡改成桌寵從螢幕頂端垂降進場）
