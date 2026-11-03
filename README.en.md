# 拉麵 Lamian (noodle-pet) v1.1.2

[繁體中文](README.md) | **English**

A little desktop pet for Windows. After you've been at the computer for a while, it pops up to get you stretching and drinking water, demonstrates the exercise itself, and times it for you. It can also pop up with reminders you set, and check your Google Calendar to remind you before meetings. The name is a pun: 拉麵 means ramen ("pulled noodles"), and 拉 also means "to stretch."

> **Unofficial port.** The characters, exercise demos and instructions come from [Noodle](https://tonitherigatoni.com/) by Pete Cybriwsky ([source](https://github.com/pcybriwsky/noodle), MIT License). This project is not affiliated with or endorsed by the original author. The Windows desktop pet, the drinking animation, and the reminder and calendar features were added in this version. See more of Pete's work on Instagram: [@_re_pete](https://www.instagram.com/_re_pete/).

The interface is in Traditional Chinese. Below, menu and button names are given in English, followed by the Chinese label.

## Getting started

Download `noodle-pet.exe` from [Releases](https://github.com/bb6800182-boop/noodle-pet/releases/latest) (the [project page](https://c520.pages.dev/lamian/) has screenshots and setup steps, in Chinese), put it in a folder you'll keep, and run it. There's no installer and nothing else to install: it runs on the .NET Framework 4.8 that comes with Windows 10 and 11. The exe isn't code-signed, so the first time, Windows SmartScreen may say "Windows protected your PC". Click "More info", then "Run anyway".

On first launch, the pet drops in from the top of the screen and walks you through a short intro.

## How it works

- **Left-click the pet** to say hi and see how long until the next break. When a reminder is up, click to start; while stretching, click to show the steps. If you missed a reminder (ignored it, pressed "Later" 「晚點」, or walked away while the water reminder was up), clicking the pet within 30 minutes brings it back.
- **Drag** to pick it up; it dangles in mid-air. In walking mode it drops back onto the taskbar when you let go.
- **Right-click the pet** (or the tray icon) for: do a set now 「現在做一組」 (redoes the exercise you just missed, if any), log a glass of water 「喝了一杯水」, stay put or walk along the taskbar, pause, hide, demos, and settings.
- **Stretch reminders** come after 30 minutes of computer use (configurable). "Start" 「開始」 begins the exercise with a timer; "Later" snoozes it. If you ignore it, the pet peeks over the bubble after 20 seconds, and the bubble closes itself after a minute.
- **Water**: by default, every 60 minutes of computer use the pet drinks a glass of water next to you. Press "Drank it" 「喝了」 to count a glass. If you missed the reminder or drank before it came, log it from the right-click menu.
- **Custom reminders**: Settings → Custom reminders 「自訂提醒」. Pick a time and a repeat (daily, weekdays, chosen days, or once). The pet shows your message in a speech bubble and keeps it up until you press "Got it" 「知道了」.
- **Google Calendar** (optional): holds off stretch and water reminders during meetings, and reminds you 10 minutes (configurable) before an event starts.
- **Staying out of your way**: 5 minutes away from the keyboard and mouse counts as a break and resets the stretch timer. No reminders during calls (microphone or camera in use) or in full-screen apps.
- Pausing and quiet hours only affect stretch and water reminders. Custom and calendar reminders still appear: in full screen they wait until you leave it, and while the pet is hidden they show up as Windows notifications.

## Settings and data

Changes in the settings window (right-click → Settings 「設定…」) are saved and applied right away. Everything is stored in `%USERPROFILE%\.noodle-pet\`:

| File | Contents |
| --- | --- |
| `config.json` | Settings and custom reminders (you can edit it by hand; changes apply a few seconds after saving) |
| `state.json` | Position, mode, exercise rotation, pause state, reminder progress |
| `log.jsonl` | One line per stretch or water reminder: done / snoozed / ignored / away / stopped |
| `error.log` | Only written when something goes wrong |

There's no telemetry; your data stays on your computer.

The Google Calendar link file `calendar-link.json` is kept in the **project folder** (the parent of `dist\` when the exe is there, otherwise next to the exe). If that folder is synced with Dropbox or a similar service, every computer shares the same link. It contains a secret key and is excluded from git.

## Google Calendar link (optional)

Settings → Google Calendar, in three steps:

1. "Copy code" 「複製程式碼」 puts `gas/calendar-relay.gs`, filled in with a key just for this link, on the clipboard (excluded from clipboard history and cloud sync).
2. Open https://script.new with the Google account whose calendar you want to read. Paste, save, deploy as a web app (Execute as: Me; Who has access: Anyone), and authorize calendar access. As the comment at the top of the code explains, you can narrow the permission to `calendar.readonly`.
3. Paste the `…/exec` URL back into the settings, press "Link" 「連結」, and tick the calendars to read (only the primary calendar by default).

- Read-only. The Apps Script rejects any request without the correct key. The pet only sends the key to `script.google.com` and only follows redirects to `googleusercontent.com`.
- Every 5 minutes it fetches events from 6 hours ago to 36 hours ahead in the background. All-day events, events marked "free", and events you declined don't count as meetings.
- To change the key: "Unlink" 「解除連結」, copy the code again, replace the code in Apps Script and deploy a new version.

## Claude Code integration (optional)

Settings → Claude Code → "Link Claude Code…" 「連結 Claude Code…」 first shows the changes it will make to `~/.claude/settings.json` and only writes them after you confirm, backing up the original as `settings.json.bak-<time>`. It adds three http hooks (UserPromptSubmit, Stop, StopFailure) that only call the local `127.0.0.1:47615`. When Claude has been busy for more than 15 seconds and you've been sitting for at least 20 minutes, the pet calls you for a break early.

## Building from source

Most people can just download the exe. To build it yourself, run `build.cmd`: it compiles `dist\noodle-pet.exe` with the C# compiler that comes with Windows (.NET Framework 4.8), so there's no Visual Studio or SDK to install.

```
build.cmd                     :: build dist\noodle-pet.exe
build.cmd snapshot            :: dev tool: dist\noodle-snapshot.exe renders the bubbles and settings pages to PNG for layout checks
node tools\render-sprites.mjs "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" assets\figures assets
                              :: re-render the sprite sheets after changing character art (headless Edge, no npm packages)
                              :: the 4th argument limits characters (* = all), the 5th limits poses, e.g. * drink
node tools\make-drink.mjs assets\figures
                              :: regenerate the drinking pose drink.svg from each character's idle.svg
```

The project page (https://c520.pages.dev/lamian/) lives in `site/` and is deployed with the author's C520 site. Its download button always points to the latest GitHub Release.

## Project structure

- `src/`: C# source and XAML (`Theme.xaml` holds the design tokens from `DESIGN.md`)
- `gas/`: the Apps Script calendar relay (the key is filled in by the settings window)
- `assets/figures/`: Noodle's original SVGs (5 characters × 12 poses) plus the drinking pose `drink.svg`
- `assets/sprites/`: rendered sprite sheets (identical frames are merged, so it uses almost no CPU when idle)
- `tools/`: sprite renderer, drinking-pose generator, snapshot tool
- `site/`: the project page (`index.html`, icons, animated character SVGs, bubble screenshots)
- `LICENSE`: this project's MIT License; `LICENSE-noodle.txt`: Noodle's original MIT License

## Not included

- Outlook calendars (only Google Calendar for now)
- The Mac version's menu-bar drop-in (here the pet drops in from the top of the screen instead)

## License and notices

- 拉麵 is released under the [MIT License](LICENSE). The characters, exercise demos and instructions come from [Noodle](https://github.com/pcybriwsky/noodle) (© 2026 Pete Cybriwsky, MIT), and its original license is kept in [`LICENSE-noodle.txt`](LICENSE-noodle.txt). Both licenses are also built into the exe: Settings → General → License info 「設定 → 一般 → 授權資訊…」.
- This is an **unofficial** Windows port. It is not affiliated with or endorsed by the author of Noodle.
- The stretches are general activity suggestions, not medical advice. Stop if anything hurts or feels wrong, and check with a doctor or physical therapist first if you have an injury or condition.
- Claude and Claude Code are trademarks of Anthropic; Google and Google Calendar are trademarks of Google. This project is not affiliated with or endorsed by Anthropic or Google.
