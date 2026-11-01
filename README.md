# 拉麵（noodle-pet）v1.0.0

拉筋的麵：待在桌面上的小桌寵，連續用電腦一段時間就叫你起來伸展，還會親自示範動作、幫你計時。
角色、動作示範與說明改寫自 [Noodle](https://tonitherigatoni.com/)（MIT），Windows 版另外做成桌寵。

## 使用

- 執行 `dist\noodle-pet.exe`（不用安裝；需要 Windows 10/11 內建的 .NET Framework 4.8）。
- **左鍵點桌寵**：打招呼、看還有幾分鐘；提醒出現時點牠就開始；做動作時點牠展開步驟。
- **拖曳**：抓著會吊在半空中；「走來走去」模式放手會掉回工作列。
- **右鍵點桌寵**（或系統匣圖示）：現在做一組、切換固定位置／走來走去、暫停、收起、示範、設定。
- 提醒出現後：「開始」跟著做並計時、「晚點」延後、沒理它 20 秒會從泡泡上緣探頭，一分鐘後自己收起來。
- 離開鍵盤滑鼠 5 分鐘就算休息過，時間重新算；通話中（麥克風或鏡頭使用中）、全螢幕時不打擾。

## 設定

設定視窗（右鍵 → 設定…）改了就自動存檔、立即套用。資料都在 `%USERPROFILE%\.noodle-pet\`：

| 檔案 | 內容 |
| --- | --- |
| `config.json` | 設定（也可以手動改，存檔後幾秒內生效） |
| `state.json` | 位置、模式、輪替進度、暫停狀態 |
| `log.jsonl` | 每次提醒一行：done／snoozed／ignored／away／stopped |
| `error.log` | 出錯時才有 |

## Claude Code 連動（選配）

設定 → Claude Code → 「連結 Claude Code…」：會先顯示 `~/.claude/settings.json` 要改的地方，確認後才寫入，原檔備份成 `settings.json.bak-時間`。
加入的是三個 http hook（UserPromptSubmit、Stop、StopFailure），只打本機 `127.0.0.1:47615`；Claude 忙超過 15 秒、而且你已經坐了 20 分鐘以上，桌寵就提早叫你。
有用同步腳本管理 `.claude` 設定的話，記得也同步這次的變更。

## 重新建置

```
build.cmd                     :: 編譯 dist\noodle-pet.exe（用 Windows 內建的 csc，不用裝任何東西）
build.cmd snapshot            :: 開發用：dist\noodle-snapshot.exe 把泡泡與設定頁畫成 PNG 檢查版面
node tools\render-sprites.mjs "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" assets\figures assets
                              :: 換角色素材後重畫精靈圖（用 Edge 無頭模式，不用 npm 套件；可加第 4 個參數只畫某些角色）
```

## 專案結構

- `src/`：C# 原始碼與 XAML（Theme.xaml 是設計 token，對應 `DESIGN.md`）
- `assets/figures/`：Noodle 的原始 SVG（5 個角色 × 12 個動作）
- `assets/sprites/`：畫好的精靈圖（相同影格已合併，靜止時幾乎不耗 CPU）
- `LICENSE-noodle.txt`：Noodle 的 MIT 授權

## 沒有做的

- 行事曆忙碌時不打擾（Noodle 用 macOS 行事曆；Windows 要另外接 Outlook 或 Google 行事曆）
- Mac 版的選單列垂降（這裡改成桌寵從螢幕頂端垂降進場）
