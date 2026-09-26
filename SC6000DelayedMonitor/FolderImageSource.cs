using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace SC6000DelayedMonitor
{
    // Scan runs on one background worker. The delay advances only on decoded files.
    internal sealed class FolderImageSource : IDisposable
    {
        private readonly string _root;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _observed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly InspectionBuffer _buffer;
        private readonly object _gate = new object();
        private bool _disposed;
        private long _sequence;
        private string _status;
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public FolderImageSource(string root, int delay)
        {
            _root = root; _buffer = new InspectionBuffer(delay);
            _status = "새 이미지 대기 중: 0 / " + delay;
            // Existing historical files are not inspections received in this run.
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                foreach (var file in Files()) _seen.Add(file.FullName);
        }
        private FileInfo[] Files()
        {
            return Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(p => Extensions.Contains(Path.GetExtension(p)))
                .Select(p => new FileInfo(p)).OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public void Scan()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_root)) { Status("config.ini의 IMAGE_FOLDER에 PC 수신 폴더를 입력하세요."); return; }
                if (!Directory.Exists(_root)) { Status("수신 폴더 생성 대기 중: " + _root); return; }
                var files = Files();
                foreach (var file in files)
                {
                    lock (_gate) { if (_disposed) return; }
                    if (_seen.Contains(file.FullName)) continue;
                    string fingerprint = file.Length + ":" + file.LastWriteTimeUtc.Ticks;
                    string previous;
                    bool stable = _observed.TryGetValue(file.FullName, out previous) && previous == fingerprint;
                    _observed[file.FullName] = fingerprint;
                    if (!stable)
                    {
                        // Observe all candidates, but never let a later file overtake an unfinished one.
                        foreach (var later in files.Where(f => !_seen.Contains(f.FullName) && f.FullName != file.FullName))
                            _observed[later.FullName] = later.Length + ":" + later.LastWriteTimeUtc.Ticks;
                        Status("이미지 저장 완료 확인 중"); return;
                    }
                    Bitmap copy;
                    try
                    {
                        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None))
                        using (var image = Image.FromStream(stream, true, true)) copy = new Bitmap(image);
                    }
                    catch (IOException) { Status("이미지 수신 중: " + file.Name); return; }
                    catch (ArgumentException) { Status("이미지 저장 완료 대기: " + file.Name); return; }
                    catch (OutOfMemoryException) { Status("이미지 읽기 대기: " + file.Name); return; }
                    lock (_gate)
                    {
                        if (_disposed) { copy.Dispose(); return; }
                        _buffer.Push(new InspectionResult(++_sequence, copy, file.FullName));
                        _status = _sequence <= _buffer.Delay ? "새 이미지 대기 중: " + _buffer.Count + " / " + _buffer.Delay : null;
                    }
                    _seen.Add(file.FullName); _observed.Remove(file.FullName);
                }
            }
            catch (Exception ex) { Status("수신 폴더 확인 중: " + ex.Message); }
        }
        private void Status(string text) { lock (_gate) { _status = text; } }
        public InspectionResult Take(out string status)
        { lock (_gate) { status = _status; return _disposed ? null : _buffer.Take(); } }
        public void Dispose() { lock (_gate) { _disposed = true; _buffer.Dispose(); } }
    }
}
