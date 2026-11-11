// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>Tray-Symbol mit Menü – das Programm läuft unsichtbar im Hintergrund.</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        public const string ShowSettingsEventName = "WindowGrid_Modern_ShowSettings";

        private readonly Settings _settings;
        private readonly GridEngine _engine;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private Point _menuAnchor; // Mausposition beim Öffnen des Tray-Menüs
        private readonly Icon _appIcon;
        private readonly Icon _trayIcon;
        private readonly Icon _trayIconPaused;
        private MenuTheme _menuTheme;
        private float _menuScale = 1f;
        private readonly EventWaitHandle _showEvent;
        private readonly RegisteredWaitHandle _showWait;
        private readonly Control _invoker;
        private SettingsForm _form;
        private readonly System.Windows.Forms.Timer _updateTimer;
        private bool _balloonIsUpdate;
        private string _notifiedVersion;

        /// <summary>Nur für die Screenshots: Menü mit Beispieldaten, ohne Tray-Symbol und ohne Hooks.</summary>
        private TrayContext(Settings settings, Icon appIcon)
        {
            _settings = settings;
            _appIcon = appIcon;
            _menu = new ContextMenuStrip();
            BuildMenu();
            _menu.Opened += delegate { MenuHelper.RoundCorners(_menu); };
        }

        internal static ContextMenuStrip CreateDemoMenu(Settings settings, Icon appIcon)
        {
            return new TrayContext(settings, appIcon)._menu;
        }

        public TrayContext() : this(false) { }

        /// <param name="afterUpdate">Neustart durch das Update-Skript (--nach-update): Ergebnis anzeigen.</param>
        public TrayContext(bool afterUpdate)
        {
            _invoker = new Control();
            IntPtr h = _invoker.Handle;
            GC.KeepAlive(h);

            _settings = Settings.Load();
            _appIcon = LoadIcon(32);
            _trayIcon = LoadIcon(SystemInformation.SmallIconSize.Width);
            _trayIconPaused = MakeGrayIcon(_trayIcon);

            _engine = new GridEngine(_settings);
            _engine.Start();
            SnapBar.Cleanup(_settings); // Änderung einer früheren Version einmalig zurücksetzen
            Autostart.UpdateMovedPath(); // Programmordner verschoben: Autostart auf den neuen Pfad umstellen

            _menu = new ContextMenuStrip();
            BuildMenu();
            _menu.Opening += delegate (object sender, System.ComponentModel.CancelEventArgs e)
            {
                BuildMenu(); // jedes Mal frisch: Design, Akzentfarbe und Zustände aktuell
                _menuAnchor = Cursor.Position;
                e.Cancel = false;
            };
            _menu.Opened += delegate
            {
                // bei mehreren Bildschirmen nicht auf den Nachbarbildschirm ragen (globale Vorgabe)
                MenuHelper.KeepOnScreen(_menu, _menuAnchor);
                MenuHelper.RoundCorners(_menu);
            };

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };
            UpdateTray();
            _tray.Visible = true;

            // Zweiter Programmstart öffnet die Einstellungen der laufenden Instanz
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, delegate
            {
                try { _invoker.BeginInvoke((MethodInvoker)ShowSettings); } catch { }
            }, null, -1, false);

            if (_settings.FirstRun)
            {
                _settings.FirstRun = false;
                _settings.Save();
                _tray.ShowBalloonTip(6000, "WindowGrid läuft",
                    "Fenster ziehen und dabei „" + KeyNames.Get(_settings.ResizeKey) + "“ drücken – das Raster erscheint.",
                    ToolTipIcon.Info);
            }
            WarnIfOldVersionRunning();

            // ---------- Updates (Vorlage: Fensterpositionen wiederherstellen) ----------
            if (afterUpdate)
            {
                string result = Updater.LastResult();
                if (result != null && result.StartsWith("OK"))
                    _tray.ShowBalloonTip(6000, "WindowGrid aktualisiert", "Version " + result.Substring(2).Trim() + " ist installiert.", ToolTipIcon.Info);
                else if (result != null)
                {
                    Log.Write("Update", result);
                    _tray.ShowBalloonTip(8000, "Update fehlgeschlagen",
                        result.Replace("FEHLER", "").Trim() + " – die bisherige Version läuft weiter.", ToolTipIcon.Warning);
                }
            }
            AboutDialog.LiveSettings = _settings;
            AboutDialog.StartInstall = delegate
            {
                _invoker.BeginInvoke((MethodInvoker)delegate { Updater.InstallInteractive(VisibleForm, _invoker, null); });
            };
            Updater.AvailableChanged += delegate { NotifyUpdate(); };
            Updater.ExitRequested += delegate { ExitApp(); };
            _tray.BalloonTipClicked += delegate
            {
                if (_balloonIsUpdate) Updater.InstallInteractive(VisibleForm, _invoker, null);
            };
            _tray.BalloonTipClosed += delegate { _balloonIsUpdate = false; };
            // still im Hintergrund: 20 Sekunden nach dem Start, danach alle 6 Stunden nachsehen,
            // ob die Prüfung fällig ist (höchstens alle 20 Stunden, abschaltbar im Info-Fenster)
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 20000;
            _updateTimer.Tick += delegate
            {
                _updateTimer.Interval = 6 * 60 * 60 * 1000;
                if (Updater.Due(_settings)) Updater.CheckAsync(_settings, _invoker, false, null);
            };
            _updateTimer.Start();
        }

        private Form VisibleForm
        {
            get { return _form != null && !_form.IsDisposed && _form.Visible ? _form : null; }
        }

        /// <summary>Hinweis im Infobereich, einmal je Version und Programmstart; Klick startet das Update.</summary>
        private void NotifyUpdate()
        {
            ReleaseInfo r = Updater.Available;
            if (r == null || r.Version == _notifiedVersion) return;
            _notifiedVersion = r.Version;
            _balloonIsUpdate = true;
            _tray.ShowBalloonTip(10000, "WindowGrid " + r.Version + " ist verfügbar",
                "Installiert ist " + AppInfo.Version + ". Klicken, um jetzt zu aktualisieren.", ToolTipIcon.Info);
        }

        private static string ShortVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor;
            }
        }

        internal static Icon LoadIcon(int size)
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WindowGrid.ico"))
                {
                    if (s != null) return new Icon(s, size, size);
                }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            return SystemIcons.Application;
        }

        private static Icon MakeGrayIcon(Icon source)
        {
            try
            {
                using (Bitmap bmp = Theme.IconToBitmap(source, source.Width))
                using (Bitmap gray = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(gray))
                    {
                        ColorMatrix cm = new ColorMatrix(new float[][]
                        {
                            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
                            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                            new float[] { 0, 0, 0, 0.55f, 0 },
                            new float[] { 0, 0, 0, 0, 1 }
                        });
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    IntPtr hIcon = gray.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch { return source; }
        }

        /// <summary>Tray-Menü im Windows-11-Stil (wie Monitor Dimmer): Kopf mit Status, Symbole, Schalter.</summary>
        private void BuildMenu()
        {
            Theme.Accent = _settings.Accent;
            ToolStripItem[] old = new ToolStripItem[_menu.Items.Count];
            _menu.Items.CopyTo(old, 0);
            _menu.Items.Clear();
            foreach (ToolStripItem o in old) o.Dispose();

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _menuScale = g.DpiX / 96f;
            _menuTheme = MenuTheme.Current();
            _menu.Renderer = new ModernRenderer(_menuTheme, _menuScale);
            _menu.Font = new Font("Segoe UI", 9.5f);
            _menu.ShowCheckMargin = false;
            _menu.ShowItemToolTips = false;
            // Ab Windows 11 zeichnet das System selbst einen Schatten – den alten WinForms-Schatten abschalten
            if (Environment.OSVersion.Version.Build >= 22000) _menu.DropShadowEnabled = false;
            _menu.ShowImageMargin = true;
            _menu.ImageScalingSize = new Size(IconWidth, (int)(16 * _menuScale));
            _menu.Padding = new Padding(0, (int)(4 * _menuScale), 0, (int)(4 * _menuScale));

            // ---------- Kopf ----------
            Image icon = null;
            try { icon = Theme.IconToBitmap(_appIcon, (int)(64 * _menuScale)); } catch { }
            MenuHeader header = new MenuHeader(_menuTheme, _menuScale, icon);
            header.Title = "WindowGrid";
            header.Badge = !Demo.Active && Autostart.IsElevated ? "ADMIN" : null;
            header.Version = "v" + ShortVersion;
            header.Status = _settings.Paused ? "Pausiert" : "Aktiv · Fenster ziehen + " + KeyNames.Get(_settings.ResizeKey);
            header.StatusColor = _settings.Paused ? Color.FromArgb(150, 150, 150) : Color.FromArgb(34, 160, 80);
            header.Size = header.GetPreferredSize(Size.Empty);
            _menu.Items.Add(header);
            _menu.Items.Add(new ToolStripSeparator());

            // ---------- Update verfügbar ----------
            ReleaseInfo update = Demo.Active ? null : Updater.Available;
            if (update != null)
            {
                GlyphMenuItem upd = Item("Update auf " + update.Version + " installieren …", MenuHelper.Download,
                    delegate { Updater.InstallInteractive(VisibleForm, _invoker, null); });
                upd.Font = new Font(_menu.Font, FontStyle.Bold);
                _menu.Items.Add(upd);
                _menu.Items.Add(new ToolStripSeparator());
            }

            // ---------- Einträge ----------
            GlyphMenuItem settings = Item("Einstellungen …", MenuHelper.Settings, ShowSettings);
            settings.Font = new Font(_menu.Font, FontStyle.Bold);
            _menu.Items.Add(settings);

            GlyphMenuItem pause = Item("Pausieren", MenuHelper.Pause, delegate { SetPaused(!_settings.Paused); });
            MakeToggle(pause, _settings.Paused);
            _menu.Items.Add(pause);

            bool autoOn = Demo.Active || Autostart.IsEnabled;
            GlyphMenuItem auto = Item("Mit Windows starten", MenuHelper.Startup, delegate
            {
                if (!Autostart.Set(!autoOn, _settings.RunAsAdmin))
                    MessageBox.Show("Der Autostart konnte nicht geändert werden.", "WindowGrid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
            MakeToggle(auto, autoOn);
            _menu.Items.Add(auto);
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add(Item("Info über WindowGrid", MenuHelper.Info, delegate
            {
                AboutDialog.ShowFor(_form != null && !_form.IsDisposed && _form.Visible ? _form : null);
            }));
            _menu.Items.Add(Item("Beenden", MenuHelper.Power, ExitApp));
            // Unsichtbare Leerzeile: mehr Luft unter "Beenden" (der Innenabstand unten wird von Windows ignoriert)
            _menu.Items.Add(new MenuSpacer((int)(6 * _menuScale)));
        }

        private int IconWidth
        {
            get { return (int)(36 * _menuScale); }
        }

        /// <summary>Einheitliche Zeilenhöhe wie im Monitor Dimmer: Schrifthöhe + je 4 Einheiten oben und unten.</summary>
        private int RowHeight
        {
            get { return TextRenderer.MeasureText("Ag", _menu.Font).Height + (int)(8 * _menuScale); }
        }

        private GlyphMenuItem Item(string text, char glyph, MethodInvoker action)
        {
            GlyphMenuItem item = new GlyphMenuItem(text);
            int pad = (int)(5 * _menuScale);
            item.Padding = new Padding(0, pad, (int)(8 * _menuScale), pad);
            item.ShowShortcutKeys = true;
            item.FixedHeight = RowHeight;
            if (glyph != '\0') item.Glyph = MenuHelper.Glyph(glyph, _menuTheme.Text, (int)(16 * _menuScale));
            // Unsichtbarer Platzhalter: reserviert die volle Breite der Symbolspalte
            item.Image = MenuHelper.Blank(IconWidth, (int)(16 * _menuScale));
            item.ImageScaling = ToolStripItemImageScaling.None;
            if (action != null) item.Click += delegate { action(); };
            return item;
        }

        /// <summary>Schalter rechts im Menüeintrag statt Haken.</summary>
        private static void MakeToggle(GlyphMenuItem item, bool on)
        {
            item.IsToggle = true;
            item.ToggleOn = on;
            // Platz rechts für den Schalter: Windows reserviert die Breite des "Tastenkürzel"-Texts
            item.ShortcutKeyDisplayString = "\u2007\u2007\u2007\u2007\u2007\u2007";
        }

        private void UpdateTray()
        {
            _tray.Icon = _settings.Paused ? _trayIconPaused : _trayIcon;
            string text = "WindowGrid – Fenster ziehen + " + KeyNames.Get(_settings.ResizeKey);
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = _settings.Paused ? "WindowGrid (pausiert)" : text;
        }

        private void SetPaused(bool paused)
        {
            _settings.Paused = paused;
            _settings.Save();
            UpdateTray();
        }

        private void ShowSettings()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new SettingsForm(_settings, _appIcon);
                _form.Saved += delegate (object sender, EventArgs e)
                {
                    UpdateTray();
                    _engine.UpdateHotkeys(); // Strg+Win+Alt+1–9 an- oder abmelden
                    SettingsForm f = sender as SettingsForm;
                    if (f != null && f.RestartElevatedRequested)
                    {
                        // Erst die neue Instanz (mit UAC) starten, dann diese beenden
                        _invoker.BeginInvoke((MethodInvoker)delegate
                        {
                            if (Autostart.RestartElevated()) ExitApp();
                            else f.RestartElevatedRequested = false;
                        });
                    }
                };
                _form.ExitRequested += delegate { ExitApp(); };
                _form.FormClosed += delegate
                {
                    _form = null;
                    Theme.Accent = _settings.Accent; // Vorschau-Farbe verwerfen, falls mit "Abbrechen" geschlossen
                };
                _form.Show();
            }
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
        }

        private void WarnIfOldVersionRunning()
        {
            bool oldRunning = false;
            try
            {
                if (Process.GetProcessesByName("WindowGridOverlay").Length > 0) oldRunning = true;
                int me = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName("WindowGrid"))
                {
                    if (p.Id == me) continue;
                    try
                    {
                        if (!string.Equals(p.MainModule.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                            oldRunning = true;
                    }
                    catch { oldRunning = true; }
                }
            }
            catch { }
            if (oldRunning)
            {
                _tray.ShowBalloonTip(8000, "Alte WindowGrid-Version läuft noch",
                    "Bitte die alte Version (2016) beenden, sonst reagieren beide Programme beim Ziehen.",
                    ToolTipIcon.Warning);
            }
        }

        private void ExitApp()
        {
            if (_form != null && !_form.IsDisposed) _form.CloseWithoutAsking();
            if (_updateTimer != null) _updateTimer.Stop();
            _tray.Visible = false;
            _showWait.Unregister(null);
            _engine.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tray.Dispose();
                _menu.Dispose();
                _showEvent.Dispose();
                _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}