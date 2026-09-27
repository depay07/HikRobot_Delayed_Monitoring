using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;

namespace SC6000DelayedMonitor
{
    internal enum InspectionVerdict { Unknown, OK, NG }

    // Owns only application-created images, never an SDK object or borrowed buffer.
    internal sealed class InspectionResult : IDisposable
    {
        public long SequenceNo { get; private set; }
        public Bitmap Image { get; private set; }
        public string FilePath { get; private set; }
        public string FileName { get { return Path.GetFileName(FilePath); } }
        public string Number { get; private set; }
        public InspectionVerdict Verdict { get; private set; }
        public DateTime Timestamp { get; private set; }
        public InspectionResult(long sequence, Bitmap image, string filePath)
        {
            SequenceNo = sequence; Image = image; FilePath = filePath; Timestamp = DateTime.Now;
            string name = Path.GetFileNameWithoutExtension(filePath);
            Verdict = name.IndexOf("_NG_", StringComparison.OrdinalIgnoreCase) >= 0 ? InspectionVerdict.NG :
                name.IndexOf("_OK_", StringComparison.OrdinalIgnoreCase) >= 0 ? InspectionVerdict.OK : InspectionVerdict.Unknown;
            var number = Regex.Match(name, @"_(\d+)$");
            Number = number.Success ? number.Groups[1].Value : "?";
        }
        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }

    // Callers serialize Push/Reset/Take with their session lock.
    // Pending UI work holds at most one additional result, even if the UI is busy.
    internal sealed class InspectionBuffer : IDisposable
    {
        private readonly Queue<InspectionResult> _queue = new Queue<InspectionResult>();
        private InspectionResult _pending;
        private readonly Queue<InspectionPreview> _trail = new Queue<InspectionPreview>();
        public long Version { get; private set; }
        public int Delay { get; private set; }
        public int Count { get { return _queue.Count; } }
        public InspectionBuffer(int delay)
        { if (delay < 0) throw new ArgumentOutOfRangeException("delay"); Delay = delay; }
        public void Push(InspectionResult result, InspectionPreview preparedPreview = null)
        {
            // Keep only small thumbnails for the right-hand conveyor view.
            _trail.Enqueue(preparedPreview ?? InspectionPreview.Create(result));
            while (_trail.Count > (long)Delay + 1) _trail.Dequeue().Dispose();
            ++Version;
            _queue.Enqueue(result);
            if (_queue.Count <= Delay) return;
            if (_pending != null) _pending.Dispose();
            _pending = _queue.Dequeue();
        }
        public InspectionResult Take()
        { var result = _pending; _pending = null; return result; }
        public List<InspectionPreview> Snapshot()
        {
            var items = _trail.ToArray();
            var snapshots = new List<InspectionPreview>();
            try
            {
                for (int i = items.Length - 1; i >= 0; --i)
                    snapshots.Add(items[i].Copy(items.Length - 1 - i));
                return snapshots;
            }
            catch { foreach (var item in snapshots) item.Dispose(); throw; }
        }
        public void Reset()
        {
            while (_queue.Count > 0) _queue.Dequeue().Dispose();
            if (_pending != null) { _pending.Dispose(); _pending = null; }
            while (_trail.Count > 0) _trail.Dequeue().Dispose();
            ++Version;
        }
        public void Dispose() { Reset(); }
    }

    // UI snapshots own their thumbnails; no SDK/file handle or original image is retained here.
    internal sealed class InspectionPreview : IDisposable
    {
        public Bitmap Thumbnail { get; private set; }
        public long SequenceNo { get; private set; }
        public string FileName { get; private set; }
        public string Number { get; private set; }
        public InspectionVerdict Verdict { get; private set; }
        public int Moves { get; private set; }
        private InspectionPreview() { }
        public static InspectionPreview Create(InspectionResult result)
        {
            double scale = Math.Min(160.0 / result.Image.Width, 100.0 / result.Image.Height);
            var bitmap = new Bitmap(Math.Max(1, (int)(result.Image.Width * scale)), Math.Max(1, (int)(result.Image.Height * scale)));
            try
            {
                using (var g = Graphics.FromImage(bitmap))
                    g.DrawImage(result.Image, new Rectangle(Point.Empty, bitmap.Size));
                return new InspectionPreview { Thumbnail = bitmap, SequenceNo = result.SequenceNo,
                    FileName = result.FileName, Number = result.Number, Verdict = result.Verdict };
            }
            catch { bitmap.Dispose(); throw; }
        }
        public InspectionPreview Copy(int moves)
        {
            return new InspectionPreview { Thumbnail = new Bitmap(Thumbnail), SequenceNo = SequenceNo,
                FileName = FileName, Number = Number, Verdict = Verdict, Moves = moves };
        }
        public void Dispose() { if (Thumbnail != null) { Thumbnail.Dispose(); Thumbnail = null; } }
    }
}
