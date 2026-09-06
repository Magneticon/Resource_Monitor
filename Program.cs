using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LegacyGpuMonitor
{
    internal static class Program
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            // XP supports the Vista-era DPI awareness API. Calling it before
            // creating any WinForms controls prevents the desktop from
            // bitmap-virtualizing this application at high DPI.
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
