// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowGrid
{
    /// <summary>
    /// Einstellungsfenster mit Navigation links (globale Vorgabe, Vorlage Wake on LAN): Raster, Bedienung,
    /// Schnellzonen, Darstellung und Allgemein sind Seiten in einem Fenster. Änderungen werden über alle Seiten
    /// gesammelt und erst mit "Speichern" übernommen. Passt sich dem hellen/dunklen Windows-Design an.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Settings _original;
        private readonly Settings _edit;
        private readonly float _sc;
        public event EventHandler Saved;

        /// <summary>"Beenden" in der Navigation: WindowGrid ganz beenden (das X schließt nur das Fenster).</summary>
        public event EventHandler ExitRequested;

        /// <summary>Nach dem Speichern soll WindowGrid mit Adminrechten neu gestartet werden.</summary>
        public bool RestartElevatedRequested;

        // Maße in logischen Einheiten (× Skalierung)
        private const int NavFull = 230, NavCompact = 68;      // Navigation aus- bzw. eingeklappt
        private const int ContentW = 768;                      // 28 Rand + 712 Inhalt + 28 Rand
        private const int HeaderH = 84, PageH = 520, FooterH = 64;

        private Stepper _cols, _rows, _gap;
        private GridPreview _preview;
        private ToggleSwitch _resizeOn, _moveOn, _centerOn, _runAdmin, _zoneKeys;
        private bool _discard; // "Abbrechen"/Esc: ohne Rückfrage schließen
        private HotkeyBox _resizeKey, _moveKey, _centerKey;
        private Label _keyHint;
        private Slider _dim;
        private Label _dimValue;
        private ToggleSwitch _sysAccent, _showSize, _showMargins, _animate, _autostart;
        private ColorSwatch _swatch;
        private ModernButton _btnSave, _btnCancel;
        private Label _error; // Zeile links neben den Knöpfen: rote Fehler bzw. kurz grün "Gespeichert"
        private Timer _statusTimer;
        private PictureBox _logo;
        private UpdateBar _bar;
        private bool _barShown;
        private readonly EventHandler _onUpdateChanged;

        // Navigation und Seiten
        private Panel _content, _nav;
        private Panel[] _pages;
        private NavButton[] _navItems;
        private NavButton _navMenu, _navPin, _navMore, _navQuit;
        private bool _navOpen;       // ausgeklappt, obwohl nicht angepinnt (liegt dann über dem Inhalt)
        private int _page;
        private Timer _navTimer;
        private ContextMenuStrip _moreMenu;
        private ModernToolTip _tips;
        private ZonesEditor _zones;
        private MonitorsEditor _monitors;
        private ExcludeEditor _excluded;

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
            Font = Theme.UiFont(13.5f * _sc, FontStyle.Regular);
            KeyPreview = true;
            DoubleBuffered = true;
            _navOpen = _original.NavPinned;

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
            _barShown = show;
            LayoutAll();
        }

        private int S(float v) { return (int)Math.Round(v * _sc); }

        // ------------------------------------------------------------------ Aufbau

        private void BuildUi(Icon icon)
        {
            _tips = new ModernToolTip();

            // ---------- Kopfbereich (volle Breite) ----------
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
            version.Text = "Version " + AppInfo.ShownVersion;
            int vw;
            using (Font vf = Theme.UiFont(10.5f * _sc, FontStyle.Regular))
                vw = TextRenderer.MeasureText(version.Text, vf, Size.Empty, np).Width + S(16); // Innenabstand je 8
            // direkt rechts neben dem Programmnamen (globale Vorgabe, Referenz Wake on LAN HeaderPanel), senkrecht mittig.
            // Abstand ab dem letzten sichtbaren Pixel des Namens: 12 × Skalierung + 1 für den Rand des letzten Buchstabens.
            // Nicht ab der gemessenen Textbreite: Die kann (z. B. bei "Segoe UI Variable" Halbfett) größer sein als die
            // gezeichnete Schrift, der Abstand wirkt dann zu groß.
            int inkRight = NoPaddingLabel.InkRight(title);
            int textEnd = inkRight > 0 ? inkRight : ts.Width;
            version.Bounds = new Rectangle(title.Left + textEnd + S(13), title.Top + (ts.Height - S(21)) / 2, vw, S(21));
            version.Click += delegate { AboutDialog.ShowFor(this); };
            Controls.Add(version);

            // ---------- Inhaltsbereich rechts neben der Navigation ----------
            _content = new Panel();
            Controls.Add(_content);

            // Hinweisleiste "Neue Version verfügbar" (nur sichtbar, wenn es ein Update gibt)
            _bar = new UpdateBar(_sc);
            _bar.Bounds = new Rectangle(S(28), 0, S(712), S(52));
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
            _content.Controls.Add(_bar);

            _pages = new Panel[5];
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new Panel();
                _pages[i].Visible = i == 0;
                _content.Controls.Add(_pages[i]);
            }
            int cardX = S(28), cardW = S(712);

            // ================= Seite 1: Raster =================
            Card grid = MakeCard(_pages[0], "Raster", new Rectangle(cardX, 0, cardW, S(190)));
            _cols = MakeStepper(1, 100, null);
            _rows = MakeStepper(1, 100, null);
            _gap = MakeStepper(0, 64, "px");
            // Werte in der linken Hälfte, rechts daneben die Vorschau
            int half = cardW - S(348);
            AddRow(grid, "Spalten", _cols, S(54), half);
            AddRow(grid, "Zeilen", _rows, S(96), half);
            AddRow(grid, "Abstand", _gap, S(138), half);
            _preview = new GridPreview();
            _preview.UiScale = _sc;
            _preview.Bounds = new Rectangle(S(366), S(18), S(328), S(154));
            grid.Controls.Add(_preview);

            Card monitors = MakeCard(_pages[0], "Raster je Monitor", new Rectangle(cardX, S(206), cardW, S(MonitorsEditor.CardHeight)));
            Label monitorsSub = MakeLabel("Sonst gelten Spalten und Zeilen von oben.", 12.5f, false, true);
            monitorsSub.Location = new Point(S(18), S(46));
            monitors.Controls.Add(monitorsSub);
            _monitors = new MonitorsEditor(monitors, _sc);

            // ================= Seite 2: Bedienung =================
            Card input = MakeCard(_pages[1], "Bedienung", new Rectangle(cardX, 0, cardW, S(300)));
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
            _zoneKeys = MakeToggle();
            Label zoneKeysLabel = AddRow(input, "Schnellzonen mit Strg+Win+Alt+1–9", _zoneKeys, S(218));
            _tips.SetToolTip(zoneKeysLabel,
                "Setzt das gerade aktive Fenster ohne Maus auf die Schnellzone 1–9,\n" +
                "auf dem Monitor, auf dem es liegt.");
            _keyHint = MakeLabel(KeyHintText, 12.5f, false, true);
            _keyHint.Location = new Point(S(18), S(258));
            input.Controls.Add(_keyHint);

            // ================= Seite 3: Schnellzonen =================
            Label zonesSub = MakeLabel(
                "Bei sichtbarem Raster Ziffer 1–9 drücken, um das Fenster auf die Zone zu setzen.\n" +
                "Umschalt+Ziffer speichert die aktuelle Auswahl als Zone. Werte in Prozent des Bildschirms.", 12.5f, false, true);
            zonesSub.Location = new Point(S(30), 0);
            _pages[2].Controls.Add(zonesSub);
            ModernButton zoneDefaults = new ModernButton();
            zoneDefaults.Text = "Standardzonen";
            zoneDefaults.Bounds = new Rectangle(cardX + cardW - S(140), S(2), S(140), S(30));
            _pages[2].Controls.Add(zoneDefaults);
            Card zonesCard = MakeCard(_pages[2], null, new Rectangle(cardX, S(44), cardW, S(ZonesEditor.CardHeight)));
            _zones = new ZonesEditor(zonesCard, _sc);
            zoneDefaults.Click += delegate { _zones.Load(QuickZone.Defaults()); };

            // ================= Seite 4: Darstellung =================
            Card look = MakeCard(_pages[3], "Darstellung", new Rectangle(cardX, 0, cardW, S(290)));
            _dim = new Slider();
            _dim.Minimum = 0;
            _dim.Maximum = 80;
            _dim.Size = new Size(S(150), S(24));
            _dimValue = MakeLabel("35 %", 13f, false, true);
            _dimValue.AutoSize = false;
            _dimValue.TextAlign = ContentAlignment.MiddleRight;
            _dimValue.Size = new Size(S(44), S(24));
            AddRow(look, "Abdunkelung", _dim, S(54), S(52));
            _dimValue.Location = new Point(cardW - S(18) - _dimValue.Width, S(54));
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

            // ================= Seite 5: Allgemein =================
            Card general = MakeCard(_pages[4], "Allgemein", new Rectangle(cardX, 0, cardW, S(178)));
            _autostart = MakeToggle();
            AddRow(general, "Mit Windows starten", _autostart, S(54));
            _runAdmin = MakeToggle();
            Label adminLabel = AddRow(general, "Mit Administratorrechten", _runAdmin, S(92));
            _tips.SetToolTip(adminLabel,
                "Nötig, damit WindowGrid auch Fenster von Programmen anordnen kann,\n" +
                "die als Administrator laufen. Der Autostart läuft dann über die\n" +
                "Aufgabenplanung – Windows fragt nur beim Einrichten einmal nach.");
            ModernButton snapButton = new ModernButton();
            snapButton.Text = "Öffnen …";
            snapButton.Size = new Size(S(100), S(30));
            Label snapLabel = AddRow(general, "Windows-Andocklayouts", snapButton, S(130));
            snapButton.Click += delegate { SnapBar.OpenWindowsSettings(); };
            _tips.SetToolTip(snapLabel,
                "Öffnet Einstellungen → System → Multitasking. Dort den Haken bei\n" +
                "„Andocklayouts anzeigen, wenn ich ein Fenster an den oberen Bildschirmrand ziehe“\n" +
                "entfernen, damit sich die Windows-Leiste nicht mit dem Raster überlagert.");
            _tips.SetToolTip(snapButton, "Windows-Einstellung für die Andocklayouts öffnen");

            Card exclude = MakeCard(_pages[4], "Ausnahmen", new Rectangle(cardX, S(194), cardW, S(280)));
            _excluded = new ExcludeEditor(exclude, _sc);
            exclude.Height = S(_excluded.CardHeight);

            // ---------- Schaltflächen unter den Seiten (gelten für alle Seiten zusammen) ----------
            // "Abbrechen" links, "Speichern" (primär) ganz rechts, 10 Abstand; links davon die Statuszeile
            _btnCancel = MakeButton("Abbrechen", false, new Rectangle(0, 0, S(120), S(34)));
            _btnSave = MakeButton("Speichern", true, new Rectangle(0, 0, S(120), S(34)));
            _content.Controls.Add(_btnCancel);
            _content.Controls.Add(_btnSave);
            _error = MakeLabel("", 12.5f, false, false);
            _error.AutoSize = false;
            _error.AutoEllipsis = true;
            _error.TextAlign = ContentAlignment.MiddleLeft;
            _error.Tag = "error";
            _content.Controls.Add(_error);
            _btnCancel.Click += delegate { _discard = true; Close(); };
            _btnSave.Click += delegate { Save(); };

            // ---------- Navigation links ----------
            _nav = new Panel();
            _nav.Paint += delegate (object s, PaintEventArgs e)
            {
                // ausgeklappt über dem Inhalt: feine Trennlinie rechts
                if (_navOpen && !_original.NavPinned)
                    using (Pen pen = new Pen(Theme.Current.CardBorder)) e.Graphics.DrawLine(pen, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height);
            };
            _navMenu = AddNav("", '', delegate
            {
                if (_original.NavPinned) SetNavPinned(false);
                else { _navOpen = !_navOpen; LayoutAll(); }
            });
            _tips.SetToolTip(_navMenu, "Navigation aus- bzw. einklappen");
            _navPin = AddNav("", '', delegate { SetNavPinned(!_original.NavPinned); });
            _navPin.IconOnly = true;
            string[] names = { "Raster", "Bedienung", "Schnellzonen", "Darstellung", "Allgemein" };
            char[] glyphs = { '', MenuHelper.Keyboard, '', '', '' };
            _navItems = new NavButton[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                _navItems[i] = AddNav(names[i], glyphs[i], delegate { SelectPage(index); CloseNav(); });
            }
            _navItems[0].Selected = true;
            // unten: Extras (Zahnrad, öffnet ein Menü rechts über dem Knopf), darunter Beenden
            _navMore = AddNav("Extras", MenuHelper.Settings, ShowMoreMenu);
            _navQuit = AddNav("Beenden", MenuHelper.Power, QuitProgram);
            _tips.SetToolTip(_navQuit, "WindowGrid ganz beenden (das X schließt nur dieses Fenster)");
            Controls.Add(_nav); // liegt ausgeklappt über dem Inhalt

            // ---------- Fußzeile (einheitlich in allen Projekten) ----------
            SignatureFooter footer = new SignatureFooter(_sc);
            footer.Dock = DockStyle.Bottom;   // volle Breite, unterhalb des Fensterinhalts
            footer.Height = S(FooterH);
            Controls.Add(footer);

            // ausgeklappt, nicht angepinnt: wieder einklappen, sobald die Maus die Navigation verlässt
            _navTimer = new Timer();
            _navTimer.Interval = 250;
            _navTimer.Tick += delegate
            {
                if (!_navOpen || _original.NavPinned || !Visible) return;
                if (_moreMenu != null && _moreMenu.Visible) return;
                if (!_nav.RectangleToScreen(_nav.ClientRectangle).Contains(Cursor.Position)) { _navOpen = false; LayoutAll(); }
            };
            _navTimer.Start();

            LayoutAll();

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

        // ------------------------------------------------------------------ Navigation und Seiten

        private NavButton AddNav(string text, char glyph, MethodInvoker click)
        {
            NavButton b = new NavButton(text, glyph, _sc);
            b.Click += delegate { click(); };
            _nav.Controls.Add(b);
            return b;
        }

        /// <summary>Seite anzeigen: 0 Raster, 1 Bedienung, 2 Schnellzonen, 3 Darstellung, 4 Allgemein.</summary>
        public void SelectPage(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            _page = index;
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = i == index;
                _navItems[i].Selected = i == index;
            }
        }

        /// <summary>Ausgeklappte (nicht angepinnte) Navigation nach einer Auswahl wieder einklappen.</summary>
        private void CloseNav()
        {
            if (!_original.NavPinned && _navOpen) { _navOpen = false; LayoutAll(); }
        }

        private void SetNavPinned(bool pinned)
        {
            _original.NavPinned = pinned;
            _edit.NavPinned = pinned;
            if (!Demo.Active) { try { _original.Save(); } catch { } } // gilt sofort, unabhängig von "Speichern"
            _navOpen = pinned;
            LayoutAll();
        }

        /// <summary>Alles anordnen: Navigation (angepinnt schiebt sie den Inhalt), Hinweisleiste, Seiten, Knöpfe, Fenstergröße.</summary>
        private void LayoutAll()
        {
            if (_nav == null || _content == null) return;
            bool pinned = _original.NavPinned;
            bool open = pinned || _navOpen;
            int navW = S(pinned ? NavFull : NavCompact);
            int barH = _barShown ? S(64) : 0;
            int pagesTop = barH + S(4);
            int by = pagesTop + S(PageH) + S(18);
            int contentH = by + S(34) + S(18);

            _content.Bounds = new Rectangle(navW, S(HeaderH), S(ContentW), contentH);
            _bar.Visible = _barShown;
            foreach (Panel page in _pages) page.Bounds = new Rectangle(0, pagesTop, S(ContentW), S(PageH));
            int right = S(ContentW) - S(28);
            _btnSave.Bounds = new Rectangle(right - S(120), by, S(120), S(34));
            _btnCancel.Bounds = new Rectangle(right - S(120) - S(10) - S(120), by, S(120), S(34));
            _error.Bounds = new Rectangle(S(28), by, _btnCancel.Left - S(12) - S(28), S(34));

            SetClient(new Size(navW + S(ContentW), S(HeaderH) + contentH + S(FooterH)));

            _nav.Bounds = new Rectangle(0, S(HeaderH), S(open ? NavFull : NavCompact), contentH);
            _nav.BringToFront();
            _navPin.Visible = open;
            _navPin.Glyph = pinned ? '' : '';
            _tips.SetToolTip(_navPin, pinned ? "Navigation lösen (einklappbar)" : "Navigation anpinnen");
            LayoutNav();
            _nav.Invalidate(true);
        }

        private void LayoutNav()
        {
            int x = S(10), w = _nav.Width - S(20), h = S(40);
            int y = S(4);
            _navMenu.SetBounds(x, y, S(48), h);
            _navPin.SetBounds(_nav.Width - S(10) - S(36), y + (h - S(36)) / 2, S(36), S(36));
            y += h + S(10);
            foreach (NavButton b in _navItems)
            {
                b.SetBounds(x, y, w, h);
                y += h + S(4);
            }
            int bottom = _nav.Height - S(14);
            _navQuit.SetBounds(x, bottom - h, w, h);
            bottom -= h + S(4);
            _navMore.SetBounds(x, bottom - h, w, h);
        }

        /// <summary>Fenstergröße ändern, obwohl sie sonst fest ist; das Fenster bleibt auf dem Bildschirm.</summary>
        private void SetClient(Size size)
        {
            if (ClientSize == size) return;
            bool fixedSize = MinimumSize != Size.Empty;
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            ClientSize = size;
            if (fixedSize) { MinimumSize = Size; MaximumSize = Size; }
            if (IsHandleCreated)
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                if (Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
                if (Right > work.Right) Left = Math.Max(work.Left, work.Right - Width);
            }
        }

        /// <summary>"Extras": Menü rechts über dem Knopf (Info, Anleitung, Protokoll, Export/Import, Standardwerte).</summary>
        private void ShowMoreMenu()
        {
            if (_moreMenu != null) { _moreMenu.Dispose(); _moreMenu = null; }
            MenuTheme theme = MenuTheme.Current();
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new ModernRenderer(theme, _sc);
            m.Font = new Font("Segoe UI", 9.5f);
            m.ShowCheckMargin = false;
            m.ShowItemToolTips = false;
            // Ab Windows 11 zeichnet das System selbst einen Schatten – den alten WinForms-Schatten abschalten
            if (Environment.OSVersion.Version.Build >= 22000) m.DropShadowEnabled = false;
            m.ShowImageMargin = true;
            m.ImageScalingSize = new Size((int)(36 * _sc), (int)(16 * _sc));
            m.Padding = new Padding(0, (int)(4 * _sc), 0, (int)(4 * _sc));

            m.Items.Add(MoreItem(m, theme, "Info über WindowGrid", MenuHelper.Info, delegate { AboutDialog.ShowFor(this); }));
            m.Items.Add(MoreItem(m, theme, "Anleitung öffnen", MenuHelper.Document, delegate { OpenFile(Path.Combine(AppInfo.ExeDirectory, "Anleitung.txt"), "Die Anleitung wurde nicht gefunden."); }));
            m.Items.Add(MoreItem(m, theme, "Fehlerprotokoll öffnen", MenuHelper.Log, delegate { OpenFile(Log.FilePath, "Es gibt noch kein Fehlerprotokoll."); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MoreItem(m, theme, "Einstellungen exportieren …", '', ExportSettings));
            m.Items.Add(MoreItem(m, theme, "Einstellungen importieren …", MenuHelper.Download, ImportSettings));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MoreItem(m, theme, "Standardwerte", MenuHelper.Refresh, ResetDefaults));
            m.Items.Add(new MenuSpacer((int)(6 * _sc)));

            _moreMenu = m;
            _navMore.Selected = true; // ausgewählt, solange das Menü offen ist
            m.Closed += delegate
            {
                if (!_navMore.IsDisposed) _navMore.Selected = false;
                CloseNav();
            };
            m.Show(_navMore, new Point(_navMore.Width + S(4), _navMore.Height), ToolStripDropDownDirection.AboveRight);
        }

        private GlyphMenuItem MoreItem(ContextMenuStrip menu, MenuTheme theme, string text, char glyph, MethodInvoker action)
        {
            GlyphMenuItem item = new GlyphMenuItem(text);
            int pad = (int)(5 * _sc);
            int iconW = (int)(36 * _sc);
            item.Padding = new Padding(0, pad, (int)(8 * _sc), pad);
            item.FixedHeight = TextRenderer.MeasureText("Ag", menu.Font).Height + (int)(8 * _sc);
            if (glyph != '\0') item.Glyph = MenuHelper.Glyph(glyph, theme.Text, (int)(16 * _sc));
            // Unsichtbarer Platzhalter: reserviert die volle Breite der Symbolspalte
            item.Image = MenuHelper.Blank(iconW, (int)(16 * _sc));
            item.ImageScaling = ToolStripItemImageScaling.None;
            item.Click += delegate { action(); };
            return item;
        }

        private void OpenFile(string path, string missing)
        {
            try
            {
                if (!File.Exists(path)) { SetStatus(missing, false); return; }
                System.Diagnostics.Process.Start(path);
            }
            catch (Exception ex) { SetStatus(ex.Message, false); }
        }

        /// <summary>Alle Seiten auf die Standardwerte setzen; gilt erst nach "Speichern".</summary>
        private void ResetDefaults()
        {
            Settings d = new Settings();
            _edit.CopyFrom(d);
            SyncRuntimeFields();
            LoadValues(false);
            SetStatus("Standardwerte geladen – mit „Speichern“ übernehmen", true);
        }

        /// <summary>"Beenden": WindowGrid ganz beenden, bei ungespeicherten Änderungen vorher nachfragen.</summary>
        private void QuitProgram()
        {
            if (Demo.Active) return;
            if (HasChanges())
            {
                DialogResult r = MessageBox.Show(this,
                    "Die Änderungen sind noch nicht gespeichert.\n\nWindowGrid trotzdem beenden?",
                    "WindowGrid", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            _discard = true;
            if (ExitRequested != null) ExitRequested(this, EventArgs.Empty);
        }

        private Card MakeCard(Control parent, string title, Rectangle bounds)
        {
            Card c = new Card();
            c.Title = title;
            c.UiScale = _sc;
            c.Bounds = bounds;
            parent.Controls.Add(c);
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

        private void ExportSettings()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Einstellungen exportieren";
                d.Filter = "WindowGrid-Einstellungen (*.ini)|*.ini|Alle Dateien (*.*)|*.*";
                d.FileName = "WindowGrid.ini";
                d.OverwritePrompt = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // was im Fenster steht (auch noch nicht Gespeichertes)
                    ReadValues();
                    SyncRuntimeFields();
                    _edit.SaveTo(d.FileName);
                    SetStatus("Exportiert", true);
                }
                catch (Exception ex)
                {
                    Log.Write("Export", ex);
                    SetStatus("Export fehlgeschlagen: " + ex.Message, false);
                }
            }
        }

        private void ImportSettings()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Einstellungen importieren";
                d.Filter = "WindowGrid-Einstellungen (*.ini)|*.ini|Alle Dateien (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Settings loaded = Settings.LoadFrom(d.FileName);
                if (loaded == null)
                {
                    SetStatus("Die Datei enthält keine WindowGrid-Einstellungen.", false);
                    return;
                }
                _edit.CopyFrom(loaded);
                SyncRuntimeFields();
                LoadValues(false);
                SetStatus("Importiert – mit „Speichern“ übernehmen", true);
            }
        }

        /// <summary>Werte, die nicht in diesem Fenster eingestellt werden, vom laufenden Programm übernehmen.</summary>
        private void SyncRuntimeFields()
        {
            _edit.LastSelection = _original.LastSelection;
            _edit.SnapBarOriginal = _original.SnapBarOriginal;
            _edit.LastUpdateCheck = _original.LastUpdateCheck;
            _edit.UpdateCheck = _original.UpdateCheck; // wird im Info-Fenster eingestellt
            _edit.Paused = _original.Paused;           // wird im Tray-Menü eingestellt
            _edit.FirstRun = _original.FirstRun;
            _edit.NavPinned = _original.NavPinned;     // Stecknadel der Navigation, gilt sofort
        }

        /// <summary>Steht im Fenster etwas anderes als gespeichert ist?</summary>
        private bool HasChanges()
        {
            try
            {
                ReadValues();
                SyncRuntimeFields();
                if (_edit.ToIni() != _original.ToIni()) return true;
                return _autostart.Checked != Autostart.IsEnabled;
            }
            catch { return false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Schließen über das X mit ungespeicherten Änderungen: nachfragen ("Abbrechen" und Esc verwerfen ohne Rückfrage)
            if (e.CloseReason == CloseReason.UserClosing && !_discard && !Demo.Active && !RestartElevatedRequested && HasChanges())
            {
                DialogResult r = MessageBox.Show(this,
                    "Die Änderungen sind noch nicht gespeichert.\n\nÄnderungen verwerfen?",
                    "WindowGrid", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        private void CheckKeyConflict()
        {
            bool same = _resizeKey.Key == _moveKey.Key || _resizeKey.Key == _centerKey.Key || _moveKey.Key == _centerKey.Key;
            _keyHint.Text = same
                ? "Zwei Funktionen haben dieselbe Taste – bitte unterschiedliche Tasten wählen."
                : KeyHintText;
            _keyHint.MaximumSize = new Size(S(676), 0);
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
            _zoneKeys.Checked = _edit.ZoneHotkeys;
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
            _zones.Load(_edit.Zones);
            _monitors.Load(_edit.Monitors, _edit.Columns, _edit.Rows);
            _excluded.Text = _edit.Excluded;
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
            _edit.ZoneHotkeys = _zoneKeys.Checked;
            _edit.Dim = _dim.Value;
            _edit.UseSystemAccent = _sysAccent.Checked;
            _edit.CustomColor = _swatch.Color;
            _edit.ShowSize = _showSize.Checked;
            _edit.ShowMargins = _showMargins.Checked;
            _edit.Animate = _animate.Checked;
            _zones.Read(_edit.Zones);
            _monitors.Read(_edit.Monitors);
            _edit.Excluded = _excluded.Text;
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

        /// <summary>Schließen ohne Rückfrage (Programmende, Update).</summary>
        public void CloseWithoutAsking()
        {
            _discard = true;
            Close();
        }

        private void Save()
        {
            RestartElevatedRequested = false;
            SetStatus("", false);
            // erst prüfen, dann speichern – bei einem Fehler bleibt das Fenster offen
            int[] keys = { _resizeOn.Checked ? _resizeKey.Key : -1, _moveOn.Checked ? _moveKey.Key : -2, _centerOn.Checked ? _centerKey.Key : -3 };
            if (keys[0] == keys[1] || keys[0] == keys[2] || keys[1] == keys[2])
            {
                SetStatus("Zwei Funktionen haben dieselbe Taste.", false);
                return;
            }
            ReadValues();
            // Die letzte Auswahl merkt sich WindowGrid laufend – nicht mit dem Stand beim Öffnen überschreiben
            SyncRuntimeFields();
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
                    SetStatus("Autostart nicht eingerichtet – Windows-Nachfrage abgebrochen?", false);
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
            // globale Vorgabe: Speichern schließt das Fenster nicht, sondern meldet kurz grün "Gespeichert"
            SetStatus("Gespeichert", true);
        }

        /// <summary>Text der Statuszeile links neben den Knöpfen: Fehler rot (bleibt), Erfolg grün (verschwindet nach 4 s).</summary>
        private void SetStatus(string text, bool ok)
        {
            if (_statusTimer != null) _statusTimer.Stop();
            _error.Tag = ok ? "ok" : "error";
            Palette p = Theme.Current;
            _error.ForeColor = ok ? OkColor(p) : ErrorColor(p);
            _error.Text = text;
            if (ok && text.Length > 0)
            {
                if (_statusTimer == null)
                {
                    _statusTimer = new Timer();
                    _statusTimer.Interval = 4000;
                    _statusTimer.Tick += delegate { _statusTimer.Stop(); if ((_error.Tag as string) == "ok") _error.Text = ""; };
                }
                _statusTimer.Start();
            }
        }

        /// <summary>Grün für Erfolgsmeldungen (Windows 11: hell #0F7B0F, dunkel #6CCB5F).</summary>
        private static Color OkColor(Palette p)
        {
            return p.Dark ? Color.FromArgb(108, 203, 95) : Color.FromArgb(15, 123, 15);
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
                    c.ForeColor = tag == "error" ? ErrorColor(p) : tag == "ok" ? OkColor(p) : tag == "sub" ? p.SubText : p.Text;
                }
                if (c is GridPreview) c.BackColor = p.Card;
                if (c is TextField) continue; // zeichnet sich selbst passend zum Farbschema
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
            if (e.KeyCode == Keys.Escape && !capturing) { _discard = true; Close(); e.Handled = true; }
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
                if (_statusTimer != null) _statusTimer.Dispose();
                if (_navTimer != null) _navTimer.Dispose();
                if (_moreMenu != null) _moreMenu.Dispose();
                if (_tips != null) _tips.Dispose();
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