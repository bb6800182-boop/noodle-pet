using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NoodlePet
{
    // 設定視窗的「喝水」「自訂提醒」「Google 月曆」三個分類
    public partial class SettingsWindow
    {
        // 喝水
        Player waterPlayer;
        Image waterImg;
        Sprites waterSprites;
        TextBlock waterToday;
        // 自訂提醒
        StackPanel reminderList;
        // Google 月曆
        TextBlock calBadge, calDetail, calUrlError;
        Border calDot;
        Button calSync, calUnlink;
        TextBox calUrl;
        StackPanel calSteps, calPicker, calPickerList;
        string calPickerSig;

        // ---------- 喝水 ----------

        FrameworkElement BuildWater()
        {
            TextBlock e1 = ErrorText();
            waterImg = new Image { VerticalAlignment = VerticalAlignment.Center };
            waterPlayer = new Player(waterImg);
            var preview = new Grid { Background = Brushes.Transparent };
            preview.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            preview.ColumnDefinitions.Add(new ColumnDefinition());
            preview.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            preview.Children.Add(waterImg);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 16, 0) };
            text.Children.Add(T(L.T("時間到了，桌寵會拿杯水在旁邊喝給你看", "When it's time, the pet drinks a glass of water next to you"), "t-body-sm", null, FontWeights.Medium));
            text.Children.Add(T(L.T("按「喝了」算一杯並重新計算，按「晚點」延後。滑鼠移到這裡可以先看動作。", "“Drank it” logs a glass and restarts the count; “Later” snoozes. Hover here to preview the animation."), "t-caption", new Thickness(0, 2, 0, 0)));
            waterToday = T("", "t-caption", new Thickness(0, 6, 0, 0));
            text.Children.Add(waterToday);
            Grid.SetColumn(text, 1);
            preview.Children.Add(text);
            var tryIt = Btn(L.T("試試看", "Try it"), "button-secondary", () => brain.Demo("water"));
            AutomationProperties.SetName(tryIt, L.T("試試喝水提醒", "Try the water reminder"));
            tryIt.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tryIt, 2);
            preview.Children.Add(tryIt);
            preview.MouseEnter += (s, e) => { if (waterSprites != null) waterPlayer.Play(waterSprites, "drink"); };
            preview.MouseLeave += (s, e) => { waterPlayer.Stop(); ShowWaterStill(); };
            refreshers.Add(() =>
            {
                if (waterSprites == null || waterSprites.Character != brain.Cfg.character)
                {
                    waterPlayer.Stop();
                    waterSprites = new Sprites(brain.Cfg.character, 72);
                    ShowWaterStill();
                }
                waterToday.Text = L.T("今天喝了 " + brain.WaterToday + " 杯", "Glasses today: " + brain.WaterToday);
            });
            return Section(L.T("喝水", "Water"), L.T("每隔一段時間，桌寵會拿杯水提醒你補充水分。", "Every so often the pet holds up a glass to remind you to drink."),
                Group(
                    Row(L.T("喝水提醒", "Water reminder"), null, Toggle(() => brain.Cfg.water, v =>
                    {
                        brain.Cfg.water = v;
                        if (v) brain.WaterSeconds = 0;   // 重新打開從現在算，不會一打開就叫
                        Save();
                    }, L.T("喝水提醒", "Water reminder"))),
                    Row(L.T("多久提醒一次", "How often"), L.T("用電腦的時間才算；按「喝了」就重新計算，離開座位不會歸零。", "Only computer time counts. “Drank it” restarts the count; leaving your seat doesn't reset it."),
                        Number(() => brain.Cfg.waterMinutes, v => { brain.Cfg.waterMinutes = v; Save(); }, 15, () => 240, L.T("分鐘", "min"), e1, L.T("喝水提醒間隔（分鐘）", "Water reminder interval (minutes)")), e1),
                    preview),
                T(L.T("「晚點」延後幾分鐘、泡泡多久自動收起，跟伸展提醒用同一個設定。勿擾時段、暫停提醒、開會時都不會叫你喝水。", "The snooze length and bubble auto-hide use the same settings as stretch reminders. No water reminders during quiet hours, while paused, or in meetings."),
                    "t-caption", new Thickness(0, 12, 0, 0)));
        }

        void ShowWaterStill()
        {
            if (waterSprites == null) return;
            var pose = waterSprites.Get("drink");
            if (pose == null) return;
            waterImg.Width = waterImg.Height = waterSprites.Size;
            waterImg.Source = pose.Frames[pose.Index[0]];
        }

        // ---------- 自訂提醒 ----------

        FrameworkElement BuildReminders()
        {
            reminderList = new StackPanel();
            refreshers.Add(FillReminders);
            var box = new Border { Background = Theme.Brush("surface-1"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Child = reminderList, Margin = new Thickness(0, 16, 0, 0) };
            var add = Btn(L.T("新增提醒", "New reminder"), "button-primary", () => EditReminder(null));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 16, 0, 0);
            return Section(L.T("自訂提醒", "Custom reminders"), L.T("到時間桌寵會出來說一聲（不做動作），按「知道了」才收起來。", "At the set time the pet comes out and says it (no exercise), and stays until you press “Got it”."),
                box, add,
                T(L.T("暫停提醒、安靜時段不會擋自訂提醒。全螢幕時等你離開全螢幕再說；桌寵收起來時改用 Windows 通知。電腦睡著錯過的，醒來 12 小時內會補說。", "Pause and quiet hours don't block custom reminders. In full screen it waits until you leave full screen; when the pet is hidden it uses a Windows notification. Reminders missed while the computer slept are said within 12 hours of waking."),
                    "t-caption", new Thickness(0, 12, 0, 0)));
        }

        void FillReminders()
        {
            if (reminderList == null) return;
            reminderList.Children.Clear();
            var now = DateTime.Now;
            var list = brain.Cfg.reminders.OrderBy(r => r.time, StringComparer.Ordinal).ThenBy(r => r.text).ToList();
            if (list.Count == 0)
            {
                reminderList.Children.Add(new Border { Padding = new Thickness(16, 14, 16, 14),
                    Child = T(L.T("還沒有提醒。按「新增提醒」設定第一個，例如每天 12:00「去吃飯囉」。", "No reminders yet. Press “New reminder” to add the first, for example “Lunch time” every day at 12:00."), "t-body-sm-subtle") });
                return;
            }
            var latin = (FontFamily)Application.Current.FindResource("font-latin");
            int n = 0;
            foreach (var x in list)
            {
                var r = x;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                var line = new TextBlock { Style = Theme.Style("t-body-sm"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
                line.Inlines.Add(new Run(r.time) { FontFamily = latin, FontWeight = FontWeights.SemiBold });
                line.Inlines.Add(new Run(L.T("　", "  ") + r.text));
                if (!r.enabled) line.Foreground = Theme.Brush("ink-subtle");
                text.Children.Add(line);
                text.Children.Add(T(r.RepeatText() + " · " + NextText(r, now), "t-caption", new Thickness(0, 2, 0, 0)));
                g.Children.Add(text);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var tryIt = Btn(L.T("試試看", "Try it"), "button-tertiary", () => brain.DemoSay(r.text));
                var edit = Btn(L.T("編輯", "Edit"), "button-tertiary", () => EditReminder(r));
                var del = Btn(L.T("刪除", "Delete"), "button-tertiary", () => DeleteReminder(r));
                AutomationProperties.SetName(tryIt, L.T("試試看：", "Try it: ") + r.text);
                AutomationProperties.SetName(edit, L.T("編輯：", "Edit: ") + r.text);
                AutomationProperties.SetName(del, L.T("刪除：", "Delete: ") + r.text);
                var on = new CheckBox { Style = Theme.Style("toggle"), IsChecked = r.enabled, Margin = new Thickness(12, 0, 0, 0) };
                AutomationProperties.SetName(on, L.T("啟用：", "Enable: ") + r.text);
                RoutedEventHandler h = (s, e) => ToggleReminder(r, on);
                on.Checked += h;
                on.Unchecked += h;
                actions.Children.Add(tryIt);
                actions.Children.Add(edit);
                actions.Children.Add(del);
                actions.Children.Add(on);
                Grid.SetColumn(actions, 1);
                g.Children.Add(actions);
                reminderList.Children.Add(new Border { Child = g, Padding = new Thickness(16, 10, 16, 10),
                    BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(0, n++ == 0 ? 0 : 1, 0, 0) });
            }
        }

        static string NextText(Reminder r, DateTime now)
        {
            if (!r.enabled)
            {
                DateTime d;
                TimeSpan t;
                bool past = r.repeat == "once" && Reminder.TryDate(r.date, out d) && Reminder.TryTime(r.time, out t) && d.Date + t <= now;
                return past ? L.T("已經提醒過了", "Already reminded") : L.T("已關閉", "Off");
            }
            var next = r.Next(now);
            if (next == null) return L.T("沒有下一次", "No next time");
            var day = next.Value.Date;
            string when = day == now.Date ? L.T("今天", "today") : day == now.Date.AddDays(1) ? L.T("明天", "tomorrow")
                : day.ToString("M/d", CultureInfo.InvariantCulture) + L.T("（週", " (") + Reminder.DayNames[Reminder.Weekday(day) - 1] + L.T("）", ")");
            return L.T("下次 ", "Next: ") + when + " " + next.Value.ToString("HH:mm");
        }

        void ToggleReminder(Reminder r, CheckBox on)
        {
            if (loading) return;
            bool want = on.IsChecked == true;
            if (want && r.repeat == "once" && r.Next(DateTime.Now) == null)
            {
                loading = true;
                on.IsChecked = false;
                loading = false;
                Dialog.Info(this, L.T("這個時間已經過了", "That time has passed"), L.T("單次提醒要先按「編輯」改成之後的日期或時間，才能再打開。", "For a one-time reminder, press “Edit” and choose a later date or time before turning it back on."));
                return;
            }
            var target = brain.Cfg.reminders.FirstOrDefault(x => x.id == r.id);
            if (target == null) { FillReminders(); return; }
            target.enabled = want;
            brain.ReminderEdited(target);   // 重新打開：從現在開始算，不補關掉期間的
            brain.Store.SaveConfig();
            Saved();
            FillReminders();
        }

        void DeleteReminder(Reminder r)
        {
            if (!Dialog.Confirm(this, L.T("刪除這個提醒？", "Delete this reminder?"), r.time + L.T("　", "  ") + r.text + L.T("（", " (") + r.RepeatText() + L.T("）", ")"), null, L.T("刪除", "Delete"), L.T("取消", "Cancel"), true)) return;
            brain.Cfg.reminders.RemoveAll(x => x.id == r.id);
            brain.Store.SaveConfig();
            Saved();
            FillReminders();
        }

        // 新增／編輯提醒的對話框：內容、時間、重複（每天／平日／自選星期／單次）
        void EditReminder(Reminder existing)
        {
            if (existing == null && brain.Cfg.reminders.Count >= Config.MaxReminders)
            {
                Dialog.Info(this, L.T("提醒太多了", "Too many reminders"), L.T("最多 " + Config.MaxReminders + " 個，先刪掉一些用不到的。", "The limit is " + Config.MaxReminders + ". Delete some you no longer need."));
                return;
            }
            var now = DateTime.Now;
            var r = existing != null ? existing.Clone() : new Reminder { time = now.AddHours(1).ToString("HH:00", CultureInfo.InvariantCulture) };
            string rep = r.repeat;
            var days = new HashSet<int>(r.days);

            var text = new TextBox { Style = Theme.Style("text-input"), MaxLength = Reminder.MaxText, Text = r.text };
            AutomationProperties.SetName(text, L.T("提醒內容", "Reminder text"));
            var count = T("", "t-caption", new Thickness(0, 4, 0, 0));
            Action counted = () => count.Text = text.Text.Length + " / " + Reminder.MaxText + L.T(" 字", " characters");
            text.TextChanged += (s, e) => { counted(); text.Tag = null; };
            counted();
            var textCol = new StackPanel();
            textCol.Children.Add(text);
            textCol.Children.Add(count);

            var time = new TextBox { Style = Theme.Style("text-input"), Width = 88, TextAlignment = TextAlignment.Center, Text = r.time };
            AutomationProperties.SetName(time, L.T("時間（24 小時制）", "Time (24-hour)"));
            time.GotKeyboardFocus += (s, e) => time.SelectAll();
            time.TextChanged += (s, e) => time.Tag = null;
            time.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Up && e.Key != Key.Down) return;
                TimeSpan t;
                int m = Reminder.TryTime(time.Text, out t) ? (int)t.TotalMinutes : now.Hour * 60;
                m = e.Key == Key.Up ? (m / 5 + 1) * 5 : ((m + 4) / 5 - 1) * 5;   // 每次 5 分鐘，順便對齊到 5 的倍數
                time.Text = TimeSpan.FromMinutes((m % 1440 + 1440) % 1440).ToString(@"hh\:mm");
                time.SelectAll();
                e.Handled = true;
            };
            var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
            timeRow.Children.Add(time);
            var timeHint = T(L.T("24 小時制；方向鍵上下每次 5 分鐘", "24-hour time; up/down arrows step 5 minutes"), "t-caption", new Thickness(12, 0, 0, 0));
            timeHint.VerticalAlignment = VerticalAlignment.Center;
            timeRow.Children.Add(timeHint);

            var date = new TextBox { Style = Theme.Style("text-input"), Width = 132, TextAlignment = TextAlignment.Center, Text = r.date };
            AutomationProperties.SetName(date, L.T("日期（年-月-日）", "Date (year-month-day)"));
            date.GotKeyboardFocus += (s, e) => date.SelectAll();
            date.TextChanged += (s, e) => date.Tag = null;
            date.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Up && e.Key != Key.Down) return;
                DateTime d;
                if (!Reminder.TryDate(date.Text, out d)) d = DateTime.Today;
                date.Text = d.AddDays(e.Key == Key.Up ? 1 : -1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                date.SelectAll();
                e.Handled = true;
            };
            var dateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            dateRow.Children.Add(date);
            var dateHint = T(L.T("方向鍵上下換一天", "Up/down arrows change the day"), "t-caption", new Thickness(12, 0, 0, 0));
            dateHint.VerticalAlignment = VerticalAlignment.Center;
            dateRow.Children.Add(dateHint);

            var dayRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var chips = new List<CheckBox>();
            for (int i = 1; i <= 7; i++)
            {
                int d = i;
                var chip = new CheckBox { Style = Theme.Style("chip"), Content = Reminder.DayNames[i - 1], IsChecked = days.Contains(i), Margin = new Thickness(0, 0, 6, 0) };
                AutomationProperties.SetName(chip, L.T("週", "") + Reminder.DayNames[i - 1]);
                chip.Checked += (s, e) => days.Add(d);
                chip.Unchecked += (s, e) => days.Remove(d);
                chips.Add(chip);
                dayRow.Children.Add(chip);
            }

            Action sync = () =>
            {
                dayRow.Visibility = rep == "days" ? Visibility.Visible : Visibility.Collapsed;
                dateRow.Visibility = rep == "once" ? Visibility.Visible : Visibility.Collapsed;
            };
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in new[] { new[] { "daily", L.T("每天", "Every day") }, new[] { "weekdays", L.T("平日", "Weekdays") }, new[] { "days", L.T("自選星期", "Chosen days") }, new[] { "once", L.T("單次", "Once") } })
            {
                var v = o[0];
                var rb = new RadioButton { Style = Theme.Style("segment"), Content = o[1], GroupName = "reminder-repeat", IsChecked = rep == v };
                rb.Checked += (s, e) =>
                {
                    rep = v;
                    if (v == "days" && days.Count == 0) chips[Reminder.Weekday(DateTime.Today) - 1].IsChecked = true;   // 先勾今天，方便改
                    if (v == "once" && date.Text.Trim().Length == 0)
                    {
                        TimeSpan t;
                        bool later = Reminder.TryTime(time.Text, out t) && DateTime.Today + t > DateTime.Now;
                        date.Text = (later ? DateTime.Today : DateTime.Today.AddDays(1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    sync();
                };
                seg.Children.Add(rb);
            }
            var segBox = new Border { Background = Theme.Brush("canvas"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9999), Padding = new Thickness(3), Child = seg, HorizontalAlignment = HorizontalAlignment.Left };
            var repeatCol = new StackPanel();
            repeatCol.Children.Add(segBox);
            repeatCol.Children.Add(dayRow);
            repeatCol.Children.Add(dateRow);
            sync();

            var error = ErrorText();
            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            form.ColumnDefinitions.Add(new ColumnDefinition());
            int row = 0;
            Action<string, FrameworkElement> addRow = (label, control) =>
            {
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = T(label, "t-body-sm", new Thickness(0, row == 0 ? 8 : 20, 0, 0), FontWeights.Medium);
                l.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(l, row);
                form.Children.Add(l);
                control.Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0);
                Grid.SetRow(control, row);
                Grid.SetColumn(control, 1);
                form.Children.Add(control);
                row++;
            };
            addRow(L.T("提醒內容", "Reminder text"), textCol);
            addRow(L.T("時間", "Time"), timeRow);
            addRow(L.T("重複", "Repeat"), repeatCol);
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(error, row);
            Grid.SetColumn(error, 1);
            error.Margin = new Thickness(0, 12, 0, 0);
            form.Children.Add(error);

            Func<bool> validate = () =>
            {
                text.Tag = time.Tag = date.Tag = null;
                var content = text.Text.Trim();
                TimeSpan t;
                DateTime d;
                bool timeOk = Reminder.TryTime(time.Text, out t);
                bool dateOk = Reminder.TryDate(date.Text, out d);
                string err = null;
                if (content.Length == 0) { err = L.T("請輸入要提醒的內容", "Enter the reminder text"); text.Tag = "error"; }
                else if (!timeOk) { err = L.T("時間請用 24 小時制，例如 15:30", "Use 24-hour time, for example 15:30"); time.Tag = "error"; }
                else if (rep == "days" && days.Count == 0) err = L.T("至少選一天", "Choose at least one day");
                else if (rep == "once" && !dateOk) { err = L.T("日期請寫成 2026-11-03 這樣", "Write the date like 2026-11-03"); date.Tag = "error"; }
                else if (rep == "once" && d.Date + t <= DateTime.Now) { err = L.T("這個時間已經過了，請改成之後的日期或時間", "That time has passed. Choose a later date or time"); date.Tag = time.Tag = "error"; }
                if (err == null)
                {
                    r.text = content;
                    r.time = t.ToString(@"hh\:mm");
                    r.repeat = rep;
                    r.days = days.OrderBy(x => x).ToList();
                    r.date = rep == "once" ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
                    r.enabled = true;
                    if (!r.Normalize()) err = L.T("內容有問題，請再檢查一次", "Something is wrong with the text. Please check again");
                }
                error.Text = err ?? "";
                error.Visibility = err == null ? Visibility.Collapsed : Visibility.Visible;
                return err == null;
            };

            if (!Dialog.Confirm(this, existing == null ? L.T("新增提醒", "New reminder") : L.T("編輯提醒", "Edit reminder"), L.T("到時間桌寵會出來說這句話。", "At the set time the pet comes out and says this."), form, L.T("儲存", "Save"), L.T("取消", "Cancel"), false, validate, text)) return;
            var list = brain.Cfg.reminders;
            int at = existing == null ? -1 : list.FindIndex(x => x.id == existing.id);
            if (at >= 0) list[at] = r; else list.Add(r);
            brain.ReminderEdited(r);   // 從現在開始算
            brain.Store.SaveConfig();
            Saved();
            FillReminders();
        }

        // ---------- Google 月曆 ----------

        FrameworkElement BuildCalendar()
        {
            calDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            calBadge = T("", "t-caption");
            calBadge.Foreground = Theme.Brush("ink-muted");
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
            badgeRow.Children.Add(calDot);
            badgeRow.Children.Add(calBadge);
            var badge = new Border { Background = Theme.Brush("surface-2"), CornerRadius = new CornerRadius(9999), Padding = new Thickness(8, 2, 8, 2), Child = badgeRow,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            calSync = Btn(L.T("立即同步", "Sync now"), "button-secondary", () => brain.Calendar.SyncNow());
            var status = new StackPanel { Orientation = Orientation.Horizontal };
            status.Children.Add(badge);
            status.Children.Add(calSync);
            calDetail = T("", "t-caption", new Thickness(0, 6, 0, 0));
            var statusRow = new StackPanel();
            statusRow.Children.Add(Row(L.T("連動狀態", "Status"), null, status));
            statusRow.Children.Add(calDetail);

            // 連結步驟
            var copy = Btn(L.T("複製程式碼", "Copy code"), "button-primary", CopyScript);
            var open = Btn(L.T("開啟 Apps Script", "Open Apps Script"), "button-secondary", () => OpenUrl("https://script.new"));
            calUrl = new TextBox { Style = Theme.Style("text-input") };
            AutomationProperties.SetName(calUrl, L.T("Apps Script 網頁應用程式網址", "Apps Script web app URL"));
            calUrl.TextChanged += (s, e) => { calUrl.Tag = null; calUrlError.Visibility = Visibility.Collapsed; };
            calUrl.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { LinkUrl(); e.Handled = true; } };
            var link = Btn(L.T("連結", "Link"), "button-primary", LinkUrl);
            link.Margin = new Thickness(8, 0, 0, 0);
            var urlRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            urlRow.ColumnDefinitions.Add(new ColumnDefinition());
            urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            urlRow.Children.Add(calUrl);
            Grid.SetColumn(link, 1);
            urlRow.Children.Add(link);
            calUrlError = ErrorText();
            var step3 = new StackPanel();
            step3.Children.Add(Row(L.T("3. 貼上網頁應用程式網址", "3. Paste the web app URL"), L.T("部署完成畫面上的網址，結尾是 /exec。", "The URL on the deployment screen, ending in /exec."), null));
            step3.Children.Add(urlRow);
            step3.Children.Add(calUrlError);
            calSteps = new StackPanel();
            calSteps.Children.Add(Label(L.T("連結步驟（大約 3 分鐘）", "How to link (about 3 minutes)")));
            calSteps.Children.Add(Group(
                Row(L.T("1. 複製 Apps Script 程式碼", "1. Copy the Apps Script code"), L.T("程式碼裡有這組連結專用的金鑰；複製時不會進剪貼簿歷程記錄。", "The code contains a key just for this link. It is kept out of clipboard history."), copy),
                Row(L.T("2. 貼到 Apps Script 並部署", "2. Paste into Apps Script and deploy"), L.T("用要讀行事曆的 Google 帳號開新專案、整段貼上、儲存 → 部署 → 新增部署作業 → 網頁應用程式（執行身分：我；誰可以存取：所有人）→ 授權。程式碼最上面也寫了步驟。", "Open a new project with the Google account whose calendar you want, paste the whole code, save → Deploy → New deployment → Web app (Execute as: Me; Who has access: Anyone) → authorize. The steps are also at the top of the code."), open),
                step3));

            // 要讀的行事曆
            calPickerList = new StackPanel();
            calPicker = new StackPanel();
            calPicker.Children.Add(Label(L.T("要讀的行事曆", "Calendars to read")));
            calPicker.Children.Add(T(L.T("勾選的行事曆才會用來判斷開會、跳出活動提醒。同事分享給你看的行事曆、節日通常不用勾。", "Only ticked calendars are used for meetings and event reminders. Calendars shared by colleagues and holiday calendars usually don't need ticking."), "t-body-sm-subtle"));
            calPicker.Children.Add(new Border { Background = Theme.Brush("surface-1"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Child = calPickerList, Margin = new Thickness(0, 16, 0, 0) });

            // 怎麼用
            TextBlock e1 = ErrorText();
            var options = Group(
                Row(L.T("開會時暫停伸展、喝水提醒", "Pause stretch and water reminders in meetings"), L.T("行事曆上有忙碌的活動時先不叫你，結束後再提醒。全天活動、標成「有空」、你回覆不參加的不算。", "While a busy event is on your calendar it leaves you alone and reminds you afterwards. All-day events, events marked Free, and ones you declined don't count."),
                    Toggle(() => brain.Calendar.Link.pauseDuringEvents, v => UpdateLink(l => l.pauseDuringEvents = v), L.T("開會時暫停伸展、喝水提醒", "Pause stretch and water reminders in meetings"))),
                Row(L.T("活動開始前跳出提醒", "Remind before events"), L.T("桌寵會說活動名稱、時間和地點；不受暫停提醒、安靜時段影響。", "The pet says the event's name, time and place. Not affected by Pause or quiet hours."),
                    Toggle(() => brain.Calendar.Link.remind, v => UpdateLink(l => l.remind = v), L.T("活動開始前跳出提醒", "Remind before events"))),
                Row(L.T("提前幾分鐘", "Minutes ahead"), L.T("0 = 活動開始的時候。", "0 = when the event starts."),
                    Number(() => brain.Calendar.Link.remindMinutes, v => UpdateLink(l => l.remindMinutes = v), 0, () => 120, L.T("分鐘", "min"), e1, L.T("活動提醒提前幾分鐘", "Minutes ahead for event reminders")), e1));

            // 連動設定檔
            var openFolder = Btn(L.T("開啟資料夾", "Open folder"), "button-secondary", () => OpenFolder(GoogleCalendar.LinkFile));
            calUnlink = Btn(L.T("解除連結…", "Unlink…"), "button-danger", Unlink);
            var file = Group(
                Row(L.T("連動設定檔", "Link settings file"), GoogleCalendar.LinkFile, openFolder),
                Row(L.T("解除連結", "Unlink"), L.T("清掉網址和金鑰；有同步這個資料夾的電腦都會一起解除。Apps Script 專案不會被刪。", "Clears the URL and key; every computer syncing this folder is unlinked too. The Apps Script project is not deleted."), calUnlink));

            return Section(L.T("Google 月曆", "Google Calendar"), L.T("讀取你的 Google 月曆（只讀不寫）：開會時先不叫你伸展、喝水；活動快開始時，桌寵會跳出來提醒你。", "Reads your Google Calendar (read-only): no stretch or water reminders during meetings, and the pet pops up when an event is about to start."),
                Group(statusRow), calSteps, calPicker, Label(L.T("怎麼用", "Options")), options, Label(L.T("連動設定檔", "Link settings file")), file,
                T(L.T("這個檔案放在專案資料夾；資料夾用 Dropbox 之類的雲端硬碟同步的話，每台電腦會共用同一份連結。裡面有金鑰，已排除在 git 之外，不會傳到 GitHub。", "This file lives in the project folder. If the folder is synced by a cloud drive such as Dropbox, every computer shares the same link. It contains the key, is excluded from git and is never pushed to GitHub."),
                    "t-caption", new Thickness(0, 12, 0, 0)));
        }

        // Brain 收到行事曆有變化（同步完成、連動設定檔被改）時呼叫
        public void CalendarChanged() { RefreshCalendar(); }

        void RefreshCalendar()
        {
            if (calBadge == null) return;
            var cal = brain.Calendar;
            var now = DateTime.Now;
            bool linked = cal.Link.Linked, working = linked && cal.LastSync > DateTime.MinValue;
            string state, detail, dot = "ink-subtle";
            bool bad = false;
            if (!linked)
            {
                state = L.T("未連結", "Not linked");
                detail = cal.LinkError ?? (string.IsNullOrEmpty(cal.Link.token) ? L.T("照下面三個步驟連結，大約 3 分鐘。", "Follow the three steps below; it takes about 3 minutes.") : L.T("程式碼複製過了；部署好之後把網址貼在第 3 步。", "Code copied. After deploying, paste the URL in step 3."));
                bad = cal.LinkError != null;
            }
            else if (cal.SyncError != null)
            {
                state = L.T("連不上", "Can't connect");
                dot = "semantic-danger";
                bad = true;
                detail = cal.SyncError + (working ? L.T("（上次成功是 ", " (last success ") + cal.LastSync.ToString("HH:mm") + L.T("）", ")") : "");
            }
            else if (!working)
            {
                state = L.T("同步中…", "Syncing…");
                detail = L.T("正在讀取行事曆…", "Reading calendars…");
            }
            else
            {
                state = L.T("已連結", "Linked");
                dot = "semantic-success";
                int n = cal.Link.calendars.Count > 0 ? cal.Link.calendars.Count : 1;
                var next = cal.NextEvent(now);
                detail = L.T("上次同步 ", "Last sync ") + cal.LastSync.ToString("HH:mm") + L.T(" · 讀取 ", " · ") + n + L.T(" 個行事曆\n", " calendars\n") + (next == null ? L.T("接下來 36 小時沒有活動", "No events in the next 36 hours")
                    : L.T("接下來：", "Next: ") + (next.Start.Date == now.Date ? L.T("今天 ", "today ") : next.Start.Date == now.Date.AddDays(1) ? L.T("明天 ", "tomorrow ") : next.Start.ToString("M/d ")) +
                      next.Start.ToString("HH:mm") + L.T("　", "  ") + (string.IsNullOrEmpty(next.Title) ? L.T("（沒有標題）", "(no title)") : next.Title));
                var meeting = cal.BusyNow(now);
                if (meeting != null) detail += L.T("\n現在在開會：", "\nIn a meeting now: ") + meeting.Title + L.T("（到 ", " (until ") + meeting.End.ToString("HH:mm") + L.T("）", ")");
            }
            calBadge.Text = state;
            calDot.Background = Theme.Brush(dot);
            calDetail.Text = detail;
            if (bad) calDetail.Foreground = Theme.Brush("semantic-danger"); else calDetail.ClearValue(TextBlock.ForegroundProperty);
            calSync.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            calSync.IsEnabled = !cal.Syncing;
            calSync.Content = cal.Syncing ? L.T("同步中…", "Syncing…") : L.T("立即同步", "Sync now");
            calSteps.Visibility = working ? Visibility.Collapsed : Visibility.Visible;   // 還沒成功同步過就留著步驟，方便修正
            calPicker.Visibility = linked && cal.Calendars.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            calUnlink.IsEnabled = cal.Link.url.Length > 0 || cal.Link.token.Length > 0;
            if (calUrl.Text.Trim().Length == 0 && cal.Link.url.Length > 0) calUrl.Text = cal.Link.url;
            FillCalendarPicker();
        }

        void FillCalendarPicker()
        {
            var cal = brain.Calendar;
            var selected = SelectedCalendars();
            var sig = string.Join("|", cal.Calendars.Select(c => c.Id + "=" + c.Name)) + "#" + string.Join("|", selected);
            if (sig == calPickerSig) return;   // 沒變就不重畫（不打斷鍵盤焦點）
            calPickerSig = sig;
            calPickerList.Children.Clear();
            int n = 0;
            foreach (var c in cal.Calendars.OrderByDescending(c => c.Primary))
            {
                var info = c;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
                text.Children.Add(T((info.Name.Length > 0 ? info.Name : info.Id) + (info.Primary ? L.T("（主要）", " (primary)") : ""), "t-body-sm", null, FontWeights.Medium));
                if (info.Name != info.Id && info.Name.Length > 0) text.Children.Add(T(info.Id, "t-caption", new Thickness(0, 2, 0, 0)));
                g.Children.Add(text);
                var cb = new CheckBox { Style = Theme.Style("check"), IsChecked = selected.Contains(info.Id) };
                AutomationProperties.SetName(cb, L.T("讀取行事曆 ", "Read calendar ") + info.Name);
                RoutedEventHandler h = (s, e) => ToggleCalendar(info.Id, cb);
                cb.Checked += h;
                cb.Unchecked += h;
                Grid.SetColumn(cb, 1);
                g.Children.Add(cb);
                calPickerList.Children.Add(new Border { Child = g, Padding = new Thickness(16, 12, 16, 12),
                    BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(0, n++ == 0 ? 0 : 1, 0, 0) });
            }
        }

        // 沒特別選過 = 只讀主要行事曆
        List<string> SelectedCalendars()
        {
            var cal = brain.Calendar;
            return cal.Link.calendars.Count > 0 ? cal.Link.calendars.ToList() : cal.Calendars.Where(c => c.Primary).Select(c => c.Id).ToList();
        }

        void ToggleCalendar(string id, CheckBox cb)
        {
            if (loading) return;
            var ids = SelectedCalendars();
            if (cb.IsChecked == true) { if (!ids.Contains(id)) ids.Add(id); }
            else
            {
                ids.Remove(id);
                if (ids.Count == 0)
                {
                    loading = true;
                    cb.IsChecked = true;
                    loading = false;
                    Dialog.Info(this, L.T("至少要留一個行事曆", "Keep at least one calendar"), L.T("全部取消就沒有行事曆可以讀了；不想用的話，到下面按「解除連結」。", "With none ticked there is nothing to read. To stop using it, press “Unlink” below."));
                    return;
                }
            }
            if (UpdateLink(l => l.calendars = ids)) { Saved(); brain.Calendar.SyncNow(); }
        }

        // 改連動設定：一律複製一份改好再存，GoogleCalendar 才分得出網址、金鑰、行事曆有沒有換
        bool UpdateLink(Action<CalendarLink> change)
        {
            var next = brain.Calendar.Link.Copy();
            change(next);
            string error;
            if (brain.Calendar.Set(next, out error)) return true;
            Dialog.Info(this, L.T("存不了連動設定檔", "Can't save the link settings file"), error + "\n" + GoogleCalendar.LinkFile);
            RefreshCalendar();
            return false;
        }

        void CopyScript()
        {
            var token = brain.Calendar.Link.token;
            if (string.IsNullOrEmpty(token))
            {
                var fresh = GoogleCalendar.NewToken();
                if (!UpdateLink(l => l.token = fresh)) return;
                token = fresh;
            }
            string code;
            try { code = GoogleCalendar.Script(token); }
            catch (Exception e) { Dialog.Info(this, L.T("拿不到程式碼", "Can't get the code"), e.Message); return; }
            if (!CopyPrivate(code)) { Dialog.Info(this, L.T("複製失敗", "Copy failed"), L.T("剪貼簿正被別的程式占用，請再按一次。", "Another program is using the clipboard. Please press again.")); return; }
            Saved();
            savedNote.Text = L.T("程式碼已複製", "Code copied");
            RefreshCalendar();
        }

        // 複製到剪貼簿，但不進「剪貼簿歷程記錄」、不同步到雲端剪貼簿（程式碼裡有金鑰）
        static bool CopyPrivate(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetText(text, TextDataFormat.UnicodeText);
                    data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                    data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(80); }   // 別的程式正開著剪貼簿：等一下再試
            }
            return false;
        }

        void LinkUrl()
        {
            var url = calUrl.Text.Trim();
            string err = null;
            if (url.Length == 0) err = L.T("請貼上部署後拿到的網址", "Paste the URL you got after deploying");
            else if (!GoogleCalendar.ValidUrl(url)) err = L.T("這不是 Apps Script 網頁應用程式的網址：要像 https://script.google.com/macros/s/…/exec", "This is not an Apps Script web app URL. It should look like https://script.google.com/macros/s/…/exec");
            else if (string.IsNullOrEmpty(brain.Calendar.Link.token)) err = L.T("請先在第 1 步複製程式碼（裡面有金鑰），部署好再貼網址", "Copy the code in step 1 first (it contains the key), deploy, then paste the URL");
            if (err != null)
            {
                calUrl.Tag = "error";
                calUrlError.Text = err;
                calUrlError.Visibility = Visibility.Visible;
                return;
            }
            if (url == brain.Calendar.Link.url) { brain.Calendar.SyncNow(); return; }   // 同一個網址：重試一次
            if (UpdateLink(l => l.url = url)) { Saved(); brain.Calendar.SyncNow(); }
        }

        void Unlink()
        {
            if (!Dialog.Confirm(this, L.T("解除 Google 月曆連結？", "Unlink Google Calendar?"),
                L.T("會清掉連動設定檔裡的網址和金鑰；有同步這個資料夾的其他電腦也會一起解除。Apps Script 專案不會被刪，不用的話可以到 script.google.com 刪除。", "Clears the URL and key from the link settings file; other computers syncing this folder are unlinked too. The Apps Script project is not deleted; you can delete it at script.google.com if you no longer need it."),
                null, L.T("解除連結", "Unlink"), L.T("取消", "Cancel"), true)) return;
            if (UpdateLink(l => { l.url = ""; l.token = ""; l.calendars = new List<string>(); }))
            {
                calUrl.Text = "";
                Saved();
            }
        }

        void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Dialog.Info(this, L.T("打不開瀏覽器", "Can't open the browser"), e.Message); }
        }

        void OpenFolder(string file)
        {
            try
            {
                if (File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(file) + "\"");
            }
            catch (Exception e) { Dialog.Info(this, L.T("打不開資料夾", "Can't open the folder"), e.Message); }
        }
    }
}
