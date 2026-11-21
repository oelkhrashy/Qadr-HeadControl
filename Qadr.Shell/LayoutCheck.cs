using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Qadr.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Qadr.Shell;

internal sealed partial class MainWindow
{
    // Available only with all four preview/diagnostic flags. The engine never
    // starts, so this exports our own sample XAML without accessing a camera.
    async Task CheckLayout(){
        string directory=Arguments.DataDirectory(Environment.GetCommandLineArgs());
        System.IO.Directory.CreateDirectory(directory);
        try{
            title.Text="Hold comfortably while the oval fits your face and distance";
            help.Text="Qadr learns your comfortable position automatically. Esc stops control everywhere.";
            previewPlaceholder.Text="Camera preview · layout sample";
            face.Text="Face guide adapts to your distance";
            distance.Text="Your whole face stays inside the view";
            shortcut.Text="Esc stops control everywhere · Preview only";
            state.Text="Calibration";palette.Update("left",false);
            faceGuide.Update(new FacePosition("adapting","",false,.58,.46,.25,.45),"position");coach.Update("position");
            var report=new List<object>();
            var sample=new EngineStatus("Positioning","","",0,0,0,true,"",0,0,"Esc or Ctrl+Alt+Esc",[],null,[],"Microphone off",Position:new("adapting","",false,.58,.46,.25,.45),DetectedFace:new("face","",true,.58,.46,.22,.4));
            SetPresentation(sample);faceGuide.Update(sample.Position!,"position",sample.DetectedFace);
            foreach(var size in new[]{(1920,1080,"setup-normal"),(1093,614,"setup-125-logical"),(911,512,"setup-150-logical")}){
                ResizeLogical(size.Item1,size.Item2);await Task.Delay(600);
                report.Add(await ExportLayout(this,VisualButtons(control).Append(stopButton),directory,size.Item3));
            }
            SetPresentation(sample with{State="Tracking"});
            foreach(var size in new[]{(1280,860,"normal"),(1093,614,"compact-125-logical"),(911,512,"compact-150-logical")}){
                ResizeLogical(size.Item1,size.Item2);
                await Task.Delay(600);
                report.Add(await ExportLayout(this,VisualButtons(control).Append(stopButton),directory,size.Item3));
            }
            foreach(var scenario in new[]{("mode-position",sample with{State="Tracking",CurrentSettings=new Settings{Mapping="position",ClickMethod="smile"}}),("mode-drag",sample with{State="Tracking",Dragging=true}),("mode-scroll",sample with{State="Tracking",HeadScrolling=true}),("mode-lost",sample with{State="Lost",Help="Bring your face back into view."}),("mode-paused",sample with{State="Stopped",Help="Say resume when ready."})}){
                ResizeLogical(911,512);SetPresentation(scenario.Item2);await Task.Delay(350);
                report.Add(await ExportLayout(this,VisualButtons(control).Append(stopButton),directory,scenario.Item1));
            }
            SetPresentation(sample with{State="Tracking"});
            foreach(var size in new[]{(1280,860,"adjustment-normal"),(911,512,"adjustment-compact")}){
                ResizeLogical(size.Item1,size.Item2);await Task.Delay(350);
                UpdateAdjustmentPanel(sample with{State="Tracking",Adjustment=new("Slower movement applied. Try pointing, or choose Undo.",true)});
                adjustmentButton.Flyout.ShowAt(adjustmentButton);await Task.Delay(400);
                if(adjustmentButton.Flyout is Microsoft.UI.Xaml.Controls.Flyout adjustment&&adjustment.Content is FrameworkElement content)report.Add(await ExportElementLayout(content,VisualButtons(content),directory,size.Item3));
                adjustmentButton.Flyout.Hide();await Task.Delay(150);
            }
            var preferencesItem=nav.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().First(x=>x.Content?.ToString()=="Preferences");
            nav.SelectedItem=preferencesItem;
            foreach(var size in new[]{(1280,860,"speed-normal"),(1093,614,"speed-125-logical"),(911,512,"speed-150-logical")}){
                ResizeLogical(size.Item1,size.Item2);await Task.Delay(600);
                report.Add(await ExportLayout(this,VisualButtons(preferences).Where(b=>b.Content?.ToString() is "Slower −" or "Faster +").Append(stopButton),directory,size.Item3));
            }
            nav.SelectedItem=nav.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().First(x=>x.Content?.ToString()=="Control");
            ToggleDock();dock!.Update(sample with{State="Tracking"});await Task.Delay(600);
            report.Add(await ExportLayout(dock!,new[]{dock!.Handle,dock.Halt},directory,"dock-collapsed"));
            dock.TogglePanel();await Task.Delay(600);
            report.Add(await ExportLayout(dock!,dock.Palette.Targets.Where(b=>MousePalette.Zone(dock,b,"test") is not null).Append(dock.Halt).Append(dock.Handle),directory,"dock-expanded"));
            speech.Enabled=false;
            ResizeLogical(1024,760);await Task.Delay(400);
            UpdateTutorial(sample with{State="TutorialChoice",Cue="tutorial:0"});await Task.Delay(600);
            if(tutorialDialog is {} tourChoice)report.Add(await ExportElementLayout(tourChoice,VisualButtons(tourChoice),directory,"tutorial-choice"));
            for(int i=0;i<TutorialFlow.Pages.Length;i++){
                UpdateTutorial(sample with{State="Tutorial",Cue="tutorial:"+i});await Task.Delay(200);
                if(tutorialDialog is {} tourPage)report.Add(await ExportElementLayout(tourPage,VisualButtons(tourPage),directory,"tutorial-step-"+(i+1)));
            }
            UpdateTutorial(sample with{State="Stopped"});await Task.Delay(600);
            speech.Enabled=false;
            ResizeLogical(1024,700);await Task.Delay(300);
            var picker=ShowCameraPicker([new("sample-camera-1","Built-in camera"),new("sample-camera-2","USB camera")]);await Task.Delay(500);
            if(cameraDialog is {} dialog){report.Add(await ExportElementLayout(dialog,VisualButtons(dialog),directory,"camera-picker"));dialog.Hide();}await picker;
            nav.SelectedItem=assessmentNavigation;pageHost.Content=assessmentPage;SetAssessmentFocus(true);
            foreach(var size in new[]{(1280,860,"assessment-normal"),(1093,614,"assessment-125-logical"),(911,512,"assessment-150-logical")}){
                assessmentRunning=false;ResizeLogical(size.Item1,size.Item2);await Task.Delay(600);
                assessmentRunning=true;ApplyAssessmentLayout();assessmentPage.UpdateLayout();
                assessmentTargetButton.Content="E";assessmentTargetButton.Visibility=Visibility.Visible;
                Canvas.SetLeft(assessmentTargetButton,Math.Max(8,(assessmentCanvas.ActualWidth-88)/2));Canvas.SetTop(assessmentTargetButton,Math.Max(8,(assessmentCanvas.ActualHeight-88)/2));
                assessmentInstructions.Text="5/10 · Target E · Double click here — raise your eyebrows";assessmentResults.Text="D: passed · overshoots 1 · centre error 8 px · completed 4/10";
                await Task.Delay(200);report.Add(await ExportLayout(this,VisualButtons(assessmentPage).Append(stopButton),directory,size.Item3));
            }
            quickAssessment=true;assessmentPageTitle.Text="Your startup movement check";assessmentStartButton.Content="Quick check starts automatically";assessmentStartButton.IsEnabled=false;assessmentEndButton.Content="Stop + save";assessmentStartButton.Visibility=Visibility.Collapsed;SetAssessmentFocus(true);
            foreach(var size in new[]{(1280,860,"launch-normal"),(1093,614,"launch-125-logical"),(911,512,"launch-150-logical"),(731,491,"launch-175-logical"),(640,430,"launch-200-logical")}){
                assessmentRunning=false;ResizeLogical(size.Item1,size.Item2);await Task.Delay(600);
                assessmentRunning=true;ApplyAssessmentLayout();assessmentPage.UpdateLayout();
                assessmentTargetButton.Content="E";assessmentTargetButton.Visibility=Visibility.Visible;
                RenderQuickLayoutTarget(4);
                assessmentInstructions.Text="5/5 - Target E - Move here and hold inside (6s left)\nSay skip target to skip. Esc or stop the app stops and saves.";
                assessmentResults.Text="4 targets attempted - 3 reached, 1 timed out. Click gestures are paused.";
                await Task.Delay(200);report.Add(await ExportLayout(this,VisualButtons(assessmentPage).Where(b=>b.IsEnabled&&b.Visibility==Visibility.Visible),directory,size.Item3));
            }
            foreach(var size in new[]{(1280,860,"tuning-normal"),(1093,614,"tuning-125-logical"),(911,512,"tuning-150-logical")}){
                assessmentRunning=false;ResizeLogical(size.Item1,size.Item2);await Task.Delay(600);
                assessmentRunning=true;quickAssessment=true;ApplyAssessmentLayout();assessmentPage.UpdateLayout();
                RenderQuickLayoutTarget(4);assessmentTargetButton.Content="J";assessmentInstructions.Text="Check adjustment - 5/5 - Target J - Move here and hold inside (6s left)\nSay skip target to skip. Esc or stop the app stops and saves.";
                assessmentResults.Text="Trying slightly easier travel while keeping slow corrections unchanged. Repeat the same five targets.";
                await Task.Delay(200);report.Add(await ExportLayout(this,VisualButtons(assessmentPage).Where(b=>b.IsEnabled&&b.Visibility==Visibility.Visible),directory,size.Item3));
            }
            assessmentRunning=quickAssessment=false;SetAssessmentFocus(false);assessmentStartButton.Visibility=Visibility.Visible;assessmentStartButton.Content="Start ten points";assessmentStartButton.IsEnabled=true;undoMovementButton.Visibility=Visibility.Visible;
            foreach(var size in new[]{(1280,860,"tuned-normal"),(1093,614,"tuned-125-logical"),(911,512,"tuned-150-logical")}){
                ResizeLogical(size.Item1,size.Item2);await Task.Delay(600);ApplyAssessmentLayout();
                assessmentTargetButton.Visibility=Visibility.Collapsed;assessmentInstructions.Text="Your movement settings were adjusted automatically.";
                assessmentResults.Text="The repeat check improved the measured result. Sensitivity 0.45; reach 3.00; precision 0.75.";
                await Task.Delay(200);report.Add(await ExportLayout(this,VisualButtons(assessmentPage).Where(b=>b.IsEnabled).Append(stopButton),directory,size.Item3));
            }
            assessmentRunning=quickAssessment=false;SetAssessmentFocus(false);
            var savedShown=shown;shown=sample with{State="Tracking",CurrentSettings=new Settings{EyebrowLearned=true,EyebrowDoubleClick=true}};
            for(int attempt=0;attempt<20;attempt++){
                StartAssessment();StartAssessment();ShowAssessmentPage();SetAssessmentFocus(true);
                if(!ReferenceEquals(assessmentFocusHost.Child,assessmentPage)||pageHost.Content is not null)throw new InvalidOperationException("Assessment has multiple owners.");
                EndAssessmentUi(false);
                nav.SelectedItem=nav.MenuItems.OfType<NavigationViewItem>().First(x=>x.Content?.ToString()=="Control");nav.SelectedItem=assessmentNavigation;
            }
            shown=sample with{State="Degraded"};assessmentRunning=true;quickAssessment=false;assessmentStep=0;
            SetAssessmentFocus(true);PlaceAssessmentTarget();
            if(!pendingAssessmentTarget)throw new InvalidOperationException("A paused target was discarded.");
            shown=sample with{State="Tracking"};
            for(int retry=0;retry<20&&pendingAssessmentTarget;retry++){await Task.Delay(50);TrySendAssessmentTarget();}
            await Task.Delay(50);
            if(pendingAssessmentTarget)throw new InvalidOperationException($"A pending target was not sent after recovery: running={assessmentRunning}, queued={assessmentTargetQueued}, loaded={assessmentTargetButton.IsLoaded}, visibility={assessmentTargetButton.Visibility}, enabled={assessmentTargetButton.IsEnabled}, width={assessmentTargetButton.ActualWidth}, height={assessmentTargetButton.ActualHeight}.");
            EndAssessmentUi(false);shown=savedShown;
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"assessment-lifecycle.json"),"{\"cycles\":20,\"repeatedStarts\":40,\"passed\":true,\"deferredTargetRecovery\":true}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"layout.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Diagnostic.Write("Layout check completed");
            if(Environment.GetCommandLineArgs().Contains("--speech-check"))await ExportSyntheticSpeech(directory);
        }catch(Exception error){Diagnostic.Error("Layout check failed",error);Diagnostic.Write("Layout check failed: "+error);Environment.ExitCode=1;}
        finally{Close();}
    }
    void RenderQuickLayoutTarget(int index){
        var plan=QuickTargetLayout.Create(assessmentCanvas.ActualWidth,assessmentCanvas.ActualHeight)??throw new InvalidOperationException("Test surface cannot fit five separated targets.");
        assessmentTargetButton.Width=assessmentTargetButton.Height=plan.Size;var target=plan.Targets[index];Canvas.SetLeft(assessmentTargetButton,target.X);Canvas.SetTop(assessmentTargetButton,target.Y);
    }
    void ResizeLogical(int width,int height){double scale=((FrameworkElement)Content).XamlRoot?.RasterizationScale??1;AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(width*scale),(int)Math.Round(height*scale)));}
    static async Task ExportSyntheticSpeech(string directory){
        try{
        using var synthesizer=new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        var english=Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v=>v.Language.StartsWith("en-",StringComparison.OrdinalIgnoreCase));if(english is not null)synthesizer.Voice=english;
        foreach(string text in new[]{"camera one","choose camera two","computer stop","resume","stop the app"}){
            using var stream=await synthesizer.SynthesizeTextToStreamAsync(text);using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);var bytes=new byte[stream.Size];reader.ReadBytes(bytes);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,text.Replace(' ','-')+".wav"),bytes);
        }
        Diagnostic.Write("Synthetic speech exported using "+synthesizer.Voice.DisplayName);
        }catch(Exception error){
            Diagnostic.Write("Synthetic WinRT TTS unavailable: "+error.Message);
            foreach(string text in new[]{"camera one","choose camera two","computer stop","resume","stop the app"})await DesktopSpeech.Speak(text,CancellationToken.None,System.IO.Path.Combine(directory,text.Replace(' ','-')+".wav"));
            Diagnostic.Write("Synthetic speech exported using desktop SAPI");
        }
    }
    async Task<object> ExportLayout(Window window,IEnumerable<Button> buttons,string directory,string name){
        return await ExportElementLayout((FrameworkElement)window.Content,buttons,directory,name);
    }
    static IEnumerable<Button> VisualButtons(DependencyObject root){
        if(root is Button button)yield return button;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in VisualButtons(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    async Task<object> ExportElementLayout(FrameworkElement root,IEnumerable<Button> buttons,string directory,string name){
        root.UpdateLayout();
        var checks=buttons.Where(button=>button.ActualWidth>0&&button.ActualHeight>0&&LayoutVisible(button)).Select(button=>{
            var origin=button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            string label=Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button);if(string.IsNullOrEmpty(label))label=button.Content is TextBlock text?text.Text:button.Content?.ToString()??"";
            return new{Label=label,X=origin.X,Y=origin.Y,Width=button.ActualWidth,Height=button.ActualHeight,
                Fits=origin.X>=0&&origin.Y>=0&&origin.X+button.ActualWidth<=root.ActualWidth+.5&&origin.Y+button.ActualHeight<=root.ActualHeight+.5&&button.ActualHeight>=44&&button.ActualWidth>=44};
        }).ToArray();
        await SaveRender(root,System.IO.Path.Combine(directory,name+".png"));
        var quick=assessmentFocusHost.Visibility==Visibility.Visible?QuickTargetLayout.Create(assessmentCanvas.ActualWidth,assessmentCanvas.ActualHeight):null;
        return new{QuickTargetsSeparated=assessmentFocusHost.Visibility!=Visibility.Visible||quick is not null&&QuickTargetLayout.Comparable(quick.Targets),QuickTargets=quick?.Targets,CanvasWidth=assessmentCanvas.ActualWidth,CanvasHeight=assessmentCanvas.ActualHeight,Name=name,Width=root.ActualWidth,Height=root.ActualHeight,Scale=root.XamlRoot.RasterizationScale,AllTargetsFit=checks.Length>0&&checks.All(c=>c.Fits),Targets=checks};
    }
    static bool LayoutVisible(DependencyObject element){
        for(DependencyObject? at=element;at is not null;at=VisualTreeHelper.GetParent(at))if(at is UIElement visual&&visual.Visibility!=Visibility.Visible)return false;
        return true;
    }
    static async Task SaveRender(FrameworkElement root,string path){
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(root);
        using var pixels=DataReader.FromBuffer(await bitmap.GetPixelsAsync());var bytes=new byte[pixels.UnconsumedBufferLength];pixels.ReadBytes(bytes);
        using var stream=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,bytes);await encoder.FlushAsync();
        stream.Seek(0);using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);var png=new byte[stream.Size];reader.ReadBytes(png);
        System.IO.File.WriteAllBytes(path,png);
    }
}

