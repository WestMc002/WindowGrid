// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowGrid
{
    /// <summary>
    /// Kern von WindowGrid: erkennt, wann ein Fenster gezogen wird, blendet auf Tastendruck
    /// das Raster ein und setzt das Fenster beim Loslassen auf die gewählten Felder.
    /// </summary>
    internal sealed class GridEngine : IDisposable
    {
        private enum Mode { None, Resize, Move }

        private const uint VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly Settings _settings;
        private readonly OverlayWindow _overlay;

        private NativeMethods.WinEventProc _winEventProc;
        private NativeMethods.LowLevelProc _mouseProc;
        private NativeMethods.LowLevelProc _keyProc;
        private IntPtr _winEventHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyHook = IntPtr.Zero;

        private bool _dragging;
        private IntPtr _dragHwnd = IntPtr.Zero;
        private Mode _mode = Mode.None;
        private GridGeometry _geo;
        private int _anchorC, _anchorR;
        private Rectangle _target = Rectangle.Empty;
        private Point _grabOffset;
        private Size _windowSize;

        // Eingaben, die wir geschluckt haben – das passende "Loslassen" schlucken wir ebenfalls
        private bool _resizeHeld, _moveHeld, _centerHeld, _escHeld;

        // Zentrieren (Taste) und Rasterfeinheit (Mausrad)
        private bool _centered;
        private static readonly float[] Levels = { 1f / 16f, 1f / 8f, 1f / 4f, 1f / 2f, 1f, 2f, 4f };
        private const int BaseLevel = 4;
        private int _level = BaseLevel;
        private Point _anchorPoint;

        // Größe-Taste noch gehalten: Startfeld wandert mit der Maus, fest wird es beim Loslassen
        private bool _anchorFollow;

        private Point _pendingPoint;
        private bool _updateQueued;

        // Weitere Tasten bei sichtbarem Raster (Ziffern, Pfeile) – gedrückte merken, um "Loslassen" zu schlucken
        private readonly HashSet<uint> _swallowed = new HashSet<uint>();

        // Feinjustieren mit Pfeiltasten
        private bool _kbActive;
        private int _kbMouseC, _kbMouseR;
        private Rectangle _sel;          // Auswahl in Feldern (Größe-Modus)
        private Rectangle _moveTarget;   // Zielrechteck im Verschieben-Modus

        // Fester Bereich: Schnellzone (1–9) oder letzte Auswahl (0)
        private bool _fixedActive;
        private RectangleF _fixedFrac;
        private string _fixedLabel;
        private int _fixedZone;
        private long _lastResizeTick = long.MinValue / 2;

        // Kurzer Hinweis oben (z. B. "Als Schnellzone 3 gespeichert")
        private string _hintFlash;
        private string _lastHint;
        private Timer _flashTimer;

        // Windows-Andocken (Aero Snap / Andocklayouts) während des Rasters abgeschaltet?
        private static bool _snapDisabled;

        public GridEngine(Settings settings)
        {
            _settings = settings;
            _overlay = new OverlayWindow();
            IntPtr force = _overlay.Handle; // Fensterhandle jetzt erzeugen (für BeginInvoke)
            GC.KeepAlive(force);
        }

        public void Start()
        {
            _winEventProc = OnWinEvent;
            _mouseProc = MouseHookProc;
            _keyProc = KeyboardHookProc;
            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, _winEventProc, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        }

        // ------------------------------------------------------------------ Fenster ziehen

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;

            if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZESTART)
            {
                if (_settings.Paused) return;
                IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                _dragHwnd = root != IntPtr.Zero ? root : hwnd;
                _dragging = true;
                _mode = Mode.None;
                _target = Rectangle.Empty;
                InstallInputHooks();
            }
            else if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZEEND)
            {
                if (!_dragging) return;
                _dragging = false;

                bool apply = _mode != Mode.None && !_target.IsEmpty;
                Rectangle target = _target;
                IntPtr h = _dragHwnd;
                if (apply && _geo != null)
                {
                    // Für "Größe-Taste doppelt = letzte Auswahl" merken (in % des Arbeitsbereichs)
                    _settings.LastSelection = RectToFrac(target);
                    try { _settings.Save(); } catch { }
                }
                ExitGridMode();
                RemoveHooksIfIdle();

                if (apply)
                {
                    // Erst nach Ende der Verschiebe-Schleife setzen, danach noch einmal
                    // (falls das Fenster durch einen DPI-Wechsel seine Größe angepasst hat).
                    _overlay.BeginInvoke((MethodInvoker)delegate { ApplyWindowRect(h, target); });
                    Timer again = new Timer();
                    again.Interval = 150;
                    again.Tick += delegate
                    {
                        again.Stop();
                        again.Dispose();
                        ApplyWindowRect(h, target);
                    };
                    again.Start();
                }
            }
        }

        // ------------------------------------------------------------------ Eingabe-Hooks

        private void InstallInputHooks()
        {
            IntPtr hMod = Marshal.GetHINSTANCE(typeof(GridEngine).Module);
            if (_mouseHook == IntPtr.Zero)
                _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
            if (_keyHook == IntPtr.Zero)
                _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyProc, hMod, 0);
        }

        private bool InputPending
        {
            get { return _resizeHeld || _moveHeld || _centerHeld || _escHeld || _swallowed.Count > 0; }
        }

        private void RemoveHooksIfIdle()
        {
            if (_dragging || InputPending) return;
            // Nicht direkt aus dem Hook-Callback aushängen
            _overlay.BeginInvoke((MethodInvoker)delegate
            {
                if (_dragging || InputPending) return;
                if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
                if (_keyHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
            });
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                NativeMethods.MSLLHOOKSTRUCT info = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                Point pt = new Point(info.pt.X, info.pt.Y);

                if (msg == NativeMethods.WM_MOUSEMOVE && _dragging && _mode != Mode.None) QueueUpdate(pt);

                // Mausrad bei sichtbarem Raster: feiner (hoch) / gröber (runter) – Rad wird geschluckt
                if (msg == NativeMethods.WM_MOUSEWHEEL && _dragging && _mode != Mode.None)
                {
                    short delta = (short)((info.mouseData >> 16) & 0xFFFF);
                    int step = delta > 0 ? 1 : -1;
                    _overlay.BeginInvoke((MethodInvoker)delegate { ChangeLevel(step); });
                    return new IntPtr(1);
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                NativeMethods.KBDLLHOOKSTRUCT info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                bool down = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool up = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;
                uint vk = info.vkCode;

                // Eigene Tasten aus den Einstellungen (Standard: Leertaste = Größe, Strg = Verschieben)
                if (vk != VK_ESCAPE && _settings.ResizeEnabled && KeyNames.Matches(_settings.ResizeKey, vk))
                {
                    if (HandleTriggerKey(ref _resizeHeld, Mode.Resize, down, up, vk)) return new IntPtr(1);
                }
                else if (vk != VK_ESCAPE && _settings.MoveEnabled && KeyNames.Matches(_settings.MoveKey, vk))
                {
                    if (HandleTriggerKey(ref _moveHeld, Mode.Move, down, up, vk)) return new IntPtr(1);
                }
                else if (vk != VK_ESCAPE && _settings.CenterEnabled && KeyNames.Matches(_settings.CenterKey, vk) && (_centerHeld || _mode != Mode.None))
                {
                    if (down && _centerHeld) return new IntPtr(1);
                    if (down && _dragging && _mode != Mode.None && !WasAlreadyDown(vk))
                    {
                        _centerHeld = true;
                        _overlay.BeginInvoke((MethodInvoker)ToggleCenter);
                        return new IntPtr(1);
                    }
                    if (up && _centerHeld)
                    {
                        _centerHeld = false;
                        RemoveHooksIfIdle();
                        return new IntPtr(1);
                    }
                }
                else if (IsGridKey(vk) && (_swallowed.Contains(vk) || (_dragging && _mode != Mode.None)))
                {
                    // Ziffern 1–9 (Schnellzonen, mit Umschalt speichern) und Pfeiltasten (feinjustieren)
                    if (down)
                    {
                        bool first = !_swallowed.Contains(vk);
                        _swallowed.Add(vk);
                        if (_dragging && _mode != Mode.None)
                        {
                            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                            uint key = vk;
                            _overlay.BeginInvoke((MethodInvoker)delegate { HandleGridKey(key, shift, first); });
                        }
                        return new IntPtr(1);
                    }
                    if (up && _swallowed.Remove(vk))
                    {
                        RemoveHooksIfIdle();
                        return new IntPtr(1);
                    }
                }
                else if (vk == VK_ESCAPE)
                {
                    // Erstes Esc blendet nur das Raster aus, ein zweites bricht das Ziehen wie gewohnt ab.
                    if (down && _escHeld) return new IntPtr(1);
                    if (down && _dragging && _mode != Mode.None)
                    {
                        _escHeld = true;
                        _overlay.BeginInvoke((MethodInvoker)ExitGridMode);
                        return new IntPtr(1);
                    }
                    if (up && _escHeld)
                    {
                        _escHeld = false;
                        RemoveHooksIfIdle();
                        return new IntPtr(1);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_keyHook, nCode, wParam, lParam);
        }

        /// <summary>Auslöser-Taste verarbeiten. true = Taste schlucken.</summary>
        private bool HandleTriggerKey(ref bool held, Mode mode, bool down, bool up, uint vk)
        {
            if (down && held) return true; // automatische Wiederholung
            if (down && _dragging && !WasAlreadyDown(vk))
            {
                held = true;
                QueueTrigger(mode, CursorPosition());
                return true;
            }
            if (up && held)
            {
                held = false;
                if (mode == Mode.Resize) _overlay.BeginInvoke((MethodInvoker)FixAnchor);
                RemoveHooksIfIdle();
                return true;
            }
            return false;
        }

        /// <summary>Größe-Taste losgelassen: Startfeld an der aktuellen Mausposition festlegen.</summary>
        private void FixAnchor()
        {
            if (!_anchorFollow) return;
            _anchorFollow = false;
            if (!_dragging || _mode != Mode.Resize || _geo == null) return;
            UpdatePointer(CursorPosition(), true);
        }

        /// <summary>Taste war schon vor dem Ziehen gedrückt (z. B. Strg gehalten) – dann nicht auslösen.</summary>
        private static bool WasAlreadyDown(uint vk)
        {
            return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
        }

        /// <summary>Ziffern 1–9 (oben und Ziffernblock) und Pfeiltasten.</summary>
        private static bool IsGridKey(uint vk)
        {
            return (vk >= 0x31 && vk <= 0x39) || (vk >= 0x61 && vk <= 0x69) || (vk >= 0x25 && vk <= 0x28);
        }

        private static Point CursorPosition()
        {
            NativeMethods.POINT p;
            NativeMethods.GetCursorPos(out p);
            return new Point(p.X, p.Y);
        }

        // ------------------------------------------------------------------ Rasterlogik

        private void QueueTrigger(Mode mode, Point pt)
        {
            _overlay.BeginInvoke((MethodInvoker)delegate { Trigger(mode, pt); });
        }

        private void QueueUpdate(Point pt)
        {
            _pendingPoint = pt;
            if (_updateQueued) return;
            _updateQueued = true;
            _overlay.BeginInvoke((MethodInvoker)delegate
            {
                _updateQueued = false;
                if (_dragging && _mode != Mode.None) UpdatePointer(_pendingPoint, false);
            });
        }

        private void Trigger(Mode mode, Point pt)
        {
            if (!_dragging) return;

            // Größe-Taste zweimal schnell: letzte Auswahl übernehmen
            if (mode == Mode.Resize)
            {
                long now = Environment.TickCount;
                long diff = now - _lastResizeTick;
                bool dbl = diff >= 0 && diff <= SystemInformation.DoubleClickTime;
                _lastResizeTick = now;
                if (dbl && _mode == Mode.Resize && !_settings.LastSelection.IsEmpty)
                {
                    _lastResizeTick = long.MinValue / 2; // kein dritter Doppeldruck
                    SetFixed(_settings.LastSelection, "Letzte Auswahl", 0);
                    return;
                }
            }

            if (_mode == mode)
            {
                ExitGridMode(); // gleicher Auslöser nochmal = Raster aus
                return;
            }

            bool wasOff = _mode == Mode.None;
            _mode = mode;
            if (wasOff)
            {
                _level = BaseLevel;
                _centered = false;
                DisableSnap(); // Andocken an den Rändern abschalten, solange das Raster sichtbar ist
            }
            _fixedActive = false;
            _kbActive = false;
            _anchorPoint = pt;
            _anchorFollow = mode == Mode.Resize && _resizeHeld;

            Rectangle frame = GetVisibleFrame(_dragHwnd);
            _windowSize = frame.Size;
            _grabOffset = new Point(pt.X - frame.X, pt.Y - frame.Y);

            if (_geo == null) _geo = CreateGeometry(pt);
            if (wasOff) _overlay.Prepare(_geo, _settings);
            _anchorC = _geo.ColumnAt(pt.X);
            _anchorR = _geo.RowAt(pt.Y);
            UpdatePointer(pt, true);
            if (wasOff) _overlay.ShowOverlay(_settings.Animate);
        }

        private void UpdatePointer(Point pt, bool force)
        {
            IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(pt.X, pt.Y), NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (_geo == null || _geo.Monitor != monitor)
            {
                _geo = CreateGeometry(pt);
                _overlay.Prepare(_geo, _settings);
                _anchorPoint = pt;
                _anchorC = _geo.ColumnAt(pt.X);
                _anchorR = _geo.RowAt(pt.Y);
                _kbActive = false;
                force = true;
            }

            int c = _geo.ColumnAt(pt.X);
            int r = _geo.RowAt(pt.Y);
            // Nach Pfeiltasten übernimmt die Maus erst wieder, wenn sie in ein anderes Feld bewegt wird
            if (_kbActive && (c != _kbMouseC || r != _kbMouseR))
            {
                _kbActive = false;
                force = true;
            }

            Rectangle t;
            string hint, line2;
            if (_fixedActive)
            {
                t = FracToRect(_fixedFrac);
                hint = _fixedLabel;
                line2 = _fixedZone > 0 ? "Ziffer " + _fixedZone + " nochmal = aus" : "Loslassen = übernehmen";
            }
            else if (_mode == Mode.Resize)
            {
                if (_anchorFollow && !_kbActive)
                {
                    // Taste noch gehalten: Startfeld folgt der Maus
                    _anchorPoint = pt;
                    _anchorC = c;
                    _anchorR = r;
                }
                if (!_kbActive)
                    _sel = Rectangle.FromLTRB(Math.Min(_anchorC, c), Math.Min(_anchorR, r), Math.Max(_anchorC, c) + 1, Math.Max(_anchorR, r) + 1);
                t = _geo.Selection(_sel.Left, _sel.Top, _sel.Right - 1, _sel.Bottom - 1);
                if (_centered) t = CenterInGrid(t.Size);
                hint = _centered ? "Größe anpassen · zentriert" : (_kbActive ? "Größe anpassen · Tastatur" : "Größe anpassen");
                line2 = _sel.Width + " × " + _sel.Height + (_sel.Width * _sel.Height == 1 ? " Feld" : " Felder");
                if (_anchorFollow && !_kbActive && !_centered)
                {
                    hint = "Startfeld wählen";
                    line2 = KeyNames.Get(_settings.ResizeKey) + " loslassen = Startfeld";
                }
            }
            else
            {
                if (_kbActive) t = _moveTarget;
                else
                {
                    Point desired = new Point(pt.X - _grabOffset.X, pt.Y - _grabOffset.Y);
                    t = _geo.SnapMove(desired, _windowSize);
                    if (_centered) t = CenterInGrid(t.Size);
                    _moveTarget = t;
                }
                hint = _centered ? "Verschieben · zentriert" : (_kbActive ? "Verschieben · Tastatur" : "Verschieben");
                line2 = "Position " + (t.X - _geo.WorkArea.X) + ", " + (t.Y - _geo.WorkArea.Y);
            }
            if (_hintFlash != null) hint = _hintFlash;

            if (!force && t == _target && hint == _lastHint) return;
            _target = t;
            _lastHint = hint;

            string line1 = _target.Width + " × " + _target.Height;
            string sub = "Raster " + _geo.Columns + " × " + _geo.Rows + "  ·  Mausrad feiner/gröber  ·  1–9 Schnellzonen  ·  Pfeile";
            if (_settings.CenterEnabled) sub += "  ·  " + KeyNames.Get(_settings.CenterKey) + " zentrieren";
            sub += "  ·  Esc aus";
            _overlay.Render(_target, hint, sub, line1, line2, _settings.ShowMargins ? _geo.Margins(_target) : null);
        }

        // ------------------------------------------------------------------ Schnellzonen, Pfeiltasten, letzte Auswahl

        private void HandleGridKey(uint vk, bool shift, bool first)
        {
            if (!_dragging || _mode == Mode.None || _geo == null) return;
            if (vk >= 0x25 && vk <= 0x28)
            {
                int dx = vk == 0x25 ? -1 : (vk == 0x27 ? 1 : 0);
                int dy = vk == 0x26 ? -1 : (vk == 0x28 ? 1 : 0);
                Nudge(dx, dy, shift);
                return;
            }
            if (!first) return; // Ziffern nicht wiederholen, solange gehalten
            int n = vk >= 0x61 ? (int)(vk - 0x60) : (int)(vk - 0x30);
            if (n < 1 || n > _settings.Zones.Length) return;
            if (shift && vk < 0x61) SaveZone(n);
            else ApplyZone(n);
        }

        private void SetFixed(RectangleF frac, string label, int zone)
        {
            _fixedActive = true;
            _fixedFrac = frac;
            _fixedLabel = label;
            _fixedZone = zone;
            _kbActive = false;
            _centered = false;
            UpdatePointer(CursorPosition(), true);
        }

        /// <summary>Ziffer 1–9: Fenster auf die Schnellzone setzen (gleiche Ziffer nochmal = aus).</summary>
        private void ApplyZone(int n)
        {
            QuickZone z = _settings.Zones[n - 1];
            if (_fixedActive && _fixedZone == n)
            {
                _fixedActive = false;
                UpdatePointer(CursorPosition(), true);
                return;
            }
            if (!z.IsValid) return;
            SetFixed(z.Frac, "Schnellzone " + n + (string.IsNullOrEmpty(z.Name) ? "" : " · " + z.Name), n);
        }

        /// <summary>Umschalt+Ziffer: aktuelle Auswahl als Schnellzone speichern.</summary>
        private void SaveZone(int n)
        {
            if (_target.IsEmpty) return;
            RectangleF f = RectToFrac(_target);
            int l = Clamp((int)Math.Round(f.Left), 0, 100), t = Clamp((int)Math.Round(f.Top), 0, 100);
            int r = Clamp((int)Math.Round(f.Right), 0, 100), b = Clamp((int)Math.Round(f.Bottom), 0, 100);
            if (r <= l || b <= t) return;
            QuickZone z = _settings.Zones[n - 1];
            z.Left = l; z.Top = t; z.Right = r; z.Bottom = b;
            z.Name = "Eigene Zone " + n;
            try { _settings.Save(); } catch { }
            Flash("Als Schnellzone " + n + " gespeichert");
        }

        private void Flash(string text)
        {
            _hintFlash = text;
            if (_flashTimer == null)
            {
                _flashTimer = new Timer();
                _flashTimer.Interval = 1600;
                _flashTimer.Tick += delegate
                {
                    _flashTimer.Stop();
                    _hintFlash = null;
                    if (_dragging && _mode != Mode.None) UpdatePointer(CursorPosition(), true);
                };
            }
            _flashTimer.Stop();
            _flashTimer.Start();
            UpdatePointer(CursorPosition(), true);
        }

        /// <summary>Pfeiltasten: Auswahl um ein Feld verschieben, mit Umschalt die Größe um ein Feld ändern.</summary>
        private void Nudge(int dx, int dy, bool shift)
        {
            Point pt = CursorPosition();
            if (_mode == Mode.Resize)
            {
                Rectangle sel = (_fixedActive || _centered) ? CellsFromRect(_target) : _sel;
                if (shift)
                {
                    int w = Clamp(sel.Width + dx, 1, _geo.Columns - sel.X);
                    int h = Clamp(sel.Height + dy, 1, _geo.Rows - sel.Y);
                    sel = new Rectangle(sel.X, sel.Y, w, h);
                }
                else
                {
                    int x = Clamp(sel.X + dx, 0, _geo.Columns - sel.Width);
                    int y = Clamp(sel.Y + dy, 0, _geo.Rows - sel.Height);
                    sel = new Rectangle(x, y, sel.Width, sel.Height);
                }
                _sel = sel;
            }
            else
            {
                Rectangle t = _target;
                int cw = Math.Max(1, (int)Math.Round(_geo.CellWidth));
                int ch = Math.Max(1, (int)Math.Round(_geo.CellHeight));
                if (shift)
                {
                    t.Width = Math.Max(cw, t.Width + dx * cw);
                    t.Height = Math.Max(ch, t.Height + dy * ch);
                }
                else t.Offset(dx * cw, dy * ch);
                _moveTarget = ClampToGrid(t);
            }
            _fixedActive = false;
            _centered = false;
            _kbActive = true;
            _kbMouseC = _geo.ColumnAt(pt.X);
            _kbMouseR = _geo.RowAt(pt.Y);
            UpdatePointer(pt, true);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (max < min) max = min;
            return Math.Max(min, Math.Min(max, v));
        }

        private static int NearestIndex(int[] lines, int value)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i < lines.Length; i++)
            {
                int d = Math.Abs(lines[i] - value);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        /// <summary>Rechteck auf ganze Felder umrechnen (X/Y = erstes Feld, Breite/Höhe = Anzahl Felder).</summary>
        private Rectangle CellsFromRect(Rectangle t)
        {
            int half2 = _geo.Gap / 2, half = _geo.Gap - _geo.Gap / 2;
            int c0 = Clamp(NearestIndex(_geo.Xs, t.Left - half2), 0, _geo.Columns - 1);
            int c1 = Clamp(NearestIndex(_geo.Xs, t.Right + half) - 1, c0, _geo.Columns - 1);
            int r0 = Clamp(NearestIndex(_geo.Ys, t.Top - half2), 0, _geo.Rows - 1);
            int r1 = Clamp(NearestIndex(_geo.Ys, t.Bottom + half) - 1, r0, _geo.Rows - 1);
            return new Rectangle(c0, r0, c1 - c0 + 1, r1 - r0 + 1);
        }

        /// <summary>Bereich in Prozent des Rasterbereichs in ein Fensterrechteck umrechnen (mit Abstand).</summary>
        private Rectangle FracToRect(RectangleF f)
        {
            int half2 = _geo.Gap / 2, half = _geo.Gap - _geo.Gap / 2;
            float x0 = _geo.Xs[0], x1 = _geo.Xs[_geo.Columns], y0 = _geo.Ys[0], y1 = _geo.Ys[_geo.Rows];
            int l = (int)Math.Round(x0 + (x1 - x0) * f.Left / 100f) + half2;
            int r = (int)Math.Round(x0 + (x1 - x0) * f.Right / 100f) - half;
            int t = (int)Math.Round(y0 + (y1 - y0) * f.Top / 100f) + half2;
            int b = (int)Math.Round(y0 + (y1 - y0) * f.Bottom / 100f) - half;
            if (r <= l) r = l + 1;
            if (b <= t) b = t + 1;
            return Rectangle.FromLTRB(l, t, r, b);
        }

        /// <summary>Fensterrechteck in Prozent des Rasterbereichs umrechnen.</summary>
        private RectangleF RectToFrac(Rectangle rc)
        {
            int half2 = _geo.Gap / 2, half = _geo.Gap - _geo.Gap / 2;
            float x0 = _geo.Xs[0], x1 = _geo.Xs[_geo.Columns], y0 = _geo.Ys[0], y1 = _geo.Ys[_geo.Rows];
            float w = Math.Max(1f, x1 - x0), h = Math.Max(1f, y1 - y0);
            return RectangleF.FromLTRB(
                (rc.Left - half2 - x0) / w * 100f, (rc.Top - half2 - y0) / h * 100f,
                (rc.Right + half - x0) / w * 100f, (rc.Bottom + half - y0) / h * 100f);
        }

        /// <summary>Rechteck in den Rasterbereich (mit Abstand) einpassen.</summary>
        private Rectangle ClampToGrid(Rectangle t)
        {
            int half2 = _geo.Gap / 2, half = _geo.Gap - _geo.Gap / 2;
            Rectangle area = Rectangle.FromLTRB(_geo.Xs[0] + half2, _geo.Ys[0] + half2, _geo.Xs[_geo.Columns] - half, _geo.Ys[_geo.Rows] - half);
            int w = Math.Min(t.Width, area.Width), h = Math.Min(t.Height, area.Height);
            int x = Clamp(t.X, area.Left, area.Right - w);
            int y = Clamp(t.Y, area.Top, area.Bottom - h);
            return new Rectangle(x, y, w, h);
        }

        // ------------------------------------------------------------------ Windows-Andocken

        /// <summary>Andocken (Aero Snap und Andocklayouts) abschalten, solange das Raster sichtbar ist.</summary>
        private static void DisableSnap()
        {
            if (_snapDisabled) return;
            try
            {
                int on;
                if (NativeMethods.SystemParametersInfoGet(NativeMethods.SPI_GETWINARRANGING, 0, out on, 0) && on != 0)
                {
                    // Ohne SPIF_UPDATEINIFILE: gilt nur bis zur Rückstellung bzw. bis zur Abmeldung
                    if (NativeMethods.SystemParametersInfoSet(NativeMethods.SPI_SETWINARRANGING, 0, IntPtr.Zero, 0))
                        _snapDisabled = true;
                }
            }
            catch { }
        }

        /// <summary>Andocken wieder einschalten, falls WindowGrid es abgeschaltet hat.</summary>
        public static void RestoreSnap()
        {
            if (!_snapDisabled) return;
            try { NativeMethods.SystemParametersInfoSet(NativeMethods.SPI_SETWINARRANGING, 0, new IntPtr(1), 0); }
            catch { }
            _snapDisabled = false;
        }

        private void ExitGridMode()
        {
            _mode = Mode.None;
            _centered = false;
            _fixedActive = false;
            _kbActive = false;
            _hintFlash = null;
            _lastHint = null;
            if (_flashTimer != null) _flashTimer.Stop();
            RestoreSnap();
            _level = BaseLevel;
            _geo = null;
            _target = Rectangle.Empty;
            _overlay.HideOverlay();
        }

        private int LevelColumns(int level)
        {
            return Math.Max(1, Math.Min(100, (int)Math.Round(_settings.Columns * Levels[level])));
        }

        private int LevelRows(int level)
        {
            return Math.Max(1, Math.Min(100, (int)Math.Round(_settings.Rows * Levels[level])));
        }

        /// <summary>Mausrad: Raster feiner (+1) oder gröber (-1).</summary>
        private void ChangeLevel(int step)
        {
            if (!_dragging || _mode == Mode.None) return;
            int next = _level;
            // Stufen überspringen, die (wegen Unter-/Obergrenze) dasselbe Raster ergeben
            do
            {
                next += step;
                if (next < 0 || next >= Levels.Length) return;
            }
            while (LevelColumns(next) == LevelColumns(_level) && LevelRows(next) == LevelRows(_level));
            _level = next;

            Point pt = CursorPosition();
            _geo = CreateGeometry(pt);
            _overlay.Prepare(_geo, _settings);
            _anchorC = _geo.ColumnAt(_anchorPoint.X);
            _anchorR = _geo.RowAt(_anchorPoint.Y);
            UpdatePointer(pt, true);
        }

        /// <summary>Zentrieren-Taste: Auswahl bzw. Fenster genau mittig setzen (nochmal drücken = aus).</summary>
        private void ToggleCenter()
        {
            if (!_dragging || _mode == Mode.None) return;
            _centered = !_centered;
            UpdatePointer(CursorPosition(), true);
        }

        /// <summary>Rechteck dieser Größe mittig in den Rasterbereich setzen (gleich viel frei links/rechts und oben/unten).</summary>
        private Rectangle CenterInGrid(Size size)
        {
            int half2 = _geo.Gap / 2, half = _geo.Gap - _geo.Gap / 2;
            int left = _geo.Xs[0] + half2, right = _geo.Xs[_geo.Columns] - half;
            int top = _geo.Ys[0] + half2, bottom = _geo.Ys[_geo.Rows] - half;
            int w = Math.Min(size.Width, right - left);
            int h = Math.Min(size.Height, bottom - top);
            return new Rectangle(left + (right - left - w) / 2, top + (bottom - top - h) / 2, w, h);
        }

        private GridGeometry CreateGeometry(Point pt)
        {
            IntPtr monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT(pt.X, pt.Y), NativeMethods.MONITOR_DEFAULTTONEAREST);
            NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            Rectangle work;
            if (NativeMethods.GetMonitorInfo(monitor, ref mi)) work = mi.rcWork.ToRectangle();
            else work = Screen.FromPoint(pt).WorkingArea;

            float scale = 1f;
            try
            {
                uint dx, dy;
                if (NativeMethods.GetDpiForMonitor(monitor, 0, out dx, out dy) == 0 && dx > 0) scale = dx / 96f;
            }
            catch { }
            return new GridGeometry(monitor, work, LevelColumns(_level), LevelRows(_level), _settings.Gap, scale);
        }

        // ------------------------------------------------------------------ Fenster setzen

        /// <summary>Sichtbarer Fensterrahmen (ohne die unsichtbaren Ränder von Windows 10/11).</summary>
        private static Rectangle GetVisibleFrame(IntPtr hwnd)
        {
            NativeMethods.RECT r;
            try
            {
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(NativeMethods.RECT))) == 0)
                    return r.ToRectangle();
            }
            catch { }
            NativeMethods.GetWindowRect(hwnd, out r);
            return r.ToRectangle();
        }

        private static void ApplyWindowRect(IntPtr hwnd, Rectangle visible)
        {
            if (!NativeMethods.IsWindow(hwnd)) return;
            if (NativeMethods.IsZoomed(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            NativeMethods.RECT wr;
            NativeMethods.GetWindowRect(hwnd, out wr);
            Rectangle frame = GetVisibleFrame(hwnd);
            int l = frame.Left - wr.Left;
            int t = frame.Top - wr.Top;
            int r = wr.Right - frame.Right;
            int b = wr.Bottom - frame.Bottom;
            if (l < 0 || l > 64) l = 0;
            if (t < 0 || t > 64) t = 0;
            if (r < 0 || r > 64) r = 0;
            if (b < 0 || b > 64) b = 0;

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                visible.X - l, visible.Y - t, visible.Width + l + r, visible.Height + t + b,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }

        public void Dispose()
        {
            if (_winEventHook != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
            RestoreSnap();
            if (_flashTimer != null) _flashTimer.Dispose();
            _overlay.Dispose();
        }
    }
}