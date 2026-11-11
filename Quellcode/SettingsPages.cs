// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>Gemeinsame Helfer der Seiten im Einstellungsfenster.</summary>
    internal static class PageUi
    {
        public static Label MakeLabel(string text, float size, bool bold, bool subText, float scale)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.UseMnemonic = false;
            l.Font = Theme.UiFont(size * scale, bold ? FontStyle.Bold : FontStyle.Regular);
            l.Tag = subText ? "sub" : null;
            return l;
        }

        public static Label Head(Card card, string text, int centerX, int y, int width, float scale)
        {
            Label h = MakeLabel(text, 12.5f, false, true, scale);
            h.AutoSize = false;
            h.TextAlign = ContentAlignment.MiddleCenter;
            h.Bounds = new Rectangle(centerX - width / 2, y, width, (int)Math.Round(22 * scale));
            card.Controls.Add(h);
            return h;
        }
    }

    /// <summary>
    /// Eintrag der Navigation links (globale Vorgabe, Vorlage Wake on LAN): Symbol und Text, ausgewählt grau
    /// hinterlegt mit Akzentbalken links; eingeklappt nur das Symbol. IconOnly: kleiner Symbolknopf (Stecknadel).
    /// </summary>
    internal sealed class NavButton : Control
    {
        private readonly float k;
        private bool _hover, _down, _selected;
        public char Glyph;
        public bool IconOnly;

        public NavButton(string text, char glyph, float scale)
        {
            k = scale;
            Text = text;
            Glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            Font = new Font("Segoe UI", 9.5f);
            Cursor = Cursors.Hand;
            Height = (int)(40 * k);
            TabStop = false;
        }

        public bool Selected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : p.Back;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color tint = p.Dark ? Color.White : Color.Black;
            Color fill = _selected && !IconOnly ? Theme.Blend(back, tint, 0.07f)
                       : _down ? Theme.Blend(back, tint, 0.10f)
                       : _hover ? Theme.Blend(back, tint, 0.05f) : back;
            if (fill != back)
                using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 5f * k))
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);

            // Symbolgrößen: Einträge und ☰ 14 pt, Stecknadel 12 pt
            int gs = (int)Math.Round((IconOnly ? 12f : 14f) * 96f / 72f * k);
            if (IconOnly)
            {
                DrawGlyph(g, (Width - gs) / 2, gs, p.Text);
                return;
            }
            if (_selected)
                using (GraphicsPath path = Theme.RoundRect(new RectangleF(0, Height / 2f - 8 * k, 3 * k, 16 * k), 1.5f * k))
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            int x = (int)(14 * k);
            if (Glyph != '\0')
            {
                DrawGlyph(g, x + ((int)(20 * k) - gs) / 2, gs, p.Text);
                x += (int)(32 * k);
            }
            // eingeklappt (schmal) nur das Symbol
            if (Text.Length > 0 && Width > x + (int)(40 * k))
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - (int)(6 * k), Height), p.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void DrawGlyph(Graphics g, int x, int size, Color color)
        {
            if (Glyph == '\0') return;
            Image img = MenuHelper.Glyph(Glyph, color, size);
            if (img == null) return;
            g.DrawImage(img, x, (Height - size) / 2, size, size);
            img.Dispose();
        }
    }

    /// <summary>Kleiner Bildschirm mit der Zone in Akzentfarbe.</summary>
    internal sealed class ZonePreview : Control
    {
        public RectangleF Zone;
        public float UiScale = 1f;

        public ZonePreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF screen = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundRect(screen, 4f * UiScale))
            {
                using (SolidBrush b = new SolidBrush(p.Input)) g.FillPath(b, path);
                using (Pen pen = new Pen(p.InputBorder, 1f)) g.DrawPath(pen, path);
            }
            if (Zone.Width <= 0 || Zone.Height <= 0) return;
            float pad = 2.5f * UiScale;
            RectangleF inner = RectangleF.Inflate(screen, -pad, -pad);
            RectangleF z = new RectangleF(
                inner.X + inner.Width * Zone.Left / 100f,
                inner.Y + inner.Height * Zone.Top / 100f,
                inner.Width * Zone.Width / 100f,
                inner.Height * Zone.Height / 100f);
            using (GraphicsPath path = Theme.RoundRect(z, 2f * UiScale))
            using (SolidBrush b = new SolidBrush(Theme.Accent))
                g.FillPath(b, path);
        }
    }

    /// <summary>Seite "Schnellzonen": neun Zeilen mit Vorschau, Name und Bereich in Prozent (Karte 712 breit).</summary>
    internal sealed class ZonesEditor
    {
        private readonly float _sc;
        private readonly TextField[] _names = new TextField[9];
        private readonly Stepper[,] _values = new Stepper[9, 4];
        private readonly ZonePreview[] _previews = new ZonePreview[9];

        /// <summary>Höhe der Karte in logischen Einheiten.</summary>
        public const int CardHeight = 470;

        public ZonesEditor(Card card, float scale)
        {
            _sc = scale;
            int xNr = S(18), xPrev = S(44), xName = S(116), xVal = S(278);
            int stepW = S(98), stepGap = S(6);
            string[] heads = { "Links %", "Oben %", "Rechts %", "Unten %" };
            Label hName = PageUi.MakeLabel("Name", 12.5f, false, true, _sc);
            hName.Location = new Point(xName, S(16));
            card.Controls.Add(hName);
            for (int k = 0; k < 4; k++)
                PageUi.Head(card, heads[k], xVal + k * (stepW + stepGap) + stepW / 2, S(12), stepW, _sc);

            for (int i = 0; i < 9; i++)
            {
                int y = S(44) + i * S(46);
                Label nr = PageUi.MakeLabel((i + 1).ToString(), 15f, true, false, _sc);
                nr.Location = new Point(xNr, y + S(6));
                card.Controls.Add(nr);

                ZonePreview pv = new ZonePreview();
                pv.UiScale = _sc;
                pv.Bounds = new Rectangle(xPrev, y + S(2), S(56), S(34));
                card.Controls.Add(pv);
                _previews[i] = pv;

                TextField tb = new TextField();
                tb.Font = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
                tb.Bounds = new Rectangle(xName, y + S(4), S(150), S(30));
                tb.MaxLength = 40;
                card.Controls.Add(tb);
                _names[i] = tb;

                for (int k = 0; k < 4; k++)
                {
                    Stepper st = new Stepper();
                    st.Minimum = 0;
                    st.Maximum = 100;
                    st.Bounds = new Rectangle(xVal + k * (stepW + stepGap), y + S(4), stepW, S(30));
                    int row = i;
                    st.ValueChanged += delegate { UpdatePreview(row); };
                    card.Controls.Add(st);
                    _values[i, k] = st;
                }
            }
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }

        public void Load(QuickZone[] zones)
        {
            for (int i = 0; i < 9 && i < zones.Length; i++)
            {
                _names[i].Text = zones[i].Name;
                _values[i, 0].Value = zones[i].Left;
                _values[i, 1].Value = zones[i].Top;
                _values[i, 2].Value = zones[i].Right;
                _values[i, 3].Value = zones[i].Bottom;
                UpdatePreview(i);
            }
        }

        /// <summary>Werte in die Zonen schreiben (vertauschte Werte richtigstellen, Mindestgröße 1 %).</summary>
        public void Read(QuickZone[] zones)
        {
            for (int i = 0; i < 9 && i < zones.Length; i++)
            {
                int l = _values[i, 0].Value, t = _values[i, 1].Value, r = _values[i, 2].Value, b = _values[i, 3].Value;
                if (r < l) { int x = l; l = r; r = x; }
                if (b < t) { int x = t; t = b; b = x; }
                if (r == l) { if (r < 100) r++; else l--; }
                if (b == t) { if (b < 100) b++; else t--; }
                zones[i].Name = _names[i].Text.Trim();
                zones[i].Left = l; zones[i].Top = t; zones[i].Right = r; zones[i].Bottom = b;
            }
        }

        private void UpdatePreview(int i)
        {
            if (_previews[i] == null) return;
            _previews[i].Zone = RectangleF.FromLTRB(_values[i, 0].Value, _values[i, 1].Value, _values[i, 2].Value, _values[i, 3].Value);
            _previews[i].Invalidate();
        }

        public void InvalidatePreviews()
        {
            foreach (ZonePreview pv in _previews) if (pv != null) pv.Invalidate();
        }
    }

    /// <summary>Karte "Raster je Monitor": Schalter und Spalten/Zeilen für jeden angeschlossenen Bildschirm (höchstens fünf).</summary>
    internal sealed class MonitorsEditor
    {
        private readonly float _sc;
        private readonly List<string> _devices = new List<string>();
        private readonly List<ToggleSwitch> _own = new List<ToggleSwitch>();
        private readonly List<Stepper> _cols = new List<Stepper>();
        private readonly List<Stepper> _rows = new List<Stepper>();

        public const int MaxMonitors = 5;

        /// <summary>Höhe der Karte in logischen Einheiten für die angeschlossenen Monitore.</summary>
        public static int CardHeight
        {
            get { return 74 + Math.Min(MaxMonitors, Screen.AllScreens.Length) * 46 + 8; }
        }

        public MonitorsEditor(Card card, float scale)
        {
            _sc = scale;
            int xToggle = S(410), xCols = S(476), xRows = S(590), stepW = S(104);
            PageUi.Head(card, "Eigenes Raster", xToggle + S(22), S(46), S(110), _sc);
            PageUi.Head(card, "Spalten", xCols + stepW / 2, S(46), stepW, _sc);
            PageUi.Head(card, "Zeilen", xRows + stepW / 2, S(46), stepW, _sc);

            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length && i < MaxMonitors; i++)
            {
                Screen sc = screens[i];
                string device = Settings.DeviceName(sc);
                int y = S(74) + i * S(46);

                Label name = PageUi.MakeLabel("Monitor " + Number(device, i + 1), 13.5f, false, false, _sc);
                name.Location = new Point(S(18), y);
                card.Controls.Add(name);
                Label info = PageUi.MakeLabel(sc.Bounds.Width + " × " + sc.Bounds.Height + (sc.Primary ? " · Hauptmonitor" : ""), 12f, false, true, _sc);
                info.Location = new Point(S(18), y + S(20));
                card.Controls.Add(info);

                ToggleSwitch t = new ToggleSwitch();
                t.Size = new Size(S(44), S(22));
                t.Location = new Point(xToggle, y + S(8));
                Stepper c = MakeStepper(new Rectangle(xCols, y + S(4), stepW, S(30)));
                Stepper r = MakeStepper(new Rectangle(xRows, y + S(4), stepW, S(30)));
                card.Controls.Add(t);
                card.Controls.Add(c);
                card.Controls.Add(r);
                t.CheckedChanged += delegate { c.Enabled = r.Enabled = t.Checked; };

                _devices.Add(device);
                _own.Add(t);
                _cols.Add(c);
                _rows.Add(r);
            }
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }

        private static Stepper MakeStepper(Rectangle bounds)
        {
            Stepper s = new Stepper();
            s.Minimum = 1;
            s.Maximum = 100;
            s.Bounds = bounds;
            return s;
        }

        /// <summary>Nummer aus dem Gerätenamen (\\.\DISPLAY2 → 2), sonst die laufende Nummer.</summary>
        private static int Number(string device, int fallback)
        {
            int end = device.Length, start = end;
            while (start > 0 && char.IsDigit(device[start - 1])) start--;
            int n;
            return start < end && int.TryParse(device.Substring(start), out n) ? n : fallback;
        }

        public void Load(List<MonitorGrid> list, int defaultColumns, int defaultRows)
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                string device = _devices[i];
                MonitorGrid own = list.Find(m => string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase));
                _cols[i].Value = own != null ? own.Columns : defaultColumns;
                _rows[i].Value = own != null ? own.Rows : defaultRows;
                _own[i].Checked = own != null;
                _cols[i].Enabled = _rows[i].Enabled = own != null;
            }
        }

        /// <summary>Einträge der angezeigten Monitore neu schreiben; Einträge anderer Monitore bleiben erhalten.</summary>
        public void Read(List<MonitorGrid> list)
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                string device = _devices[i];
                list.RemoveAll(m => string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase));
                if (_own[i].Checked) list.Add(new MonitorGrid(device, _cols[i].Value, _rows[i].Value));
            }
        }
    }

    /// <summary>Karte "Ausnahmen": Programme, bei denen WindowGrid kein Raster zeigt.</summary>
    internal sealed class ExcludeEditor
    {
        private readonly float _sc;
        private readonly TextField _text;

        /// <summary>Höhe der Karte in logischen Einheiten (nach dem Aufbau).</summary>
        public int CardHeight;

        public ExcludeEditor(Card card, float scale)
        {
            _sc = scale;
            int cardW = card.Width;
            Label sub = PageUi.MakeLabel(
                "Bei diesen Programmen zeigt WindowGrid kein Raster und setzt sie nicht per Tastenkürzel.\n" +
                "Programmnamen ohne „.exe“, mehrere mit Semikolon trennen – z. B. mstsc; vlc", 12.5f, false, true, _sc);
            sub.Location = new Point(S(18), S(44));
            card.Controls.Add(sub);

            _text = new TextField();
            _text.Font = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
            _text.Bounds = new Rectangle(S(18), S(88), cardW - S(36), S(32));
            _text.MaxLength = 2000;
            card.Controls.Add(_text);

            Label running = PageUi.MakeLabel("Gerade geöffnete Programme – anklicken zum Hinzufügen:", 12.5f, false, true, _sc);
            running.Location = new Point(S(18), S(132));
            card.Controls.Add(running);

            // Knöpfe der laufenden Programme, höchstens drei Zeilen
            int x = S(18), y = S(158), rowH = S(36), rows = 1;
            Font f = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
            foreach (string name in RunningPrograms())
            {
                int w = TextRenderer.MeasureText(name, f).Width + S(24);
                if (x + w > cardW - S(18))
                {
                    if (rows == 3) break;
                    rows++;
                    x = S(18);
                    y += rowH;
                }
                ModernButton b = new ModernButton();
                b.Text = name;
                b.Bounds = new Rectangle(x, y, w, S(30));
                string n = name;
                b.Click += delegate { Add(n); };
                card.Controls.Add(b);
                x += w + S(8);
            }
            CardHeight = (int)Math.Ceiling((y + S(30) + S(18)) / _sc);
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }

        public string Text
        {
            get { return Settings.NormalizeExcluded(_text.Text); }
            set { _text.Text = value ?? ""; }
        }

        private void Add(string name)
        {
            string joined = _text.Text.Trim();
            if (joined.Length > 0 && !joined.EndsWith(";")) joined += ";";
            _text.Text = Settings.NormalizeExcluded(joined + name);
        }

        /// <summary>Namen der Programme mit sichtbarem Fenster (ohne WindowGrid), alphabetisch.</summary>
        private static List<string> RunningPrograms()
        {
            List<string> names = new List<string>();
            if (Demo.Active)
            {
                // Screenshots: nur Beispielnamen, nichts vom echten Rechner
                names.AddRange(new[] { "Editor", "Explorer", "Rechner" });
                return names;
            }
            try
            {
                int own = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id != own && p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                        {
                            string n = p.ProcessName;
                            if (!names.Exists(e => string.Equals(e, n, StringComparison.OrdinalIgnoreCase))) names.Add(n);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}