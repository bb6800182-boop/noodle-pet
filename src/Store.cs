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
    // config.json：設定視窗或手動修改都可以（存檔後幾秒內生效）
    public class Config
    {
        public int intervalMinutes { get; set; }     // 連續使用多久提醒一次
        public int breakResetMinutes { get; set; }   // 離開鍵盤滑鼠多久，算你已經休息過
        public int snoozeMinutes { get; set; }       // 按「晚點」延後幾分鐘
        public int bubbleSeconds { get; set; }       // 提醒泡泡沒理它，幾秒後自己收起來
        public string character { get; set; }        // rigatoni／spaghetti／penne／lasagna／sprout
        public string focus { get; set; }            // neck／hips／all／custom
        public List<string> exercises { get; set; }  // focus = custom 時手動勾選的動作
        public bool announce { get; set; }           // 先說「該伸展囉」再顯示動作
        public bool expandSteps { get; set; }        // 一開始就展開步驟
        public bool peekNudge { get; set; }          // 沒理它 20 秒，從泡泡上方探頭
        public string entrance { get; set; }         // drop：掛麵條垂降；none：直接出現
        public double petScale { get; set; }         // 桌寵大小（1 = 預設）
        public double walkSpeed { get; set; }        // 走路速度（每秒幾個 DIP）
        public bool holdDuringCalls { get; set; }    // 麥克風或鏡頭使用中就不提醒
        public bool hideInFullscreen { get; set; }   // 全螢幕看影片、簡報時躲起來
        public bool quietHours { get; set; }         // 安靜時段開關
        public string quietStart { get; set; }
        public string quietEnd { get; set; }
        public bool claudeHook { get; set; }         // Claude Code 跑長任務時提早叫你
        public int claudePort { get; set; }
        public int claudeDelaySeconds { get; set; }  // Claude 連續忙幾秒才算長任務
        public int claudeEarlyMinutes { get; set; }  // 至少坐了幾分鐘才提早叫
        public bool water { get; set; }              // 喝水提醒
        public int waterMinutes { get; set; }        // 累計用電腦多久提醒喝一次水
        public List<Reminder> reminders { get; set; } // 自訂提醒（到時間桌寵說一句話）

        public static readonly string[] Characters = { "rigatoni", "spaghetti", "penne", "lasagna", "sprout" };
        public const int MaxReminders = 50;

        public Config()
        {
            intervalMinutes = 30; breakResetMinutes = 5; snoozeMinutes = 10; bubbleSeconds = 60;
            character = "rigatoni"; focus = "all"; exercises = new List<string>();
            announce = false; expandSteps = false; peekNudge = true; entrance = "drop";
            petScale = 1; walkSpeed = 40;
            holdDuringCalls = true; hideInFullscreen = true;
            quietHours = false; quietStart = "22:00"; quietEnd = "07:00";
            claudeHook = true; claudePort = 47615; claudeDelaySeconds = 15; claudeEarlyMinutes = 20;
            water = true; waterMinutes = 60; reminders = new List<Reminder>();
        }

        public void Clamp()
        {
            intervalMinutes = Fit(intervalMinutes, 5, 240);
            breakResetMinutes = Fit(breakResetMinutes, 1, 60);
            snoozeMinutes = Fit(snoozeMinutes, 1, 120);
            bubbleSeconds = Fit(bubbleSeconds, 10, 600);
            petScale = Math.Round(Math.Max(0.6, Math.Min(2.5, petScale)), 2);
            walkSpeed = Math.Max(10, Math.Min(150, walkSpeed));
            claudePort = Fit(claudePort, 1024, 65535);
            claudeDelaySeconds = Fit(claudeDelaySeconds, 3, 600);
            claudeEarlyMinutes = Fit(claudeEarlyMinutes, 0, intervalMinutes);
            waterMinutes = Fit(waterMinutes, 15, 240);
            var seen = new HashSet<string>();
            reminders = (reminders ?? new List<Reminder>()).Where(r => r != null && r.Normalize()).Take(MaxReminders).ToList();
            foreach (var r in reminders) if (!seen.Add(r.id)) r.id = Reminder.NewId();   // 手動複製貼上的同 id 換一個
            if (Array.IndexOf(Characters, character) < 0) character = "rigatoni";
            if (focus != "neck" && focus != "hips" && focus != "custom") focus = "all";
            if (entrance != "none") entrance = "drop";
            if (exercises == null) exercises = new List<string>();
            exercises = exercises.Where(x => x != null).Distinct().ToList();
            TimeSpan t;
            if (quietStart == null || !TimeSpan.TryParse(quietStart, CultureInfo.InvariantCulture, out t)) quietStart = "22:00";
            if (quietEnd == null || !TimeSpan.TryParse(quietEnd, CultureInfo.InvariantCulture, out t)) quietEnd = "07:00";
        }

        static int Fit(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }
    }

    // 自訂提醒：到時間桌寵出來說一句話（不做動作）
    public class Reminder
    {
        public string id { get; set; }         // 自動產生；用來記「上次提醒到哪一次」
        public string text { get; set; }       // 要說的話
        public string time { get; set; }       // HH:mm（24 小時制）
        public string repeat { get; set; }     // daily 每天／weekdays 平日／days 自選星期／once 單次
        public List<int> days { get; set; }    // repeat = days：1 = 週一 … 7 = 週日
        public string date { get; set; }       // repeat = once：yyyy-MM-dd
        public bool enabled { get; set; }

        public const int MaxText = 60;
        public static readonly string[] DayNames = { "一", "二", "三", "四", "五", "六", "日" };

        public Reminder() { id = NewId(); text = ""; time = "09:00"; repeat = "daily"; days = new List<int>(); date = ""; enabled = true; }

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        public Reminder Clone()
        {
            return new Reminder { id = id, text = text, time = time, repeat = repeat, days = (days ?? new List<int>()).ToList(), date = date, enabled = enabled };
        }

        // 整理欄位；回傳這筆能不能用（內容空白、時間或日期格式不對就不能用）
        public bool Normalize()
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 40) id = NewId();
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return false;
            if (text.Length > MaxText) text = text.Substring(0, MaxText);
            TimeSpan t;
            if (!TryTime(time, out t)) return false;
            time = t.ToString(@"hh\:mm");
            days = (days ?? new List<int>()).Where(d => d >= 1 && d <= 7).Distinct().OrderBy(d => d).ToList();
            if (repeat == "once")
            {
                DateTime d;
                if (!TryDate(date, out d)) return false;
                date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                if (repeat != "weekdays" && repeat != "days") repeat = "daily";
                if (repeat == "days" && days.Count == 0) return false;
                date = "";
            }
            if (repeat != "days") days = new List<int>();
            return true;
        }

        public static Reminder From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new Reminder();
            object v;
            try
            {
                if (d.TryGetValue("id", out v)) r.id = v as string;
                if (d.TryGetValue("text", out v)) r.text = v as string;
                if (d.TryGetValue("time", out v)) r.time = v as string;
                if (d.TryGetValue("repeat", out v)) r.repeat = v as string;
                if (d.TryGetValue("date", out v)) r.date = v as string;
                if (d.TryGetValue("enabled", out v)) { if (!(v is bool)) return null; r.enabled = (bool)v; }
                if (d.TryGetValue("days", out v) && v != null)
                {
                    if (!(v is IEnumerable) || v is string) return null;
                    r.days = ((IEnumerable)v).Cast<object>().Select(x => Convert.ToInt32(x, CultureInfo.InvariantCulture)).ToList();
                }
            }
            catch { return null; }
            return r.Normalize() ? r : null;
        }

        public static bool TryTime(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            return s != null && TimeSpan.TryParseExact(s.Trim().Replace("：", ":"), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out t)
                && t < TimeSpan.FromDays(1);
        }

        public static bool TryDate(string s, out DateTime d)
        {
            return DateTime.TryParseExact((s ?? "").Trim().Replace('/', '-'), new[] { "yyyy-M-d", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        public static int Weekday(DateTime day) { return ((int)day.DayOfWeek + 6) % 7 + 1; }   // 週一 = 1 … 週日 = 7

        bool On(DateTime day)
        {
            int w = Weekday(day);
            if (repeat == "weekdays") return w <= 5;
            if (repeat == "days") return days.Contains(w);
            if (repeat == "once") return date == day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        // 最近一次「該提醒」的時間（不晚於 now）；最多往回找 12 小時，電腦睡著錯過的也能補提醒
        public DateTime? Last(DateTime now)
        {
            TimeSpan t;
            if (!enabled || !TryTime(time, out t)) return null;
            for (int back = 0; back <= 1; back++)
            {
                var day = now.Date.AddDays(-back);
                var at = day + t;
                if (at <= now && now - at <= TimeSpan.FromHours(12) && On(day)) return at;
            }
            return null;
        }

        // 下一次提醒（設定頁顯示用）
        public DateTime? Next(DateTime now)
        {
            TimeSpan t;
            if (!TryTime(time, out t)) return null;
            if (repeat == "once")
            {
                DateTime d;
                if (!TryDate(date, out d) || d.Date + t <= now) return null;
                return d.Date + t;
            }
            for (int i = 0; i <= 7; i++)
            {
                var day = now.Date.AddDays(i);
                if (day + t > now && On(day)) return day + t;
            }
            return null;
        }

        public string RepeatText()
        {
            if (repeat == "weekdays") return "平日";
            if (repeat == "days") return days.Count == 7 ? "每天" : "每週" + string.Join("、", days.Select(d => DayNames[d - 1]));
            if (repeat == "once")
            {
                DateTime d;
                return TryDate(date, out d) ? d.ToString("M/d", CultureInfo.InvariantCulture) + "（週" + DayNames[Weekday(d) - 1] + "）單次" : "單次";
            }
            return "每天";
        }
    }

    // state.json：程式自己記的狀態
    public class PetState
    {
        public string mode { get; set; }         // fixed：固定位置；walk：在工作列走動
        public bool hasPos { get; set; }
        public double x { get; set; }            // 桌寵中心 X（DIP）
        public double y { get; set; }            // 桌寵視窗底邊 Y（DIP）
        public int rotation { get; set; }
        public string pausedUntil { get; set; }  // ISO 8601；空字串代表沒暫停
        public bool hidden { get; set; }
        public bool introDone { get; set; }
        public Dictionary<string, string> reminderLast { get; set; }    // 自訂提醒：上次提醒到哪一次（id → ISO 時間）
        public Dictionary<string, string> eventsReminded { get; set; }  // 行事曆：提醒過的活動（key → 開始時間 ISO）
        public PetState()
        {
            mode = "fixed"; pausedUntil = "";
            reminderLast = new Dictionary<string, string>();
            eventsReminded = new Dictionary<string, string>();
        }
    }

    public class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".noodle-pet");
        public static readonly string ConfigFile = Path.Combine(Dir, "config.json");
        static readonly string StateFile = Path.Combine(Dir, "state.json");
        static readonly string LogFile = Path.Combine(Dir, "log.jsonl");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public Config Config = new Config();
        public PetState State = new PetState();
        public string ConfigError;               // 上次讀設定檔的問題（null = 沒問題）
        DateTime configStamp = DateTime.MinValue;

        public static Store Load()
        {
            Directory.CreateDirectory(Dir);
            var s = new Store();
            try { if (File.Exists(StateFile)) s.State = Json.Deserialize<PetState>(File.ReadAllText(StateFile, Encoding.UTF8)) ?? new PetState(); }
            catch { s.State = new PetState(); }
            if (s.State.mode != "walk") s.State.mode = "fixed";
            if (s.State.pausedUntil == null) s.State.pausedUntil = "";
            if (s.State.reminderLast == null) s.State.reminderLast = new Dictionary<string, string>();
            if (s.State.eventsReminded == null) s.State.eventsReminded = new Dictionary<string, string>();
            s.ReloadConfig(true);
            return s;
        }

        // 設定檔有變才重讀，回傳有沒有重讀
        public bool ReloadConfig(bool force)
        {
            if (!File.Exists(ConfigFile)) { Config = new Config(); SaveConfig(); return true; }
            var stamp = File.GetLastWriteTimeUtc(ConfigFile);
            if (!force && stamp == configStamp) return false;
            configStamp = stamp;
            string text;
            try { text = File.ReadAllText(ConfigFile, Encoding.UTF8); }
            catch (Exception e) { ConfigError = e.Message; return false; }   // 別的程式正在存檔：下一輪再讀
            Config = Parse(text, out ConfigError);
            return true;
        }

        // 一個欄位一個欄位讀：某欄型別錯了只有那欄用預設值，不會連累其他設定
        static Config Parse(string text, out string error)
        {
            var c = new Config();
            var bad = new List<string>();
            int skipped = 0;
            Dictionary<string, object> d = null;
            try { d = Json.DeserializeObject(text) as Dictionary<string, object>; }
            catch (Exception e) { error = "格式錯誤，整份改用預設值（" + e.Message + "）"; c.Clamp(); return c; }
            if (d == null) { error = "內容不是 JSON 物件，改用預設值"; c.Clamp(); return c; }
            foreach (var p in typeof(Config).GetProperties())
            {
                object v;
                if (!d.TryGetValue(p.Name, out v)) continue;
                try
                {
                    if (p.PropertyType == typeof(List<string>))
                    {
                        var list = v as IEnumerable;
                        if (list == null || v is string) throw new FormatException();
                        p.SetValue(c, list.Cast<object>().Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToList(), null);
                    }
                    else if (p.PropertyType == typeof(List<Reminder>))
                    {
                        var list = v as IEnumerable;
                        if (list == null || v is string) throw new FormatException();
                        var rs = new List<Reminder>();
                        foreach (var o in list)
                        {
                            var r = Reminder.From(o as Dictionary<string, object>);
                            if (r != null) rs.Add(r); else skipped++;   // 壞掉的那一筆略過，其他照用
                        }
                        p.SetValue(c, rs, null);
                    }
                    else if (p.PropertyType == typeof(string))
                    {
                        if (!(v is string)) throw new FormatException();
                        p.SetValue(c, v, null);
                    }
                    else if (p.PropertyType == typeof(bool))
                    {
                        if (!(v is bool)) throw new FormatException();
                        p.SetValue(c, v, null);
                    }
                    else p.SetValue(c, Convert.ChangeType(v, p.PropertyType, CultureInfo.InvariantCulture), null);
                }
                catch { bad.Add(p.Name); }
            }
            c.Clamp();
            var problems = new List<string>();
            if (bad.Count > 0) problems.Add("這幾個欄位格式不對，先用預設值：" + string.Join("、", bad));
            if (skipped > 0) problems.Add("自訂提醒有 " + skipped + " 筆格式不對，先略過");
            error = problems.Count == 0 ? null : string.Join("；", problems);
            return c;
        }

        // 一行一個設定，方便手動修改
        public void SaveConfig()
        {
            Config.Clamp();
            var props = typeof(Config).GetProperties();
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < props.Length; i++)
            {
                var value = props[i].GetValue(Config, null);
                var rs = value as List<Reminder>;
                sb.Append("  \"").Append(props[i].Name).Append("\": ");
                if (rs != null && rs.Count > 0)   // 自訂提醒一筆一行
                    sb.Append("[\n").Append(string.Join(",\n", rs.Select(r => "    " + Json.Serialize(r)))).Append("\n  ]");
                else sb.Append(Json.Serialize(value));
                sb.Append(i < props.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("}\n");
            try
            {
                File.WriteAllText(ConfigFile, sb.ToString(), Utf8);
                configStamp = File.GetLastWriteTimeUtc(ConfigFile);
                ConfigError = null;
            }
            catch (Exception e) { LogError(e); }
        }

        public void SaveState()
        {
            try { File.WriteAllText(StateFile, Json.Serialize(State), Utf8); } catch (Exception e) { LogError(e); }
        }

        // log.jsonl：每次提醒一行，outcome = done／snoozed／ignored／away／stopped
        public void Log(string exercise, string outcome, string trigger)
        {
            var line = Json.Serialize(new Dictionary<string, object> {
                { "ts", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) },
                { "exercise", exercise }, { "outcome", outcome }, { "trigger", trigger } });
            try { File.AppendAllText(LogFile, line + "\n", Utf8); } catch (Exception e) { LogError(e); }
        }

        // 今天做完幾組伸展、喝了幾杯水（喝水也記在 log.jsonl，exercise = water）
        public int DoneToday() { return CountToday(false); }
        public int WaterToday() { return CountToday(true); }

        int CountToday(bool water)
        {
            var prefix = "{\"ts\":\"" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try
            {
                return File.Exists(LogFile) ? File.ReadLines(LogFile).Count(l => l.StartsWith(prefix) && l.Contains("\"outcome\":\"done\"")
                    && l.Contains("\"exercise\":\"water\"") == water) : 0;
            }
            catch { return 0; }
        }

        public static void LogError(Exception e)
        {
            if (e == null) return;
            try { File.AppendAllText(Path.Combine(Dir, "error.log"), DateTime.Now.ToString("s") + " " + e + "\n", Utf8); } catch { }
        }
    }

    // 編進 exe 的資源（精靈圖、動作資料、XAML）
    static class Res
    {
        public static Stream Open(string name)
        {
            var s = typeof(Res).Assembly.GetManifestResourceStream(name);
            if (s == null) throw new FileNotFoundException("缺少內嵌資源：" + name);
            return s;
        }

        public static bool Has(string name) { return typeof(Res).Assembly.GetManifestResourceInfo(name) != null; }

        public static T Json<T>(string name)
        {
            using (var r = new StreamReader(Open(name), Encoding.UTF8)) return new JavaScriptSerializer().Deserialize<T>(r.ReadToEnd());
        }
    }
}
