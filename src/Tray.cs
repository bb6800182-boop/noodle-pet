using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoodlePet
{
    // 系統匣圖示與選單（右鍵點桌寵或泡泡也會叫出同一份）；選單配色跟設計 token 一致
    public class Tray : IDisposable
    {
        readonly NotifyIcon icon = new NotifyIcon();
        readonly Brain brain;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem info, fixedItem, walkItem, pauseItem, resumeItem, hideItem;
        readonly ToolStripMenuItem[] countdownItems;
        readonly ToolStripMenuItem quickTimer, quickPeek;
        string iconFor;
        Icon current;

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        public Tray(Brain b)
        {
            brain = b;
            var title = new ToolStripMenuItem(Program.AppName + " v" + Program.Version) { Enabled = false };
            info = new ToolStripMenuItem("") { Enabled = false };
            var now = new ToolStripMenuItem("現在做一組", null, (o, e) => brain.NowPlease(null));
            var hello = new ToolStripMenuItem("打招呼", null, (o, e) => brain.ShowHello());
            fixedItem = new ToolStripMenuItem("固定位置（可拖曳）", null, (o, e) => brain.SetWalking(false));
            walkItem = new ToolStripMenuItem("在工作列上走來走去", null, (o, e) => brain.SetWalking(true));
            pauseItem = new ToolStripMenuItem("暫停提醒");
            pauseItem.DropDownItems.Add("30 分鐘", null, (o, e) => brain.PauseFor(TimeSpan.FromMinutes(30)));
            pauseItem.DropDownItems.Add("1 小時", null, (o, e) => brain.PauseFor(TimeSpan.FromHours(1)));
            pauseItem.DropDownItems.Add("2 小時", null, (o, e) => brain.PauseFor(TimeSpan.FromHours(2)));
            pauseItem.DropDownItems.Add("到明天", null, (o, e) => brain.PauseFor(null));
            resumeItem = new ToolStripMenuItem("繼續提醒", null, (o, e) => brain.Resume());
            hideItem = new ToolStripMenuItem("收起桌寵", null, (o, e) => brain.SetHidden(!brain.Hidden));

            // 示範：照 Noodle 的 Film 選單，可以倒數、縮短計時；示範不記錄、不影響輪替
            var demo = new ToolStripMenuItem("示範");
            var cd = new ToolStripMenuItem("倒數");
            int[] secs = { 0, 3, 5, 10 };
            countdownItems = new ToolStripMenuItem[secs.Length];
            for (int i = 0; i < secs.Length; i++)
            {
                int s = secs[i];
                countdownItems[i] = new ToolStripMenuItem(s == 0 ? "不倒數" : s + " 秒", null, (o, e) => { brain.DemoCountdown = s; Refresh(); });
                cd.DropDownItems.Add(countdownItems[i]);
            }
            quickTimer = new ToolStripMenuItem("計時縮成 5 秒", null, (o, e) => { brain.DemoQuickTimer = !brain.DemoQuickTimer; Refresh(); });
            quickPeek = new ToolStripMenuItem("3 秒就探頭", null, (o, e) => { brain.DemoQuickPeek = !brain.DemoQuickPeek; Refresh(); });
            demo.DropDownItems.Add(cd);
            demo.DropDownItems.Add(quickTimer);
            demo.DropDownItems.Add(quickPeek);
            demo.DropDownItems.Add(new ToolStripSeparator());
            foreach (var ex in brain.All)
            {
                var id = ex.id;
                demo.DropDownItems.Add(ex.name, null, (o, e) => brain.Demo(id));
            }
            demo.DropDownItems.Add(new ToolStripSeparator());
            demo.DropDownItems.Add("開場導覽", null, (o, e) => brain.Demo("intro"));
            demo.DropDownItems.Add("垂降進場", null, (o, e) => brain.Demo("drop"));
            demo.DropDownItems.Add("探頭提醒", null, (o, e) => brain.Demo("peek"));
            demo.DropDownItems.Add("完成慶祝", null, (o, e) => brain.Demo("celebrate"));
            demo.DropDownItems.Add("Claude 做完了", null, (o, e) => brain.Demo("claude"));

            var settings = new ToolStripMenuItem("設定…", null, (o, e) => brain.OpenSettings(null));
            var quit = new ToolStripMenuItem("結束", null, (o, e) => System.Windows.Application.Current.Shutdown());

            menu = new ContextMenuStrip { Font = new Font("Microsoft JhengHei UI", 9.5f), Renderer = new DarkRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
            menu.Items.AddRange(new ToolStripItem[] {
                title, info, new ToolStripSeparator(), now, hello, new ToolStripSeparator(), fixedItem, walkItem,
                new ToolStripSeparator(), pauseItem, resumeItem, hideItem, demo, new ToolStripSeparator(), settings, quit });
            foreach (ToolStripItem it in menu.Items) Pad(it);
            menu.Opening += (o, e) => Refresh();
            icon.ContextMenuStrip = menu;
            icon.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) brain.ShowHello(); };
            icon.Visible = true;
        }

        static void Pad(ToolStripItem it)
        {
            it.Padding = new Padding(4, 3, 4, 3);
            var m = it as ToolStripMenuItem;
            if (m == null) return;
            var dd = m.DropDown as ToolStripDropDownMenu;
            if (dd != null) { dd.Renderer = new DarkRenderer(); dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
            foreach (ToolStripItem sub in m.DropDownItems) Pad(sub);
        }

        public void Refresh()
        {
            info.Text = brain.StatusLine();
            fixedItem.Checked = !brain.Walking;
            walkItem.Checked = brain.Walking;
            bool paused = brain.IsPaused;
            resumeItem.Visible = paused;
            pauseItem.Text = paused ? "暫停提醒（已暫停）" : "暫停提醒";
            hideItem.Text = brain.Hidden ? "顯示桌寵" : "收起桌寵";
            int[] secs = { 0, 3, 5, 10 };
            for (int i = 0; i < secs.Length; i++) countdownItems[i].Checked = brain.DemoCountdown == secs[i];
            quickTimer.Checked = brain.DemoQuickTimer;
            quickPeek.Checked = brain.DemoQuickPeek;
            var tip = Program.AppName + " · " + brain.StatusLine();
            icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            UpdateIcon();
        }

        // 系統匣圖示跟著角色換；先高品質縮到系統要的大小，比讓 Windows 縮清楚
        void UpdateIcon()
        {
            var ch = brain.Cfg.character;
            if (ch == iconFor) return;
            iconFor = ch;
            var size = SystemInformation.SmallIconSize;
            var name = Res.Has("tray-" + ch + ".png") ? "tray-" + ch + ".png" : "tray-rigatoni.png";
            using (var s = Res.Open(name))
            using (var src = new Bitmap(s))
            using (var bmp = new Bitmap(size.Width, size.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, 0, 0, size.Width, size.Height);
                }
                var old = current;
                var h = bmp.GetHicon();
                current = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                icon.Icon = current;
                if (old != null) old.Dispose();
            }
        }

        // 借用 NotifyIcon 內建的顯示選單方式，點別處才會正確收起
        public void ShowMenu()
        {
            var show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            if (show != null) show.Invoke(icon, null);
        }

        public void Warn(string text) { icon.ShowBalloonTip(5000, Program.AppName, text, ToolTipIcon.Warning); }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            if (current != null) current.Dispose();
        }

        // 深色選單：surface-2 底、hairline 框線、ink 文字、primary 勾選
        class DarkRenderer : ToolStripProfessionalRenderer
        {
            static readonly Color Surface = ColorTranslator.FromHtml("#141516"), Hover = ColorTranslator.FromHtml("#23252a"),
                Line = ColorTranslator.FromHtml("#34343a"), Ink = ColorTranslator.FromHtml("#f7f8f8"),
                Subtle = ColorTranslator.FromHtml("#8a8f98"), Primary = ColorTranslator.FromHtml("#5e6ad2");

            public DarkRenderer() : base(new Colors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Ink : Subtle;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Subtle;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var r = e.ImageRectangle;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Primary, Math.Max(1.6f, r.Height / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] {
                        new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                        new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                        new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f) });
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (var pen = new Pen(Line)) e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }

            class Colors : ProfessionalColorTable
            {
                public override Color ToolStripDropDownBackground { get { return Surface; } }
                public override Color MenuBorder { get { return Line; } }
                public override Color MenuItemBorder { get { return Hover; } }
                public override Color MenuItemSelected { get { return Hover; } }
                public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
                public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
                public override Color MenuItemPressedGradientBegin { get { return Hover; } }
                public override Color MenuItemPressedGradientEnd { get { return Hover; } }
                public override Color ImageMarginGradientBegin { get { return Surface; } }
                public override Color ImageMarginGradientMiddle { get { return Surface; } }
                public override Color ImageMarginGradientEnd { get { return Surface; } }
                public override Color CheckBackground { get { return Surface; } }
                public override Color CheckSelectedBackground { get { return Hover; } }
                public override Color CheckPressedBackground { get { return Hover; } }
                public override Color SeparatorDark { get { return Line; } }
                public override Color SeparatorLight { get { return Line; } }
            }
        }
    }
}
