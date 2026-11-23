using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HandyBadgeRemote
{
    // System tray app: keeps a link to the badge, watches the active image's footprint and turns swipes into key presses.
    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "HandyBadgeRemote";

        readonly NotifyIcon tray;
        readonly ToolStripMenuItem miStatus, miConnect, miCalibrate, miFlip, miPause, miSwap, miStartup;
        readonly BadgeLink link = new BadgeLink();
        readonly Detector det = new Detector();
        Settings s;
        bool wantConnected = true, exiting, loopRunning;
        int last, swipes;
        string lastSwipe = "";

        public TrayApp()
        {
            s = Settings.Load();
            det.Ring = s.Ring; det.Calibrated = s.Calibrated;

            var menu = new ContextMenuStrip();
            miStatus = new ToolStripMenuItem("Starting…") { Enabled = false };
            miConnect = new ToolStripMenuItem("Disconnect", null, (a, b) => ToggleConnect());
            miCalibrate = new ToolStripMenuItem("Calibrate (swipe left twice)", null, (a, b) => { if (det.CalibrationActive) CancelCalibration(); else StartCalibration(); });
            miFlip = new ToolStripMenuItem("Flip direction", null, (a, b) => Flip());
            miPause = new ToolStripMenuItem("Pause keys", null, (a, b) => { s.Paused = !s.Paused; s.Save(); UpdateUi(); });
            miSwap = new ToolStripMenuItem("Swap left and right keys", null, (a, b) => { s.SwapKeys = !s.SwapKeys; s.Save(); UpdateUi(); });
            miStartup = new ToolStripMenuItem("Start with Windows", null, (a, b) => ToggleStartup());
            menu.Items.AddRange(new ToolStripItem[] {
                miStatus, new ToolStripSeparator(),
                miConnect, miCalibrate, miFlip, new ToolStripSeparator(),
                miPause, miSwap, miStartup, new ToolStripSeparator(),
                new ToolStripMenuItem("Open settings file", null, (a, b) => OpenFile(Settings.FilePath)),
                new ToolStripMenuItem("Reload settings", null, (a, b) => ReloadSettings()),
                new ToolStripMenuItem("Open log", null, (a, b) => OpenFile(Settings.LogPath)),
                new ToolStripMenuItem("About", null, (a, b) => About()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (a, b) => Exit())
            });

            tray = new NotifyIcon { Icon = LoadIcon(), ContextMenuStrip = menu, Visible = true, Text = AppInfo.Name };
            tray.DoubleClick += (a, b) => Balloon(StatusLine(), ToolTipIcon.Info);
            UpdateUi();
            RunLoop();
        }

        static Icon LoadIcon()
        {
            try
            {
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (st != null) return new Icon(st, SystemInformation.SmallIconSize);
            }
            catch { }
            return SystemIcons.Application;
        }

        // ---- Main loop: connect, then poll the active image's footprint ----
        async void RunLoop()
        {
            if (loopRunning) return;
            loopRunning = true;
            int fails = 0;
            try
            {
                while (!exiting)
                {
                    if (!wantConnected) { await Task.Delay(300); continue; }
                    if (!link.Connected)
                    {
                        last = 0;
                        if (!await ConnectOnce()) { await Task.Delay(5000); continue; }
                        fails = 0;
                    }
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        int v = await link.ReadActiveFootprintAsync(3000);
                        fails = 0;
                        if (v > 0) OnFootprint(v);
                    }
                    catch (Exception e)
                    {
                        fails++;
                        Log.Write("Poll failed (" + fails + "): " + e.Message);
                        if (fails >= 3) { link.Close(); SetStatus("Lost the badge. Reconnecting…"); continue; }
                    }
                    int wait = s.PollMs - (int)sw.ElapsedMilliseconds;
                    if (wait > 0) await Task.Delay(wait);
                }
            }
            finally { loopRunning = false; }
        }

        async Task<bool> ConnectOnce()
        {
            try
            {
                if (s.LastAddress != 0)
                {
                    SetStatus("Connecting to your badge…");
                    try { await link.ConnectAsync(s.LastAddress); }
                    catch (Exception e) { Log.Write("Remembered badge failed: " + e.Message); }
                }
                if (!link.Connected)
                {
                    SetStatus("Searching for a badge named " + s.BadgeName + "…");
                    ulong addr = await link.ScanAsync(s.BadgeName, 15000);
                    if (addr == 0) { SetStatus("No badge found. Is it on and disconnected from the phone?"); return false; }
                    await link.ConnectAsync(addr);
                }
                if (!wantConnected || exiting) { link.Close(); return false; }
                if (s.LastAddress != link.Address) { s.LastAddress = link.Address; s.Save(); }
                SetStatus("Connected to " + link.Name);
                Balloon("Connected to " + link.Name + (det.Calibrated ? "" : ". Calibrate from the tray menu: swipe left twice."), ToolTipIcon.Info);
                return true;
            }
            catch (Exception e)
            {
                Log.Write("Connect failed: " + e.Message);
                link.Close();
                SetStatus("Couldn't connect: " + e.Message);
                return false;
            }
        }

        void OnFootprint(int v)
        {
            if (last == 0)
            {
                if (det.CheckFirst(v)) { SaveRing(); Log.Write("Unknown image footprint, calibration cleared"); }
                last = v; UpdateUi(); return;
            }
            if (v == last) return;
            int from = last; last = v;
            var r = det.Transition(from, v);
            Log.Write("Footprint " + from + " -> " + v + ": " + r);
            switch (r)
            {
                case SwipeResult.Left: Swipe(true); break;
                case SwipeResult.Right: Swipe(false); break;
                case SwipeResult.Calibrating: Balloon("Good. Swipe left once more.", ToolTipIcon.Info); break;
                case SwipeResult.Calibrated: SaveRing(); Balloon("Calibrated. Swipes now send keys.", ToolTipIcon.Info); break;
                case SwipeResult.CalibrationFailed: Balloon("Calibration needs three different images. Try again.", ToolTipIcon.Warning); break;
                case SwipeResult.Learned: SaveRing(); Balloon("Found 3 images. Calibrate to set the direction: swipe left twice.", ToolTipIcon.Info); break;
                case SwipeResult.Relearning: SaveRing(); Balloon("Your images changed. Please calibrate again.", ToolTipIcon.Warning); break;
                default: lastSwipe = "swipe (direction unknown)"; break;
            }
            UpdateUi();
        }

        void Swipe(bool left)
        {
            swipes++;
            bool sendLeft = left ^ s.SwapKeys;
            string key = sendLeft ? s.LeftKey : s.RightKey;
            lastSwipe = (left ? "left" : "right") + (s.Paused ? " (paused)" : " → " + key);
            if (s.Paused) return;
            var err = KeySender.Send(key);
            Log.Write("Swipe " + (left ? "left" : "right") + ": sent " + key + (err.Length > 0 ? " FAILED: " + err : ""));
            if (err.Length > 0) Balloon("Couldn't send " + key + ": " + err, ToolTipIcon.Warning);
        }

        void SaveRing() { s.Ring = det.Ring; s.Calibrated = det.Calibrated; s.Save(); }

        // ---- Menu actions ----
        void StartCalibration()
        {
            if (!link.Connected) { Balloon("Connect to the badge first.", ToolTipIcon.Warning); return; }
            det.StartCalibration();
            Balloon("Calibrating: swipe LEFT twice, slowly.", ToolTipIcon.Info);
            UpdateUi();
        }

        void Flip()
        {
            if (det.Flip()) { SaveRing(); Balloon("Direction flipped.", ToolTipIcon.Info); }
            UpdateUi();
        }

        void ToggleConnect()
        {
            wantConnected = !wantConnected;
            if (!wantConnected) { link.Close(); SetStatus("Disconnected"); }
            else SetStatus("Connecting…");
            UpdateUi();
        }

        void ReloadSettings()
        {
            s = Settings.Load();
            det.Ring = s.Ring; det.Calibrated = s.Calibrated;
            Keys k;
            string bad = !KeySender.TryParse(s.LeftKey, out k) ? s.LeftKey : !KeySender.TryParse(s.RightKey, out k) ? s.RightKey : null;
            Balloon(bad == null ? "Settings reloaded." : "Unknown key name: " + bad, bad == null ? ToolTipIcon.Info : ToolTipIcon.Warning);
            UpdateUi();
        }

        bool StartupEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && (k.GetValue(RunName) as string) == Quoted(Application.ExecutablePath); }
            catch { return false; }
        }
        static string Quoted(string p) { return "\"" + p + "\""; }

        void ToggleStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (StartupEnabled()) k.DeleteValue(RunName, false);
                    else k.SetValue(RunName, Quoted(Application.ExecutablePath));
                }
            }
            catch (Exception e) { Balloon("Couldn't change start-up setting: " + e.Message, ToolTipIcon.Warning); }
            UpdateUi();
        }

        static void OpenFile(string path)
        {
            try { if (!File.Exists(path)) File.WriteAllText(path, ""); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show("Couldn't open " + path + "\n" + e.Message, AppInfo.Name); }
        }

        void About()
        {
            MessageBox.Show(
                AppInfo.Name + " v" + AppInfo.Version + " (" + AppInfo.BuildDate + ")\n\n" +
                "Turns swipes on your badge into key presses.\n\n" +
                "Settings: " + Settings.FilePath + "\nLog: " + Settings.LogPath,
                "About " + AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Exit()
        {
            exiting = true; wantConnected = false;
            link.Close();
            tray.Visible = false; tray.Dispose();
            Log.Write("Exit");
            ExitThread();
        }

        // ---- UI ----
        string status = "Starting…";
        void SetStatus(string text) { status = text; Log.Write("Status: " + text); UpdateUi(); }

        string StatusLine()
        {
            var t = status;
            if (link.Connected && last > 0)
            {
                int i = det.IndexOf(last);
                t = link.Name + " · " + (i >= 0 ? "image " + (i + 1) + " of " + det.Ring.Count : (last / 1024) + " KB");
                if (det.CalibrationActive) t += " · calibrating";
                else if (!det.Calibrated) t += " · not calibrated";
                if (lastSwipe.Length > 0) t += "\nLast: " + lastSwipe + " (" + swipes + " swipes)";
            }
            if (s.Paused) t += "\nKeys paused";
            return t;
        }

        void UpdateUi()
        {
            if (exiting) return;
            var line = StatusLine();
            miStatus.Text = line.Split('\n')[0];
            var tip = AppInfo.Name + "\n" + line;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;
            miConnect.Text = wantConnected ? "Disconnect" : "Connect";
            miCalibrate.Enabled = link.Connected;
            miCalibrate.Text = det.CalibrationActive ? "Calibrating… (click to cancel)" : "Calibrate (swipe left twice)";
            miFlip.Enabled = det.Calibrated;
            miPause.Checked = s.Paused;
            miSwap.Checked = s.SwapKeys;
            miStartup.Checked = StartupEnabled();
        }

        void CancelCalibration()
        {
            det.CancelCalibration();
            Balloon("Calibration cancelled.", ToolTipIcon.Info);
            UpdateUi();
        }

        void Balloon(string text, ToolTipIcon icon)
        {
            if (exiting) return;
            try { tray.ShowBalloonTip(3000, AppInfo.Name, text, icon); } catch { }
        }
    }
}
