using Microsoft.UI.Xaml;

namespace Qadr.Shell;

public partial class App : Application
{
    Window? window;
    Mutex? instance;
    public App(){AppDomain.CurrentDomain.UnhandledException+=(_,e)=>Diagnostic.Write("Fatal: "+e.ExceptionObject);Diagnostic.Write("Application constructor");InitializeComponent();Diagnostic.Write("Resources initialized");UnhandledException+=(_,e)=>{
        Diagnostic.Error("Unhandled UI exception",e.Exception);
    };}
    protected override void OnLaunched(LaunchActivatedEventArgs args){Diagnostic.Write("Launched");instance=new Mutex(false,"Local\\Qadr.NativeDesktop.Instance",out bool first);if(!first){instance.Dispose();Exit();return;}window=Environment.GetCommandLineArgs().Contains("--diagnose-minimal")?new Window{Content=new Microsoft.UI.Xaml.Controls.TextBlock{Text="Qadr startup check"}}:new MainWindow();Diagnostic.Write("Window constructed");window.Closed+=(_,_)=>instance.Dispose();window.Activate();Diagnostic.Write("Window activated");}
}
