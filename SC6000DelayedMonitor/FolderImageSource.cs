using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

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
        private volatile bool _disposed;
        private readonly ConcurrentDictionary<string, byte> _changed = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _discoveryClock = Stopwatch.StartNew();
        private FileSystemWatcher _watcher;
        private int _rescan = 1;
        private int _watcherFailed;
        private long _nextDiscovery;
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
                foreach (var path in ImagePaths()) _seen.Add(Path.GetFullPath(path));
        }
        private IEnumerable<string> ImagePaths()
        {
            return Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(p => Extensions.Contains(Path.GetExtension(p)));
        }
        private void Changed(object sender, FileSystemEventArgs e)
        {
            if (_disposed) return;
            if (Extensions.Contains(Path.GetExtension(e.FullPath))) _changed[e.FullPath] = 0;
            // A new/renamed date directory may already contain images when discovered.
            else if (e.ChangeType != WatcherChangeTypes.Changed) Interlocked.Exchange(ref _rescan, 1);
        }
        private void EnsureWatcher()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (Interlocked.Exchange(ref _watcherFailed, 0) != 0 && _watcher != null)
                { _watcher.Dispose(); _watcher = null; }
                if (_watcher != null) return;
                FileSystemWatcher watcher = null;
                try
                {
                    watcher = new FileSystemWatcher(_root) { IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 32768 };
                    watcher.Created += Changed; watcher.Changed += Changed; watcher.Renamed += Changed;
                    watcher.Error += delegate { Interlocked.Exchange(ref _rescan, 1); Interlocked.Exchange(ref _watcherFailed, 1); };
                    watcher.EnableRaisingEvents = true;
                    _watcher = watcher;
                }
                catch (IOException) { if (watcher != null) watcher.Dispose(); }
                catch (UnauthorizedAccessException) { if (watcher != null) watcher.Dispose(); }
            }
        }
        private FileInfo[] Files(bool forceRescan)
        {
            EnsureWatcher();
            // Notifications accelerate discovery. Periodic reconciliation also recovers lost events.
            if (forceRescan || Interlocked.Exchange(ref _rescan, 0) != 0 || _discoveryClock.ElapsedMilliseconds >= _nextDiscovery)
            {
                foreach (var path in ImagePaths())
                {
                    string fullPath = Path.GetFullPath(path);
                    if (!_seen.Contains(fullPath)) _pendingPaths.Add(fullPath);
                }
                _nextDiscovery = _discoveryClock.ElapsedMilliseconds + (_watcher == null ? 200 : 2000);
            }
            foreach (var path in _changed.Keys)
            {
                byte unused;
                if (_changed.TryRemove(path, out unused) && !_seen.Contains(path)) _pendingPaths.Add(path);
            }
            var candidates = _pendingPaths.Select(p => new FileInfo(p)).ToArray();
            foreach (var missing in candidates.Where(f => !f.Exists))
            { _pendingPaths.Remove(missing.FullName); _observed.Remove(missing.FullName); }
            return candidates.Where(f => f.Exists).OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public void Scan(bool forceRescan = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_root)) { Status("config.ini의 IMAGE_FOLDER에 PC 수신 폴더를 입력하세요."); return; }
                if (_disposed) return;
                if (!Directory.Exists(_root))
                {
                    Interlocked.Exchange(ref _watcherFailed, 1); Interlocked.Exchange(ref _rescan, 1);
                    Status("수신 폴더 생성 대기 중: " + _root); return;
                }
                var files = Files(forceRescan);
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
                        using (var image = (Bitmap)Image.FromStream(stream, true, true))
                            // Detach decoded pixels without drawing the entire original into a new 32-bit bitmap.
                            copy = image.Clone(new Rectangle(0, 0, image.Width, image.Height), image.PixelFormat);
                    }
                    catch (IOException) { Status("이미지 수신 중: " + file.Name); return; }
                    catch (ArgumentException) { Status("이미지 저장 완료 대기: " + file.Name); return; }
                    catch (OutOfMemoryException) { Status("이미지 읽기 대기: " + file.Name); return; }
                    var result = new InspectionResult(_sequence + 1, copy, file.FullName);
                    InspectionPreview preview;
                    try { preview = InspectionPreview.Create(result); }
                    catch { result.Dispose(); throw; }
                    lock (_gate)
                    {
                        if (_disposed) { preview.Dispose(); result.Dispose(); return; }
                        try { _buffer.Push(result, preview); }
                        catch { preview.Dispose(); result.Dispose(); throw; }
                        ++_sequence;
                        _status = _sequence <= _buffer.Delay ? "새 이미지 대기 중: " + _buffer.Count + " / " + _buffer.Delay : null;
                    }
                    _seen.Add(file.FullName); _observed.Remove(file.FullName); _pendingPaths.Remove(file.FullName);
                }
            }
            catch (Exception ex) { Status("수신 폴더 확인 중: " + ex.Message); }
        }
        private void Status(string text) { lock (_gate) { _status = text; } }
        public InspectionResult Take(out string status)
        { lock (_gate) { status = _status; return _disposed ? null : _buffer.Take(); } }
        public InspectionResult Take(out string status, ref long version, out List<InspectionPreview> previews)
        {
            lock (_gate)
            {
                status = _status; previews = null;
                if (_disposed) return null;
                if (version != _buffer.Version)
                {
                    previews = _buffer.Snapshot();
                    version = _buffer.Version;
                }
                return _buffer.Take();
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
                _buffer.Dispose();
            }
        }
    }
}
