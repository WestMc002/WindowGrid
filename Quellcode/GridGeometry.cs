// WindowGrid – Fenster beim Ziehen per Raster anordnen
// Copyright (C) 2026 Günther Schoder · https://www.westmc002.de
// Lizenz: GNU General Public License v3.0 oder später (siehe LICENSE)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Drawing;

namespace WindowGrid
{
    /// <summary>Berechnet die Rasterfelder für den Arbeitsbereich eines Monitors (physische Pixel).</summary>
    internal sealed class GridGeometry
    {
        public readonly IntPtr Monitor;
        public readonly Rectangle WorkArea;
        public readonly int Columns;
        public readonly int Rows;
        public readonly int Gap;        // Abstand zwischen Fenstern in physischen Pixeln
        public readonly float Scale;    // DPI-Faktor des Monitors (1.0 = 96 dpi)
        public readonly int[] Xs;       // Spaltengrenzen (Columns + 1 Werte)
        public readonly int[] Ys;       // Zeilengrenzen (Rows + 1 Werte)

        public GridGeometry(IntPtr monitor, Rectangle workArea, int columns, int rows, int gapLogical, float scale)
        {
            Monitor = monitor;
            WorkArea = workArea;
            Columns = Math.Max(1, columns);
            Rows = Math.Max(1, rows);
            Scale = scale;
            Gap = (int)Math.Round(gapLogical * scale);

            int half = Gap / 2;
            Rectangle outer = Rectangle.Inflate(workArea, -half, -half);
            Xs = new int[Columns + 1];
            Ys = new int[Rows + 1];
            for (int i = 0; i <= Columns; i++)
                Xs[i] = outer.X + (int)Math.Round(i * (double)outer.Width / Columns);
            for (int j = 0; j <= Rows; j++)
                Ys[j] = outer.Y + (int)Math.Round(j * (double)outer.Height / Rows);
        }

        public float CellWidth { get { return (Xs[Columns] - Xs[0]) / (float)Columns; } }
        public float CellHeight { get { return (Ys[Rows] - Ys[0]) / (float)Rows; } }

        public int ColumnAt(int x)
        {
            for (int i = 0; i < Columns; i++)
                if (x < Xs[i + 1]) return i;
            return Columns - 1;
        }

        public int RowAt(int y)
        {
            for (int j = 0; j < Rows; j++)
                if (y < Ys[j + 1]) return j;
            return Rows - 1;
        }

        /// <summary>Sichtbares Fensterrechteck für den Bereich zwischen zwei Feldern (inklusive).</summary>
        public Rectangle Selection(int c0, int r0, int c1, int r1)
        {
            int cMin = Math.Min(c0, c1), cMax = Math.Max(c0, c1);
            int rMin = Math.Min(r0, r1), rMax = Math.Max(r0, r1);
            int half = Gap - Gap / 2;
            int half2 = Gap / 2;
            return Rectangle.FromLTRB(Xs[cMin] + half2, Ys[rMin] + half2, Xs[cMax + 1] - half, Ys[rMax + 1] - half);
        }

        /// <summary>Ein einzelnes Feld als sichtbares Rechteck.</summary>
        public Rectangle Cell(int c, int r)
        {
            return Selection(c, r, c, r);
        }

        /// <summary>
        /// Freie Felder zwischen Fenster und Bildschirmrand: [links, oben, rechts, unten].
        /// Beim Größe-Modus ganze Zahlen, beim Verschieben auch Bruchteile.
        /// </summary>
        public float[] Margins(Rectangle visible)
        {
            int half2 = Gap / 2;
            int half = Gap - Gap / 2;
            float cw = Math.Max(1f, CellWidth), ch = Math.Max(1f, CellHeight);
            float left = (visible.Left - (Xs[0] + half2)) / cw;
            float top = (visible.Top - (Ys[0] + half2)) / ch;
            float right = ((Xs[Columns] - half) - visible.Right) / cw;
            float bottom = ((Ys[Rows] - half) - visible.Bottom) / ch;
            return new float[] { Clean(left), Clean(top), Clean(right), Clean(bottom) };
        }

        private static float Clean(float v)
        {
            if (v < 0.05f) return 0f;
            float r = (float)Math.Round(v);
            return Math.Abs(v - r) < 0.08f ? r : (float)Math.Round(v, 1);
        }

        /// <summary>Für den Verschieben-Modus: Position am nächstgelegenen Rasterpunkt einrasten.</summary>
        public Rectangle SnapMove(Point desiredTopLeft, Size windowSize)
        {
            int half2 = Gap / 2;
            int x = Nearest(Xs, desiredTopLeft.X - half2) + half2;
            int y = Nearest(Ys, desiredTopLeft.Y - half2) + half2;
            int w = Math.Min(windowSize.Width, WorkArea.Width - 2 * Gap);
            int h = Math.Min(windowSize.Height, WorkArea.Height - 2 * Gap);
            if (w < 50) w = Math.Min(windowSize.Width, WorkArea.Width);
            if (h < 50) h = Math.Min(windowSize.Height, WorkArea.Height);

            int minX = WorkArea.Left + Gap, maxX = WorkArea.Right - Gap - w;
            int minY = WorkArea.Top + Gap, maxY = WorkArea.Bottom - Gap - h;
            if (maxX < minX) { minX = WorkArea.Left; maxX = WorkArea.Right - w; }
            if (maxY < minY) { minY = WorkArea.Top; maxY = WorkArea.Bottom - h; }
            if (x > maxX) x = maxX;
            if (x < minX) x = minX;
            if (y > maxY) y = maxY;
            if (y < minY) y = minY;
            return new Rectangle(x, y, w, h);
        }

        private static int Nearest(int[] lines, int value)
        {
            int best = lines[0];
            int bestDist = int.MaxValue;
            foreach (int l in lines)
            {
                int d = Math.Abs(l - value);
                if (d < bestDist) { bestDist = d; best = l; }
            }
            return best;
        }
    }
}