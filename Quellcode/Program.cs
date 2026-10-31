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
        private static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "WindowGrid_Modern_SingleInstance", out created))
            {
                // Neustart mit Adminrechten: warten, bis sich die alte Instanz beendet hat
                bool restart = Array.Exists(args, a => string.Equals(a, "/neustart", StringComparison.OrdinalIgnoreCase));
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
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // Falls WindowGrid unerwartet endet: Windows-Andocken wieder einschalten
                AppDomain.CurrentDomain.ProcessExit += delegate { GridEngine.RestoreSnap(); };
                AppDomain.CurrentDomain.UnhandledException += delegate { GridEngine.RestoreSnap(); };
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    GridEngine.RestoreSnap();
                    MessageBox.Show("Unerwarteter Fehler:\n\n" + e.Exception.Message, "WindowGrid",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new TrayContext());
                GC.KeepAlive(mutex);
            }
        }
    }
}