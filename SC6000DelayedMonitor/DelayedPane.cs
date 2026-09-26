using System;
using System.Drawing;
using System.Windows.Forms;

namespace SC6000DelayedMonitor
{
    internal sealed class DelayedPane : UserControl
    {
        private readonly Label _status;
        private readonly PictureBox _picture;
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
            Controls.Add(_picture); Controls.Add(_status);
            ShowStatus("FTP 수신 이미지 대기 중", true);
        }
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
                "\r\n" + System.IO.Path.GetFileName(result.FilePath);
        }
        private void ReleaseImage()
        { _picture.Image = null; if (_shown != null) { _shown.Dispose(); _shown = null; } }
        protected override void Dispose(bool disposing)
        { if (disposing) ReleaseImage(); base.Dispose(disposing); }
    }
}
