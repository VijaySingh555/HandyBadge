using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HandyBadgeRemote
{
    // Full-screen overlay for dragging out the square to stream. The circle shows what fits on the round display.
    class RegionPicker : Form
    {
        readonly Bitmap shot;
        readonly Rectangle vs;
        Point start;
        Rectangle sel;
        bool dragging;
        public Rectangle Result { get; private set; }

        public RegionPicker()
        {
            vs = SystemInformation.VirtualScreen;
            shot = new Bitmap(vs.Width, vs.Height);
            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(vs.Location, Point.Empty, vs.Size);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = vs;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Text = "Pick a region";
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            dragging = true; start = e.Location; sel = Rectangle.Empty; Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            int side = Math.Max(Math.Abs(e.X - start.X), Math.Abs(e.Y - start.Y));
            int x = e.X < start.X ? start.X - side : start.X, y = e.Y < start.Y ? start.Y - side : start.Y;
            sel = new Rectangle(x, y, side, side);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            if (sel.Width < 40) { sel = Rectangle.Empty; Invalidate(); return; }
            Result = new Rectangle(sel.X + vs.X, sel.Y + vs.Y, sel.Width, sel.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(shot, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                var outside = new Region(ClientRectangle);
                if (!sel.IsEmpty) outside.Exclude(sel);
                g.FillRegion(dim, outside);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!sel.IsEmpty)
            {
                using (var p = new Pen(Color.FromArgb(0xA5, 0x7F, 0xE0), 2)) g.DrawRectangle(p, sel);
                using (var p = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawEllipse(p, sel);
            }
            const string tip = "Drag a square to stream to the badge. The circle shows what fits. Esc or right-click to cancel.";
            using (var f = new Font("Segoe UI", 14f))
            {
                var sz = g.MeasureString(tip, f);
                var r = new RectangleF((Width - sz.Width) / 2 - 12, 24, sz.Width + 24, sz.Height + 12);
                using (var bg = new SolidBrush(Color.FromArgb(220, 30, 30, 30))) g.FillRectangle(bg, r);
                g.DrawString(tip, f, Brushes.White, r.X + 12, r.Y + 6);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) shot.Dispose(); base.Dispose(disposing); }
    }
}
