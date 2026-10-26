using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OpenBoxBridge
{
    /// <summary>Flat settings stored in settings.json (same file and keys as the earlier PowerShell app).</summary>
    public class Settings
    {
        readonly string path;
        readonly Dictionary<string, object> v = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Settings(string path)
        {
            this.path = path;
            Def("NozzleHeight", 0.0); Def("OffsetX", 0.0); Def("OffsetY", 0.0); Def("Calibrated", false); Def("NozzleMeasured", false); Def("CentreVersion", 0.0);
            Def("Motion", "Normal"); Def("LiveStatus", true); Def("LastFile", ""); Def("HeatN", 210.0); Def("HeatB", 60.0);
            Def("ExtTemp", 210.0); Def("ExtLen", 50.0); Def("JogStep", "10"); Def("Rotate180", true); Def("Dark", false);
            Def("SpoolName", "PLA"); Def("DefaultsVersion", 0.0); Def("SpoolStart", 1000.0); Def("SpoolUsed", 0.0); Def("ChartMinutes", 10.0);
            Def("Recent", ""); Def("JobFile", ""); Def("JobStart", ""); Def("JobGrams", 0.0); Def("JobLayers", 0.0); Def("JobLastPercent", 0.0); Def("JobPrinter", ""); Def("ActivePrinter", "");
            Load();
            if (GetD("CentreVersion") < 2) { Set("Calibrated", false); }
            // PLA became the default: move values still at the old ABS defaults
            if (GetD("DefaultsVersion") < 1)
            {
                if (GetD("HeatN") == 260) Set("HeatN", 210.0);
                if (GetD("ExtTemp") == 260) Set("ExtTemp", 210.0);
                if (GetD("HeatB") == 90) Set("HeatB", 60.0);
                if (GetS("SpoolName") == "ABS" && GetD("SpoolUsed") == 0) Set("SpoolName", "PLA");
                Set("DefaultsVersion", 1.0);
            }
        }
        void Def(string k, object d) { v[k] = d; }

        public double GetD(string k) { object o; if (v.TryGetValue(k, out o)) { if (o is double) return (double)o; if (o is bool) return (bool)o ? 1 : 0; double d; if (o != null && double.TryParse(o.ToString(), NumberStyles.Float, Inv, out d)) return d; } return 0; }
        public bool GetB(string k) { object o; if (v.TryGetValue(k, out o)) { if (o is bool) return (bool)o; if (o is double) return (double)o != 0; } return false; }
        public string GetS(string k) { object o; if (v.TryGetValue(k, out o) && o != null) return o is double ? ((double)o).ToString(Inv) : o.ToString(); return ""; }
        public void Set(string k, object o)
        {
            object val = o is int ? (double)(int)o : (o is float ? (double)(float)o : (o is decimal ? (double)(decimal)o : o));
            v[k] = val;
            // calibration belongs to the printer it was measured on: keep a copy per serial number
            string ap = GetS("ActivePrinter");
            if (ap.Length > 0 && Array.IndexOf(PrinterKeys, k) >= 0) v["Printer." + ap + "." + k] = val;
        }

        /// <summary>Settings that belong to one printer (by serial number).</summary>
        public static readonly string[] PrinterKeys = { "NozzleHeight", "OffsetX", "OffsetY", "Calibrated", "NozzleMeasured", "CentreVersion", "Rotate180" };
        static readonly object[] PrinterDefaults = { 0.0, 0.0, 0.0, false, false, 0.0, true };

        public bool KnowsPrinter(string sn) { return v.ContainsKey("Printer." + sn + ".NozzleHeight"); }

        /// <summary>Makes printer <paramref name="sn"/> the active one: its own calibration is loaded
        /// (a printer never seen before starts uncalibrated). The very first printer seen keeps
        /// the calibration that was already there. Returns false if it was already active.</summary>
        public bool SwitchPrinter(string sn)
        {
            string ap = GetS("ActivePrinter");
            if (ap == sn) return false;
            bool first = ap.Length == 0;
            bool known = KnowsPrinter(sn);
            var vals = new object[PrinterKeys.Length];
            for (int i = 0; i < PrinterKeys.Length; i++)
            {
                object o;
                if (known && v.TryGetValue("Printer." + sn + "." + PrinterKeys[i], out o)) vals[i] = o;
                else if (first && v.TryGetValue(PrinterKeys[i], out o)) vals[i] = o;
                else vals[i] = PrinterDefaults[i];
            }
            v["ActivePrinter"] = sn;
            for (int i = 0; i < PrinterKeys.Length; i++) Set(PrinterKeys[i], vals[i]);
            Save();
            return true;
        }

        void Load()
        {
            if (!File.Exists(path)) return;
            try
            {
                string s = File.ReadAllText(path);
                foreach (Match m in Regex.Matches(s, "\"([^\"]+)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|true|false|null|-?[0-9.eE+-]+)"))
                {
                    string key = m.Groups[1].Value, val = m.Groups[2].Value;
                    if (val == "true") v[key] = true;
                    else if (val == "false") v[key] = false;
                    else if (val == "null") v[key] = "";
                    else if (val.StartsWith("\"")) v[key] = Unescape(val.Substring(1, val.Length - 2));
                    else { double d; if (double.TryParse(val, NumberStyles.Float, Inv, out d)) v[key] = d; }
                }
            }
            catch { }
        }
        public void Save()
        {
            try
            {
                var sb = new StringBuilder("{\r\n");
                int i = 0;
                foreach (var kv in v)
                {
                    sb.Append("    \"").Append(kv.Key).Append("\":  ");
                    if (kv.Value is bool) sb.Append((bool)kv.Value ? "true" : "false");
                    else if (kv.Value is double) sb.Append(((double)kv.Value).ToString("0.######", Inv));
                    else sb.Append('"').Append(Escape(kv.Value == null ? "" : kv.Value.ToString())).Append('"');
                    sb.Append(++i < v.Count ? ",\r\n" : "\r\n");
                }
                sb.Append("}\r\n");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
        static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"); }
        static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': if (i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; } break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Runs the UP3D command-line tools one at a time (the USB link is exclusive).
    /// Output lines and completion are delivered on the UI thread.</summary>
    public class ToolRunner
    {
        class Job
        {
            public string Exe; public string[] Args; public bool Quiet;
            public Action<string> OnLine; public Action<int, string> OnDone;
            public Process Proc;
            public DateTime LastOutput = DateTime.Now;
        }
        readonly string bin;
        readonly Control ui;
        readonly Queue<Job> queue = new Queue<Job>();
        readonly object gate = new object();
        Job running;
        public bool BusyUser { get { lock (gate) { return (running != null && !running.Quiet) || HasUserQueued(); } } }
        public bool Idle { get { lock (gate) { return running == null && queue.Count == 0; } } }
        public event Action BusyChanged;

        public ToolRunner(string bin, Control ui)
        {
            this.bin = bin; this.ui = ui;
            watchdog = new System.Threading.Timer(_ => Watch(), null, 5000, 5000);
        }

        readonly System.Threading.Timer watchdog;
        public event Action<string> Note;   // messages for the log (from any thread)

        /// <summary>How long a tool may go without saying anything before it's treated as stuck.
        /// A stuck tool keeps the USB connection, so nothing else can reach the printer.</summary>
        static double QuietLimit(string exe)
        {
            switch (exe.ToLowerInvariant())
            {
                case "up3dtranscode.exe": return 0;      // converting a big file can take a while
                case "up3dload.exe":      return 90;     // prints progress continuously while sending
                case "up3dstatus.exe":    return 120;    // "pos" waits for moves to finish
                default:                  return 240;    // homing / bed-centre moves
            }
        }

        void Watch()
        {
            Job j; lock (gate) j = running;
            if (j == null || j.Proc == null) return;
            double limit = QuietLimit(j.Exe);
            if (limit <= 0 || (DateTime.Now - j.LastOutput).TotalSeconds < limit) return;
            try { if (!j.Proc.HasExited) { j.Proc.Kill(); Say(j.Exe + " stopped responding and was ended, so it doesn't block the USB connection."); } } catch { }
        }

        void Say(string msg) { var h = Note; if (h != null) try { ui.BeginInvoke(h, msg); } catch { } }

        /// <summary>Ends the tool that's running now (used when the app closes).</summary>
        public string RunningExe { get { lock (gate) return running == null ? null : running.Exe; } }
        public void KillRunning()
        {
            lock (gate)
            {
                queue.Clear();
                if (running != null && running.Proc != null) try { if (!running.Proc.HasExited) running.Proc.Kill(); } catch { }
            }
        }

        /// <summary>Ends printer tools from this app's bin folder that are still running from an
        /// earlier session (e.g. a send that hung, or the app was closed mid-send). They hold the
        /// USB connection and cause "Access denied". Returns how many were ended.</summary>
        public int KillLeftovers()
        {
            int n = 0;
            Process mine; lock (gate) mine = running == null ? null : running.Proc;
            foreach (var name in new[] { "up3dload", "up3dstatus", "up3dctl", "up3dcenter", "up3dinfo", "up3dsnap" })
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (mine != null && p.Id == mine.Id) continue;
                        string path = "";
                        try { path = p.MainModule.FileName; } catch { }
                        if (path.Length > 0 && !path.StartsWith(bin, StringComparison.OrdinalIgnoreCase)) continue;
                        p.Kill(); n++;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            return n;
        }

        bool HasUserQueued() { foreach (var j in queue) if (!j.Quiet) return true; return false; }

        public void Run(string exe, string[] args, Action<int, string> onDone, Action<string> onLine = null, bool quiet = false)
        {
            var job = new Job { Exe = exe, Args = args, Quiet = quiet, OnDone = onDone, OnLine = onLine };
            bool start;
            lock (gate)
            {
                if (quiet && (running != null || queue.Count > 0)) return;   // polls never pile up
                queue.Enqueue(job);
                start = running == null;
            }
            if (!quiet) Fire();
            if (start) Next();
        }

        /// <summary>Runs ahead of everything queued (used by STOP). A running tool that is only
        /// waiting on the printer is ended so the stop isn't held up; an upload is left to finish.</summary>
        public void RunUrgent(string exe, string[] args, Action<int, string> onDone, Action<string> onLine = null)
        {
            var job = new Job { Exe = exe, Args = args, Quiet = false, OnDone = onDone, OnLine = onLine };
            bool start;
            lock (gate)
            {
                var rest = queue.ToArray();
                queue.Clear();
                queue.Enqueue(job);
                foreach (var j in rest) queue.Enqueue(j);
                start = running == null;
                if (running != null && running.Proc != null &&
                    !running.Exe.Equals("up3dload.exe", StringComparison.OrdinalIgnoreCase))
                {
                    try { if (!running.Proc.HasExited) running.Proc.Kill(); } catch { }
                }
            }
            Fire();
            if (start) Next();
        }

        void Fire() { var h = BusyChanged; if (h != null) ui.BeginInvoke(h); }

        void Next()
        {
            Job job;
            lock (gate)
            {
                if (running != null || queue.Count == 0) return;
                job = running = queue.Dequeue();
            }
            var t = new Thread(() => Execute(job)) { IsBackground = true, Name = "tool " + job.Exe };
            t.Start();
        }

        void Execute(Job job)
        {
            var all = new StringBuilder();
            int code = -1;
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(bin, job.Exe), Quote(job.Args))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, WorkingDirectory = bin
                };
                using (var p = Process.Start(psi))
                {
                    lock (gate) job.Proc = p;
                    var errTask = p.StandardError.ReadToEndAsync();
                    var buf = new char[512];
                    var line = new StringBuilder();
                    int n;
                    while ((n = p.StandardOutput.Read(buf, 0, buf.Length)) > 0)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            char c = buf[i];
                            all.Append(c);
                            if (c == '\r' || c == '\n')
                            {
                                if (line.Length > 0) Emit(job, line.ToString());
                                line.Clear();
                            }
                            else line.Append(c);
                        }
                    }
                    if (line.Length > 0) Emit(job, line.ToString());
                    p.WaitForExit();
                    string err = errTask.Result;
                    if (!string.IsNullOrEmpty(err))
                    {
                        all.Append(err);
                        foreach (var l in err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) Emit(job, l);
                    }
                    code = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                all.Append("ERROR: could not run " + job.Exe + ": " + ex.Message);
                Emit(job, "ERROR: could not run " + job.Exe + ": " + ex.Message);
            }
            string output = all.ToString();
            int exit = code;
            try
            {
                ui.BeginInvoke((MethodInvoker)delegate
                {
                    lock (gate) running = null;
                    try { if (job.OnDone != null) job.OnDone(exit, output); }
                    finally { if (!job.Quiet) Fire(); Next(); }
                });
            }
            catch (InvalidOperationException) { }   // window closed
        }

        void Emit(Job job, string line)
        {
            job.LastOutput = DateTime.Now;
            if (job.OnLine == null) return;
            try { ui.BeginInvoke(job.OnLine, line); } catch (InvalidOperationException) { }
        }

        static string Quote(string[] args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('"').Append((a ?? "").Replace("\"", "\\\"")).Append('"');
            }
            return sb.ToString();
        }
    }

    /// <summary>What we can learn from a G-code file written by Orca, PrusaSlicer or Bambu Studio.</summary>
    public class GcodeInfo
    {
        public string Path, Name, Time = "", Printer = "", Area = "";
        public double Grams, Metres;
        public int Layers;
        public Image Thumb;
        public long Size;

        static readonly Dictionary<string, GcodeInfo> cache = new Dictionary<string, GcodeInfo>(StringComparer.OrdinalIgnoreCase);

        public static GcodeInfo Read(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                string key = path + "|" + fi.LastWriteTimeUtc.Ticks;
                GcodeInfo c;
                lock (cache) { if (cache.TryGetValue(key, out c)) return c; }
                var g = new GcodeInfo { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path), Size = fi.Length };
                string head, tail;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    head = ReadChunk(fs, 0, (int)Math.Min(fi.Length, 600000));
                    long ts = Math.Max(0, fi.Length - 400000);
                    tail = ts > 0 ? ReadChunk(fs, ts, (int)(fi.Length - ts)) : "";
                }
                string all = head + "\n" + tail;
                Match m;
                m = Regex.Match(all, @"(?:estimated printing time \(normal mode\)|total estimated time)\s*[:=]\s*([^\r\n;]+)");
                if (m.Success) g.Time = m.Groups[1].Value.Trim();
                m = Regex.Match(all, @";\s*(?:total )?filament used \[g\]\s*=\s*([\d.]+)");
                if (!m.Success) m = Regex.Match(all, @";\s*total filament weight \[g\]\s*:\s*([\d.]+)");
                if (m.Success) double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out g.Grams);
                m = Regex.Match(all, @";\s*(?:total )?filament used \[mm\]\s*=\s*([\d.]+)");
                if (m.Success) { double mm; if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out mm)) g.Metres = mm / 1000; }
                m = Regex.Match(all, @";\s*total layer(?:s| number)\s*[:=]\s*(\d+)");
                if (m.Success) int.TryParse(m.Groups[1].Value, out g.Layers);
                m = Regex.Match(all, @";\s*(?:printable_area|bed_shape)\s*=\s*([^\r\n]+)"); if (m.Success) g.Area = m.Groups[1].Value.Trim();
                m = Regex.Match(all, @";\s*printer_settings_id\s*=\s*([^\r\n]+)"); if (m.Success) g.Printer = m.Groups[1].Value.Trim();
                g.Thumb = Thumbnail(head);
                lock (cache) { cache[key] = g; }
                return g;
            }
            catch { return null; }
        }

        static string ReadChunk(FileStream fs, long start, int len)
        {
            var buf = new byte[len];
            fs.Seek(start, SeekOrigin.Begin);
            int n = 0, r;
            while (n < len && (r = fs.Read(buf, n, len - n)) > 0) n += r;
            return Encoding.UTF8.GetString(buf, 0, n);
        }

        /// <summary>Largest PNG/JPG thumbnail embedded by the slicer.</summary>
        static Image Thumbnail(string text)
        {
            Image best = null; int bestArea = 0;
            foreach (Match m in Regex.Matches(text, @";\s*(thumbnail(?:_JPG)?) begin (\d+)x(\d+) \d+\s*\r?\n(.*?);\s*\1 end", RegexOptions.Singleline))
            {
                int w = int.Parse(m.Groups[2].Value), h = int.Parse(m.Groups[3].Value);
                if (w * h <= bestArea) continue;
                try
                {
                    var b64 = new StringBuilder();
                    foreach (var line in m.Groups[4].Value.Split('\n'))
                    {
                        string l = line.Trim();
                        if (l.StartsWith(";")) l = l.Substring(1).Trim();
                        b64.Append(l);
                    }
                    var bytes = Convert.FromBase64String(b64.ToString());
                    using (var ms = new MemoryStream(bytes))
                    using (var img = Image.FromStream(ms))
                    {
                        if (best != null) best.Dispose();
                        best = new Bitmap(img);
                        bestArea = w * h;
                    }
                }
                catch { }
            }
            return best;
        }
    }
}
