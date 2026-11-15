using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NoodlePet
{
    // 設定視窗：左邊分類、右邊內容；改了就自動存檔並立即套用（喝水、自訂提醒、Google 月曆在 SettingsMore.cs）
    public partial class SettingsWindow : Window
    {
        readonly Brain brain;
        readonly Dictionary<string, FrameworkElement> sections = new Dictionary<string, FrameworkElement>();
        readonly Dictionary<string, RadioButton> nav = new Dictionary<string, RadioButton>();
        readonly List<Action> refreshers = new List<Action>();
        readonly List<Player> players = new List<Player>();
        readonly ScrollViewer scroller;
        readonly StackPanel host;
        readonly TextBlock savedNote, todayText;
        readonly DispatcherTimer savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        readonly Image navPet = new Image { Stretch = Stretch.None };
        readonly Player navPlayer;
        Sprites navSprites;
        StackPanel exerciseList;
        string thumbsFor;
        bool loading;
        // Claude Code 區
        TextBlock claudeBadge, claudeNotes;
        Border claudeDot;
        Button claudeButton;
        string claudeState;

        public SettingsWindow(Brain b)
        {
            brain = b;
            Title = Program.AppName + L.T(" 設定", " Settings");
            Width = 920;
            Height = 700;
            MinWidth = 780;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Brush("canvas");
            UseLayoutRounding = true;
            FontFamily = (FontFamily)Application.Current.FindResource("font-text");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            // 左側：桌寵、今天次數、分類
            var side = new DockPanel { Margin = new Thickness(16, 20, 16, 16) };
            var head = new Grid { Margin = new Thickness(4, 0, 0, 20) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            navPlayer = new Player(navPet);
            head.Children.Add(navPet);
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            who.Children.Add(T(Program.AppName, "t-body", null, FontWeights.SemiBold));
            todayText = T("", "t-caption");
            who.Children.Add(todayText);
            Grid.SetColumn(who, 1);
            head.Children.Add(who);
            DockPanel.SetDock(head, Dock.Top);
            side.Children.Add(head);
            savedNote = T(L.T("改了就會自動儲存", "Changes are saved automatically"), "t-caption", new Thickness(12, 0, 0, 0));
            DockPanel.SetDock(savedNote, Dock.Bottom);
            side.Children.Add(savedNote);
            var navList = new StackPanel();
            side.Children.Add(navList);
            var sideBorder = new Border { BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(0, 0, 1, 0), Child = side };
            grid.Children.Add(sideBorder);

            host = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            scroller = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(40, 32, 32, 48), Focusable = false };
            Grid.SetColumn(scroller, 1);
            grid.Children.Add(scroller);
            Content = grid;

            AddSection(navList, "remind", L.T("伸展提醒", "Stretch reminders"), BuildRemind());
            AddSection(navList, "moves", L.T("伸展動作", "Exercises"), BuildMoves());
            AddSection(navList, "water", L.T("喝水", "Water"), BuildWater());
            AddSection(navList, "reminders", L.T("自訂提醒", "Custom reminders"), BuildReminders());
            AddSection(navList, "calendar", L.T("Google 月曆", "Google Calendar"), BuildCalendar());
            AddSection(navList, "pet", L.T("桌寵", "Pet"), BuildPet());
            AddSection(navList, "quiet", L.T("勿擾", "Do not disturb"), BuildQuiet());
            AddSection(navList, "claude", "Claude Code", BuildClaude());
            AddSection(navList, "general", L.T("一般", "General"), BuildGeneral());

            savedTimer.Tick += (s, e) => { savedTimer.Stop(); savedNote.Text = L.T("改了就會自動儲存", "Changes are saved automatically"); };
            SourceInitialized += (s, e) => Native.DarkTitleBar(new WindowInteropHelper(this).Handle);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closed += (s, e) => { foreach (var p in players) p.Stop(); navPlayer.Stop(); if (waterPlayer != null) waterPlayer.Stop(); };
            Reload();
            Go("remind");
            Loaded += (s, e) => nav["remind"].Focus();
        }

        // ---------- 版面小工具 ----------

        static TextBlock T(string s, string style, Thickness? margin = null, FontWeight? weight = null)
        {
            var t = new TextBlock { Text = s, Style = Theme.Style(style) };
            if (margin.HasValue) t.Margin = margin.Value;
            if (weight.HasValue) t.FontWeight = weight.Value;
            return t;
        }

        void AddSection(StackPanel navList, string id, string label, FrameworkElement content)
        {
            var item = new RadioButton { Style = Theme.Style("nav-item"), Content = label, GroupName = "nav" };
            item.Checked += (s, e) => Go(id);
            navList.Children.Add(item);
            nav[id] = item;
            content.Visibility = Visibility.Collapsed;
            sections[id] = content;
            host.Children.Add(content);
        }

        public void Go(string id)
        {
            if (!sections.ContainsKey(id)) return;
            foreach (var kv in sections) kv.Value.Visibility = kv.Key == id ? Visibility.Visible : Visibility.Collapsed;
            if (nav[id].IsChecked != true) nav[id].IsChecked = true;
            scroller.ScrollToTop();
            if (id == "claude") RefreshClaude();
            if (id == "calendar") RefreshCalendar();
            if (id == "reminders") FillReminders();   // 「下次提醒」的時間是當下算的
        }

        static StackPanel Section(string title, string desc, params UIElement[] children)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(title, "t-headline"));
            if (desc != null) sp.Children.Add(T(desc, "t-body-sm-subtle", new Thickness(0, 8, 0, 0)));
            foreach (var c in children) sp.Children.Add(c);
            return sp;
        }

        static TextBlock Label(string s) { return T(s, "t-eyebrow", new Thickness(0, 32, 0, 8)); }

        // 一組設定：surface-1 卡片、rounded.lg、列與列之間 hairline
        static Border Group(params FrameworkElement[] rows)
        {
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                var r = new Border { Child = rows[i], Padding = new Thickness(16, 14, 16, 14),
                    BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0) };
                sp.Children.Add(r);
            }
            return new Border { Background = Theme.Brush("surface-1"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Child = sp, Margin = new Thickness(0, 16, 0, 0) };
        }

        static FrameworkElement Row(string label, string desc, FrameworkElement control, TextBlock error = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(T(label, "t-body-sm", null, FontWeights.Medium));
            if (desc != null) left.Children.Add(T(desc, "t-caption", new Thickness(0, 2, 0, 0)));
            if (error != null) left.Children.Add(error);
            g.Children.Add(left);
            if (control != null)
            {
                control.Margin = new Thickness(24, 0, 0, 0);
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
                if (AutomationProperties.GetName(control) == "") AutomationProperties.SetName(control, label);
            }
            return g;
        }

        static TextBlock ErrorText()
        {
            return new TextBlock { Style = Theme.Style("t-caption"), Foreground = Theme.Brush("semantic-danger"), Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        }

        void Saved()
        {
            savedNote.Text = L.T("已儲存", "Saved");
            savedTimer.Stop();
            savedTimer.Start();
        }

        void Save()
        {
            brain.SaveAndApply();
            Saved();
        }

        CheckBox Toggle(Func<bool> get, Action<bool> set, string name)
        {
            var cb = new CheckBox { Style = Theme.Style("toggle") };
            AutomationProperties.SetName(cb, name);
            refreshers.Add(() => cb.IsChecked = get());
            RoutedEventHandler h = (s, e) => { if (!loading) { set(cb.IsChecked == true); Saved(); } };
            cb.Checked += h;
            cb.Unchecked += h;
            return cb;
        }

        // 數字欄：Enter 或離開欄位才存；方向鍵上下調整；超出範圍就標紅並說明
        FrameworkElement Number(Func<int> get, Action<int> set, int min, Func<int> max, string unit, TextBlock error, string name)
        {
            var box = new TextBox { Style = Theme.Style("text-input"), Width = 80, TextAlignment = TextAlignment.Right };
            AutomationProperties.SetName(box, name);
            refreshers.Add(() => { box.Text = get().ToString(CultureInfo.InvariantCulture); box.Tag = null; error.Visibility = Visibility.Collapsed; });
            Action commit = () =>
            {
                int v, hi = max();
                if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) && v >= min && v <= hi)
                {
                    box.Tag = null;
                    error.Visibility = Visibility.Collapsed;
                    if (v != get()) set(v);
                }
                else
                {
                    box.Tag = "error";
                    error.Text = L.T("請輸入 ", "Enter a whole number from ") + min + "–" + hi + L.T(" 的整數", "");
                    error.Visibility = Visibility.Visible;
                }
            };
            box.LostKeyboardFocus += (s, e) => commit();
            box.GotKeyboardFocus += (s, e) => box.SelectAll();
            box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { commit(); e.Handled = true; }
                else if (e.Key == Key.Up || e.Key == Key.Down)
                {
                    int v;
                    if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) v = get();
                    v = Math.Max(min, Math.Min(max(), v + (e.Key == Key.Up ? 1 : -1)));
                    box.Text = v.ToString(CultureInfo.InvariantCulture);
                    box.SelectAll();
                    commit();
                    e.Handled = true;
                }
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(box);
            sp.Children.Add(T(unit, "t-body-sm-subtle", new Thickness(8, 0, 0, 0)));
            ((TextBlock)sp.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)sp.Children[1]).MinWidth = 28;
            return sp;
        }

        FrameworkElement Segments(string group, string[][] options, Func<string> get, Action<string> set)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var o in options)
            {
                var rb = new RadioButton { Style = Theme.Style("segment"), Content = o[1], GroupName = group };
                var value = o[0];
                rb.Checked += (s, e) => { if (!loading) { set(value); Saved(); } };
                refreshers.Add(() => rb.IsChecked = get() == value);
                sp.Children.Add(rb);
            }
            return new Border { Background = Theme.Brush("canvas"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9999), Padding = new Thickness(3), Child = sp };
        }

        // 滑桿：拖動時只更新數字，停下 0.3 秒後才套用（避免一直重畫桌寵）
        FrameworkElement SliderControl(double min, double max, double step, Func<double> get, Action<double> set, Func<double, string> format, string name)
        {
            var sl = new Slider { Style = Theme.Style("slider"), Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 5,
                Width = 200, IsSnapToTickEnabled = true, TickFrequency = step };
            AutomationProperties.SetName(sl, name);
            var val = T("", "t-body-sm-subtle", new Thickness(12, 0, 0, 0));
            val.Width = 64;
            val.VerticalAlignment = VerticalAlignment.Center;
            var apply = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            apply.Tick += (s, e) => { apply.Stop(); set(sl.Value); Saved(); };
            sl.ValueChanged += (s, e) =>
            {
                val.Text = format(sl.Value);
                if (loading) return;
                apply.Stop();
                apply.Start();
            };
            refreshers.Add(() => { sl.Value = get(); val.Text = format(sl.Value); });
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(sl);
            sp.Children.Add(val);
            return sp;
        }

        static Button Btn(string text, string style, Action click)
        {
            var b = new Button { Content = text, Style = Theme.Style(style) };
            b.Click += (s, e) => click();
            return b;
        }

        // ---------- 各分類 ----------

        FrameworkElement BuildRemind()
        {
            var c = brain.Cfg;
            TextBlock e1 = ErrorText(), e2 = ErrorText(), e3 = ErrorText(), e4 = ErrorText();
            return Section(L.T("伸展提醒", "Stretch reminders"), L.T("坐多久叫你起來伸展、叫了沒理怎麼辦。", "How long you sit before a reminder, and what happens if you ignore it."),
                Group(
                    Row(L.T("連續使用多久提醒一次", "Remind after this much use"), L.T("一直在用電腦才會累計，離開座位就重新計算。", "Counts only while you keep using the computer; leaving your seat resets it."),
                        Number(() => brain.Cfg.intervalMinutes, v => { brain.Cfg.intervalMinutes = v; Save(); }, 5, () => 240, L.T("分鐘", "min"), e1, L.T("提醒間隔（分鐘）", "Reminder interval (minutes)")), e1),
                    Row(L.T("離開多久算休息過", "Away this long counts as a break"), L.T("這段時間沒碰鍵盤滑鼠，就從頭計算。", "With no keyboard or mouse for this long, the count starts over."),
                        Number(() => brain.Cfg.breakResetMinutes, v => { brain.Cfg.breakResetMinutes = v; Save(); }, 1, () => 60, L.T("分鐘", "min"), e2, L.T("休息判定（分鐘）", "Break threshold (minutes)")), e2),
                    Row(L.T("按「晚點」延後", "“Later” snoozes for"), null,
                        Number(() => brain.Cfg.snoozeMinutes, v => { brain.Cfg.snoozeMinutes = v; Save(); }, 1, () => 120, L.T("分鐘", "min"), e3, L.T("晚點延後（分鐘）", "Snooze (minutes)")), e3),
                    Row(L.T("沒理它多久自動收起", "Put away if ignored for"), L.T("滑鼠停在泡泡上的時候不會收。", "It stays while the mouse is over the bubble."),
                        Number(() => brain.Cfg.bubbleSeconds, v => { brain.Cfg.bubbleSeconds = v; Save(); }, 10, () => 600, L.T("秒", "sec"), e4, L.T("泡泡自動收起（秒）", "Bubble auto-hide (seconds)")), e4)),
                Label(L.T("提醒的樣子", "How reminders look")),
                Group(
                    Row(L.T("先說「該伸展囉！」再顯示動作", "Say “Time to stretch!” first"), L.T("揮手打招呼約 2 秒，再換成這次的動作。", "It waves for about 2 seconds, then shows the exercise."),
                        Toggle(() => brain.Cfg.announce, v => { brain.Cfg.announce = v; Save(); }, L.T("先說該伸展囉", "Say time to stretch first"))),
                    Row(L.T("一開始就展開步驟", "Show the steps right away"), L.T("動作還不熟的時候很好用。", "Handy while the exercises are new to you."),
                        Toggle(() => brain.Cfg.expandSteps, v => { brain.Cfg.expandSteps = v; Save(); }, L.T("一開始就展開步驟", "Show the steps right away"))),
                    Row(L.T("沒理它時探頭提醒", "Peek if ignored"), L.T("20 秒沒反應，牠會從泡泡上緣探頭看你一次。", "After 20 seconds with no response, it peeks over the bubble once."),
                        Toggle(() => brain.Cfg.peekNudge, v => { brain.Cfg.peekNudge = v; Save(); }, L.T("探頭提醒", "Peek reminder")))));
        }

        FrameworkElement BuildPet()
        {
            var stage = new UniformGrid { Columns = 5, Margin = new Thickness(0, 16, 0, 0) };
            foreach (var ch in Character.All)
            {
                var c = ch;
                var img = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
                var sprites = new Sprites(c.Id, 80);
                var player = new Player(img);
                players.Add(player);
                player.Play(sprites, "idle");
                var panel = new StackPanel();
                panel.Children.Add(img);
                var name = T(c.Name, "t-body-sm", null, FontWeights.Medium);
                name.TextAlignment = TextAlignment.Center;
                var kind = T(c.Kind, "t-caption");
                kind.TextAlignment = TextAlignment.Center;
                panel.Children.Add(name);
                panel.Children.Add(kind);
                var rb = new RadioButton { Style = Theme.Style("tile"), Content = panel, GroupName = "character", Margin = new Thickness(0, 0, 8, 0) };
                AutomationProperties.SetName(rb, c.FullName);
                rb.MouseEnter += (s, e) => player.Play(sprites, "wave");
                rb.MouseLeave += (s, e) => player.Play(sprites, "idle");
                rb.Checked += (s, e) => { if (!loading) { brain.Cfg.character = c.Id; Save(); Reload(); } };   // Reload：動作清單縮圖、左上角也換成新角色
                refreshers.Add(() => rb.IsChecked = brain.Cfg.character == c.Id);
                stage.Children.Add(rb);
            }
            return Section(L.T("桌寵", "Pet"), L.T("挑一隻陪你，滑鼠移上去會跟你揮手。", "Pick a companion. It waves when the mouse passes over it."),
                stage,
                Label(L.T("位置與動作", "Position and movement")),
                Group(
                    Row(L.T("位置", "Position"), L.T("固定位置可以拖到任何地方；走來走去會沿著工作列散步。", "Fixed can be dragged anywhere; walking strolls along the taskbar."),
                        Segments("mode", new[] { new[] { "fixed", L.T("固定位置", "Fixed") }, new[] { "walk", L.T("走來走去", "Walking") } },
                            () => brain.Store.State.mode, v => brain.SetWalking(v == "walk"))),
                    Row(L.T("大小", "Size"), null, SliderControl(0.6, 2.5, 0.1, () => brain.Cfg.petScale,
                        v => { brain.Cfg.petScale = v; Save(); }, v => Math.Round(v * 100) + "%", L.T("桌寵大小", "Pet size"))),
                    Row(L.T("走路速度", "Walking speed"), L.T("只有走來走去的時候有用。", "Only applies when walking."), SliderControl(10, 150, 5, () => brain.Cfg.walkSpeed,
                        v => { brain.Cfg.walkSpeed = v; Save(); }, v => v < 30 ? L.T("慢", "Slow") : v < 70 ? L.T("普通", "Normal") : L.T("快", "Fast"), L.T("走路速度", "Walking speed"))),
                    Row(L.T("出場方式", "Entrance"), L.T("開機、顯示桌寵或換角色的時候。", "At startup, when shown, or when you change character."),
                        Segments("entrance", new[] { new[] { "drop", L.T("掛麵條垂降", "Drop in on a noodle") }, new[] { "none", L.T("直接出現", "Just appear") } },
                            () => brain.Cfg.entrance, v => { brain.Cfg.entrance = v; Save(); }))));
        }

        FrameworkElement BuildMoves()
        {
            var focusList = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var options = BubbleWindow.FocusOptions.ToList();
            options.Add(new[] { "custom", L.T("自訂", "Custom"), L.T("自己勾選下面要輪流做的動作。", "Tick the exercises below to rotate through.") });
            foreach (var f in options)
            {
                var id = f[0];
                var panel = new StackPanel();
                panel.Children.Add(T(f[1], "t-body-sm", null, FontWeights.Medium));
                panel.Children.Add(T(f[2], "t-caption", new Thickness(0, 2, 0, 0)));
                var rb = new RadioButton { Style = Theme.Style("radio-card"), Content = panel, GroupName = "focus", Margin = new Thickness(0, 0, 0, 8) };
                AutomationProperties.SetName(rb, f[1]);
                rb.Checked += (s, e) => { if (!loading) { brain.SetFocus(id); Saved(); } };
                refreshers.Add(() => rb.IsChecked = brain.Cfg.focus == id);
                focusList.Children.Add(rb);
            }
            exerciseList = new StackPanel();
            BuildExercises();
            refreshers.Add(() => { if (thumbsFor != brain.Cfg.character) BuildExercises(); });
            var group = new Border { Background = Theme.Brush("surface-1"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Child = exerciseList, Margin = new Thickness(0, 16, 0, 0) };
            return Section(L.T("伸展動作", "Exercises"), L.T("選一個重點，提醒時會輪流排適合的動作。", "Pick a focus and reminders rotate through exercises that suit it."),
                focusList, Label(L.T("動作清單", "Exercise list")), T(L.T("勾選的動作會輪流出現；滑鼠移到動作上可以看示範，按「試做」馬上做一組。", "Ticked exercises take turns. Hover over one to see the demo, or press “Try” to do a set now."), "t-body-sm-subtle"), group);
        }

        void BuildExercises()
        {
            thumbsFor = brain.Cfg.character;
            foreach (var p in players.Where(p => p.Name != "idle" && p.Name != "wave").ToList()) { p.Stop(); players.Remove(p); }
            exerciseList.Children.Clear();
            var sprites = new Sprites(brain.Cfg.character, 64);
            int n = 0;
            foreach (var x in brain.All)
            {
                var ex = x;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var img = new Image { Stretch = Stretch.Fill, Width = sprites.Size, Height = sprites.Size, VerticalAlignment = VerticalAlignment.Center };
                var pose = sprites.Get(ex.id);
                img.Source = pose.Frames[pose.Index[0]];
                var player = new Player(img);
                players.Add(player);
                g.Children.Add(img);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 16, 0) };
                text.Children.Add(T(ex.name, "t-body-sm", null, FontWeights.Medium));
                text.Children.Add(T(BubbleWindow.SpecLine(ex), "t-caption"));
                text.Children.Add(T(ex.instruction, "t-body-sm-subtle", new Thickness(0, 2, 0, 0)));
                Grid.SetColumn(text, 1);
                g.Children.Add(text);
                var tryIt = Btn(L.T("試做", "Try"), "button-secondary", () => brain.NowPlease(ex.id));
                AutomationProperties.SetName(tryIt, L.T("試做 ", "Try ") + ex.name);
                tryIt.VerticalAlignment = VerticalAlignment.Center;
                tryIt.Margin = new Thickness(0, 0, 16, 0);
                Grid.SetColumn(tryIt, 2);
                g.Children.Add(tryIt);
                var cb = new CheckBox { Style = Theme.Style("check") };
                AutomationProperties.SetName(cb, L.T("輪流做 ", "Rotate ") + ex.name);
                refreshers.Add(() => cb.IsChecked = brain.Enabled().Any(e => e.id == ex.id));
                cb.IsChecked = brain.Enabled().Any(e => e.id == ex.id);
                RoutedEventHandler h = (s, e) => ToggleExercise(ex.id, cb);
                cb.Checked += h;
                cb.Unchecked += h;
                Grid.SetColumn(cb, 3);
                g.Children.Add(cb);
                var row = new Border { Child = g, Padding = new Thickness(16, 12, 16, 12), Background = Brushes.Transparent,
                    BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(0, n++ == 0 ? 0 : 1, 0, 0) };
                row.MouseEnter += (s, e) => { player.Play(sprites, ex.id); row.Background = Theme.Brush("surface-2"); };
                row.MouseLeave += (s, e) => { player.Stop(); img.Source = pose.Frames[pose.Index[0]]; row.Background = Brushes.Transparent; };
                exerciseList.Children.Add(row);
            }
        }

        void ToggleExercise(string id, CheckBox cb)
        {
            if (loading) return;
            var ids = brain.Enabled().Select(x => x.id).ToList();
            if (cb.IsChecked == true) { if (!ids.Contains(id)) ids.Add(id); }
            else
            {
                ids.Remove(id);
                if (ids.Count == 0)
                {
                    loading = true;
                    cb.IsChecked = true;
                    loading = false;
                    Dialog.Info(this, L.T("至少要留一個動作", "Keep at least one exercise"), L.T("全部取消的話就沒有東西可以輪了。", "With none ticked there is nothing to rotate through."));
                    return;
                }
            }
            brain.Cfg.exercises = brain.All.Where(x => ids.Contains(x.id)).Select(x => x.id).ToList();
            brain.Cfg.focus = "custom";
            brain.Store.SaveConfig();
            Saved();
            Reload();
        }

        FrameworkElement BuildQuiet()
        {
            TextBlock err = ErrorText();
            var from = TimeBox(() => brain.Cfg.quietStart, v => { brain.Cfg.quietStart = v; Save(); }, err, L.T("安靜時段開始", "Quiet hours start"));
            var to = TimeBox(() => brain.Cfg.quietEnd, v => { brain.Cfg.quietEnd = v; Save(); }, err, L.T("安靜時段結束", "Quiet hours end"));
            var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            range.Children.Add(from);
            range.Children.Add(T(L.T("到", "to"), "t-body-sm-subtle", new Thickness(12, 0, 12, 0)));
            ((TextBlock)range.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            range.Children.Add(to);
            var quiet = new StackPanel();
            quiet.Children.Add(Row(L.T("安靜時段", "Quiet hours"), L.T("這段時間不提醒，桌寵照樣在。", "No reminders during these hours; the pet stays."),
                Toggle(() => brain.Cfg.quietHours, v => { brain.Cfg.quietHours = v; Save(); }, L.T("安靜時段", "Quiet hours"))));
            quiet.Children.Add(range);
            quiet.Children.Add(err);
            return Section(L.T("勿擾", "Do not disturb"), L.T("什麼時候先不要打擾你。", "When to leave you alone."),
                Group(
                    Row(L.T("通話中不打擾", "Quiet during calls"), L.T("有程式在用麥克風或鏡頭時，先不提醒。", "No reminders while an app is using the microphone or camera."),
                        Toggle(() => brain.Cfg.holdDuringCalls, v => { brain.Cfg.holdDuringCalls = v; Save(); }, L.T("通話中不打擾", "Quiet during calls"))),
                    Row(L.T("全螢幕時躲起來", "Hide in full screen"), L.T("看影片、簡報或玩遊戲時，桌寵和提醒都先收起來。", "The pet and reminders stay away during videos, presentations and games."),
                        Toggle(() => brain.Cfg.hideInFullscreen, v => { brain.Cfg.hideInFullscreen = v; Save(); }, L.T("全螢幕時躲起來", "Hide in full screen"))),
                    quiet),
                T(L.T("開會時不打擾：在「Google 月曆」設定。這一頁和暫停提醒只管伸展、喝水；自訂提醒和行事曆提醒照常出現。", "Quiet during meetings is set under Google Calendar. This page and Pause only affect stretch and water reminders; custom and calendar reminders still appear."), "t-caption", new Thickness(0, 12, 0, 0)));
        }

        TextBox TimeBox(Func<string> get, Action<string> set, TextBlock error, string name)
        {
            var box = new TextBox { Style = Theme.Style("text-input"), Width = 80, TextAlignment = TextAlignment.Center };
            AutomationProperties.SetName(box, name);
            refreshers.Add(() => { box.Text = get(); box.Tag = null; error.Visibility = Visibility.Collapsed; });
            Action commit = () =>
            {
                TimeSpan t;
                var s = box.Text.Trim().Replace("：", ":");
                if (TimeSpan.TryParseExact(s, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out t) && t < TimeSpan.FromHours(24))
                {
                    var v = t.ToString(@"hh\:mm");
                    box.Text = v;
                    box.Tag = null;
                    error.Visibility = Visibility.Collapsed;
                    if (v != get()) set(v);
                }
                else
                {
                    box.Tag = "error";
                    error.Text = L.T("時間請用 24 小時制，例如 22:00", "Use 24-hour time, for example 22:00");
                    error.Visibility = Visibility.Visible;
                }
            };
            box.LostKeyboardFocus += (s, e) => commit();
            box.GotKeyboardFocus += (s, e) => box.SelectAll();
            box.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
            return box;
        }

        FrameworkElement BuildClaude()
        {
            TextBlock e1 = ErrorText(), e2 = ErrorText(), e3 = ErrorText();
            claudeDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            claudeBadge = T("", "t-caption");
            claudeBadge.Foreground = Theme.Brush("ink-muted");
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
            badgeRow.Children.Add(claudeDot);
            badgeRow.Children.Add(claudeBadge);
            var badge = new Border { Background = Theme.Brush("surface-2"), CornerRadius = new CornerRadius(9999), Padding = new Thickness(8, 2, 8, 2), Child = badgeRow,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            claudeButton = Btn("", "button-primary", ClaudeButtonClicked);
            var status = new StackPanel { Orientation = Orientation.Horizontal };
            status.Children.Add(badge);
            status.Children.Add(claudeButton);
            claudeNotes = T("", "t-caption", new Thickness(0, 4, 0, 0));
            claudeNotes.Foreground = Theme.Brush("semantic-danger");
            var statusRow = new StackPanel();
            statusRow.Children.Add(Row(L.T("連動狀態", "Status"), ClaudeHooks.SettingsPath, status));
            statusRow.Children.Add(claudeNotes);
            return Section("Claude Code", L.T("Claude Code 跑長任務的時候，讓桌寵提早叫你起來動一動。CLI、VS Code、Cursor 和 Claude 桌面版的 Code 分頁都適用。", "When Claude Code runs a long task, the pet calls you early for a stretch. Works with the CLI, VS Code, Cursor and the Code tab of the Claude desktop app."),
                Group(statusRow,
                    Row(L.T("啟用連動", "Enable"), L.T("關掉的話，就算已經連結也不會提早叫。", "When off, it never calls early, even if linked."),
                        Toggle(() => brain.Cfg.claudeHook, v => { brain.Cfg.claudeHook = v; Save(); }, L.T("啟用 Claude Code 連動", "Enable Claude Code link"))),
                    Row(L.T("Claude 忙多久才算長任務", "Claude busy this long counts as a long task"), L.T("短的問題不會打擾你。", "Short questions won't disturb you."),
                        Number(() => brain.Cfg.claudeDelaySeconds, v => { brain.Cfg.claudeDelaySeconds = v; Save(); }, 3, () => 600, L.T("秒", "sec"), e1, L.T("長任務門檻（秒）", "Long task threshold (seconds)")), e1),
                    Row(L.T("至少坐了多久才提早叫", "Call early only after sitting this long"), L.T("剛做完一組就不會又被叫起來。", "So you are not called again right after a set."),
                        Number(() => brain.Cfg.claudeEarlyMinutes, v => { brain.Cfg.claudeEarlyMinutes = v; Save(); }, 0, () => brain.Cfg.intervalMinutes, L.T("分鐘", "min"), e2, L.T("提早叫的最短坐姿時間（分鐘）", "Minimum sitting time for an early call (minutes)")), e2),
                    Row(L.T("本機連接埠", "Local port"), L.T("只聽本機 127.0.0.1，不連外網。和別的程式衝突時再換。", "Listens on 127.0.0.1 only, nothing external. Change it only if another program uses it."),
                        Number(() => brain.Cfg.claudePort, ChangePort, 1024, () => 65535, "", e3, L.T("本機連接埠", "Local port")), e3)),
                T(L.T("連結時會先顯示要改的內容、確認後才寫入，原本的 settings.json 會備份在同一個資料夾。有用同步腳本管理 .claude 設定的話，記得也同步這次的變更。", "Linking shows the changes first and writes only after you confirm; the original settings.json is backed up in the same folder. If you manage .claude settings with a sync script, remember to sync this change too."),
                    "t-caption", new Thickness(0, 12, 0, 0)));
        }

        void RefreshClaude()
        {
            if (claudeBadge == null) return;
            string error;
            List<string> notes;
            claudeState = ClaudeHooks.Status(brain.Cfg.claudePort, out error, out notes);
            if (brain.HookError != null) notes.Insert(0, brain.HookError + L.T("，請換一個連接埠。", ". Please choose another port."));
            if (claudeState == "linked") { claudeBadge.Text = L.T("已連結", "Linked"); claudeDot.Background = Theme.Brush("semantic-success"); claudeButton.Content = L.T("移除連結…", "Remove link…"); claudeButton.Style = Theme.Style("button-danger"); }
            else if (claudeState == "partial") { claudeBadge.Text = L.T("只連了一部分", "Partly linked"); claudeDot.Background = Theme.Brush("semantic-danger"); claudeButton.Content = L.T("修好連結…", "Repair link…"); claudeButton.Style = Theme.Style("button-primary"); }
            else if (claudeState == "error") { claudeBadge.Text = L.T("讀不到設定檔", "Can't read settings"); claudeDot.Background = Theme.Brush("semantic-danger"); claudeButton.Content = L.T("重新檢查", "Check again"); claudeButton.Style = Theme.Style("button-secondary"); notes.Insert(0, error); }
            else { claudeBadge.Text = L.T("未連結", "Not linked"); claudeDot.Background = Theme.Brush("ink-subtle"); claudeButton.Content = L.T("連結 Claude Code…", "Link Claude Code…"); claudeButton.Style = Theme.Style("button-primary"); }
            claudeNotes.Text = string.Join("\n", notes);
            claudeNotes.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void ClaudeButtonClicked()
        {
            int port = brain.Cfg.claudePort;
            if (claudeState == "error") RefreshClaude();
            else if (claudeState == "linked")
                ConfirmHooks(port, 0, L.T("移除 Claude Code 連結", "Remove Claude Code link"), L.T("會從 Claude Code 的使用者設定移除桌寵的 hook，其他設定都不動。", "Removes the pet's hooks from Claude Code's user settings. Nothing else is touched. "), L.T("移除", "Remove"), true);
            else
                ConfirmHooks(port, port, L.T("連結 Claude Code", "Link Claude Code"), L.T("會在 Claude Code 的使用者設定加入三個 http hook：送出提示詞、回應結束、回應出錯時通知桌寵。", "Adds three http hooks to Claude Code's user settings, notifying the pet when a prompt is sent, a response ends, or a response fails. "), L.T("寫入設定", "Write settings"), false);
        }

        void ChangePort(int v)
        {
            int old = brain.Cfg.claudePort;
            brain.Cfg.claudePort = v;
            Save();
            string error;
            List<string> notes;
            if (old != v && ClaudeHooks.Status(old, out error, out notes) != "none")
                ConfirmHooks(old, v, L.T("一起更新 Claude Code 的設定？", "Update Claude Code's settings too?"), L.T("連接埠改了，Claude Code 的 hook 也要換成新的連接埠才會通。", "The port changed, so Claude Code's hooks need the new port to work. "), L.T("更新設定", "Update settings"), false);
            RefreshClaude();
        }

        void ConfirmHooks(int removePort, int addPort, string title, string message, string ok, bool danger)
        {
            string before, after, error, backup;
            if (!ClaudeHooks.Plan(removePort, addPort, out before, out after, out error)) { Dialog.Info(this, L.T("沒辦法修改 settings.json", "Can't change settings.json"), error); RefreshClaude(); return; }
            var diff = ClaudeHooks.Diff(before ?? "", after);
            if (diff.All(d => d.Key == ' ')) { RefreshClaude(); return; }
            if (!Dialog.Confirm(this, title, message + L.T("原本的設定會先備份。", "The original settings are backed up first."), Dialog.DiffView(diff, ClaudeHooks.SettingsPath), ok, L.T("取消", "Cancel"), danger)) return;
            if (ClaudeHooks.Apply(before, after, out backup, out error))
            {
                RefreshClaude();
                Saved();
                savedNote.Text = backup == null ? L.T("已寫入 Claude Code 設定", "Written to Claude Code settings") : L.T("已寫入，舊設定備份好了", "Written; old settings backed up");
            }
            else Dialog.Info(this, L.T("寫入失敗", "Write failed"), error);
        }

        FrameworkElement BuildGeneral()
        {
            var open = Btn(L.T("開啟資料夾", "Open folder"), "button-secondary", () =>
            {
                try { Process.Start("explorer.exe", "\"" + Store.Dir + "\""); } catch (Exception e) { Dialog.Info(this, L.T("打不開資料夾", "Can't open the folder"), e.Message); }
            });
            var intro = Btn(L.T("重看一次", "Replay"), "button-secondary", () => brain.StartIntro());
            var licenses = Btn(L.T("授權資訊…", "Licenses…"), "button-secondary", () => Dialog.Confirm(this, L.T("授權資訊", "Licenses"),
                L.T("拉麵以 MIT 授權釋出；角色、動作示範與說明來自 Noodle，沿用原作者的 MIT 授權。", "Lamian is released under the MIT License. The characters, exercise demos and instructions come from Noodle, under the original author's MIT License."), LicenseView(), L.T("關閉", "Close"), null, false));
            licenses.HorizontalAlignment = HorizontalAlignment.Left;
            licenses.Margin = new Thickness(0, 16, 0, 0);
            // 聯繫作者、更多軟體：只交給系統的寄信程式和瀏覽器開
            var mail = Btn(L.T("寄信給作者", "Email the author"), "button-secondary", () => OpenUrl("mailto:bb6800182@gmail.com?subject="
                + Uri.EscapeDataString(Program.AppName + L.T(" Windows 版意見回饋（v", " for Windows feedback (v") + Program.Version + L.T("）", ")")) + "&body="
                + Uri.EscapeDataString(L.T("（請寫下你的建議；回報問題的話，請說明做了什麼、發生什麼事）\r\n\r\n\r\n\r\n——\r\n", "(Write your suggestion here. For a bug, please say what you did and what happened.)\r\n\r\n\r\n\r\n——\r\n")
                    + Program.AppName + L.T(" Windows 版 v", " for Windows v") + Program.Version + "\r\n" + Environment.OSVersion.VersionString + "\r\n")));
            var more = Btn(L.T("前往 C520", "Visit C520"), "button-secondary", () => OpenUrl(L.T("https://c520.pages.dev/", "https://c520.pages.dev/en/")));
            var contact = Group(
                Row(L.T("聯繫作者", "Contact"), L.T("有建議、想要的功能，或遇到問題（bug），都歡迎寫信告訴我：bb6800182@gmail.com", "Suggestions, feature requests or bugs? Send me an email: bb6800182@gmail.com"), mail),
                Row(L.T("更多軟體", "More apps"), L.T("作者的其他 App 都放在 C520，歡迎去看看。", "My other apps are on C520. Come take a look."), more));
            contact.Margin = new Thickness(0, 16, 0, 0);
            return Section(L.T("一般", "General"), null,
                Group(
                    Row(L.T("開機自動啟動", "Start with Windows"), L.T("登入 Windows 後桌寵自己出來。", "The pet appears after you sign in to Windows."),
                        Toggle(() => Native.AutoStart, v => Native.AutoStart = v, L.T("開機自動啟動", "Start with Windows"))),
                    Row(L.T("開場導覽", "Intro tour"), L.T("第一次打開時的介紹和桌面環境檢查。", "The first-run introduction and desk setup check."), intro),
                    Row(L.T("設定與紀錄", "Settings and logs"), Store.Dir, open)),
                Label(L.T("關於", "About")),
                T(Program.AppName + " v" + Program.Version, "t-body-sm", null, FontWeights.Medium),
                T(L.T("非官方的 Windows 改寫版：角色、動作示範與說明來自 Noodle（© 2026 Pete Cybriwsky，MIT 授權），與原作者無關，也未經原作者背書。喝水動作、提醒和行事曆功能是這個版本另外加的。", "An unofficial Windows port: the characters, exercise demos and instructions come from Noodle (© 2026 Pete Cybriwsky, MIT License). Not affiliated with or endorsed by the original author. The water animation, reminders and calendar features were added in this version."),
                    "t-caption", new Thickness(0, 4, 0, 0)),
                T(L.T("伸展動作只是一般的活動建議，不能取代醫療建議；做的時候覺得痛或不舒服就停下來，有傷病請先問醫師或物理治療師。", "The stretches are general activity suggestions, not medical advice. Stop if anything hurts or feels wrong; if you have an injury or condition, ask a doctor or physiotherapist first."),
                    "t-caption", new Thickness(0, 8, 0, 0)),
                T(L.T("Claude、Claude Code 是 Anthropic 的商標；Google、Google 月曆是 Google 的商標。本程式與 Anthropic、Google 沒有關係。", "Claude and Claude Code are trademarks of Anthropic; Google and Google Calendar are trademarks of Google. This program is not affiliated with Anthropic or Google."),
                    "t-caption", new Thickness(0, 8, 0, 0)),
                licenses,
                contact);
        }

        // 授權全文：編進 exe 的 LICENSE 與 Noodle 原始授權（MIT 要求連同執行檔一起附上）
        internal FrameworkElement LicenseView()
        {
            var text = new StackPanel();
            foreach (var part in new[] { new[] { L.T("拉麵（noodle-pet）", "Lamian (noodle-pet)"), "LICENSE.txt" }, new[] { L.T("Noodle（原作）", "Noodle (original)"), "LICENSE-noodle.txt" } })
            {
                string body;
                try { using (var r = new System.IO.StreamReader(Res.Open(part[1]), System.Text.Encoding.UTF8)) body = r.ReadToEnd().Trim(); }
                catch (Exception e) { Store.LogError(e); body = L.T("讀不到授權檔：", "Can't read the license file: ") + e.Message; }
                // 授權檔是 80 字寬硬換行：段落接成一行，讓它照視窗寬度自動換行（版權行維持各自一行）
                body = string.Join("\n\n", body.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None)
                    .Select(p => p.StartsWith("Copyright") ? p : p.Replace('\n', ' ')));
                text.Children.Add(T(part[0], "t-eyebrow", new Thickness(0, text.Children.Count == 0 ? 0 : 24, 0, 8)));
                text.Children.Add(new TextBlock { Text = body, Style = Theme.Style("t-mono"), TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("ink-muted") });
            }
            // 可用 Tab 移進來、方向鍵捲動
            var scroll = new ScrollViewer { Content = text, MaxHeight = 340, Padding = new Thickness(16, 12, 16, 12), Focusable = true,
                FocusVisualStyle = Theme.Style("focus-visual"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            AutomationProperties.SetName(scroll, L.T("授權條款全文", "Full license text"));
            var source = Btn(L.T("開啟 Noodle 原始碼", "Open Noodle source"), "button-secondary", () => OpenUrl("https://github.com/pcybriwsky/noodle"));
            source.HorizontalAlignment = HorizontalAlignment.Left;
            source.Margin = new Thickness(0, 12, 0, 0);
            var wrap = new StackPanel();
            wrap.Children.Add(new Border { Background = Theme.Brush("surface-1"), BorderBrush = Theme.Brush("hairline"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Child = scroll });
            wrap.Children.Add(source);
            return wrap;
        }

        // 從設定檔重新帶入所有欄位（設定檔被手動改、或別的地方改了設定時）
        public void Reload()
        {
            loading = true;
            try { foreach (var r in refreshers.ToList()) r(); }
            finally { loading = false; }
            todayText.Text = brain.TodayLine();
            if (navSprites == null || navSprites.Character != brain.Cfg.character) navSprites = new Sprites(brain.Cfg.character, 40);
            navPlayer.Play(navSprites, "idle");
            if (sections.ContainsKey("claude") && sections["claude"].Visibility == Visibility.Visible) RefreshClaude();
        }
    }
}
