using System;
using System.Globalization;

namespace NoodlePet
{
    /// <summary>
    /// 介面語言：Windows 的顯示語言是中文就顯示中文，其他語言一律英文。
    /// 中英文寫在同一行（L.T("中文", "English")），改字時才不會漏改一邊。
    /// 環境變數 LAMIAN_LANG=en／zh 可以強制指定（測試英文介面用）。
    /// </summary>
    static class L
    {
        public static readonly bool Zh =
            (Environment.GetEnvironmentVariable("LAMIAN_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        public static string T(string zh, string en) { return Zh ? zh : en; }
    }
}
