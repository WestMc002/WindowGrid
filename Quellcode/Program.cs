// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Windows.Forms;

namespace WindowGrid
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Screenshots für GitHub mit Beispieldaten: WindowGrid.exe --screenshots [Zielordner]
            int shot = Array.FindIndex(args, a => string.Equals(a, "--screenshots", StringComparison.OrdinalIgnoreCase));
            if (shot >= 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string dir = shot + 1 < args.Length ? args[shot + 1] : System.IO.Path.Combine(AppInfo.ExeDirectory, "Screenshots");
                return Demo.RunScreenshots(dir) ? 0 : 1;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "WindowGrid_Modern_SingleInstance", out created))
            {
                // Neustart mit Adminrechten: warten, bis sich die alte Instanz beendet hat
                bool afterUpdate = Array.Exists(args, a => string.Equals(a, "--nach-update", StringComparison.OrdinalIgnoreCase));
                bool restart = afterUpdate || Array.Exists(args, a => string.Equals(a, "/neustart", StringComparison.OrdinalIgnoreCase));
                if (!created && restart)
                {
                    try { created = mutex.WaitOne(10000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    // Läuft schon – dort die Einstellungen öffnen
                    try
                    {
                        EventWaitHandle ev = EventWaitHandle.OpenExisting(TrayContext.ShowSettingsEventName);
                        ev.Set();
                        ev.Dispose();
                    }
                    catch { }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // Falls WindowGrid unerwartet endet: Windows-Andocken wieder einschalten
                AppDomain.CurrentDomain.ProcessExit += delegate { GridEngine.RestoreSnap(); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    GridEngine.RestoreSnap();
                    Log.Write("Unerwarteter Fehler", e.ExceptionObject as Exception);
                };
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    GridEngine.RestoreSnap();
                    Log.Write("Unerwarteter Fehler", e.Exception);
                    MessageBox.Show("Unerwarteter Fehler:\n\n" + e.Exception.Message, "WindowGrid",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new TrayContext(afterUpdate));
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}