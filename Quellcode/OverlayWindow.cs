// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>
    /// Durchklickbares, randloses Overlay mit echter Pixel-Transparenz (UpdateLayeredWindow).
    /// Zeigt das Raster und die Vorschau der neuen Fensterposition.
    /// </summary>
    internal sealed class OverlayWindow : Form
    {
        private IntPtr _memDC = IntPtr.Zero;
        private IntPtr _dib = IntPtr.Zero;
        private IntPtr _oldBmp = IntPtr.Zero;
        private IntPtr _bits = IntPtr.Zero;
        private Bitmap _surface;
        private IntPtr _bgBits = IntPtr.Zero;
        private Bitmap _background;
        private int _w, _h;

        private Rectangle _screen;
        private GridGeometry _geo;
        private Settings _settings;
        private Color _accent;
        private Font _fontBig, _fontSmall, _fontHint;
        private float _fontScale;

        private byte _alpha = 255;
        private bool _shown;
        private readonly Timer _fadeTimer;
        private readonly Timer _releaseTimer;
        private int _fadeStart;
        private const int FadeDuration = 120;

        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "WindowGrid Overlay";
            AutoScaleMode = AutoScaleMode.None;

            _fadeTimer = new Timer();
            _fadeTimer.Interval = 15;
            _fadeTimer.Tick += OnFadeTick;

            _releaseTimer = new Timer();
            _releaseTimer.Interval = 5000;
            _releaseTimer.Tick += delegate
            {
                _releaseTimer.Stop();
                if (!_shown) FreeBuffers();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE | TOPMOST
                cp.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000 | 0x00000008;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            // Größe/Position steuern wir selbst – WinForms soll bei DPI-Wechsel nichts skalieren.
            if (m.Msg == NativeMethods.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public bool IsOverlayVisible
        {
            get { return _shown; }
        }

        /// <summary>Bereitet das Overlay für einen Monitor vor (Hintergrund mit Raster wird einmal gezeichnet).</summary>
        public void Prepare(GridGeometry geo, Settings settings)
        {
            _geo = geo;
            _settings = settings;
            _accent = settings.Accent;
            _screen = geo.WorkArea;
            _releaseTimer.Stop();
            EnsureBuffers(_screen.Width, _screen.Height);
            EnsureFonts(geo.Scale);
            DrawBackground();
        }

        private void EnsureFonts(float scale)
        {
            if (_fontBig != null && Math.Abs(_fontScale - scale) < 0.01f) return;
            DisposeFonts();
            _fontScale = scale;
            _fontBig = Theme.UiFont(22f * scale, FontStyle.Bold);
            _fontSmall = Theme.UiFont(13f * scale, FontStyle.Regular);
            _fontHint = Theme.UiFont(13f * scale, FontStyle.Bold);
        }

        private void DisposeFonts()
        {
            if (_fontBig != null) { _fontBig.Dispose(); _fontBig = null; }
            if (_fontSmall != null) { _fontSmall.Dispose(); _fontSmall = null; }
            if (_fontHint != null) { _fontHint.Dispose(); _fontHint = null; }
        }

        private void EnsureBuffers(int w, int h)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            if (_memDC != IntPtr.Zero && w == _w && h == _h) return;
            FreeBuffers();
            _w = w;
            _h = h;

            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            _memDC = NativeMethods.CreateCompatibleDC(screenDC);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);

            NativeMethods.BITMAPINFOHEADER bi = new NativeMethods.BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            bi.biWidth = _w;
            bi.biHeight = -_h; // top-down
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            bi.biCompression = 0;
            _dib = NativeMethods.CreateDIBSection(_memDC, ref bi, 0, out _bits, IntPtr.Zero, 0);
            _oldBmp = NativeMethods.SelectObject(_memDC, _dib);
            _surface = new Bitmap(_w, _h, _w * 4, PixelFormat.Format32bppPArgb, _bits);

            _bgBits = Marshal.AllocHGlobal(_w * _h * 4);
            _background = new Bitmap(_w, _h, _w * 4, PixelFormat.Format32bppPArgb, _bgBits);
        }

        private void FreeBuffers()
        {
            if (_surface != null) { _surface.Dispose(); _surface = null; }
            if (_background != null) { _background.Dispose(); _background = null; }
            if (_bgBits != IntPtr.Zero) { Marshal.FreeHGlobal(_bgBits); _bgBits = IntPtr.Zero; }
            if (_memDC != IntPtr.Zero)
            {
                if (_oldBmp != IntPtr.Zero) NativeMethods.SelectObject(_memDC, _oldBmp);
                NativeMethods.DeleteDC(_memDC);
                _memDC = IntPtr.Zero;
                _oldBmp = IntPtr.Zero;
            }
            if (_dib != IntPtr.Zero) { NativeMethods.DeleteObject(_dib); _dib = IntPtr.Zero; }
            _bits = IntPtr.Zero;
            _w = 0;
            _h = 0;
        }

        private void DrawBackground()
        {
            float sc = _geo.Scale;
            int dimAlpha = (int)Math.Round(_settings.Dim * 255 / 100.0);
            using (Graphics g = Graphics.FromImage(_background))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.FromArgb(dimAlpha, 8, 10, 14));
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(-_screen.X, -_screen.Y);

                bool fine = Math.Min(_geo.CellWidth, _geo.CellHeight) < 30f * sc;
                if (fine)
                {
                    // Feines Raster: dezente Linien + Punkte an den Kreuzungen
                    g.SmoothingMode = SmoothingMode.None;
                    using (Pen pen = new Pen(Color.FromArgb(38, 255, 255, 255), 1f))
                    {
                        int top = _geo.Ys[0], bottom = _geo.Ys[_geo.Rows];
                        int left = _geo.Xs[0], right = _geo.Xs[_geo.Columns];
                        foreach (int x in _geo.Xs) g.DrawLine(pen, x, top, x, bottom);
                        foreach (int y in _geo.Ys) g.DrawLine(pen, left, y, right, y);
                    }
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float d = Math.Max(2f, 2.5f * sc);
                    using (SolidBrush dot = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                    {
                        foreach (int x in _geo.Xs)
                            foreach (int y in _geo.Ys)
                                g.FillEllipse(dot, x - d / 2f, y - d / 2f, d, d);
                    }
                }
                else
                {
                    // Kacheln mit abgerundeten Ecken
                    float inset = _geo.Gap < 4 ? 3f * sc : 0f;
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                    using (Pen border = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, sc)))
                    {
                        for (int c = 0; c < _geo.Columns; c++)
                        {
                            for (int r = 0; r < _geo.Rows; r++)
                            {
                                RectangleF cell = _geo.Cell(c, r);
                                cell.Inflate(-inset, -inset);
                                if (cell.Width < 2 || cell.Height < 2) continue;
                                float radius = Math.Min(10f * sc, Math.Min(cell.Width, cell.Height) / 4f);
                                using (GraphicsPath p = Theme.RoundRect(cell, radius))
                                {
                                    g.FillPath(fill, p);
                                    g.DrawPath(border, p);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Zeichnet Raster + Auswahl und zeigt das Ergebnis an.</summary>
        public void Render(Rectangle selection, string hint, string hintSub, string line1, string line2, float[] margins)
        {
            if (_memDC == IntPtr.Zero || _geo == null) return;
            NativeMethods.CopyMemory(_bits, _bgBits, new UIntPtr((uint)(_w * _h * 4)));

            float sc = _geo.Scale;
            using (Graphics g = Graphics.FromImage(_surface))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(-_screen.X, -_screen.Y);

                if (selection.Width > 2 && selection.Height > 2)
                {
                    RectangleF r = selection;
                    float radius = Math.Min(12f * sc, Math.Min(r.Width, r.Height) / 3f);

                    // Weicher Schein
                    for (int i = 5; i >= 1; i--)
                    {
                        float grow = i * 2.2f * sc;
                        RectangleF gr = RectangleF.Inflate(r, grow, grow);
                        using (GraphicsPath gp = Theme.RoundRect(gr, radius + grow))
                        using (Pen pen = new Pen(Theme.WithAlpha(_accent, 10 + (5 - i) * 9), 2.4f * sc))
                            g.DrawPath(pen, gp);
                    }

                    // Füllung mit leichtem Verlauf
                    using (GraphicsPath path = Theme.RoundRect(r, radius))
                    {
                        using (LinearGradientBrush br = new LinearGradientBrush(
                            new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                            Theme.WithAlpha(_accent, 130), Theme.WithAlpha(_accent, 80), LinearGradientMode.Vertical))
                            g.FillPath(br, path);
                    }

                    // Rand
                    RectangleF br2 = RectangleF.Inflate(r, -1f * sc, -1f * sc);
                    using (GraphicsPath path = Theme.RoundRect(br2, Math.Max(1f, radius - 1f * sc)))
                    using (Pen pen = new Pen(Theme.Blend(_accent, Color.White, 0.25f), 2f * sc))
                        g.DrawPath(pen, path);

                    // Innerer Glanz
                    RectangleF ir = RectangleF.Inflate(r, -4f * sc, -4f * sc);
                    if (ir.Width > 4 && ir.Height > 4)
                    {
                        using (GraphicsPath path = Theme.RoundRect(ir, Math.Max(1f, radius - 4f * sc)))
                        using (Pen pen = new Pen(Color.FromArgb(55, 255, 255, 255), Math.Max(1f, sc)))
                            g.DrawPath(pen, path);
                    }

                    if (margins != null)
                        DrawMargins(g, r, margins, sc);

                    if (_settings.ShowSize && line1 != null)
                        DrawLabel(g, r, line1, line2, sc);
                }

                if (hint != null)
                    DrawHint(g, hint, hintSub, sc);
            }
            Present();
        }

        /// <summary>
        /// Maßlinien von der Auswahl zu den Bildschirmrändern mit Anzahl der freien Felder.
        /// Gleiche Abstände links/rechts bzw. oben/unten werden in der Akzentfarbe hervorgehoben.
        /// </summary>
        private void DrawMargins(Graphics g, RectangleF sel, float[] m, float sc)
        {
            bool symH = m[0] > 0 && Math.Abs(m[0] - m[2]) < 0.05f;
            bool symV = m[1] > 0 && Math.Abs(m[1] - m[3]) < 0.05f;
            float edge = 6f * sc;
            float cx = sel.X + sel.Width / 2f;
            float cy = sel.Y + sel.Height / 2f;

            // links, oben, rechts, unten
            if (m[0] > 0) DrawMarginLine(g, new PointF(_screen.Left + edge, cy), new PointF(sel.Left - 3f * sc, cy), m[0], symH, sc);
            if (m[1] > 0) DrawMarginLine(g, new PointF(cx, _screen.Top + edge), new PointF(cx, sel.Top - 3f * sc), m[1], symV, sc);
            if (m[2] > 0) DrawMarginLine(g, new PointF(sel.Right + 3f * sc, cy), new PointF(_screen.Right - edge, cy), m[2], symH, sc);
            if (m[3] > 0) DrawMarginLine(g, new PointF(cx, sel.Bottom + 3f * sc), new PointF(cx, _screen.Bottom - edge), m[3], symV, sc);
        }

        private void DrawMarginLine(Graphics g, PointF a, PointF b, float cells, bool highlight, float sc)
        {
            bool horizontal = Math.Abs(a.Y - b.Y) < 0.5f;
            float len = horizontal ? b.X - a.X : b.Y - a.Y;
            if (len < 4f * sc) return;

            Color lineColor = highlight ? Theme.Blend(_accent, Color.White, 0.35f) : Color.FromArgb(170, 255, 255, 255);
            using (Pen pen = new Pen(lineColor, 1.6f * sc))
            {
                pen.DashPattern = new float[] { 3f, 2.5f };
                g.DrawLine(pen, a, b);
            }
            // Endstriche
            float t = 6f * sc;
            using (Pen pen = new Pen(lineColor, 1.6f * sc))
            {
                if (horizontal)
                {
                    g.DrawLine(pen, a.X, a.Y - t, a.X, a.Y + t);
                    g.DrawLine(pen, b.X, b.Y - t, b.X, b.Y + t);
                }
                else
                {
                    g.DrawLine(pen, a.X - t, a.Y, a.X + t, a.Y);
                    g.DrawLine(pen, b.X - t, b.Y, b.X + t, b.Y);
                }
            }

            // Beschriftung in der Mitte der Linie
            string text = cells.ToString(Math.Abs(cells - (float)Math.Round(cells)) < 0.01f ? "0" : "0.0");
            SizeF ts = g.MeasureString(text, _fontHint, PointF.Empty, StringFormat.GenericTypographic);
            float pw = Math.Max(ts.Width + 16f * sc, 26f * sc), ph = ts.Height + 8f * sc;
            PointF mid = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
            RectangleF pill = new RectangleF(mid.X - pw / 2f, mid.Y - ph / 2f, pw, ph);
            Color fill = highlight ? _accent : Color.FromArgb(225, 22, 24, 28);
            using (GraphicsPath p = Theme.RoundRect(pill, ph / 2f))
            {
                using (SolidBrush b2 = new SolidBrush(fill)) g.FillPath(b2, p);
                using (Pen pen = new Pen(Color.FromArgb(highlight ? 90 : 50, 255, 255, 255), Math.Max(1f, sc))) g.DrawPath(pen, p);
            }
            using (SolidBrush tb = new SolidBrush(highlight ? Theme.ContrastText(_accent) : Color.White))
                g.DrawString(text, _fontHint, tb, mid.X - ts.Width / 2f, mid.Y - ts.Height / 2f, StringFormat.GenericTypographic);
        }

        private void DrawLabel(Graphics g, RectangleF sel, string line1, string line2, float sc)
        {
            SizeF s1 = g.MeasureString(line1, _fontBig, PointF.Empty, StringFormat.GenericTypographic);
            SizeF s2 = line2 != null ? g.MeasureString(line2, _fontSmall, PointF.Empty, StringFormat.GenericTypographic) : SizeF.Empty;
            float padX = 18f * sc, padY = 10f * sc, spacing = 2f * sc;
            float w = Math.Max(s1.Width, s2.Width) + padX * 2;
            float h = s1.Height + (line2 != null ? s2.Height + spacing : 0) + padY * 2;

            float x = sel.X + (sel.Width - w) / 2f;
            float y = sel.Y + (sel.Height - h) / 2f;
            PointF pos;
            if (w + 16f * sc > sel.Width || h + 16f * sc > sel.Height)
            {
                // Passt nicht in die Auswahl: schräg daneben setzen – dort laufen keine Maßlinien
                // und Auswahl und Abstände bleiben sichtbar
                pos = LabelOutside(sel, w, h, sc);
                x = pos.X;
                y = pos.Y;
            }
            // Im sichtbaren Bereich halten
            x = Math.Max(_screen.Left + 8 * sc, Math.Min(x, _screen.Right - w - 8 * sc));
            y = Math.Max(_screen.Top + 8 * sc, Math.Min(y, _screen.Bottom - h - 8 * sc));

            RectangleF pill = new RectangleF(x, y, w, h);
            using (GraphicsPath p = Theme.RoundRect(pill, Math.Min(12f * sc, h / 2f)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 22, 24, 28)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, sc)))
                    g.DrawPath(pen, p);
            }
            using (SolidBrush t1 = new SolidBrush(Color.White))
                g.DrawString(line1, _fontBig, t1, x + (w - s1.Width) / 2f, y + padY, StringFormat.GenericTypographic);
            if (line2 != null)
            {
                using (SolidBrush t2 = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
                    g.DrawString(line2, _fontSmall, t2, x + (w - s2.Width) / 2f, y + padY + s1.Height + spacing, StringFormat.GenericTypographic);
            }
        }

        /// <summary>
        /// Position für den Größen-Kasten außerhalb einer kleinen Auswahl: immer links oben schräg
        /// daneben (feste Seite, damit der Kasten beim Bewegen nicht hin und her springt).
        /// Dort verdeckt ihn auch der Mauszeiger nicht, da der Pfeil nach rechts unten zeigt.
        /// Nur wenn am Bildschirmrand kein Platz ist, weicht er nach rechts bzw. unten aus.
        /// </summary>
        private PointF LabelOutside(RectangleF sel, float w, float h, float sc)
        {
            float gap = 12f * sc;
            float edge = 8f * sc;
            float left = sel.Left - gap - w;
            float top = sel.Top - gap - h;
            float x = left >= _screen.Left + edge ? left : sel.Right + gap;
            float y = top >= _screen.Top + edge ? top : sel.Bottom + gap;
            return new PointF(x, y);
        }

        private void DrawHint(Graphics g, string hint, string sub, float sc)
        {
            if (sub == null) sub = "Esc  Raster aus";
            SizeF s1 = g.MeasureString(hint, _fontHint, PointF.Empty, StringFormat.GenericTypographic);
            SizeF s2 = g.MeasureString(sub, _fontSmall, PointF.Empty, StringFormat.GenericTypographic);
            float dot = 8f * sc, gap = 10f * sc, sep = 14f * sc, padX = 16f * sc;
            float h = 34f * sc;
            float w = padX + dot + gap + s1.Width + sep + 1f + sep + s2.Width + padX;
            float x = _screen.Left + (_screen.Width - w) / 2f;
            float y = _screen.Top + 18f * sc;

            RectangleF pill = new RectangleF(x, y, w, h);
            using (GraphicsPath p = Theme.RoundRect(pill, h / 2f))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(225, 22, 24, 28)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, sc)))
                    g.DrawPath(pen, p);
            }
            float cx = x + padX;
            using (SolidBrush d = new SolidBrush(_accent))
                g.FillEllipse(d, cx, y + (h - dot) / 2f, dot, dot);
            cx += dot + gap;
            using (SolidBrush t = new SolidBrush(Color.White))
                g.DrawString(hint, _fontHint, t, cx, y + (h - s1.Height) / 2f, StringFormat.GenericTypographic);
            cx += s1.Width + sep;
            using (Pen line = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                g.DrawLine(line, cx, y + 9f * sc, cx, y + h - 9f * sc);
            cx += 1f + sep;
            using (SolidBrush t = new SolidBrush(Color.FromArgb(170, 255, 255, 255)))
                g.DrawString(sub, _fontSmall, t, cx, y + (h - s2.Height) / 2f, StringFormat.GenericTypographic);
        }

        private void Present()
        {
            if (_memDC == IntPtr.Zero) return;
            NativeMethods.POINT dst = new NativeMethods.POINT(_screen.X, _screen.Y);
            NativeMethods.SIZE size = new NativeMethods.SIZE(_w, _h);
            NativeMethods.POINT src = new NativeMethods.POINT(0, 0);
            NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
            blend.BlendOp = NativeMethods.AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = _alpha;
            blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;
            NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _memDC, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);
        }

        public void ShowOverlay(bool animate)
        {
            _releaseTimer.Stop();
            if (_shown) return;
            _shown = true;
            _alpha = animate ? (byte)0 : (byte)255;
            Present();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            if (animate)
            {
                _fadeStart = Environment.TickCount;
                _fadeTimer.Start();
            }
        }

        public void HideOverlay()
        {
            _fadeTimer.Stop();
            if (!_shown) return;
            _shown = false;
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
            _geo = null;
            _releaseTimer.Start();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            int elapsed = Environment.TickCount - _fadeStart;
            double t = Math.Min(1.0, elapsed / (double)FadeDuration);
            double eased = 1 - Math.Pow(1 - t, 3);
            _alpha = (byte)Math.Round(255 * eased);
            Present();
            if (t >= 1.0) _fadeTimer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fadeTimer.Dispose();
                _releaseTimer.Dispose();
                DisposeFonts();
            }
            FreeBuffers();
            base.Dispose(disposing);
        }
    }
}