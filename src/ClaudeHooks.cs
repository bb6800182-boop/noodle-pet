using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace NoodlePet
{
    // 接上 Claude Code：在 ~/.claude/settings.json 加三個 http hook。
    // 一律先算出差異給使用者看、按確認才寫入；寫入前先備份，而且確認期間檔案被別人改過就不寫。
    public static class ClaudeHooks
    {
        static readonly string[][] Events = {
            new[] { "UserPromptSubmit", "prompt" },
            new[] { "Stop", "stop" },
            new[] { "StopFailure", "stop" },
        };

        public static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"); }
        }

        static string Url(int port, string path) { return "http://127.0.0.1:" + port + "/" + path; }

        // linked：三個都在；partial：只有一部分；none：沒有；error：讀不懂檔案
        public static string Status(int port, out string error, out List<string> notes)
        {
            Dictionary<string, object> root;
            string text;
            notes = new List<string>();
            if (!Read(out root, out text, out error)) return "error";
            if (root == null) return "none";
            if (root.ContainsKey("allowedHttpHookUrls")) notes.Add("你的設定有 allowedHttpHookUrls 白名單，要把 " + Url(port, "*") + " 加進去 hook 才會送出。");
            object off;
            if (root.TryGetValue("disableAllHooks", out off) && off is bool && (bool)off) notes.Add("你的設定把所有 hook 關掉了（disableAllHooks），連結了也不會觸發。");
            int found = Events.Count(ev => Has(root, ev[0], Url(port, ev[1])));
            return found == Events.Length ? "linked" : found == 0 ? "none" : "partial";
        }

        // 算出改完的內容（不寫檔）；removePort／addPort 填 0 代表不移除／不加入
        public static bool Plan(int removePort, int addPort, out string before, out string after, out string error)
        {
            Dictionary<string, object> root;
            after = null;
            if (!Read(out root, out before, out error)) return false;
            if (root == null) root = new Dictionary<string, object>();
            try
            {
                if (removePort > 0) foreach (var ev in Events) Remove(root, ev[0], Url(removePort, ev[1]));
                if (addPort > 0) foreach (var ev in Events) Add(root, ev[0], Url(addPort, ev[1]));
            }
            catch (FormatException e) { error = e.Message; return false; }
            var sb = new StringBuilder();
            Write(sb, root, 0);
            var nl = before != null && before.Contains("\r\n") ? "\r\n" : "\n";
            after = sb.ToString().Replace("\n", nl) + nl;
            return true;
        }

        public static bool Apply(string before, string after, out string backup, out string error)
        {
            backup = null;
            error = null;
            var path = SettingsPath;
            try
            {
                string now = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
                if ((now ?? "") != (before ?? "")) { error = "settings.json 在你確認的時候被別的程式改過了，請再開一次確認視窗。"; return false; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (now != null)
                {
                    backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    for (int k = 1; File.Exists(backup); k++) backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + k;
                    File.Copy(path, backup);
                }
                File.WriteAllText(path, after, new UTF8Encoding(false));
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        static bool Read(out Dictionary<string, object> root, out string text, out string error)
        {
            root = null;
            text = null;
            error = null;
            try
            {
                if (!File.Exists(SettingsPath)) return true;
                text = File.ReadAllText(SettingsPath, Encoding.UTF8);
                if (text.Trim().Length == 0) return true;
                root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text) as Dictionary<string, object>;
                if (root == null) { error = "settings.json 不是 JSON 物件，先不動它。"; return false; }
                return true;
            }
            catch (Exception e) { error = "讀不懂 settings.json：" + e.Message; return false; }
        }

        static bool IsOurs(object handler, string url)
        {
            var h = handler as IDictionary<string, object>;
            object type, u;
            return h != null && h.TryGetValue("type", out type) && "http".Equals(type) && h.TryGetValue("url", out u) && url.Equals(u);
        }

        static IEnumerable<object> Items(object v)
        {
            var e = v as IEnumerable;
            return e == null || v is string || v is IDictionary ? Enumerable.Empty<object>() : e.Cast<object>();
        }

        static bool Has(Dictionary<string, object> root, string ev, string url)
        {
            object hooks, groups;
            var d = root.TryGetValue("hooks", out hooks) ? hooks as IDictionary<string, object> : null;
            if (d == null || !d.TryGetValue(ev, out groups)) return false;
            foreach (var g in Items(groups))
            {
                var gd = g as IDictionary<string, object>;
                object hs;
                if (gd != null && gd.TryGetValue("hooks", out hs) && Items(hs).Any(h => IsOurs(h, url))) return true;
            }
            return false;
        }

        static void Add(Dictionary<string, object> root, string ev, string url)
        {
            if (Has(root, ev, url)) return;
            object hooks;
            if (!root.TryGetValue("hooks", out hooks) || hooks == null) { hooks = new Dictionary<string, object>(); root["hooks"] = hooks; }
            var d = hooks as IDictionary<string, object>;
            if (d == null) throw new FormatException("settings.json 的 hooks 不是物件，先不動它。");
            object groups;
            d.TryGetValue(ev, out groups);
            if (groups != null && !(groups is IList)) throw new FormatException("settings.json 的 hooks." + ev + " 不是陣列，先不動它。");
            var list = Items(groups).ToList();
            list.Add(new Dictionary<string, object> {
                { "hooks", new List<object> { new Dictionary<string, object> { { "type", "http" }, { "url", url }, { "timeout", 2 } } } } });
            d[ev] = list;
        }

        static void Remove(Dictionary<string, object> root, string ev, string url)
        {
            object hooks, groups;
            var d = root.TryGetValue("hooks", out hooks) ? hooks as IDictionary<string, object> : null;
            if (d == null || !d.TryGetValue(ev, out groups)) return;
            var kept = new List<object>();
            foreach (var g in Items(groups))
            {
                var gd = g as IDictionary<string, object>;
                object hs;
                if (gd == null || !gd.TryGetValue("hooks", out hs)) { kept.Add(g); continue; }
                var left = Items(hs).Where(h => !IsOurs(h, url)).ToList();
                if (left.Count == Items(hs).Count()) { kept.Add(g); continue; }
                if (left.Count == 0) continue;
                gd["hooks"] = left;
                kept.Add(g);
            }
            if (kept.Count > 0) d[ev] = kept; else d.Remove(ev);
            if (d.Count == 0) root.Remove("hooks");
        }

        // 2 格縮排的 JSON；非 ASCII 字元照原樣寫（不轉 \u）
        static void Write(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }
            var s = v as string;
            if (s != null) { Quote(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            var d = v as IDictionary<string, object>;
            if (d != null)
            {
                if (d.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var kv in d)
                {
                    sb.Append(' ', (indent + 1) * 2);
                    Quote(sb, kv.Key);
                    sb.Append(": ");
                    Write(sb, kv.Value, indent + 1);
                    sb.Append(++i < d.Count ? ",\n" : "\n");
                }
                sb.Append(' ', indent * 2).Append('}');
                return;
            }
            var f = v as IFormattable;
            if (f != null) { sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); return; }
            var items = Items(v).ToList();
            if (items.Count == 0) { sb.Append("[]"); return; }
            sb.Append("[\n");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(' ', (indent + 1) * 2);
                Write(sb, items[i], indent + 1);
                sb.Append(i < items.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(' ', indent * 2).Append(']');
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\b') sb.Append("\\b");
                else if (c == '\f') sb.Append("\\f");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // 逐行差異（LCS）：' ' 沒變、'+' 新增、'-' 移除
        public static List<KeyValuePair<char, string>> Diff(string a, string b)
        {
            var x = Lines(a);
            var y = Lines(b);
            int n = x.Length, m = y.Length;
            var L = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    L[i, j] = x[i] == y[j] ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
            var r = new List<KeyValuePair<char, string>>();
            int p = 0, q = 0;
            while (p < n || q < m)
            {
                if (p < n && q < m && x[p] == y[q]) { r.Add(new KeyValuePair<char, string>(' ', x[p])); p++; q++; }
                else if (p < n && (q == m || L[p + 1, q] >= L[p, q + 1])) { r.Add(new KeyValuePair<char, string>('-', x[p])); p++; }
                else { r.Add(new KeyValuePair<char, string>('+', y[q])); q++; }
            }
            return r;
        }

        static string[] Lines(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            var lines = s.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            return lines.ToArray();
        }
    }
}
