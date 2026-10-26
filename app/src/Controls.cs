using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace OpenBoxBridge
{
    /// <summary>Double-buffered base for custom-painted controls.</summary>
    public class Smooth : Control
    {
        public Smooth()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }
        /// <summary>Colour behind this control (cards paint their own white surface).</summary>
        protected Color Under()
        {
            if (Parent is Card) return Theme.Surface;
            return Parent != null ? Parent.BackColor : Theme.Surface;
        }
        protected float K { get { return DeviceDpi / 96f; } }
        protected static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
        protected static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Rounded white card with an optional caption. Children sit on Surface colour.</summary>
    public class Card : Panel
    {
        public string Caption = "";
        public int Radius = 10;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Padding = new Padding(16, 36, 16, 14);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Radius * DeviceDpi / 96f;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 2.5f);
            using (var shadow = Theme.Round(new RectangleF(0.5f, 1.8f, Width - 1.5f, Height - 2.5f), rad))
            using (var sb = new SolidBrush(Color.FromArgb(Theme.Dark ? 60 : 18, 15, 23, 42)))
                g.FillPath(sb, shadow);
            using (var path = Theme.Round(r, rad))
            using (var b = new SolidBrush(Theme.Surface))
            using (var p = new Pen(Theme.Border, 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
            float k = DeviceDpi / 96f;
            if (!string.IsNullOrEmpty(Caption))
                TextRenderer.DrawText(g, Caption.ToUpperInvariant(), Theme.Caption, new Point((int)(16 * k), (int)(12 * k)), Theme.Muted, TextFormatFlags.NoPadding);
        }
    }

    public enum BtnKind { Secondary, Primary, Danger, Stop, Header, Ghost, Nav }

    /// <summary>Rounded flat button with an animated hover.</summary>
    public class FlatButton : Smooth
    {
        public BtnKind Kind = BtnKind.Secondary;
        public string Glyph = "";
        public bool Selected;
        public int Radius = 7;
        float hover, press;
        bool over, down;
        readonly Timer anim = new Timer { Interval = 15 };

        public FlatButton()
        {
            Cursor = Cursors.Hand;
            Font = Theme.Base;
            anim.Tick += delegate
            {
                float th = over ? 1 : 0, tp = down ? 1 : 0;
                hover += (th - hover) * 0.35f; press += (tp - press) * 0.5f;
                if (Math.Abs(th - hover) < 0.02f && Math.Abs(tp - press) < 0.02f) { hover = th; press = tp; anim.Stop(); }
                Invalidate();
            };
            TabStop = true;
        }
        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
        protected override void OnMouseEnter(EventArgs e) { over = true; anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { over = false; down = false; anim.Start(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; anim.Start(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; anim.Start(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        void Colors(out Color bg, out Color fg, out Color border)
        {
            switch (Kind)
            {
                case BtnKind.Primary:
                    bg = Theme.Blend(Theme.Accent, Theme.AccentHover, hover); fg = Color.White; border = Color.Empty; break;
                case BtnKind.Stop:
                    bg = Theme.Blend(Theme.Bad, Theme.BadHover, hover); fg = Color.White; border = Color.Empty; break;
                case BtnKind.Danger:
                    bg = Theme.Blend(Theme.Surface, Theme.Dark ? Theme.Hex("#3B1215") : Theme.Hex("#FEF2F2"), hover);
                    fg = Theme.Bad; border = Theme.Dark ? Theme.Hex("#7F1D1D") : Theme.Hex("#FECACA"); break;
                case BtnKind.Header:
                    bg = Theme.Blend(Theme.HeaderButton, Theme.HeaderButtonHover, hover); fg = Color.White; border = Theme.Hex("#334155"); break;
                case BtnKind.Nav:
                    bg = Selected ? Theme.Hex("#1E293B") : Theme.Blend(Theme.Header, Theme.Hex("#172033"), hover);
                    fg = Selected ? Color.White : Theme.HeaderMuted; border = Color.Empty; break;
                case BtnKind.Ghost:
                    bg = Theme.Blend(Theme.Surface, Theme.Surface2, hover); fg = Theme.Accent; border = Color.Empty; break;
                default:
                    bg = Theme.Blend(Theme.Surface, Theme.Surface2, hover); fg = Theme.Ink; border = Theme.BorderStrong; break;
            }
            if (press > 0) bg = Theme.Blend(bg, Color.Black, press * 0.12f);
            if (!Enabled) { bg = Theme.Blend(bg, Theme.Surface2, 0.55f); fg = Theme.Blend(fg, Theme.Faint, 0.7f); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Hq(g);
            g.Clear(Under());
            Color bg, fg, border; Colors(out bg, out fg, out border);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Radius))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            if (Kind == BtnKind.Nav && Selected)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 10, Height - 3, Width - 20, 3);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (!string.IsNullOrEmpty(Glyph) && !string.IsNullOrEmpty(Text))
            {
                Size ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
                Size gs = TextRenderer.MeasureText(g, Glyph, Theme.IconSmall, Size.Empty, TextFormatFlags.NoPadding);
                int total = gs.Width + 6 + ts.Width;
                int x = Math.Max(6, (Width - total) / 2);
                TextRenderer.DrawText(g, Glyph, Theme.IconSmall, new Rectangle(x, 0, gs.Width + 2, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gs.Width + 6, 0, Width - x - gs.Width - 8, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            else if (!string.IsNullOrEmpty(Glyph))
                TextRenderer.DrawText(g, Glyph, Theme.IconSmall, ClientRectangle, fg, flags);
            else
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, flags);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Accent) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, 3, 3, Width - 7, Height - 7);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }
    }

    /// <summary>Rounded "pill" status badge.</summary>
    public class Pill : Smooth
    {
        public Color Fill = Theme.Idle;
        public Pill() { Font = Theme.Caption; }
        public void Set(string text, Color c) { Text = text; Fill = c; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Hq(g);
            g.Clear(Under());
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
            using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
            int d = Math.Max(6, Height / 4);
            using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillEllipse(b, 12, (Height - d) / 2f, d, d);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(12 + d + 6, 0, Width - d - 24, Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Small stat tile: big value, caption underneath.</summary>
    public class Tile : Smooth
    {
        public string Value = "-", Sub = "";
        public Color SubColor = Color.Empty, ValueColor = Color.Empty, Dot = Color.Empty;
        public Tile() { }
        public void Set(string value, string sub, Color subColor) { Value = value; Sub = sub; SubColor = subColor; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Hq(g);
            g.Clear(Under());
            using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
            using (var b = new SolidBrush(Theme.Surface2))
            using (var p = new Pen(Theme.Border)) { g.FillPath(b, path); g.DrawPath(p, path); }
            float k = K;
            int x = (int)(12 * k);
            if (Dot != Color.Empty) using (var b = new SolidBrush(Dot)) g.FillEllipse(b, 12 * k, Height * 0.25f - 4 * k, 8 * k, 8 * k);
            if (Dot != Color.Empty) x = (int)(26 * k);
            TextRenderer.DrawText(g, Value, Theme.Tile, new Rectangle(x, 0, Width - x - (int)(6 * k), Height / 2 + (int)(6 * k)), ValueColor == Color.Empty ? Theme.Ink : ValueColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, string.IsNullOrEmpty(Sub) ? Text : Sub, Theme.Small, new Rectangle((int)(12 * k), Height / 2 + (int)(6 * k), Width - (int)(18 * k), Height / 2 - (int)(8 * k)),
                SubColor == Color.Empty ? Theme.Muted : SubColor, TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Rounded progress bar that glides to its value.</summary>
    public class Progress : Smooth
    {
        float shown, target;
        public Color Fill = Theme.Accent;
        readonly Timer anim = new Timer { Interval = 15 };
        public Progress()
        {
            anim.Tick += delegate
            {
                shown += (target - shown) * 0.18f;
                if (Math.Abs(target - shown) < 0.1f) { shown = target; anim.Stop(); }
                Invalidate();
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
        public float Value
        {
            get { return target; }
            set { float v = Math.Max(0, Math.Min(100, value)); if (v != target) { target = v; if (v < shown - 1) shown = v; anim.Start(); } }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Hq(g);
            g.Clear(Under());
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, Height / 2f)) using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, path);
            float w = (Width - 1) * shown / 100f;
            if (w >= 2)
                using (var path = Theme.Round(new RectangleF(0, 0, Math.Max(w, Height), Height - 1), Height / 2f))
                using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
        }
    }

    /// <summary>Plain label painted on the card surface (no flicker, no transparency tricks).</summary>
    public class Lbl : Label
    {
        public Lbl(string text, Font f, Color c)
        {
            base.Text = text; Font = f; ForeColor = c; BackColor = Theme.Surface; AutoEllipsis = true; UseMnemonic = false;
        }
    }

    /// <summary>One sample for the temperature chart.</summary>
    public struct TempSample
    {
        public DateTime T; public float Nozzle, NozzleTarget, Bed, BedTarget;
    }

    /// <summary>Fluidd-style temperature chart: actual (solid) and target (dashed) for nozzle and bed.</summary>
    public class TempChart : Smooth
    {
        public readonly List<TempSample> Data = new List<TempSample>();
        public int Minutes = 10;
        int mouseX = -1;
        public TempChart() { Font = Theme.Small; }
        public void Add(TempSample s)
        {
            Data.Add(s);
            DateTime cut = DateTime.Now.AddMinutes(-60);
            while (Data.Count > 0 && Data[0].T < cut) Data.RemoveAt(0);
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e) { mouseX = e.X; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { mouseX = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Hq(g);
            g.Clear(Under());
            float k = K; int left = (int)(38 * k), right = (int)(8 * k), top = (int)(8 * k), bottom = (int)(22 * k);
            var plot = new Rectangle(left, top, Math.Max(10, Width - left - right), Math.Max(10, Height - top - bottom));
            DateTime now = DateTime.Now, start = now.AddMinutes(-Minutes);

            float maxV = 100;
            foreach (var s in Data) if (s.T >= start) maxV = Math.Max(maxV, Math.Max(Math.Max(s.Nozzle, s.NozzleTarget), Math.Max(s.Bed, s.BedTarget)));
            maxV = (float)(Math.Ceiling((maxV + 10) / 50.0) * 50);

            using (var gp = new Pen(Theme.Grid))
                for (int v = 0; v <= maxV; v += 50)
                {
                    float y = plot.Bottom - plot.Height * v / maxV;
                    g.DrawLine(gp, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, v.ToString(), Font, new Rectangle(0, (int)(y - 8 * k), left - (int)(6 * k), (int)(16 * k)), Theme.Faint,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            for (int m = 0; m <= Minutes; m += Minutes <= 10 ? 2 : 5)
            {
                float x = plot.Right - plot.Width * m / (float)Minutes;
                TextRenderer.DrawText(g, m == 0 ? "now" : "-" + m + "m", Font, new Rectangle((int)(x - 22 * k), plot.Bottom + (int)(4 * k), (int)(44 * k), (int)(16 * k)), Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }

            Func<DateTime, float> X = t => plot.Right - (float)((now - t).TotalSeconds / (Minutes * 60.0)) * plot.Width;
            Func<float, float> Y = v => plot.Bottom - plot.Height * Math.Max(0, v) / maxV;

            DrawSeries(g, plot, start, X, Y, s => s.BedTarget, Theme.SeriesBed, true);
            DrawSeries(g, plot, start, X, Y, s => s.NozzleTarget, Theme.SeriesNozzle, true);
            DrawSeries(g, plot, start, X, Y, s => s.Bed, Theme.SeriesBed, false);
            DrawSeries(g, plot, start, X, Y, s => s.Nozzle, Theme.SeriesNozzle, false);

            if (Data.Count == 0)
                TextRenderer.DrawText(g, "Waiting for the printer...", Theme.Base, plot, Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // hover read-out
            if (mouseX >= plot.Left && mouseX <= plot.Right && Data.Count > 0)
            {
                DateTime t = now.AddSeconds(-(plot.Right - mouseX) / (double)plot.Width * Minutes * 60);
                TempSample best = Data[0]; double bd = double.MaxValue;
                foreach (var s in Data) { double d = Math.Abs((s.T - t).TotalSeconds); if (d < bd) { bd = d; best = s; } }
                if (bd < 30)
                {
                    float x = X(best.T);
                    using (var p = new Pen(Theme.BorderStrong) { DashStyle = DashStyle.Dash }) g.DrawLine(p, x, plot.Top, x, plot.Bottom);
                    string txt = string.Format("{0:HH:mm:ss}   nozzle {1:0}° / {2:0}°   bed {3:0}° / {4:0}°",
                        best.T, best.Nozzle, best.NozzleTarget, best.Bed, best.BedTarget);
                    Size sz = TextRenderer.MeasureText(txt, Font);
                    int bx = (int)Math.Min(Math.Max(plot.Left, x - sz.Width / 2), plot.Right - sz.Width - 8);
                    var box = new Rectangle(bx, plot.Top + 2, sz.Width + 8, sz.Height + 4);
                    using (var path = Theme.Round(box, 5)) using (var b = new SolidBrush(Theme.Dark ? Theme.Hex("#0F172A") : Color.FromArgb(240, 15, 23, 42)))
                        g.FillPath(b, path);
                    TextRenderer.DrawText(g, txt, Font, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        void DrawSeries(Graphics g, Rectangle plot, DateTime start, Func<DateTime, float> X, Func<float, float> Y,
                        Func<TempSample, float> val, Color c, bool dashed)
        {
            var pts = new List<PointF>();
            bool any = false;
            foreach (var s in Data)
            {
                if (s.T < start.AddSeconds(-10)) continue;
                float v = val(s);
                if (v > 0) any = true;
                pts.Add(new PointF(Math.Max(plot.Left, X(s.T)), Y(v)));
            }
            if (!any || pts.Count < 2) return;
            var clip = g.Clip; g.SetClip(plot);
            using (var p = new Pen(dashed ? Color.FromArgb(150, c) : c, dashed ? 1.3f : 2f))
            {
                if (dashed) p.DashStyle = DashStyle.Dash;
                p.LineJoin = LineJoin.Round;
                if (dashed) g.DrawLines(p, pts.ToArray());
                else
                {
                    // soft fill under the actual-temperature line
                    var poly = new List<PointF>(pts);
                    poly.Add(new PointF(pts[pts.Count - 1].X, plot.Bottom));
                    poly.Add(new PointF(pts[0].X, plot.Bottom));
                    using (var lg = new LinearGradientBrush(plot, Color.FromArgb(45, c), Color.FromArgb(0, c), LinearGradientMode.Vertical))
                        g.FillPolygon(lg, poly.ToArray());
                    g.DrawLines(p, pts.ToArray());
                }
            }
            g.Clip = clip;
        }
    }

    /// <summary>Owner-drawn list of jobs with thumbnails.</summary>
    public class JobList : ListBox
    {
        public JobList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 72;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface;
            IntegralHeight = false;
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var job = Items[e.Index] as JobEntry;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 4, e.Bounds.Width - 8, e.Bounds.Height - 8);
            using (var path = Theme.Round(r, 8))
            using (var b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Surface2))
            using (var p = new Pen(sel ? Theme.Accent : Theme.Border)) { g.FillPath(b, path); g.DrawPath(p, path); }
            if (job == null) return;
            var tr = new Rectangle(r.X + 8, r.Y + 6, r.Height - 12, r.Height - 12);
            using (var path = Theme.Round(tr, 6)) using (var b = new SolidBrush(Theme.Dark ? Theme.Hex("#0F172A") : Theme.Hex("#E2E8F0"))) g.FillPath(b, path);
            if (job.Thumb != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(job.Thumb, Fit(job.Thumb.Size, tr));
            }
            else TextRenderer.DrawText(g, Theme.GCube, Theme.Icon, tr, Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int x = tr.Right + 12;
            TextRenderer.DrawText(g, job.Name, Theme.Bold, new Rectangle(x, r.Y + 10, r.Right - x - 10, 22), Theme.Ink, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, job.Line2, Theme.Small, new Rectangle(x, r.Y + 34, r.Right - x - 10, 18), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        static Rectangle Fit(Size s, Rectangle box)
        {
            float k = Math.Min(box.Width / (float)s.Width, box.Height / (float)s.Height);
            int w = (int)(s.Width * k), h = (int)(s.Height * k);
            return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }
    }

    public class JobEntry
    {
        public string Path, Name, Line2;
        public Image Thumb;
        public override string ToString() { return Name; }
    }

    /// <summary>Picture with rounded frame for the job thumbnail.</summary>
    public class Thumb : Smooth
    {
        static readonly Font BigIcon = new Font("Segoe MDL2 Assets", 26f);
        public Image Image;
        public Thumb() { }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Hq(g);
            g.Clear(Under());
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 10))
            {
                using (var b = new SolidBrush(Theme.Surface2)) g.FillPath(b, path);
                if (Image != null)
                {
                    g.SetClip(path);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float k = Math.Min((Width - 12) / (float)Image.Width, (Height - 12) / (float)Image.Height);
                    int w = (int)(Image.Width * k), h = (int)(Image.Height * k);
                    g.DrawImage(Image, (Width - w) / 2, (Height - h) / 2, w, h);
                    g.ResetClip();
                }
                else TextRenderer.DrawText(g, Theme.GCube, BigIcon, ClientRectangle, Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
        }
    }
}
