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

                var settings = new SettingsWindow(brain);
                foreach (var id in new[] { "remind", "pet", "moves", "quiet", "claude", "general" })
                {
                    settings.Go(id);
                    Save((FrameworkElement)settings.Content, "settings-" + id, 920, 1500, "#010102");
                }
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
