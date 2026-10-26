using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace OpenBoxBridge
{
    static class Program
    {
        public const string Version = "1.0";
        public const string WindowTitle = "OpenBoxBridge - for the UP Box+";
        const string MutexName = "Local\\OpenBoxBridge.Single";

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        public static string Root { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'); } }
        public static string Jobs { get { return Path.Combine(Root, "jobs"); } }

        [STAThread]
        static int Main(string[] args)
        {
            // Slicer post-processing (Orca, PrusaSlicer, Bambu Studio): "OpenBoxBridge.exe" <gcode>. Copy the
            // file (the slicer deletes its temp file) and return straight away so the export isn't held up.
            // Files sliced for another printer are ignored.
            if (args.Length >= 1 && args[0] != "--open" && File.Exists(args[args.Length - 1]))
            {
                string dst = HandOff(args[args.Length - 1]);
                if (dst != null && !Running())
                {
                    try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--open \"" + dst + "\"") { UseShellExecute = false }); }
                    catch { }
                }
                return 0;
            }

            string open = (args.Length >= 2 && args[0] == "--open") ? args[1] : null;

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    IntPtr h = FindWindow(null, WindowTitle);
                    if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                    return 0;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
                Application.Run(new MainForm(open));
            }
            return 0;
        }

        static bool Running()
        {
            Mutex m;
            if (Mutex.TryOpenExisting(MutexName, out m)) { m.Dispose(); return true; }
            return false;
        }

        /// <summary>True when the file was sliced for some other printer (the slicer names the printer
        /// profile in the G-code). Such files are left alone instead of being opened here.</summary>
        static bool OtherPrinter(string src, out string printer)
        {
            printer = "";
            try
            {
                var g = GcodeInfo.Read(src);
                if (g == null) return false;
                printer = g.Printer.Trim().Trim('"');
                return printer.Length > 0 && printer.IndexOf("UP Box", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        static string HandOff(string src)
        {
            string other;
            if (OtherPrinter(src, out other))
            {
                try
                {
                    Directory.CreateDirectory(Jobs);
                    File.AppendAllText(Path.Combine(Jobs, "handoff.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                        "  ignored " + Path.GetFileName(Environment.GetEnvironmentVariable("SLIC3R_PP_OUTPUT_NAME") ?? src) + " (sliced for \"" + other + "\")\r\n");
                }
                catch { }
                return null;
            }
            try
            {
                Directory.CreateDirectory(Jobs);
                string name = Environment.GetEnvironmentVariable("SLIC3R_PP_OUTPUT_NAME");
                name = string.IsNullOrEmpty(name) ? "orca_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".gcode" : Path.GetFileName(name);
                string dst = Path.Combine(Jobs, name);
                File.Copy(src, dst, true);
                File.WriteAllText(Path.Combine(Jobs, "latest.txt"), dst, new UTF8Encoding(false));
                return dst;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not copy the sliced file from the slicer:\n" + src + "\n\n" + ex.Message, WindowTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        static void Crash(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Root, "error.log"), DateTime.Now + "  " + ex + "\r\n\r\n");
                MessageBox.Show("Something went wrong:\n\n" + (ex == null ? "unknown error" : ex.Message) +
                    "\n\nDetails were saved to error.log in the app folder. The app will keep running if it can.",
                    WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
