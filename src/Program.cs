using System;
using System.Threading;
using System.Windows.Forms;

namespace HandyBadgeRemote
{
    static class AppInfo
    {
        public const string Name = "HandyBadge Remote";
        public const string Version = "0.1";
        public const string BuildDate = "23 November 2026";
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "HandyBadgeRemote.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("HandyBadge Remote is already running. Look for its icon in the system tray.", AppInfo.Name);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Log.Write(AppInfo.Name + " v" + AppInfo.Version + " (" + AppInfo.BuildDate + ") starting");
                Application.Run(new TrayApp());
            }
        }
    }
}
