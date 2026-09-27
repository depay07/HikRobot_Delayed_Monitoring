using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SC6000DelayedMonitor
{
    internal sealed class ViewerForm : Form
    {
        private readonly CameraSettings _settings;
        private readonly IntPtr _parent;
        private readonly System.Windows.Forms.Timer _layoutTimer = new System.Windows.Forms.Timer { Interval = 200 };
        private readonly DelayedPane _delayed;
        private readonly object _gate = new object();
        private FolderImageSource _source;
        private System.Threading.Timer _scanTimer;
        private int _busy;
        private bool _closed;
        private string _error;
        private long _trailVersion = -1;
        private readonly Label _connectionStatus;
        private SdkCameraConnection _connection;

        public ViewerForm(CameraSettings settings, IntPtr parent)
        {
            _settings = settings; _parent = parent;
            AutoScaleMode = AutoScaleMode.None;
            Text = settings.Title; TopLevel = false; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(45, 52, 56);
            _delayed = new DelayedPane(settings.Title, settings.DelayCount);
            Controls.Add(_delayed);
            _connectionStatus = new Label { Dock = DockStyle.Bottom, Height = 28, ForeColor = Color.White,
                AutoEllipsis = true, Text = "카메라 접속 준비 중" };
            Controls.Add(_connectionStatus);
            _layoutTimer.Tick += delegate
            {
                FitParent();
                if (IsDisposed || Disposing) return;
                FolderImageSource source;
                string error;
                lock (_gate) { source = _source; error = _error; }
                if (error != null) { _delayed.ShowStatus(error, false); return; }
                if (source == null) return;
                string status;
                System.Collections.Generic.List<InspectionPreview> previews;
                var result = source.Take(out status, ref _trailVersion, out previews);
                if (previews != null) _delayed.ShowTrail(previews);
                if (result != null) _delayed.ShowResult(result);
                else if (status != null) _delayed.ShowStatus(status, false);
            };
            Shown += delegate
            {
                FitParent(); _layoutTimer.Start();
                // This timer discovers files; delay is determined exclusively by file count.
                _scanTimer = new System.Threading.Timer(Scan, null, 0, 200);
                BeginInvoke(new Action(ConnectCamera));
            };
        }
        private void ConnectCamera()
        {
            if (IsDisposed || Disposing) return;
            try
            {
                _connection = new SdkCameraConnection();
                _connectionStatus.Text = CameraStartup.Start(_settings, _connection);
            }
            catch (Exception ex)
            {
                var vm = ex as VM.PlatformSDKCS.VmException;
                _connectionStatus.Text = "카메라 접속/로딩 실패: " +
                    (vm == null ? ex.Message : "VisionMaster 0x" + vm.errorCode.ToString("X8"));
                MonitorLog.Write(_connectionStatus.Text);
            }
        }
        private void Scan(object unused)
        {
            if (Interlocked.Exchange(ref _busy, 1) != 0) return;
            try
            {
                FolderImageSource source;
                lock (_gate) { if (_closed) return; source = _source; }
                if (source == null)
                {
                    source = new FolderImageSource(_settings.ImageFolder, _settings.DelayCount);
                    lock (_gate)
                    {
                        if (_closed) { source.Dispose(); return; }
                        _source = source;
                    }
                }
                source.Scan();
                lock (_gate) { _error = null; }
            }
            catch (Exception ex) { lock (_gate) { _error = "수신 폴더 확인 중: " + ex.Message; } }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (_parent != IntPtr.Zero)
                {
                    cp.Parent = _parent;
                    cp.Style = (cp.Style & ~unchecked((int)0x80000000)) | 0x40000000; // WS_CHILD, no WS_POPUP
                }
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetParent(Handle, _parent);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

        private void FitParent()
        {
            if (!IsWindow(_parent)) { Close(); return; }
            EmbeddedWindow.Fit(Handle, _parent, false);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            lock (_gate) { _closed = true; if (_source != null) _source.Dispose(); }
            if (_scanTimer != null) _scanTimer.Dispose();
            _layoutTimer.Stop(); _layoutTimer.Dispose();
            if (_connection != null)
                try { _connection.Dispose(); } catch (Exception ex) { MonitorLog.Write(ex.Message); }
            base.OnFormClosed(e);
        }
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hwnd);
    }
}
