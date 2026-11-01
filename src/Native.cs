using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace NoodlePet
{
    // Windows 原生功能：閒置時間、全螢幕偵測、通話偵測、視窗樣式、深色標題列、開機啟動
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // 鍵盤滑鼠多久沒動（秒）
        public static double IdleSeconds()
        {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            if (!GetLastInputInfo(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
        }

        // 全螢幕程式、D3D 全螢幕或簡報模式
        public static bool FullscreenBusy()
        {
            int state;
            if (SHQueryUserNotificationState(out state) != 0) return false;
            return state == 2 || state == 3 || state == 4;
        }

        // 工具視窗（不進 Alt+Tab）＋點了也不搶鍵盤焦點
        public static void MakeToolNoActivate(IntPtr hwnd)
        {
            const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        // 重新壓到最上層（不啟用視窗、不動位置大小）
        public static void KeepTopmost(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // 深色標題列＋標題列顏色對齊 canvas／hairline／ink（Windows 11 才有顏色，Windows 10 只有深色）
        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1, caption = 0x00020101, border = 0x002a2523, text = 0x00f8f8f7;
            try
            {
                DwmSetWindowAttribute(hwnd, 20, ref on, 4);
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
                DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            }
            catch { }
        }

        // 系統動畫效果（設定 → 協助工具 → 視覺效果 → 動畫效果）；關掉就不做滑動、探頭、垂降
        public static bool Motion { get { return SystemParameters.ClientAreaAnimation; } }

        // Windows 隱私設定會記錄每個 App 用麥克風／鏡頭的時間；LastUsedTimeStop = 0 代表正在用
        public static bool MicOrCameraInUse()
        {
            try
            {
                foreach (var cap in new[] { "microphone", "webcam" })
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap))
                        if (key != null && InUse(key, 0)) return true;
            }
            catch { }
            return false;   // 讀不到就當作沒在通話，不要因此卡住提醒
        }

        static bool InUse(RegistryKey key, int depth)
        {
            object start = key.GetValue("LastUsedTimeStart"), stop = key.GetValue("LastUsedTimeStop");
            if (start is long && (long)start != 0 && stop is long && (long)stop == 0) return true;
            if (depth >= 2) return false;
            foreach (var name in key.GetSubKeyNames())
                using (var sub = key.OpenSubKey(name))
                    if (sub != null && InUse(sub, depth + 1)) return true;
            return false;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "NoodlePet";

        public static bool AutoStart
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue(RunName, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                        else k.DeleteValue(RunName, false);
                    }
                }
                catch (Exception e) { Store.LogError(e); }
            }
        }
    }
}
