using System;
using System.IO;

namespace HandyBadgeRemote
{
    // Plain-text log in %APPDATA%\HandyBadgeRemote\log.txt, rolled over at about 1 MB.
    static class Log
    {
        static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    var path = Settings.LogPath;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        var old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch { /* logging must never break the app */ }
        }

        public static string Hex(byte[] b)
        {
            return b == null ? "" : BitConverter.ToString(b).Replace('-', ' ').ToLowerInvariant();
        }
    }
}
