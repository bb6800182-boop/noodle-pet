using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoodlePet
{
    // 開發用：把泡泡各狀態、設定視窗各分類畫成 PNG（不會顯示在螢幕上、不改任何設定），檢查版面用
    // 用法：build.cmd snapshot，然後 dist\noodle-snapshot.exe [輸出資料夾]
    static class Snapshot
    {
        static string dir;

        [STAThread]
        static void Main(string[] args)
        {
            dir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "noodle-snap");
            Directory.CreateDirectory(dir);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                Theme.Load(app);
                var brain = new Brain(Store.Load());
                var pet = new PetWindow(brain);
                brain.Pet = pet;
                pet.Init();
                BubbleWindow.Headless = true;
                var bubble = new BubbleWindow(brain, pet);
                brain.Bubble = bubble;
                var ex = brain.All;

                bubble.ShowHello("再 12 分鐘叫你起來動一動", "今天完成 2 組");
                Bubble(bubble, "bubble-1-hello");
                bubble.ShowNudge(ex[0], "已經坐了 32 分鐘", 1, 8, false);
                Bubble(bubble, "bubble-2-nudge");
                bubble.ToggleSteps();
                Bubble(bubble, "bubble-3-nudge-steps");
                bubble.ShowNudge(ex[1], "該伸展囉！", 2, 8, true);
                Bubble(bubble, "bubble-4-announce");
                bubble.ShowStretch(ex[2], 50, 3);
                bubble.ClaudeDone();
                Bubble(bubble, "bubble-5-stretch");
                bubble.ShowDone(3);
                Bubble(bubble, "bubble-6-done");
                bubble.ShowBye(10);
                Bubble(bubble, "bubble-7-bye");
                for (int i = 0; i < BubbleWindow.IntroCount; i++)
                {
                    bubble.ShowIntro(i);
                    Bubble(bubble, "bubble-8-intro-" + i);
                }
                var now = DateTime.Now;
                bubble.ShowWater(3);
                Bubble(bubble, "bubble-9-water");
                bubble.ShowDone("喝好了！", "今天第 3 杯水了");
                Bubble(bubble, "bubble-10-water-done");
                var three = now.Date.AddHours(15); // 標題寫三點，泡泡上的時間也要是 15:00（介紹頁用這張截圖）
                bubble.ShowSay(new Say { Kind = "custom", Title = "三點了，記得回覆廠商的信！", At = three, Due = three }, three, 0);
                Bubble(bubble, "bubble-11-say");
                bubble.ShowSay(new Say { Kind = "event", IsEvent = true, Title = "產品週會", Place = "三樓會議室",
                    At = now.AddMinutes(10), End = now.AddMinutes(70), Due = now }, now, 1);
                Bubble(bubble, "bubble-12-event");

                // 設定頁：放幾筆示範資料（只在記憶體裡，不存檔）
                brain.Cfg.reminders.Add(new Reminder { text = "去吃飯囉", time = "12:00" });
                brain.Cfg.reminders.Add(new Reminder { text = "週報記得交", time = "16:30", repeat = "days", days = new System.Collections.Generic.List<int> { 5 } });
                brain.Cfg.reminders.Add(new Reminder { text = "打電話給包材廠商確認交期", time = "10:00", repeat = "once",
                    date = now.AddDays(1).ToString("yyyy-MM-dd"), enabled = false });
                var settings = new SettingsWindow(brain);
                foreach (var id in new[] { "remind", "moves", "water", "reminders", "calendar", "pet", "quiet", "claude", "general" })
                {
                    settings.Go(id);
                    Save((FrameworkElement)settings.Content, "settings-" + id, 920, 1500, "#010102");
                }
                Save(settings.LicenseView(), "license", 592, 0, "#010102");
                brain.Calendar.Link = new CalendarLink { url = "https://script.google.com/macros/s/AKfycbx0000000000000000000000000/exec", token = "demo" };
                brain.Calendar.Calendars = new System.Collections.Generic.List<CalInfo> {
                    new CalInfo { Id = "me@example.com", Name = "me@example.com", Primary = true },
                    new CalInfo { Id = "work@example.com", Name = "work@example.com" },
                    new CalInfo { Id = "zh-tw.taiwan#holiday@group.v.calendar.google.com", Name = "台灣的節慶假日" } };
                brain.Calendar.Events = new System.Collections.Generic.List<CalEvent> {
                    new CalEvent { Key = "a", Title = "產品週會", Start = now.AddMinutes(-10), End = now.AddMinutes(50), Busy = true },
                    new CalEvent { Key = "b", Title = "供應商來訪", Start = now.AddHours(2), End = now.AddHours(3), Busy = true } };
                brain.Calendar.LastSync = now;
                settings.Go("calendar");
                Save((FrameworkElement)settings.Content, "settings-calendar-linked", 920, 1500, "#010102");
                Console.WriteLine("ok: " + dir);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(dir, "error.txt"), e.ToString());
                Console.WriteLine(e);
            }
        }

        static void Bubble(BubbleWindow b, string name)
        {
            Save((FrameworkElement)b.Content, name, 0, 0, "#3a4150");
        }

        // 量好版面後用 VisualBrush 畫到 1.5 倍 DPI 的圖上（跟 150% 縮放的螢幕一樣）
        static void Save(FrameworkElement el, string name, double width, double height, string backdrop)
        {
            el.UpdateLayout();   // 先把內層「顯示／隱藏」造成的版面變動算完，量到的大小才準
            el.InvalidateMeasure();
            el.Measure(new Size(width > 0 ? width : double.PositiveInfinity, height > 0 ? height : double.PositiveInfinity));
            var size = new Size(width > 0 ? width : el.DesiredSize.Width, height > 0 ? height : el.DesiredSize.Height);
            el.Arrange(new Rect(size));
            el.UpdateLayout();
            if (height > 0)   // 設定頁：裁到內容實際高度
            {
                var fe = el as System.Windows.Controls.Grid;
                double h = 0;
                if (fe != null)
                    foreach (UIElement c in fe.Children)
                    {
                        var sv = c as System.Windows.Controls.ScrollViewer;
                        if (sv != null && sv.Content is FrameworkElement) h = Math.Max(h, ((FrameworkElement)sv.Content).ActualHeight + sv.Padding.Top + sv.Padding.Bottom);
                    }
                if (h > 0 && h < size.Height) { size = new Size(size.Width, Math.Max(560, h)); el.Measure(size); el.Arrange(new Rect(size)); el.UpdateLayout(); }
            }
            const double dpi = 144;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle((Brush)new BrushConverter().ConvertFromString(backdrop), null, new Rect(size));
                dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(size));
            }
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi / 96), (int)Math.Ceiling(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = File.Create(Path.Combine(dir, name + ".png"))) enc.Save(f);
        }
    }
}
