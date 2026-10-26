using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace OpenBoxBridge
{
    /// <summary>Base for small styled dialogs.</summary>
    public class StyledDialog : Form
    {
        public StyledDialog(string title, int w, int h)
        {
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = title; Font = Theme.Base; BackColor = Theme.Surface; ForeColor = Theme.Ink;
            ClientSize = new Size(w, h);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        }
        protected Lbl L(string text, int x, int y, int w, int h, Font f = null, Color? c = null)
        {
            var l = new Lbl(text, f ?? Theme.Base, c ?? Theme.Ink) { Location = new Point(x, y), Size = new Size(w, h) };
            Controls.Add(l); return l;
        }
        protected FlatButton Btn(string text, int x, int y, int w, int h, BtnKind k, EventHandler click)
        {
            var b = new FlatButton { Text = text, Kind = k, Location = new Point(x, y), Size = new Size(w, h) };
            if (k == BtnKind.Primary) b.Font = Theme.Bold;
            b.Click += click; Controls.Add(b); return b;
        }
    }

    /// <summary>Nudge the nozzle over the bed centre, then save the position.</summary>
    public class CentreDialog : StyledDialog
    {
        public double X, Y;
        readonly ToolRunner tools;
        readonly MainForm main;
        readonly double sign;
        readonly NumericUpDown nx, ny;
        readonly Lbl status;
        readonly FlatButton[] buttons;
        bool homed;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public CentreDialog(MainForm main, ToolRunner tools, double x, double y, double sign) : base("Find bed centre", 500, 330)
        {
            this.main = main; this.tools = tools; this.sign = sign; X = x; Y = y;
            L("Nudge the nozzle until it is over the middle of the bed, then press Save.", 20, 16, 460, 40, Theme.Base, Theme.Muted);
            L("Printer X", 20, 66, 80, 18, Theme.Small, Theme.Muted);
            nx = new NumericUpDown { Location = new Point(20, 86), Width = 96, Minimum = -500, Maximum = 500, DecimalPlaces = 1, Increment = 1, Value = (decimal)x, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Ink };
            L("Printer Y", 20, 120, 80, 18, Theme.Small, Theme.Muted);
            ny = new NumericUpDown { Location = new Point(20, 140), Width = 96, Minimum = -500, Maximum = 500, DecimalPlaces = 1, Increment = 1, Value = (decimal)y, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Ink };
            Controls.Add(nx); Controls.Add(ny);

            // physical directions; the value is the change to the printer-frame number
            var defs = new object[,] {
                { "Left 10", -10.0, 0.0 }, { "Left 1", -1.0, 0.0 }, { "Right 1", 1.0, 0.0 }, { "Right 10", 10.0, 0.0 },
                { "Fwd 10", 0.0, -10.0 }, { "Fwd 1", 0.0, -1.0 }, { "Back 1", 0.0, 1.0 }, { "Back 10", 0.0, 10.0 } };
            buttons = new FlatButton[9];
            for (int i = 0; i < 8; i++)
            {
                double dx = (double)defs[i, 1] * sign, dy = (double)defs[i, 2] * sign;
                int col = i % 4, row = i / 4;
                buttons[i] = Btn((string)defs[i, 0], 140 + col * 84, 82 + row * 54, 78, 34, BtnKind.Secondary, (s, e) => Go(dx, dy));
            }
            buttons[8] = Btn("Move to the numbers", 140, 190, 200, 32, BtnKind.Ghost, (s, e) => Go(0, 0));
            status = L("", 20, 236, 460, 36, Theme.Small, Theme.Muted);
            var save = Btn("Save as centre", 236, 282, 136, 34, BtnKind.Primary, (s, e) => { X = (double)nx.Value; Y = (double)ny.Value; DialogResult = DialogResult.OK; Close(); });
            Btn("Close", 380, 282, 100, 34, BtnKind.Secondary, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            Shown += (s, e) => Go(0, 0);
            tools.BusyChanged += Sync;
            FormClosed += (s, e) => tools.BusyChanged -= Sync;
        }

        void Sync()
        {
            if (IsDisposed) return;
            bool free = !tools.BusyUser;
            foreach (var b in buttons) b.Enabled = free;
        }

        void Go(double dx, double dy)
        {
            if (tools.BusyUser) return;
            nx.Value = Math.Max(nx.Minimum, Math.Min(nx.Maximum, nx.Value + (decimal)dx));
            ny.Value = Math.Max(ny.Minimum, Math.Min(ny.Maximum, ny.Value + (decimal)dy));
            var args = new System.Collections.Generic.List<string> { "box", "goto", ((double)nx.Value).ToString("0.###", Inv), ((double)ny.Value).ToString("0.###", Inv) };
            if (homed) args.Add("nohome");
            status.Text = homed ? "Moving..." : "Homing, then moving (about 30 s)...";
            tools.Run("up3dcenter.exe", args.ToArray(), (code, output) =>
            {
                if (IsDisposed) return;
                if (code == 0) { homed = true; status.Text = "Nozzle at printer X " + ((double)nx.Value).ToString("0.#", Inv) + ", Y " + ((double)ny.Value).ToString("0.#", Inv) + "."; }
                else { status.Text = "Move failed - see the Console page."; main.LogFromDialog(output.Trim()); }
            });
        }
    }

    /// <summary>Nozzle height calibration: home the way a print does, raise the bed under the
    /// nozzle until a sheet of paper drags, and read the bed position back from the printer.
    /// The result is in the converter's own Z frame (nozzle height = how far the bed rises).</summary>
    public class NozzleDialog : StyledDialog
    {
        public double Result;
        const double Paper = 0.10;          // sheet of printer paper
        const double FastTo = 200.0;        // below UP Studio's figure (206) and the real one (~230)
        const double BigStepMax = 200.0;    // 10 mm steps stop here
        const double MidStepMax = 220.0;    // 5 mm steps stop here
        const double Travel = 250.0;        // never ask for more than this
        readonly ToolRunner tools;
        readonly MainForm main;
        readonly double cx, cy, current;
        readonly Lbl readout, hint, status, result;
        readonly FlatButton start, fast, save;
        readonly FlatButton[] moves;
        bool ready;                          // homed and over the centre
        double raised = 0;                   // bed rise, mm, from the printer (bed up = positive)
        double tracked = 0;                  // sum of the moves we asked for
        double raised0 = double.NaN;         // reading right after homing
        bool havePrinterZ;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Font Huge = new Font("Segoe UI Semibold", 24f);

        public NozzleDialog(MainForm main, ToolRunner tools, double cx, double cy, double current) : base("Calibrate nozzle height", 560, 520)
        {
            this.main = main; this.tools = tools; this.cx = cx; this.cy = cy; this.current = current;
            L("Measures how far the bed travels up to the nozzle, homing the same way a print does.", 20, 14, 520, 20, Theme.Base, Theme.Ink);
            L("Before you start: nozzle cold with no plastic on the tip, bed clear, a sheet of printer paper ready.", 20, 36, 520, 36, Theme.Small, Theme.Muted);
            start = Btn("1.  Home and move to the bed centre", 20, 76, 300, 34, BtnKind.Primary, (s, e) => Begin());

            L("BED RAISED", 20, 126, 200, 18, Theme.Small, Theme.Muted);
            readout = L("--", 20, 144, 300, 50, Huge, Theme.Ink);
            L("mm above Z home, read from the printer", 20, 194, 300, 18, Theme.Small, Theme.Faint);

            fast = Btn("Fast to " + FastTo.ToString("0", Inv) + " mm", 340, 150, 200, 34, BtnKind.Secondary, (s, e) => MoveBed(FastTo - raised));

            var defs = new object[,] {
                { "Up 10", 10.0 }, { "Up 5", 5.0 }, { "Up 1", 1.0 }, { "Up 0.1", 0.1 }, { "Up 0.05", 0.05 },
                { "Down 10", -10.0 }, { "Down 5", -5.0 }, { "Down 1", -1.0 }, { "Down 0.1", -0.1 }, { "Down 0.05", -0.05 } };
            moves = new FlatButton[10];
            L("2.  Raise the bed. Slide the paper between nozzle and bed as you go.", 20, 228, 520, 18, Theme.Base, Theme.Ink);
            for (int i = 0; i < 10; i++)
            {
                double d = (double)defs[i, 1];
                int col = i % 5, row = i / 5;
                moves[i] = Btn((string)defs[i, 0], 20 + col * 104, 254 + row * 44, 98, 36, row == 0 ? BtnKind.Secondary : BtnKind.Ghost, (s, e) => MoveBed(d));
            }
            hint = L("", 20, 344, 520, 36, Theme.Small, Theme.Muted);
            L("3.  When the paper just drags under the nozzle, press Save.", 20, 386, 520, 18, Theme.Base, Theme.Ink);
            result = L("", 20, 408, 520, 36, Theme.Small, Theme.Muted);
            status = L("", 20, 446, 520, 20, Theme.Small, Theme.Faint);
            save = Btn("Save nozzle height", 280, 474, 160, 34, BtnKind.Primary, (s, e) => DoSave());
            Btn("Close", 448, 474, 92, 34, BtnKind.Secondary, (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            FormClosing += (s, e) => LowerOnClose();
            tools.BusyChanged += Sync;
            FormClosed += (s, e) => tools.BusyChanged -= Sync;
            Refresh2();
        }

        void Sync() { if (!IsDisposed) Refresh2(); }

        void Refresh2()
        {
            bool free = !tools.BusyUser;
            start.Enabled = free;
            fast.Enabled = free && ready && raised < FastTo - 0.5;
            foreach (var b in moves) b.Enabled = free && ready;
            moves[0].Enabled = free && ready && raised + 10 <= BigStepMax + 0.01;
            moves[1].Enabled = free && ready && raised + 5 <= MidStepMax + 0.01;
            save.Enabled = free && ready && havePrinterZ && raised > 50;
            readout.Text = ready && havePrinterZ ? raised.ToString("0.00", Inv) : "--";
            if (!ready) hint.Text = "Press step 1 first.";
            else if (raised < FastTo - 0.5) hint.Text = "Use \"Fast to " + FastTo.ToString("0", Inv) + " mm\", then 5 mm steps while you watch the gap.";
            else hint.Text = "Watch the gap: 5 mm steps while it's big (they stop at " + MidStepMax.ToString("0", Inv) + " mm), 1 mm until it's about 1 mm, then 0.1 and 0.05 until the paper just drags.";
            if (ready && havePrinterZ && raised > 50)
                result.Text = "Nozzle height would be " + (raised + Paper).ToString("0.00", Inv) + " mm (bed raised + " + Paper.ToString("0.00", Inv) +
                              " mm for the paper). Currently set: " + current.ToString("0.00", Inv) + " mm.";
            else result.Text = "";
        }

        void Begin()
        {
            if (tools.BusyUser) return;
            ready = false; havePrinterZ = false; raised = 0; tracked = 0; raised0 = double.NaN;
            status.Text = "Homing, then moving to the centre (about 30 s)...";
            var args = new[] { "box", "goto", cx.ToString("0.###", Inv), cy.ToString("0.###", Inv) };
            tools.Run("up3dcenter.exe", args, (code, output) =>
            {
                if (IsDisposed) return;
                if (code != 0) { status.Text = "Homing failed - see the Console page."; main.LogFromDialog(output.Trim()); Refresh2(); return; }
                ready = true;
                ReadZ("Nozzle over the bed centre. Bed at the bottom.");
            });
            Refresh2();
        }

        void MoveBed(double up)
        {
            if (tools.BusyUser || !ready || Math.Abs(up) < 0.001) return;
            if (up > 0 && raised + up > Travel) { status.Text = "That's beyond the bed's travel - check what's happening at the nozzle."; return; }
            if (up >= 10 && raised + up > BigStepMax + 0.01 && Math.Abs(raised + up - FastTo) > 0.01) { status.Text = "Use 5 mm or smaller steps from here."; return; }
            if (up >= 5 && up < 10 && raised + up > MidStepMax + 0.01) { status.Text = "Use 1 mm or smaller steps from here."; return; }
            if (up < 0 && raised + up < 0) up = -raised;
            if (Math.Abs(up) < 0.001) return;
            tracked += up;
            status.Text = (up > 0 ? "Raising" : "Lowering") + " the bed " + Math.Abs(up).ToString("0.##", Inv) + " mm...";
            // up3dctl jog: Z is the machine axis, bed up = negative
            tools.Run("up3dctl.exe", new[] { "box", "jog", "0", "0", (-up).ToString("0.###", Inv) }, (code, output) =>
            {
                if (IsDisposed) return;
                if (code != 0) { status.Text = "Move failed - see the Console page."; main.LogFromDialog(output.Trim()); Refresh2(); return; }
                ReadZ(null);
            });
            Refresh2();
        }

        void ReadZ(string done)
        {
            tools.Run("up3dstatus.exe", new[] { "pos" }, (code, output) =>
            {
                if (IsDisposed) return;
                var m = System.Text.RegularExpressions.Regex.Match(output, @"POS\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)");
                if (code == 0 && m.Success)
                {
                    raised = -double.Parse(m.Groups[3].Value, Inv);
                    havePrinterZ = true;
                    if (double.IsNaN(raised0)) raised0 = raised;
                    status.Text = done ?? ("Bed at " + raised.ToString("0.00", Inv) + " mm.");
                    if (ready && Math.Abs(raised - raised0 - tracked) > 0.5)
                        status.Text += "  (Moves asked for add up to " + tracked.ToString("0.00", Inv) + " mm.)";
                }
                else { status.Text = "Couldn't read the bed position - see the Console page."; main.LogFromDialog(output.Trim()); }
                Refresh2();
            });
        }

        void DoSave()
        {
            if (!havePrinterZ) return;
            double h = Math.Round(raised + Paper, 2);
            if (Math.Abs(raised - raised0 - tracked) > 0.5)
            {
                var r = MessageBox.Show(this, "The printer reports the bed raised " + raised.ToString("0.00", Inv) + " mm, but the moves asked for add up to " +
                    tracked.ToString("0.00", Inv) + " mm.\n\nSave the printer's figure (" + h.ToString("0.00", Inv) + " mm) anyway?",
                    "Check the measurement", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
            Result = h;
            DialogResult = DialogResult.OK;
            Close();
        }

        bool lowered;
        void LowerOnClose()
        {
            // move the bed clear of the nozzle when leaving
            if (lowered || !ready || raised < 1) return;
            lowered = true;
            double down = Math.Min(10.0, raised);
            main.LogFromDialog("Lowering the bed " + down.ToString("0.#", Inv) + " mm away from the nozzle.");
            tools.Run("up3dctl.exe", new[] { "box", "jog", "0", "0", down.ToString("0.###", Inv) }, (c, o) => { });
        }
    }

    /// <summary>Two-field input box (text + number).</summary>
    public static class Ask
    {
        public static bool TwoFields(IWin32Window owner, string title, string label1, ref string value1, string label2, ref double value2)
        {
            using (var d = new StyledDialog(title, 420, 230))
            {
                var l1 = new Lbl(label1, Theme.Small, Theme.Muted) { Location = new Point(20, 18), Size = new Size(380, 18) };
                var t1 = new TextBox { Location = new Point(20, 38), Width = 380, Text = value1, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Ink };
                var l2 = new Lbl(label2, Theme.Small, Theme.Muted) { Location = new Point(20, 76), Size = new Size(380, 36) };
                var n2 = new NumericUpDown { Location = new Point(20, 114), Width = 140, Minimum = 0, Maximum = 10000, DecimalPlaces = 0, Increment = 10,
                    Value = (decimal)Math.Max(0, Math.Min(10000, value2)), BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Ink };
                var ok = new FlatButton { Text = "Save", Kind = BtnKind.Primary, Font = Theme.Bold, Location = new Point(196, 176), Size = new Size(100, 34) };
                var cancel = new FlatButton { Text = "Cancel", Kind = BtnKind.Secondary, Location = new Point(304, 176), Size = new Size(96, 34) };
                ok.Click += (s, e) => { d.DialogResult = DialogResult.OK; d.Close(); };
                cancel.Click += (s, e) => { d.DialogResult = DialogResult.Cancel; d.Close(); };
                d.Controls.AddRange(new Control[] { l1, t1, l2, n2, ok, cancel });
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
                value1 = t1.Text; value2 = (double)n2.Value;
                return true;
            }
        }
    }
}
