using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SC6000DelayedMonitor;
internal static class BufferTests
{
    sealed class FakeConnection : ICameraConnection
    {
        public string Address, Password, Path, FilePassword;
        public ushort Port;
        public void Connect(string ip, ushort port, string password) { Address=ip; Port=port; Password=password; }
        public void Load(string path, string password) { Check(Address!=null,"Load before connect"); Path=path; FilePassword=password; }
    }
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
            QueueTests.Run(root);
            WatcherTests(root);
            foreach(int delay in new[]{0,1,14})
            {
                string dir=Path.Combine(root,"delay"+delay); Directory.CreateDirectory(dir);
                Save(Path.Combine(dir,"old.png"),0);
                using(var source=new FolderImageSource(dir,delay))
                {
                    source.Scan(true); Check(Read(source)==null,"Old file counted");
                    for(int n=1;n<=40;n++)
                    {
                        // Switch date folders without restarting or resetting the count.
                        Save(Path.Combine(dir,n<20?"2026/09/26":"2026/09/27",n.ToString()+".png"),n);
                        source.Scan(true); source.Scan(true);
                        using(var shown=Read(source))
                        {
                            if(n<=delay) Check(shown==null,"Warmup off by one");
                            else { Check(shown!=null && shown.SequenceNo==n-delay,"Delay sequence"); Check(shown.Image.GetPixel(0,0).R==(n-delay)%255,"Wrong saved image"); }
                        }
                        source.Scan(true); Check(Read(source)==null,"Counted same file twice");
                    }
                }
                Console.WriteLine("PASS folder delay="+delay+": exact sequence, old files ignored, duplicate scans, date rollover");
            }
            string batch=Path.Combine(root,"batch"); Directory.CreateDirectory(batch);
            using(var source=new FolderImageSource(batch,14))
            {
                for(int n=15;n>=1;n--) Save(Path.Combine(batch,(100-n)+".png"),n);
                source.Scan(true); source.Scan(true);
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
                    source.Scan(true); source.Scan(true); Check(Read(source)==null,"Read partial image or overtook writer");
                    writer.Write(bytes,bytes.Length/2,bytes.Length-bytes.Length/2);
                }
                File.SetLastWriteTimeUtc(first,Epoch.AddSeconds(1));
                source.Scan(true); source.Scan(true);
                using(var shown=Read(source)) Check(shown!=null && shown.Image.GetPixel(0,0).R==1,"Partial upload recovery");
            }
            Console.WriteLine("PASS incomplete/locked FTP upload retried in sequence");
            string future=Path.Combine(root,"future");
            using(var source=new FolderImageSource(future,0))
            { source.Scan(true); Save(Path.Combine(future,"2026/09/27/image.png"),3); source.Scan(true); source.Scan(true); using(var shown=Read(source)) Check(shown!=null,"New root/date directory missed"); }
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
            File.WriteAllText(ini,"CAMERAS=1\n[CAMERA1]\nIP=192.0.2.8\nPORT=5566\nPASSWORD=test-only\nSOLUTION_PATH=/root/vmtempfiles/ftp/solution/test.solx\nSOLUTION_PASSWORD=file-test\n");
            var configured=IniConfig.Load(ini).GetCamera(1); var connection=new FakeConnection();
            CameraStartup.Start(configured,connection);
            Check(connection.Address=="192.0.2.8" && connection.Port==5566 && connection.Password=="test-only","Config endpoint not passed to SDK adapter");
            Check(connection.Path==configured.SolutionPath && connection.FilePassword=="file-test","Startup load settings lost");
            var disabled=new FakeConnection(); CameraStartup.Start(new CameraSettings(),disabled); Check(disabled.Address==null,"Empty IP attempted connection");
            foreach(string port in new[]{"0","65536","abc"})
            {
                File.WriteAllText(ini,"[CAMERA1]\nPORT="+port); bool rejected=false;
                try { IniConfig.Load(ini).GetCamera(1); } catch(InvalidOperationException) { rejected=true; }
                Check(rejected,"Invalid port accepted");
            }
            Console.WriteLine("PASS configurable IP/port/password, startup load order, empty IP, invalid port");
        }
        catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
    }
    static void WatcherTests(string root)
    {
        string dir=Path.Combine(root,"watcher"); Directory.CreateDirectory(dir);
        Save(Path.Combine(dir,"old.png"),0);
        using(var source=new FolderImageSource(dir,0))
        {
            source.Scan(); // Start notifications and the reconciliation interval.
            Save(Path.Combine(dir,"ESPACK_NG_001.png"),1);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            using(var result=WaitForImage(source))
            {
                Check(result.SequenceNo==1 && result.Verdict==InspectionVerdict.NG,"Watcher first image");
                Check(result.Image.GetPixel(0,0).R==1,"Saved pixels changed");
            }
            long firstMs=clock.ElapsedMilliseconds;
            Check(firstMs<1500,"Watcher waited for periodic full rescan");
            // Duplicate Changed events must not create additional products.
            for(int n=0;n<10;n++) File.SetLastWriteTimeUtc(Path.Combine(dir,"ESPACK_NG_001.png"),DateTime.UtcNow.AddSeconds(n));
            string renamed=Path.Combine(dir,"ESPACK_OK_002.png");
            string temp=Path.Combine(dir,"upload.tmp"); File.WriteAllBytes(temp,Bytes(2)); File.Move(temp,renamed);
            using(var result=WaitForImage(source)) Check(result.SequenceNo==2 && result.Number=="002","Rename/duplicate notification");
            Save(Path.Combine(dir,"2026/09/28/ESPACK_NG_003.png"),3);
            using(var result=WaitForImage(source)) Check(result.SequenceNo==3,"New date directory notification");
            // Simulate lost notifications: periodic reconciliation must still find the next product.
            var watcher=(FileSystemWatcher)typeof(FolderImageSource).GetField("_watcher",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(source);
            watcher.EnableRaisingEvents=false;
            Save(Path.Combine(dir,"ESPACK_OK_004.png"),4);
            using(var result=WaitForImage(source)) Check(result.SequenceNo==4,"Lost notification reconciliation");
            Console.WriteLine("PASS actual watcher: first image {0} ms, saved pixels, duplicate events, rename, date folder",firstMs);
            Console.WriteLine("PASS periodic reconciliation with notifications disabled");
        }
        string burst=Path.Combine(root,"watcher-burst"); Directory.CreateDirectory(burst);
        using(var source=new FolderImageSource(burst,13))
        {
            source.Scan();
            for(int n=1;n<=100;n++) Save(Path.Combine(burst,"ESPACK_"+(n%3==0?"NG":"OK")+"_"+n.ToString("D3")+".png"),n);
            var clock=System.Diagnostics.Stopwatch.StartNew(); long last=0;
            while(clock.ElapsedMilliseconds<5000 && last<87)
            {
                source.Scan(); using(var result=Read(source)) if(result!=null) last=result.SequenceNo;
                System.Threading.Thread.Sleep(25);
            }
            Check(last==87,"Watcher burst lost or duplicated an inspection / wrong delay");
            string status; long version=-1; System.Collections.Generic.List<InspectionPreview> previews;
            using(var pending=source.Take(out status,ref version,out previews)) { }
            Check(previews.Count==14 && previews[0].SequenceNo==100 && previews[13].SequenceNo==87,"Burst queue positions");
            foreach(var preview in previews) preview.Dispose();
            Console.WriteLine("PASS actual watcher: 100-file burst, all inspections counted, 13 subsequent inputs, bounded trail ({0} ms)",clock.ElapsedMilliseconds);
        }
    }
    static InspectionResult WaitForImage(FolderImageSource source)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<5000)
        {
            source.Scan(); var result=Read(source); if(result!=null) return result;
            System.Threading.Thread.Sleep(25);
        }
        throw new Exception("Watcher did not deliver image");
    }
}
