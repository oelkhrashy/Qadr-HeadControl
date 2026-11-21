using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qadr.Core;

namespace Qadr.Shell;

internal sealed partial class MainWindow
{
    readonly StartupSpeech speech=new();
    ContentDialog? cameraDialog;
    TextBlock? cameraVoiceStatus;
    string? spokenPhase;
    bool closed;
    int cameraPromptGeneration;
    double narratedAt=double.NegativeInfinity;
    void CancelCameraPrompt(){cameraPromptGeneration++;speech.Cancel();connection.Send(new("cameraVoice",Enabled:false));}
    sealed record CameraOption(CameraDevice Device,string Label);

    async Task ShowCameraPicker(CameraDevice[] devices){
        var options=devices.Select((d,i)=>new CameraOption(d,$"Camera {i+1} · {d.Name}")).ToArray();
        var choices=new ListView{ItemsSource=options,DisplayMemberPath="Label",SelectionMode=ListViewSelectionMode.Single,MaxHeight=280};
        choices.ItemContainerStyle=new Style(typeof(ListViewItem)){Setters={new Setter(FrameworkElement.MinHeightProperty,48d)}};
        var panel=new StackPanel{Spacing=10};panel.Children.Add(new TextBlock{Text="Choose manually, or say ‘camera one’, ‘camera two’, etc. After selection, resume and stop the app stay available by voice.",TextWrapping=TextWrapping.Wrap});panel.Children.Add(choices);
        cameraVoiceStatus=new TextBlock{Text="Starting camera command listening…",TextWrapping=TextWrapping.Wrap};panel.Children.Add(cameraVoiceStatus);
        var voiceOff=Button("Stop camera listening",()=>{CancelCameraPrompt();if(cameraVoiceStatus is not null)cameraVoiceStatus.Text="Camera listening stopped. Choose manually or select Listen again.";});panel.Children.Add(voiceOff);
        var retry=Button("Listen again",()=>_ = PromptCameras(devices));panel.Children.Add(retry);
        var dialog=new ContentDialog{Title="Which camera would you like to use?",Content=panel,PrimaryButtonText="Use selected camera",CloseButtonText="■ Stop · Esc",IsPrimaryButtonEnabled=false,XamlRoot=((FrameworkElement)Content).XamlRoot};cameraDialog=dialog;
        var dialogButtons=new Style(typeof(Button)){Setters={new Setter(FrameworkElement.MinHeightProperty,48d),new Setter(Control.FontSizeProperty,16d)}};
        dialog.PrimaryButtonStyle=dialogButtons;dialog.CloseButtonStyle=dialogButtons;
        dialog.CloseButtonStyle=new Style(typeof(Button)){Setters={new Setter(FrameworkElement.MinHeightProperty,48d),new Setter(Control.FontSizeProperty,16d),new Setter(Control.BackgroundProperty,stopButton.Background),new Setter(Control.ForegroundProperty,OnPrimary)}};
        choices.SelectionChanged+=(_,_)=>dialog.IsPrimaryButtonEnabled=choices.SelectedItem is CameraOption;
        dialog.PrimaryButtonClick+=(_,_)=>CancelCameraPrompt();dialog.CloseButtonClick+=(_,_)=>{CancelCameraPrompt();connection.Send(new("stop"));};
        try{
            var shown=dialog.ShowAsync();_ = PromptCameras(devices);
            var result=await shown;
            connection.Send(new("cameraVoice",Enabled:false));
            if(result==ContentDialogResult.Primary&&choices.SelectedItem is CameraOption chosen){updating=true;camera.SelectedItem=chosen.Device;updating=false;connection.Send(new("camera",CameraId:chosen.Device.Id));}
            else if(!closed&&connection.Latest?.State=="ChooseCamera")connection.Send(new("stop"));
        }catch(Exception error){Diagnostic.Write("Camera picker: "+error.Message);connection.Send(new("cameraVoice",Enabled:false));}
        finally{if(ReferenceEquals(cameraDialog,dialog)){cameraDialog=null;cameraVoiceStatus=null;}}
    }
    async Task PromptCameras(CameraDevice[] devices){
        int generation=++cameraPromptGeneration;
        connection.Send(new("cameraVoice",Enabled:false));
        string names=string.Join(". ",devices.Take(20).Select((d,i)=>$"Camera {CameraVoice.Number(i)}: {d.Name}"));
        await speech.Say($"Welcome to Qadr. Which camera would you like to use? {names}. After this prompt, say camera one, or the number of your camera. You can also choose manually.");
        if(generation==cameraPromptGeneration&&!closed&&cameraDialog is not null&&connection.Latest?.State=="ChooseCamera")connection.Send(new("cameraVoice",Enabled:true));
    }
    void Guide(EngineStatus status){
        speech.Enabled=settings.SpokenGuidance&&!Environment.GetCommandLineArgs().Contains("--test-source");
        if(status.State is "TutorialChoice" or "Tutorial")return;
        if(status.State!="ChooseCamera"&&cameraDialog is not null){CancelCameraPrompt();cameraDialog.Hide();}
        if(cameraVoiceStatus is not null)cameraVoiceStatus.Text=(speech.Error is null?"":speech.Error+"\n")+(status.Voice=="Listening for camera choice"?"Listening now. Say ‘camera one’, ‘camera two’, etc.":status.Voice=="Microphone off"?"Microphone off · manual selection remains available":status.Voice);
        if(speech.Enabled&&speech.Error is not null)shortcut.Text=status.StopShortcut+" · Spoken guidance unavailable";
        string key=currentInstruction+(status.State=="Stopped"?":"+status.Help:"");if(key==spokenPhase)return;
        if(status.State!="Stopped"&&Arguments.Now-narratedAt<4)return;
        spokenPhase=key;narratedAt=Arguments.Now;
        string? text=status.State switch{
            "Positioning"=>"Your camera is selected. Calibration is starting. Keep your whole face visible and hold a comfortable position. The oval will fit your face.",
            "Calibrating"=>status.Title+". "+status.Help,
            "LearnRest"=>"Rest naturally for four seconds. Qadr is learning your resting movement on this PC. Esc cancels.",
            "LearnClick"=>status.Title+". "+status.Help,
            "Ready"=>"Your movement is learned. Rest in your relaxed position. Mouse control will begin after you settle. Esc stops control.",
            "Tracking"=>"You are in control. Esc stops the mouse everywhere.",
            "Stopped"=>status.Title+". "+status.Help,
            _=>null};
        if(speech.Enabled&&text is not null)_ = speech.Say(currentInstruction+(status.State=="Stopped"?". "+status.Help:""));
    }
}
