using System;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

[assembly: AssemblyTitle("拉麵")]
[assembly: AssemblyDescription("拉筋的麵：會提醒你起來伸展、喝水的桌面寵物")]
[assembly: AssemblyProduct("拉麵")]
[assembly: AssemblyCopyright("© 2026 C520；角色與動作素材：Noodle © 2026 Pete Cybriwsky（MIT）")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]
// 宣告目標 .NET Framework 4.8：才會啟用新版 WPF 的無障礙行為（例如螢幕閱讀器的即時播報）
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace NoodlePet
{
    static class Program
    {
        public const string AppName = "拉麵";
        public const string Version = "1.1.2";

        [STAThread]
        static void Main()
        {
            bool first;
            using (new Mutex(true, @"Local\NoodlePet.SingleInstance", out first))
            {
                if (!first) { HelloRunningPet(); return; }
                // 桌寵只畫幾張小圖，用軟體繪圖就夠；不載入顯示卡驅動（Intel 驅動光載入就吃上百 MB 記憶體）
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                var store = Store.Load();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Store.LogError(e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Store.LogError(e.ExceptionObject as Exception);
                Tray tray = null;
                Brain brain = null;
                try
                {
                    Theme.Load(app);
                    brain = new Brain(store);
                    tray = new Tray(brain);
                    brain.Start(tray);
                }
                catch (Exception e)
                {
                    // 一啟動就失敗：記下原因、清掉系統匣圖示，再告訴使用者（這時自己的介面可能還沒好，只能用系統訊息框）
                    Store.LogError(e);
                    if (tray != null) tray.Dispose();
                    System.Windows.Forms.MessageBox.Show(AppName + " 啟動失敗：" + e.Message + "\n\n詳細內容在 " + Store.Dir + "\\error.log", AppName);
                    return;
                }
                app.Exit += (s, e) => { brain.Shutdown(); tray.Dispose(); };
                app.SessionEnding += (s, e) => brain.Shutdown();
                app.Run();
            }
        }

        // 已經有一隻在跑：叫牠出來打招呼就好
        static void HelloRunningPet()
        {
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    wc.UploadString("http://127.0.0.1:" + Store.Load().Config.claudePort + "/hello", "{}");
                }
            }
            catch { }
        }
    }

    // 設計 token：Theme.xaml 放進全程式資源，其他地方用 key 取用
    static class Theme
    {
        public static void Load(Application app)
        {
            using (var s = Res.Open("Theme.xaml")) app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
        }

        public static Brush Brush(string key) { return (Brush)Application.Current.FindResource(key); }
        public static Style Style(string key) { return (Style)Application.Current.FindResource(key); }
    }
}
