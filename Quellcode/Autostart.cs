// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindowGrid
{
    /// <summary>
    /// Autostart nur über HKCU\...\Run (globale Vorgabe: kein Autostart mit Administratorrechten).
    /// Eine Aufgabe "WindowGrid" in der Aufgabenplanung aus früheren Versionen ("Mit höchsten Privilegien")
    /// wird beim Start erkannt und zum Entfernen angeboten (OldTaskCommand, RemoveOldTask).
    /// </summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WindowGrid";
        public const string TaskName = "WindowGrid";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        /// <summary>schtasks.exe nur über den festen Systempfad, nie über den Suchpfad.</summary>
        private static string Schtasks
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"); }
        }

        /// <summary>Läuft dieses Programm gerade mit Administratorrechten (z. B. von Hand "Als Administrator ausführen")?</summary>
        public static bool IsElevated
        {
            get
            {
                if (_elevated < 0)
                {
                    try { _elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? 1 : 0; }
                    catch { _elevated = 0; }
                }
                return _elevated == 1;
            }
        }
        private static int _elevated = -1; // ändert sich während der Laufzeit nicht

        /// <summary>
        /// WindowGrid von Hand (nach Rückfrage) mit Administratorrechten neu starten – gilt nur bis zum nächsten
        /// Beenden, kein Autostart mit Adminrechten. true = neuer Prozess wurde gestartet.
        /// </summary>
        public static bool RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "/neustart");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                return Process.Start(psi) != null;
            }
            catch (Win32Exception) { return false; } // Nachfrage abgebrochen
            catch (Exception ex) { Log.Write("Neustart als Administrator", ex); return false; }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        if (key == null) return false;
                        string v = key.GetValue(ValueName) as string;
                        return v != null && string.Equals(v, Command, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Nach dem Verschieben des Programmordners: Zeigt der Autostart-Eintrag auf eine EXE, die es
        /// nicht mehr gibt, wird er auf den aktuellen Pfad umgestellt. Ein Eintrag auf eine vorhandene
        /// andere EXE (z. B. eine zweite Kopie) bleibt unverändert.
        /// </summary>
        public static void UpdateMovedPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    string v = key.GetValue(ValueName) as string;
                    if (v == null || string.Equals(v, Command, StringComparison.OrdinalIgnoreCase)) return;
                    string old = v.Trim().Trim('"');
                    // gemerkter Pfad: Netzwerk oder Umleitung nicht ansprechen, Eintrag dann unverändert lassen
                    if (SafeIo.IsLocalUnredirected(old) && !File.Exists(old)) key.SetValue(ValueName, Command);
                }
            }
            catch { }
        }

        /// <summary>Autostart ein- bzw. ausschalten (HKCU\...\Run). Rückgabe false, wenn das nicht geklappt hat.</summary>
        public static bool Set(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return false;
                    if (enabled) key.SetValue(ValueName, Command);
                    else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
                }
                return IsEnabled == enabled;
            }
            catch (Exception ex) { Log.Write("Autostart", ex); return false; }
        }

        /// <summary>
        /// Alte Aufgabe "WindowGrid" aus früheren Versionen über die Aufgabenplanungs-Schnittstelle (COM) lesen:
        /// alle Aktionen, Benutzer, Ausführungsebene und Trigger. null = es gibt keine Aufgabe; bei einem Lesefehler
        /// ist Error gesetzt (wird gesondert protokolliert, nicht als "keine Aufgabe" gewertet).
        /// </summary>
        public static OldTask QueryOldTask()
        {
            object service = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) return OldTask.Failed("Aufgabenplanung nicht verfügbar.");
                service = Activator.CreateInstance(type);
                Call(service, "Connect");
                object folder = Call(service, "GetFolder", "\\");
                object task;
                try { task = Call(folder, "GetTask", TaskName); }
                catch (Exception ex)
                {
                    COMException com = Inner(ex) as COMException;
                    if (com != null && (com.ErrorCode == unchecked((int)0x80070002) || com.ErrorCode == unchecked((int)0x80070003))) return null; // gibt es nicht
                    return OldTask.Failed(Inner(ex).Message);
                }
                OldTask info = new OldTask();
                object def = Get(task, "Definition");
                object principal = Get(def, "Principal");
                info.User = Convert.ToString(Get(principal, "UserId"));
                info.Highest = Convert.ToInt32(Get(principal, "RunLevel")) == 1; // TASK_RUNLEVEL_HIGHEST
                object actions = Get(def, "Actions");
                int count = Convert.ToInt32(Get(actions, "Count"));
                for (int i = 1; i <= count; i++)
                {
                    object action = Item(actions, i);
                    int kind = Convert.ToInt32(Get(action, "Type"));
                    if (kind == 0) // TASK_ACTION_EXEC
                    {
                        string path = Convert.ToString(Get(action, "Path")).Trim().Trim('"');
                        string args = Convert.ToString(Get(action, "Arguments"));
                        info.Commands.Add(string.IsNullOrEmpty(args) ? path : path + " " + args);
                        info.Paths.Add(path);
                    }
                    else info.Commands.Add("(andere Aktion, Typ " + kind + ")");
                }
                // Vergleichswert über die vollständige Aufgabendefinition (XML: Aktionen mit Arbeitsverzeichnis,
                // Trigger, Benutzer, Ausführungsebene, Einstellungen)
                info.Fingerprint = HashOf(Convert.ToString(Get(task, "Xml")));
                return info;
            }
            catch (Exception ex) { return OldTask.Failed(Inner(ex).Message); }
            finally
            {
                if (service != null) try { Marshal.ReleaseComObject(service); } catch { }
            }
        }

        private static string HashOf(string text)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "");
        }

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static object Call(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
        }

        private static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }

        private static object Item(object collection, int index)
        {
            return collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, new object[] { index });
        }

        /// <summary>Zeigt die Aufgabe nur auf diese WindowGrid.exe?</summary>
        public static bool IsOwnExe(OldTask task)
        {
            if (task == null || task.Paths.Count == 0 || task.Paths.Count != task.Commands.Count) return false;
            foreach (string path in task.Paths)
            {
                try
                {
                    if (!string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)),
                                       Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase)) return false;
                }
                catch { return false; }
            }
            return true;
        }

        /// <summary>
        /// Alte Aufgabe entfernen (Windows fragt nach Administratorrechten). Die letzte Prüfung läuft im erhöhten
        /// Prozess unmittelbar vor dem Löschen: Nur wenn die vollständige Definition noch genau der angezeigten
        /// entspricht (SHA-256 des Aufgaben-XML), wird gelöscht. true = entfernt bzw. schon weg.
        /// </summary>
        public static bool RemoveOldTask(OldTask shown)
        {
            if (shown == null || shown.Error != null || string.IsNullOrEmpty(shown.Fingerprint)) return false;
            string script =
                "$ErrorActionPreference = 'Stop'\n" +
                "$Name = '" + TaskName + "'\n" +
                "$Hash = '" + shown.Fingerprint + "'\n" +
                "try {\n" +
                "$s = New-Object -ComObject Schedule.Service\n" +
                "$s.Connect()\n" +
                "$f = $s.GetFolder('\\')\n" +
                "try { $t = $f.GetTask($Name) } catch { exit 3 }\n" +
                "$sha = [System.Security.Cryptography.SHA256]::Create()\n" +
                "$h = [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes([string]$t.Xml))).Replace('-', '')\n" +
                "if ($h -ne $Hash) { exit 2 }\n" +
                "$f.DeleteTask($Name, 0)\n" +
                "exit 0\n" +
                "} catch { exit 1 }\n";
            try
            {
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string ps = Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe");
                if (!File.Exists(ps)) return false;
                ProcessStartInfo psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
                psi.WorkingDirectory = system;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                if (IsElevated) { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
                else { psi.UseShellExecute = true; psi.Verb = "runas"; }
                using (Process p = Process.Start(psi))
                {
                    if (p == null || !p.WaitForExit(30000)) return false;
                    if (p.ExitCode == 2) Log.Write("Alte Aufgabe entfernen", "Aufgabe hat sich seit der Rückfrage verändert – nichts gelöscht.");
                    else if (p.ExitCode == 1) Log.Write("Alte Aufgabe entfernen", "Aufgabenplanung nicht lesbar oder Löschen fehlgeschlagen.");
                    if (p.ExitCode != 0 && p.ExitCode != 3) return false;
                }
                return QueryOldTask() == null;
            }
            catch (Win32Exception) { return false; } // Nachfrage abgebrochen
            catch (Exception ex) { Log.Write("Alte Aufgabe entfernen", ex); return false; }
        }
    }

    /// <summary>Gelesene alte Aufgabe "WindowGrid" (siehe Autostart.QueryOldTask).</summary>
    internal sealed class OldTask
    {
        public readonly List<string> Commands = new List<string>(); // alle Aktionen zur Anzeige
        public readonly List<string> Paths = new List<string>();    // Programme der Exec-Aktionen
        public string User = "";
        public bool Highest;          // "Mit höchsten Privilegien ausführen"
        public string Fingerprint = "";
        public string Error;          // Lesefehler (null = gelesen)

        public static OldTask Failed(string error)
        {
            OldTask t = new OldTask();
            t.Error = string.IsNullOrEmpty(error) ? "Unbekannter Fehler." : error;
            return t;
        }
    }
}