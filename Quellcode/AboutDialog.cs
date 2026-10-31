// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>
    /// "Info über WindowGrid": großes GS-Logo, Version, Eckdaten, Anleitung/Sicherungen.
    /// Aufbau, Schriften und Maße 1:1 wie in Monitor Dimmer und Fensterpositionen.
    /// </summary>
    internal sealed class AboutDialog : Form
    {
        private static AboutDialog _open;
        private readonly float k;
        private readonly DialogButton _close;

        // Farben wie in den anderen Programmen
        private readonly Color _back, _card, _border, _text, _muted, _versionText;

        public static void ShowFor(Form owner)
        {
            if (_open != null) { _open.Activate(); return; }
            using (AboutDialog d = new AboutDialog(owner))
            {
                _open = d;
                try
                {
                    if (owner != null && owner.Visible) d.ShowDialog(owner);
                    else d.ShowDialog();
                }
                finally { _open = null; }
            }
        }

        private AboutDialog(Form owner)
        {
            bool dark = Theme.IsDark;
            _back = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
            _card = dark ? Color.FromArgb(43, 43, 43) : Color.White;
            _border = dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(229, 229, 229);
            _text = dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(27, 27, 27);
            _muted = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
            _versionText = dark ? Theme.Blend(Theme.Accent, Color.White, 0.45f) : Theme.Blend(Theme.Accent, Color.Black, 0.35f);

            Text = "Info über WindowGrid";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AutoScaleMode = AutoScaleMode.None;
            // Normaler Fensterrahmen wie im Einstellungsfenster (Icon mit gleichem Abstand);
            // die Größe wird in OnLoad festgesetzt.
            FormBorderStyle = FormBorderStyle.Sizable;
            SizeGripStyle = SizeGripStyle.Hide;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = owner == null;
            StartPosition = owner != null && owner.Visible ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            BackColor = _back;
            Font = new Font("Segoe UI", 9.5f);
            KeyPreview = true;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
            int W = S(480);

            int y = S(24);
            PictureBox logo = new PictureBox();
            logo.Image = SignatureFooter.Logo;
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Location = new Point(0, y);
            logo.Size = new Size(W, S(130));
            logo.BackColor = _back;
            if (logo.Image == null)
            {
                try { logo.Image = Theme.IconToBitmap(Icon, S(128)); } catch { }
            }
            Controls.Add(logo);
            y += S(146);

            Label title = new Label();
            title.Text = "WindowGrid";
            title.Font = new Font("Segoe UI Semibold", 15f);
            title.ForeColor = _text;
            title.BackColor = _back;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.Location = new Point(0, y);
            title.Size = new Size(W, S(36));
            Controls.Add(title);
            y += S(38);

            Label version = new Label();
            version.Text = "Version " + AppInfo.Version;
            version.Font = new Font("Segoe UI", 10.5f);
            version.ForeColor = _versionText;
            version.BackColor = _back;
            version.TextAlign = ContentAlignment.MiddleCenter;
            version.Location = new Point(0, y);
            version.Size = new Size(W, S(26));
            Controls.Add(version);
            y += S(40);

            // ---------- Karte mit Eckdaten ----------
            string built;
            try { built = File.GetLastWriteTime(Application.ExecutablePath).ToString("dd.MM.yyyy"); }
            catch { built = "-"; }
            string[,] rows =
            {
                { "Entwickler", "Günther Schoder" },
                { "Erstellt", built },
                { "Farbschema", (dark ? "dunkel" : "hell") + " (folgt Windows)" },
                { "Rechte", Autostart.IsElevated ? "Administrator" : "Standard" },
                { "Programm", AppInfo.ExeDirectory }
            };
            int rowCount = rows.GetLength(0);
            InfoCard card = new InfoCard(k, _card, _border, _back);
            card.Location = new Point(S(24), y);
            card.Size = new Size(W - S(48), S(18) * 2 + rowCount * S(26));
            ToolTip tip = new ToolTip();
            for (int i = 0; i < rowCount; i++)
            {
                int ry = S(18) + i * S(26);
                Label n = new Label();
                n.Text = rows[i, 0];
                n.ForeColor = _muted;
                n.BackColor = _card;
                n.TextAlign = ContentAlignment.MiddleLeft;
                n.Bounds = new Rectangle(S(24), ry, S(118), S(26));
                card.Controls.Add(n);

                Label v = new Label();
                v.Text = rows[i, 1];
                v.ForeColor = _text;
                v.BackColor = _card;
                v.TextAlign = ContentAlignment.MiddleLeft;
                v.AutoEllipsis = true;
                v.UseMnemonic = false;
                v.Bounds = new Rectangle(S(24) + S(118), ry, card.Width - S(24) * 2 - S(118), S(26));
                tip.SetToolTip(v, rows[i, 1]);
                card.Controls.Add(v);
            }
            Controls.Add(card);
            y += card.Height + S(24);

            // ---------- Knöpfe ----------
            DialogButton manual = new DialogButton("Anleitung", MenuHelper.Document, false, k, this);
            manual.Click += delegate { Open(Path.Combine(AppInfo.ExeDirectory, "Anleitung.txt"), true); };
            DialogButton backups = new DialogButton("Sicherungen", MenuHelper.Folder, false, k, this);
            backups.Click += delegate
            {
                string dir = Path.Combine(AppInfo.ExeDirectory, "Backup");
                if (!Directory.Exists(dir)) dir = AppInfo.ExeDirectory;
                Open(dir, false);
            };
            _close = new DialogButton("Schließen", '\0', true, k, this);
            _close.Click += delegate { Close(); };
            int gap = S(14);
            int total = manual.Width + backups.Width + _close.Width + 2 * gap;
            int bx = (W - total) / 2;
            manual.Location = new Point(bx, y);
            backups.Location = new Point(manual.Right + gap, y);
            _close.Location = new Point(backups.Right + gap, y);
            Controls.Add(manual);
            Controls.Add(backups);
            Controls.Add(_close);
            y += _close.Height + S(22);

            Label copy = new Label();
            copy.Text = "© " + DateTime.Now.Year + " Günther Schoder";
            copy.ForeColor = _muted;
            copy.BackColor = _back;
            copy.TextAlign = ContentAlignment.MiddleCenter;
            copy.Location = new Point(0, y);
            copy.Size = new Size(W, S(24));
            Controls.Add(copy);
            y += S(24) + S(20);

            ClientSize = new Size(W, y);
        }

        private int S(float v) { return (int)Math.Round(v * k); }

        internal Color BackColorUi { get { return _back; } }
        internal Color CardColorUi { get { return _card; } }
        internal Color BorderColorUi { get { return _border; } }
        internal Color TextColorUi { get { return _text; } }

        private static void Open(string path, bool notepadFallback)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                if (notepadFallback)
                {
                    try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.UseDarkTitleBar(Handle, Theme.IsDark);
        }

        // Feste Größe: am Rand keine Pfeile zum Größe-Ändern anzeigen (Randbereiche als Client-Bereich melden)
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
            // Feste Größe trotz normalem Rahmen
            MinimumSize = Size;
            MaximumSize = Size;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _close.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        /// <summary>Karte mit abgerundeten Ecken und feinem Rahmen (wie CardPanel in den anderen Programmen).</summary>
        private sealed class InfoCard : Panel
        {
            private readonly float k;
            private readonly Color _fill, _border, _outside;

            public InfoCard(float scale, Color fill, Color border, Color outside)
            {
                k = scale;
                _fill = fill;
                _border = border;
                _outside = outside;
                BackColor = fill;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(_outside);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (GraphicsPath p = Theme.RoundRect(r, 10 * k))
                {
                    using (SolidBrush b = new SolidBrush(_fill)) g.FillPath(b, p);
                    using (Pen pen = new Pen(_border)) g.DrawPath(pen, p);
                }
            }
        }

        /// <summary>Knopf mit Symbol, Breite passt sich dem Text an (wie ModernButton in den anderen Programmen).</summary>
        private sealed class DialogButton : Button
        {
            private readonly float k;
            private readonly AboutDialog _owner;
            private bool _hover, _down;
            private readonly bool _primary;
            private readonly char _glyph;

            public DialogButton(string text, char glyph, bool primary, float scale, AboutDialog owner)
            {
                k = scale;
                _owner = owner;
                Text = text;
                _glyph = glyph;
                _primary = primary;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Font = new Font("Segoe UI Semibold", 9.5f);
                Cursor = Cursors.Hand;
                AutoSize = false;
                int tw = TextRenderer.MeasureText(Text, Font).Width;
                int icon = _glyph == '\0' ? 0 : (int)(26 * k);
                Size = new Size(tw + icon + (int)(36 * k), (int)(40 * k));
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(_owner.BackColorUi);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                bool dark = Theme.IsDark;
                Color fill, text, border;
                if (_primary)
                {
                    // Akzentfarbe wie Windows 11: hell = etwas dunkler, dunkel = etwas heller
                    Color baseColor = dark ? Theme.Blend(Theme.Accent, Color.White, 0.35f) : Theme.Blend(Theme.Accent, Color.Black, 0.12f);
                    fill = _down ? Theme.Blend(baseColor, dark ? Color.Black : Color.White, 0.18f)
                         : _hover ? Theme.Blend(baseColor, dark ? Color.White : Color.Black, 0.08f)
                         : baseColor;
                    text = Theme.ContrastText(fill);
                    border = fill;
                }
                else
                {
                    Color card = _owner.CardColorUi;
                    fill = _down ? Theme.Blend(card, dark ? Color.White : Color.Black, 0.08f)
                         : _hover ? Theme.Blend(card, dark ? Color.White : Color.Black, 0.04f)
                         : card;
                    text = _owner.TextColorUi;
                    border = _owner.BorderColorUi;
                }
                using (GraphicsPath p = Theme.RoundRect(r, 6 * k))
                {
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                    using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
                }

                int tw = TextRenderer.MeasureText(Text, Font).Width;
                int icon = _glyph == '\0' ? 0 : (int)(26 * k);
                int x = (Width - tw - icon) / 2;
                if (_glyph != '\0')
                {
                    int gs = (int)(16 * k);
                    Image img = MenuHelper.Glyph(_glyph, text, gs);
                    if (img != null)
                    {
                        g.DrawImage(img, x, (Height - gs) / 2, gs, gs);
                        img.Dispose();
                    }
                    x += icon;
                }
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, tw + 2, Height), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
    }
}