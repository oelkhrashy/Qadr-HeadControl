using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Qadr.Core;

namespace Qadr.Shell;

internal sealed partial class MainWindow
{
    readonly PreviewSchedule previewSchedule=new();
    readonly TextBlock trackingNotice=Text("Waiting for tracking measurements",14,Muted);
    Button adjustmentButton=null!,undoAdjustmentButton=null!;
    readonly TextBlock adjustmentMessage=Text("Choose one small change. Undo is available until you close Qadr or change movement settings elsewhere.",15);
    readonly TextBlock adjustmentValues=Text("",14,Muted);
    readonly List<Button> adjustmentChoices=[];
    void BuildSimpleControls(Button configure,Button adjust){
        adjustmentButton=adjust;
        Grid.SetColumn(adjustmentButton,2);
        adjustmentButton.Flyout=new Flyout{Content=AdjustmentPanel(),Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top};
        AutomationProperties.SetLiveSetting(mouseMode,AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(adjustmentMessage,AutomationLiveSetting.Polite);
        configure.Content="More";
        AutomationProperties.SetName(configure,"More: camera, gestures and recentre");
    }
    FrameworkElement AdjustmentPanel(){
        var panel=Stack(10);panel.Width=340;panel.Children.Add(Text("Help me adjust",22));
        panel.Children.Add(Text("Apply one small change, try pointing, then Undo if it feels worse. Gestures and calibration stay the same.",15,Muted));
        foreach(var item in new[]{("slower","The pointer is too fast"),("reach","I cannot reach the edges"),("steady","The pointer shakes at rest")}){
            var button=Button(item.Item2,()=>connection.Send(new("adjustMovement",Name:item.Item1)));adjustmentChoices.Add(button);panel.Children.Add(button);
        }
        panel.Children.Add(adjustmentValues);panel.Children.Add(adjustmentMessage);
        undoAdjustmentButton=Button("Undo last adjustment",()=>connection.Send(new("undoAdjustment")));undoAdjustmentButton.IsEnabled=false;
        panel.Children.Add(Text("One-step Undo is available for this app session. Esc or stop the app always pauses control.",13,Muted));
        var root=new Grid{Width=340,MaxHeight=430,RowSpacing=10};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.Children.Add(new ScrollViewer{Content=panel,MaxHeight=360,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Grid.SetRow(undoAdjustmentButton,1);root.Children.Add(undoAdjustmentButton);return root;
    }
    void UpdateAdjustmentPanel(EngineStatus s){
        bool available=s.Assessment is null&&s.LaunchCheck?.Phase is not ("running" or "validating")&&s.State is not ("Routine" or "Calibrating" or "LearnClick" or "LearnRest")&&!s.Dragging&&!s.HeadScrolling;
        foreach(var choice in adjustmentChoices)choice.IsEnabled=available&&((s.CurrentSettings??settings).Mapping!="position"||ReferenceEquals(choice,adjustmentChoices[0]));
        undoAdjustmentButton.IsEnabled=available&&s.Adjustment?.CanUndo==true;
        adjustmentMessage.Text=available?s.Adjustment?.Message??"Choose one small change. Undo restores your previous movement settings.":"Finish or pause the current test or action before adjusting movement.";
        var value=s.CurrentSettings??settings;adjustmentValues.Text=value.Mapping=="position"?$"Speed {value.Sensitivity*100:F0}% · Head position control. Adjust comfortable ranges in Preferences.":$"Speed {value.Sensitivity*100:F0}%  ·  Reach {value.RelativeTravelBoost:F2}×  ·  Steadiness {value.PrecisionNoiseMultiplier:F2}";
    }
    void UpdateSimpleFeedback(EngineStatus s){
        actionFeedback.Background=Pale;actionFeedback.Padding=new Thickness(12);actionFeedback.CornerRadius=new CornerRadius(14);
        bool running=s.State=="Tracking";
        state.Text=s.State switch{"Tracking"=>"Control active","Stopped"=>"Paused","Lost" or "Degraded"=>"Tracking paused","Ready"=>"Getting ready",_=>s.State};
        string health=s.State is "Lost" or "Degraded"?
            (s.Face?"Face found. Waiting for stable tracking; mouse control is paused.":"Tracking paused. Keep your whole face visible; control resumes after stable recovery."):
            s.TrackingHealth?.Message??"Waiting for tracking measurements";
        // Only announce state/message changes, not every FPS/status refresh.
        if(trackingNotice.Text!=health)trackingNotice.Text=health;
        if(setupMode)return;
        string mode=s.HeadScrolling?"Scrolling":s.Dragging?"Holding an item":MouseActions.Modes.FirstOrDefault(m=>m.Id==s.MouseMode)?.Label??"Left click";
        if(s.MouseMode=="left"&&!s.HeadScrolling&&!s.Dragging)mode+=" · "+((s.CurrentSettings??settings).ClickMethod switch{"smile"=>"Smile","dwell"=>"Dwell","mouth"=>"Mouth",_=>"Off"});
        mouseMode.FontSize=22;mouseMode.Text=running?mode:s.State is "Lost" or "Degraded"?"Tracking paused":"Control paused";
        mouseHelp.Text=!running?(s.State is "Lost" or "Degraded"?"Bring your face back into view. Rest briefly to recover. Esc stops control.":"Say resume or choose Resume to start."):s.HeadScrolling?"Move head up or down. Repeat your mouth gesture to leave scroll mode.":s.Dragging?"Move to your destination, then repeat your drag gesture to drop.":s.MouseMode=="left"&&(s.CurrentSettings??settings).ClickMethod=="smile"?"Tiny smile, hold briefly, then relax. Learn my subtle smile in Preferences if it does not register.":s.MouseHelp;
        resumeButton.Content=running?"Pause":"Resume";
        if(running&&(s.CurrentSettings??settings).Mapping=="position"&&!s.HeadScrolling&&!s.Dragging)
            mouseHelp.Text="Face forward to return to screen centre. Turn gently to point. "+mouseHelp.Text;
    }
}
