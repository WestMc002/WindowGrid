// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowGrid
{
    /// <summary>Schnellzonen 1–9 bearbeiten: Name und Bereich in Prozent des Arbeitsbereichs.</summary>
    internal sealed class ZonesForm : Form
    {
        private readonly QuickZone[] _zones;
        private readonly float _sc;
        private readonly TextField[] _names = new TextField[9];
        private readonly Stepper[,] _values = new Stepper[9, 4];
        private readonly ZonePreview[] _previews = new ZonePreview[9];
        private Card _card;

        /// <summary>Die Zonen werden direkt in dem übergebenen Array geändert, wenn "Übernehmen" geklickt wird.</summary>
        public ZonesForm(QuickZone[] zones, Icon icon)
        {
            _zones = zones;
            using (Graphics g = CreateGraphics()) _sc = g.DpiX / 96f;

            AutoScaleMode = AutoScaleMode.None;
            Text = "WindowGrid – Schnellzonen";
            Icon = icon;
            // Normaler Rahmen mit fester Größe (siehe Globale Richtlinien)
            FormBorderStyle = FormBorderStyle.Sizable;
            SizeGripStyle = SizeGripStyle.Hide;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
            KeyPreview = true;
            DoubleBuffered = true;
            ClientSize = new Size(S(760), S(700));

            BuildUi();
            LoadValues(_zones);
            ApplyTheme();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }

        private void BuildUi()
        {
            Label title = MakeLabel("Schnellzonen", 22f, true, false);
            title.Location = new Point(S(24), S(18));
            Controls.Add(title);

            Label sub = MakeLabel("Bei sichtbarem Raster Ziffer 1–9 drücken, um das Fenster auf die Zone zu setzen.\nUmschalt+Ziffer speichert die aktuelle Auswahl als Zone. Werte in Prozent des Bildschirms.", 12.5f, false, true);
            sub.Location = new Point(S(26), S(52));
            Controls.Add(sub);

            _card = new Card();
            _card.UiScale = _sc;
            _card.Bounds = new Rectangle(S(24), S(100), S(712), S(470));
            Controls.Add(_card);

            // Spaltenköpfe
            int xNr = S(18), xPrev = S(44), xName = S(116), xVal = S(278);
            int stepW = S(98), stepGap = S(6);
            string[] heads = { "Links %", "Oben %", "Rechts %", "Unten %" };
            Label hName = MakeLabel("Name", 12.5f, false, true);
            hName.Location = new Point(xName, S(16));
            _card.Controls.Add(hName);
            for (int k = 0; k < 4; k++)
            {
                Label h = MakeLabel(heads[k], 12.5f, false, true);
                h.AutoSize = false;
                h.TextAlign = ContentAlignment.MiddleCenter;
                h.Bounds = new Rectangle(xVal + k * (stepW + stepGap), S(12), stepW, S(22));
                _card.Controls.Add(h);
            }

            for (int i = 0; i < 9; i++)
            {
                int y = S(44) + i * S(46);
                Label nr = MakeLabel((i + 1).ToString(), 15f, true, false);
                nr.Location = new Point(xNr, y + S(6));
                _card.Controls.Add(nr);

                ZonePreview pv = new ZonePreview();
                pv.UiScale = _sc;
                pv.Bounds = new Rectangle(xPrev, y + S(2), S(56), S(34));
                _card.Controls.Add(pv);
                _previews[i] = pv;

                TextField tb = new TextField();
                tb.Font = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
                tb.Bounds = new Rectangle(xName, y + S(4), S(150), S(30));
                tb.MaxLength = 40;
                _card.Controls.Add(tb);
                _names[i] = tb;

                for (int k = 0; k < 4; k++)
                {
                    Stepper st = new Stepper();
                    st.Minimum = 0;
                    st.Maximum = 100;
                    st.Bounds = new Rectangle(xVal + k * (stepW + stepGap), y + S(4), stepW, S(30));
                    int row = i;
                    st.ValueChanged += delegate { UpdatePreview(row); };
                    _card.Controls.Add(st);
                    _values[i, k] = st;
                }
            }

            int by = S(590);
            ModernButton defaults = MakeButton("Standardwerte", false, new Rectangle(S(24), by, S(140), S(34)));
            // wie die Einstellungsseite: "Abbrechen" links, Primärknopf ganz rechts. "Übernehmen", weil erst
            // "Speichern" im Einstellungsfenster die Schnellzonen dauerhaft speichert.
            ModernButton cancel = MakeButton("Abbrechen", false, new Rectangle(S(736) - S(120) - S(10) - S(120), by, S(120), S(34)));
            ModernButton ok = MakeButton("Übernehmen", true, new Rectangle(S(736) - S(120), by, S(120), S(34)));
            defaults.Click += delegate { LoadValues(QuickZone.Defaults()); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ok.Click += delegate { Save(); };

            SignatureFooter footer = new SignatureFooter(_sc);
            footer.Dock = DockStyle.Bottom;
            footer.Height = S(64);
            Controls.Add(footer);
        }

        private Label MakeLabel(string text, float size, bool bold, bool subText)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.UseMnemonic = false;
            l.Font = Theme.UiFont(size * _sc, bold ? FontStyle.Bold : FontStyle.Regular);
            l.Tag = subText ? "sub" : null;
            return l;
        }

        private ModernButton MakeButton(string text, bool primary, Rectangle bounds)
        {
            ModernButton b = new ModernButton();
            b.Text = text;
            b.Primary = primary;
            b.Bounds = bounds;
            Controls.Add(b);
            return b;
        }

        private void LoadValues(QuickZone[] zones)
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

        private void UpdatePreview(int i)
        {
            if (_previews[i] == null) return;
            _previews[i].Zone = RectangleF.FromLTRB(_values[i, 0].Value, _values[i, 1].Value, _values[i, 2].Value, _values[i, 3].Value);
            _previews[i].Invalidate();
        }

        private void Save()
        {
            for (int i = 0; i < 9 && i < _zones.Length; i++)
            {
                int l = _values[i, 0].Value, t = _values[i, 1].Value, r = _values[i, 2].Value, b = _values[i, 3].Value;
                // Vertauschte Werte richtigstellen, Mindestgröße 1 %
                if (r < l) { int x = l; l = r; r = x; }
                if (b < t) { int x = t; t = b; b = x; }
                if (r == l) { if (r < 100) r++; else l--; }
                if (b == t) { if (b < 100) b++; else t--; }
                _zones[i].Name = _names[i].Text.Trim();
                _zones[i].Left = l; _zones[i].Top = t; _zones[i].Right = r; _zones[i].Bottom = b;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // ------------------------------------------------------------------ Design

        private void ApplyTheme()
        {
            Palette p = Theme.Current;
            BackColor = p.Back;
            ForeColor = p.Text;
            ApplyTheme(this, p);
            if (IsHandleCreated) NativeMethods.UseDarkTitleBar(Handle, p.Dark);
            Invalidate(true);
        }

        private static void ApplyTheme(Control parent, Palette p)
        {
            foreach (Control c in parent.Controls)
            {
                bool onCard = parent is Card;
                if (!(c is Card)) c.BackColor = onCard ? p.Card : p.Back;
                if (c is Label) c.ForeColor = (c.Tag as string) == "sub" ? p.SubText : p.Text;
                if (c is TextField) continue; // zeichnet sich selbst passend zum Farbschema
                if (c.Controls.Count > 0) ApplyTheme(c, p);
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)ApplyTheme);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.UseDarkTitleBar(Handle, Theme.IsDark);
        }

        // Feste Größe: am Rand keine Pfeile zum Größe-Ändern anzeigen
        private const int WM_NCHITTEST = 0x0084, HTCLIENT = 1, HTLEFT = 10, HTBOTTOMRIGHT = 17;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST)
            {
                int hit = m.Result.ToInt32();
                if (hit >= HTLEFT && hit <= HTBOTTOMRIGHT) m.Result = (IntPtr)HTCLIENT;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            MinimumSize = Size;
            MaximumSize = Size;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Esc = Abbrechen, Enter = Übernehmen (außer ein Knopf hat den Fokus – der löst selbst aus)
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && !(ActiveControl is ModernButton))
            {
                Save();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            base.Dispose(disposing);
        }

        /// <summary>Kleiner Bildschirm mit der Zone in Akzentfarbe.</summary>
        private sealed class ZonePreview : Control
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
    }
}