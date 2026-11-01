using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NoodlePet
{
    public class Exercise
    {
        public string id { get; set; }
        public string name { get; set; }
        public string instruction { get; set; }
        public string spec { get; set; }
        public int seconds { get; set; }
        public List<string> steps { get; set; }
        public string tip { get; set; }
    }

    public enum Phase { Idle, Intro, Nudge, Stretch, Water, Say }

    // 桌寵要說的話：自訂提醒、行事曆活動（不做動作，只有對話泡泡）
    public class Say
    {
        public string Key, Kind, Title, Place;   // Kind：custom／event／demo
        public DateTime Due;                     // 什麼時候說（「晚點」會往後延）
        public DateTime At;                      // 自訂提醒原定的時間／活動開始時間
        public DateTime End;                     // 活動結束時間
        public bool IsEvent;

        public static Say Custom(Reminder r, DateTime at)
        {
            return new Say { Key = "r|" + r.id + "|" + at.Ticks, Kind = "custom", Title = r.text, Due = at, At = at };
        }

        public static Say Event(CalEvent e)
        {
            return new Say { Key = "e|" + e.Key, Kind = "event", IsEvent = true, Title = string.IsNullOrEmpty(e.Title) ? "（沒有標題的活動）" : e.Title,
                Place = e.Location, Due = DateTime.Now, At = e.Start, End = e.End };
        }

        public string Eyebrow { get { return IsEvent ? "行事曆" : "提醒"; } }

        // 說的當下才算「幾分鐘後開始」「晚了多久」
        public string When(DateTime now)
        {
            if (IsEvent)
            {
                double m = (At - now).TotalMinutes;
                if (m >= 0.5) return Math.Ceiling(m) + " 分鐘後開始";
                if (m > -1) return "現在開始";
                return "已經開始 " + Math.Floor(-m) + " 分鐘";
            }
            return now - At > TimeSpan.FromMinutes(2) ? "原定 " + At.ToString("HH:mm") : At.ToString("HH:mm");
        }

        public string Detail()
        {
            if (!IsEvent) return null;
            var span = At.ToString("HH:mm") + "–" + End.ToString("HH:mm");
            return string.IsNullOrEmpty(Place) ? span : span + " · " + Place;
        }
    }

    // 提醒邏輯：累計連續使用時間、決定什麼時候叫你、記錄結果；桌寵、泡泡、系統匣都由這裡指揮
    public class Brain
    {
        public static readonly string[] FocusNeck = { "chin-tuck", "neck-side", "scap", "doorway", "t-ext", "walk" };
        public static readonly string[] FocusHips = { "hip-flexor", "calf-raise", "walk", "t-ext" };

        public readonly Store Store;
        public readonly List<Exercise> All;
        public Phase Phase = Phase.Idle;
        public Exercise Current;
        public double ActiveSeconds;
        public double WaterSeconds;   // 上次喝水後累計用電腦的時間（離開座位不歸零：離開不代表喝過水）
        public int DoneToday, WaterToday;
        public readonly GoogleCalendar Calendar = new GoogleCalendar();
        public PetWindow Pet;
        public BubbleWindow Bubble;
        // 示範選單（給錄影、試看用）：倒數秒數、計時縮成 5 秒、3 秒就探頭；示範不記錄、不影響輪替與計時
        public int DemoCountdown;
        public bool DemoQuickTimer, DemoQuickPeek;

        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        readonly Dictionary<string, DateTime> claudeBusy = new Dictionary<string, DateTime>();
        readonly List<Say> sayQueue = new List<Say>();
        Say saying;
        Tray tray;
        SettingsWindow settings;
        HookServer hooks;
        int hookPort;
        string trigger = "timer";
        bool demo, fullscreen;
        DateTime lastTick = DateTime.Now, today = DateTime.Today, nextAllowed = DateTime.MinValue;
        int ticks;

        public Brain(Store store)
        {
            Store = store;
            All = Res.Json<List<Exercise>>("exercises.json");
            DoneToday = store.DoneToday();
            WaterToday = store.WaterToday();
            timer.Tick += (s, e) => Tick();
            Calendar.Changed += () => { if (settings != null) settings.CalendarChanged(); };
        }

        public Config Cfg { get { return Store.Config; } }
        public bool Walking { get { return Store.State.mode == "walk"; } }
        public bool Hidden { get { return Store.State.hidden; } }
        public bool Fullscreen { get { return fullscreen; } }
        public bool IsPaused { get { DateTime u; return PausedUntil(out u); } }
        public Character CharacterInfo { get { return Character.Get(Cfg.character); } }
        public bool CanWander { get { return Phase == Phase.Idle && Bubble.Kind == null; } }
        public double PeekAfter { get { return demo && DemoQuickPeek ? 3 : 20; } }
        public string HookError { get; private set; }

        public void Start(Tray t)
        {
            tray = t;
            Pet = new PetWindow(this);
            Bubble = new BubbleWindow(this, Pet);
            Pet.Moved += () => Bubble.Follow();
            Bubble.BubbleClosed += () => { if (Phase == Phase.Idle) Pet.SetPose("idle"); };
            Pet.Init();
            fullscreen = Cfg.hideInFullscreen && Native.FullscreenBusy();
            if (!Hidden && !fullscreen) Pet.Appear(true, AfterArrival);
            RestartHooks();
            timer.Start();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            tray.Refresh();
            if (Store.ConfigError != null) tray.Warn("設定檔有問題：" + Store.ConfigError);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Pet.Dispatcher.BeginInvoke(new Action(() => { Pet.OnDisplayChanged(); Bubble.Follow(); }));
        }

        void AfterArrival()
        {
            if (!Store.State.introDone && Phase == Phase.Idle) StartIntro();
        }

        public void Shutdown()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (Pet != null) Pet.SavePos();
            Store.SaveState();
            if (hooks != null) hooks.Stop();
        }

        void Tick()
        {
            var now = DateTime.Now;
            double dt = (now - lastTick).TotalSeconds;
            lastTick = now;
            if (now.Date != today) { today = now.Date; DoneToday = Store.DoneToday(); WaterToday = Store.WaterToday(); }
            if (Store.ReloadConfig(false))
            {
                ApplyConfig();
                if (settings != null) settings.Reload();
                if (Store.ConfigError != null) tray.Warn("設定檔有問題：" + Store.ConfigError);
            }
            Calendar.Reload(false);   // 連動設定檔在 Dropbox：別台電腦改了也會同步過來

            bool fs = Cfg.hideInFullscreen && Native.FullscreenBusy();
            if (fs != fullscreen) { fullscreen = fs; ApplyVisibility(false); }

            // 電腦睡過一覺、或離開鍵盤滑鼠夠久，就當作休息過了（喝水不歸零）
            if (dt > 120 || Native.IdleSeconds() >= Cfg.breakResetMinutes * 60)
            {
                ActiveSeconds = 0;
                if (Phase == Phase.Nudge && !demo) Finish("away");
                else if (Phase == Phase.Water && !demo) FinishWater("away");
                if (dt > 120) Calendar.SyncNow();
            }
            else
            {
                if (Phase != Phase.Stretch) ActiveSeconds += dt;
                if (Phase != Phase.Water) WaterSeconds += dt;
            }

            foreach (var sid in claudeBusy.Where(p => (now - p.Value).TotalHours > 1).Select(p => p.Key).ToList()) claudeBusy.Remove(sid);
            Bubble.KeepOnTop();
            if (++ticks % 5 == 0) tray.Refresh();
            Calendar.Tick(now);
            QueueReminders(now);
            if (SpeakNext(now)) return;

            if (Phase != Phase.Idle || Bubble.Kind == "done" || Bubble.Kind == "bye") return;
            if (Hidden || fullscreen || IsPaused || InQuietHours(now) || now < nextAllowed) return;
            bool due = ActiveSeconds >= Cfg.intervalMinutes * 60;
            bool claudeDue = Cfg.claudeHook && ClaudeBusySeconds(now) >= Cfg.claudeDelaySeconds
                && ActiveSeconds >= Cfg.claudeEarlyMinutes * 60;
            bool waterDue = Cfg.water && WaterSeconds >= Cfg.waterMinutes * 60;
            if (!due && !claudeDue && !waterDue) return;
            if (Cfg.holdDuringCalls && Native.MicOrCameraInUse()) return;   // 通話中，下一輪再看
            if (InMeeting(now) != null) return;                               // 行事曆上正在開會，結束再叫
            if (due || claudeDue) Nudge(due ? "timer" : "claude", null, false);
            else WaterNudge(false);
        }

        // 行事曆上正在開會（設定要「開會時暫停」才算）
        public CalEvent InMeeting(DateTime now)
        {
            return Calendar.Link.Linked && Calendar.Link.pauseDuringEvents ? Calendar.BusyNow(now) : null;
        }

        // 這次要輪替的動作：依「重點」挑，順序照 exercises.json
        public List<Exercise> Enabled()
        {
            IEnumerable<string> ids = null;
            if (Cfg.focus == "neck") ids = FocusNeck;
            else if (Cfg.focus == "hips") ids = FocusHips;
            else if (Cfg.focus == "custom") ids = Cfg.exercises;
            var list = ids == null ? All : All.Where(x => ids.Contains(x.id)).ToList();
            return list.Count > 0 ? list : All;
        }

        void Nudge(string why, string id, bool isDemo)
        {
            demo = isDemo;
            var list = Enabled();
            if (id != null) Current = All.FirstOrDefault(x => x.id == id) ?? list[0];
            else
            {
                int i = Math.Abs(Store.State.rotation) % list.Count;
                Current = list[i];
                if (!isDemo) { Store.State.rotation = (i + 1) % list.Count; Store.SaveState(); }
            }
            trigger = why;
            Phase = Phase.Nudge;
            Pet.SetPose("wave");
            Bubble.ShowNudge(Current, ReasonLine(why), list.IndexOf(Current) + 1, list.Count, Cfg.announce);
        }

        string ReasonLine(string why)
        {
            if (why == "claude") return "Claude 還在忙，正好動一下";
            if (why == "manual") return "現在就來一組";
            if (why == "demo") return "示範";
            return "已經坐了 " + Math.Max(1, (int)(ActiveSeconds / 60)) + " 分鐘";
        }

        public void StartStretch()
        {
            if (Phase != Phase.Nudge) return;
            Phase = Phase.Stretch;
            Pet.SetPose(Current.id);
            int seconds = demo && DemoQuickTimer ? 5 : Current.seconds;
            Bubble.ShowStretch(Current, seconds, demo ? 0 : DoneToday + 1);
            if (claudeBusy.Count == 0 && trigger == "claude") Bubble.ClaudeDone();
        }

        public void CompleteStretch()
        {
            if (Phase != Phase.Stretch) return;
            if (!demo)
            {
                Store.Log(Current.id, "done", trigger);
                DoneToday++;
                ActiveSeconds = 0;
                nextAllowed = DateTime.Now.AddMinutes(1);
            }
            Phase = Phase.Idle;
            Pet.SetPose("wave");
            Pet.Hop(1);
            Pet.Sprites.Release(Current.id);   // 示範動作的影格用完就放掉，省記憶體
            Bubble.ShowDone(demo ? 1 : DoneToday);
            tray.Refresh();
        }

        public void DoneShown()
        {
            demo = false;
            if (Bubble.Kind == "done") Bubble.Dismiss();
        }

        public void SkipStretch()
        {
            if (Phase != Phase.Stretch) return;
            if (!demo) { Store.Log(Current.id, "stopped", trigger); Later(); }
            Phase = Phase.Idle;
            Pet.Sprites.Release(Current.id);
            demo = false;
            Bubble.Dismiss();
        }

        public void Snooze()
        {
            if (Phase != Phase.Nudge) return;
            if (!demo) { Store.Log(Current.id, "snoozed", trigger); Later(); }
            Phase = Phase.Idle;
            demo = false;
            Pet.SetPose("wave");
            Bubble.ShowBye(Cfg.snoozeMinutes);
        }

        public void ByeShown()
        {
            if (Bubble.Kind == "bye") Bubble.Dismiss();
        }

        public void Ignore()
        {
            if (Phase != Phase.Nudge) return;
            if (!demo) { Store.Log(Current.id, "ignored", trigger); Later(); }
            Phase = Phase.Idle;
            demo = false;
            Bubble.Dismiss();
        }

        void Finish(string outcome)   // away：人不在，從頭算
        {
            Store.Log(Current.id, outcome, trigger);
            ActiveSeconds = 0;
            Phase = Phase.Idle;
            Bubble.Dismiss();
        }

        // 晚點／沒理／跳過：再坐 snoozeMinutes 分鐘才會再叫（Claude 提早叫也一樣要等）
        void Later()
        {
            ActiveSeconds = Math.Max(0, (Cfg.intervalMinutes - Cfg.snoozeMinutes) * 60);
            nextAllowed = DateTime.Now.AddMinutes(Cfg.snoozeMinutes);
        }

        // 現在做一組（id = null 就照輪替）；手動做的也算數
        public void NowPlease(string id)
        {
            if (Phase == Phase.Stretch) return;
            if (Hidden) SetHidden(false);
            if (Phase == Phase.Intro) FinishIntro();
            Nudge("manual", id, false);
            StartStretch();
        }

        // ---------- 喝水 ----------

        void WaterNudge(bool isDemo)
        {
            demo = isDemo;
            Phase = Phase.Water;
            Pet.SetPose("drink");
            Bubble.ShowWater(isDemo ? 0 : WaterToday + 1);
        }

        public void WaterDone()
        {
            if (Phase != Phase.Water) return;
            if (!demo)
            {
                Store.Log("water", "done", "timer");
                WaterToday++;
                WaterSeconds = 0;
                nextAllowed = DateTime.Now.AddMinutes(1);
            }
            Phase = Phase.Idle;
            Pet.SetPose("wave");
            Pet.Hop(1);
            Pet.Sprites.Release("drink");
            int n = demo ? 1 : WaterToday;
            Bubble.ShowDone("喝好了！", n <= 1 ? "今天第一杯水" : "今天第 " + n + " 杯水了");
            tray.Refresh();
        }

        public void WaterSnooze()
        {
            if (Phase != Phase.Water) return;
            if (!demo) { Store.Log("water", "snoozed", "timer"); WaterLater(); }
            Phase = Phase.Idle;
            demo = false;
            Pet.SetPose("wave");
            Pet.Sprites.Release("drink");
            Bubble.ShowBye(Cfg.snoozeMinutes);
        }

        public void WaterIgnore()
        {
            if (Phase != Phase.Water) return;
            if (!demo) { Store.Log("water", "ignored", "timer"); WaterLater(); }
            Phase = Phase.Idle;
            demo = false;
            Pet.Sprites.Release("drink");
            Bubble.Dismiss();
        }

        void FinishWater(string outcome)   // away：泡泡開著人就走了
        {
            Store.Log("water", outcome, "timer");
            WaterLater();
            Phase = Phase.Idle;
            Pet.Sprites.Release("drink");
            Bubble.Dismiss();
        }

        // 晚點／沒理：再用 snoozeMinutes 分鐘電腦才會再叫
        void WaterLater()
        {
            WaterSeconds = Math.Max(0, (Cfg.waterMinutes - Cfg.snoozeMinutes) * 60);
            nextAllowed = DateTime.Now.AddMinutes(Cfg.snoozeMinutes);
        }

        // ---------- 自訂提醒、行事曆：桌寵說話 ----------

        // 到時間的自訂提醒、快開始的活動，排進「要說的話」
        void QueueReminders(DateTime now)
        {
            var st = Store.State;
            bool dirty = false;
            foreach (var r in Cfg.reminders.ToList())
            {
                string last;
                if (!st.reminderLast.TryGetValue(r.id, out last))   // 沒看過的提醒（例如手動加進設定檔）：從現在開始算，不補之前的
                {
                    st.reminderLast[r.id] = Iso(now);
                    dirty = true;
                    continue;
                }
                var at = r.Last(now);
                if (at == null || at.Value <= ParseIso(last)) continue;
                st.reminderLast[r.id] = Iso(at.Value);
                dirty = true;
                Enqueue(Say.Custom(r, at.Value));
                if (r.repeat == "once")   // 單次的響過就關掉
                {
                    r.enabled = false;
                    Store.SaveConfig();
                    if (settings != null) settings.Reload();
                }
            }
            foreach (var id in st.reminderLast.Keys.Where(k => !Cfg.reminders.Any(r => r.id == k)).ToList()) { st.reminderLast.Remove(id); dirty = true; }
            foreach (var e in Calendar.Due(now))
            {
                if (st.eventsReminded.ContainsKey(e.Key)) continue;
                st.eventsReminded[e.Key] = Iso(e.Start);
                dirty = true;
                Enqueue(Say.Event(e));
            }
            foreach (var k in st.eventsReminded.Where(p => ParseIso(p.Value) < now.AddDays(-2)).Select(p => p.Key).ToList()) { st.eventsReminded.Remove(k); dirty = true; }
            if (dirty) Store.SaveState();
        }

        void Enqueue(Say s)
        {
            if ((saying != null && saying.Key == s.Key) || sayQueue.Any(x => x.Key == s.Key)) return;
            sayQueue.Add(s);
        }

        // 有話要說就先說：不受暫停、安靜時段影響；伸展做到一半就等做完。
        // 全螢幕（看影片、簡報）時先等，離開全螢幕桌寵回來再說（這時系統通知多半也被勿擾擋掉）；桌寵收起來才改用 Windows 通知
        bool SpeakNext(DateTime now)
        {
            if (Phase == Phase.Say) return true;
            var next = sayQueue.Where(s => s.Due <= now).OrderBy(s => s.Due).FirstOrDefault();
            if (next == null) return false;
            if (Hidden)
            {
                sayQueue.Remove(next);
                tray.Notify(next.Eyebrow + " · " + next.When(now), next.Title + (next.IsEvent ? "\n" + next.Detail() : ""));
                return false;
            }
            if (fullscreen || Phase == Phase.Stretch || Phase == Phase.Intro || Pet.Arriving) return false;
            if (Phase == Phase.Nudge || Phase == Phase.Water)   // 伸展、喝水的提醒先收起來，說完再叫
            {
                if (!demo) nextAllowed = now.AddMinutes(1);
                if (Phase == Phase.Water) Pet.Sprites.Release("drink");
                Phase = Phase.Idle;
                demo = false;
            }
            sayQueue.Remove(next);
            Speak(next, now);
            return true;
        }

        void Speak(Say s, DateTime now)
        {
            saying = s;
            Phase = Phase.Say;
            Pet.SetPose("wave");
            Bubble.ShowSay(s, now, sayQueue.Count(x => x.Due <= now));
        }

        public void SayDone()
        {
            if (Phase != Phase.Say) return;
            saying = null;
            Phase = Phase.Idle;
            Bubble.Dismiss();
        }

        public void SaySnooze()
        {
            if (Phase != Phase.Say) return;
            if (saying.Kind != "demo") { saying.Due = DateTime.Now.AddMinutes(Cfg.snoozeMinutes); sayQueue.Add(saying); }
            saying = null;
            Phase = Phase.Idle;
            Pet.SetPose("wave");
            Bubble.ShowBye(Cfg.snoozeMinutes);
        }

        // 設定視窗新增、修改、重新打開某個提醒：從現在開始算，不補之前錯過的
        public void ReminderEdited(Reminder r)
        {
            Store.State.reminderLast[r.id] = Iso(DateTime.Now);
            Store.SaveState();
        }

        static string Iso(DateTime t) { return t.ToString("o", CultureInfo.InvariantCulture); }

        static DateTime ParseIso(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t) ? t : DateTime.MinValue;
        }

        // ---------- 開場導覽 ----------

        public void StartIntro()
        {
            if (Hidden) SetHidden(false);
            if (Phase == Phase.Stretch) return;
            Phase = Phase.Intro;
            Pet.SetPose("wave");
            Bubble.ShowIntro(0);
        }

        public void IntroGo(int step)
        {
            if (Phase != Phase.Intro) return;
            if (step >= BubbleWindow.IntroCount) FinishIntro();
            else Bubble.ShowIntro(step);
        }

        public void FinishIntro()
        {
            if (Phase != Phase.Intro) return;
            Phase = Phase.Idle;
            Store.State.introDone = true;
            Store.SaveState();
            Bubble.Dismiss();
        }

        public void IntroTry()
        {
            FinishIntro();
            NowPlease(null);
        }

        // ---------- 點擊 ----------

        public void PetClicked()
        {
            if (Phase == Phase.Nudge) { if (Bubble.Kind == "nudge") StartStretch(); }
            else if (Phase == Phase.Stretch) Bubble.ToggleSteps();
            else if (Phase == Phase.Idle) { if (Bubble.Kind == "hello") Bubble.Dismiss(); else ShowHello(); }
        }

        public void ShowHello()
        {
            if (Hidden) { SetHidden(false); return; }
            if (Phase != Phase.Idle) return;
            Pet.SetPose("wave");
            Bubble.ShowHello(HelloLine(), TodayLine());
        }

        public string TodayLine()
        {
            return "今天完成 " + DoneToday + " 組" + (Cfg.water || WaterToday > 0 ? "、喝水 " + WaterToday + " 杯" : "");
        }

        public void ShowMenu() { tray.ShowMenu(); }

        string HelloLine()
        {
            var now = DateTime.Now;
            DateTime until;
            if (PausedUntil(out until)) return until.Date > now.Date ? "暫停中，明天再提醒你" : "暫停中，" + until.ToString("HH:mm") + " 再提醒你";
            if (InQuietHours(now)) return "安靜時段，先不提醒";
            if (Cfg.holdDuringCalls && Native.MicOrCameraInUse()) return "你在通話中，先不打擾";
            var meeting = InMeeting(now);
            if (meeting != null) return "行事曆上在開會，" + meeting.End.ToString("HH:mm") + " 結束再叫你";
            int left = MinutesLeft();
            return left <= 1 ? "快要叫你起來動一動了" : "再 " + left + " 分鐘叫你起來動一動";
        }

        int MinutesLeft() { return Math.Max(0, (int)Math.Ceiling((Cfg.intervalMinutes * 60 - ActiveSeconds) / 60)); }

        public string StatusLine()
        {
            string tail = IsPaused ? "暫停中" : Hidden ? "已收起" : "再 " + MinutesLeft() + " 分鐘";
            return TodayLine() + " · " + tail;
        }

        // ---------- 狀態切換 ----------

        bool PausedUntil(out DateTime until)
        {
            until = DateTime.MinValue;
            var s = Store.State.pausedUntil;
            return !string.IsNullOrEmpty(s)
                && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out until)
                && until > DateTime.Now;
        }

        public void PauseFor(TimeSpan? span)
        {
            var until = span.HasValue ? DateTime.Now + span.Value : DateTime.Today.AddDays(1);
            Store.State.pausedUntil = until.ToString("o", CultureInfo.InvariantCulture);
            Store.SaveState();
            Ignore();
            WaterIgnore();
            tray.Refresh();
        }

        public void Resume()
        {
            Store.State.pausedUntil = "";
            Store.SaveState();
            ActiveSeconds = Math.Min(ActiveSeconds, Cfg.intervalMinutes * 60 - 60);
            tray.Refresh();
        }

        public void SetHidden(bool hidden)
        {
            if (Store.State.hidden == hidden) return;
            Store.State.hidden = hidden;
            Store.SaveState();
            if (hidden)
            {
                if (Phase == Phase.Nudge) Ignore();
                else if (Phase == Phase.Stretch) SkipStretch();
                else if (Phase == Phase.Water) WaterIgnore();
                else if (Phase == Phase.Say) SayDone();   // 正在說的話已經看到了
                else if (Phase == Phase.Intro) { Phase = Phase.Idle; Bubble.Dismiss(); }
                else Bubble.Dismiss();
            }
            ApplyVisibility(!hidden);
            tray.Refresh();
            if (!hidden && !Store.State.introDone && Phase == Phase.Idle) StartIntro();
        }

        void ApplyVisibility(bool entrance)
        {
            bool show = !Hidden && !fullscreen;
            Pet.SetShown(show, entrance);
            Bubble.SetSuppressed(!show);
        }

        public void SetWalking(bool on)
        {
            Store.State.mode = on ? "walk" : "fixed";
            Store.SaveState();
            Pet.ApplyMode();
            Bubble.Follow();
            tray.Refresh();
            if (settings != null) settings.Reload();
        }

        // 設定視窗或手動改設定檔之後：套用到桌寵、Claude 連動、系統匣
        public void ApplyConfig()
        {
            var before = Pet.Sprites == null ? null : Pet.Sprites.Character;
            Pet.ApplyConfig();
            if (before != null && before != Cfg.character) Pet.Reenter();
            Bubble.Follow();
            RestartHooks();
            tray.Refresh();
        }

        public void SaveAndApply()
        {
            Store.SaveConfig();
            ApplyConfig();
        }

        public void SetFocus(string focus)
        {
            if (focus == "custom" && Cfg.focus != "custom") Cfg.exercises = Enabled().Select(x => x.id).ToList();
            Cfg.focus = focus;
            Store.SaveConfig();
            if (settings != null) settings.Reload();
        }

        public void OpenSettings(string section)
        {
            if (settings == null)
            {
                settings = new SettingsWindow(this);
                settings.Closed += (s, e) => settings = null;
                settings.Show();
            }
            if (settings.WindowState == System.Windows.WindowState.Minimized) settings.WindowState = System.Windows.WindowState.Normal;
            settings.Activate();
            if (section != null) settings.Go(section);
        }

        // ---------- 示範選單 ----------

        // 設定頁的「試試看」：用這則提醒的內容示範一次（不影響真正的提醒）
        public void DemoSay(string text)
        {
            sayText = text;
            Demo("say");
        }

        string sayText;

        public void Demo(string what)
        {
            Action go = () =>
            {
                if (Hidden) SetHidden(false);
                if (Phase == Phase.Stretch) SkipStretch();
                if (Phase == Phase.Nudge) { Phase = Phase.Idle; demo = false; Bubble.Dismiss(); }
                if (Phase == Phase.Water) { Phase = Phase.Idle; demo = false; Bubble.Dismiss(); }
                if (Phase == Phase.Say)   // 真的提醒放回去，示範完再說
                {
                    if (saying.Kind != "demo") { saying.Due = DateTime.Now; sayQueue.Add(saying); }
                    saying = null;
                    Phase = Phase.Idle;
                    Bubble.Dismiss();
                }
                if (Phase == Phase.Intro) { Phase = Phase.Idle; Bubble.Dismiss(); }
                var now = DateTime.Now;
                if (what == "water") WaterNudge(true);
                else if (what == "say")
                {
                    Speak(new Say { Key = "demo", Kind = "demo", Title = sayText ?? "三點了，記得回覆廠商的信！", Due = now, At = now }, now);
                    sayText = null;
                }
                else if (what == "event")
                    Speak(new Say { Key = "demo", Kind = "demo", IsEvent = true, Title = "產品週會", Place = "三樓會議室",
                        Due = now, At = now.AddMinutes(10), End = now.AddMinutes(70) }, now);
                else if (what == "intro") { Phase = Phase.Intro; Pet.SetPose("wave"); Bubble.ShowIntro(0); }
                else if (what == "drop") Pet.Reenter();
                else if (what == "peek") Nudge("demo", null, true);
                else if (what == "celebrate") { demo = true; Pet.SetPose("wave"); Pet.Hop(1); Bubble.ShowDone(DoneToday + 1); }
                else if (what == "claude")
                {
                    Nudge("demo", null, true);
                    StartStretch();
                    Bubble.ClaudeDone();
                }
                else Nudge("demo", what, true);
            };
            if (DemoCountdown <= 0) { go(); return; }
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DemoCountdown) };
            t.Tick += (s, e) => { t.Stop(); go(); };
            t.Start();
        }

        // ---------- 勿擾 ----------

        bool InQuietHours(DateTime now)
        {
            TimeSpan a, b;
            if (!Cfg.quietHours || !TimeSpan.TryParse(Cfg.quietStart, CultureInfo.InvariantCulture, out a)
                || !TimeSpan.TryParse(Cfg.quietEnd, CultureInfo.InvariantCulture, out b)) return false;
            var t = now.TimeOfDay;
            return a <= b ? (t >= a && t < b) : (t >= a || t < b);
        }

        // ---------- Claude Code 連動 ----------

        void RestartHooks()
        {
            if (hooks != null && hookPort == Cfg.claudePort) return;
            if (hooks != null) hooks.Stop();
            hookPort = Cfg.claudePort;
            hooks = new HookServer();
            var ui = Dispatcher.CurrentDispatcher;
            if (hooks.Start(hookPort, (path, sid) => ui.BeginInvoke(new Action(() => OnHook(path, sid)))))
                HookError = null;
            else
            {
                hooks = null;
                HookError = "本機連接埠 " + hookPort + " 被別的程式占用了";
                tray.Warn(HookError + "，Claude Code 連動先關閉（可以在設定裡換一個）");
            }
        }

        void OnHook(string path, string sid)
        {
            if (path == "hello") { ShowHello(); return; }
            if (!Cfg.claudeHook) return;
            if (path == "prompt") claudeBusy[sid] = DateTime.Now;
            else if (path == "stop")
            {
                claudeBusy.Remove(sid);
                if (claudeBusy.Count == 0) Bubble.ClaudeDone();
            }
        }

        double ClaudeBusySeconds(DateTime now)
        {
            return claudeBusy.Count == 0 ? 0 : (now - claudeBusy.Values.Min()).TotalSeconds;
        }
    }
}
