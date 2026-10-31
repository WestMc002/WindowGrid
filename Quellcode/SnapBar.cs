// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowGrid
{
    /// <summary>
    /// Windows-11-Andocklayouts oben am Bildschirm (Leiste beim Ziehen an den oberen Rand).
    /// Windows übernimmt eine geänderte Einstellung nur über die Windows-Einstellungen selbst
    /// (oder nach einem Explorer-Neustart). WindowGrid ändert sie deshalb nicht mehr selbst,
    /// sondern öffnet die passende Seite der Windows-Einstellungen.
    /// </summary>
    internal static class SnapBar
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ValueName = "EnableSnapBar";

        /// <summary>Windows-Einstellungen → System → Multitasking öffnen.</summary>
        public static void OpenWindowsSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:multitasking") { UseShellExecute = true }); }
            catch { }
        }

        /// <summary>
        /// Aufräumen: Hat eine frühere WindowGrid-Version den Windows-Wert geändert, wird der
        /// ursprüngliche Wert einmalig wiederhergestellt.
        /// </summary>
        public static void Cleanup(Settings settings)
        {
            if (settings.SnapBarOriginal < 0) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key != null) key.SetValue(ValueName, settings.SnapBarOriginal, RegistryValueKind.DWord);
                }
            }
            catch { }
            settings.SnapBarOriginal = -1;
            try { settings.Save(); } catch { }
        }
    }
}