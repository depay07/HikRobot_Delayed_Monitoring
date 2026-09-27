using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SC6000DelayedMonitor;

internal static class QueueTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static InspectionResult Item(int sequence, bool ng, int? number = null)
    {
        var image = new Bitmap(320, 160);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(ng ? Color.Maroon : Color.DarkSlateGray);
            using (var font = new Font("Segoe UI", 26))
                g.DrawString("TEST " + (ng ? "NG " : "OK ") + (number ?? sequence), font, Brushes.White, 12, 45);
        }
        return new InspectionResult(sequence, image, "ESPACK_" + (ng ? "NG" : "OK") + "_" + (number ?? sequence).ToString("000") + ".png");
    }
    public static void Run(string directory)
    {
        foreach (int delay in new[] { 0, 12, 13, 14, 15 })
        foreach (var ngAt in new[] { new int[0], new[] { 3 }, new[] { 2, 3 }, new[] { 1, 2, 3 }, Enumerable.Range(1, 10).ToArray() })
        {
            using (var buffer = new InspectionBuffer(delay))
            {
                var arrivals = new List<int>();
                var filmSequences = new List<int>();
                for (int n = 1; n <= 50; ++n)
                {
                    buffer.Push(Item(n, ngAt.Contains(n)));
                    if (delay > 0 && (filmSequences.Count > 0 || ngAt.Contains(n)))
                    {
                        filmSequences.Add(n);
                        if (filmSequences.Count == delay) filmSequences.Clear();
                    }
                    using (var arrival = buffer.Take())
                    {
                        if (n <= delay) Check(arrival == null, "Arrival before required movements");
                        else
                        {
                            Check(arrival != null && arrival.SequenceNo == n - delay, "Arrival off by one");
                            if (arrival.Verdict == InspectionVerdict.NG) arrivals.Add(n);
                        }
                    }
                    var previews = buffer.Snapshot();
                    try
                    {
                        Check(previews.Count == filmSequences.Count, "NG film count/reset");
                        for (int i = 0; i < previews.Count; ++i)
                            Check(previews[i].SequenceNo == filmSequences[i] && previews[i].Moves == i + 1, "Film input order/one-based count");
                    }
                    finally { foreach (var preview in previews) preview.Dispose(); }
                }
                Check(arrivals.SequenceEqual(ngAt.Select(n => n + delay)), "Missing or merged consecutive NG arrivals");
            }
        }
        Console.WriteLine("PASS single/consecutive 2, 3, 10 NG and all-OK, exact FIFO arrival at delays 0/12/13/14/15");
        using (var buffer = new InspectionBuffer(13))
        using (var pane = new DelayedPane("CAMERA 1", 13))
        {
            buffer.Push(Item(1, true, 100)); buffer.Push(Item(2, true, 101));
            for (int i = 3; i <= 14; ++i) buffer.Push(Item(i, false, 99 + i));
            pane.ShowResult(buffer.Take());
            pane.ShowTrail(buffer.Snapshot());
            pane.ShowStatus("표시 갱신", true); // Releasing the displayed first NG must not clear later NGs.
            var snapshot = buffer.Snapshot();
            Check(snapshot.Count == 0, "Completed film not reset");
            foreach (var item in snapshot) item.Dispose();
            buffer.Push(Item(15, false, 114));
            using (var next = buffer.Take()) Check(next.Number == "101" && next.Verdict == InspectionVerdict.NG, "Next consecutive NG arrival lost");
        }
        Console.WriteLine("PASS first NG display release leaves next NG and sequence intact (physical Reset not controlled)");
        using (var buffer = new InspectionBuffer(13))
        {
            var originals = new List<InspectionResult>();
            for (int i = 1; i <= 2000; ++i) { var item = Item(i, i % 2 == 0); originals.Add(item); buffer.Push(item); }
            Check(originals.Count(i => i.Image != null) == 14, "Original image memory accumulated");
            var snapshots = buffer.Snapshot();
            Check(snapshots.Count < 13 && snapshots.All(p => p.Thumbnail.Width <= 160 && p.Thumbnail.Height <= 100), "Thumbnail bounds");
            buffer.Dispose();
            Check(originals.All(i => i.Image == null), "Original bitmap leak on close");
            Check(snapshots.All(i => i.Thumbnail.GetPixel(0, 0).A > 0), "UI snapshot borrows disposed image");
            foreach (var item in snapshots) { item.Dispose(); Check(item.Thumbnail == null, "Thumbnail disposal"); }
        }
        Console.WriteLine("PASS 2000-image bounded originals/thumbnails and independent UI snapshot lifetime");
        string folder = Path.Combine(directory, "ng-files"); Directory.CreateDirectory(folder);
        using (var source = new FolderImageSource(folder, 13))
        {
            for (int i = 1; i <= 15; ++i)
            {
                using (var item = Item(i, i <= 2)) item.Image.Save(Path.Combine(folder, item.FileName), System.Drawing.Imaging.ImageFormat.Png);
                source.Scan(true); source.Scan(true); source.Scan(true);
                string status;
                using (var arrived = source.Take(out status))
                    if (i >= 14) Check(arrived != null && arrived.Verdict == InspectionVerdict.NG && arrived.SequenceNo == i - 13, "Duplicate scan shifts NG sequence");
                Check(source.Take(out status) == null, "Duplicate arrival");
            }
        }
        Console.WriteLine("PASS repeated file scans do not duplicate named NG products");
        using (var unknown = new InspectionResult(1, new Bitmap(2, 2), "photo.png"))
            Check(unknown.Verdict == InspectionVerdict.Unknown && unknown.Number == "?", "Unknown filename treated as OK");
        using (var buffer = new InspectionBuffer(13))
        using (var pane = new DelayedPane("CAMERA 1", 13))
        {
            for (int i = 1; i <= 14; ++i) buffer.Push(Item(i, i <= 3));
            for (int i = 15; i <= 22; ++i) buffer.Push(Item(i, i == 15));
            pane.Size = new Size(1440, 800); pane.CreateControl(); pane.PerformLayout();
            pane.ShowResult(buffer.Take()); pane.ShowTrail(buffer.Snapshot());
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var picture = (Control)typeof(DelayedPane).GetField("_picture", flags).GetValue(pane);
            var trail = (Control)typeof(DelayedPane).GetField("_trail", flags).GetValue(pane);
            Check(picture.Right <= trail.Left && trail.Width > 0 && picture.Width > trail.Width, "Thumbnail layout overlaps main image");
            using (var bitmap = new Bitmap(pane.Width, pane.Height))
            { pane.DrawToBitmap(bitmap, pane.ClientRectangle); bitmap.Save(Path.Combine(directory, "queue-preview.png")); }
        }
        Console.WriteLine("PASS right-side preview layout and render");
        foreach (int count in new[] { 0, 1, 2, 12, 13, 14, 15 })
        using (var buffer = new InspectionBuffer(count))
        {
            int sequence=0;
            for(int i=0;i<20;i++) buffer.Push(Item(++sequence,false));
            CheckFilm(buffer,0);
            // Repeated complete batches; an NG in the middle must not restart the count.
            for(int cycle=0;cycle<3;cycle++)
            {
                for(int i=1;i<=Math.Max(1,count);i++)
                {
                    buffer.Push(Item(++sequence,i==1 || i==2));
                    CheckFilm(buffer,count==0 || i==count ? 0 : i);
                }
                for(int i=0;i<4;i++) { buffer.Push(Item(++sequence,false)); CheckFilm(buffer,0); }
            }
        }
        Console.WriteLine("PASS NG-only film trigger, NG included as 1, exact count reset, OK idle, consecutive NG, retrigger, counts 0/1/2/12/13/14/15");
    }
    private static void CheckFilm(InspectionBuffer buffer,int expected)
    {
        var items=buffer.Snapshot();
        try { Check(items.Count==expected,"Film lifecycle expected="+expected+", actual="+items.Count); }
        finally { foreach(var item in items) item.Dispose(); }
    }
}
