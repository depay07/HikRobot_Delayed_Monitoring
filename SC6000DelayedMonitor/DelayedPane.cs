using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SC6000DelayedMonitor
{
    internal sealed class DelayedPane : UserControl
    {
        private readonly Label _status;
        private readonly PictureBox _picture;
        private readonly InspectionTrailControl _trail;
        private InspectionResult _shown;
        private readonly string _title;
        private readonly int _delay;
        public DelayedPane(string title, int delay)
        {
            _title = title; _delay = delay;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(45, 52, 56);
            ForeColor = Color.White;
            _status = new Label { Dock = DockStyle.Top, Height = 64, Padding = new Padding(8), AutoEllipsis = true };
            _picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            _trail = new InspectionTrailControl(delay);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty, Padding = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _picture.Margin = Padding.Empty;
            content.Controls.Add(_picture, 0, 0); content.Controls.Add(_trail, 1, 0);
            Controls.Add(content); Controls.Add(_status);
            ShowStatus("FTP 수신 이미지 대기 중", true);
        }
        public void ShowTrail(List<InspectionPreview> previews) { _trail.SetItems(previews); }
        public void ShowStatus(string message, bool clear)
        {
            if (clear) ReleaseImage();
            _status.ForeColor = Color.White;
            _status.Text = _title + " | " + _delay + "회 지연\r\n" + message;
        }
        public void ShowResult(InspectionResult result)
        {
            ReleaseImage(); _shown = result; _picture.Image = result.Image;
            _status.ForeColor = Color.White;
            _status.Text = _title + " | " + _delay + "회 지연 | 표시 순번: " + result.SequenceNo +
                "\r\n" + result.FileName + " | " +
                (result.Verdict == InspectionVerdict.Unknown ? "판정 미확인" : result.Verdict.ToString()) + " | 제거 위치 도착";
        }
        private void ReleaseImage()
        { _picture.Image = null; if (_shown != null) { _shown.Dispose(); _shown = null; } }
        protected override void Dispose(bool disposing)
        { if (disposing) ReleaseImage(); base.Dispose(disposing); }
    }
}
