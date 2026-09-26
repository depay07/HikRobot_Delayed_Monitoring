using System;
using VM.Core;
using VM.PlatformSDKCS;

namespace SC6000DelayedMonitor
{
    internal interface ICameraConnection
    {
        void Connect(string ip, ushort port, string password);
        void Load(string path, string password);
    }
    internal sealed class SdkCameraConnection : ICameraConnection, IDisposable
    {
        private bool _connected;
        public void Connect(string ip, ushort port, string password)
        {
            VmSolution.SetControlMode(ControlModeType.REMOTE);
            VmSolution.GetSolutionInstanceToDevice(new DeviceModeInfo
            { emDeviceMode = DeviceModeType.NET, strDevIP = ip, nPort = port, strPassword = password });
            _connected = true;
        }
        public void Load(string path, string password) { VmSolution.Load(path, password); }
        public void Dispose()
        {
            // Release this client's SDK connection; never issue Stop or unload the camera Solution.
            if (_connected) { _connected = false; VmSolution.Instance?.Dispose(); }
        }
    }
    internal static class CameraStartup
    {
        public static string Start(CameraSettings settings, ICameraConnection connection)
        {
            if (string.IsNullOrWhiteSpace(settings.Ip)) return "카메라 IP 미설정 / FTP 이미지 표시 중";
            connection.Connect(settings.Ip, settings.Port, settings.Password);
            if (string.IsNullOrWhiteSpace(settings.SolutionPath)) return "카메라 접속 완료 / 자동 로딩 파일 미설정";
            connection.Load(settings.SolutionPath, settings.SolutionPassword);
            return "카메라 접속 및 Solution 로딩 완료";
        }
    }
}
