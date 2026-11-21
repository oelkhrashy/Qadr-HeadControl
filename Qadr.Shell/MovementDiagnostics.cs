using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qadr.Core;

namespace Qadr.Shell;
internal sealed partial class MainWindow
{
    readonly TextBlock movementReportStatus=Text("Movement measurements are off",14,Muted);
    FrameworkElement MovementDiagnostics(){
        var panel=Stack(10);panel.Children.Add(Text("Frame rate and movement report",22));
        var fps=new ComboBox{Header="Camera and tracking target",ItemsSource=new[]{30,60},SelectedItem=settings.TrackingFps,MinHeight=48};
        fps.SelectionChanged+=(_,_)=>{if(!updating&&fps.SelectedItem is int value){settings=settings with{TrackingFps=value};SaveSettings();}};
        settingControls.Add(s=>fps.SelectedItem=s.TrackingFps);panel.Children.Add(fps);
        panel.Children.Add(Text("60 FPS can reduce sampling delay if your camera and PC support it. Actual rates may be lower. Camera format changes apply when the camera next opens; inference and preview limits change immediately. 30 FPS uses fewer resources. Tracking and preview always use the newest frame.",14,Muted));
        var automatic=new ToggleSwitch{Header="Record movement measurements on startup",IsOn=settings.MotionDiagnosticsOnStartup};
        automatic.Toggled+=(_,_)=>{if(!updating){settings=settings with{MotionDiagnosticsOnStartup=automatic.IsOn};SaveSettings();}};
        settingControls.Add(s=>automatic.IsOn=s.MotionDiagnosticsOnStartup);panel.Children.Add(automatic);
        panel.Children.Add(Text("Numeric face pose/expression scores, cursor coordinates and timing only. No images, sound or window contents. Off by default for customers. Each session is limited to 15 minutes or 32 MB; saving creates one shareable ZIP. Save before closing Qadr. Recording does not train a model automatically.",14,Muted));
        panel.Children.Add(Button("Start movement recording",()=>connection.Send(new("motionRecord",Enabled:true)),true));
        panel.Children.Add(Button("Save movement report",()=>connection.Send(new("motionRecord",Enabled:false))));
        var labels=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};labels.Children.Add(Button("Mark rest",()=>connection.Send(new("motionLabel",Name:"rest"))));labels.Children.Add(Button("Mark movement",()=>connection.Send(new("motionLabel",Name:"movement"))));panel.Children.Add(labels);
        panel.Children.Add(Text("Or say start motion test, mark rest, mark movement, and save motion test. For a rest measurement, say mark rest and settle for 5 seconds; then say mark movement and move normally. These commands are separate from routine recording.",14,Muted));
        panel.Children.Add(movementReportStatus);
        panel.Children.Add(Button("Open movement reports folder",()=>{
            string folder=System.IO.Path.Combine(Arguments.DataDirectory(Environment.GetCommandLineArgs()),"movement-reports");
            System.IO.Directory.CreateDirectory(folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});
        }));
        panel.Children.Add(Button("Open ten-point movement test",()=>{nav.SelectedItem=assessmentNavigation;pageHost.Content=assessmentPage;}));return Card(panel);
    }
    void UpdateMovementRecording(EngineStatus s){
        var record=s.MovementRecording;
        movementReportStatus.Text=$"Requested: {s.CurrentSettings?.TrackingFps??30} FPS · Camera format: {s.CameraNominalFps:F0} FPS · Camera delivered: {s.CaptureFps:F0} FPS · Tracking: {s.Fps:F0} FPS\n"+
            (record?.Error??(record?.Recording==true?"Recording measurements — say save motion test when finished":record?.Saving==true?"Saving ZIP report…":record?.Report is {} path?"Saved: "+path:"No active recording"));
    }
}
