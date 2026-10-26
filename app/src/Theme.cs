using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpenBoxBridge
{
    /// <summary>Colours and fonts for the whole app (light or dark).</summary>
    public static class Theme
    {
        public static bool Dark;

        public static Color Bg, Surface, Surface2, Border, BorderStrong, Ink, Muted, Faint;
        public static Color Accent, AccentHover, AccentSoft, Good, Warn, Bad, BadHover, Idle;
        public static Color Header, HeaderInk, HeaderMuted, HeaderButton, HeaderButtonHover;
        public static Color LogBg, LogInk;
        public static Color SeriesNozzle, SeriesBed, Grid;

        public static Font Base, Small, Caption, Bold, Big, Tile, Title, Icon, IconSmall, Mono;

        public static void Init(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                Bg = Hex("#EEF2F6"); Surface = Hex("#FFFFFF"); Surface2 = Hex("#F6F8FB");
                Border = Hex("#E2E8F0"); BorderStrong = Hex("#CBD5E1");
                Ink = Hex("#0F172A"); Muted = Hex("#64748B"); Faint = Hex("#94A3B8");
                LogBg = Hex("#0B1220"); LogInk = Hex("#CBD5E1");
                Grid = Hex("#EEF2F7");
            }
            else
            {
                Bg = Hex("#0B1018"); Surface = Hex("#141B26"); Surface2 = Hex("#1A2331");
                Border = Hex("#243044"); BorderStrong = Hex("#334155");
                Ink = Hex("#E5EAF2"); Muted = Hex("#94A3B8"); Faint = Hex("#64748B");
                LogBg = Hex("#070B11"); LogInk = Hex("#CBD5E1");
                Grid = Hex("#1E2938");
            }
            Accent = Hex("#2563EB"); AccentHover = Hex("#1D4ED8"); AccentSoft = dark ? Hex("#1E3A8A") : Hex("#DBEAFE");
            Good = Hex("#16A34A"); Warn = Hex("#D97706"); Bad = Hex("#DC2626"); BadHover = Hex("#B91C1C"); Idle = Hex("#64748B");
            Header = Hex("#0F172A"); HeaderInk = Color.White; HeaderMuted = Hex("#94A3B8");
            HeaderButton = Hex("#1E293B"); HeaderButtonHover = Hex("#334155");
            SeriesNozzle = Hex("#F97316"); SeriesBed = Hex("#3B82F6");

            Base = new Font("Segoe UI", 9.75f);
            Small = new Font("Segoe UI", 8.25f);
            Caption = new Font("Segoe UI Semibold", 8.25f);
            Bold = new Font("Segoe UI Semibold", 10.5f);
            Big = new Font("Segoe UI Semibold", 15f);
            Tile = new Font("Segoe UI Semibold", 14f);
            Title = new Font("Segoe UI Semibold", 14f);
            Icon = new Font("Segoe MDL2 Assets", 16f);
            IconSmall = new Font("Segoe MDL2 Assets", 11f);
            Mono = new Font("Consolas", 9f);
        }

        public static Color Hex(string h) { return ColorTranslator.FromHtml(h); }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t <= 0) return a; if (t >= 1) return b;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Segoe MDL2 Assets glyphs
        public const string GPrinter = "", GHome = "", GFolder = "", GStop = "",
            GPower = "", GRefresh = "", GUp = "", GDown = "", GLeft = "",
            GRight = "", GDelete = "", GPlay = "", GHeat = "", GSnow = "",
            GSettings = "", GList = "", GDash = "", GConsole = "", GCube = "",
            GTarget = "", GSpool = "", GCheck = "", GWarn = "";
    }
}
