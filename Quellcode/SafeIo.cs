// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Textdatei mit einem einzigen Öffnen lesen und die Grenze beim Lesen durchsetzen (UTF-8, BOM wird erkannt).
        /// null, wenn sie größer als max Bytes ist. So prüft und verarbeitet der Aufrufer genau den gelesenen Inhalt;
        /// eine spätere Änderung der Datei spielt keine Rolle.
        /// </summary>
        internal static string ReadText(string path, int max)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buf = new byte[max + 1];
                int total = 0, n;
                while (total < buf.Length && (n = fs.Read(buf, total, buf.Length - total)) > 0) total += n;
                if (total > max) return null;
                using (StreamReader r = new StreamReader(new MemoryStream(buf, 0, total), Encoding.UTF8, true))
                    return r.ReadToEnd();
            }
        }

        /// <summary>Wie ReadText, aber als Zeilen (Zeilenenden wie File.ReadAllLines); null, wenn zu groß.</summary>
        internal static string[] ReadLines(string path, int max)
        {
            string text = ReadText(path, max);
            if (text == null) return null;
            List<string> lines = new List<string>(text.Split('\n'));
            for (int i = 0; i < lines.Count; i++) if (lines[i].EndsWith("\r")) lines[i] = lines[i].Substring(0, lines[i].Length - 1);
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines.ToArray();
        }

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