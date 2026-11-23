using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;

namespace HandyBadgeRemote
{
    // Simple key=value settings in %APPDATA%\HandyBadgeRemote\settings.ini
    class Settings
    {
        public string BadgeName = "BG";      // name prefix to look for
        public ulong LastAddress;            // remembered for faster reconnects
        public string LeftKey = "Left";      // key sent on a left swipe
        public string RightKey = "Right";    // key sent on a right swipe
        public int PollMs = 500;
        public bool SwapKeys;
        public bool Paused;
        public List<int> Ring = new List<int>(); // footprints in swipe-right order
        public bool Calibrated;
        // Streaming (v0.2)
        public int StreamFps = 3;            // 1-5 frames per second
        public int StreamQuality = 40;       // JPEG quality 10-90
        public int KeepAliveMs = 2000;       // resend the frame this often when nothing changes
        public string ShutterKey = "Right";  // key the shutter tap sends in Region mode (and Presenter without PowerPoint)
        public Rectangle StreamRegion = Rectangle.Empty;

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HandyBadgeRemote"); }
        }
        public static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }
        public static string LogPath { get { return Path.Combine(Folder, "log.txt"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) { s.Save(); return s; }
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";")) continue;
                    int eq = t.IndexOf('=');
                    if (eq < 1) continue;
                    var k = t.Substring(0, eq).Trim().ToLowerInvariant();
                    var v = t.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "badgename": s.BadgeName = v; break;
                        case "lastaddress": ulong a; if (ulong.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) s.LastAddress = a; break;
                        case "leftkey": s.LeftKey = v; break;
                        case "rightkey": s.RightKey = v; break;
                        case "pollms": int p; if (int.TryParse(v, out p)) s.PollMs = Math.Max(200, Math.Min(5000, p)); break;
                        case "swapkeys": s.SwapKeys = IsTrue(v); break;
                        case "paused": s.Paused = IsTrue(v); break;
                        case "calibrated": s.Calibrated = IsTrue(v); break;
                        case "streamfps": int f; if (int.TryParse(v, out f)) s.StreamFps = Math.Max(1, Math.Min(5, f)); break;
                        case "streamquality": int q; if (int.TryParse(v, out q)) s.StreamQuality = Math.Max(10, Math.Min(90, q)); break;
                        case "keepalivems": int ka; if (int.TryParse(v, out ka)) s.KeepAliveMs = Math.Max(500, Math.Min(30000, ka)); break;
                        case "shutterkey": s.ShutterKey = v; break;
                        case "streamregion":
                            var parts = v.Split(',');
                            int rx, ry, rs;
                            if (parts.Length == 3 && int.TryParse(parts[0], out rx) && int.TryParse(parts[1], out ry) && int.TryParse(parts[2], out rs) && rs >= 40)
                                s.StreamRegion = new Rectangle(rx, ry, rs, rs);
                            break;
                        case "ring":
                            s.Ring = v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(x => { int n; return int.TryParse(x.Trim(), out n) ? n : 0; })
                                .Where(n => n > 0).ToList();
                            break;
                    }
                }
            }
            catch (Exception e) { Log.Write("Settings load failed: " + e.Message); }
            if (s.Ring.Count < 3) { s.Ring.Clear(); s.Calibrated = false; }
            return s;
        }

        static bool IsTrue(string v) { v = v.ToLowerInvariant(); return v == "1" || v == "true" || v == "yes" || v == "on"; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                sb.AppendLine("# HandyBadge Remote settings. Edit while the app is closed, or use Reload settings in the tray menu.");
                sb.AppendLine("# Key names: Left, Right, Up, Down, PageUp, PageDown, Space, Enter, Home, End, B, W, F5, None");
                sb.AppendLine("BadgeName=" + BadgeName);
                sb.AppendLine("LeftKey=" + LeftKey);
                sb.AppendLine("RightKey=" + RightKey);
                sb.AppendLine("SwapKeys=" + (SwapKeys ? "true" : "false"));
                sb.AppendLine("Paused=" + (Paused ? "true" : "false"));
                sb.AppendLine("# How often to check the badge, in milliseconds (200-5000)");
                sb.AppendLine("PollMs=" + PollMs);
                sb.AppendLine("# Streaming: frames per second (1-5), JPEG quality (10-90), keep-alive in ms, and the key the shutter tap sends in Region mode");
                sb.AppendLine("StreamFps=" + StreamFps);
                sb.AppendLine("StreamQuality=" + StreamQuality);
                sb.AppendLine("KeepAliveMs=" + KeepAliveMs);
                sb.AppendLine("ShutterKey=" + ShutterKey);
                sb.AppendLine("StreamRegion=" + (StreamRegion.IsEmpty ? "" : StreamRegion.X + "," + StreamRegion.Y + "," + StreamRegion.Width));
                sb.AppendLine("# Set by the app. Delete these lines to forget the badge and calibration.");
                sb.AppendLine("LastAddress=" + LastAddress.ToString("X12"));
                sb.AppendLine("Ring=" + string.Join(",", Ring));
                sb.AppendLine("Calibrated=" + (Calibrated ? "true" : "false"));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Log.Write("Settings save failed: " + e.Message); }
        }
    }
}
