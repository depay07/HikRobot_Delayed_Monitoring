using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SC6000DelayedMonitor
{
    internal sealed class InspectionTrailControl : Control
    {
        private readonly int _delay;
        private List<InspectionPreview> _items = new List<InspectionPreview>();
        public InspectionTrailControl(int delay)
        {
            _delay = delay; Dock = DockStyle.Fill; Margin = Padding.Empty;
            BackColor = Color.FromArgb(32, 37, 40); ForeColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public void SetItems(List<InspectionPreview> items)
        {
            foreach (var item in _items) item.Dispose();
            _items = items; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            const int header = 30, gap = 4;
            TextRenderer.DrawText(e.Graphics, "NG 감지 필름 · " + _items.Count + "/" + _delay + (_items.Count == 0 ? " · NG 대기" : " · 수집 중"), Font,
                new Rectangle(4, 0, Math.Max(1, Width - 8), header), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            // Exactly DELAY_COUNT slots, numbered 1..DELAY_COUNT.
            int rows = (int)Math.Max(1, ((long)_delay + 1) / 2);
            float cellHeight = (Height - header) / (float)rows;
            if (Width < 20 || cellHeight < 8) return;
            for (int index = 0; index < (long)_delay && index < (long)rows * 2; ++index)
            {
                int row = index / 2, col = index % 2;
                var box = new Rectangle(col * Width / 2 + gap, header + (int)(row * cellHeight) + gap,
                    Math.Max(1, Width / 2 - 2 * gap), Math.Max(1, (int)cellHeight - 2 * gap));
                var item = index < _items.Count ? _items[index] : null;
                Color border = item == null ? Color.DimGray : item.Verdict == InspectionVerdict.NG ? Color.Red : Color.SeaGreen;
                if (item != null && item.Verdict == InspectionVerdict.Unknown) border = Color.Gray;
                using (var pen = new Pen(border, 2))
                    e.Graphics.DrawRectangle(pen, box);
                string state = item == null ? "대기" : item.Verdict == InspectionVerdict.Unknown ? "판정 미확인" : item.Verdict.ToString();
                string caption = item == null ? "대기 " + (index + 1) + "/" + _delay :
                    state + " #" + item.Number + " · " + item.Moves + "/" + _delay;
                int textHeight = Math.Min(22, box.Height);
                TextRenderer.DrawText(e.Graphics, caption, Font,
                    new Rectangle(box.X + 3, box.Y + 1, Math.Max(1, box.Width - 6), textHeight),
                    item != null && item.Verdict == InspectionVerdict.NG ? Color.Salmon : ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                if (item == null || box.Height <= textHeight + 4) continue;
                var area = new Rectangle(box.X + 3, box.Y + textHeight + 1, Math.Max(1, box.Width - 6), box.Height - textHeight - 4);
                double scale = Math.Min((double)area.Width / item.Thumbnail.Width, (double)area.Height / item.Thumbnail.Height);
                int w = Math.Max(1, (int)(item.Thumbnail.Width * scale)), h = Math.Max(1, (int)(item.Thumbnail.Height * scale));
                e.Graphics.DrawImage(item.Thumbnail, new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var item in _items) item.Dispose(); _items.Clear(); }
            base.Dispose(disposing);
        }
    }
}
