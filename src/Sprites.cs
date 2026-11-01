using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NoodlePet
{
    // 角色名單（素材來自 Noodle）
    public class Character
    {
        public string Id, Name, Kind;
        public string FullName { get { return Kind + " " + Name; } }

        public static readonly Character[] All = {
            new Character { Id = "rigatoni", Name = "Toni", Kind = "水管麵" },
            new Character { Id = "spaghetti", Name = "Sammy", Kind = "直麵" },
            new Character { Id = "penne", Name = "Patty", Kind = "筆管麵" },
            new Character { Id = "lasagna", Name = "Lenny", Kind = "千層麵" },
            new Character { Id = "sprout", Name = "Sprout", Kind = "小芽" },
        };

        public static Character Get(string id) { return All.FirstOrDefault(c => c.Id == id) ?? All[0]; }
    }

    // 一個動作：去重後的影格＋播放順序與每格停留時間
    public class Pose
    {
        public BitmapSource[] Frames;
        public int[] Index;
        public int[] Ms;
    }

    // 精靈圖：從內嵌 PNG 切格，並預先縮放成螢幕實際像素（播放時不必每格縮放，省 CPU 也更清楚）
    public class Sprites
    {
        public static double DpiScale = 1;
        public readonly string Character;
        public readonly double Size;   // 一格的 DIP 大小
        readonly Dictionary<string, object> poses;
        readonly int cell, cols;
        readonly Dictionary<string, Pose> cache = new Dictionary<string, Pose>();

        public Sprites(string character, double size)
        {
            Character = character;
            Size = size;
            var m = Res.Json<Dictionary<string, object>>(character + ".sprites.json");
            cell = Convert.ToInt32(m["cell"]) * Convert.ToInt32(m["scale"]);
            cols = Convert.ToInt32(m["cols"]);
            poses = (Dictionary<string, object>)m["poses"];
        }

        public bool Has(string name) { return poses.ContainsKey(name); }

        public Pose Get(string name)
        {
            Pose p;
            if (cache.TryGetValue(name, out p)) return p;
            if (!poses.ContainsKey(name)) return name == "idle" ? null : Get("idle");
            var info = (Dictionary<string, object>)poses[name];
            int n = Convert.ToInt32(info["frames"]);
            // 解碼時就直接縮成螢幕實際像素（整張一次縮），再切成一格一格；不另外複製影格，省記憶體也省 CPU
            int px = Math.Max(8, (int)Math.Round(Size * DpiScale));
            var sheet = new BitmapImage();
            using (var s = Res.Open(Character + "." + name + ".png"))
            {
                sheet.BeginInit();
                sheet.CacheOption = BitmapCacheOption.OnLoad;
                sheet.DecodePixelWidth = Math.Min(n, cols) * px;
                sheet.StreamSource = s;
                sheet.EndInit();
            }
            sheet.Freeze();
            var frames = new BitmapSource[n];
            for (int i = 0; i < n; i++)
            {
                int x = i % cols * px, y = i / cols * px;
                var f = new CroppedBitmap(sheet, new Int32Rect(x, y, Math.Min(px, sheet.PixelWidth - x), Math.Min(px, sheet.PixelHeight - y)));
                f.Freeze();
                frames[i] = f;
            }
            var seq = ((IEnumerable)info["seq"]).Cast<object>().Select(o => ((IEnumerable)o).Cast<object>().Select(v => Convert.ToInt32(v)).ToArray()).ToArray();
            p = new Pose { Frames = frames, Index = seq.Select(x => x[0]).ToArray(), Ms = seq.Select(x => Math.Max(16, x[1])).ToArray() };
            cache[name] = p;
            return p;
        }

        public void Release(string name) { cache.Remove(name); }
    }

    // 把一個動作播在 Image 上（每格停留時間不同，靜止的時候計時器幾乎不醒來）
    public class Player
    {
        readonly Image image;
        readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);
        Sprites sprites;
        Pose pose;
        int step;
        public string Name { get; private set; }

        public Player(Image img)
        {
            image = img;
            image.Stretch = Stretch.Fill;   // 影格像素剛好等於螢幕像素，Fill 到格子大小就是 1:1
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            timer.Tick += (s, e) => { step = (step + 1) % pose.Index.Length; Show(); };
        }

        public void Play(Sprites sp, string name)
        {
            if (sp == sprites && name == Name) return;
            var p = sp.Get(name);
            if (p == null) return;
            image.Width = image.Height = sp.Size;
            sprites = sp;
            pose = p;
            Name = name;
            step = 0;
            Show();
        }

        void Show()
        {
            image.Source = pose.Frames[pose.Index[step]];
            timer.Stop();
            if (pose.Index.Length > 1) { timer.Interval = TimeSpan.FromMilliseconds(pose.Ms[step]); timer.Start(); }
        }

        public void Pause() { timer.Stop(); }
        public void Resume() { if (pose != null && pose.Index.Length > 1 && !timer.IsEnabled) Show(); }
        public void Stop() { timer.Stop(); Name = null; sprites = null; pose = null; }
    }
}
