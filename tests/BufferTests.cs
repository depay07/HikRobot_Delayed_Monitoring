using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SC6000DelayedMonitor;
internal static class BufferTests
{
    static void Check(bool ok, string why) { if(!ok) throw new Exception(why); }
    static readonly DateTime Epoch = new DateTime(2026,9,26,0,0,0,DateTimeKind.Utc);
    static byte[] Bytes(int n)
    {
        using(var image=new Bitmap(24,16)) using(var stream=new MemoryStream())
        { image.SetPixel(0,0,Color.FromArgb(n%255,20,30)); image.Save(stream,ImageFormat.Png); return stream.ToArray(); }
    }
    static void Save(string path, int n)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,Bytes(n)); File.SetLastWriteTimeUtc(path,Epoch.AddSeconds(n)); }
    static InspectionResult Read(FolderImageSource source) { string status; return source.Take(out status); }
    [STAThread] static void Main()
    {
        try
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ftp-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            foreach(int delay in new[]{0,1,14})
            {
                string dir=Path.Combine(root,"delay"+delay); Directory.CreateDirectory(dir);
                Save(Path.Combine(dir,"old.png"),0);
                using(var source=new FolderImageSource(dir,delay))
                {
                    source.Scan(); Check(Read(source)==null,"Old file counted");
                    for(int n=1;n<=40;n++)
                    {
                        // Switch date folders without restarting or resetting the count.
                        Save(Path.Combine(dir,n<20?"2026/09/26":"2026/09/27",n.ToString()+".png"),n);
                        source.Scan(); source.Scan();
                        using(var shown=Read(source))
                        {
                            if(n<=delay) Check(shown==null,"Warmup off by one");
                            else { Check(shown!=null && shown.SequenceNo==n-delay,"Delay sequence"); Check(shown.Image.GetPixel(0,0).R==(n-delay)%255,"Wrong saved image"); }
                        }
                        source.Scan(); Check(Read(source)==null,"Counted same file twice");
                    }
                }
                Console.WriteLine("PASS folder delay="+delay+": exact sequence, old files ignored, duplicate scans, date rollover");
            }
            string batch=Path.Combine(root,"batch"); Directory.CreateDirectory(batch);
            using(var source=new FolderImageSource(batch,14))
            {
                for(int n=15;n>=1;n--) Save(Path.Combine(batch,(100-n)+".png"),n);
                source.Scan(); source.Scan();
                using(var shown=Read(source)) Check(shown!=null && shown.Image.GetPixel(0,0).R==1,"Files not sorted by saved time");
            }
            Console.WriteLine("PASS reverse creation/filename order sorted by last-write timestamp: 15 -> 1");
            string partial=Path.Combine(root,"partial"); Directory.CreateDirectory(partial);
            using(var source=new FolderImageSource(partial,1))
            {
                string first=Path.Combine(partial,"first.png"); byte[] bytes=Bytes(1);
                using(var writer=new FileStream(first,FileMode.Create,FileAccess.Write,FileShare.ReadWrite))
                {
                    writer.Write(bytes,0,bytes.Length/2); writer.Flush(); File.SetLastWriteTimeUtc(first,Epoch.AddSeconds(1));
                    Save(Path.Combine(partial,"second.png"),2);
                    source.Scan(); source.Scan(); Check(Read(source)==null,"Read partial image or overtook writer");
                    writer.Write(bytes,bytes.Length/2,bytes.Length-bytes.Length/2);
                }
                File.SetLastWriteTimeUtc(first,Epoch.AddSeconds(1));
                source.Scan(); source.Scan();
                using(var shown=Read(source)) Check(shown!=null && shown.Image.GetPixel(0,0).R==1,"Partial upload recovery");
            }
            Console.WriteLine("PASS incomplete/locked FTP upload retried in sequence");
            string future=Path.Combine(root,"future");
            using(var source=new FolderImageSource(future,0))
            { source.Scan(); Save(Path.Combine(future,"2026/09/27/image.png"),3); source.Scan(); source.Scan(); using(var shown=Read(source)) Check(shown!=null,"New root/date directory missed"); }
            using(var queue=new InspectionBuffer(14))
            {
                var all=new System.Collections.Generic.List<InspectionResult>();
                for(int n=1;n<=1000;n++){var r=new InspectionResult(n,new Bitmap(2,2),n+".png");all.Add(r);queue.Push(r);}
                int alive=0;foreach(var r in all) if(r.Image!=null) alive++;
                Check(alive==15,"UI backlog memory unbounded");queue.Reset();foreach(var r in all) Check(r.Image==null,"Reset leak");
            }
            Console.WriteLine("PASS late folder creation and bounded/disposed image memory");
            string ini=Path.Combine(root,"config.ini"); File.WriteAllText(ini,"CAMERAS=1\nDELAY_COUNT=14\nIMAGE_FOLDER=D:\\vision\n[CAMERA1]\nTITLE=검사\n");
            var settings=IniConfig.Load(ini).GetCamera(1);Check(settings.ImageFolder==@"D:\vision"&&settings.DelayCount==14,"Simple folder config");
            foreach(string invalid in new[]{"-1","bad","2147483648"})
            { File.WriteAllText(ini,"DELAY_COUNT="+invalid);Check(IniConfig.Load(ini).DelayCount==0,"Invalid delay fallback"); }
            Console.WriteLine("PASS folder-only config without camera IP or SDK output names");
        }
        catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    }
}