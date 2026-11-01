using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NoodlePet
{
    // 桌寵頭上的對話泡泡：獨立的透明置頂視窗，跟著桌寵移動，不搶焦點
    public class BubbleWindow : Window
    {
        const double W = 300;
        const string IconCheck = "M3.5,8.5 L6.5,11.5 L12.5,4.5";
        const string IconClock = "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 M8,4.6 L8,8 L10.4,9.6";

        readonly Brain brain;
        readonly PetWindow pet;
        readonly Grid root;
        readonly StackPanel stack, pick, setup, steps, dots;
        readonly Border card, bar;
        readonly FrameworkElement tailTop, tailBottom, header, timerRow, barTrack, noteRow, footer;
        readonly TextBlock eyebrow, meta, title, body, spec, timerSpec, clock, note;
        readonly Path noteIcon;
        readonly Button primary, secondary, link;
        readonly Canvas peekBackLayer;
        readonly Image peekBack, peekFront;
        readonly TranslateTransform rise = new TranslateTransform(), backY = new TranslateTransform(), frontY = new TranslateTransform();
        readonly DispatcherTimer dismiss = new DispatcherTimer();
        readonly DispatcherTimer countdown = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        readonly DispatcherTimer peekTimer = new DispatcherTimer();
        readonly List<DispatcherTimer> pending = new List<DispatcherTimer>();
        Action onPrimary, onSecondary, onLink, onTimeout;
        DateTime dismissAt, stretchEnd;
        double stretchTotal = 30, tailX = W / 2;
        bool hovering, touched, above = true, stepsOpen, suppressed, peeking;
        IntPtr hwnd;

        public static bool Headless;               // 只給截圖工具用：不真的顯示視窗
        public string Kind { get; private set; }   // null = 沒開；hello／announce／nudge／stretch／done／bye／intro
        public event Action BubbleClosed;

        public BubbleWindow(Brain b, PetWindow p)
        {
            brain = b;
            pet = p;
            Title = Program.AppName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;

            using (var x = Res.Open("Bubble.xaml")) root = (Grid)XamlReader.Load(x);
            Content = root;
            stack = Find<StackPanel>("Stack");
            pick = Find<StackPanel>("Pick");
            setup = Find<StackPanel>("Setup");
            steps = Find<StackPanel>("Steps");
            dots = Find<StackPanel>("Dots");
            card = Find<Border>("Card");
            bar = Find<Border>("Bar");
            tailTop = Find<FrameworkElement>("TailTop");
            tailBottom = Find<FrameworkElement>("TailBottom");
            header = Find<FrameworkElement>("Header");
            timerRow = Find<FrameworkElement>("TimerRow");
            barTrack = Find<FrameworkElement>("BarTrack");
            noteRow = Find<FrameworkElement>("NoteRow");
            footer = Find<FrameworkElement>("Footer");
            eyebrow = Find<TextBlock>("Eyebrow");
            meta = Find<TextBlock>("Meta");
            title = Find<TextBlock>("Title");
            body = Find<TextBlock>("Body");
            spec = Find<TextBlock>("Spec");
            timerSpec = Find<TextBlock>("TimerSpec");
            clock = Find<TextBlock>("Clock");
            note = Find<TextBlock>("Note");
            noteIcon = Find<Path>("NoteIcon");
            primary = Find<Button>("Primary");
            secondary = Find<Button>("Secondary");
            link = Find<Button>("Link");
            peekBackLayer = Find<Canvas>("PeekBackLayer");
            peekBack = Find<Image>("PeekBack");
            peekFront = Find<Image>("PeekFront");

            stack.RenderTransform = rise;
            peekBack.RenderTransform = backY;
            peekFront.RenderTransform = frontY;
            AutomationProperties.SetLiveSetting(title, AutomationLiveSetting.Assertive);
            primary.Click += (s, e) => Run(onPrimary);
            secondary.Click += (s, e) => Run(onSecondary);
            link.Click += (s, e) => Run(onLink);
            card.MouseEnter += (s, e) => { hovering = true; touched = true; };
            card.MouseLeave += (s, e) =>
            {
                hovering = false;
                if (dismiss.IsEnabled && (dismissAt - DateTime.Now).TotalSeconds < 6) dismissAt = DateTime.Now.AddSeconds(6);
            };
            card.PreviewMouseDown += (s, e) => touched = true;
            card.MouseRightButtonUp += (s, e) => brain.ShowMenu();
            dismiss.Tick += (s, e) => DismissTick();
            countdown.Tick += (s, e) => UpdateCountdown();
            peekTimer.Tick += (s, e) => { peekTimer.Stop(); Peek(); };
            SizeChanged += (s, e) => Anchor();
            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; Native.MakeToolNoActivate(hwnd); };
        }

        T Find<T>(string name) where T : class { return root.FindName(name) as T; }

        void Run(Action a) { touched = true; if (a != null) a(); }

        // ---------- 位置：跟著桌寵；上面放不下就改放到桌寵下方 ----------

        double PeekRoom { get { return 48 * pet.S; } }

        public void Follow() { if (Kind != null) { Layout(); Anchor(); } }

        void Layout()
        {
            var c = pet.CellRect;
            var work = pet.Work;
            double cx = c.Left + c.Width / 2;
            double left = Math.Max(work.Left + 8, Math.Min(work.Right - 8 - W, cx - W / 2));
            tailX = Math.Max(20, Math.Min(W - 20, cx - left));
            card.Measure(new Size(W, double.PositiveInfinity));
            above = c.Top + 14 * pet.S - work.Top >= card.DesiredSize.Height + 9 + 4;
            tailBottom.Visibility = above ? Visibility.Visible : Visibility.Collapsed;
            tailTop.Visibility = above ? Visibility.Collapsed : Visibility.Visible;
            tailBottom.Margin = new Thickness(tailX - 8, -1, 0, 0);
            tailTop.Margin = new Thickness(tailX - 8, 0, 0, -1);
            stack.Margin = new Thickness(0, above ? PeekRoom : 0, 0, 0);
            Left = left;
        }

        void Anchor()
        {
            if (Kind == null || ActualHeight <= 0) return;
            var c = pet.CellRect;
            Top = above ? c.Top + 14 * pet.S - ActualHeight : c.Top + (PetWindow.FeetY + 2) * pet.S;
        }

        // ---------- 開關 ----------

        void Open(string kind, int seconds, Action timeout)
        {
            bool wasOpen = Kind != null;
            Kind = kind;
            touched = false;
            StopPeek();
            dismiss.Stop();
            onTimeout = timeout;
            if (seconds > 0)
            {
                dismissAt = DateTime.Now.AddSeconds(seconds);
                dismiss.Interval = TimeSpan.FromSeconds(seconds);
                dismiss.Start();
            }
            Layout();
            if (!IsVisible && !suppressed && !Headless) { Show(); Native.KeepTopmost(hwnd); }
            UpdateLayout();
            Anchor();
            if (!wasOpen && Native.Motion)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                stack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(above ? 8 : -8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            }
            else if (!wasOpen)
            {
                stack.BeginAnimation(OpacityProperty, null);
                rise.BeginAnimation(TranslateTransform.YProperty, null);
                stack.Opacity = 1;
            }
            Announce();
        }

        void DismissTick()
        {
            if (hovering) { dismiss.Interval = TimeSpan.FromSeconds(1); return; }   // 滑鼠停在泡泡上就不收
            var left = dismissAt - DateTime.Now;
            if (left.TotalMilliseconds > 50) { dismiss.Interval = left; return; }
            dismiss.Stop();
            var cb = onTimeout;
            onTimeout = null;
            if (cb != null) cb();
        }

        public void Dismiss()
        {
            if (Kind == null) return;
            Kind = null;
            dismiss.Stop();
            countdown.Stop();
            peekTimer.Stop();
            ClearLater();
            StopPeek();
            hovering = false;
            if (IsVisible && Native.Motion)
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                fade.Completed += (s, e) => { if (Kind == null) Hide(); };
                stack.BeginAnimation(OpacityProperty, fade);
            }
            else Hide();
            if (BubbleClosed != null) BubbleClosed();
        }

        // 全螢幕時暫時藏起來（狀態保留）
        public void SetSuppressed(bool on)
        {
            suppressed = on;
            if (on) { if (IsVisible) Hide(); }
            else if (Kind != null && !IsVisible) { Show(); Native.KeepTopmost(hwnd); Follow(); }
        }

        public void KeepOnTop() { if (IsVisible) Native.KeepTopmost(hwnd); }

        // 螢幕閱讀器：泡泡出現時唸出標題
        void Announce()
        {
            try
            {
                var peer = UIElementAutomationPeer.FromElement(title) ?? UIElementAutomationPeer.CreatePeerForElement(title);
                if (peer != null) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            catch { }
        }

        void After(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); pending.Remove(t); a(); };
            pending.Add(t);
            t.Start();
        }

        void ClearLater()
        {
            foreach (var t in pending) t.Stop();
            pending.Clear();
        }

        // ---------- 內容 ----------

        void Reset()
        {
            ClearLater();
            countdown.Stop();
            peekTimer.Stop();
            SetHeader(null, null);
            Set(title, null);
            Set(body, null);
            Set(spec, null);
            timerRow.Visibility = barTrack.Visibility = noteRow.Visibility = Visibility.Collapsed;
            pick.Children.Clear();
            setup.Children.Clear();
            dots.Children.Clear();
            pick.Visibility = setup.Visibility = steps.Visibility = dots.Visibility = Visibility.Collapsed;
            stepsOpen = false;
            SetButtons(null, null, null, null);
            SetLink(null, null);
        }

        static void Set(TextBlock t, string text)
        {
            t.Text = text ?? "";
            t.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        void SetHeader(string left, string right)
        {
            Set(eyebrow, left);
            Set(meta, right);
            header.Visibility = string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right) ? Visibility.Collapsed : Visibility.Visible;
        }

        void SetButtons(string p, Action pa, string s, Action sa)
        {
            onPrimary = pa;
            onSecondary = sa;
            primary.Content = p;
            primary.Visibility = p == null ? Visibility.Collapsed : Visibility.Visible;
            secondary.Content = s;
            secondary.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateFooter();
        }

        void SetLink(string text, Action a)
        {
            onLink = a;
            link.Content = text;
            link.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            UpdateFooter();
        }

        void UpdateFooter()
        {
            bool any = primary.Visibility == Visibility.Visible || secondary.Visibility == Visibility.Visible
                || link.Visibility == Visibility.Visible || dots.Visibility == Visibility.Visible;
            footer.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetNote(string text, string icon)
        {
            noteRow.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            note.Text = text ?? "";
            noteIcon.Data = Geometry.Parse(icon == "clock" ? IconClock : IconCheck);
        }

        public static string Duration(int seconds)
        {
            if (seconds < 60) return seconds + " 秒";
            return seconds % 60 == 0 ? seconds / 60 + " 分鐘" : seconds / 60 + " 分 " + seconds % 60 + " 秒";
        }

        // 「慢慢做 10 下 · 約 45 秒」；規格本身就是時間（30 秒、2 分鐘）就不重複
        public static string SpecLine(Exercise ex)
        {
            var d = Duration(ex.seconds);
            return ex.spec == d ? ex.spec : ex.spec + " · 約 " + d;
        }

        public void ShowHello(string line, string sub)
        {
            Reset();
            SetHeader(brain.CharacterInfo.FullName, null);
            Set(title, line);
            Set(body, sub);
            SetButtons("現在做一組", () => brain.NowPlease(null), "設定", () => { Dismiss(); brain.OpenSettings(null); });
            Open("hello", 8, Dismiss);
        }

        public void ShowNudge(Exercise ex, string reason, int index, int total, bool announce)
        {
            if (announce && Native.Motion)
            {
                Reset();
                Set(title, "該伸展囉！");
                Set(body, reason);
                Open("announce", 0, null);
                After(1700, () => { if (Kind == "announce") FillNudge(ex, reason, index, total); });
                return;
            }
            FillNudge(ex, reason, index, total);
        }

        void FillNudge(Exercise ex, string reason, int index, int total)
        {
            Reset();
            SetHeader(reason, index > 0 ? index + "/" + total : null);
            Set(title, ex.name);
            Set(body, ex.instruction);
            Set(spec, SpecLine(ex));
            BuildSteps(ex);
            SetButtons("開始", brain.StartStretch, "晚點", brain.Snooze);
            ShowSteps(brain.Cfg.expandSteps);
            Open("nudge", brain.Cfg.bubbleSeconds, brain.Ignore);
            if (brain.Cfg.peekNudge && Native.Motion)
            {
                peekTimer.Interval = TimeSpan.FromSeconds(brain.PeekAfter);
                peekTimer.Start();
            }
        }

        public void ShowStretch(Exercise ex, int seconds, int setNumber)
        {
            bool keepSteps = stepsOpen;
            Reset();
            SetHeader("跟著做", setNumber > 0 ? "今天第 " + setNumber + " 組" : null);
            Set(title, ex.name);
            Set(body, ex.instruction);
            timerRow.Visibility = barTrack.Visibility = Visibility.Visible;
            timerSpec.Text = ex.spec;
            BuildSteps(ex);
            SetButtons("完成", brain.CompleteStretch, "跳過", brain.SkipStretch);
            ShowSteps(keepSteps || brain.Cfg.expandSteps);
            stretchTotal = Math.Max(3, seconds);
            stretchEnd = DateTime.Now.AddSeconds(stretchTotal);
            Open("stretch", 0, null);
            UpdateCountdown();
            countdown.Start();
        }

        void UpdateCountdown()
        {
            double left = Math.Max(0, (stretchEnd - DateTime.Now).TotalSeconds);
            int s = (int)Math.Ceiling(left);
            clock.Text = s / 60 + ":" + (s % 60).ToString("00");
            bar.Width = Math.Max(0, barTrack.ActualWidth * (1 - left / stretchTotal));
            if (left <= 0 && Kind == "stretch") { countdown.Stop(); brain.CompleteStretch(); }
        }

        public void ShowDone(int count)
        {
            Reset();
            Set(title, "做得好！");
            SetNote(count <= 1 ? "今天第一組完成" : "今天第 " + count + " 組了，繼續保持", "check");
            Open("done", 0, null);
            After(1800, brain.DoneShown);
        }

        public void ShowBye(int minutes)
        {
            Reset();
            Set(title, "等等見");
            SetNote(minutes + " 分鐘後再叫你", "clock");
            Open("bye", 0, null);
            After(1400, brain.ByeShown);
        }

        // Claude 做完了：泡泡還開著就說一聲
        public void ClaudeDone()
        {
            if (Kind == "stretch") SetNote("Claude 做完了，做完這組再回去", "check");
            else if (Kind == "nudge") SetNote("Claude 做完了，還有時間做這組", "check");
        }

        // ---------- 步驟 ----------

        void BuildSteps(Exercise ex)
        {
            steps.Children.Clear();
            int n = 0;
            foreach (var step in ex.steps ?? new List<string>())
            {
                var row = new Grid { Margin = new Thickness(0, n == 0 ? 0 : 8, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(Text((++n).ToString(), "t-body-sm-subtle"));
                var t = Text(step, "t-body-sm-muted");
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                steps.Children.Add(row);
            }
            if (!string.IsNullOrEmpty(ex.tip))
            {
                var tip = Text("注意：" + ex.tip, "t-body-sm");
                tip.Margin = new Thickness(0, 12, 0, 0);
                steps.Children.Add(tip);
            }
            var safety = Text("動作要輕柔，有疼痛、刺麻或尖銳感就停下來。", "t-caption");
            safety.Margin = new Thickness(0, 8, 0, 0);
            steps.Children.Add(safety);
        }

        void ShowSteps(bool open)
        {
            stepsOpen = open;
            steps.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            SetLink(open ? "收起步驟" : "怎麼做", ToggleSteps);
            if (Kind != null) { UpdateLayout(); Follow(); }
        }

        public void ToggleSteps()
        {
            if (Kind != "nudge" && Kind != "stretch") return;
            ShowSteps(!stepsOpen);
        }

        static TextBlock Text(string s, string style)
        {
            return new TextBlock { Text = s, Style = Theme.Style(style) };
        }

        // ---------- 開場導覽 ----------

        static readonly string[][] Intro = {
            new[] { "嗨，我是 {name}", "{fullName}。你專心工作，我負責提醒你起來動一動。" },
            new[] { "坐久了才叫你", "連續用電腦 {interval} 分鐘我才會出現；你起身離開，時間就重新算。" },
            new[] { "你說了算", "按「開始」我會示範動作、幫你計時；按「晚點」就延後。右鍵點我可以暫停或打開設定。" },
            new[] { "哪裡最需要照顧？", "選一個，我會多排適合的動作，之後也能在設定裡改。" },
            new[] { "順便檢查一下桌面", "伸展有幫助，好的工作環境幫助更大。重點是這幾個：" },
        };
        public static int IntroCount { get { return Intro.Length; } }

        public static readonly string[][] FocusOptions = {
            new[] { "neck", "肩頸前傾", "頭往前探、肩膀內縮。多做下巴內收、頸部和上背。" },
            new[] { "hips", "髖部緊繃", "久坐讓骨盆前傾、腰往前拱。多做髖部和腿部。" },
            new[] { "all", "全身痠僵", "什麼都來一點，八個動作輪流做。" },
        };

        static readonly string[][] DeskSetup = {
            new[] { "螢幕在視線高度。", "螢幕上緣和眼睛同高或略低，距離約一個手臂長。" },
            new[] { "筆電墊高。", "用支架或書墊高，另外接鍵盤滑鼠。" },
            new[] { "手肘約 90 度。", "肩膀放鬆、前臂水平，打字時手腕打直。" },
            new[] { "雙腳踩平。", "踩在地板或腳踏墊上，膝蓋大約和髖部同高。" },
            new[] { "坐到椅子最裡面。", "讓椅背撐住下背；撐不到就加個小靠墊。" },
            new[] { "光線從側面來。", "窗戶在螢幕後面或你背後，容易反光、瞇眼。" },
        };

        public void ShowIntro(int step)
        {
            Reset();
            var who = brain.CharacterInfo;
            Func<string, string> fill = t => t.Replace("{name}", who.Name).Replace("{fullName}", who.FullName)
                .Replace("{interval}", brain.Cfg.intervalMinutes.ToString());
            Set(title, fill(Intro[step][0]));
            Set(body, fill(Intro[step][1]));
            if (step == 3)
            {
                pick.Visibility = Visibility.Visible;
                foreach (var f in FocusOptions)
                {
                    var id = f[0];
                    var panel = new StackPanel();
                    panel.Children.Add(Text(f[1], "t-body-sm"));
                    panel.Children.Add(Text(f[2], "t-caption"));
                    var rb = new RadioButton { Style = Theme.Style("radio-card"), Content = panel, GroupName = "intro-focus", Focusable = false,
                        Margin = new Thickness(0, 0, 0, 6), IsChecked = brain.Cfg.focus == id };
                    rb.Checked += (s, e) => brain.SetFocus(id);
                    AutomationProperties.SetName(rb, f[1]);
                    pick.Children.Add(rb);
                }
            }
            if (step == 4)
            {
                setup.Visibility = Visibility.Visible;
                foreach (var item in DeskSetup)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new Path { Data = Geometry.Parse(IconCheck), Stroke = Theme.Brush("primary"), StrokeThickness = 1.8, Width = 16, Height = 16,
                        Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top,
                        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
                    var t = new TextBlock { Style = Theme.Style("t-body-sm-muted") };
                    t.Inlines.Add(new System.Windows.Documents.Run(item[0] + " ") { Foreground = Theme.Brush("ink"), FontWeight = FontWeights.SemiBold });
                    t.Inlines.Add(new System.Windows.Documents.Run(item[1]));
                    Grid.SetColumn(t, 1);
                    row.Children.Add(t);
                    setup.Children.Add(row);
                }
            }
            dots.Visibility = Visibility.Visible;
            for (int i = 0; i < Intro.Length; i++)
                dots.Children.Add(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 0, 0, 0),
                    Fill = Theme.Brush(i == step ? "ink" : "hairline-tertiary") });
            bool last = step == Intro.Length - 1;
            if (last) SetButtons("試做一組", brain.IntroTry, "知道了", brain.FinishIntro);
            else SetButtons("下一步", () => brain.IntroGo(step + 1), "略過", brain.FinishIntro);
            Open("intro", 0, null);
        }

        // ---------- 探頭：沒理它 20 秒，從泡泡上緣探頭（頭在卡片後面，手抓著上緣） ----------

        void Peek()
        {
            if (Kind != "nudge" || hovering || touched || !above || !Native.Motion || !IsVisible) return;
            var back = pet.Sprites.Get("peek-back");
            var front = pet.Sprites.Get("peek-front");
            if (back == null || front == null) return;
            peeking = true;
            double s = pet.S, size = pet.PetSize, cardTop = stack.Margin.Top;
            peekBack.Source = back.Frames[0];
            peekFront.Source = front.Frames[0];
            peekBack.Width = peekBack.Height = peekFront.Width = peekFront.Height = size;
            double x = Math.Max(0, Math.Min(W - size, tailX - size / 2)), y = cardTop - 52 * s;   // 96 viewBox 的 y=40 對齊卡片上緣
            Canvas.SetLeft(peekBack, x);
            Canvas.SetTop(peekBack, y);
            Canvas.SetLeft(peekFront, x);
            Canvas.SetTop(peekFront, y);
            peekBackLayer.Clip = new RectangleGeometry(new Rect(0, 0, W, cardTop + card.ActualHeight - 2));
            backY.BeginAnimation(TranslateTransform.YProperty, null);
            backY.Y = 34 * s;
            peekFront.Opacity = 0;
            peekBack.Visibility = peekFront.Visibility = Visibility.Visible;
            pet.Vanish();
            var pop = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
            After(170, () =>
            {
                backY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(34 * s, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = pop });
                var fin = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(70) };
                peekFront.BeginAnimation(OpacityProperty, fin);
                frontY.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5 * s, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = pop });
            });
            After(170 + 620 + 1250, () =>
            {
                peekFront.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
                var down = new DoubleAnimation(34 * s, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                down.Completed += (o, e) => StopPeek();
                backY.BeginAnimation(TranslateTransform.YProperty, down);
            });
        }

        void StopPeek()
        {
            if (!peeking) return;
            peeking = false;
            peekBack.Visibility = peekFront.Visibility = Visibility.Hidden;
            pet.Reappear();
        }
    }
}
