using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace SideNote;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Length==2&&e.Args[0]=="--verify-window")
        {
            var output=Path.GetFullPath(e.Args[1]);Directory.CreateDirectory(output);ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try
            {
                var window=new MainWindow(false,Path.Combine(output,"fixture"));MainWindow=window;bool started=false;
                window.Loaded+=async(_,_)=>
                {
                    if(started)return;started=true;int code=0;
                    try {await window.VerifyWindowAsync(output);File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: window visibility, inline save, drag target geometry and ordering, history task/review edit and delete");}
                    catch(Exception ex){code=1;File.WriteAllText(Path.Combine(output,"result.txt"),ex.ToString());}
                    finally {window.CloseForCheck();Shutdown(code);}
                };
                window.Show();
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(output,"result.txt"),ex.ToString());Shutdown(1);}
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--verify-data")
        {
            try { Store.Verify(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"verification.txt"), "PASS: persistence, drag ordering, cross-section moves, same-day completion retention, next-day preservation, history and backup restore"); Shutdown(0); }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"verification.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        mutex = new Mutex(true, "Local\\SideNote.Personal.v1", out bool first);
        if (!first) { System.Windows.MessageBox.Show("便签已在运行，请点击屏幕侧边标签或系统托盘图标。", "see you"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => {
            System.Windows.MessageBox.Show("操作未完成：" + args.Exception.Message + "\n请重试。数据保存在本地应用数据目录。", "see you");
            args.Handled = true;
        };
        try { new MainWindow(Array.IndexOf(e.Args,"--preview")>=0).Show(); }
        catch(Exception ex) { System.Windows.MessageBox.Show("无法打开本地数据，请保留数据目录并检查磁盘权限。\n"+ex.Message,"see you"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
