using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HandyBadgeRemote
{
    enum StreamMode { Off, Screen, Region, Presenter }

    // Builds each 240 x 240 frame for the badge's round display.
    class StreamSource : IDisposable
    {
        public const int Size = 240;
        public static readonly int[] ZoomLevels = { 1, 2, 4 };
        static readonly Color Accent = Color.FromArgb(0xA5, 0x7F, 0xE0);  // light GT purple, readable on black

        readonly Bitmap bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        readonly byte[] bgra = new byte[Size * Size * 4];

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
            IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);
        const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
        const int HALFTONE = 4;

        // Copies part of the screen into part of the frame, smoothly scaled.
        void Blit(Graphics g, Rectangle src, Rectangle dst)
        {
            IntPtr screen = GetDC(IntPtr.Zero), hdc = g.GetHdc();
            try
            {
                SetStretchBltMode(hdc, HALFTONE);
                SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                StretchBlt(hdc, dst.X, dst.Y, dst.Width, dst.Height, screen, src.X, src.Y, src.Width, src.Height, SRCCOPY | CAPTUREBLT);
            }
            finally { g.ReleaseHdc(hdc); ReleaseDC(IntPtr.Zero, screen); }
        }

        // Whole monitor (zoom 1) fitted inside the circle, or a square around the mouse (zoom 2, 4).
        public void RenderScreen(int zoom)
        {
            var cur = Cursor.Position;
            var mon = Screen.FromPoint(cur).Bounds;
            double a = (double)mon.Width / mon.Height;
            int fw = (int)(Size * a / Math.Sqrt(a * a + 1)), fh = (int)(Size / Math.Sqrt(a * a + 1));
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                if (zoom <= 1)
                {
                    var dst = new Rectangle((Size - fw) / 2, (Size - fh) / 2, fw, fh);
                    Blit(g, mon, dst);
                    DrawPointer(g, dst.X + (cur.X - mon.X) * fw / mon.Width, dst.Y + (cur.Y - mon.Y) * fh / mon.Height, 3);
                }
                else
                {
                    double scale = (double)fw / mon.Width * zoom;
                    int side = Math.Min((int)(Size / scale), Math.Min(mon.Width, mon.Height));
                    int x = Math.Max(mon.Left, Math.Min(mon.Right - side, cur.X - side / 2));
                    int y = Math.Max(mon.Top, Math.Min(mon.Bottom - side, cur.Y - side / 2));
                    Blit(g, new Rectangle(x, y, side, side), new Rectangle(0, 0, Size, Size));
                    DrawPointer(g, (cur.X - x) * Size / side, (cur.Y - y) * Size / side, 5);
                }
            }
        }

        static void DrawPointer(Graphics g, int x, int y, int r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(230, 255, 60, 60))) g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
            using (var p = new Pen(Color.White, 1.5f)) g.DrawEllipse(p, x - r, y - r, r * 2, r * 2);
        }

        // A chosen square of the screen, filling the display (corners fall outside the circle).
        public void RenderRegion(Rectangle region)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                Blit(g, region, new Rectangle(0, 0, Size, Size));
            }
        }

        // Presenter view: slide n of N, elapsed time, clock and next slide title, laid out for a round screen.
        public void RenderPresenter(PowerPointLink pp, TimeSpan elapsed)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var ring = new Pen(Accent, 4)) g.DrawEllipse(ring, 3, 3, Size - 7, Size - 7);
                var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var small = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var big = new Font("Segoe UI", 54f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var mid = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var note = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var grey = new SolidBrush(Color.FromArgb(190, 190, 190)))
                using (var accent = new SolidBrush(Accent))
                {
                    g.DrawString(DateTime.Now.ToString("HH:mm"), small, grey, new RectangleF(0, 22, Size, 24), centre);
                    if (pp.Presenting)
                    {
                        g.DrawString(pp.Slide.ToString(), big, Brushes.White, new RectangleF(0, 48, Size, 64), centre);
                        g.DrawString("of " + pp.Total, small, grey, new RectangleF(0, 108, Size, 20), centre);
                    }
                    else
                        g.DrawString(pp.Available ? "Not presenting" : "PowerPoint not found", small, grey, new RectangleF(0, 64, Size, 48), centre);
                    g.DrawString(Elapsed(elapsed), mid, accent, new RectangleF(0, 128, Size, 34), centre);
                    if (pp.Presenting && !string.IsNullOrEmpty(pp.NextTitle))
                    {
                        var nf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };
                        g.DrawString("Next: " + pp.NextTitle, note, Brushes.White, new RectangleF(38, 166, Size - 76, 40), nf);
                    }
                }
            }
        }

        static string Elapsed(TimeSpan t)
        {
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                     : ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
        }

        // Current frame as RGBA bytes for the encoder.
        public byte[] Pixels()
        {
            var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < Size; y++) Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * Size * 4, Size * 4);
            }
            finally { bmp.UnlockBits(data); }
            var rgba = new byte[bgra.Length];
            for (int i = 0; i < bgra.Length; i += 4) { rgba[i] = bgra[i + 2]; rgba[i + 1] = bgra[i + 1]; rgba[i + 2] = bgra[i]; rgba[i + 3] = 255; }
            return rgba;
        }

        public void Dispose() { bmp.Dispose(); }
    }
}
