// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>Neueste Veröffentlichung auf GitHub.</summary>
    internal sealed class ReleaseInfo
    {
        public string Version, PageUrl, ZipUrl;
        public string Sha256; // Prüfsumme des ZIP laut GitHub (hex), null = nicht angegeben
    }

    /// <summary>
    /// Update-Prüfung über die GitHub-Releases und Selbst-Update (Vorlage: Fensterpositionen wiederherstellen).
    /// Abgefragt wird nur die öffentliche GitHub-Schnittstelle, ohne Anmeldung; übertragen wird nur die
    /// Programmversion im User-Agent.
    /// </summary>
    internal static class Updater
    {
        public const string Repository = "WestMc002/WindowGrid";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

        /// <summary>Arbeitsordner für Download und Hilfsskript.</summary>
        public static string Folder { get { return Path.Combine(Path.GetTempPath(), "WindowGrid-Update"); } }
        public static string LogFile { get { return Path.Combine(Folder, "Update.log"); } }

        /// <summary>Gefundene neuere Version (null = keine bekannt).</summary>
        public static ReleaseInfo Available { get; private set; }

        /// <summary>Stand nach einer fehlgeschlagenen Prüfung von Hand bzw. "Nur Vorabversionen" (null = keiner).</summary>
        private static UpdateStatus _error;

        /// <summary>Fehler beim automatischen Aktualisieren (Kurzmeldung, Originaltext für "Details"); null = keiner.</summary>
        private static Exception _installError;

        /// <summary>Wird ausgelöst, wenn sich <see cref="Available"/> geändert hat (im UI-Thread).</summary>
        public static event EventHandler AvailableChanged;

        /// <summary>Das Update-Skript läuft – WindowGrid muss sich jetzt beenden.</summary>
        public static event EventHandler ExitRequested;

        private static string UserAgent { get { return "WindowGrid/" + AppInfo.Version; } }

        private static void PrepareTls()
        {
            // .NET 4: TLS 1.2 (3072) für github.com einschalten
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        // ------------------------------------------------------------------ Abfrage

        public static ReleaseInfo GetLatest()
        {
            PrepareTls();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repository + "/releases/latest");
            request.UserAgent = UserAgent;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            string json;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse http = ex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound)
                {
                    // /releases/latest kennt nur fertige Versionen – kein Fehler
                    Exception pre = new Exception("Nur Vorabversionen");
                    pre.Data["Prerelease"] = true;
                    throw pre;
                }
                // feste Kurzmeldung, technischer Originaltext für "Details" (globale Vorgabe, Referenz Dock Pilot/Wake on LAN)
                string detail = ex.Message;
                if (ex.InnerException != null && ex.InnerException.Message != ex.Message) detail += "\r\n" + ex.InnerException.Message;
                if (http != null && (int)http.StatusCode == 403) throw Fail("Zu viele Anfragen.", detail);
                if (ex.Status == WebExceptionStatus.NameResolutionFailure || ex.Status == WebExceptionStatus.ProxyNameResolutionFailure
                    || ex.Status == WebExceptionStatus.ConnectFailure) throw Fail("Keine Verbindung.", detail);
                if (ex.Status == WebExceptionStatus.Timeout) throw Fail("Keine Antwort.", detail);
                throw Fail("Nicht erreichbar.", detail);
            }
            catch (IOException ex)
            {
                throw new Exception(ex.Message); // unbekannter Fehler: nur "Details"
            }

            ReleaseInfo info = new ReleaseInfo();
            info.PageUrl = ReleasesPage;
            Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"[vV]?([^\"]+)\"");
            if (m.Success) info.Version = m.Groups[1].Value.Trim();
            m = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]+/releases/tag/[^\"]+)\"");
            if (m.Success) info.PageUrl = m.Groups[1].Value;
            m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(https://[^\"]+\\.zip)\"", RegexOptions.IgnoreCase);
            if (m.Success) info.ZipUrl = m.Groups[1].Value;
            // Prüfsumme, die GitHub zum ZIP angibt ("digest": "sha256:…" steht im selben Eintrag vor der Download-Adresse)
            m = Regex.Match(json, "\"digest\"\\s*:\\s*\"sha256:([0-9a-fA-F]{64})\"[^{}]*?\"browser_download_url\"\\s*:\\s*\"https://[^\"]+\\.zip\"", RegexOptions.IgnoreCase);
            if (m.Success) info.Sha256 = m.Groups[1].Value.ToLowerInvariant();
            Version parsed;
            if (string.IsNullOrEmpty(info.Version) || !Version.TryParse(info.Version, out parsed)) throw Fail("Antwort unlesbar.", null); // ohne "Details"
            return info;
        }

        /// <summary>Feste Kurzmeldung mit technischem Originaltext für "Details" (null = ohne "Details").</summary>
        private static Exception Fail(string message, string detail)
        {
            Exception ex = new Exception(message);
            ex.Data["Detail"] = detail ?? "";
            return ex;
        }

        /// <summary>true, wenn <paramref name="remote"/> neuer ist als die laufende Version.</summary>
        public static bool IsNewer(string remote)
        {
            Version a, b;
            if (!Version.TryParse(remote, out a) || !Version.TryParse(AppInfo.Version, out b)) return false;
            return a > b;
        }

        // ------------------------------------------------------------------ Prüfung mit Einstellungen

        private static DateTime LastCheck(Settings s)
        {
            DateTime last;
            if (!string.IsNullOrEmpty(s.LastUpdateCheck) &&
                DateTime.TryParse(s.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last))
                return last;
            return DateTime.MinValue;
        }

        /// <summary>Automatische Prüfung fällig? Höchstens alle 20 Stunden.</summary>
        public static bool Due(Settings s)
        {
            if (Demo.Active || !s.UpdateCheck) return false;
            DateTime last = LastCheck(s);
            return last == DateTime.MinValue || (DateTime.UtcNow - last.ToUniversalTime()).TotalHours >= 20;
        }

        /// <summary>Stand für das Info-Fenster: Symbol, Überschrift und zweite Zeile (Vorlage: Wake on LAN).</summary>
        public static UpdateStatus Status(Settings s)
        {
            if (Demo.Active) return new UpdateStatus(UpdateKind.Current, "Aktuell", "Zuletzt geprüft " + DateTime.Today.ToString("dd.MM.yyyy"));
            DateTime last = LastCheck(s);
            string lastText = last == DateTime.MinValue ? "" : "Zuletzt geprüft " + last.ToLocalTime().ToString("dd.MM.yyyy");
            if (Available != null)
            {
                UpdateStatus a = new UpdateStatus(UpdateKind.Available, "Version " + Available.Version + " verfügbar", lastText);
                if (_installError != null)
                {
                    // automatisches Aktualisieren ist gescheitert: Kurzmeldung mit "Details" statt der letzten Prüfung
                    a = new UpdateStatus(UpdateKind.Available, a.Title, "Update fehlgeschlagen.");
                    a.More = _installError.Message;
                }
                return a;
            }
            if (_error != null) return _error;
            if (last == DateTime.MinValue) return new UpdateStatus(UpdateKind.Unknown, "Noch nicht geprüft", "");
            return new UpdateStatus(UpdateKind.Current, "Aktuell", lastText);
        }

        /// <summary>Kann das Update direkt installiert werden (ZIP vorhanden, nicht im Projektordner)?</summary>
        public static bool CanInstall()
        {
            return Available != null && !string.IsNullOrEmpty(Available.ZipUrl) && InstallBlocker() == null;
        }

        /// <summary>
        /// Neueste Version im Hintergrund abfragen. Automatisch (manual = false) still: Fehler werden nicht gemeldet.
        /// <paramref name="done"/> bekommt den neuen Stand für das Info-Fenster (im Thread von <paramref name="invoker"/>).
        /// </summary>
        public static void CheckAsync(Settings s, Control invoker, bool manual, Action<UpdateStatus> done)
        {
            if (Demo.Active) { if (done != null) done(Status(s)); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo release = null;
                UpdateStatus error = null;
                try { release = GetLatest(); }
                catch (Exception ex)
                {
                    error = UpdateStatus.FromError(ex);
                    if (error.Kind == UpdateKind.Failed)
                        Log.Write(manual ? "Update-Prüfung" : "Update-Prüfung (automatisch)",
                            (ex.Message + (error.More.Length > 0 && error.More != ex.Message ? " – " + error.More : "")).Replace("\r\n", " "));
                }
                try
                {
                    invoker.BeginInvoke((MethodInvoker)delegate
                    {
                        if (release != null)
                        {
                            _error = null;
                            s.LastUpdateCheck = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                            s.Save();
                            SetAvailable(IsNewer(release.Version) ? release : null);
                        }
                        else if (manual)
                        {
                            _error = error;
                        }
                        if (done != null) done(Status(s));
                    });
                }
                catch { }
            });
        }

        private static void SetAvailable(ReleaseInfo release)
        {
            bool changed = (Available == null) != (release == null) ||
                           (Available != null && release != null && Available.Version != release.Version);
            if (changed) _installError = null;
            Available = release;
            if (changed && AvailableChanged != null) AvailableChanged(null, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ Installation

        /// <summary>
        /// Im Projektordner (mit Erstellen.bat) wird nicht automatisch ersetzt: dort entsteht die EXE beim Bauen.
        /// </summary>
        public static string InstallBlocker()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(dir, "Erstellen.bat")))
                return "Im Projektordner (mit Erstellen.bat) wird nicht automatisch aktualisiert – dort entsteht die EXE beim Erstellen.";
            return null;
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, "~update-test.tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// ZIP herunterladen, Hilfsskript schreiben und starten (blockierend – im Hintergrund aufrufen).
        /// Das Skript wartet, bis WindowGrid beendet ist, ersetzt EXE und Anleitung und startet WindowGrid
        /// neu. Die Einstellungen (WindowGrid.ini) bleiben unberührt. Danach muss sich WindowGrid beenden.
        /// </summary>
        public static void Install(ReleaseInfo release) { Install(release, false); }

        /// <param name="tray">Fenster war zu: nach dem Update nur im Infobereich starten (--nach-update /tray).</param>
        public static void Install(ReleaseInfo release, bool tray)
        {
            if (string.IsNullOrEmpty(release.ZipUrl)) throw new Exception("Im Release ist keine ZIP-Datei angehängt.");
            string blocker = InstallBlocker();
            if (blocker != null) throw new Exception(blocker);
            PrepareTls();
            Directory.CreateDirectory(Folder);
            string zip = Path.Combine(Folder, "WindowGrid-" + release.Version + ".zip");
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                client.DownloadFile(new Uri(release.ZipUrl), zip);
            }
            if (new FileInfo(zip).Length < 1024) throw new Exception("Die heruntergeladene Datei ist unvollständig.");
            // Prüfsumme vergleichen, bevor irgendetwas ersetzt wird
            if (!string.IsNullOrEmpty(release.Sha256))
            {
                string actual = Sha256Of(zip);
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(zip); } catch { }
                    Log.Write("Update", "Prüfsumme falsch: erwartet " + release.Sha256 + ", erhalten " + actual);
                    throw new Exception("Die heruntergeladene Datei ist beschädigt (Prüfsumme stimmt nicht). Bitte später erneut versuchen.");
                }
            }
            else Log.Write("Update", "GitHub hat keine Prüfsumme zum ZIP geliefert – Download wird ohne Vergleich übernommen.");
            string script = Path.Combine(Folder, "Update.ps1");
            File.WriteAllText(script, Script, new UTF8Encoding(true));
            try { File.Delete(LogFile); } catch { }
            string exe = Application.ExecutablePath;
            string target = Path.GetDirectoryName(exe);
            ProcessStartInfo start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -ProcessId " + Process.GetCurrentProcess().Id +
                " -Zip \"" + zip + "\" -Exe \"" + exe + "\" -Version \"" + release.Version + "\"" + (tray ? " -Tray" : ""));
            start.UseShellExecute = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            // Programmordner ohne Schreibrechte (z. B. unter Programme): Hilfsskript mit Administratorrechten starten
            if (!CanWrite(target)) start.Verb = "runas";
            try { Process.Start(start); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) throw new Exception("Update abgebrochen (Administratorrechte nicht erteilt).");
                throw;
            }
        }

        private static string Sha256Of(string file)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                StringBuilder sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(stream)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Nachfragen, herunterladen und installieren. Im Projektordner oder ohne ZIP öffnet sich stattdessen
        /// die Release-Seite. <paramref name="busy"/> wird während des Downloads mit true/false aufgerufen.
        /// </summary>
        public static void InstallInteractive(IWin32Window owner, Control invoker, Action<bool> busy)
        {
            bool tray = owner == null; // ohne sichtbares Fenster: nach dem Update nur im Infobereich
            ReleaseInfo release = Available;
            if (release == null) return;
            string blocker = InstallBlocker();
            if (blocker != null || string.IsNullOrEmpty(release.ZipUrl))
            {
                if (MessageBox.Show(owner,
                    (blocker ?? "Im Release ist keine ZIP-Datei angehängt.") + "\n\nRelease-Seite auf GitHub öffnen?",
                    "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenPage();
                return;
            }
            if (MessageBox.Show(owner,
                "WindowGrid wird beendet, auf Version " + release.Version + " aktualisiert und neu gestartet.\n" +
                "Die Einstellungen bleiben erhalten.\n\nJetzt aktualisieren?",
                "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            if (busy != null) busy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { Install(release, tray); }
                catch (Exception ex) { error = ex.Message; Log.Write("Update installieren", ex); }
                try
                {
                    invoker.BeginInvoke((MethodInvoker)delegate
                    {
                        if (busy != null) busy(false);
                        if (error != null)
                        {
                            MessageBox.Show(owner, error, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        if (ExitRequested != null) ExitRequested(null, EventArgs.Empty);
                    });
                }
                catch { }
            });
        }

        /// <summary>
        /// Automatisch aktualisieren: ohne Rückfrage herunterladen und installieren (nur mit ZIP, nicht im Projektordner).
        /// Fehler ohne Meldung: Hinweisleiste bleibt, die Kurzmeldung steht mit "Details" im Info-Fenster.
        /// </summary>
        public static void InstallSilently(Control invoker, bool tray)
        {
            ReleaseInfo release = Available;
            if (release == null || !CanInstall()) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                try { Install(release, tray); }
                catch (Exception ex) { error = ex; Log.Write("Update automatisch installieren", ex); }
                try
                {
                    invoker.BeginInvoke((MethodInvoker)delegate
                    {
                        if (error != null) { _installError = error; return; }
                        if (ExitRequested != null) ExitRequested(null, EventArgs.Empty);
                    });
                }
                catch { }
            });
        }

        public static void OpenPage()
        {
            ReleaseInfo r = Available;
            try { Process.Start(r != null && !string.IsNullOrEmpty(r.PageUrl) ? r.PageUrl : ReleasesPage); } catch { }
        }

        /// <summary>Ergebnis des letzten Updates (nach dem Neustart mit --nach-update).</summary>
        public static string LastResult()
        {
            try { return File.Exists(LogFile) ? File.ReadAllText(LogFile, Encoding.UTF8).Trim() : null; }
            catch { return null; }
        }

        private const string Script = @"# WindowGrid – Update (wird vom Programm erzeugt)
param([int]$ProcessId,[string]$Zip,[string]$Exe,[string]$Version,[switch]$Tray)
$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'Update.log'
$name = Split-Path $Exe -Leaf
$target = Split-Path $Exe -Parent
try {
  try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
  Start-Sleep -Milliseconds 500
  $dir = Join-Path $PSScriptRoot 'Neu'
  if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
  Expand-Archive -Path $Zip -DestinationPath $dir -Force
  $new = Get-ChildItem $dir -Recurse -File | Where-Object { $_.Name -eq $name } | Select-Object -First 1
  if (-not $new) { throw ('Im ZIP fehlt ' + $name) }
  $old = $Exe + '.alt'
  Copy-Item $Exe $old -Force
  try {
    Copy-Item $new.FullName $Exe -Force
    Get-ChildItem $new.DirectoryName -File | Where-Object { $_.Name -ne $name } | Copy-Item -Destination $target -Force
  } catch {
    Copy-Item $old $Exe -Force
    throw
  }
  Remove-Item $old -Force
  Remove-Item $dir -Recurse -Force
  Remove-Item $Zip -Force
  ('OK ' + $Version) | Out-File $log -Encoding utf8
} catch {
  ('FEHLER ' + $_.Exception.Message) | Out-File $log -Encoding utf8
}
if ($Tray) { Start-Process -FilePath $Exe -ArgumentList '--nach-update','/tray' }
else { Start-Process -FilePath $Exe -ArgumentList '--nach-update' }
";
    }

    internal enum UpdateKind { Unknown, Current, Available, Failed, Checking }

    /// <summary>Stand der Update-Prüfung für das Info-Fenster (Vorlage: Wake on LAN).</summary>
    internal sealed class UpdateStatus
    {
        public readonly UpdateKind Kind;
        public readonly string Title, Detail;
        /// <summary>Technischer Originaltext für "Details" (leer = kein "Details").</summary>
        public string More = "";
        public UpdateStatus(UpdateKind kind, string title, string detail) { Kind = kind; Title = title; Detail = detail ?? ""; }

        /// <summary>
        /// Fehler der Prüfung als Stand: feste Kurzmeldung (Message) mit Originaltext in Data["Detail"];
        /// unbekannte Fehler nur mit "Details". "Nur Vorabversionen" ist kein Fehler.
        /// </summary>
        public static UpdateStatus FromError(Exception ex)
        {
            if (ex.Data.Contains("Prerelease"))
                return new UpdateStatus(UpdateKind.Unknown, "Nur Vorabversionen", "Noch keine fertige Version.");
            UpdateStatus st;
            if (ex.Data.Contains("Detail"))
            {
                st = new UpdateStatus(UpdateKind.Failed, "Prüfung nicht möglich", ex.Message);
                st.More = ex.Data["Detail"] as string ?? "";
            }
            else
            {
                st = new UpdateStatus(UpdateKind.Failed, "Prüfung nicht möglich", "");
                st.More = ex.Message;
            }
            return st;
        }
    }
}