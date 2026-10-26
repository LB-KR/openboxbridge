using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OpenBoxBridge
{
    public partial class MainForm : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string Deg = "°";
        const string Dot = "  ·  ";

        readonly Settings S;
        readonly ToolRunner Tools;
        readonly string Bin, Jobs, LatestPath, HistoryPath;

        // ---- state ----
        bool isPrinting, homed, connected;
        double posX, posY, posZ; bool havePos;
        DateTime nextPoll = DateTime.Now.AddSeconds(1), latestStamp = DateTime.MinValue, nextLatestCheck = DateTime.Now;
        int lastPercent, lastLayer, lastRemain; string lastSystem = "";
        bool jobSawRunning; DateTime jobFirstCheck = DateTime.MinValue;
        bool jobGap;          // lost the printer while a job was on (unplugged, switched off, app busy elsewhere)
        bool askingEnd;       // the "did it finish?" question is showing
        GcodeInfo current;
        int convertedLayers;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 250 };

        // ---- shell ----
        Panel header, pages, statusBar;
        Lbl statusText;
        readonly Dictionary<string, Panel> pageMap = new Dictionary<string, Panel>();
        readonly Dictionary<string, FlatButton> navMap = new Dictionary<string, FlatButton>();
        Pill pill;
        FlatButton btnPowerOff, btnStop;

        // ---- dashboard ----
        Thumb thumb; Lbl jobName, jobInfo, jobState; Progress jobBar; Tile tLayer, tElapsed, tLeft, tEta;
        FlatButton btnPrint, btnOpen, btnCancel;
        Tile tNozzle, tBed; NumericUpDown numHeatN, numHeatB; FlatButton btnHeatN, btnHeatB, btnCool; TempChart chart;
        readonly List<FlatButton> rangeButtons = new List<FlatButton>();
        FlatButton btnYp, btnYm, btnXp, btnXm, btnHome, btnZu, btnZd, btnInit, btnExtrude, btnWithdraw;
        ComboBox cmbStep; Lbl lblPos; NumericUpDown numExtT, numExtL;
        Lbl spoolName, spoolLeft, spoolUsed, spoolNeed; Progress spoolBar;

        // ---- jobs page ----
        JobList jobList; Thumb jobThumb; Lbl jobsName, jobsInfo; FlatButton btnJobLoad, btnJobPrint, btnJobFolder, btnJobRemove;

        // ---- setup page ----
        NumericUpDown numNozzle, numOX, numOY; ComboBox cmbMotion; CheckBox chkRotate, chkLive, chkDark;
        Lbl setupState, printerInfo, lblPrinterSn; FlatButton btnCentre, btnCheck, btnOrca, btnPrusa, btnBambu, btnSnap, btnNozzle;

        // ---- console ----
        TextBox log;

        static readonly Dictionary<string, double[]> Motion = new Dictionary<string, double[]>
        {
            { "Gentle", new double[] { 10, 100, 0.05 } },   // speed factors are % of the 100 mm/s cap in the converter
            { "Normal", new double[] { 20, 100, 0.05 } },
            { "Fast",   new double[] { 50, 100, 0.05 } },     // 750 mm/s2, the most UP Studio uses
        };

        public MainForm(string openFile)
        {
            Bin = Path.Combine(Program.Root, "bin");
            Jobs = Program.Jobs;
            Directory.CreateDirectory(Jobs);
            LatestPath = Path.Combine(Jobs, "latest.txt");
            HistoryPath = Path.Combine(Jobs, "history.txt");
            S = new Settings(Path.Combine(Program.Root, "settings.json"));
            Theme.Init(S.GetB("Dark"));
            Tools = new ToolRunner(Bin, this);
            Tools.Note += m => Log(m);
            int stale = Tools.KillLeftovers();
            if (stale > 0) Log("Ended " + stale + " printer tool(s) left running from before - they were blocking the USB connection.");

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Program.WindowTitle;
            Font = Theme.Base;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            ClientSize = new Size(1200, 790);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildShell();
            BuildDashboard();
            BuildJobs();
            BuildSetup();
            BuildConsole();
            ResumeLayout(true);

            Tools.BusyChanged += UpdateEnabled;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) { LoadGcode(f[0]); ShowPage("Dashboard"); } };
            tick.Tick += OnTick;
            Shown += delegate
            {
                float k = DeviceDpi / 96f;
                jobList.ItemHeight = (int)(72 * k);
                if (File.Exists(LatestPath)) latestStamp = File.GetLastWriteTimeUtc(LatestPath);
                Log("OpenBoxBridge " + Program.Version + " ready - USB printing for the Tiertime UP Box+.");
                if (S.GetD("NozzleHeight") < 50) Log("First time: enter your nozzle height on the Setup page, then find the bed centre.");
                string f = openFile;
                if (string.IsNullOrEmpty(f) && File.Exists(S.GetS("LastFile"))) f = S.GetS("LastFile");
                if (!string.IsNullOrEmpty(f) && File.Exists(f)) LoadGcode(f);
                RefreshSpool();
                UpdateSetupState();
                UpdateEnabled();
                RefreshJobs();
                tick.Start();
            };
            FormClosing += (s, e) =>
            {
                string exe = Tools.RunningExe;
                if (exe != null && exe.Equals("up3dload.exe", StringComparison.OrdinalIgnoreCase) &&
                    !Confirm("A print is still being sent to the printer. Closing now cancels sending it.\n\nClose anyway?", "Sending in progress", MessageBoxIcon.Warning))
                { e.Cancel = true; return; }
                tick.Stop(); SaveAll();
                Tools.KillRunning();   // never leave a tool holding the USB connection
            };
        }

        // =====================================================================================
        // building blocks
        // =====================================================================================
        Lbl T(Control parent, string text, int x, int y, int w, int h, Font f = null, Color? c = null)
        {
            var l = new Lbl(text, f ?? Theme.Base, c ?? Theme.Ink) { Location = new Point(x, y), Size = new Size(w, h) };
            l.BackColor = parent is Card || parent.BackColor == Theme.Surface ? Theme.Surface : parent.BackColor;
            parent.Controls.Add(l);
            return l;
        }
        FlatButton B(Control parent, string text, int x, int y, int w, int h, BtnKind kind, Action click, string glyph = "")
        {
            var b = new FlatButton { Text = text, Kind = kind, Glyph = glyph, Location = new Point(x, y), Size = new Size(w, h) };
            if (kind == BtnKind.Primary) b.Font = Theme.Bold;
            if (click != null) b.Click += (s, e) => click();
            parent.Controls.Add(b);
            return b;
        }
        NumericUpDown N(Control parent, int x, int y, int w, decimal min, decimal max, int dec, decimal inc, double val)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y), Width = w, Minimum = min, Maximum = max, DecimalPlaces = dec, Increment = inc,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Ink, TextAlign = HorizontalAlignment.Right
            };
            n.Value = (decimal)Math.Min((double)max, Math.Max((double)min, val));
            parent.Controls.Add(n);
            return n;
        }
        ComboBox C(Control parent, int x, int y, int w, IEnumerable<string> items, string sel)
        {
            var c = new ComboBox { Location = new Point(x, y), Width = w, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Surface2, ForeColor = Theme.Ink };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedItem = sel;
            if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = Math.Min(1, c.Items.Count - 1);
            parent.Controls.Add(c);
            return c;
        }
        CheckBox X(Control parent, string text, int x, int y, int w, bool val)
        {
            var c = new CheckBox { Text = text, Location = new Point(x, y), Width = w, Height = 24, Checked = val, BackColor = Theme.Surface, ForeColor = Theme.Ink };
            parent.Controls.Add(c);
            return c;
        }
        Card MkCard(Control parent, string caption, int x, int y, int w, int h, AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left)
        {
            var c = new Card { Caption = caption, Location = new Point(x, y), Size = new Size(w, h), Anchor = anchor };
            parent.Controls.Add(c);
            return c;
        }
        Lbl Section(Control parent, string text, int x, int y) { return T(parent, text.ToUpperInvariant(), x, y, 300, 16, Theme.Caption, Theme.Faint); }
        Panel Page(string name)
        {
            // outer page scrolls; inner content keeps the design size and is centred in wide windows
            var outer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false, AutoScroll = true };
            var inner = new Panel { Location = new Point(0, 0), Size = new Size(1200, 712), BackColor = Theme.Bg };
            var db = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (db != null) { db.SetValue(outer, true, null); db.SetValue(inner, true, null); }
            outer.Controls.Add(inner);
            outer.Resize += (s, e) => { inner.Left = Math.Max(0, (outer.ClientSize.Width - inner.Width) / 2) + outer.AutoScrollPosition.X; };
            pages.Controls.Add(outer);
            pageMap[name] = outer;
            return inner;
        }

        // =====================================================================================
        // shell: header, nav, status bar
        // =====================================================================================
        void BuildShell()
        {
            pages = new Panel { Dock = DockStyle.Fill, Size = new Size(1200, 702), BackColor = Theme.Bg };
            // give docked panels their design width before adding right-anchored children
            statusBar = new Panel { Dock = DockStyle.Bottom, Size = new Size(1200, 28), BackColor = Theme.Surface };
            statusText = new Lbl("", Theme.Small, Theme.Muted) { Location = new Point(16, 6), Size = new Size(900, 18), BackColor = Theme.Surface, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            statusBar.Controls.Add(statusText);
            var ver = new Lbl("v" + Program.Version + "  ·  UP3D tools", Theme.Small, Theme.Faint) { Location = new Point(1040, 6), Size = new Size(150, 18), BackColor = Theme.Surface, Anchor = AnchorStyles.Right | AnchorStyles.Top, TextAlign = ContentAlignment.TopRight };
            statusBar.Controls.Add(ver);
            statusBar.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, statusBar.Width, 0); };

            header = new Panel { Dock = DockStyle.Top, Size = new Size(1200, 60), BackColor = Theme.Header };
            var logo = new Lbl(Theme.GPrinter, Theme.Icon, Theme.Hex("#93C5FD")) { Location = new Point(16, 15), Size = new Size(34, 30), BackColor = Theme.Header };
            var title = new Lbl("OpenBoxBridge", Theme.Title, Color.White) { Location = new Point(54, 8), Size = new Size(200, 26), BackColor = Theme.Header };
            var sub = new Lbl("USB printing for the UP Box+", Theme.Small, Theme.HeaderMuted) { Location = new Point(55, 34), Size = new Size(200, 16), BackColor = Theme.Header };
            header.Controls.AddRange(new Control[] { logo, title, sub });

            string[] names = { "Dashboard", "Jobs", "Setup", "Console" };
            string[] glyphs = { Theme.GDash, Theme.GList, Theme.GSettings, Theme.GConsole };
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                var b = B(header, n, 270 + i * 112, 0, 108, 60, BtnKind.Nav, () => ShowPage(n), glyphs[i]);
                b.Radius = 0;
                navMap[n] = b;
            }
            pill = new Pill { Location = new Point(818, 16), Size = new Size(140, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            pill.Set("Not connected", Theme.Idle);
            header.Controls.Add(pill);
            btnPowerOff = B(header, "Power off", 968, 14, 100, 32, BtnKind.Header, DoPowerOff, Theme.GPower);
            btnStop = B(header, "STOP", 1076, 14, 108, 32, BtnKind.Stop, DoStop, Theme.GStop);
            btnStop.Font = Theme.Caption;
            btnPowerOff.Anchor = btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Controls.Add(pages);
            Controls.Add(statusBar);
            Controls.Add(header);
        }

        void ShowPage(string name)
        {
            foreach (var kv in pageMap) kv.Value.Visible = kv.Key == name;
            foreach (var kv in navMap) { kv.Value.Selected = kv.Key == name; kv.Value.Invalidate(); }
            if (name == "Jobs") RefreshJobs();
        }

        // =====================================================================================
        // dashboard
        // =====================================================================================
        void BuildDashboard()
        {
            var p = Page("Dashboard");

            // ---- job card ----
            var job = MkCard(p, "Print job", 16, 16, 768, 244);
            thumb = new Thumb { Location = new Point(16, 40), Size = new Size(150, 150) };
            job.Controls.Add(thumb);
            jobName = T(job, "No file loaded", 184, 38, 560, 28, new Font("Segoe UI Semibold", 13f));
            jobInfo = T(job, "Export from your slicer, drop a .gcode file here, or pick one from Jobs.", 185, 68, 560, 20, Theme.Small, Theme.Muted);
            jobState = T(job, "", 185, 90, 300, 18, Theme.Small, Theme.Muted);
            jobBar = new Progress { Location = new Point(184, 112), Size = new Size(568, 10) };
            job.Controls.Add(jobBar);
            tLayer = new Tile { Location = new Point(184, 132), Size = new Size(136, 58), Text = "Layer" };
            tElapsed = new Tile { Location = new Point(328, 132), Size = new Size(136, 58), Text = "Elapsed" };
            tLeft = new Tile { Location = new Point(472, 132), Size = new Size(136, 58), Text = "Time left" };
            tEta = new Tile { Location = new Point(616, 132), Size = new Size(136, 58), Text = "Finishes at" };
            job.Controls.AddRange(new Control[] { tLayer, tElapsed, tLeft, tEta });
            btnPrint = B(job, "Print", 16, 200, 150, 34, BtnKind.Primary, () => DoPrint(current == null ? null : current.Path), Theme.GPlay);
            btnOpen = B(job, "Open G-code...", 184, 200, 150, 34, BtnKind.Secondary, BrowseGcode, Theme.GFolder);
            btnCancel = B(job, "Cancel print", 342, 200, 140, 34, BtnKind.Danger, DoStop, Theme.GStop);

            // ---- temperatures ----
            var temp = MkCard(p, "Temperatures", 16, 276, 768, 420);
            tNozzle = new Tile { Location = new Point(16, 40), Size = new Size(176, 62), Text = "Nozzle", Dot = Theme.SeriesNozzle };
            tBed = new Tile { Location = new Point(200, 40), Size = new Size(176, 62), Text = "Bed", Dot = Theme.SeriesBed };
            temp.Controls.Add(tNozzle); temp.Controls.Add(tBed);
            T(temp, "Nozzle", 392, 46, 52, 20);
            numHeatN = N(temp, 446, 42, 62, 0, 300, 0, 5, S.GetD("HeatN"));
            T(temp, Deg + "C", 512, 46, 24, 20, Theme.Small, Theme.Muted);
            btnHeatN = B(temp, "Heat", 538, 40, 70, 28, BtnKind.Secondary, () => Heat(true));
            T(temp, "Bed", 392, 80, 52, 20);
            numHeatB = N(temp, 446, 76, 62, 0, 110, 0, 5, S.GetD("HeatB"));
            T(temp, Deg + "C", 512, 80, 24, 20, Theme.Small, Theme.Muted);
            btnHeatB = B(temp, "Heat", 538, 74, 70, 28, BtnKind.Secondary, () => Heat(false));
            btnCool = B(temp, "Cool down", 618, 40, 134, 62, BtnKind.Secondary, Cool, Theme.GSnow);
            chart = new TempChart { Location = new Point(12, 114), Size = new Size(744, 262), Minutes = (int)Math.Max(5, S.GetD("ChartMinutes")) };
            temp.Controls.Add(chart);
            int[] ranges = { 5, 10, 30, 60 };
            for (int i = 0; i < ranges.Length; i++)
            {
                int m = ranges[i];
                var rb = B(temp, m + " min", 16 + i * 66, 382, 60, 26, BtnKind.Ghost, () => SetRange(m));
                rb.Font = Theme.Small;
                rb.Tag = m;
                rangeButtons.Add(rb);
            }
            var legend = T(temp, "— actual     - - target", 560, 386, 192, 18, Theme.Small, Theme.Faint);
            legend.TextAlign = ContentAlignment.TopRight;
            SetRange(chart.Minutes);

            // ---- control ----
            var ctl = MkCard(p, "Control", 800, 16, 384, 352);
            Section(ctl, "Move", 16, 38);
            btnYp = B(ctl, "Y+", 66, 60, 46, 34, BtnKind.Secondary, () => Jog(0, 1, 0));
            btnXm = B(ctl, "X-", 16, 98, 46, 34, BtnKind.Secondary, () => Jog(-1, 0, 0));
            btnHome = B(ctl, "", 66, 98, 46, 34, BtnKind.Secondary, DoHome, Theme.GHome);
            btnXp = B(ctl, "X+", 116, 98, 46, 34, BtnKind.Secondary, () => Jog(1, 0, 0));
            btnYm = B(ctl, "Y-", 66, 136, 46, 34, BtnKind.Secondary, () => Jog(0, -1, 0));
            btnZu = B(ctl, "Bed up", 176, 60, 92, 34, BtnKind.Secondary, () => Jog(0, 0, -1), Theme.GUp);
            T(ctl, "Z", 216, 106, 20, 18, Theme.Small, Theme.Muted);
            btnZd = B(ctl, "Bed down", 176, 136, 92, 34, BtnKind.Secondary, () => Jog(0, 0, 1), Theme.GDown);
            T(ctl, "Step (mm)", 280, 40, 90, 18, Theme.Small, Theme.Muted).BringToFront();   // above the "Move" heading strip
            cmbStep = C(ctl, 280, 60, 88, new[] { "0.1", "1", "10", "50" }, S.GetS("JogStep"));
            cmbStep.SelectedIndexChanged += (s, e) => S.Set("JogStep", cmbStep.SelectedItem as string);
            btnInit = B(ctl, "Initialise", 280, 136, 88, 34, BtnKind.Secondary, DoInit);
            lblPos = T(ctl, "Position: press Home first", 16, 180, 352, 18, Theme.Small, Theme.Muted);
            Section(ctl, "Extruder", 16, 210);
            T(ctl, "Temp", 16, 236, 46, 20);
            numExtT = N(ctl, 64, 232, 62, 170, 300, 0, 5, S.GetD("ExtTemp"));
            T(ctl, Deg + "C", 130, 236, 26, 20, Theme.Small, Theme.Muted);
            T(ctl, "Length", 170, 236, 54, 20);
            numExtL = N(ctl, 226, 232, 62, 1, 200, 0, 5, S.GetD("ExtLen"));
            T(ctl, "mm", 292, 236, 30, 20, Theme.Small, Theme.Muted);
            btnExtrude = B(ctl, "Extrude", 16, 270, 172, 34, BtnKind.Secondary, () => Extrude(false), Theme.GDown);
            btnWithdraw = B(ctl, "Withdraw", 196, 270, 172, 34, BtnKind.Secondary, () => Extrude(true), Theme.GUp);
            T(ctl, "Heats first, waits until hot, then moves the filament.", 16, 314, 352, 18, Theme.Small, Theme.Faint);

            // ---- filament ----
            var fil = MkCard(p, "Filament", 800, 384, 384, 312);
            spoolName = T(fil, "", 16, 40, 352, 24, Theme.Bold);
            spoolLeft = T(fil, "", 16, 66, 352, 34, Theme.Big);
            spoolBar = new Progress { Location = new Point(16, 106), Size = new Size(352, 10) };
            fil.Controls.Add(spoolBar);
            spoolUsed = T(fil, "", 16, 124, 352, 18, Theme.Small, Theme.Muted);
            spoolNeed = T(fil, "", 16, 146, 352, 36, Theme.Small, Theme.Muted);
            B(fil, "New spool...", 16, 192, 170, 32, BtnKind.Secondary, NewSpool, Theme.GSpool);
            B(fil, "Adjust...", 198, 192, 170, 32, BtnKind.Secondary, AdjustSpool);
            T(fil, "Each print subtracts the filament the slicer says it uses (part of it if a print is cancelled). " +
                   "It's an estimate - the printer can't measure the spool.", 16, 236, 352, 60, Theme.Small, Theme.Faint);
        }

        void SetRange(int minutes)
        {
            chart.Minutes = minutes; chart.Invalidate();
            S.Set("ChartMinutes", (double)minutes);
            foreach (var b in rangeButtons) { b.Kind = (int)b.Tag == minutes ? BtnKind.Secondary : BtnKind.Ghost; b.Invalidate(); }
        }

        // =====================================================================================
        // jobs page
        // =====================================================================================
        void BuildJobs()
        {
            var p = Page("Jobs");
            var list = MkCard(p, "Recent jobs", 16, 16, 760, 680, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom);
            jobList = new JobList { Location = new Point(12, 40), Size = new Size(736, 626), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            list.Controls.Add(jobList);
            jobList.SelectedIndexChanged += (s, e) => ShowJobPreview();
            jobList.DoubleClick += (s, e) => { var j = jobList.SelectedItem as JobEntry; if (j != null) { LoadGcode(j.Path); ShowPage("Dashboard"); } };

            var prev = MkCard(p, "Preview", 792, 16, 392, 680);
            jobThumb = new Thumb { Location = new Point(16, 40), Size = new Size(360, 300) };
            prev.Controls.Add(jobThumb);
            jobsName = T(prev, "Pick a job on the left", 16, 352, 360, 26, Theme.Bold);
            jobsInfo = T(prev, "", 16, 380, 360, 120, Theme.Small, Theme.Muted);
            btnJobLoad = B(prev, "Load on dashboard", 16, 508, 360, 34, BtnKind.Secondary, () => { var j = jobList.SelectedItem as JobEntry; if (j != null) { LoadGcode(j.Path); ShowPage("Dashboard"); } }, Theme.GDash);
            btnJobPrint = B(prev, "Print now", 16, 548, 360, 34, BtnKind.Primary, () => { var j = jobList.SelectedItem as JobEntry; if (j != null) { LoadGcode(j.Path); ShowPage("Dashboard"); DoPrint(j.Path); } }, Theme.GPlay);
            btnJobFolder = B(prev, "Show in folder", 16, 596, 176, 32, BtnKind.Secondary, () => { var j = jobList.SelectedItem as JobEntry; if (j != null) Process.Start("explorer.exe", "/select,\"" + j.Path + "\""); }, Theme.GFolder);
            btnJobRemove = B(prev, "Remove from list", 200, 596, 176, 32, BtnKind.Secondary, RemoveJob, Theme.GDelete);
            B(prev, "Open G-code...", 16, 636, 360, 32, BtnKind.Ghost, BrowseGcode, Theme.GFolder);
        }

        List<string> RecentPaths()
        {
            var set = new List<string>();
            foreach (var r in S.GetS("Recent").Split('|')) if (r.Length > 0 && File.Exists(r) && !set.Contains(r, StringComparer.OrdinalIgnoreCase)) set.Add(r);
            try { foreach (var f in Directory.GetFiles(Jobs, "*.gcode")) if (!set.Contains(f, StringComparer.OrdinalIgnoreCase) && !Hidden(f)) set.Add(f); } catch { }
            return set.OrderByDescending(f => File.GetLastWriteTime(f)).Take(40).ToList();
        }
        bool Hidden(string f) { return ("|" + S.GetS("Hidden") + "|").IndexOf("|" + f + "|", StringComparison.OrdinalIgnoreCase) >= 0 || Path.GetFileName(f).StartsWith("_"); }

        void AddRecent(string path)
        {
            var l = S.GetS("Recent").Split('|').Where(x => x.Length > 0 && !x.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
            l.Insert(0, path);
            S.Set("Recent", string.Join("|", l.Take(30)));
            S.Set("Hidden", string.Join("|", S.GetS("Hidden").Split('|').Where(x => x.Length > 0 && !x.Equals(path, StringComparison.OrdinalIgnoreCase))));
        }

        Dictionary<string, string> LastResults()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(HistoryPath))
                    foreach (var line in File.ReadAllLines(HistoryPath))
                    {
                        var f = line.Split('|');
                        if (f.Length >= 5) d[f[1]] = f[2] + " " + f[0] + (f[4].Length > 0 ? " (" + f[4] + ")" : "");
                    }
            }
            catch { }
            return d;
        }

        void RefreshJobs()
        {
            var paths = RecentPaths();
            var results = LastResults();
            string sel = jobList.SelectedItem is JobEntry ? ((JobEntry)jobList.SelectedItem).Path : null;
            var t = new Thread(() =>
            {
                var entries = new List<JobEntry>();
                foreach (var f in paths)
                {
                    var g = GcodeInfo.Read(f);
                    var bits = new List<string> { File.GetLastWriteTime(f).ToString("d MMM HH:mm") };
                    if (g != null && g.Time.Length > 0) bits.Add(g.Time);
                    if (g != null && g.Grams > 0) bits.Add(g.Grams.ToString("0.#", Inv) + " g");
                    string r; if (results.TryGetValue(Path.GetFileNameWithoutExtension(f), out r)) bits.Add(r);
                    entries.Add(new JobEntry { Path = f, Name = Path.GetFileNameWithoutExtension(f), Line2 = string.Join(Dot, bits), Thumb = g == null ? null : g.Thumb });
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        jobList.BeginUpdate();
                        jobList.Items.Clear();
                        foreach (var e in entries) jobList.Items.Add(e);
                        jobList.EndUpdate();
                        if (sel != null) for (int i = 0; i < jobList.Items.Count; i++) if (((JobEntry)jobList.Items[i]).Path == sel) jobList.SelectedIndex = i;
                        if (jobList.SelectedIndex < 0 && jobList.Items.Count > 0) jobList.SelectedIndex = 0;
                        ShowJobPreview();
                    });
                }
                catch { }
            }) { IsBackground = true };
            t.Start();
        }

        void ShowJobPreview()
        {
            var j = jobList.SelectedItem as JobEntry;
            bool has = j != null;
            btnJobLoad.Enabled = btnJobFolder.Enabled = btnJobRemove.Enabled = has;
            btnJobPrint.Enabled = has && !Tools.BusyUser && !isPrinting;
            if (!has) { jobThumb.Image = null; jobThumb.Invalidate(); jobsName.Text = "No jobs yet"; jobsInfo.Text = "Export from your slicer or open a G-code file."; return; }
            var g = GcodeInfo.Read(j.Path);
            jobThumb.Image = g == null ? null : g.Thumb; jobThumb.Invalidate();
            jobsName.Text = j.Name;
            var sb = new StringBuilder();
            if (g != null)
            {
                if (g.Time.Length > 0) sb.AppendLine("Slicer estimate: " + g.Time);
                if (g.Grams > 0) sb.AppendLine("Filament: " + g.Grams.ToString("0.#", Inv) + " g" + (g.Metres > 0 ? " (" + g.Metres.ToString("0.00", Inv) + " m)" : ""));
                if (g.Layers > 0) sb.AppendLine("Layers: " + g.Layers);
                if (g.Printer.Length > 0) sb.AppendLine("Printer profile: " + g.Printer);
                sb.AppendLine("Size: " + (g.Size / 1048576.0).ToString("0.0", Inv) + " MB");
            }
            sb.AppendLine(j.Line2);
            jobsInfo.Text = sb.ToString();
        }

        void RemoveJob()
        {
            var j = jobList.SelectedItem as JobEntry;
            if (j == null) return;
            S.Set("Recent", string.Join("|", S.GetS("Recent").Split('|').Where(x => x.Length > 0 && !x.Equals(j.Path, StringComparison.OrdinalIgnoreCase))));
            S.Set("Hidden", (S.GetS("Hidden") + "|" + j.Path).Trim('|'));
            S.Save();
            RefreshJobs();
        }

        // =====================================================================================
        // setup page
        // =====================================================================================
        void BuildSetup()
        {
            var p = Page("Setup");
            var pr = MkCard(p, "Printer setup", 16, 16, 584, 340);
            T(pr, "Nozzle height", 16, 40, 120, 18, Theme.Small, Theme.Muted);
            numNozzle = N(pr, 16, 62, 120, 0, 300, 2, 0.05m, S.GetD("NozzleHeight"));
            btnNozzle = B(pr, "Calibrate...", 146, 58, 130, 32, BtnKind.Secondary, CalibrateNozzle, Theme.GTarget);
            T(pr, "mm the bed rises to the nozzle, measured with Calibrate. Bigger = bed closer to the nozzle.", 288, 60, 280, 40, Theme.Small, Theme.Faint);
            numNozzle.ValueChanged += (s, e) => { S.Set("NozzleHeight", (double)numNozzle.Value); UpdateSetupState(); UpdatePos(); };

            T(pr, "Bed centre X", 16, 108, 120, 18, Theme.Small, Theme.Muted);
            numOX = N(pr, 16, 128, 120, -500, 500, 2, 0.5m, S.GetD("OffsetX"));
            T(pr, "Bed centre Y", 146, 108, 120, 18, Theme.Small, Theme.Muted);
            numOY = N(pr, 146, 128, 120, -500, 500, 2, 0.5m, S.GetD("OffsetY"));
            numOX.ValueChanged += (s, e) => S.Set("OffsetX", (double)numOX.Value);
            numOY.ValueChanged += (s, e) => S.Set("OffsetY", (double)numOY.Value);
            btnCentre = B(pr, "Find bed centre...", 284, 124, 180, 32, BtnKind.Secondary, FindCentre, Theme.GTarget);

            T(pr, "Motion", 16, 172, 120, 18, Theme.Small, Theme.Muted);
            string mot = S.GetS("Motion"); if (mot.StartsWith("Gentle")) mot = "Gentle";
            cmbMotion = C(pr, 16, 192, 120, Motion.Keys, mot);
            cmbMotion.SelectedIndexChanged += (s, e) => S.Set("Motion", cmbMotion.SelectedItem as string);
            T(pr, "Acceleration when converting. Printing is capped at 25 mm/s and travel at 100 mm/s, like UP Studio.", 146, 194, 420, 36, Theme.Small, Theme.Faint);

            chkRotate = X(pr, "Turn prints 180° (this Box+ runs X and Y the opposite way to the slicer)", 16, 240, 552, S.GetB("Rotate180"));
            chkRotate.CheckedChanged += (s, e) => { S.Set("Rotate180", chkRotate.Checked); UpdatePos(); };
            setupState = T(pr, "", 16, 290, 552, 22, Theme.Base, Theme.Muted);
            lblPrinterSn = T(pr, "", 16, 314, 552, 18, Theme.Small, Theme.Faint);

            var con = MkCard(p, "Connection", 616, 16, 568, 340);
            btnCheck = B(con, "Check printer", 16, 40, 160, 34, BtnKind.Secondary, CheckPrinter, Theme.GRefresh);
            btnSnap = B(con, "Printer snapshot", 184, 40, 170, 34, BtnKind.Secondary, TakeSnapshot, Theme.GList);
            printerInfo = T(con, "Not checked yet.", 16, 86, 536, 120, Theme.Base, Theme.Muted);
            chkLive = X(con, "Live status (every 2 seconds)", 16, 216, 400, S.GetB("LiveStatus"));
            chkLive.CheckedChanged += (s, e) => { S.Set("LiveStatus", chkLive.Checked); if (chkLive.Checked) nextPoll = DateTime.Now; };
            T(con, "Close UP Studio while using this app - only one program can talk to the printer over USB.", 16, 250, 536, 40, Theme.Small, Theme.Faint);

            var orca = MkCard(p, "Slicer presets", 16, 372, 584, 172);
            T(orca, "Installs the UP Box+ USB printer, the 0.15 / 0.20 / 0.25mm print profiles and PLA / ABS. Close the slicer first. " +
                    "Exports from these profiles open here; files sliced for other printers are left alone.", 16, 40, 552, 40, Theme.Small, Theme.Muted);
            btnOrca = B(orca, "OrcaSlicer", 16, 86, 150, 34, BtnKind.Secondary, InstallOrca, Theme.GCube);
            btnPrusa = B(orca, "PrusaSlicer", 174, 86, 170, 34, BtnKind.Secondary, InstallPrusa, Theme.GCube);
            btnBambu = B(orca, "Bambu Studio", 352, 86, 170, 34, BtnKind.Secondary, InstallBambu, Theme.GCube);
            B(orca, "Open presets folder", 16, 128, 180, 32, BtnKind.Ghost, () => Process.Start("explorer.exe", "\"" + Program.Root + "\""), Theme.GFolder);

            var about = MkCard(p, "Appearance and files", 616, 372, 568, 172);
            chkDark = X(about, "Dark theme (applies next time the app opens)", 16, 40, 520, S.GetB("Dark"));
            chkDark.CheckedChanged += (s, e) => S.Set("Dark", chkDark.Checked);
            B(about, "App folder", 16, 88, 160, 34, BtnKind.Ghost, () => Process.Start("explorer.exe", "\"" + Program.Root + "\""), Theme.GFolder);
            B(about, "Read me", 184, 88, 160, 34, BtnKind.Ghost, () => { string r = Path.Combine(Program.Root, "README.txt"); if (File.Exists(r)) Process.Start("notepad.exe", "\"" + r + "\""); });
            T(about, "OpenBoxBridge " + Program.Version + "  ·  free software (GPL-3.0)  ·  not affiliated with Tiertime", 16, 134, 536, 18, Theme.Small, Theme.Faint);
        }

        void UpdateSetupState()
        {
            string sn = S.GetS("ActivePrinter");
            if (lblPrinterSn != null) lblPrinterSn.Text = sn.Length > 0 ? "Settings for printer serial " + sn + " (each printer keeps its own)" : "Settings are saved per printer once one is connected";
            if ((double)numNozzle.Value < 50) { setupState.Text = "●  Nozzle height not set - press Calibrate."; setupState.ForeColor = Theme.Bad; }
            else if (!S.GetB("NozzleMeasured")) { setupState.Text = "●  Nozzle height not measured on this printer yet - press Calibrate."; setupState.ForeColor = Theme.Bad; }
            else if (!S.GetB("Calibrated")) { setupState.Text = "●  Bed centre not set yet - press Find bed centre once."; setupState.ForeColor = Theme.Warn; }
            else { setupState.Text = "●  Setup complete."; setupState.ForeColor = Theme.Good; }
        }

        // =====================================================================================
        // console page
        // =====================================================================================
        void BuildConsole()
        {
            var p = Page("Console");
            var c = MkCard(p, "Activity", 16, 16, 1168, 680, AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom);
            log = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                Font = Theme.Mono, BackColor = Theme.LogBg, ForeColor = Theme.LogInk,
                Location = new Point(16, 84), Size = new Size(1136, 580),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            c.Controls.Add(log);
            B(c, "Copy all", 16, 40, 120, 32, BtnKind.Secondary, () => { if (log.TextLength > 0) Clipboard.SetText(log.Text); });
            B(c, "Clear", 144, 40, 100, 32, BtnKind.Ghost, () => log.Clear());
            ShowPage("Dashboard");
        }

        void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            if (log != null) log.AppendText(line + Environment.NewLine);
            if (statusText != null) statusText.Text = msg;
        }

        // =====================================================================================
        // timer: polling, Orca hand-off, clocks
        // =====================================================================================
        void OnTick(object sender, EventArgs e)
        {
            if (S.GetB("LiveStatus") && DateTime.Now >= nextPoll && Tools.Idle) Poll();
            if (DateTime.Now >= nextLatestCheck)
            {
                nextLatestCheck = DateTime.Now.AddSeconds(1);
                try
                {
                    if (File.Exists(LatestPath))
                    {
                        var st = File.GetLastWriteTimeUtc(LatestPath);
                        if (st > latestStamp)
                        {
                            latestStamp = st;
                            string f = File.ReadAllText(LatestPath).Trim();
                            if (File.Exists(f))
                            {
                                LoadGcode(f); ShowPage("Dashboard");
                                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                                Activate();
                            }
                        }
                    }
                }
                catch { }
                UpdateJobClock();
            }
        }

        void Poll()
        {
            nextPoll = DateTime.Now.AddSeconds(2);
            Tools.Run("up3dstatus.exe", new string[0], OnStatus, null, true);
        }

        static double D(string s) { double d; double.TryParse(s, NumberStyles.Float, Inv, out d); return d; }
        static int I(string s) { int i; int.TryParse(s, NumberStyles.Integer, Inv, out i); return i; }

        void OnStatus(int code, string output)
        {
            string line = output.Split('\r', '\n').LastOrDefault(l => Regex.IsMatch(l, "^[^;]+;[^;]+;[^;]+;"));
            if (code != 0 || line == null)
            {
                connected = false; isPrinting = false;
                bool cleared = false;
                if (JobActive) jobGap = true;
                // "Access denied" usually means one of our own tools got stuck holding the USB
                // connection (polls only run when nothing else of ours is running): clear it
                if (Regex.IsMatch(output, "Access denied|ACCESS|BUSY|Claim Interface", RegexOptions.IgnoreCase))
                {
                    int n = Tools.KillLeftovers();
                    if (n > 0) { Log("Ended " + n + " stuck printer tool(s) that were holding the USB connection."); cleared = true; }
                }
                string first = output.Split('\r', '\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "No response";
                pill.Set("Not connected", Theme.Idle);
                jobState.Text = "Printer: " + Regex.Replace(first, @"^\[ERROR\]\s*", "");
                tNozzle.Set("-", "Nozzle", Color.Empty); tBed.Set("-", "Bed", Color.Empty);
                nextPoll = DateTime.Now.AddSeconds(cleared ? 1 : 5);
                UpdateEnabled();
                return;
            }
            var f = line.Split(';');
            connected = true;
            bool wasPrinting = isPrinting;
            isPrinting = f[0] == "Running Program";
            lastSystem = f[2];
            bool bad = Regex.IsMatch(f[2], "Error|Hot|Cool|Canceled");
            float nozzle = (float)D(f[3]);
            lastLayer = I(f[4]); lastPercent = I(f[6]); lastRemain = I(f[7]);
            float nset = 0, bset = 0, bed = 0; int non = 0, hot = 0, bon = 0;
            if (f.Length >= 16)
            {
                nset = I(f[8]); non = I(f[9]); hot = I(f[10]); bset = I(f[11]); bon = I(f[12]);
                posX = D(f[13]); posY = D(f[14]); posZ = D(f[15]); havePos = true;
            }
            if (f.Length >= 17) bed = (float)D(f[16]);
            if (f.Length >= 18) OnPrinterSerial(f[17].Trim());
            float nTarget = (non != 0) ? nset : 0, bTarget = (bon != 0) ? bset : 0;

            tNozzle.Set(Math.Round(nozzle) + Deg + (nTarget > 0 ? " / " + nTarget + Deg : ""),
                nTarget > 0 ? (hot == 1 ? "Nozzle  ·  ready" : "Nozzle  ·  heating") : "Nozzle  ·  off",
                nTarget > 0 ? (hot == 1 ? Theme.Good : Theme.Warn) : Theme.Muted);
            int mins = (int)Math.Ceiling(bon * 2 / 60.0);
            tBed.Set((f.Length >= 17 ? Math.Round(bed) + Deg : "-") + (bTarget > 0 ? " / " + bTarget + Deg : ""),
                bon != 0 ? (mins > 120 ? "Bed  ·  on" : "Bed  ·  on, " + mins + " min left") : "Bed  ·  off",
                bon != 0 ? Theme.Warn : Theme.Muted);
            chart.Add(new TempSample { T = DateTime.Now, Nozzle = nozzle, NozzleTarget = nTarget, Bed = bed, BedTarget = bTarget });

            if (isPrinting) pill.Set(JobActive ? "Printing" : "Busy", Theme.Accent);
            else if (bad) pill.Set("Attention", Theme.Bad);
            else pill.Set("Ready", Theme.Good);

            TrackJob();
            UpdatePos();
            if (wasPrinting != isPrinting) UpdateEnabled();
            nextPoll = DateTime.Now.AddSeconds(2);
        }

        // =====================================================================================
        // G-code file and job card
        // =====================================================================================
        void BrowseGcode()
        {
            using (var d = new OpenFileDialog { Filter = "G-code (*.gcode;*.gco;*.g)|*.gcode;*.gco;*.g|All files|*.*" })
            {
                try { var lf = S.GetS("LastFile"); if (lf.Length > 0) d.InitialDirectory = Path.GetDirectoryName(lf); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) { LoadGcode(d.FileName); ShowPage("Dashboard"); }
            }
        }

        void LoadGcode(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var g = GcodeInfo.Read(path);
            if (g == null) { Log("Could not read " + path); return; }
            if (JobActive && !string.Equals(S.GetS("JobFile"), path, StringComparison.OrdinalIgnoreCase))
                Log("Loaded " + g.Name + " (the current print carries on).");
            else Log("Loaded " + g.Name);
            current = g;
            S.Set("LastFile", path);
            AddRecent(path);
            S.Save();
            ShowJobCard();
            RefreshSpool();
        }

        void ShowJobCard()
        {
            var g = JobActive ? (GcodeInfo.Read(S.GetS("JobFile")) ?? current) : current;
            if (g == null) return;
            thumb.Image = g.Thumb; thumb.Invalidate();
            jobName.Text = g.Name;
            var bits = new List<string>();
            if (g.Time.Length > 0) bits.Add("Slicer estimate " + g.Time);
            if (g.Grams > 0) bits.Add(g.Grams.ToString("0.#", Inv) + " g");
            if (g.Layers > 0) bits.Add(g.Layers + " layers");
            bits.Add((g.Size / 1048576.0).ToString("0.0", Inv) + " MB");
            jobInfo.Text = string.Join(Dot, bits);
            if (!JobActive) { jobBar.Value = 0; tLayer.Set("-", "", Color.Empty); tElapsed.Set("-", "", Color.Empty); tLeft.Set("-", "", Color.Empty); tEta.Set("-", "", Color.Empty); }
        }

        bool JobActive { get { return S.GetS("JobFile").Length > 0; } }

        void UpdateJobClock()
        {
            if (!JobActive) return;
            DateTime start;
            if (DateTime.TryParse(S.GetS("JobStart"), Inv, DateTimeStyles.RoundtripKind, out start))
                tElapsed.Set(Dur(DateTime.Now - start), "Elapsed", Color.Empty);
            if (isPrinting && lastRemain > 0)
            {
                tLeft.Set(Dur(TimeSpan.FromSeconds(lastRemain)), "Time left", Color.Empty);
                tEta.Set(DateTime.Now.AddSeconds(lastRemain).ToString("HH:mm"), "Finishes at", Color.Empty);
            }
        }

        static string Dur(TimeSpan t)
        {
            if (t.TotalSeconds < 0) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? string.Format("{0}h {1:00}m", (int)t.TotalHours, t.Minutes) : string.Format("{0}m {1:00}s", t.Minutes, t.Seconds);
        }

        /// <summary>Follows a print we started: progress, and filament/history when it ends.</summary>
        void TrackJob()
        {
            if (!JobActive) { jobState.Text = connected ? (isPrinting ? "The printer is running a routine." : "Printer ready.") : jobState.Text; return; }
            string jp = S.GetS("JobPrinter");
            if (jp.Length > 0 && connectedSerial.Length > 0 && jp != connectedSerial)
            {
                jobState.Text = "The current job is on another printer (serial " + jp + "). It's picked up again when that printer is connected.";
                return;
            }
            int total = (int)S.GetD("JobLayers");
            if (isPrinting)
            {
                jobSawRunning = true; jobGap = false;
                // remember progress across disconnects and app restarts
                if (lastPercent > 0 && lastPercent != (int)S.GetD("JobLastPercent")) { S.Set("JobLastPercent", (double)lastPercent); S.Save(); }
                jobBar.Value = lastPercent;
                tLayer.Set(lastLayer <= 0 ? "Preparing" : (total > 0 ? lastLayer + " / " + total : lastLayer.ToString()), "Layer", Color.Empty);
                jobState.Text = lastPercent > 0 ? "Printing  ·  " + lastPercent + "%" : "Heating up - the print starts when the bed and nozzle are ready.";
                return;
            }
            // not running: finished, cancelled, or ended while the app wasn't watching
            if (jobFirstCheck == DateTime.MinValue) jobFirstCheck = DateTime.Now;
            if (!jobSawRunning && !jobGap && (DateTime.Now - jobFirstCheck).TotalSeconds < 8) return;
            int seen = (int)S.GetD("JobLastPercent");
            bool printerSaysDone = lastSystem.IndexOf("Finished", StringComparison.OrdinalIgnoreCase) >= 0;
            if (jobSawRunning && !jobGap)
            {
                // we watched it end
                bool done = printerSaysDone || lastPercent >= 98 || seen >= 98;
                EndJob(done ? "Finished" : "Stopped at " + seen + "%", done ? 1.0 : seen / 100.0);
                return;
            }
            if (printerSaysDone) { EndJob("Finished", 1.0); return; }
            if (askingEnd) return;
            askingEnd = true;
            BeginInvoke((MethodInvoker)AskHowJobEnded);
        }

        /// <summary>The job ended while the app was closed or the printer was out of reach:
        /// ask whether it finished, so the spool and history come out right.</summary>
        void AskHowJobEnded()
        {
            try
            {
                if (!JobActive || isPrinting) return;
                int seen = (int)S.GetD("JobLastPercent");
                double all = S.GetD("JobGrams");
                string name = Path.GetFileNameWithoutExtension(S.GetS("JobFile"));
                string part = seen > 0
                    ? "about " + (all * seen / 100.0).ToString("0", Inv) + " g (it was at " + seen + "% when the app last saw it)"
                    : "nothing (the app never saw its progress - use Adjust... on the spool if needed)";
                string text = name + " ended while the app wasn't connected to the printer.\n\nDid it finish printing?\n\n" +
                              "Yes - it finished: about " + all.ToString("0", Inv) + " g comes off the spool.\n" +
                              "No - it was stopped or failed: " + part + ".";
                bool finished = MessageBox.Show(this, text, "Print ended", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                if (!JobActive) return;
                if (finished) EndJob("Finished", 1.0);
                else EndJob(seen > 0 ? "Stopped at about " + seen + "%" : "Stopped", seen / 100.0);
            }
            finally { askingEnd = false; }
        }

        void EndJob(string result, double frac)
        {
            double grams = S.GetD("JobGrams") * frac;
            S.Set("SpoolUsed", S.GetD("SpoolUsed") + grams);
            DateTime start; string dur = "";
            if (DateTime.TryParse(S.GetS("JobStart"), Inv, DateTimeStyles.RoundtripKind, out start)) dur = Dur(DateTime.Now - start);
            string name = Path.GetFileNameWithoutExtension(S.GetS("JobFile"));
            try { File.AppendAllText(HistoryPath, DateTime.Now.ToString("d MMM HH:mm") + "|" + name + "|" + result + "|" + grams.ToString("0.0", Inv) + "|" + dur + "\r\n"); } catch { }
            Log(name + ": " + result.ToLowerInvariant() + (dur.Length > 0 ? " after " + dur : "") + ". Filament used: about " + grams.ToString("0.0", Inv) + " g.");
            S.Set("JobFile", ""); S.Set("JobStart", ""); S.Set("JobLastPercent", 0.0);
            S.Save();
            jobSawRunning = false; jobGap = false; jobFirstCheck = DateTime.MinValue;
            jobBar.Value = result == "Finished" ? 100 : jobBar.Value;
            jobState.Text = result + (dur.Length > 0 ? " after " + dur : "") + ".";
            jobBar.Fill = result == "Finished" ? Theme.Good : Theme.Warn; jobBar.Invalidate();
            tLeft.Set("-", "Time left", Color.Empty); tEta.Set(DateTime.Now.ToString("HH:mm"), result == "Finished" ? "Finished at" : "Ended at", Color.Empty);
            if (result == "Finished") SystemSounds.Asterisk.Play();
            RefreshSpool();
            UpdateEnabled();
        }

        // =====================================================================================
        // printing
        // =====================================================================================
        bool Confirm(string text, string title, MessageBoxIcon icon = MessageBoxIcon.Question)
        {
            return MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, icon) == DialogResult.Yes;
        }
        void Alert(string text, MessageBoxIcon icon = MessageBoxIcon.Warning)
        {
            MessageBox.Show(this, text, Program.WindowTitle, MessageBoxButtons.OK, icon);
        }
        static string Num(double v) { if (Math.Abs(v) < 0.0005) v = 0; return v.ToString("0.###", Inv); }

        void DoPrint(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Alert("Open a G-code file first (or export one from your slicer)."); return; }
            var g = GcodeInfo.Read(path);
            double h = (double)numNozzle.Value;
            if (h < 50) { Alert("Set the nozzle height first: Setup page > Calibrate."); ShowPage("Setup"); return; }
            if (!S.GetB("NozzleMeasured") &&
                !Confirm("The nozzle height hasn't been measured with Setup > Calibrate yet. With UP Studio's number the last print ran about 25 mm above the bed.\n\nPrint anyway?", "Nozzle height not measured", MessageBoxIcon.Warning)) { ShowPage("Setup"); return; }
            if (!S.GetB("Calibrated") && !Confirm("The bed centre hasn't been set yet, so the print may land in the wrong place.\n\nPrint anyway?", "Bed centre not set", MessageBoxIcon.Warning)) return;
            if (g != null && g.Area.StartsWith("0x0") &&
                !Confirm("This file was sliced with the bed origin in a corner (" + g.Area + ").\nThe UP Box+ USB profile puts 0,0 in the middle of the bed.\n\nSlice it with the 'UP Box+ USB' printer profile. Print anyway?", "Different printer profile", MessageBoxIcon.Warning)) return;
            if (isPrinting && !Confirm("The printer is already running a job. Sending a new one replaces it. Continue?", "Printer busy", MessageBoxIcon.Warning)) return;
            double left = S.GetD("SpoolStart") - S.GetD("SpoolUsed");
            if (g != null && g.Grams > 0 && g.Grams > left &&
                !Confirm("This print needs about " + g.Grams.ToString("0.#", Inv) + " g but the spool has about " + Math.Max(0, left).ToString("0", Inv) + " g left.\n\nPrint anyway?", "Filament may run out", MessageBoxIcon.Warning)) return;

            string src = path;
            if (Regex.IsMatch(path, @"[^\x20-\x7E]")) { src = Path.Combine(Jobs, "_input.gcode"); File.Copy(path, src, true); }
            string umc = Path.Combine(Jobs, Regex.Replace(Path.GetFileNameWithoutExtension(path), @"[^A-Za-z0-9_.-]", "_") + ".umc");
            double[] m; if (!Motion.TryGetValue(cmbMotion.SelectedItem as string ?? "Normal", out m)) m = Motion["Normal"];
            var args = new List<string> { "box", src, umc, Num(h), Num(m[0]), Num(m[1]), Num(m[2]), Num((double)numOX.Value), Num((double)numOY.Value) };
            if (chkRotate.Checked) args.Add("rot180");
            Log("Converting " + Path.GetFileName(path) + " for the UP Box+ ...");
            jobState.Text = "Converting..."; jobBar.Fill = Theme.Accent; jobBar.Value = 0;
            Tools.Run("up3dtranscode.exe", args.ToArray(), (code, output) =>
            {
                if (code != 0 || output.Contains("ERROR"))
                {
                    jobState.Text = "Conversion failed - see Console.";
                    Alert("Conversion failed - the Console page shows the line that caused it.\n\nCommon cause: the file wasn't sliced with the UP Box+ USB profile.", MessageBoxIcon.Error);
                    return;
                }
                string summary = output.Split('\r', '\n').FirstOrDefault(l => l.StartsWith("Height:")) ?? "";
                var lm = Regex.Match(summary, @"Layer:\s*(\d+)");
                convertedLayers = lm.Success ? int.Parse(lm.Groups[1].Value) : (g != null ? g.Layers : 0);
                jobState.Text = "Ready to send.";
                string msg = "Ready to print " + Path.GetFileName(path) + "\n\n" + summary + "\n\nBefore sending:\n  - the bed is clear and the build plate is on\n  - filament is loaded\n  - UP Studio is closed\n\n" +
                             "The bed heats first; the print starts a few minutes later. Send to printer now?";
                if (!Confirm(msg, "Send to printer")) { Log("Not sent."); jobState.Text = ""; return; }
                Log("Sending to the printer over USB...");
                Tools.Run("up3dload.exe", new[] { umc }, (code2, out2) =>
                {
                    if (code2 == 0)
                    {
                        jobBar.Value = 0;
                        S.Set("JobFile", path); S.Set("JobStart", DateTime.Now.ToString("o", Inv));
                        S.Set("JobGrams", g != null ? g.Grams : 0.0); S.Set("JobLayers", (double)convertedLayers);
                        S.Save();
                        S.Set("JobLastPercent", 0.0); S.Set("JobPrinter", S.GetS("ActivePrinter")); S.Save();
                        jobSawRunning = false; jobGap = false; jobFirstCheck = DateTime.MinValue;
                        jobState.Text = "Sent. Heating up - the print starts when the bed and nozzle are ready.";
                        Log("Sent. The printer heats up and starts on its own.");
                        current = g ?? current; ShowJobCard();
                        nextPoll = DateTime.Now;
                        UpdateEnabled();
                    }
                    else
                    {
                        jobState.Text = "Sending failed - see Console.";
                        UsbHint(out2);
                        Alert("Sending failed (code " + code2 + "). The Console page has the details.", MessageBoxIcon.Error);
                    }
                }, l =>
                {
                    var pm = Regex.Match(l, @"^UPload: [\d.]+ kB sent \((\d+)%\)");
                    if (pm.Success) { jobBar.Value = int.Parse(pm.Groups[1].Value); jobState.Text = "Sending " + pm.Groups[1].Value + "%"; }
                    else Log(l);
                });
            }, l => Log(l));
        }

        void UsbHint(string output)
        {
            if (output.Contains("No UP printer found")) Log("Hint: plug the USB cable in, switch the printer on and wait 10 s.");
            else if (Regex.IsMatch(output, "Claim Interface|BUSY|ACCESS|Another program")) Log("Hint: close UP Studio (check the system tray too), then try again.");
            else if (Regex.IsMatch(output, "driver|NOT_SUPPORTED")) Log("Hint: Windows driver problem - see README.txt, 'If Check printer fails'.");
        }

        void DoStop()
        {
            if (!Confirm(JobActive ? "Stop the current print?" : "Stop whatever the printer is doing?", "Stop", MessageBoxIcon.Warning)) return;
            Log("Stopping...");
            Tools.RunUrgent("up3dstatus.exe", new[] { "stop" }, (c, o) =>
            {
                nextPoll = DateTime.Now;
                if (c == 0) { Log("Stopped. Heaters off."); return; }
                if (c == 11) { Alert("The printer paused the job and turned the heaters off, but didn't end it.\n\nPress the button on the printer to end the job.", MessageBoxIcon.Warning); return; }
                if (c == 12) { Alert("The printer is still running the job.\n\nPress the button on the printer to stop it.", MessageBoxIcon.Error); return; }
                UsbHint(o);
                Log("Stop did not reach the printer.");
                Alert("The stop command did not reach the printer.\n\nPress the button on the printer to stop it.", MessageBoxIcon.Error);
            }, l => Log(l));
        }
        void DoPowerOff()
        {
            if (!Confirm("Switch the printer's heaters and motors off?", "Power off")) return;
            Tools.Run("up3dstatus.exe", new[] { "off" }, (c, o) => { Log(c == 0 ? "Printer powered down." : "Power off did not reach the printer."); nextPoll = DateTime.Now; });
        }

        // =====================================================================================
        // printer controls
        // =====================================================================================
        void Ctl(string exe, string[] args, string what, Action after = null)
        {
            Log(what);
            Tools.Run(exe, args, (code, output) =>
            {
                if (code == 0) { if (after != null) after(); }
                else { UsbHint(output); Log("That command did not reach the printer - details above."); }
                nextPoll = DateTime.Now.AddSeconds(1);
            }, l => { if (!Regex.IsMatch(l, "^[^;]+;[^;]+;[^;]+;[-\\d.]+;")) Log(l); });
        }

        void Heat(bool nozzle)
        {
            if (nozzle) { int t = (int)numHeatN.Value; S.Set("HeatN", (double)t); Ctl("up3dstatus.exe", new[] { "heat", t.ToString() }, "Heating nozzle to " + t + Deg + "C..."); }
            else { int t = (int)numHeatB.Value; S.Set("HeatB", (double)t); Ctl("up3dstatus.exe", new[] { "bed", t.ToString(), "30" }, "Bed heater on (" + t + Deg + "C) for 30 minutes - Cool down turns it off sooner."); }
        }
        void Cool()
        {
            if (isPrinting && !Confirm("The printer is running a job. Cooling down stops it. Continue?", "Cool down", MessageBoxIcon.Warning)) return;
            Ctl("up3dstatus.exe", new[] { "cool" }, "Heaters off.");
        }
        void DoHome()
        {
            if (!Confirm("Home all axes? The bed moves to the bottom and the head goes to its home corner.", "Home")) return;
            Ctl("up3dctl.exe", new[] { "box", "home" }, "Homing...", () => { homed = true; UpdatePos(); });
        }
        void DoInit()
        {
            if (!Confirm("Run the printer's own initialise routine? It homes every axis and parks the head.", "Initialise")) return;
            Ctl("up3dstatus.exe", new[] { "init" }, "Initialising...", () => { homed = true; UpdatePos(); });
        }

        double Sign { get { return chkRotate.Checked ? -1.0 : 1.0; } }
        void OrcaXY(out double x, out double y)
        {
            x = Sign * (posX - (double)numOX.Value);
            y = Sign * (posY - (double)numOY.Value);
        }
        void UpdatePos()
        {
            if (lblPos == null) return;
            if (!homed || !havePos) { lblPos.Text = "Position: press Home (or Initialise) first"; return; }
            double x, y; OrcaXY(out x, out y);
            string bed = Dot + "bed raised " + (-posZ).ToString("0.0", Inv) + " mm";
            string gap = (double)numNozzle.Value >= 50 ? Dot + "gap about " + Math.Max(0, (double)numNozzle.Value + posZ).ToString("0.0", Inv) + " mm" : "";
            lblPos.Text = "Position: X " + Num(Math.Round(x, 1)) + "   Y " + Num(Math.Round(y, 1)) + bed + gap;
        }

        void Jog(double dx, double dy, double dz)
        {
            if (!homed) { Alert("Press Home (or Initialise) first, so the app knows where the head and bed are."); return; }
            double step = D(cmbStep.SelectedItem as string ?? "10");
            dx *= step; dy *= step; dz *= step;
            if (havePos)
            {
                if (dz < 0)
                {
                    double h = (double)numNozzle.Value;
                    if (h < 50) { Alert("Set the nozzle height first (Setup page) - the app uses it to stop the bed before it reaches the nozzle."); return; }
                    double limit = -(h - 1.0);
                    if (posZ + dz < limit)
                    {
                        dz = limit - posZ;
                        if (dz > -0.05) { Log("The bed is already 1 mm from the nozzle - not moving it up any further."); return; }
                        Log("Limited the move so the bed stops 1 mm below the nozzle.");
                    }
                }
                if (S.GetB("Calibrated") && (dx != 0 || dy != 0))
                {
                    double x, y; OrcaXY(out x, out y);
                    if (Math.Abs(x + dx) > 135 || Math.Abs(y + dy) > 110) { Log("That would move the head past the edge of the bed - not moving."); return; }
                }
                posX += Sign * dx; posY += Sign * dy; posZ += dz;
                UpdatePos();
            }
            Ctl("up3dctl.exe", new[] { "box", "jog", Num(Sign * dx), Num(Sign * dy), Num(dz) }, "Move X" + Num(dx) + " Y" + Num(dy) + " Z" + Num(dz));
        }

        void Extrude(bool withdraw)
        {
            int t = (int)numExtT.Value, l = (int)numExtL.Value;
            S.Set("ExtTemp", (double)t); S.Set("ExtLen", (double)l);
            if (withdraw)
            {
                if (!Confirm("Withdraw " + l + " mm of filament at " + t + Deg + "C?\n\nThe nozzle heats first; the filament is pulled out once it's hot, then the heater turns off.", "Withdraw filament")) return;
                Ctl("up3dctl.exe", new[] { "box", "withdraw", t.ToString(), l.ToString() }, "Withdraw: heating to " + t + Deg + "C, then pulling " + l + " mm...");
            }
            else Ctl("up3dctl.exe", new[] { "box", "extrude", t.ToString(), l.ToString(), "120" }, "Extrude: heating to " + t + Deg + "C, then pushing " + l + " mm (heater stays on)...");
        }

        void CheckPrinter()
        {
            Log("Checking printer...");
            printerInfo.Text = "Checking...";
            Tools.Run("up3dinfo.exe", new string[0], (code, output) =>
            {
                var m = Regex.Match(output, @"TypeID:(\S+)\s+Serial:(\S+)\s+ROM:(\S+)");
                if (code == 0 && m.Success)
                {
                    var st = Regex.Match(output, @"Steps/mm-X:([\d.]+)\s+Steps/mm-Y:([\d.]+)\s+Steps/mm-Z:([\d.]+)");
                    printerInfo.Text = "Connected over USB.\n\nPrinter type " + m.Groups[1].Value + "   ·   serial " + m.Groups[2].Value + "   ·   firmware " + m.Groups[3].Value +
                        (st.Success ? "\nSteps/mm: X " + D(st.Groups[1].Value) + "  Y " + D(st.Groups[2].Value) + "  Z " + D(st.Groups[3].Value) : "");
                    printerInfo.ForeColor = Theme.Ink;
                    Log("Printer found (type " + m.Groups[1].Value + ", firmware " + m.Groups[3].Value + ").");
                    nextPoll = DateTime.Now;
                }
                else
                {
                    printerInfo.Text = "Printer not reachable.\n\n" + (output.Split('\r', '\n').FirstOrDefault(x => x.Trim().Length > 0) ?? "");
                    printerInfo.ForeColor = Theme.Bad;
                    UsbHint(output);
                }
            });
        }

        /// <summary>Read-only dump of the printer's settings memory (for finding where values live).</summary>
        void TakeSnapshot()
        {
            string dir = Path.Combine(Jobs, "snapshots");
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, "snap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            Log("Reading the printer's settings memory (read-only)...");
            Tools.Run("up3dsnap.exe", new[] { f }, (code, output) =>
            {
                if (code == 0) Log("Snapshot saved: " + f);
                else { UsbHint(output); Log("Snapshot failed."); }
            });
        }

        // =====================================================================================
        // bed centre
        // =====================================================================================
        void FindCentre()
        {
            if (!Confirm("The printer will home, then move the nozzle over the bed (the bed stays low).\n\nNudge it until it is over the middle of the bed, then press Save.\n\nMake sure the bed is clear and UP Studio is closed. Continue?", "Find bed centre")) return;
            double sx = 125, sy = 20;
            if (S.GetB("Calibrated")) { sx = (double)numOX.Value; sy = (double)numOY.Value; }
            using (var d = new CentreDialog(this, Tools, sx, sy, Sign))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    numOX.Value = (decimal)d.X; numOY.Value = (decimal)d.Y;
                    S.Set("OffsetX", d.X); S.Set("OffsetY", d.Y); S.Set("Calibrated", true); S.Set("CentreVersion", 2.0);
                    S.Save();
                    Log("Bed centre saved: X " + Num(d.X) + ", Y " + Num(d.Y) + ".");
                    UpdateSetupState();
                }
            }
        }
        public void LogFromDialog(string s) { Log(s); }

        // =====================================================================================
        // several printers: each serial number keeps its own calibration
        // =====================================================================================
        string connectedSerial = "";
        void OnPrinterSerial(string sn)
        {
            if (sn.Length == 0 || sn == "0" || sn == connectedSerial) return;
            connectedSerial = sn;
            string prev = S.GetS("ActivePrinter");
            bool known = S.KnowsPrinter(sn);
            if (!S.SwitchPrinter(sn)) return;
            homed = false; havePos = false;
            numNozzle.Value = Clamp(numNozzle, S.GetD("NozzleHeight"));
            numOX.Value = Clamp(numOX, S.GetD("OffsetX"));
            numOY.Value = Clamp(numOY, S.GetD("OffsetY"));
            chkRotate.Checked = S.GetB("Rotate180");
            UpdateSetupState(); UpdatePos();
            if (prev.Length == 0) { Log("Printer serial " + sn + " connected - the current calibration is saved for it."); return; }
            if (known)
            {
                Log("Printer serial " + sn + " connected - loaded its own calibration (nozzle height " + S.GetD("NozzleHeight").ToString("0.00", Inv) + " mm).");
                return;
            }
            Log("New printer serial " + sn + " connected - it needs calibrating before it can print.");
            BeginInvoke((MethodInvoker)delegate
            {
                ShowPage("Setup");
                Alert("A different printer is connected (serial " + sn + ").\n\nEach printer keeps its own calibration, and this one hasn't been calibrated yet. " +
                      "Run Find bed centre and Calibrate on the Setup page before printing on it.\n\nThe other printer's settings are kept and come back when it's plugged in again.", MessageBoxIcon.Information);
            });
        }
        static decimal Clamp(NumericUpDown n, double d) { decimal m = (decimal)d; return Math.Max(n.Minimum, Math.Min(n.Maximum, m)); }

        // =====================================================================================
        // nozzle height
        // =====================================================================================
        void CalibrateNozzle()
        {
            if (isPrinting) { Alert("The printer is running a job. Calibrate when it has finished."); return; }
            if (!S.GetB("Calibrated")) { Alert("Set the bed centre first (Find bed centre...), so the nozzle is measured over the middle of the bed."); return; }
            if (!Confirm("The printer will home the same way a print does, then move the nozzle over the bed centre.\n\nYou then raise the bed in steps until a sheet of paper just drags under the nozzle.\n\nNozzle cold and clean, bed clear, UP Studio closed. Continue?", "Calibrate nozzle height")) return;
            using (var d = new NozzleDialog(this, Tools, (double)numOX.Value, (double)numOY.Value, (double)numNozzle.Value))
            {
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    double old = (double)numNozzle.Value;
                    numNozzle.Value = (decimal)Math.Max(0, Math.Min(300, d.Result));
                    S.Set("NozzleHeight", d.Result); S.Set("NozzleMeasured", true);
                    S.Save();
                    Log("Nozzle height saved: " + d.Result.ToString("0.00", Inv) + " mm (was " + old.ToString("0.00", Inv) + ").");
                    UpdateSetupState();
                }
            }
            homed = true; nextPoll = DateTime.Now;
        }

        // =====================================================================================
        // filament
        // =====================================================================================
        void RefreshSpool()
        {
            double start = S.GetD("SpoolStart"), used = S.GetD("SpoolUsed"), left = Math.Max(0, start - used);
            double pct = start > 0 ? left * 100 / start : 0;
            spoolName.Text = S.GetS("SpoolName") + " spool";
            spoolLeft.Text = left.ToString("0", Inv) + " g left";
            spoolLeft.ForeColor = pct < 10 ? Theme.Bad : (pct < 25 ? Theme.Warn : Theme.Ink);
            spoolBar.Fill = pct < 10 ? Theme.Bad : (pct < 25 ? Theme.Warn : Theme.Good);
            spoolBar.Value = (float)pct;
            spoolUsed.Text = "Used " + used.ToString("0", Inv) + " g of " + start.ToString("0", Inv) + " g  (" + pct.ToString("0", Inv) + "% left)";
            var g = current;
            if (g != null && g.Grams > 0)
            {
                bool enough = g.Grams <= left;
                spoolNeed.Text = "Loaded job needs about " + g.Grams.ToString("0.#", Inv) + " g" + (enough ? " - enough on the spool." : " - more than is left on the spool!");
                spoolNeed.ForeColor = enough ? Theme.Muted : Theme.Bad;
            }
            else spoolNeed.Text = "";
        }

        void NewSpool()
        {
            string name = S.GetS("SpoolName");
            double grams = 1000;
            if (!Ask.TwoFields(this, "New spool", "Material / name", ref name, "Filament weight on the spool (g)", ref grams)) return;
            S.Set("SpoolName", name.Trim().Length > 0 ? name.Trim() : "Filament");
            S.Set("SpoolStart", grams); S.Set("SpoolUsed", 0.0); S.Save();
            Log("New spool: " + name + ", " + grams.ToString("0", Inv) + " g.");
            RefreshSpool();
        }
        void AdjustSpool()
        {
            string name = S.GetS("SpoolName");
            double left = Math.Max(0, S.GetD("SpoolStart") - S.GetD("SpoolUsed"));
            if (!Ask.TwoFields(this, "Adjust spool", "Material / name", ref name, "Filament left (g) - weigh the spool and subtract the empty spool weight", ref left)) return;
            S.Set("SpoolName", name.Trim().Length > 0 ? name.Trim() : "Filament");
            S.Set("SpoolUsed", Math.Max(0, S.GetD("SpoolStart") - left)); if (left > S.GetD("SpoolStart")) { S.Set("SpoolStart", left); S.Set("SpoolUsed", 0.0); }
            S.Save();
            RefreshSpool();
        }

        // =====================================================================================
        // Orca presets
        // =====================================================================================
        // The presets name this app as the slicer's post-processing step. Point that at wherever the
        // app actually is, so moving the folder doesn't break the hand-off.
        static string PostProcessLiteral()
        {
            string inner = "\"" + Application.ExecutablePath + "\"";
            return "\"" + inner.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        static string FixPostProcessJson(string text)
        {
            return Regex.Replace(text, "\"post_process\"\\s*:\\s*\\[[^\\]]*\\]",
                m => "\"post_process\": [\r\n        " + PostProcessLiteral() + "\r\n    ]");
        }
        static string FixPostProcessIni(string text)
        {
            return Regex.Replace(text, @"(?m)^post_process\s*=.*?(\r?)$", m => "post_process = " + PostProcessLiteral() + m.Groups[1].Value);
        }
        static bool SlicerRunning(params string[] names)
        {
            foreach (var n in names) if (Process.GetProcessesByName(n).Length > 0) return true;
            return false;
        }
        static string Json(string file) { return Regex.Match(File.ReadAllText(file), "\"type\"\\s*:\\s*\"(machine|process|filament)\"").Groups[1].Value; }

        /// <summary>Orca and Bambu Studio keep user presets in user\&lt;account or default&gt;\machine|process|filament.</summary>
        int InstallJsonPresets(string srcDir, string appRoot)
        {
            string[] files = Directory.Exists(srcDir) ? Directory.GetFiles(srcDir, "*.json") : new string[0];
            string userRoot = Path.Combine(appRoot, "user");
            var targets = Directory.Exists(userRoot) ? Directory.GetDirectories(userRoot).ToList() : new List<string>();
            if (targets.Count == 0) targets.Add(Path.Combine(userRoot, "default"));
            int n = 0;
            foreach (var t in targets)
                foreach (var f in files)
                {
                    string type = Json(f);
                    if (type.Length == 0) continue;
                    string dir = Path.Combine(t, type);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, Path.GetFileName(f)), FixPostProcessJson(File.ReadAllText(f)), new UTF8Encoding(false));
                    n++;
                }
            return n;
        }

        void InstallOrca()
        {
            string src = Path.Combine(Program.Root, "orca");
            if (!Directory.Exists(src) || Directory.GetFiles(src, "*.json").Length == 0) { Alert("No preset files found in " + src); return; }
            string orcaRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrcaSlicer");
            if (!Directory.Exists(orcaRoot)) { Alert("OrcaSlicer's settings folder wasn't found (" + orcaRoot + "). Open Orca once, close it, then try again."); return; }
            if (SlicerRunning("orca-slicer", "OrcaSlicer")) { Alert("Please close OrcaSlicer first, then press the button again."); return; }
            int n = InstallJsonPresets(src, orcaRoot);
            Log("Orca presets installed (" + n + " files).");
            Alert("Installed " + n + " preset files.\n\nOpen OrcaSlicer and choose:\n  Printer:   UP Box+ USB\n  Process:   UP Box+ 0.20mm (or 0.15mm Fine / 0.25mm Draft)\n  Filament:  UP PLA (Box+) or UP ABS (Box+)", MessageBoxIcon.Information);
        }

        void InstallBambu()
        {
            string src = Path.Combine(Program.Root, "bambustudio");
            if (!Directory.Exists(src) || Directory.GetFiles(src, "*.json").Length == 0) { Alert("No preset files found in " + src); return; }
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuStudio");
            if (!Directory.Exists(root)) { Alert("Bambu Studio's settings folder wasn't found (" + root + "). Open Bambu Studio once, close it, then try again."); return; }
            if (SlicerRunning("bambu-studio", "BambuStudio")) { Alert("Please close Bambu Studio first, then press the button again."); return; }
            int n = InstallJsonPresets(src, root);
            Log("Bambu Studio presets installed (" + n + " files).");
            Alert("Installed " + n + " preset files.\n\nOpen Bambu Studio and choose:\n  Printer:   UP Box+ USB (under user presets)\n  Process:   UP Box+ 0.20mm (or 0.15mm Fine / 0.25mm Draft)\n  Filament:  UP PLA (Box+) or UP ABS (Box+)\n\nThen slice and use Export G-code (not Print).", MessageBoxIcon.Information);
        }

        void InstallPrusa()
        {
            string src = Path.Combine(Program.Root, "prusaslicer");
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrusaSlicer");
            if (!Directory.Exists(src)) { Alert("No preset files found in " + src); return; }
            if (!Directory.Exists(root)) { Alert("PrusaSlicer's settings folder wasn't found (" + root + "). Open PrusaSlicer once, close it, then try again."); return; }
            if (SlicerRunning("prusa-slicer", "PrusaSlicer", "prusa-slicer-console")) { Alert("Please close PrusaSlicer first, then press the button again."); return; }
            int n = 0;
            foreach (var kind in new[] { "printer", "print", "filament" })
            {
                string sd = Path.Combine(src, kind);
                if (!Directory.Exists(sd)) continue;
                string dd = Path.Combine(root, kind);
                Directory.CreateDirectory(dd);
                foreach (var f in Directory.GetFiles(sd, "*.ini"))
                {
                    File.WriteAllText(Path.Combine(dd, Path.GetFileName(f)), FixPostProcessIni(File.ReadAllText(f)), new UTF8Encoding(false));
                    n++;
                }
            }
            Log("PrusaSlicer presets installed (" + n + " files).");
            Alert("Installed " + n + " preset files.\n\nOpen PrusaSlicer (Expert mode shows every setting) and choose:\n  Printer:   UP Box+ USB\n  Print:     UP Box+ 0.20mm (or 0.15mm Fine / 0.25mm Draft)\n  Filament:  UP PLA (Box+) or UP ABS (Box+)\n\nThen slice and use Export G-code.", MessageBoxIcon.Information);
        }

        // =====================================================================================
        // enable/disable
        // =====================================================================================
        void UpdateEnabled()
        {
            bool free = !Tools.BusyUser;
            bool ctl = free && !isPrinting;   // sending anything to the printer would cancel a running print
            foreach (var b in new[] { btnYp, btnYm, btnXp, btnXm, btnHome, btnZu, btnZd, btnInit, btnExtrude, btnWithdraw, btnHeatN, btnHeatB, btnCentre, btnNozzle })
                if (b != null) b.Enabled = ctl;
            if (btnPrint != null) btnPrint.Enabled = free && current != null;
            if (btnCool != null) btnCool.Enabled = free;
            if (btnCheck != null) btnCheck.Enabled = free;
            if (btnSnap != null) btnSnap.Enabled = free;
            if (btnPowerOff != null) btnPowerOff.Enabled = free;
            if (btnCancel != null) btnCancel.Visible = JobActive;
            if (btnJobPrint != null) btnJobPrint.Enabled = free && !isPrinting && jobList.SelectedItem != null;
            Cursor = free ? Cursors.Default : Cursors.AppStarting;
        }

        void SaveAll()
        {
            S.Set("HeatN", (double)numHeatN.Value); S.Set("HeatB", (double)numHeatB.Value);
            S.Set("ExtTemp", (double)numExtT.Value); S.Set("ExtLen", (double)numExtL.Value);
            S.Set("NozzleHeight", (double)numNozzle.Value); S.Set("OffsetX", (double)numOX.Value); S.Set("OffsetY", (double)numOY.Value);
            S.Save();
        }
    }
}
