using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Qadr.Core;

namespace Qadr.Shell;
internal sealed partial class MainWindow
{
    readonly Grid assessmentPage=new(){Margin=new Thickness(20),RowSpacing=8};
    NavigationViewItem assessmentNavigation=null!;
    readonly Canvas assessmentCanvas=new(){Background=Pale};
    readonly TextBlock assessmentPageTitle=Text("Your ten-point movement test",25),assessmentDetails=Text("Use face control only. Move: hold inside 0.6 s. Click: use your usual gesture. Double click: learned eyebrow raise. Esc stops and saves. Say start pointing test, skip target, or save pointing test.",13,Muted);
    StackPanel assessmentOptions=null!;
    readonly TextBlock assessmentInstructions=Text("Start when you are comfortably controlling the pointer.",18),assessmentResults=Text("Ten targets, A to J. No setting changes or automatic training.",14,Muted);
    readonly ProgressBar assessmentHold=new(){Minimum=0,Maximum=100,Height=6,Foreground=Emerald};
    readonly Button assessmentTargetButton=new(){Width=88,Height=88,MinWidth=48,MinHeight=48,FontSize=28,CornerRadius=new(50),Background=Emerald,Foreground=OnPrimary,Visibility=Visibility.Collapsed,IsDoubleTapEnabled=true,IsRightTapEnabled=true};
    ComboBox assessmentTasks=null!,assessmentSize=null!;
    int assessmentStep=-1,assessmentCompleted,assessmentSkipped;
    long seenAssessmentRequest;
    string? assessmentAction;
    bool assessmentRunning,quickAssessment,launchFinishSent,pendingAssessmentTarget,assessmentTargetQueued;
    double lastLaunchBegin=double.NegativeInfinity;
    Button assessmentStartButton=null!,assessmentEndButton=null!,undoMovementButton=null!;
    int assessmentRound=1;
    string AssessmentId=>((char)((quickAssessment&&assessmentRound==2?'F':'A')+assessmentStep)).ToString();
    MovementTuningStatus? shownMovementTuning;
    int AssessmentTotal=>quickAssessment?5:10;
    readonly List<(UIElement Element,Visibility Was)> hiddenAssessmentChrome=[];
    void ShowAssessmentPage(){
        if(ReferenceEquals(assessmentFocusHost.Child,assessmentPage)||ReferenceEquals(pageHost.Content,assessmentPage))return;
        pageHost.Content=null;pageHost.Content=assessmentPage;
    }
    void SetAssessmentFocus(bool enabled){
        if(enabled){
            if(ReferenceEquals(assessmentFocusHost.Child,assessmentPage))return;
            if(ReferenceEquals(pageHost.Content,assessmentPage))pageHost.Content=null;
            if(assessmentFocusHost.Visibility!=Visibility.Visible)foreach(var child in windowRoot.Children.Where(c=>!ReferenceEquals(c,assessmentFocusHost))){hiddenAssessmentChrome.Add((child,child.Visibility));child.Visibility=Visibility.Collapsed;}
            assessmentFocusHost.Child=assessmentPage;assessmentFocusHost.Visibility=Visibility.Visible;assessmentPage.Margin=new Thickness(12);
        }
        else if(ReferenceEquals(assessmentFocusHost.Child,assessmentPage)){
            assessmentFocusHost.Child=null;assessmentFocusHost.Visibility=Visibility.Collapsed;pageHost.Content=null;pageHost.Content=assessmentPage;assessmentPage.Margin=new Thickness(20);
            foreach(var (element,was) in hiddenAssessmentChrome)element.Visibility=was;hiddenAssessmentChrome.Clear();
        }
        assessmentPage.UpdateLayout();
    }
    static readonly (double X,double Y)[] QuickPoints=[(.5,.5),(.12,.5),(.88,.5),(.5,.12),(.5,.88)];
    void UpdateLaunchAssessment(EngineStatus s){
        bool required=s.LaunchCheck?.Required==true;
        undoMovementButton.Visibility=!required&&s.MovementTuning?.UndoAvailable==true?Visibility.Visible:Visibility.Collapsed;
        if(s.MovementTuning is {} tuning&&tuning!=shownMovementTuning){
            shownMovementTuning=tuning;
            assessmentResults.Text=tuning.Message+(tuning.Applied is {} value?$" Sensitivity {value.Sensitivity:F2}; reach {value.TravelBoost:F2}; precision {value.PrecisionNoise:F2}.":"");
        }
        foreach(var item in nav.MenuItems.OfType<NavigationViewItem>())item.IsEnabled=!required||s.State!="Tracking"||ReferenceEquals(item,assessmentNavigation);
        assessmentStartButton.Content=required?(s.State=="Stopped"?"Resume quick check":"Quick check starts automatically"):"Start ten points";
        assessmentStartButton.IsEnabled=!required||s.State=="Stopped";assessmentStartButton.Visibility=required&&s.State!="Stopped"?Visibility.Collapsed:Visibility.Visible;
        assessmentEndButton.Content=required?"Stop + save":"End test + save";
        if(required){assessmentPageTitle.Text="Your startup movement check";assessmentDetails.Text="Five movement targets measure your control. If a small adjustment is supported, the same five positions check it automatically. Otherwise your settings stay. Say skip target; Esc stops. Click gestures pause. Numeric results stay on this PC; no video or audio.";}
        else{assessmentPageTitle.Text="Your ten-point movement test";assessmentDetails.Text="Use face control only. Move: hold inside 0.6 s. Click: use your usual gesture. Esc stops and saves.";}
        if(s.LaunchCheck?.Phase=="ready"&&s.State=="Tracking"&&!assessmentRunning)StartLaunchAssessment();
        if(quickAssessment&&s.LaunchCheck is {Phase:"running" or "validating"} check&&(!assessmentRunning&&(!launchFinishSent||check.Round!=assessmentRound)||check.Round!=assessmentRound)){
            assessmentRound=check.Round;launchFinishSent=false;
            assessmentRunning=true;assessmentStep=assessmentCompleted=assessmentSkipped=0;ApplyAssessmentLayout();PlaceAssessmentTarget();
        }
        if(s.LaunchCheck?.Phase=="interrupted"&&quickAssessment){EndAssessmentUi(false);assessmentInstructions.Text="Quick check stopped and saved. Say resume to try again.";}
        if(!required&&quickAssessment){assessmentInstructions.Text=s.MovementTuning?.Message??"Movement check finished. Your existing controls are restored.";quickAssessment=false;SetAssessmentFocus(false);assessmentTasks.IsEnabled=assessmentSize.IsEnabled=true;ApplyAssessmentLayout();}
    }
    void StartLaunchAssessment(){
        if(shown?.State!="Tracking"||shown.LaunchCheck?.Phase!="ready"||shown.MovementRecording?.Saving==true||Arguments.Now-lastLaunchBegin<1)return;
        lastLaunchBegin=Arguments.Now;quickAssessment=true;launchFinishSent=false;assessmentRound=1;
        Activate();nav.SelectedItem=assessmentNavigation;
        ShowAssessmentPage();
        assessmentTasks.IsEnabled=assessmentSize.IsEnabled=false;assessmentOptions.Visibility=Visibility.Collapsed;
        assessmentInstructions.Text="Measuring your movement. Small adjustments are checked automatically. Click gestures pause.";
        assessmentResults.Text="A numeric report is saved locally. No video or audio is saved.";
        SetAssessmentFocus(true);ApplyAssessmentLayout();assessmentPage.UpdateLayout();
        if(QuickTargetLayout.Create(assessmentCanvas.ActualWidth,assessmentCanvas.ActualHeight) is null){assessmentInstructions.Text="Making room for five separate targets...";if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.Maximize();return;}
        DispatcherQueue.TryEnqueue(()=>{if(shown?.LaunchCheck?.Phase=="ready"&&MousePalette.Zone(this,assessmentCanvas,"launch-canvas") is {} area)connection.Send(new("launchAssessmentBegin",Zones:[area]));});
    }
    static readonly (double X,double Y)[] AssessmentPoints=[(.13,.18),(.85,.80),(.13,.80),(.85,.18),(.5,.5),(.5,.18),(.85,.5),(.13,.5),(.5,.80),(.67,.35)];
    FrameworkElement BuildAssessmentPage(){
        assessmentPage.RowDefinitions.Add(new(){Height=GridLength.Auto});assessmentPage.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});assessmentPage.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=Stack(6);header.Children.Add(assessmentPageTitle);header.Children.Add(assessmentInstructions);assessmentInstructions.MaxLines=2;assessmentInstructions.Height=52;assessmentResults.MaxLines=2;assessmentResults.Height=44;
        var options=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};assessmentOptions=options;
        Choice[] tasks=[new("mixed","Move + left + double click"),new("move","Movement only"),new("left","Left click only"),new("double","Double click only"),new("all","Include right clicks")];
        assessmentTasks=new(){Header="Actions",ItemsSource=tasks,DisplayMemberPath="Label",SelectedIndex=0,MinHeight=48,MinWidth=220};
        assessmentSize=new(){Header="Target size",ItemsSource=new[]{88,64,48},SelectedIndex=0,MinHeight=48,MinWidth=100};options.Children.Add(assessmentTasks);options.Children.Add(assessmentSize);header.Children.Add(options);
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};assessmentStartButton=Button("Start ten points",StartAssessment,true);controls.Children.Add(assessmentStartButton);controls.Children.Add(Button("Skip this target",()=>{if(assessmentRunning)connection.Send(new("assessmentSkip"));}));assessmentEndButton=Button("End test + save",()=>EndAssessmentUi(true));controls.Children.Add(assessmentEndButton);undoMovementButton=Button("Undo adjustment",()=>connection.Send(new("movementTuningUndo")));undoMovementButton.Visibility=Visibility.Collapsed;controls.Children.Add(undoMovementButton);header.Children.Add(controls);
        header.Children.Add(assessmentHold);assessmentPage.Children.Add(header);
        Grid.SetRow(assessmentCanvas,1);assessmentCanvas.Children.Add(assessmentTargetButton);assessmentPage.Children.Add(assessmentCanvas);
        var footer=Stack(2);footer.Children.Add(assessmentResults);footer.Children.Add(assessmentDetails);Grid.SetRow(footer,2);assessmentPage.Children.Add(footer);
        assessmentPage.SizeChanged+=(_,_)=>ApplyAssessmentLayout();
        assessmentCanvas.AddHandler(UIElement.TappedEvent,new TappedEventHandler((_,e)=>AssessmentClick("left",e.GetPosition(assessmentCanvas))),true);
        assessmentCanvas.AddHandler(UIElement.DoubleTappedEvent,new DoubleTappedEventHandler((_,e)=>{AssessmentClick("double",e.GetPosition(assessmentCanvas));e.Handled=true;}),true);
        assessmentCanvas.AddHandler(UIElement.RightTappedEvent,new RightTappedEventHandler((_,e)=>{AssessmentClick("right",e.GetPosition(assessmentCanvas));e.Handled=true;}),true);
        AppWindow.Changed+=(_,e)=>{if((assessmentRunning||quickAssessment&&shown?.LaunchCheck?.Phase is "running" or "validating")&&(e.DidSizeChange||e.DidPositionChange)){EndAssessmentUi(true);assessmentInstructions.Text="Window moved or resized. Restart the test to keep target coordinates reliable.";}};
        Activated+=(_,e)=>{if((assessmentRunning||quickAssessment&&shown?.LaunchCheck?.Required==true)&&e.WindowActivationState==WindowActivationState.Deactivated){EndAssessmentUi(true);assessmentInstructions.Text="Test paused because Qadr lost focus. Restart when the test is visible.";}};
        return assessmentPage;
    }
    void StartAssessment(){
        if(shown?.LaunchCheck?.Required==true){if(shown.State=="Stopped")connection.Send(new("resume"));else StartLaunchAssessment();return;}
        quickAssessment=false;Activate();
        nav.SelectedItem=assessmentNavigation;
        ShowAssessmentPage();
        if(assessmentRunning)return;
        if(shown?.MovementRecording?.Saving==true){assessmentInstructions.Text="Wait for the previous report to finish saving, then start again.";return;}
        if(shown?.State!="Tracking"){assessmentInstructions.Text="Say resume first. The test starts after face control is ready.";return;}
        if(shown.Dragging||shown.HeadScrolling){assessmentInstructions.Text="Drop the held item and leave scroll mode before testing pointer movement.";return;}
        string mode=(assessmentTasks.SelectedItem as Choice)?.Value??"mixed";
        if(mode is "mixed" or "double" or "all"&&!(shown.CurrentSettings?.EyebrowDoubleClick==true&&shown.CurrentSettings.EyebrowLearned)){assessmentInstructions.Text="Learn your eyebrow double click first, or choose Movement only / Left click only.";return;}
        if(mode=="all"&&shown.CurrentSettings?.RightClickMethod=="none"){assessmentInstructions.Text="Right click is off. Enable and learn it first, or choose another set of actions.";return;}
        assessmentRunning=true;assessmentStep=assessmentCompleted=assessmentSkipped=0;assessmentTasks.IsEnabled=assessmentSize.IsEnabled=false;
        SetAssessmentFocus(true);ApplyAssessmentLayout();
        connection.Send(new("motionRecord",Enabled:true));connection.Send(new("motionLabel",Name:"movement"));
        assessmentPage.UpdateLayout();PlaceAssessmentTarget();
    }
    void PlaceAssessmentTarget(){
        if(!assessmentRunning)return;
        var quick=quickAssessment?QuickTargetLayout.Create(assessmentCanvas.ActualWidth,assessmentCanvas.ActualHeight):null;
        if(quickAssessment&&quick is null){EndAssessmentUi(true);assessmentInstructions.Text="Not enough room for separate targets. Enlarge the window, then say resume.";return;}
        double size=quick?.Size??Math.Min(assessmentSize.SelectedItem is int selected?selected:88,QuickTargetLayout.Create(assessmentCanvas.ActualWidth,assessmentCanvas.ActualHeight)?.Size??48);
        assessmentTargetButton.Width=assessmentTargetButton.Height=size;
        var point=quickAssessment?QuickPoints[assessmentStep]:AssessmentPoints[assessmentStep];
        Canvas.SetLeft(assessmentTargetButton,Math.Clamp(point.X*assessmentCanvas.ActualWidth-size/2,8,Math.Max(8,assessmentCanvas.ActualWidth-size-8)));
        Canvas.SetTop(assessmentTargetButton,Math.Clamp(point.Y*assessmentCanvas.ActualHeight-size/2,8,Math.Max(8,assessmentCanvas.ActualHeight-size-8)));
        if(quick is not null){Canvas.SetLeft(assessmentTargetButton,quick.Targets[assessmentStep].X);Canvas.SetTop(assessmentTargetButton,quick.Targets[assessmentStep].Y);}
        string id=AssessmentId;string mode=(assessmentTasks.SelectedItem as Choice)?.Value??"mixed";
        string[] mixed=["move","left","double","left","move","double","left","move","double","left"];
        assessmentAction=quickAssessment?"move":mode is "move" or "left" or "double"?mode:mode=="all"&&assessmentStep is 4 or 8?"right":mixed[assessmentStep];
        string instruction=assessmentAction switch{"move"=>"Move here and hold inside","left"=>"Left click here","double"=>"Double click here — raise your eyebrows","right"=>"Right click here — deliberate wink",_=>"Move here"};
        assessmentTargetButton.Content=id;assessmentTargetButton.Visibility=Visibility.Visible;AutomationProperties.SetName(assessmentTargetButton,$"Target {id}: {instruction}");
        assessmentInstructions.Text=$"{(quickAssessment&&assessmentRound==2?"Check adjustment - ":"")}{assessmentStep+1}/{AssessmentTotal} · Target {id} · {instruction}"+(quickAssessment?"\nSay skip target to skip. Esc or stop the app stops and saves.":"");assessmentHold.Value=0;
        assessmentCanvas.UpdateLayout();
        pendingAssessmentTarget=true;TrySendAssessmentTarget();
    }
    // A target may advance while tracking is paused. Keep the latest unsent target
    // and submit it once control returns; never lose it in a rejected IPC command.
    void TrySendAssessmentTarget(){
        if(!assessmentRunning||!pendingAssessmentTarget||assessmentTargetQueued||shown?.State!="Tracking")return;
        string id=AssessmentId;assessmentTargetQueued=true;
        if(!DispatcherQueue.TryEnqueue(()=>{
            assessmentTargetQueued=false;
            if(!assessmentRunning||!pendingAssessmentTarget||shown?.State!="Tracking"||AssessmentId!=id)return;
            if(MousePalette.Zone(this,assessmentTargetButton,"assessment:"+id) is {} zone){
                pendingAssessmentTarget=false;connection.Send(new("assessmentTarget",Name:id,Action:assessmentAction,Zones:[zone]));
            }
        }))assessmentTargetQueued=false;
    }
    void AssessmentClick(string action,Windows.Foundation.Point local){
        if(!assessmentRunning||MousePalette.Zone(this,assessmentCanvas,"assessment-canvas") is not {} zone||assessmentCanvas.ActualWidth<=0||assessmentCanvas.ActualHeight<=0)return;
        double x=zone.X+local.X*zone.Width/assessmentCanvas.ActualWidth,y=zone.Y+local.Y*zone.Height/assessmentCanvas.ActualHeight;
        connection.Send(new("assessmentClick",Name:AssessmentId,Action:action,InputX:x,InputY:y));
    }
    void EndAssessmentUi(bool send){
        assessmentRunning=pendingAssessmentTarget=false;SetAssessmentFocus(false);assessmentTargetButton.Visibility=Visibility.Collapsed;assessmentTasks.IsEnabled=assessmentSize.IsEnabled=true;
        ApplyAssessmentLayout();
        if(send)connection.Send(new(quickAssessment?"launchAssessmentCancel":"assessmentEnd"));
        assessmentInstructions.Text="Test ended. The numeric ZIP report is being saved.";
    }
    void UpdateAssessment(EngineStatus s){
        UpdateLaunchAssessment(s);
        if(s.AssessmentRequestSequence!=seenAssessmentRequest){seenAssessmentRequest=s.AssessmentRequestSequence;if(s.AssessmentRequest=="launch")StartLaunchAssessment();else if(s.AssessmentRequest=="start")StartAssessment();else if(s.AssessmentRequest=="finish")EndAssessmentUi(false);}
        if(!assessmentRunning)return;
        TrySendAssessmentTarget();
        if(pendingAssessmentTarget&&s.State!="Tracking")assessmentInstructions.Text="Tracking paused. Keep your face visible; the next target starts after recovery.";
        if(s.MovementRecording?.Recording!=true){EndAssessmentUi(false);return;}
        if(s.Assessment is {} progress){
            assessmentHold.Value=progress.InsideProgress*100;
            string prompt=progress.Target.Action switch{"move"=>"Move here and hold inside","left"=>"Left click here","double"=>"Double click here — raise your eyebrows","right"=>"Right click here — deliberate wink",_=>"Move here"};
            assessmentInstructions.Text=$"{(quickAssessment&&assessmentRound==2?"Check adjustment - ":"")}{assessmentStep+1}/{AssessmentTotal} · Target {progress.Target.Id} · "+(s.State!="Tracking"?"Tracking paused — keep your face visible; target time still runs":progress.ConfirmingSingleClick?"Checking that this was one click…":prompt)+(quickAssessment?$" ({Math.Max(0,Math.Ceiling(8-progress.ElapsedSeconds)):F0}s left)\nSay skip target to skip. Esc or stop the app stops and saves.":"");
        }
        if(s.AssessmentResult is not {} result||result.Id!=AssessmentId)return;
        if(result.Completed)assessmentCompleted++;else if(result.Reason=="skipped"||quickAssessment&&result.Reason=="time limit")assessmentSkipped++;else{EndAssessmentUi(false);return;}
        assessmentResults.Text=$"{result.Id}: {(result.Completed?"passed":"skipped")} · {result.DurationMs/1000:F1} s · overshoots {result.Overshoots} · centre error {(result.FinalCentreErrorPx is {} error?$"{error:F1} px":"n/a")} · completed {assessmentCompleted}/{AssessmentTotal}";
        if(++assessmentStep>=AssessmentTotal){
            if(quickAssessment){launchFinishSent=true;assessmentRunning=false;assessmentTargetButton.Visibility=Visibility.Collapsed;connection.Send(new("launchAssessmentFinish"));assessmentInstructions.Text=$"Check measured: {assessmentCompleted} reached, {assessmentSkipped} skipped/timed out. Qadr is reviewing your movement settings.";}
            else{EndAssessmentUi(true);assessmentInstructions.Text=$"Finished: {assessmentCompleted} passed, {assessmentSkipped} skipped. Send the saved ZIP for review.";}
            return;
        }
        PlaceAssessmentTarget();
    }
    void ApplyAssessmentLayout(){
        bool compact=assessmentPage.ActualHeight<480;
        assessmentPageTitle.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        assessmentDetails.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        assessmentOptions.Visibility=assessmentRunning||shown?.LaunchCheck?.Required==true?Visibility.Collapsed:Visibility.Visible;
        assessmentInstructions.FontSize=compact?16:18;assessmentResults.Height=quickAssessment?24:44;assessmentResults.MaxLines=quickAssessment?1:2;
    }
}
