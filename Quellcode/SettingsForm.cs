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
    /// <summary>Einstellungsfenster – passt sich dem hellen/dunklen Windows-Design an.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Settings _original;
        private readonly Settings _edit;
        private readonly float _sc;
        public event EventHandler Saved;

        /// <summary>Nach dem Speichern soll WindowGrid mit Adminrechten neu gestartet werden.</summary>
        public bool RestartElevatedRequested;

        private Stepper _cols, _rows, _gap;
        private GridPreview _preview;
        private ToggleSwitch _resizeOn, _moveOn, _centerOn, _runAdmin;
        private HotkeyBox _resizeKey, _moveKey, _centerKey;
        private Label _keyHint;
        private Slider _dim;
        private Label _dimValue;
        private ToggleSwitch _sysAccent, _showSize, _showMargins, _animate, _autostart;
        private ColorSwatch _swatch;
        private ModernButton _btnSave, _btnCancel, _btnDefaults;
        private Label _error; // rote Fehlerzeile links neben den Knöpfen
        private Card[] _cards;
        private PictureBox _logo;
        private UpdateBar _bar;
        private bool _barShown;
        private readonly EventHandler _onUpdateChanged;

        public SettingsForm(Settings settings, Icon icon)
        {
            _original = settings;
            _edit = settings.Clone();

            using (Graphics g = CreateGraphics()) _sc = g.DpiX / 96f;

            AutoScaleMode = AutoScaleMode.None;
            Text = "WindowGrid – Einstellungen";
            Icon = icon;
            // Normaler Fensterrahmen wie in Monitor Dimmer / Fensterpositionen (Icon sitzt dann
            // mit gleichem Abstand in der Titelleiste); Größe wird in OnLoad festgesetzt.
            FormBorderStyle = FormBorderStyle.Sizable;
            SizeGripStyle = SizeGripStyle.Hide;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(760), S(858));
            Font = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
            KeyPreview = true;
            DoubleBuffered = true;

            BuildUi(icon);
            LoadValues();
            ApplyTheme();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // Hinweisleiste oben, wenn auf GitHub eine neuere Version liegt (wie in Fensterpositionen)
            _onUpdateChanged = delegate { if (!IsDisposed) ShowUpdateBar(Updater.Available != null); };
            Updater.AvailableChanged += _onUpdateChanged;
            ShowUpdateBar(!Demo.Active && Updater.Available != null);
        }

        private void ShowUpdateBar(bool show)
        {
            ReleaseInfo r = Updater.Available;
            show = show && r != null;
            if (show)
            {
                _bar.Text = "Neue Version " + r.Version + " verfügbar – installiert ist " + AppInfo.Version + ".";
                _bar.Invalidate();
            }
            if (show == _barShown) return;
            int d = S(64) * (show ? 1 : -1);
            foreach (Control c in Controls)
                if (c != _bar && c.Dock == DockStyle.None && c.Top >= S(80)) c.Top += d;
            bool fixedSize = MinimumSize != Size.Empty;
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + d);
            if (fixedSize) { MinimumSize = Size; MaximumSize = Size; }
            if (IsHandleCreated)
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                if (Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
            }
            _bar.Visible = show;
            _barShown = show;
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }


        // ------------------------------------------------------------------ Aufbau

        private void BuildUi(Icon icon)
        {
            // Kopfbereich
            _logo = new PictureBox();
            _logo.SizeMode = PictureBoxSizeMode.Zoom;
            _logo.Bounds = new Rectangle(S(24), S(20), S(44), S(44));
            try { _logo.Image = Theme.IconToBitmap(icon, S(48)); } catch { }
            Controls.Add(_logo);

            // Name ohne Innenabstand gezeichnet (wie gemessen) – so stimmt der Abstand zur Versions-Plakette genau.
            // Links um den früheren Innenabstand eingerückt, damit der Name an derselben Stelle steht wie bisher.
            Label title = new NoPaddingLabel();
            title.Text = "WindowGrid";
            title.AutoSize = true;
            title.Font = Theme.UiFont(22f * _sc, FontStyle.Bold);
            title.UseMnemonic = false;
            TextFormatFlags np = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Size ts = TextRenderer.MeasureText(title.Text, title.Font, Size.Empty, np);
            int indent = (TextRenderer.MeasureText(title.Text, title.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width - ts.Width) / 2;
            title.Location = new Point(S(80) + indent, S(18));
            Controls.Add(title);

            Label subtitle = MakeLabel("Fenster beim Ziehen per Raster anordnen", 13.5f, false, true);
            subtitle.Location = new Point(S(82), S(48));
            Controls.Add(subtitle);

            // Versionsnummer als Pille – Klick öffnet "Info über WindowGrid"
            VersionPill version = new VersionPill();
            version.Text = "Version " + AppInfo.Version;
            int vw;
            using (Font vf = Theme.UiFont(10.5f * _sc, FontStyle.Regular))
                vw = TextRenderer.MeasureText(version.Text, vf, Size.Empty, np).Width + S(16); // Innenabstand je 8
            // direkt rechts neben dem Programmnamen (globale Vorgabe, Referenz Wake on LAN HeaderPanel), senkrecht mittig.
            // Abstand ab dem letzten sichtbaren Pixel des Namens: 12 × Skalierung + 1 für den Rand des letzten Buchstabens
            // (wie bei Wake on LAN sichtbar). Nicht ab der gemessenen Textbreite: Bei "Segoe UI Variable" meldet
            // TextRenderer für Halbfett eine zu große Breite, der Abstand wirkte dadurch viel größer.
            int inkRight = NoPaddingLabel.InkRight(title);
            int textEnd = inkRight > 0 ? inkRight : ts.Width;
            version.Bounds = new Rectangle(title.Left + textEnd + S(13), title.Top + (ts.Height - S(21)) / 2, vw, S(21));
            version.Click += delegate { AboutDialog.ShowFor(this); };
            Controls.Add(version);

            // Hinweisleiste "Neue Version verfügbar" (nur sichtbar, wenn es ein Update gibt)
            _bar = new UpdateBar(_sc);
            _bar.Bounds = new Rectangle(S(24), S(84), S(712), S(52));
            _bar.Visible = false;
            _bar.Install.Click += delegate
            {
                Updater.InstallInteractive(this, this, delegate (bool busy)
                {
                    _bar.Install.Enabled = _bar.Page.Enabled = _bar.Later.Enabled = !busy;
                    if (busy) _bar.Text = "Lade Version " + Updater.Available.Version + " …";
                    else ShowUpdateBar(true);
                    _bar.Invalidate();
                });
            };
            _bar.Page.Click += delegate { Updater.OpenPage(); };
            _bar.Later.Click += delegate { ShowUpdateBar(false); };
            Controls.Add(_bar);

            int top = S(88);
            int colW = S(348);
            int leftX = S(24), rightX = S(388);

            // --- Karte: Raster
            Card grid = MakeCard("Raster", new Rectangle(leftX, top, colW, S(480)));
            _cols = MakeStepper(1, 100, null);
            _rows = MakeStepper(1, 100, null);
            _gap = MakeStepper(0, 64, "px");
            AddRow(grid, "Spalten", _cols, S(54));
            AddRow(grid, "Zeilen", _rows, S(96));
            AddRow(grid, "Abstand", _gap, S(138));
            _preview = new GridPreview();
            _preview.UiScale = _sc;
            _preview.Bounds = new Rectangle(S(18), S(190), colW - S(36), S(270));
            grid.Controls.Add(_preview);

            // --- Karte: Allgemein
            Card general = MakeCard(null, new Rectangle(leftX, top + S(496), colW, S(140)));
            _autostart = MakeToggle();
            AddRow(general, "Mit Windows starten", _autostart, S(20));
            _runAdmin = MakeToggle();
            Label adminLabel = AddRow(general, "Mit Administratorrechten", _runAdmin, S(58));
            ToolTip adminTip = new ModernToolTip();
            adminTip.SetToolTip(adminLabel,
                "Nötig, damit WindowGrid auch Fenster von Programmen anordnen kann,\n" +
                "die als Administrator laufen. Der Autostart läuft dann über die\n" +
                "Aufgabenplanung – Windows fragt nur beim Einrichten einmal nach.");
            ModernButton snapButton = new ModernButton();
            snapButton.Text = "Öffnen …";
            snapButton.Size = new Size(S(100), S(30));
            Label snapLabel = AddRow(general, "Windows-Andocklayouts", snapButton, S(96));
            snapButton.Click += delegate { SnapBar.OpenWindowsSettings(); };
            adminTip.SetToolTip(snapLabel,
                "Öffnet Einstellungen → System → Multitasking. Dort den Haken bei\n" +
                "„Andocklayouts anzeigen, wenn ich ein Fenster an den oberen Bildschirmrand ziehe“\n" +
                "entfernen, damit sich die Windows-Leiste nicht mit dem Raster überlagert.");
            adminTip.SetToolTip(snapButton, "Windows-Einstellung für die Andocklayouts öffnen");

            // --- Karte: Bedienung
            Card input = MakeCard("Bedienung", new Rectangle(rightX, top, colW, S(330)));
            Label hint = MakeLabel("Beim Ziehen eines Fensters (linke Maustaste):", 12.5f, false, true);
            hint.Location = new Point(S(18), S(44));
            input.Controls.Add(hint);
            _resizeOn = MakeToggle();
            _moveOn = MakeToggle();
            _resizeKey = MakeHotkey();
            _moveKey = MakeHotkey();
            AddRow(input, "Größe anpassen", _resizeOn, S(80));
            PlaceHotkey(input, _resizeKey, _resizeOn);
            AddRow(input, "Verschieben", _moveOn, S(126));
            PlaceHotkey(input, _moveKey, _moveOn);
            _centerOn = MakeToggle();
            _centerKey = MakeHotkey();
            AddRow(input, "Zentrieren", _centerOn, S(172));
            PlaceHotkey(input, _centerKey, _centerOn);
            ModernButton zones = new ModernButton();
            zones.Text = "Bearbeiten …";
            zones.Size = new Size(S(124), S(30));
            AddRow(input, "Schnellzonen 1–9", zones, S(218));
            zones.Click += delegate { EditZones(); };
            _keyHint = MakeLabel(KeyHintText, 12.5f, false, true);
            _keyHint.Location = new Point(S(18), S(262));
            input.Controls.Add(_keyHint);

            // --- Karte: Darstellung
            Card look = MakeCard("Darstellung", new Rectangle(rightX, top + S(346), colW, S(290)));
            _dim = new Slider();
            _dim.Minimum = 0;
            _dim.Maximum = 80;
            _dim.Size = new Size(S(150), S(24));
            _dimValue = MakeLabel("35 %", 13f, false, true);
            _dimValue.AutoSize = false;
            _dimValue.TextAlign = ContentAlignment.MiddleRight;
            _dimValue.Size = new Size(S(44), S(24));
            AddRow(look, "Abdunkelung", _dim, S(54), S(52));
            _dimValue.Location = new Point(colW - S(18) - _dimValue.Width, S(54));
            look.Controls.Add(_dimValue);

            _sysAccent = MakeToggle();
            AddRow(look, "Windows-Akzentfarbe", _sysAccent, S(94));
            _swatch = new ColorSwatch();
            _swatch.Size = new Size(S(112), S(28));
            AddRow(look, "Eigene Farbe", _swatch, S(132));
            _showSize = MakeToggle();
            AddRow(look, "Größe anzeigen", _showSize, S(172));
            _showMargins = MakeToggle();
            AddRow(look, "Freie Felder zum Rand anzeigen", _showMargins, S(208));
            _animate = MakeToggle();
            AddRow(look, "Einblenden animieren", _animate, S(244));

            _cards = new[] { grid, general, input, look };

            // --- Schaltflächen
            int by = S(742);
            _btnDefaults = MakeButton("Standardwerte", false, new Rectangle(leftX, by, S(140), S(34)));
            // "Abbrechen" links, "Speichern" (primär) ganz rechts, 10 Abstand; links davon die Fehlerzeile
            _btnCancel = MakeButton("Abbrechen", false, new Rectangle(S(736) - S(120) - S(10) - S(120), by, S(120), S(34)));
            _btnSave = MakeButton("Speichern", true, new Rectangle(S(736) - S(120), by, S(120), S(34)));
            _error = MakeLabel("", 12.5f, false, false);
            _error.AutoSize = false;
            _error.AutoEllipsis = true;
            _error.TextAlign = ContentAlignment.MiddleLeft;
            _error.Tag = "error";
            _error.Bounds = new Rectangle(leftX + S(140) + S(12), by, _btnCancel.Left - S(12) - (leftX + S(140) + S(12)), S(34));
            Controls.Add(_error);
            _btnDefaults.Click += delegate { Settings d = new Settings(); d.FirstRun = false; d.Paused = _edit.Paused; d.LastSelection = _edit.LastSelection; d.LastUpdateCheck = _edit.LastUpdateCheck; d.UpdateCheck = _edit.UpdateCheck; _edit.CopyFrom(d); LoadValues(false); };
            _btnCancel.Click += delegate { Close(); };
            _btnSave.Click += delegate { Save(); };

            // --- Fußzeile (einheitlich in allen Projekten)
            SignatureFooter footer = new SignatureFooter(_sc);
            footer.Dock = DockStyle.Bottom;   // volle Breite, unterhalb des Fensterinhalts
            footer.Height = S(64);
            Controls.Add(footer);

            // Live-Vorschau
            EventHandler preview = delegate { UpdatePreview(); };
            _cols.ValueChanged += preview;
            _rows.ValueChanged += preview;
            _gap.ValueChanged += preview;
            _dim.ValueChanged += delegate { _dimValue.Text = _dim.Value + " %"; UpdatePreview(); };
            _sysAccent.CheckedChanged += delegate { _swatch.Enabled = !_sysAccent.Checked; UpdatePreview(); };
            _swatch.ColorChanged += preview;
            _resizeOn.CheckedChanged += delegate { _resizeKey.Enabled = _resizeOn.Checked; };
            _moveOn.CheckedChanged += delegate { _moveKey.Enabled = _moveOn.Checked; };
            _resizeKey.KeyChanged += delegate { CheckKeyConflict(); };
            _moveKey.KeyChanged += delegate { CheckKeyConflict(); };
            _centerOn.CheckedChanged += delegate { _centerKey.Enabled = _centerOn.Checked; };
            _centerKey.KeyChanged += delegate { CheckKeyConflict(); };
        }

        private Card MakeCard(string title, Rectangle bounds)
        {
            Card c = new Card();
            c.Title = title;
            c.UiScale = _sc;
            c.Bounds = bounds;
            Controls.Add(c);
            return c;
        }

        private Label MakeLabel(string text, float size, bool bold, bool sub)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Theme.UiFont(size * _sc, bold ? FontStyle.Bold : FontStyle.Regular);
            l.Tag = sub ? "sub" : null;
            l.UseMnemonic = false;
            return l;
        }

        private Stepper MakeStepper(int min, int max, string suffix)
        {
            Stepper s = new Stepper();
            s.Minimum = min;
            s.Maximum = max;
            s.Suffix = suffix;
            s.Size = new Size(S(132), S(32));
            return s;
        }

        private ToggleSwitch MakeToggle()
        {
            ToggleSwitch t = new ToggleSwitch();
            t.Size = new Size(S(44), S(22));
            return t;
        }

        private HotkeyBox MakeHotkey()
        {
            HotkeyBox h = new HotkeyBox();
            h.Size = new Size(S(112), S(30));
            return h;
        }

        /// <summary>Tastenfeld links neben den Schalter derselben Zeile setzen.</summary>
        private void PlaceHotkey(Card card, HotkeyBox box, Control toggle)
        {
            box.Location = new Point(toggle.Left - S(16) - box.Width, toggle.Top + (toggle.Height - box.Height) / 2);
            card.Controls.Add(box);
        }

        private const string KeyHintText =
            "Im Raster: Pfeile verschieben, Umschalt+Pfeile ändern die Größe, Größe-Taste doppelt = letzte Auswahl, Esc blendet aus.";

        /// <summary>Schnellzonen-Fenster öffnen; Änderungen werden mit "Speichern" übernommen.</summary>
        private void EditZones()
        {
            using (ZonesForm f = new ZonesForm(_edit.Zones, Icon))
                f.ShowDialog(this);
        }

        private void CheckKeyConflict()
        {
            bool same = _resizeKey.Key == _moveKey.Key || _resizeKey.Key == _centerKey.Key || _moveKey.Key == _centerKey.Key;
            _keyHint.Text = same
                ? "Zwei Funktionen haben dieselbe Taste – bitte unterschiedliche Tasten wählen."
                : KeyHintText;
            _keyHint.MaximumSize = new Size(S(312), 0);
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

        /// <summary>Zeile mit Beschriftung links und Steuerelement rechtsbündig.</summary>
        private Label AddRow(Card card, string text, Control control, int y)
        {
            return AddRow(card, text, control, y, 0);
        }

        private Label AddRow(Card card, string text, Control control, int y, int rightReserve)
        {
            int rowH = Math.Max(control.Height, S(24));
            Label l = MakeLabel(text, 13.5f, false, false);
            card.Controls.Add(l);
            l.Location = new Point(S(18), y + (rowH - l.PreferredHeight) / 2);
            control.Location = new Point(card.Width - S(18) - rightReserve - control.Width, y + (rowH - control.Height) / 2);
            card.Controls.Add(control);
            l.Click += delegate
            {
                ToggleSwitch t = control as ToggleSwitch;
                if (t != null && t.Enabled) t.Checked = !t.Checked;
                else control.Focus();
            };
            return l;
        }

        // ------------------------------------------------------------------ Werte

        private void LoadValues()
        {
            LoadValues(true);
        }

        private void LoadValues(bool includeAutostart)
        {
            _cols.Value = _edit.Columns;
            _rows.Value = _edit.Rows;
            _gap.Value = _edit.Gap;
            _resizeOn.Checked = _edit.ResizeEnabled;
            _resizeKey.Key = _edit.ResizeKey;
            _resizeKey.Enabled = _edit.ResizeEnabled;
            _moveOn.Checked = _edit.MoveEnabled;
            _moveKey.Key = _edit.MoveKey;
            _moveKey.Enabled = _edit.MoveEnabled;
            _centerOn.Checked = _edit.CenterEnabled;
            _centerKey.Key = _edit.CenterKey;
            _centerKey.Enabled = _edit.CenterEnabled;
            _runAdmin.Checked = _edit.RunAsAdmin;
            CheckKeyConflict();
            _dim.Value = _edit.Dim;
            _dimValue.Text = _edit.Dim + " %";
            _sysAccent.Checked = _edit.UseSystemAccent;
            _swatch.Color = _edit.CustomColor;
            _swatch.Enabled = !_edit.UseSystemAccent;
            _showSize.Checked = _edit.ShowSize;
            _showMargins.Checked = _edit.ShowMargins;
            _animate.Checked = _edit.Animate;
            if (includeAutostart) _autostart.Checked = Demo.Active || Autostart.IsEnabled;
            UpdatePreview();
        }

        private void ReadValues()
        {
            _edit.Columns = _cols.Value;
            _edit.Rows = _rows.Value;
            _edit.Gap = _gap.Value;
            _edit.ResizeEnabled = _resizeOn.Checked;
            _edit.ResizeKey = _resizeKey.Key;
            _edit.MoveEnabled = _moveOn.Checked;
            _edit.MoveKey = _moveKey.Key;
            _edit.CenterEnabled = _centerOn.Checked;
            _edit.CenterKey = _centerKey.Key;
            _edit.RunAsAdmin = _runAdmin.Checked;
            _edit.Dim = _dim.Value;
            _edit.UseSystemAccent = _sysAccent.Checked;
            _edit.CustomColor = _swatch.Color;
            _edit.ShowSize = _showSize.Checked;
            _edit.ShowMargins = _showMargins.Checked;
            _edit.Animate = _animate.Checked;
        }

        private Color CurrentAccent
        {
            get { return _sysAccent.Checked ? Theme.SystemAccent : _swatch.Color; }
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            _preview.Columns = _cols.Value;
            _preview.Rows = _rows.Value;
            _preview.Gap = _gap.Value;
            _preview.Dim = _dim.Value;
            _preview.Accent = CurrentAccent;
            Theme.Accent = CurrentAccent;
            _preview.Invalidate();
            InvalidateAll(this);
        }

        private void Save()
        {
            _error.Text = "";
            // erst prüfen, dann speichern – bei einem Fehler bleibt das Fenster offen
            int[] keys = { _resizeOn.Checked ? _resizeKey.Key : -1, _moveOn.Checked ? _moveKey.Key : -2, _centerOn.Checked ? _centerKey.Key : -3 };
            if (keys[0] == keys[1] || keys[0] == keys[2] || keys[1] == keys[2])
            {
                _error.Text = "Zwei Funktionen haben dieselbe Taste.";
                return;
            }
            ReadValues();
            // Die letzte Auswahl merkt sich WindowGrid laufend – nicht mit dem Stand beim Öffnen überschreiben
            _edit.LastSelection = _original.LastSelection;
            _edit.SnapBarOriginal = _original.SnapBarOriginal;
            _edit.LastUpdateCheck = _original.LastUpdateCheck;
            _edit.UpdateCheck = _original.UpdateCheck; // wird im Info-Fenster eingestellt
            bool adminChanged = _edit.RunAsAdmin != _original.RunAsAdmin;
            bool autoWanted = _autostart.Checked;
            bool autoNow = Autostart.IsEnabled;
            _original.CopyFrom(_edit);
            _original.Save();

            // Autostart (bei Adminrechten über die Aufgabenplanung – Windows fragt einmal nach)
            if (autoWanted != autoNow || (autoWanted && adminChanged))
            {
                Cursor = Cursors.WaitCursor;
                bool ok = Autostart.Set(autoWanted, _edit.RunAsAdmin);
                Cursor = Cursors.Default;
                if (!ok)
                {
                    // übrige Einstellungen sind gespeichert; das Fenster bleibt offen, damit man es erneut versuchen kann
                    if (Saved != null) Saved(this, EventArgs.Empty);
                    _error.Text = "Autostart nicht eingerichtet – Windows-Nachfrage abgebrochen?";
                    return;
                }
            }

            // Jetzt sofort mit Adminrechten neu starten?
            if (_edit.RunAsAdmin && !Autostart.IsElevated)
            {
                DialogResult r = MessageBox.Show(this,
                    "WindowGrid läuft gerade ohne Administratorrechte.\n\nJetzt mit Administratorrechten neu starten?",
                    "WindowGrid", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                RestartElevatedRequested = r == DialogResult.Yes;
            }

            if (Saved != null) Saved(this, EventArgs.Empty);
            Close();
        }

        // ------------------------------------------------------------------ Design

        private void ApplyTheme()
        {
            Palette p = Theme.Current;
            Theme.Accent = CurrentAccent;
            BackColor = p.Back;
            ForeColor = p.Text;
            ApplyTheme(this, p);
            if (IsHandleCreated) NativeMethods.UseDarkTitleBar(Handle, p.Dark);
            InvalidateAll(this);
        }

        private static void ApplyTheme(Control parent, Palette p)
        {
            foreach (Control c in parent.Controls)
            {
                bool onCard = parent is Card;
                if (!(c is Card)) c.BackColor = onCard ? p.Card : p.Back;
                if (c is Label)
                {
                    string tag = c.Tag as string;
                    c.ForeColor = tag == "error" ? ErrorColor(p) : tag == "sub" ? p.SubText : p.Text;
                }
                if (c is GridPreview) c.BackColor = p.Card;
                if (c.Controls.Count > 0) ApplyTheme(c, p);
            }
        }

        /// <summary>Rot für Fehlermeldungen (Windows 11: hell #C42B1C, dunkel #FF99A4).</summary>
        private static Color ErrorColor(Palette p)
        {
            return p.Dark ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28);
        }

        private static void InvalidateAll(Control c)
        {
            c.Invalidate();
            foreach (Control child in c.Controls) InvalidateAll(child);
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
            _btnSave.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool capturing = _resizeKey.Capturing || _moveKey.Capturing || _centerKey.Capturing;
            // Esc = Abbrechen, Enter = Speichern (außer ein Knopf hat den Fokus – der löst selbst aus)
            if (e.KeyCode == Keys.Escape && !capturing) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && !capturing && !(ActiveControl is ModernButton))
            {
                Save();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                Updater.AvailableChanged -= _onUpdateChanged;
                if (_logo != null && _logo.Image != null) _logo.Image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    /// <summary>Label, das seinen Text ohne Innenabstand zeichnet und misst (TextRenderer mit NoPadding).</summary>
    internal sealed class NoPaddingLabel : Label
    {
        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        public NoPaddingLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return TextRenderer.MeasureText(Text, Font, Size.Empty, Flags);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, Point.Empty, ForeColor, BackColor, Flags);
        }

        /// <summary>Rechter Rand der tatsächlich gezeichneten Schrift (Pixel ab links, 0 = nicht ermittelbar).</summary>
        public static int InkRight(Label label)
        {
            Size size = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, Flags);
            int w = size.Width + size.Height, h = size.Height; // mit Reserve, falls die Messung zu knapp ist
            if (w <= 0 || h <= 0) return 0;
            try
            {
                using (Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        TextRenderer.DrawText(g, label.Text, label.Font, Point.Empty, Color.Black, Color.White, Flags);
                    }
                    for (int x = w - 1; x >= 0; x--)
                        for (int y = 0; y < h; y++)
                        {
                            Color c = bmp.GetPixel(x, y);
                            if (c.R < 160 || c.G < 160 || c.B < 160) return x + 1;
                        }
                }
            }
            catch { }
            return 0;
        }
    }

    /// <summary>
    /// Hinweisleiste "Neue Version verfügbar" mit "Jetzt aktualisieren", "Release-Seite" und "Später"
    /// (Vorlage: Fensterpositionen wiederherstellen).
    /// </summary>
    internal sealed class UpdateBar : Control
    {
        public readonly ModernButton Install = new ModernButton();
        public readonly ModernButton Page = new ModernButton();
        public readonly ModernButton Later = new ModernButton();
        private readonly float _sc;

        public UpdateBar(float scale)
        {
            _sc = scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Install.Text = "Jetzt aktualisieren";
            Install.Primary = true;
            Page.Text = "Release-Seite";
            Later.Text = "Später";
            Install.Size = new Size(S(150), S(32));
            Page.Size = new Size(S(116), S(32));
            Later.Size = new Size(S(80), S(32));
            Controls.Add(Install);
            Controls.Add(Page);
            Controls.Add(Later);
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Width - S(10);
            foreach (ModernButton b in new[] { Later, Page, Install })
            {
                x -= b.Width;
                b.Location = new Point(x, (Height - b.Height) / 2);
                x -= S(8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Palette p = Theme.Current;
            g.Clear(p.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Theme.RoundRect(r, 8f * _sc))
            using (SolidBrush brush = new SolidBrush(Theme.Blend(p.Back, Theme.Accent, 0.14f)))
            using (Pen pen = new Pen(Theme.Blend(p.Back, Theme.Accent, 0.45f)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            int x = S(16);
            if (Theme.IconFontName != null)
            {
                using (Font icon = new Font(Theme.IconFontName, 14f * _sc, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, MenuHelper.Download.ToString(), icon, new Rectangle(x, 0, S(20), Height), Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                x += S(34);
            }
            int right = Install.Left - S(12);
            using (Font f = Theme.UiFont(13.5f * _sc, FontStyle.Bold))
                TextRenderer.DrawText(g, Text, f, new Rectangle(x, 0, Math.Max(10, right - x), Height), p.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}