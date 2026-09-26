using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SC6000DelayedMonitor
{
    internal sealed class MonitorForm : Form
    {
        private readonly List<Process> _viewers = new List<Process>();
        private readonly List<Panel> _panels = new List<Panel>();
        private readonly List<Label> _statuses = new List<Label>();
        private readonly Timer _watch = new Timer { Interval = 200 };
        private readonly string _exePath;
        private readonly string _baseDir;
        private Image _logo;

        public MonitorForm(IniConfig config, string exePath, string baseDir)
        {
            _exePath = exePath;
            _baseDir = baseDir;
            AutoScaleMode = AutoScaleMode.None;
            Text = "ASPEC | SC6000 Operation Interface Monitor";
            Icon = Icon.ExtractAssociatedIcon(exePath);
            StartPosition = FormStartPosition.Manual;
            var screens = Screen.AllScreens;
            Bounds = screens[Math.Min(config.MonitorIndex, screens.Length - 1)].WorkingArea;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(640, 400);
            BackColor = Color.White;

            var policy = new CameraLayout(config.CameraCount);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 90));
            LoadLogo();
            var header = new MonitorHeader(_logo, config.InspectionText);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = policy.Columns, RowCount = policy.Rows,
                Margin = Padding.Empty, Padding = Padding.Empty, GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                BackColor = Color.FromArgb(45, 52, 56) };
            for (int row = 0; row < policy.Rows; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / policy.Rows));
            for (int col = 0; col < policy.Columns; col++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / policy.Columns));
            for (int i = 0; i < config.CameraCount; i++)
            {
                CameraSettings camera = config.GetCamera(i + 1);
                var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.FromArgb(45, 52, 56) };
                var status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White,
                    Text = camera.Title + "\r\n" + camera.Ip + "\r\n연결 준비 중...", Font = new Font("Segoe UI", 12) };
                panel.Controls.Add(status);
                Point cell = policy.Cell(i);
                grid.Controls.Add(panel, cell.X, cell.Y);
                panel.SizeChanged += delegate { FitViewers(); };
                _panels.Add(panel);
                _statuses.Add(status);
            }
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            Controls.Add(layout);
            Shown += delegate { StartViewers(); };
            _watch.Tick += delegate
            {
                FitViewers();
                for (int i = 0; i < _viewers.Count; i++)
                    if (_viewers[i] == null || _viewers[i].HasExited)
                        _statuses[i].Text = "카메라 " + (i + 1) + " 화면이 종료되었습니다.\r\n프로그램을 다시 실행해 주세요.";
            };
        }

        private void FitViewers()
        {
            for (int i = 0; i < _viewers.Count; i++)
            {
                Process process = _viewers[i];
                if (process != null && !process.HasExited) EmbeddedWindow.FitChildren(_panels[i], process.Id);
            }
        }

        private void LoadLogo()
        {
            // Try every logo.* file; GDI+ determines whether its content is supported.
            string[] preferred = { ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tif", ".tiff", ".ico", ".wmf", ".emf" };
            foreach (string file in Directory.GetFiles(_baseDir).Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), "logo", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => { int n = Array.IndexOf(preferred, Path.GetExtension(f).ToLowerInvariant()); return n < 0 ? int.MaxValue : n; }).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using (var stream = new MemoryStream(File.ReadAllBytes(file)))
                    using (var source = Image.FromStream(stream))
                        _logo = new Bitmap(source);

                    return;
                }
                catch (Exception ex) { Trace.WriteLine("Logo load failed: " + file + " " + ex.Message); }
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);
        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private void StartViewers()
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                try
                {
                    _viewers.Add(Process.Start(new ProcessStartInfo
                    {
                        FileName = _exePath,
                        WorkingDirectory = _baseDir,
                        UseShellExecute = false,
                        Arguments = "--viewer --slot=" + (i + 1) + " --parent=" + _panels[i].Handle.ToInt64()
                    }));
                }
                catch (Exception ex)
                {
                    _viewers.Add(null);
                    MessageBox.Show(this, "카메라 " + (i + 1) + " 화면을 시작하지 못했습니다.\r\n" + ex.Message, "ASPEC", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            _watch.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            _watch.Stop();
            foreach (Panel panel in _panels)
                EnumChildWindows(panel.Handle, delegate(IntPtr window, IntPtr unused)
                {
                    uint pid; GetWindowThreadProcessId(window, out pid);
                    if (GetParent(window) == panel.Handle && _viewers.Any(p => p != null && p.Id == pid)) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
            foreach (Process process in _viewers)
            {
                if (process == null) continue;
                try
                {
                    if (!process.HasExited)
                    {
                        // Only our own viewer processes; allow SDK disposal before termination.
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1500)) process.Kill();
                    }
                }
                catch (Exception ex) { Trace.WriteLine(ex); }
                finally { process.Dispose(); }
            }
            _watch.Dispose();
            if (_logo != null) _logo.Dispose();

        }
    }
}
