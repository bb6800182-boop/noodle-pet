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

        public static readonly string[] Characters = { "rigatoni", "spaghetti", "penne", "lasagna", "sprout" };

        public Config()
        {
            intervalMinutes = 30; breakResetMinutes = 5; snoozeMinutes = 10; bubbleSeconds = 60;
            character = "rigatoni"; focus = "all"; exercises = new List<string>();
            announce = false; expandSteps = false; peekNudge = true; entrance = "drop";
            petScale = 1; walkSpeed = 40;
            holdDuringCalls = true; hideInFullscreen = true;
            quietHours = false; quietStart = "22:00"; quietEnd = "07:00";
            claudeHook = true; claudePort = 47615; claudeDelaySeconds = 15; claudeEarlyMinutes = 20;
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
        public PetState() { mode = "fixed"; pausedUntil = ""; }
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
            error = bad.Count == 0 ? null : "這幾個欄位格式不對，先用預設值：" + string.Join("、", bad);
            return c;
        }

        // 一行一個設定，方便手動修改
        public void SaveConfig()
        {
            Config.Clamp();
            var props = typeof(Config).GetProperties();
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < props.Length; i++)
                sb.Append("  \"").Append(props[i].Name).Append("\": ")
                  .Append(Json.Serialize(props[i].GetValue(Config, null)))
                  .Append(i < props.Length - 1 ? ",\n" : "\n");
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

        public int DoneToday()
        {
            var prefix = "{\"ts\":\"" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try { return File.Exists(LogFile) ? File.ReadLines(LogFile).Count(l => l.StartsWith(prefix) && l.Contains("\"outcome\":\"done\"")) : 0; }
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
