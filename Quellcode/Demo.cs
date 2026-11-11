// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>
    /// Erzeugt die Screenshots für die README mit Beispieldaten (Standardeinstellungen, nichts wird gespeichert).
    /// Aufruf: WindowGrid.exe --screenshots [Zielordner]. Die echte WindowGrid.ini, der Autostart und
    /// das Windows-Andocken werden dabei nicht angefasst.
    /// Ablage: hell.png / dunkel.png (Einstellungen) und &lt;name&gt;-hell.png / &lt;name&gt;-dunkel.png.
    /// Fenster und Menü werden einmal vor schwarzem und einmal vor weißem Hintergrund aufgenommen; daraus
    /// ergeben sich durchsichtige runde Ecken und der Windows-Schatten. Das Raster wird nicht vom Bildschirm
    /// abfotografiert, sondern über einem neutralen Beispiel-Desktop gezeichnet.
    /// </summary>
    internal static class Demo
    {
        /// <summary>true, solange die Screenshots erstellt werden.</summary>
        public static bool Active;

        /// <summary>Programmordner, der im Info-Fenster angezeigt wird (statt des echten, privaten Pfads).</summary>
        public const string ProgramPath = @"C:\Tools\WindowGrid";

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public static bool RunScreenshots(string outDir)
        {
            try
            {
                Active = true;
                Directory.CreateDirectory(outDir);
                try { File.Delete(Path.Combine(outDir, "Fehler.txt")); } catch { }

                Settings settings = new Settings(); // Standardwerte, wird nie gespeichert
                settings.FirstRun = false;
                Theme.Accent = settings.Accent;
                Icon icon = TrayContext.LoadIcon(32);

                // Maus aus dem Bild (rechts unten auf dem Hauptbildschirm)
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Cursor.Position = new Point(area.Right - 2, area.Bottom - 2);

                using (Backdrop backdrop = new Backdrop())
                {
                    // Hintergrundfläche einmal vorab anzeigen: das erste Anzeigen schließt sonst ein offenes Menü
                    backdrop.Cover(new Rectangle(area.Right - 2, area.Bottom - 2, 1, 1), Color.Black, IntPtr.Zero);
                    Pump(300);
                    backdrop.Hide();

                    foreach (bool dark in new[] { false, true })
                    {
                        Theme.Override = dark;
                        string suffix = dark ? "dunkel" : "hell";

                        // Einstellungen (Titelbild)
                        Shot(backdrop, delegate { return new SettingsForm(settings, icon); }, Path.Combine(outDir, suffix + ".png"));

                        // Tray-Menü
                        using (ContextMenuStrip menu = TrayContext.CreateDemoMenu(settings, icon))
                        {
                            Size size = menu.GetPreferredSize(Size.Empty);
                            Point pos = new Point(area.Left + (area.Width - size.Width) / 2, area.Top + Math.Max(0, (area.Height - size.Height) / 2));
                            menu.Show(pos);
                            Pump(800);
                            if (!menu.Visible) { menu.Show(pos); Pump(800); } // falls Windows es wieder geschlossen hat
                            Save(Capture(backdrop, menu.Handle, menu.Bounds, Margin(menu)), Path.Combine(outDir, "menue-" + suffix + ".png"));
                            menu.Close();
                            Pump(200);
                        }

                        Shot(backdrop, delegate { SettingsForm zf = new SettingsForm(settings, icon); zf.SelectPage(2); return zf; }, Path.Combine(outDir, "zonen-" + suffix + ".png"));
                        Shot(backdrop, delegate { return new AboutDialog(null); }, Path.Combine(outDir, "info-" + suffix + ".png"));

                        // Raster über dem Beispiel-Desktop
                        Save(GridShot(settings, dark, false), Path.Combine(outDir, "raster-" + suffix + ".png"));
                        Save(GridShot(settings, dark, true), Path.Combine(outDir, "zentriert-" + suffix + ".png"));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(outDir, "Fehler.txt"), ex.ToString()); } catch { }
                return false;
            }
            finally
            {
                Theme.Override = null;
                Active = false;
            }
        }

        // ------------------------------------------------------------------ Raster

        private const int DeskW = 1920, DeskH = 1080;
        private const float DeskScale = 1.25f;

        /// <summary>
        /// Raster wie beim Ziehen: Beispiel-Desktop mit einem Fenster, darüber das Overlay mit Auswahl,
        /// Größenkasten und freien Feldern zum Rand. Zentriert: Auswahl in der Bildschirmmitte.
        /// </summary>
        private static Bitmap GridShot(Settings settings, bool dark, bool centered)
        {
            GridGeometry geo = new GridGeometry(IntPtr.Zero, new Rectangle(0, 0, DeskW, DeskH), settings.Columns, settings.Rows, settings.Gap, DeskScale);

            Rectangle sel;
            int cells, rows;
            if (centered)
            {
                // 6 × 4 Felder genau in der Mitte: links/rechts und oben/unten gleich viele Felder frei
                Rectangle s = geo.Selection(0, 0, 5, 3);
                int half2 = geo.Gap / 2, half = geo.Gap - geo.Gap / 2;
                int left = geo.Xs[0] + half2, right = geo.Xs[geo.Columns] - half;
                int top = geo.Ys[0] + half2, bottom = geo.Ys[geo.Rows] - half;
                sel = new Rectangle(left + (right - left - s.Width) / 2, top + (bottom - top - s.Height) / 2, s.Width, s.Height);
                cells = 6; rows = 4;
            }
            else
            {
                sel = geo.Selection(2, 1, 6, 4);
                cells = 5; rows = 4;
            }

            Bitmap bmp = new Bitmap(DeskW, DeskH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                DrawDesktop(g, dark);
                // Das gezogene Fenster bleibt beim Ziehen an seiner alten Stelle
                DrawDemoWindow(g, new Rectangle(1010, 470, 760, 470), dark);

                using (OverlayWindow overlay = new OverlayWindow())
                {
                    overlay.Offscreen = true;
                    overlay.Prepare(geo, settings);
                    string hint = centered ? "Größe anpassen · zentriert" : "Größe anpassen";
                    string sub = "Raster " + geo.Columns + " × " + geo.Rows + "  ·  Mausrad feiner/gröber  ·  1–9 Schnellzonen  ·  Pfeile";
                    if (settings.CenterEnabled) sub += "  ·  " + KeyNames.Get(settings.CenterKey) + " zentrieren";
                    sub += "  ·  Esc aus";
                    string line1 = sel.Width + " × " + sel.Height;
                    string line2 = cells + " × " + rows + " Felder";
                    overlay.Render(sel, hint, sub, line1, line2, settings.ShowMargins ? geo.Margins(sel) : null);
                    overlay.DrawTo(g);
                }
            }
            return bmp;
        }

        /// <summary>Neutraler Beispiel-Desktop: ruhiger Farbverlauf mit weichen Lichtflecken.</summary>
        private static void DrawDesktop(Graphics g, bool dark)
        {
            Rectangle r = new Rectangle(0, 0, DeskW, DeskH);
            Color c1 = dark ? Color.FromArgb(18, 26, 48) : Color.FromArgb(170, 200, 236);
            Color c2 = dark ? Color.FromArgb(6, 10, 22) : Color.FromArgb(92, 140, 210);
            using (LinearGradientBrush b = new LinearGradientBrush(r, c1, c2, 35f)) g.FillRectangle(b, r);
            Glow(g, new RectangleF(-300, 380, 1300, 900), dark ? Color.FromArgb(120, 40, 90, 200) : Color.FromArgb(140, 255, 255, 255));
            Glow(g, new RectangleF(1050, -350, 1200, 900), dark ? Color.FromArgb(110, 30, 120, 210) : Color.FromArgb(120, 210, 235, 255));
        }

        private static void Glow(Graphics g, RectangleF r, Color center)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = center;
                    b.SurroundColors = new[] { Color.FromArgb(0, center) };
                    g.FillPath(b, p);
                }
            }
        }

        /// <summary>Beispielfenster (leerer Editor) im Windows-11-Stil.</summary>
        private static void DrawDemoWindow(Graphics g, Rectangle r, bool dark)
        {
            float sc = DeskScale;
            float radius = 8f * sc;

            // Schatten
            for (int i = 1; i <= 8; i++)
            {
                RectangleF sr = new RectangleF(r.X - i * 2, r.Y - i * 2 + 6, r.Width + i * 4, r.Height + i * 4);
                using (GraphicsPath sp = Theme.RoundRect(sr, radius + i * 2))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(dark ? 14 : 8, 0, 0, 0)))
                    g.FillPath(sb, sp);
            }

            Color back = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
            Color bar = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(238, 238, 238);
            Color border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(205, 205, 205);
            Color text = dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
            Color muted = dark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(110, 110, 110);
            Color line = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(222, 222, 222);
            int barH = (int)(40 * sc);

            using (GraphicsPath p = Theme.RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, p);
                Region old = g.Clip;
                g.SetClip(p);
                using (SolidBrush b = new SolidBrush(bar)) g.FillRectangle(b, r.X, r.Y, r.Width, barH);
                g.Clip = old;
                using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, p);
            }

            // Titelleiste: Symbol, Titel, Fensterknöpfe
            float cy = r.Y + barH / 2f;
            RectangleF ic = new RectangleF(r.X + 14 * sc, cy - 8 * sc, 16 * sc, 16 * sc);
            using (GraphicsPath ip = Theme.RoundRect(ic, 3f * sc))
            using (SolidBrush ib = new SolidBrush(Theme.Accent)) g.FillPath(ib, ip);
            using (Font f = Theme.UiFont(12f * sc, FontStyle.Regular))
            using (SolidBrush tb = new SolidBrush(text))
            {
                SizeF ts = g.MeasureString("Unbenannt – Editor", f);
                g.DrawString("Unbenannt – Editor", f, tb, ic.Right + 10 * sc, cy - ts.Height / 2f);
            }
            using (Pen pen = new Pen(muted, 1.2f * sc))
            {
                float bx = r.Right - 23 * sc, s = 5f * sc;
                g.DrawLine(pen, bx - s, cy - s, bx + s, cy + s);              // Schließen
                g.DrawLine(pen, bx - s, cy + s, bx + s, cy - s);
                bx -= 46 * sc;
                g.DrawRectangle(pen, bx - s, cy - s, 2 * s, 2 * s);             // Maximieren
                bx -= 46 * sc;
                g.DrawLine(pen, bx - s, cy, bx + s, cy);                       // Minimieren
            }

            // Menüzeile und ein paar Textzeilen als Platzhalter
            using (Font f = Theme.UiFont(11.5f * sc, FontStyle.Regular))
            using (SolidBrush tb = new SolidBrush(text))
            {
                float x = r.X + 16 * sc, y = r.Y + barH + 8 * sc;
                foreach (string m in new[] { "Datei", "Bearbeiten", "Ansicht" })
                {
                    g.DrawString(m, f, tb, x, y);
                    x += g.MeasureString(m, f).Width + 14 * sc;
                }
            }
            float ly = r.Y + barH + 44 * sc;
            using (Pen pen = new Pen(line, 1f)) g.DrawLine(pen, r.X + 1, ly - 8 * sc, r.Right - 1, ly - 8 * sc);
            using (SolidBrush lb = new SolidBrush(line))
            {
                int[] widths = { 70, 55, 82, 40, 64 };
                for (int i = 0; i < widths.Length; i++)
                {
                    RectangleF lr = new RectangleF(r.X + 18 * sc, ly + 8 * sc + i * 26 * sc, (r.Width - 36 * sc) * widths[i] / 100f, 9 * sc);
                    using (GraphicsPath lp = Theme.RoundRect(lr, 4.5f * sc)) g.FillPath(lb, lp);
                }
            }
        }

        // ------------------------------------------------------------------ Fenster und Menü aufnehmen

        private static void Shot(Backdrop backdrop, Func<Form> create, string file)
        {
            using (Form f = create())
            {
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.Show();
                f.TopMost = true;
                f.Activate();
                Pump(1500); // Aufbau und Zeichnen abwarten
                RECT r;
                Rectangle bounds = DwmGetWindowAttribute(f.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0
                    ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
                    : f.Bounds;
                Save(Capture(backdrop, f.Handle, bounds, Margin(f)), file);
                f.Close();
            }
            Pump(300);
        }

        /// <summary>Rand um das Fenster für den Schatten (24 Einheiten).</summary>
        private static int Margin(Control c)
        {
            try { using (Graphics g = c.CreateGraphics()) return (int)Math.Round(24 * g.DpiX / 96f); }
            catch { return 24; }
        }

        /// <summary>Nimmt den Bereich vor schwarzem und vor weißem Hintergrund auf und berechnet daraus die Durchsichtigkeit.</summary>
        private static Bitmap Capture(Backdrop backdrop, IntPtr target, Rectangle bounds, int margin)
        {
            Rectangle r = Rectangle.Inflate(bounds, margin, margin);
            backdrop.Cover(r, Color.Black, target);
            Pump(300);
            using (Bitmap onBlack = Grab(r))
            {
                backdrop.Cover(r, Color.White, target);
                Pump(300);
                using (Bitmap onWhite = Grab(r))
                {
                    backdrop.Hide();
                    return Combine(onBlack, onWhite);
                }
            }
        }

        private static Bitmap Grab(Rectangle r)
        {
            Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            return bmp;
        }

        private static Bitmap Combine(Bitmap onBlack, Bitmap onWhite)
        {
            int w = onBlack.Width, h = onBlack.Height;
            Rectangle rect = new Rectangle(0, 0, w, h);
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = onBlack.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData wd = onWhite.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData od = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int n = bd.Stride * h;
                byte[] b = new byte[n];
                byte[] wh = new byte[n];
                byte[] o = new byte[n];
                Marshal.Copy(bd.Scan0, b, 0, n);
                Marshal.Copy(wd.Scan0, wh, 0, n);
                for (int i = 0; i + 3 < n; i += 4)
                {
                    int diff = ((wh[i] - b[i]) + (wh[i + 1] - b[i + 1]) + (wh[i + 2] - b[i + 2])) / 3;
                    int a = Math.Max(0, Math.Min(255, 255 - diff));
                    if (a == 0) continue; // ganz durchsichtig
                    for (int c = 0; c < 3; c++) o[i + c] = (byte)Math.Min(255, b[i + c] * 255 / a);
                    o[i + 3] = (byte)a;
                }
                Marshal.Copy(o, 0, od.Scan0, n);
            }
            finally
            {
                onBlack.UnlockBits(bd);
                onWhite.UnlockBits(wd);
                result.UnlockBits(od);
            }
            return result;
        }

        private static void Save(Bitmap bmp, string file)
        {
            using (bmp) bmp.Save(file, ImageFormat.Png);
        }

        /// <summary>Wartet und verarbeitet dabei die Fensternachrichten (Zeichnen, Laden).</summary>
        private static void Pump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        /// <summary>Einfarbige Fläche hinter dem aufgenommenen Bereich.</summary>
        private sealed class Backdrop : Form
        {
            public Backdrop()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW
                    return cp;
                }
            }

            public void Cover(Rectangle r, Color color, IntPtr target)
            {
                BackColor = color;
                Bounds = r;
                if (!Visible) Show();
                // vor alle anderen Fenster legen, danach das aufgenommene Fenster bzw. Menü wieder davor
                SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, NoMoveSizeActivate);
                if (target != IntPtr.Zero) SetWindowPos(target, HwndTopmost, 0, 0, 0, 0, NoMoveSizeActivate);
                Refresh();
            }

            private static readonly IntPtr HwndTopmost = new IntPtr(-1);
            private const uint NoMoveSizeActivate = 0x0001 | 0x0002 | 0x0010; // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE

            [DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        }
    }
}
