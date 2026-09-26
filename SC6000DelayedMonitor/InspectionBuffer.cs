using System;
using System.Collections.Generic;
using System.Drawing;

namespace SC6000DelayedMonitor
{
    // Owns only application-created images, never an SDK object or borrowed buffer.
    internal sealed class InspectionResult : IDisposable
    {
        public long SequenceNo { get; private set; }
        public Bitmap Image { get; private set; }
        public bool IsOK { get; private set; }
        public DateTime Timestamp { get; private set; }
        public InspectionResult(long sequence, Bitmap image, bool ok)
        { SequenceNo = sequence; Image = image; IsOK = ok; Timestamp = DateTime.Now; }
        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }

    // Callers serialize Push/Reset/Take with their session lock.
    // Pending UI work holds at most one additional result, even if the UI is busy.
    internal sealed class InspectionBuffer : IDisposable
    {
        private readonly Queue<InspectionResult> _queue = new Queue<InspectionResult>();
        private InspectionResult _pending;
        public int Delay { get; private set; }
        public int Count { get { return _queue.Count; } }
        public InspectionBuffer(int delay)
        { if (delay < 0) throw new ArgumentOutOfRangeException("delay"); Delay = delay; }
        public void Push(InspectionResult result)
        {
            _queue.Enqueue(result);
            if (_queue.Count <= Delay) return;
            if (_pending != null) _pending.Dispose();
            _pending = _queue.Dequeue();
        }
        public InspectionResult Take()
        { var result = _pending; _pending = null; return result; }
        public void Reset()
        {
            while (_queue.Count > 0) _queue.Dequeue().Dispose();
            if (_pending != null) { _pending.Dispose(); _pending = null; }
        }
        public void Dispose() { Reset(); }
    }
}
