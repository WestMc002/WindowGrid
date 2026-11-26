// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WindowGrid
{
    /// <summary>
    /// Regex-Prüfungen fremder Daten (Dateien, Journal, Manifest, Befehlszeile) mit Zeitlimit 250 ms.
    /// Eine Zeitüberschreitung gilt als „nicht eindeutig prüfbar“ und liefert keinen Treffer, der Aufrufer lehnt also ab.
    /// </summary>
    internal static class Rx
    {
        internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

        internal static System.Text.RegularExpressions.Match Match(string input, string pattern)
        {
            try { return Regex.Match(input, pattern, RegexOptions.None, Timeout); }
            catch (RegexMatchTimeoutException)
            {
                Log.Write("Prüfung", "Zeitlimit beim Prüfen überschritten, Daten abgelehnt (Muster: " + pattern + ").");
                return System.Text.RegularExpressions.Match.Empty;
            }
        }

        internal static bool IsMatch(string input, string pattern)
        {
            return Match(input, pattern).Success;
        }
    }

    /// <summary>
    /// Automatische Dateizugriffe (ohne ausdrückliche Auswahl) nur auf lokale Pfade ohne Umleitung.
    /// </summary>
    internal static class SafeIo
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, uint ucchMax);

        /// <summary>
        /// true, wenn der Pfad auf einem lokalen Laufwerk liegt und kein vorhandener Pfadteil (von der Wurzel an)
        /// ein Reparse Point ist (Verknüpfung, Junction, OneDrive). Den Umleitungen wird dabei nicht gefolgt.
        /// Fehlt ein Pfadteil, ist alles darunter nicht vorhanden; der Pfad gilt dann als lokal.
        /// Im Zweifel (Netzwerk, subst, Gerätepfad, Fehler) false.
        /// </summary>
        internal static bool IsLocalUnredirected(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC, \\?\, \\.\
                string root = Path.GetPathRoot(full);
                if (root == null || root.Length != 3 || root[1] != ':' || root[2] != '\\') return false;
                DriveType type = new DriveInfo(root).DriveType;
                if (type != DriveType.Fixed && type != DriveType.Removable) return false;
                StringBuilder target = new StringBuilder(1024);
                if (QueryDosDevice(root.Substring(0, 2), target, (uint)target.Capacity) == 0) return false;
                if (!target.ToString().StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return false; // subst-Laufwerk
                string cur = root;
                foreach (string part in full.Substring(root.Length).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    cur = Path.Combine(cur, part);
                    FileAttributes a;
                    try { a = File.GetAttributes(cur); }
                    catch (FileNotFoundException) { return true; }
                    catch (DirectoryNotFoundException) { return true; }
                    if ((a & FileAttributes.ReparsePoint) != 0) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}