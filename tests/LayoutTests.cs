using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
class LayoutTests
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from,IntPtr to,ref Rect r,uint points);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static void ClearShown(Form form)
    {
        FieldInfo key=null;
        foreach(var f in typeof(Form).GetFields(BindingFlags.Static|BindingFlags.NonPublic)) if(f.Name.ToUpperInvariant().Contains("SHOWN")) key=f;
        Check(key!=null,"Cannot isolate connection event");
        var events=(System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events",Private).GetValue(form,null);
        object token=key.GetValue(null); events.RemoveHandler(token,events[token]);
    }
    static void Pump(int ms) { var until=DateTime.UtcNow.AddMilliseconds(ms); do { Application.DoEvents(); System.Threading.Thread.Sleep(10); } while(DateTime.UtcNow<until); }
    static void Fit(Form host) {host.GetType().GetMethod("FitViewers",Private).Invoke(host,null); Pump(100);}
    [STAThread] static void Main(string[] args)
    {
        try { Run(args); } catch(Exception ex) { while(ex!=null) {Console.WriteLine(ex.GetType().Name+": "+ex.Message); ex=ex.InnerException;} Environment.ExitCode=1; }
    }
    static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        var a=Assembly.LoadFrom(args[0]); var configType=a.GetType("SC6000DelayedMonitor.IniConfig");
        var viewerType=a.GetType("SC6000DelayedMonitor.ViewerForm"); var hostType=a.GetType("SC6000DelayedMonitor.MonitorForm");
        string work=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if(args.Length>1 && args[1]=="child")
        {
            object camera=Activator.CreateInstance(a.GetType("SC6000DelayedMonitor.CameraSettings"));
            camera.GetType().GetProperty("Ip").SetValue(camera,"127.0.0.1",null);
            camera.GetType().GetProperty("Title").SetValue(camera,"SDK CONTROL LAYOUT TEST",null);
            using(var child=(Form)Activator.CreateInstance(viewerType,camera,new IntPtr(long.Parse(args[2]))))
            {
                ClearShown(child);
                // Create the real VM frontend (including its WPF/native children), but never connect to a device.
                var core=Assembly.Load("VM.Core, Version=1.0.0.0, Culture=neutral, PublicKeyToken=61600122bc9264b9");
                var mode=core.GetType("VM.Core.VmSolution").GetMethod("SetControlMode");
                mode.Invoke(null,new[]{Enum.Parse(mode.GetParameters()[0].ParameterType,"REMOTE")});
                var field=viewerType.GetField("_frontend",Private);
                var sdk=(Control)Activator.CreateInstance(field.FieldType);
                field.SetValue(child,sdk); sdk.Dock=DockStyle.Fill; var content=(Panel)viewerType.GetField("_live",Private).GetValue(child); content.Controls.Add(sdk); sdk.BringToFront();
                child.Shown+=delegate { File.WriteAllText(args[3]+".tmp",child.Handle.ToInt64().ToString()+","+sdk.Handle.ToInt64()+","+content.Handle.ToInt64()); File.Move(args[3]+".tmp",args[3]); };
                child.FormClosing+=delegate
                {
                    using(var bitmap=new Bitmap(child.Width,child.Height))
                    { child.DrawToBitmap(bitmap,new Rectangle(Point.Empty,child.Size)); bitmap.Save(args[3]+".png"); }
                };
                Application.Run(child);
                File.WriteAllText(args[3]+".closed","graceful");
            }
            return;
        }
        foreach(int count in new[]{1,2,3,4})
        {
            string ini=Path.Combine(work,"layout.ini");
            string contents="CAMERAS="+count+"\nMONITOR=0\nINSPECTION_TEXT=링 유무검사\n";
            for(int i=1;i<=count;i++) contents+="[CAMERA"+i+"]\nIP=127.0.0."+i+"\n";
            File.WriteAllText(ini,contents);
            object config=configType.GetMethod("Load").Invoke(null,new object[]{ini});
            Check((string)configType.GetProperty("InspectionText").GetValue(config,null)=="링 유무검사","Korean inspection text parsing");
            using(var bitmap=new Bitmap(320,80))
            {
                using(var g=Graphics.FromImage(bitmap)) {g.Clear(Color.White); using(var font=new Font("Segoe UI",44,FontStyle.Bold)) g.DrawString("ASPEC",font,Brushes.DarkBlue,0,0);}
                bitmap.Save(Path.Combine(work,"logo.png"));
            }
            using(var host=(Form)Activator.CreateInstance(hostType,config,args[0],work))
            {
                host.WindowState=FormWindowState.Normal; host.Size=new Size(1280,720);
                ClearShown(host);host.Show();Pump(50);
                File.Delete(Path.Combine(work,"logo.png"));
                var layout=(TableLayoutPanel)host.Controls[0]; var grid=(TableLayoutPanel)layout.GetControlFromPosition(0,1);
                int columns=count==1?1:2, rows=count<=2?1:2;
                Check(grid.ColumnCount==columns && grid.RowCount==rows,"Grid policy "+count);
                Check(grid.Controls.Count==count,"Unexpected occupied cell");
                var processes=(List<Process>)hostType.GetField("_viewers",Private).GetValue(host);
                var handles=new List<IntPtr>(); var sdkHandles=new List<IntPtr>(); var contentHandles=new List<IntPtr>(); var readyFiles=new List<string>();
                try
                {
                    for(int i=0;i<count;i++)
                    {
                        var panel=(Panel)grid.GetControlFromPosition(i%columns,i/columns);
                        string ready=Path.Combine(work,"sdk-"+count+"-"+i+".txt");readyFiles.Add(ready);
                        if(File.Exists(ready))File.Delete(ready);if(File.Exists(ready+".closed"))File.Delete(ready+".closed");
                        processes.Add(Process.Start(new ProcessStartInfo {FileName=Assembly.GetExecutingAssembly().Location, UseShellExecute=false,CreateNoWindow=true,
                            Arguments="\""+args[0]+"\" child "+panel.Handle.ToInt64()+" \""+ready+"\""}));
                    }
                    foreach(var ready in readyFiles)
                    {
                        DateTime deadline=DateTime.UtcNow.AddSeconds(20);
                        while(!File.Exists(ready)&&DateTime.UtcNow<deadline)Pump(50);
                        Check(File.Exists(ready),"SDK child startup timed out: "+ready);
                        string[] parts=File.ReadAllText(ready).Split(','); handles.Add(new IntPtr(long.Parse(parts[0]))); sdkHandles.Add(new IntPtr(long.Parse(parts[1]))); contentHandles.Add(new IntPtr(long.Parse(parts[2])));
                    }
                    foreach(var size in new[]{new Size(800,600),new Size(1280,720),new Size(1920,1080),new Size(2560,1440),new Size(3840,2160)})
                    {
                        SetWindowPos(host.Handle,IntPtr.Zero,0,0,size.Width,size.Height,0x14);Pump(60);Fit(host);
                        int[] widths=grid.GetColumnWidths(), heights=grid.GetRowHeights();
                        Check(widths.Length==columns && Math.Abs(widths[0]-widths[columns-1])<=1,"Unequal column widths");
                        Check(heights.Length==rows && Math.Abs(heights[0]-heights[rows-1])<=1,"Unequal row heights");
                        for(int i=0;i<count;i++)
                        {
                            var panel=(Panel)grid.GetControlFromPosition(i%columns,i/columns);
                            Check(GetParent(handles[i])==panel.Handle,"Wrong native parent");
                            Rect actual,area;GetWindowRect(handles[i],out actual);MapWindowPoints(IntPtr.Zero,panel.Handle,ref actual,2);GetClientRect(panel.Handle,out area);
                            Check(actual.Left==0&&actual.Top==0&&actual.Right==area.Right&&actual.Bottom==area.Bottom,
                                "Native bounds mismatch "+count+" cameras, "+size+", camera "+i+": "+actual.Right+"x"+actual.Bottom+" vs "+area.Right+"x"+area.Bottom);
                            Rect sdkRect; GetWindowRect(sdkHandles[i],out sdkRect); MapWindowPoints(IntPtr.Zero,contentHandles[i],ref sdkRect,2); GetClientRect(contentHandles[i],out area);
                            Check(sdkRect.Left==0 && sdkRect.Top==24 && sdkRect.Right==area.Right && sdkRect.Bottom==area.Bottom,"SDK bounds " + sdkRect.Left + "," + sdkRect.Top + " " + sdkRect.Right + "x" + sdkRect.Bottom + " vs " + area.Right + "x" + area.Bottom);
                        }
                        Console.WriteLine("PASS "+count+" cameras "+size+": native SDK child windows match equal grid cells");
                    }
                    // Simulate SDK/WinForms overwriting bounds; the host must restore them without a child timer.
                    SetWindowPos(handles[0],IntPtr.Zero,120,90,2300,1500,0x14);Pump(50);Fit(host);
                    Rect corrected;GetWindowRect(handles[0],out corrected);MapWindowPoints(IntPtr.Zero,grid.GetControlFromPosition(0,0).Handle,ref corrected,2);
                    Check(corrected.Left==0&&corrected.Top==0,"Bounds drift not repaired");
                    SetWindowPos(host.Handle,IntPtr.Zero,0,0,1280,720,0x14);Pump(50);Fit(host);
                    using(var bitmap=new Bitmap(host.Width,host.Height)) {host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(work,"responsive-"+count+".png"));}
                    host.Close();
                    foreach(var ready in readyFiles)Check(File.Exists(ready+".closed"),"SDK child did not exit gracefully");
                    Console.WriteLine("PASS "+count+" cameras: bounds drift correction, header, logo unlock, graceful process exit");
                }
                finally {if(!host.IsDisposed)host.Close();}
            }
        }
    }
}
