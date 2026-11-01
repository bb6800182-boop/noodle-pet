using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NoodlePet
{
    // 桌寵本體：只比角色大一點的透明置頂小視窗，不搶焦點；完全透明的地方滑鼠直接穿過去
    public class PetWindow : Window
    {
        public const double Cell = 120, FeetY = 104, HeadRoom = 18, BodyMargin = 24;
        readonly Brain brain;
        readonly Image figure = new Image();
        readonly Border hit = new Border();
        readonly Player player;
        readonly ScaleTransform flip = new ScaleTransform(1, 1), squash = new ScaleTransform(1, 1);
        readonly TranslateTransform lift = new TranslateTransform();
        readonly DispatcherTimer walkTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        readonly DispatcherTimer chores = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        readonly Random rnd = new Random();
        DropWindow drop;
        double scale = 1, vy;
        int dir = 1;
        bool dragging, falling, walkingNow, hovering, vanished;
        string pose = "idle";        // 想要的姿勢；拖曳、掉落、走路時暫時改顯示別的
        DateTime segmentEnd, lastStep = DateTime.Now;

        public Sprites Sprites { get; private set; }
        public Rect Work { get; private set; }
        public IntPtr Handle { get; private set; }
        public bool Arriving { get { return drop != null; } }
        public event Action Moved;

        public PetWindow(Brain b)
        {
            brain = b;
            Title = Program.AppName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            UseLayoutRounding = true;

            figure.Stretch = Stretch.None;
            figure.HorizontalAlignment = HorizontalAlignment.Center;
            figure.VerticalAlignment = VerticalAlignment.Bottom;
            figure.RenderTransformOrigin = new Point(0.5, 1);   // 從腳底壓扁、彈起
            var tg = new TransformGroup();
            tg.Children.Add(flip);
            tg.Children.Add(squash);
            tg.Children.Add(lift);
            figure.RenderTransform = tg;
            figure.IsHitTestVisible = false;
            player = new Player(figure);

            // 幾乎全透明（alpha = 1）的感應區：看不見，但點得到、拖得動
            hit.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            hit.HorizontalAlignment = HorizontalAlignment.Center;
            hit.VerticalAlignment = VerticalAlignment.Bottom;
            hit.Cursor = Cursors.Hand;
            hit.MouseLeftButtonDown += OnPetDown;
            hit.MouseRightButtonUp += (s, e) => brain.ShowMenu();
            hit.MouseEnter += (s, e) => { hovering = true; Refresh(); };
            hit.MouseLeave += (s, e) => { hovering = false; Refresh(); };

            var root = new Grid();
            root.Children.Add(figure);
            root.Children.Add(hit);
            Content = root;

            walkTimer.Tick += (s, e) => WalkStep();
            chores.Tick += (s, e) => { Native.KeepTopmost(Handle); RefreshWork(); };
            SourceInitialized += (s, e) => { Handle = new WindowInteropHelper(this).Handle; Native.MakeToolNoActivate(Handle); };
            LocationChanged += (s, e) => { if (Moved != null) Moved(); };
        }

        public double S { get { return scale; } }
        public double PetSize { get { return Cell * scale; } }
        public Rect CellRect { get { return new Rect(Left, Top + HeadRoom * scale, PetSize, PetSize); } }
        double CenterX { get { return Left + Width / 2; } }
        double Bottom { get { return Top + Height; } }
        double FloorBottom() { return Work.Bottom + (Cell - FeetY) * scale - 1; }   // 腳踩在工作列上緣
        double MinLeft { get { return Work.Left - BodyMargin * scale; } }
        double MaxLeft { get { return Work.Right - Width + BodyMargin * scale; } }
        void PlaceAt(double cx, double bottom) { Left = cx - Width / 2; Top = bottom - Height; }

        // 建好視窗、算好 DPI 與位置，但先不顯示（交給 Appear）
        public void Init()
        {
            new WindowInteropHelper(this).EnsureHandle();
            var src = HwndSource.FromHwnd(Handle);
            if (src != null && src.CompositionTarget != null) Sprites.DpiScale = src.CompositionTarget.TransformToDevice.M11;
            RefreshWork();
            ApplyConfig();
            var st = brain.Store.State;
            if (st.hasPos) PlaceAt(st.x, st.y);
            else PlaceAt(Work.Right - PetSize / 2 - 24, FloorBottom());   // 第一次：右下角，站在工作列上
            if (brain.Walking) PlaceAt(Math.Max(MinLeft + Width / 2, Math.Min(MaxLeft + Width / 2, CenterX)), FloorBottom());
            else ClampFixed();
        }

        // 讀設定：角色或大小變了就換精靈圖，並保持腳底中心不動
        public void ApplyConfig()
        {
            var cfg = brain.Cfg;
            double cx = double.IsNaN(Left) ? 0 : CenterX, bottom = double.IsNaN(Top) ? 0 : Bottom;
            bool resized = Math.Abs(cfg.petScale - scale) > 0.001 || Sprites == null;
            scale = cfg.petScale;
            Width = PetSize;
            Height = PetSize + HeadRoom * scale;
            hit.Width = 50 * scale;
            hit.Height = 88 * scale;
            hit.Margin = new Thickness(0, 0, 0, (Cell - FeetY + 2) * scale);
            if (Sprites == null || Sprites.Character != cfg.character || Math.Abs(Sprites.Size - PetSize) > 0.01)
            {
                Sprites = new Sprites(cfg.character, PetSize);
                player.Stop();
                Refresh();
            }
            if (resized && !double.IsNaN(Left)) PlaceAt(cx, bottom);
            if (brain.Walking && !falling && !Work.IsEmpty && !double.IsNaN(Top)) PlaceAt(CenterX, FloorBottom());
        }

        // 畫面上要顯示哪個動作：拖曳／掉落 → 吊著；走路 → 走；滑鼠移上去 → 揮手；其餘照 pose
        void Refresh()
        {
            if (Sprites == null) return;
            string show = dragging || falling ? "hang" : walkingNow ? "walk" : hovering && pose == "idle" && brain.Phase == Phase.Idle ? "wave" : pose;
            flip.ScaleX = walkingNow ? dir : 1;   // 原圖面向右，往左走就鏡像；示範動作照原圖方向
            player.Play(Sprites, show);
        }

        public void SetPose(string name)
        {
            pose = Sprites != null && Sprites.Has(name) ? name : "idle";
            if (pose != "idle" && pose != "wave" && pose != "walk") walkingNow = false;
            Refresh();
        }

        // 出場：設定是「掛麵條垂降」就從螢幕頂端垂降下來，否則直接出現
        public void Appear(bool entrance, Action done)
        {
            if (drop != null) return;
            RefreshWork();
            if (brain.Walking) PlaceAt(CenterX, FloorBottom());
            if (!entrance || !Native.Motion || brain.Cfg.entrance != "drop")
            {
                ShowNow();
                if (done != null) done();
                return;
            }
            drop = new DropWindow(Sprites, CellRect, Work);
            drop.Landed += () => { ShowNow(); Hop(0.5); };
            drop.Finished += () => { drop = null; if (done != null) done(); };
            drop.Run();
        }

        // 換角色：先溜走，新角色再垂降（關掉動畫效果時直接換）
        public void Reenter()
        {
            if (!IsVisible || drop != null) return;
            if (!Native.Motion || brain.Cfg.entrance != "drop") { Refresh(); return; }
            Hide();
            Appear(true, null);
        }

        void ShowNow()
        {
            if (!IsVisible) { Show(); Native.KeepTopmost(Handle); }
            player.Resume();
            Refresh();
            lastStep = DateTime.Now;
            if (brain.Walking) walkTimer.Start();
            chores.Start();
        }

        public void SetShown(bool show, bool entrance)
        {
            if (show) { if (!IsVisible) Appear(entrance, null); return; }
            if (drop != null) { drop.Abort(); drop = null; }
            if (IsVisible) Hide();
            walkTimer.Stop();
            player.Pause();
        }

        public void ApplyMode()
        {
            RefreshWork();
            if (brain.Walking)
            {
                segmentEnd = DateTime.Now.AddSeconds(2);
                lastStep = DateTime.Now;
                if (Bottom < FloorBottom() - 1) { falling = true; vy = 0; }   // 從原位置掉到工作列上
                else PlaceAt(CenterX, FloorBottom());
                if (IsVisible) walkTimer.Start();
            }
            else
            {
                walkTimer.Stop();
                falling = false;
                walkingNow = false;
                ClampFixed();
                SavePos();
            }
            Refresh();
        }

        public void OnDisplayChanged()
        {
            RefreshWork();
            if (brain.Walking && !falling) PlaceAt(Math.Max(MinLeft + Width / 2, Math.Min(MaxLeft + Width / 2, CenterX)), FloorBottom());
            else ClampFixed();
        }

        // 角色所在螢幕的工作區（扣掉工作列），換算成 WPF 單位；拿不到就用主螢幕
        void RefreshWork()
        {
            var r = SystemParameters.WorkArea;
            var src = Handle == IntPtr.Zero ? null : HwndSource.FromHwnd(Handle);
            if (src != null && src.CompositionTarget != null)
            {
                var a = System.Windows.Forms.Screen.FromHandle(Handle).WorkingArea;
                var m = src.CompositionTarget.TransformFromDevice;
                r = new Rect(m.Transform(new Point(a.Left, a.Top)), m.Transform(new Point(a.Right, a.Bottom)));
            }
            Work = r;
        }

        // 固定模式：角色本體要留在螢幕內
        void ClampFixed()
        {
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
            Left = Math.Max(vl - BodyMargin * scale, Math.Min(vr - Width + BodyMargin * scale, Left));
            Top = Math.Max(vt - HeadRoom * scale - 14 * scale, Math.Min(vb - Height + (Cell - FeetY) * scale, Top));
        }

        public void SavePos()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            var st = brain.Store.State;
            st.x = CenterX;
            st.y = Bottom;
            st.hasPos = true;
            brain.Store.SaveState();
        }

        void OnPetDown(object sender, MouseButtonEventArgs e)
        {
            if (drop != null) return;
            double x0 = Left, y0 = Top;
            dragging = true;
            walkingNow = false;
            Refresh();
            try { DragMove(); } catch (InvalidOperationException) { }
            dragging = false;
            if (Math.Abs(Left - x0) < 4 && Math.Abs(Top - y0) < 4)
            {
                Left = x0;
                Top = y0;
                Refresh();
                brain.PetClicked();
                return;
            }
            RefreshWork();
            if (brain.Walking && Bottom < FloorBottom() - 1) { falling = true; vy = 0; lastStep = DateTime.Now; }
            else
            {
                if (brain.Walking) PlaceAt(CenterX, FloorBottom()); else ClampFixed();
                SavePos();
                Hop(0.35);
            }
            segmentEnd = DateTime.Now.AddSeconds(2);
            Refresh();
        }

        // 走動模式：沿工作列上緣來回走，走一段、停一下；被拖起來放手就掉回去
        void WalkStep()
        {
            var now = DateTime.Now;
            double dt = Math.Min(0.1, (now - lastStep).TotalSeconds);
            lastStep = now;
            if (dragging || drop != null || Work.IsEmpty) return;
            double floor = FloorBottom();
            if (falling)
            {
                vy += 2600 * dt;
                Top = Math.Min(floor - Height, Top + vy * dt);
                Left = Math.Max(MinLeft, Math.Min(MaxLeft, Left));
                if (Bottom >= floor - 0.5) { falling = false; vy = 0; Refresh(); Hop(0.7); SavePos(); }
                return;
            }
            if (Math.Abs(Bottom - floor) > 0.5)   // 工作列高度變了：貼回去
            {
                if (Bottom < floor - 30) { falling = true; Refresh(); return; }
                Top = floor - Height;
            }
            if (!brain.CanWander || vanished) { if (walkingNow) StopWalking(); return; }
            if (now >= segmentEnd)
            {
                if (walkingNow) StopWalking();
                else
                {
                    walkingNow = true;
                    if (rnd.NextDouble() < 0.4) dir = -dir;
                    segmentEnd = now.AddSeconds(4 + rnd.NextDouble() * 8);
                    Refresh();
                }
            }
            if (!walkingNow) return;
            Left += dir * brain.Cfg.walkSpeed * dt;
            if (Left <= MinLeft) { Left = MinLeft; dir = 1; Refresh(); }
            else if (Left >= MaxLeft) { Left = MaxLeft; dir = -1; Refresh(); }
        }

        void StopWalking()
        {
            walkingNow = false;
            segmentEnd = DateTime.Now.AddSeconds(3 + rnd.NextDouble() * 6);
            Refresh();
        }

        // 慶祝的小跳步（照 Noodle 的節奏：壓扁 → 跳起 → 落地壓扁 → 回正）
        public void Hop(double amp)
        {
            if (!Native.Motion) return;
            var d = TimeSpan.FromSeconds(0.62);
            double[] t = { 0, 0.16, 0.48, 0.76, 1 };
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(d, t, 1, 1.07, 0.95, 1.06, 1));
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(d, t, 1, 0.9, 1.06, 0.93, 1));
            lift.BeginAnimation(TranslateTransform.YProperty, Keys(d, t, 0, scale, -12 * amp * scale, 0, 0));
        }

        // 探頭前：往上一跳就不見了（爬到泡泡後面去）
        public void Vanish()
        {
            vanished = true;
            var d = TimeSpan.FromSeconds(0.3);
            double[] t = { 0, 0.35, 1 };
            lift.BeginAnimation(TranslateTransform.YProperty, Keys(d, t, 0, 2 * scale, -18 * scale));
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(d, t, 1, 1.06, 0.94));
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(d, t, 1, 0.92, 1.06));
            figure.BeginAnimation(OpacityProperty, Keys(d, t, 1, 1, 0));
        }

        // 探頭結束：落回原位
        public void Reappear()
        {
            if (!vanished) return;
            vanished = false;
            var d = TimeSpan.FromSeconds(Native.Motion ? 0.42 : 0.01);
            double[] t = { 0, 0.55, 1 };
            lift.BeginAnimation(TranslateTransform.YProperty, Keys(d, t, -18 * scale, 2 * scale, 0));
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(d, t, 1, 1.05, 1));
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(d, t, 1, 0.94, 1));
            figure.BeginAnimation(OpacityProperty, Keys(d, t, 0, 1, 1));
        }

        static DoubleAnimationUsingKeyFrames Keys(TimeSpan d, double[] t, params double[] v)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = d };
            for (int i = 0; i < v.Length; i++)
                a.KeyFrames.Add(new SplineDoubleKeyFrame(v[i], KeyTime.FromPercent(t[i]), new KeySpline(0.3, 0, 0.3, 1)));
            return a;
        }
    }

    // 垂降進場：從螢幕頂端掛著一條麵條垂降，落地後麵條收回去（素材節奏參考 Noodle 的 drop.html）
    public class DropWindow : Window
    {
        readonly Image fig = new Image();
        readonly Player player;
        readonly Canvas rig = new Canvas();
        readonly TranslateTransform rigY = new TranslateTransform();
        readonly double cellTop, cellHeight, s;
        public event Action Landed, Finished;

        public DropWindow(Sprites sprites, Rect cell, Rect work)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            UseLayoutRounding = true;
            IsHitTestVisible = false;
            s = cell.Width / PetWindow.Cell;
            double top = Math.Min(work.Top, cell.Top - 40 * s);
            Left = cell.Left;
            Top = top;
            Width = cell.Width;
            Height = cell.Bottom - top;
            cellTop = cell.Top - top;
            cellHeight = cell.Height;

            double cx = cell.Width / 2, hands = cellTop + 17 * s;   // 手在格子上方 17（96 viewBox 的 y=5）
            var geo = Strand(cx, -Height - 40 * s, hands, s);
            rig.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = Hex("#C99338"), StrokeThickness = 4.4 * s, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            rig.Children.Add(new System.Windows.Shapes.Path { Data = geo, Stroke = Hex("#EDBB5F"), StrokeThickness = 2.8 * s, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            fig.Stretch = Stretch.None;
            Canvas.SetLeft(fig, 0);
            Canvas.SetTop(fig, cellTop);
            rig.Children.Add(fig);
            rig.RenderTransform = rigY;
            var root = new Grid { ClipToBounds = true };
            root.Children.Add(rig);
            Content = root;
            player = new Player(fig);
            player.Play(sprites, "hang");
            SourceInitialized += (o, e) => Native.MakeToolNoActivate(new WindowInteropHelper(this).Handle);
        }

        static Brush Hex(string c) { var b = (Brush)new BrushConverter().ConvertFromString(c); b.Freeze(); return b; }

        static Geometry Strand(double cx, double top, double bottom, double s)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx, top), false, false);
                int flip = 1;
                for (double y = top; y < bottom; y += 10 * s)
                {
                    double next = Math.Min(y + 10 * s, bottom);
                    c.QuadraticBezierTo(new Point(cx + 4 * s * flip, (y + next) / 2), new Point(cx, next), true, true);
                    flip = -flip;
                }
            }
            g.Freeze();
            return g;
        }

        public void Run()
        {
            Show();
            Native.KeepTopmost(new WindowInteropHelper(this).Handle);
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.0) };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(-(cellTop + cellHeight + 20 * s), KeyTime.FromPercent(0)));
            a.KeyFrames.Add(new SplineDoubleKeyFrame(5 * s, KeyTime.FromPercent(0.7), new KeySpline(0.3, 0.7, 0.4, 1)));
            a.KeyFrames.Add(new SplineDoubleKeyFrame(-1 * s, KeyTime.FromPercent(0.88), new KeySpline(0.4, 0, 0.6, 1)));
            a.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1), new KeySpline(0.4, 0, 0.6, 1)));
            a.Completed += (o, e) => Land();
            rigY.BeginAnimation(TranslateTransform.YProperty, a);
        }

        void Land()
        {
            fig.Visibility = Visibility.Hidden;
            player.Stop();
            if (Landed != null) Landed();
            var up = new DoubleAnimation(0, -(cellTop + 60 * s), TimeSpan.FromSeconds(0.3)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            up.Completed += (o, e) => { Close(); if (Finished != null) Finished(); };
            rigY.BeginAnimation(TranslateTransform.YProperty, up);
        }

        public void Abort()
        {
            player.Stop();
            rigY.BeginAnimation(TranslateTransform.YProperty, null);
            Close();
        }
    }
}
