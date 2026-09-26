using System;
using System.Drawing;
using System.Windows.Forms;
using VM.Core;
using VM.PlatformSDKCS;
using VMControls.Interface;

namespace SC6000DelayedMonitor
{
    // One instance per camera process. No Run, Stop, Load or global callback disabling.
    internal sealed class InspectionSession : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Control _dispatcher;
        private readonly DelayedPane _pane;
        private readonly CameraSettings _settings;
        private readonly InspectionBuffer _buffer;
        private readonly VmSolution _solution;
        private VmProcedure _procedure;
        private EventHandler _handler;
        private long _generation, _sequence;
        private uint? _lastExecute;
        private bool _disposed, _uiQueued, _clearImage = true, _faulted;
        private string _status = "연결 대기 중";

        public InspectionSession(CameraSettings settings, Control dispatcher, DelayedPane pane)
        {
            _settings = settings; _dispatcher = dispatcher; _pane = pane;
            _buffer = new InspectionBuffer(settings.DelayCount);
            _solution = VmSolution.Instance;
            VmSolution.OnSolutionLoadBeginEvent += LoadBegin;
            VmSolution.OnSolutionLoadEndEvent += LoadEnd;
            VmSolution.OnServerStatusEvent += ServerLost;
            VmSolution.OnProxyCrashEvent += ProxyLost;
            VmSolution.OnProcedureUnRegisterEvent += ProcedureRemoved;
            if (_solution != null) _solution.InstanceChanged += InstanceChanged;
        }

        public void Bind()
        {
            Reset("Connected / 결과 연결 준비 중");
            if (string.IsNullOrWhiteSpace(_settings.ProcedureName) || string.IsNullOrWhiteSpace(_settings.ImageOutput) ||
                string.IsNullOrWhiteSpace(_settings.ResultOutput))
            {
                Reset("Connected / 출력 설정 필요\r\nPROCEDURE, IMAGE_OUTPUT, RESULT_OUTPUT을 지정해 주세요.");
                return;
            }
            try
            {
                var solution = VmSolution.Instance;
                if (solution == null) throw new InvalidOperationException("카메라 연결이 없습니다.");
                var procedure = solution[_settings.ProcedureName] as VmProcedure;
                if (procedure == null) throw new InvalidOperationException("설정한 Procedure를 찾을 수 없습니다.");
                lock (_gate)
                {
                    if (_disposed) return;
                    long generation = _generation;
                    _procedure = procedure;
                    _handler = (sender, args) => Receive(procedure, generation, args);
                    procedure.ModuleResultCallBackArrived += _handler;
                    _status = "Connected / Waiting for delayed image...\r\n0 / " + _settings.DelayCount;
                    ScheduleUi();
                }
                procedure.EnableResultCallback();
            }
            catch (Exception ex) { Reset("결과 연결 실패: " + Describe(ex)); MonitorLog.Write("Result binding: " + Describe(ex)); }
        }

        private void Receive(VmProcedure procedure, long generation, EventArgs args)
        {
            lock (_gate)
            {
                if (_disposed || _faulted || generation != _generation || procedure != _procedure) return;
                InspectionResult snapshot = null;
                try
                {
                    var numbered = args as ValueEventArgs;
                    if (numbered != null)
                    {
                        // Zero may mean the SDK did not provide an execution number.
                        if (numbered.ExecuteCount != 0)
                        {
                            if (_lastExecute == numbered.ExecuteCount) return;
                            if (_lastExecute.HasValue && numbered.ExecuteCount != unchecked(_lastExecute.Value + 1))
                            {
                                _buffer.Reset(); _sequence = 0; _clearImage = true;
                                MonitorLog.Write("Execution count discontinuity; delayed history cleared.");
                            }
                            _lastExecute = numbered.ExecuteCount;
                        }
                    }
                    // Read both outputs synchronously in this procedure's result callback.
                    // Do not defer SDK reads to the UI, where the next cycle could replace them.
                    var result = procedure.ModuResult;
                    if (result.ErrorCode != 0)
                        throw new InvalidOperationException("Procedure 실행 오류 0x" + result.ErrorCode.ToString("X8"));
                    var values = result.GetOutputInt(_settings.ResultOutput);
                    if (values.nValueNum != 1 || values.pIntVal == null || values.pIntVal.Length < 1)
                        throw new InvalidOperationException("RESULT_OUTPUT은 검사당 정수 1개여야 합니다.");
                    int value = values.pIntVal[0];
                    if (value != _settings.OkValue && value != _settings.NgValue)
                        throw new InvalidOperationException("판정값이 OK_VALUE/NG_VALUE와 다릅니다.");
                    var image = result.GetOutputImageV2(_settings.ImageOutput);
                    try
                    {
                        if (image == null || image.Width <= 0 || image.Height <= 0)
                            throw new InvalidOperationException("IMAGE_OUTPUT에 검사 이미지가 없습니다.");
                        using (var bitmap = image.ToBitmap())
                        {
                            // GDI+ clone breaks any dependency on the SDK/native lifetime.
                            snapshot = new InspectionResult(++_sequence, new Bitmap(bitmap), value == _settings.OkValue);
                        }
                    }
                    finally { if (image != null) image.Dispose(); }
                    _buffer.Push(snapshot); snapshot = null;
                    _status = "Connected / Waiting for delayed image...\r\n" + _buffer.Count + " / " + _settings.DelayCount;
                    ScheduleUi();
                }
                catch (Exception ex)
                {
                    if (snapshot != null) snapshot.Dispose();
                    // Never silently omit a failed inspection and shift the product alignment.
                    _buffer.Reset(); _clearImage = true; _faulted = true;
                    _status = "지연 표시 중단: " + Describe(ex) + "\r\n출력 설정 확인 후 ‘결과 다시 연결’을 누르세요.";
                    MonitorLog.Write("Inspection capture stopped: " + Describe(ex)); ScheduleUi();
                }
            }
        }

        private void ScheduleUi()
        {
            if (_disposed || _uiQueued || !_dispatcher.IsHandleCreated) return;
            _uiQueued = true;
            try { _dispatcher.BeginInvoke(new Action(DrainUi)); }
            catch (InvalidOperationException) { _uiQueued = false; }
        }
        private void DrainUi()
        {
            lock (_gate)
            {
                _uiQueued = false;
                if (_disposed || _pane.IsDisposed) return;
                if (_clearImage) { _pane.ShowStatus(_status, true); _clearImage = false; }
                var result = _buffer.Take();
                if (result != null) _pane.ShowResult(result);
                else if (_sequence <= _settings.DelayCount || _faulted || _procedure == null)
                    _pane.ShowStatus(_status, false);
            }
        }
        public void Reset(string reason)
        {
            lock (_gate)
            {
                if (_disposed) return;
                ++_generation;
                if (_procedure != null && _handler != null) _procedure.ModuleResultCallBackArrived -= _handler;
                _procedure = null; _handler = null; _lastExecute = null;
                _buffer.Reset(); _sequence = 0; _faulted = false; _clearImage = true; _status = reason;
                ScheduleUi();
            }
        }
        private void LoadBegin(ImvsSdkDefine.IMVS_SOLUTION_LOAD_BEGEIN_INFO info) { Reset("Solution 변경 중 / 지연 기록 초기화"); }
        private void LoadEnd(ImvsSdkDefine.IMVS_SOLUTION_LOAD_END_INFO info)
        {
            Reset("Solution 로딩 완료 / 지연 기록 초기화");
            long generation;
            lock (_gate) { generation = _generation; }
            // Resolve a fresh Procedure only after SDK load notification has returned.
            if (_dispatcher.IsHandleCreated)
                try { _dispatcher.BeginInvoke(new Action(delegate
                {
                    lock (_gate) { if (_disposed || generation != _generation) return; }
                    Bind();
                })); }
                catch (InvalidOperationException) { }
        }
        private void ServerLost(ImvsSdkDefine.IMVS_SERVER_INFO info) { Reset("Disconnected / 프로그램을 다시 실행해 주세요."); }
        private void ProxyLost(ImvsSdkDefine.IMVS_PROXY_CRASH_SP_INFO info) { Reset("SDK 연결 종료 / 프로그램을 다시 실행해 주세요."); }
        private void ProcedureRemoved(ImvsSdkDefine.IMVS_PROCEDURE_UNREGISTER_INFO info) { Reset("Procedure 변경 / 결과를 다시 연결해 주세요."); }
        private void InstanceChanged(object sender, EventArgs e) { Reset("연결 대상 변경 / 지연 기록 초기화"); }
        internal static string Describe(Exception ex)
        {
            var vm = ex as VmException;
            return vm == null ? ex.Message : "VisionMaster error 0x" + vm.errorCode.ToString("X8");
        }
        public void Dispose()
        {
            Reset("종료");
            lock (_gate) { _disposed = true; _buffer.Dispose(); }
            VmSolution.OnSolutionLoadBeginEvent -= LoadBegin;
            VmSolution.OnSolutionLoadEndEvent -= LoadEnd;
            VmSolution.OnServerStatusEvent -= ServerLost;
            VmSolution.OnProxyCrashEvent -= ProxyLost;
            VmSolution.OnProcedureUnRegisterEvent -= ProcedureRemoved;
            if (_solution != null) _solution.InstanceChanged -= InstanceChanged;
        }
    }
}
