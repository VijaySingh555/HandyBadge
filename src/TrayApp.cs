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
        readonly ToolStripMenuItem miStream, miSOff, miSScreen, miSRegion, miSPresenter, miFps;
        readonly StreamSource source = new StreamSource();
        readonly PowerPointLink pp = new PowerPointLink();
        StreamMode mode = StreamMode.Off;
        int zoomIndex;
        byte[] lastPixels;
        DateTime lastSent = DateTime.MinValue, presenterStart = DateTime.Now;
        int frames, skipped;
        bool modeChanged;
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
            miSOff = new ToolStripMenuItem("Off (swipe mode)", null, (a, b) => SetMode(StreamMode.Off));
            miSScreen = new ToolStripMenuItem("Screen (shutter tap zooms 1×, 2×, 4×)", null, (a, b) => SetMode(StreamMode.Screen));
            miSRegion = new ToolStripMenuItem("Region (shutter tap sends key)", null, (a, b) => SetMode(StreamMode.Region));
            miSPresenter = new ToolStripMenuItem("Presenter view (shutter tap next slide)", null, (a, b) => SetMode(StreamMode.Presenter));
            miFps = new ToolStripMenuItem("Frame rate");
            for (int f = 1; f <= 5; f++)
            {
                int fps = f;
                miFps.DropDownItems.Add(new ToolStripMenuItem(f + " fps", null, (a, b) => { s.StreamFps = fps; s.Save(); UpdateUi(); }) { Tag = fps });
            }
            miStream = new ToolStripMenuItem("Stream to badge");
            miStream.DropDownItems.AddRange(new ToolStripItem[] {
                miSOff, miSScreen, miSRegion, miSPresenter, new ToolStripSeparator(),
                new ToolStripMenuItem("Pick region…", null, (a, b) => PickRegion(false)),
                miFps,
                new ToolStripMenuItem("Reset presenter timer", null, (a, b) => { presenterStart = DateTime.Now; modeChanged = true; })
            });
            menu.Items.AddRange(new ToolStripItem[] {
                miStatus, new ToolStripSeparator(),
                miConnect, miCalibrate, miFlip, miStream, new ToolStripSeparator(),
                miPause, miSwap, miStartup, new ToolStripSeparator(),
                new ToolStripMenuItem("Open settings file", null, (a, b) => OpenFile(Settings.FilePath)),
                new ToolStripMenuItem("Reload settings", null, (a, b) => ReloadSettings()),
                new ToolStripMenuItem("Open log", null, (a, b) => OpenFile(Settings.LogPath)),
                new ToolStripMenuItem("About", null, (a, b) => About()),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (a, b) => Exit())
            });

            link.ControlEvent += OnBadgeControl;
            link.PreviewEnded += () => { mode = StreamMode.Off; last = 0; Balloon("The badge left live mode. Back to swipe mode.", ToolTipIcon.Info); UpdateUi(); };
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
                    if (mode != StreamMode.Off || link.Previewing)
                    {
                        if (!await StreamStep()) { fails++; if (fails >= 3) { link.Close(); SetStatus("Lost the badge. Reconnecting…"); } }
                        else fails = 0;
                        continue;
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

        // ---- Streaming ----
        async Task<bool> StreamStep()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (mode == StreamMode.Off)
                {
                    await link.StopPreviewAsync();
                    last = 0; // re-read the active image before detecting swipes again
                    SetStatus("Connected to " + link.Name);
                    return true;
                }
                if (!link.Previewing)
                {
                    await link.StartPreviewAsync();
                    lastPixels = null; frames = 0; skipped = 0;
                    Balloon(StreamHint(), ToolTipIcon.Info);
                    UpdateUi();
                }
                byte[] px = RenderFrame();
                bool due = (DateTime.Now - lastSent).TotalMilliseconds >= s.KeepAliveMs;
                if (modeChanged || due || lastPixels == null || !Same(px, lastPixels))
                {
                    modeChanged = false;
                    var jpeg = JpegEncoder.Encode(px, StreamSource.Size, StreamSource.Size, s.StreamQuality, true);
                    await link.SendFrameAsync(jpeg);
                    lastPixels = px; lastSent = DateTime.Now; frames++;
                    if (frames % 20 == 1) UpdateUi();
                }
                else skipped++;
            }
            catch (Exception e)
            {
                Log.Write("Stream step failed: " + e.Message);
                await Task.Delay(500);
                return false;
            }
            int wait = 1000 / Math.Max(1, s.StreamFps) - (int)sw.ElapsedMilliseconds;
            if (wait > 0) await Task.Delay(wait);
            return true;
        }

        byte[] RenderFrame()
        {
            switch (mode)
            {
                case StreamMode.Screen: source.RenderScreen(StreamSource.ZoomLevels[zoomIndex]); break;
                case StreamMode.Region: source.RenderRegion(s.StreamRegion); break;
                case StreamMode.Presenter: pp.Refresh(); source.RenderPresenter(pp, DateTime.Now - presenterStart); break;
            }
            return source.Pixels();
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        string StreamHint()
        {
            switch (mode)
            {
                case StreamMode.Screen: return "Streaming your screen. Tap the badge's shutter to zoom 1×, 2×, 4×.";
                case StreamMode.Region: return "Streaming your region. The shutter tap sends " + s.ShutterKey + ".";
                case StreamMode.Presenter: return "Presenter view on the badge. Tap the shutter for the next slide.";
                default: return "";
            }
        }

        void SetMode(StreamMode m)
        {
            if (m == StreamMode.Region && s.StreamRegion.IsEmpty && !PickRegion(true)) return;
            if (m != StreamMode.Off && !link.Connected) Balloon("Streaming starts when the badge connects.", ToolTipIcon.Info);
            if (m == StreamMode.Presenter && mode != StreamMode.Presenter) presenterStart = DateTime.Now;
            if (m == StreamMode.Screen && mode != StreamMode.Screen) zoomIndex = 0;
            bool wasStreaming = mode != StreamMode.Off;
            mode = m; modeChanged = true;
            if (wasStreaming && m != StreamMode.Off) Balloon(StreamHint(), ToolTipIcon.Info);
            Log.Write("Stream mode: " + m);
            UpdateUi();
        }

        bool PickRegion(bool forMode)
        {
            using (var f = new RegionPicker())
            {
                if (f.ShowDialog() != DialogResult.OK) return false;
                s.StreamRegion = f.Result; s.Save();
                Log.Write("Region set: " + f.Result);
            }
            if (!forMode && mode != StreamMode.Region) SetMode(StreamMode.Region);
            modeChanged = true;
            return true;
        }

        void OnBadgeControl(int action)
        {
            if (action != 2 || !link.Previewing) return; // 2 = shutter
            switch (mode)
            {
                case StreamMode.Screen:
                    zoomIndex = (zoomIndex + 1) % StreamSource.ZoomLevels.Length;
                    modeChanged = true;
                    Log.Write("Shutter: zoom " + StreamSource.ZoomLevels[zoomIndex] + "×");
                    break;
                case StreamMode.Region:
                    SendShutterKey();
                    break;
                case StreamMode.Presenter:
                    if (!pp.Next()) SendShutterKey();
                    modeChanged = true;
                    break;
            }
            UpdateUi();
        }

        void SendShutterKey()
        {
            if (s.Paused) return;
            var err = KeySender.Send(s.ShutterKey);
            Log.Write("Shutter: sent " + s.ShutterKey + (err.Length > 0 ? " FAILED: " + err : ""));
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
            if (!wantConnected) { mode = StreamMode.Off; link.Close(); SetStatus("Disconnected"); }
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

        async void Exit()
        {
            exiting = true; wantConnected = false;
            if (link.Previewing) await Task.WhenAny(link.StopPreviewAsync(), Task.Delay(1500));
            link.Close();
            source.Dispose();
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
            if (link.Connected && link.Previewing)
            {
                string what = mode == StreamMode.Screen ? "screen " + StreamSource.ZoomLevels[zoomIndex] + "×" : mode == StreamMode.Region ? "region" : "presenter view";
                t = link.Name + " · streaming " + what + "\n" + s.StreamFps + " fps cap · " + frames + " frames sent";
            }
            else if (link.Connected && last > 0)
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
            miSOff.Checked = mode == StreamMode.Off; miSScreen.Checked = mode == StreamMode.Screen;
            miSRegion.Checked = mode == StreamMode.Region; miSPresenter.Checked = mode == StreamMode.Presenter;
            miStream.Text = mode == StreamMode.Off ? "Stream to badge" : "Stream to badge (on)";
            foreach (ToolStripMenuItem f in miFps.DropDownItems) f.Checked = (int)f.Tag == s.StreamFps;
            miCalibrate.Enabled = link.Connected && mode == StreamMode.Off;
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
