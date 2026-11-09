// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>Programmdaten. AppInfo.Version erzeugt die Erstellen.bat in Quellcode\VersionInfo.cs.</summary>
    public static partial class AppInfo
    {
        public const string Name = "WindowGrid";

        public static string ExeDirectory
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }
    }

    /// <summary>
    /// Einstellungen, gespeichert als %LOCALAPPDATA%\WindowGrid\WindowGrid.ini (wie die anderen Programme).
    /// Eine WindowGrid.ini früherer Versionen (neben der EXE oder unter %APPDATA%) wird beim ersten Start übernommen.
    /// </summary>
    internal sealed class Settings
    {
        public int Columns = 12;
        public int Rows = 8;
        public int Gap = 8;
        public int Dim = 35;
        public bool UseSystemAccent = true;
        public Color CustomColor = Color.FromArgb(0, 151, 167);
        public bool ShowSize = true;
        public bool ShowMargins = true;
        public bool Animate = true;
        public bool ResizeEnabled = true;
        public int ResizeKey = 0x20;   // Leertaste
        public bool MoveEnabled = true;
        public int MoveKey = 0x11;     // Strg (links oder rechts)
        public bool CenterEnabled = true;
        public int CenterKey = 0x43;   // C
        public bool RunAsAdmin = false;
        public int SnapBarOriginal = -1;          // ursprünglicher Windows-Wert EnableSnapBar (-1 = nicht geändert)
        public QuickZone[] Zones = QuickZone.Defaults();
        public RectangleF LastSelection = RectangleF.Empty; // in % des Arbeitsbereichs
        public bool Paused = false;
        public bool FirstRun = true;
        public bool UpdateCheck = true;           // beim Start auf GitHub nach neuen Versionen sehen (höchstens einmal täglich)
        public string LastUpdateCheck = "";       // Zeitpunkt der letzten erfolgreichen Prüfung (UTC, ISO 8601)

        private static string _filePath;

        public Color Accent
        {
            get { return UseSystemAccent ? Theme.SystemAccent : CustomColor; }
        }

        public Settings Clone()
        {
            Settings s = new Settings();
            s.CopyFrom(this);
            return s;
        }

        public void CopyFrom(Settings o)
        {
            Columns = o.Columns; Rows = o.Rows; Gap = o.Gap; Dim = o.Dim;
            UseSystemAccent = o.UseSystemAccent; CustomColor = o.CustomColor;
            ShowSize = o.ShowSize; ShowMargins = o.ShowMargins; Animate = o.Animate;
            ResizeEnabled = o.ResizeEnabled; ResizeKey = o.ResizeKey;
            MoveEnabled = o.MoveEnabled; MoveKey = o.MoveKey;
            CenterEnabled = o.CenterEnabled; CenterKey = o.CenterKey;
            RunAsAdmin = o.RunAsAdmin;
            SnapBarOriginal = o.SnapBarOriginal;
            Zones = new QuickZone[o.Zones.Length];
            for (int i = 0; i < o.Zones.Length; i++) Zones[i] = o.Zones[i].Clone();
            LastSelection = o.LastSelection;
            Paused = o.Paused; FirstRun = o.FirstRun;
            UpdateCheck = o.UpdateCheck; LastUpdateCheck = o.LastUpdateCheck;
        }

        /// <summary>Ordner der Laufzeitdaten: %LOCALAPPDATA%\WindowGrid.</summary>
        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name); }
        }

        /// <summary>Einstellungsdatei: %LOCALAPPDATA%\WindowGrid\WindowGrid.ini.</summary>
        public static string DataPath
        {
            get { return Path.Combine(DataDirectory, "WindowGrid.ini"); }
        }

        /// <summary>Frühere Orte: neben der EXE (portabel, bis 1.41) und %APPDATA%\WindowGrid (Ersatz bei schreibgeschütztem Ordner).</summary>
        private static string[] OldPaths
        {
            get
            {
                return new[]
                {
                    Path.Combine(AppInfo.ExeDirectory, "WindowGrid.ini"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowGrid", "WindowGrid.ini")
                };
            }
        }

        /// <summary>
        /// Übernimmt eine WindowGrid.ini vom früheren Ort nach %LOCALAPPDATA%\WindowGrid und löscht die alte Datei,
        /// damit es nur noch eine gibt. Klappt das Kopieren nicht, wird die alte Datei weiter gelesen.
        /// </summary>
        private static string MigrateOldFile()
        {
            foreach (string old in OldPaths)
            {
                if (!File.Exists(old)) continue;
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.Copy(old, DataPath, false);
                    try { File.Delete(old); } catch { } // z. B. Ordner ohne Schreibrechte: alte Datei bleibt, wird aber nicht mehr gelesen
                    return DataPath;
                }
                catch { return old; }
            }
            return null;
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            string path = File.Exists(DataPath) ? DataPath : MigrateOldFile();
            if (path == null)
            {
                s.ImportOldWindowGrid();
                return s;
            }
            _filePath = path;
            try
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                s.Columns = GetInt(values, "Spalten", s.Columns, 1, 100);
                s.Rows = GetInt(values, "Zeilen", s.Rows, 1, 100);
                s.Gap = GetInt(values, "Abstand", s.Gap, 0, 64);
                s.Dim = GetInt(values, "Abdunkelung", s.Dim, 0, 80);
                s.UseSystemAccent = GetBool(values, "WindowsAkzentfarbe", s.UseSystemAccent);
                s.CustomColor = GetColor(values, "EigeneFarbe", s.CustomColor);
                s.ShowSize = GetBool(values, "GroesseAnzeigen", s.ShowSize);
                s.ShowMargins = GetBool(values, "AbstaendeAnzeigen", s.ShowMargins);
                s.Animate = GetBool(values, "Animation", s.Animate);
                // ältere Einträge (Leertaste/Strg) werden noch gelesen
                s.ResizeEnabled = GetBool(values, "GroesseAktiv", GetBool(values, "Leertaste", s.ResizeEnabled));
                s.ResizeKey = GetInt(values, "TasteGroesse", s.ResizeKey, 7, 254);
                s.MoveEnabled = GetBool(values, "VerschiebenAktiv", GetBool(values, "Strg", s.MoveEnabled));
                s.MoveKey = GetInt(values, "TasteVerschieben", s.MoveKey, 7, 254);
                s.CenterEnabled = GetBool(values, "ZentrierenAktiv", s.CenterEnabled);
                s.CenterKey = GetInt(values, "TasteZentrieren", s.CenterKey, 7, 254);
                s.RunAsAdmin = GetBool(values, "AlsAdministrator", s.RunAsAdmin);
                s.SnapBarOriginal = GetInt(values, "AndocklayoutsVorher", s.SnapBarOriginal, -1, 1);
                for (int i = 0; i < s.Zones.Length; i++)
                {
                    string z;
                    if (values.TryGetValue("Zone" + (i + 1), out z))
                    {
                        QuickZone q = QuickZone.Parse(z);
                        if (q != null) s.Zones[i] = q;
                    }
                }
                string last;
                if (values.TryGetValue("LetzteAuswahl", out last)) s.LastSelection = ParseRect(last);
                s.Paused = GetBool(values, "Pausiert", s.Paused);
                s.FirstRun = GetBool(values, "ErsterStart", s.FirstRun);
                s.UpdateCheck = GetBool(values, "UpdatesPruefen", s.UpdateCheck);
                string lastCheck;
                if (values.TryGetValue("LetzteUpdatePruefung", out lastCheck)) s.LastUpdateCheck = lastCheck.Trim();
            }
            catch { }
            return s;
        }

        /// <summary>Übernimmt beim ersten Start die Rasterwerte aus der alten WindowGrid.settings, falls vorhanden.</summary>
        private void ImportOldWindowGrid()
        {
            string[] candidates =
            {
                Path.Combine(AppInfo.ExeDirectory, "WindowGrid.settings")
            };
            foreach (string c in candidates)
            {
                try
                {
                    if (!File.Exists(c)) continue;
                    string json = File.ReadAllText(c);
                    Match mx = Regex.Match(json, "\"GridX\"\\s*:\\s*(\\d+)");
                    Match my = Regex.Match(json, "\"GridY\"\\s*:\\s*(\\d+)");
                    if (mx.Success && my.Success)
                    {
                        Columns = Math.Max(1, Math.Min(100, int.Parse(mx.Groups[1].Value)));
                        Rows = Math.Max(1, Math.Min(100, int.Parse(my.Groups[1].Value)));
                        if (Columns > 24 || Rows > 24) Gap = 0;
                    }
                    Match mc = Regex.Match(json, "\"ColorStart\"\\s*:\\s*\"#([0-9A-Fa-f]{8})\"");
                    if (mc.Success)
                    {
                        int argb = int.Parse(mc.Groups[1].Value, NumberStyles.HexNumber);
                        CustomColor = Color.FromArgb(255, Color.FromArgb(argb));
                    }
                    return;
                }
                catch { }
            }
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; WindowGrid " + AppInfo.Version + " - Einstellungen");
            sb.AppendLine("[Raster]");
            sb.AppendLine("Spalten=" + Columns);
            sb.AppendLine("Zeilen=" + Rows);
            sb.AppendLine("Abstand=" + Gap);
            sb.AppendLine("[Bedienung]");
            sb.AppendLine("; Tasten als virtueller Tastencode (32 = Leertaste, 17 = Strg, 16 = Umschalt, 18 = Alt)");
            sb.AppendLine("GroesseAktiv=" + B(ResizeEnabled));
            sb.AppendLine("TasteGroesse=" + ResizeKey);
            sb.AppendLine("VerschiebenAktiv=" + B(MoveEnabled));
            sb.AppendLine("TasteVerschieben=" + MoveKey);
            sb.AppendLine("ZentrierenAktiv=" + B(CenterEnabled));
            sb.AppendLine("TasteZentrieren=" + CenterKey);
            sb.AppendLine("[Darstellung]");
            sb.AppendLine("Abdunkelung=" + Dim);
            sb.AppendLine("WindowsAkzentfarbe=" + B(UseSystemAccent));
            sb.AppendLine("EigeneFarbe=#" + CustomColor.R.ToString("X2") + CustomColor.G.ToString("X2") + CustomColor.B.ToString("X2"));
            sb.AppendLine("GroesseAnzeigen=" + B(ShowSize));
            sb.AppendLine("AbstaendeAnzeigen=" + B(ShowMargins));
            sb.AppendLine("Animation=" + B(Animate));
            sb.AppendLine("[Allgemein]");
            sb.AppendLine("Pausiert=" + B(Paused));
            sb.AppendLine("AlsAdministrator=" + B(RunAsAdmin));
            sb.AppendLine("; Beim Start auf GitHub nach einer neuen Version sehen, höchstens einmal täglich (1 = ja)");
            sb.AppendLine("UpdatesPruefen=" + B(UpdateCheck));
            sb.AppendLine("LetzteUpdatePruefung=" + LastUpdateCheck);
            sb.AppendLine("; Ursprünglicher Windows-Wert aus einer früheren Version (wird beim Start zurückgesetzt, -1 = nichts zu tun)");
            sb.AppendLine("AndocklayoutsVorher=" + SnapBarOriginal);
            sb.AppendLine("[Schnellzonen]");
            sb.AppendLine("; Name|Links|Oben|Rechts|Unten in Prozent des Arbeitsbereichs");
            for (int i = 0; i < Zones.Length; i++) sb.AppendLine("Zone" + (i + 1) + "=" + Zones[i].ToIni());
            sb.AppendLine("LetzteAuswahl=" + RectToIni(LastSelection));
            sb.AppendLine("ErsterStart=" + B(FirstRun));

            string[] targets = _filePath != null && _filePath != DataPath ? new[] { DataPath, _filePath } : new[] { DataPath };
            foreach (string t in targets)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(t));
                    File.WriteAllText(t, sb.ToString(), new UTF8Encoding(false));
                    _filePath = t;
                    return;
                }
                catch { }
            }
        }

        private static string B(bool b) { return b ? "1" : "0"; }

        private static string RectToIni(RectangleF r)
        {
            if (r.IsEmpty) return "";
            CultureInfo c = CultureInfo.InvariantCulture;
            return r.Left.ToString("0.###", c) + "|" + r.Top.ToString("0.###", c) + "|" + r.Right.ToString("0.###", c) + "|" + r.Bottom.ToString("0.###", c);
        }

        private static RectangleF ParseRect(string s)
        {
            string[] p = (s ?? "").Split('|');
            if (p.Length != 4) return RectangleF.Empty;
            float l, t, r, b;
            CultureInfo c = CultureInfo.InvariantCulture;
            if (!float.TryParse(p[0], NumberStyles.Float, c, out l) || !float.TryParse(p[1], NumberStyles.Float, c, out t) ||
                !float.TryParse(p[2], NumberStyles.Float, c, out r) || !float.TryParse(p[3], NumberStyles.Float, c, out b)) return RectangleF.Empty;
            if (r <= l || b <= t) return RectangleF.Empty;
            return RectangleF.FromLTRB(l, t, r, b);
        }

        private static int GetInt(Dictionary<string, string> v, string key, int def, int min, int max)
        {
            string s;
            int i;
            if (v.TryGetValue(key, out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                return Math.Max(min, Math.Min(max, i));
            return def;
        }

        private static bool GetBool(Dictionary<string, string> v, string key, bool def)
        {
            string s;
            if (!v.TryGetValue(key, out s)) return def;
            s = s.ToLowerInvariant();
            return s == "1" || s == "true" || s == "ja";
        }

        private static Color GetColor(Dictionary<string, string> v, string key, Color def)
        {
            string s;
            if (!v.TryGetValue(key, out s)) return def;
            s = s.TrimStart('#');
            int rgb;
            if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return def;
        }
    }

    /// <summary>Schnellzone: Bereich in Prozent des Arbeitsbereichs (unabhängig von der Rasterfeinheit).</summary>
    internal sealed class QuickZone
    {
        public string Name;
        public int Left, Top, Right, Bottom;

        public QuickZone(string name, int l, int t, int r, int b)
        {
            Name = name; Left = l; Top = t; Right = r; Bottom = b;
        }

        public QuickZone Clone() { return new QuickZone(Name, Left, Top, Right, Bottom); }

        public RectangleF Frac
        {
            get { return RectangleF.FromLTRB(Left, Top, Right, Bottom); }
        }

        public bool IsValid
        {
            get { return Right > Left && Bottom > Top; }
        }

        public static QuickZone[] Defaults()
        {
            return new QuickZone[]
            {
                new QuickZone("Linke Hälfte", 0, 0, 50, 100),
                new QuickZone("Rechte Hälfte", 50, 0, 100, 100),
                new QuickZone("Mittleres Drittel", 33, 0, 67, 100),
                new QuickZone("Linkes Drittel", 0, 0, 33, 100),
                new QuickZone("Rechtes Drittel", 67, 0, 100, 100),
                new QuickZone("Linke zwei Drittel", 0, 0, 67, 100),
                new QuickZone("Rechte zwei Drittel", 33, 0, 100, 100),
                new QuickZone("Mitte", 20, 10, 80, 90),
                new QuickZone("Ganzer Bildschirm", 0, 0, 100, 100)
            };
        }

        public string ToIni()
        {
            return (Name ?? "").Replace("|", "/") + "|" + Left + "|" + Top + "|" + Right + "|" + Bottom;
        }

        public static QuickZone Parse(string s)
        {
            string[] p = (s ?? "").Split('|');
            if (p.Length != 5) return null;
            int l, t, r, b;
            if (!int.TryParse(p[1], out l) || !int.TryParse(p[2], out t) || !int.TryParse(p[3], out r) || !int.TryParse(p[4], out b)) return null;
            l = Math.Max(0, Math.Min(100, l)); t = Math.Max(0, Math.Min(100, t));
            r = Math.Max(0, Math.Min(100, r)); b = Math.Max(0, Math.Min(100, b));
            return new QuickZone(p[0].Trim(), l, t, r, b);
        }
    }
}