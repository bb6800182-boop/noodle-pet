using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace NoodlePet
{
    // calendar-link.json：Google 月曆連動設定。放在 Dropbox 的專案資料夾，跟著 Dropbox 同步到每台電腦；裡面有金鑰，不進 git
    public class CalendarLink
    {
        public string url { get; set; }               // Apps Script 網頁應用程式網址（…/exec）
        public string token { get; set; }             // 桌寵和 Apps Script 共用的金鑰
        public List<string> calendars { get; set; }   // 要讀的行事曆 id；空的 = 只讀主要行事曆
        public bool pauseDuringEvents { get; set; }   // 開會時暫停伸展、喝水提醒
        public bool remind { get; set; }              // 活動開始前跳出提醒
        public int remindMinutes { get; set; }        // 提前幾分鐘

        public CalendarLink() { url = ""; token = ""; calendars = new List<string>(); pauseDuringEvents = true; remind = true; remindMinutes = 10; }

        public bool Linked { get { return GoogleCalendar.ValidUrl(url) && !string.IsNullOrEmpty(token); } }

        public CalendarLink Copy()
        {
            return new CalendarLink { url = url, token = token, calendars = calendars.ToList(), pauseDuringEvents = pauseDuringEvents, remind = remind, remindMinutes = remindMinutes };
        }
    }

    public class CalEvent
    {
        public string Key, CalendarId, Title, Location;
        public DateTime Start, End;
        public bool AllDay, Busy, Declined;
    }

    public class CalInfo
    {
        public string Id, Name;
        public bool Primary;
    }

    class CalendarException : Exception { public CalendarException(string m) : base(m) { } }

    // 透過使用者自己部署的 Apps Script 讀 Google 月曆（只讀）：每 5 分鐘在背景抓一次「前 6 小時到後 36 小時」的活動
    public class GoogleCalendar
    {
        public const int ScriptVersion = 1;
        public static readonly string LinkFile = Path.Combine(ProjectDir(), "calendar-link.json");
        static readonly Regex UrlRule = new Regex(@"^https://script\.google\.com/(macros|a/macros/[A-Za-z0-9.-]+)/s/[A-Za-z0-9_-]{20,}/exec$");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        const string Agent = "NoodlePet/" + Program.Version;

        public CalendarLink Link = new CalendarLink();
        public string LinkError;                         // 讀連動設定檔的問題（null = 沒問題）
        public List<CalEvent> Events = new List<CalEvent>();
        public List<CalInfo> Calendars = new List<CalInfo>();
        public DateTime LastSync = DateTime.MinValue;    // 上次成功同步
        public string SyncError;                         // 上次同步失敗的原因（null = 沒問題）
        public bool Syncing { get; private set; }
        public event Action Changed;

        readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
        DateTime stamp = DateTime.MinValue, lastTry = DateTime.MinValue;
        int generation;   // 連動目標換了就作廢還在路上的結果

        public GoogleCalendar() { Reload(true); }

        // exe 在「專案\dist」裡就用專案資料夾（Dropbox 裡那份），否則用 exe 所在的資料夾
        static string ProjectDir()
        {
            var exe = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(Path.GetFileName(exe), "dist", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(exe) : exe;
        }

        public static bool ValidUrl(string url) { return url != null && UrlRule.IsMatch(url.Trim()); }

        public static string NewToken()
        {
            var b = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // 要貼到 Apps Script 的程式碼（金鑰已經填好）
        public static string Script(string token)
        {
            using (var r = new StreamReader(Res.Open("calendar-relay.gs"), Encoding.UTF8)) return r.ReadToEnd().Replace("{{TOKEN}}", token);
        }

        // ---------- 連動設定檔 ----------

        // 檔案有變（自己存的、或別台電腦改了經 Dropbox 同步過來）才重讀；回傳有沒有重讀
        public bool Reload(bool force)
        {
            DateTime s;
            try { s = File.Exists(LinkFile) ? File.GetLastWriteTimeUtc(LinkFile) : DateTime.MinValue; }
            catch { return false; }
            if (!force && s == stamp) return false;
            stamp = s;
            var link = new CalendarLink();
            string error = null;
            if (s != DateTime.MinValue)
            {
                try
                {
                    var d = Json.DeserializeObject(File.ReadAllText(LinkFile, Encoding.UTF8)) as Dictionary<string, object>;
                    if (d == null) throw new FormatException("內容不是 JSON 物件");
                    object v;
                    if (d.TryGetValue("url", out v) && v is string) link.url = ((string)v).Trim();
                    if (d.TryGetValue("token", out v) && v is string) link.token = (string)v;
                    if (d.TryGetValue("calendars", out v) && v is IEnumerable && !(v is string))
                        link.calendars = ((IEnumerable)v).OfType<string>().Where(x => x.Length > 0).Distinct().ToList();
                    if (d.TryGetValue("pauseDuringEvents", out v) && v is bool) link.pauseDuringEvents = (bool)v;
                    if (d.TryGetValue("remind", out v) && v is bool) link.remind = (bool)v;
                    if (d.TryGetValue("remindMinutes", out v) && v != null && !(v is string) && !(v is bool))
                        link.remindMinutes = Convert.ToInt32(v, CultureInfo.InvariantCulture);
                }
                catch (IOException) { stamp = DateTime.MaxValue; return false; }   // Dropbox 正在寫：下一輪再讀
                catch (Exception e) { error = "連動設定檔格式不對（" + e.Message + "）"; }
            }
            LinkError = error;
            Apply(link);
            return true;
        }

        // 存檔並套用（設定視窗用）：一律傳新的物件進來，才分得出網址、金鑰、行事曆有沒有換
        public bool Set(CalendarLink next, out string error)
        {
            next.remindMinutes = Math.Max(0, Math.Min(120, next.remindMinutes));
            var sb = new StringBuilder("{\n");
            sb.Append("  \"url\": ").Append(Json.Serialize(next.url ?? "")).Append(",\n");
            sb.Append("  \"token\": ").Append(Json.Serialize(next.token ?? "")).Append(",\n");
            sb.Append("  \"calendars\": ").Append(Json.Serialize(next.calendars)).Append(",\n");
            sb.Append("  \"pauseDuringEvents\": ").Append(next.pauseDuringEvents ? "true" : "false").Append(",\n");
            sb.Append("  \"remind\": ").Append(next.remind ? "true" : "false").Append(",\n");
            sb.Append("  \"remindMinutes\": ").Append(next.remindMinutes.ToString(CultureInfo.InvariantCulture)).Append("\n}\n");
            try
            {
                File.WriteAllText(LinkFile, sb.ToString(), Utf8);
                stamp = File.GetLastWriteTimeUtc(LinkFile);
            }
            catch (Exception e) { error = e.Message; Store.LogError(e); return false; }
            error = null;
            LinkError = null;
            Apply(next);
            return true;
        }

        void Apply(CalendarLink link)
        {
            link.remindMinutes = Math.Max(0, Math.Min(120, link.remindMinutes));
            bool target = link.url != Link.url || link.token != Link.token;
            bool cals = !link.calendars.SequenceEqual(Link.calendars);
            Link = link;
            if (target || cals)
            {
                generation++;
                Events = new List<CalEvent>();
                lastTry = DateTime.MinValue;   // 下一輪馬上重抓
                SyncError = null;
            }
            if (target) { Calendars = new List<CalInfo>(); LastSync = DateTime.MinValue; }
            Raise();
        }

        void Raise() { if (Changed != null) Changed(); }

        // ---------- 同步 ----------

        public void Tick(DateTime now)
        {
            if (!Link.Linked || Syncing) return;
            if (lastTry <= now && (now - lastTry).TotalMinutes < 5) return;
            Sync();
        }

        public void SyncNow() { if (Link.Linked && !Syncing) Sync(); }   // 剛睡醒、設定剛改、按了「立即同步」

        void Sync()
        {
            Syncing = true;
            lastTry = DateTime.Now;
            int gen = generation;
            string url = Link.url.Trim(), token = Link.token;
            var cals = Link.calendars.ToList();
            Raise();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<CalEvent> events = null;
                List<CalInfo> infos = null;
                string error = null;
                try { Fetch(url, token, cals, out events, out infos); }
                catch (Exception e) { error = Friendly(e); }
                ui.BeginInvoke(new Action(() =>
                {
                    Syncing = false;
                    if (gen == generation)   // 連動目標沒換，結果才算數
                    {
                        if (error == null) { Events = events; Calendars = infos; LastSync = DateTime.Now; SyncError = null; }
                        else SyncError = error;
                    }
                    Raise();
                }));
            });
        }

        static void Fetch(string url, string token, List<string> cals, out List<CalEvent> events, out List<CalInfo> infos)
        {
            if (!ValidUrl(url)) throw new CalendarException("網址不是 Apps Script 網頁應用程式的網址");
            var json = new JavaScriptSerializer();   // 背景執行緒用自己的一份
            var body = json.Serialize(new Dictionary<string, object> { { "token", token }, { "calendars", cals }, { "v", ScriptVersion } });
            Dictionary<string, object> d;
            try { d = json.DeserializeObject(Post(url, body)) as Dictionary<string, object>; }
            catch (ArgumentException) { d = null; }
            if (d == null) throw new CalendarException("Apps Script 回傳的不是預期的內容：確認程式碼有整段貼上，而且部署的是最新版本");
            object v;
            if (!d.TryGetValue("ok", out v) || !(v is bool) || !(bool)v)
            {
                var err = d.TryGetValue("error", out v) ? v as string : null;
                if (err == "unauthorized") throw new CalendarException("金鑰對不上：請重新複製程式碼貼到 Apps Script，再部署新版本");
                throw new CalendarException("Apps Script 出錯了：" + Short(err ?? "沒有說明", 120));
            }
            infos = new List<CalInfo>();
            if (d.TryGetValue("calendars", out v) && v is IEnumerable)
                foreach (var o in (IEnumerable)v)
                {
                    var c = o as Dictionary<string, object>;
                    if (c == null || Str(c, "id").Length == 0) continue;
                    infos.Add(new CalInfo { Id = Str(c, "id"), Name = Short(Str(c, "name"), 80), Primary = Flag(c, "primary", false) });
                }
            events = new List<CalEvent>();
            if (d.TryGetValue("events", out v) && v is IEnumerable)
                foreach (var o in (IEnumerable)v)
                {
                    var e = ParseEvent(o as Dictionary<string, object>);
                    if (e != null) events.Add(e);
                }
        }

        static CalEvent ParseEvent(Dictionary<string, object> e)
        {
            if (e == null) return null;
            try
            {
                var ev = new CalEvent
                {
                    CalendarId = Str(e, "cal"),
                    Title = Short(Str(e, "title"), 200),
                    Location = Short(Str(e, "location"), 120),
                    Start = FromMs(e["start"]),
                    End = FromMs(e["end"]),
                    AllDay = Flag(e, "allDay", false),
                    Declined = Flag(e, "declined", false),
                };
                ev.Busy = Flag(e, "busy", true) && !ev.Declined;
                if (ev.End < ev.Start) ev.End = ev.Start;
                ev.Key = ev.CalendarId + "|" + Str(e, "id") + "|" + ev.Start.Ticks.ToString(CultureInfo.InvariantCulture);
                return ev;
            }
            catch { return null; }   // 單筆格式不對就略過
        }

        static DateTime FromMs(object v) { return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(v, CultureInfo.InvariantCulture)).LocalDateTime; }
        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is string ? (string)v : ""; }
        static bool Flag(Dictionary<string, object> d, string k, bool fallback) { object v; return d.TryGetValue(k, out v) && v is bool ? (bool)v : fallback; }
        static string Short(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length > n ? s.Substring(0, n) + "…" : s; }

        // 金鑰放在 POST 內容裡送到 script.google.com；Google 會轉址到 googleusercontent.com 拿結果，只接受這兩個網域
        static string Post(string url, string json)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.UserAgent = Agent;
            req.AllowAutoRedirect = false;
            req.Timeout = req.ReadWriteTimeout = 20000;
            var data = Utf8.GetBytes(json);
            req.ContentLength = data.Length;
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (var res = Respond(req))
            {
                int code = (int)res.StatusCode;
                if (code < 300 || code >= 400) return Read(res);
                Uri next;
                if (!Uri.TryCreate(res.Headers["Location"] ?? "", UriKind.Absolute, out next) || next.Scheme != Uri.UriSchemeHttps)
                    throw new CalendarException("Apps Script 轉址的網址不對");
                if (next.Host == "accounts.google.com")
                    throw new CalendarException("Apps Script 要求登入：部署時「誰可以存取」要選「所有人」");
                if (!next.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase))
                    throw new CalendarException("Apps Script 轉址到不認識的網站（" + next.Host + "），先停止");
                var get = (HttpWebRequest)WebRequest.Create(next);
                get.UserAgent = Agent;
                get.AllowAutoRedirect = false;
                get.Timeout = get.ReadWriteTimeout = 20000;
                using (var r2 = Respond(get)) return Read(r2);
            }
        }

        static HttpWebResponse Respond(HttpWebRequest req)
        {
            try { return (HttpWebResponse)req.GetResponse(); }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                if (r == null) throw;
                int code = (int)r.StatusCode;
                r.Dispose();
                if (code == 401 || code == 403) throw new CalendarException("Apps Script 拒絕存取（" + code + "）：部署時「誰可以存取」要選「所有人」");
                if (code == 404) throw new CalendarException("找不到這個 Apps Script（404）：確認網址是部署後拿到的 …/exec，而且部署還在");
                throw new CalendarException("Apps Script 回應錯誤（" + code + "），稍後會自動再試");
            }
        }

        static string Read(HttpWebResponse res)
        {
            using (var s = res.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > 2 * 1024 * 1024) throw new CalendarException("Apps Script 回傳的資料太大");
                }
                return Utf8.GetString(ms.ToArray());
            }
        }

        static string Friendly(Exception e)
        {
            if (e is CalendarException) return e.Message;
            var w = e as WebException;
            if (w != null)
            {
                if (w.Status == WebExceptionStatus.NameResolutionFailure || w.Status == WebExceptionStatus.ConnectFailure) return "連不上網路，稍後會自動再試";
                if (w.Status == WebExceptionStatus.Timeout) return "Google 太久沒回應，稍後會自動再試";
                if (w.Status == WebExceptionStatus.TrustFailure || w.Status == WebExceptionStatus.SecureChannelFailure) return "安全連線失敗（可能是公司網路或防毒擋住了），稍後會自動再試";
            }
            Store.LogError(e);
            return "同步失敗：" + Short(e.Message, 120);
        }

        // ---------- 查詢 ----------

        // 現在正在進行、而且算「忙碌」的活動（全天活動、標成有空、你拒絕的都不算）
        public CalEvent BusyNow(DateTime now)
        {
            return Events.Where(e => e.Busy && !e.AllDay && e.Start <= now && now < e.End).OrderBy(e => e.End).FirstOrDefault();
        }

        // 該跳出提醒的活動：開始前 remindMinutes 分鐘起，到開始後 5 分鐘內（電腦剛開機也來得及）
        public IEnumerable<CalEvent> Due(DateTime now)
        {
            if (!Link.Linked || !Link.remind) return Enumerable.Empty<CalEvent>();
            return Events.Where(e => !e.AllDay && !e.Declined && e.Start.AddMinutes(-Link.remindMinutes) <= now && now < e.Start.AddMinutes(5)).ToList();
        }

        public CalEvent NextEvent(DateTime now)
        {
            return Events.Where(e => !e.AllDay && !e.Declined && e.Start > now).OrderBy(e => e.Start).FirstOrDefault();
        }
    }
}
