using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Qadr.Core;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace Qadr.Shell;

internal sealed partial class MainWindow : Window
{
    static bool HighContrast=>new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
    [DllImport("user32.dll")] static extern uint GetSysColor(int index);
    static Windows.UI.Color SystemColor(int index){uint n=GetSysColor(index);return Windows.UI.Color.FromArgb(255,(byte)n,(byte)(n>>8),(byte)(n>>16));}
    static SolidColorBrush Brush(byte r,byte g,byte b)=>new(HighContrast?SystemColor(r+g+b>450?5:r+g+b<200?8:g>r*1.5&&g>b?13:8):Windows.UI.Color.FromArgb(255,r,g,b));
    static readonly SolidColorBrush Emerald=Brush(4,120,87),Ink=Brush(15,49,40),Muted=Brush(74,96,88),Pale=Brush(220,248,233),White=Brush(255,255,255);
    static readonly SolidColorBrush OnPrimary=new(HighContrast?SystemColor(14):Colors.White);
    readonly EngineConnection connection=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    readonly TextBlock state=Text("Starting",14),title=Text("Welcome to Qadr",28),help=Text("Getting everything ready for you…",17),face=Text("Waiting for a face",14),distance=Text("",14),metrics=Text("",13),shortcut=Text("Preparing global stop…",14),voiceText=Text("Microphone off",14),recordingStatus=Text("",14);
    readonly Image preview=new(){Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
    readonly TextBlock previewPlaceholder=Text("Connecting your camera…",17,White),clickFeedback=Text("Rest on a target to left-click",15),eyeFeedback=Text("Hold a wink, then reopen",14);
    readonly ProgressBar rightProgress=new(){Minimum=0,Maximum=100,Height=8,Foreground=Emerald};
    readonly FaceGuide faceGuide=new(new SolidColorBrush(Windows.UI.Color.FromArgb(255,110,255,182)),new SolidColorBrush(Windows.UI.Color.FromArgb(255,255,213,100)));
    readonly VisualCoach coach=new(Ink,Pale);
    readonly ProgressBar calibration=new(){Minimum=0,Maximum=100,Height=8,Foreground=Emerald};
    readonly ProgressRing dwell=new(){Width=46,Height=46,IsIndeterminate=false,Minimum=0,Maximum=100,Foreground=Emerald};
    Button stopButton=null!;
    Grid windowRoot=null!;
    readonly Border assessmentFocusHost=new(){Background=Brush(245,248,246),Visibility=Visibility.Collapsed};
    readonly ComboBox camera=new(){PlaceholderText="Choose a camera",HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=48};
    readonly ComboBox routineList=new(){PlaceholderText="Choose a saved routine",HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=48};
    readonly TextBox routineName=new(){Header="Routine name",PlaceholderText="For example: morning email",MaxLength=32};
    readonly TextBlock routineReview=Text("Choose a saved routine to review its steps.",16);
    readonly CheckBox recordText=new(){Content="Record text in supported fields",IsChecked=false};
    readonly ToggleSwitch microphone=new(){Header="Routine voice commands",OffContent="Routine commands off",OnContent="Routine commands listening"};
    readonly Grid control=new(),routines=new(),preferences=new();
    readonly ContentControl pageHost=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    readonly TextBlock instruction=Text("Wait while Qadr starts",24,White),steps=Text("Position   →   Range   →   Gestures   →   Relax",16),checks=Text("Waiting for camera geometry",16);
    Grid previewBody=null!,setupChecks=null!,controlTools=null!,actionFeedback=null!;
    Button resumeButton=null!;
    bool setupMode=true;
    string currentInstruction="Wait while Qadr starts";
    Border cameraBanner=null!;
    readonly TextBlock checkDetails=Text("",16);
    MousePalette palette=null!;
    MouseDock? dock;
    readonly TextBlock mouseMode=Text("Mode: Left click",16),mouseHelp=Text("Choose a mouse action, then use your click on its target.",13);
    readonly List<Action<Settings>> settingControls=[];
    readonly NavigationView nav=new(){PaneDisplayMode=NavigationViewPaneDisplayMode.Top,IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsSettingsVisible=false,IsPaneToggleButtonVisible=false};
    Settings settings;
    string? lastPreview;
    double lastUiUpdate;
    string[] lastCameras=[],lastRoutines=[];
    EngineStatus? shown;
    bool updating;
    bool cameraDialogShown,refreshing;
    long seenDockToggle;
    public MainWindow()
    {
        Diagnostic.Write("MainWindow constructor");
        speech.SpeakingChanged=active=>connection.Send(new("speechActivity",Enabled:active));
        Title="Qadr • Your movement. Your control.";AppWindow.Resize(new Windows.Graphics.SizeInt32(1280,860));
        if(Environment.GetCommandLineArgs().Contains("--no-input"))Title="Qadr • Preview only (mouse input disabled)";
        if(!HighContrast){foreach(string key in new[]{"SystemAccentColor","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3","SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3"})Application.Current.Resources[key]=Emerald.Color;
        foreach(string key in new[]{"AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentTextFillColorPrimaryBrush","SystemControlHighlightAccentBrush"})Application.Current.Resources[key]=Emerald;}
        var store=new Storage(Arguments.DataDirectory(Environment.GetCommandLineArgs()));settings=(store.Load<Settings>("settings.json")??new()).Validate();
        var root=new Grid{Background=Brush(245,248,246),RequestedTheme=HighContrast?ElementTheme.Default:ElementTheme.Light};
        root.RowDefinitions.Add(new(){Height=new GridLength(78)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{Margin=new Thickness(16,8,16,8),ColumnSpacing=12};header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var logo=new Image{Width=44,Height=44,Source=new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory,"brand","qadr-icon.png")))};AutomationProperties.SetName(logo,"Qadr logo");header.Children.Add(logo);
        var brand=new StackPanel{Margin=new Thickness(12,0,0,0)};brand.Children.Add(Text("Qadr  قادر",22));brand.Children.Add(Text("Your movement. Your control.",12,Muted));Grid.SetColumn(brand,1);header.Children.Add(brand);
        state.Foreground=Emerald;var badge=new Border{Background=Pale,CornerRadius=new(18),Padding=new Thickness(16,10,16,10),VerticalAlignment=VerticalAlignment.Center,Child=state};Grid.SetColumn(badge,2);badge.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(badge);
        stopButton=Button("■ Stop · Esc",()=>connection.Send(new("stop")));stopButton.Background=HighContrast?Brush(4,120,87):new SolidColorBrush(Windows.UI.Color.FromArgb(255,153,27,27));stopButton.Foreground=OnPrimary;stopButton.MinWidth=150;stopButton.TabIndex=0;Grid.SetColumn(stopButton,3);header.Children.Add(stopButton);root.Children.Add(header);
        var controlItem=Item("Control","\uE7F4",control);var routineItem=Item("Routines","\uE768",new ScrollViewer{Content=routines,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});var settingsItem=Item("Preferences","\uE713",new ScrollViewer{Content=preferences,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        nav.MenuItems.Add(controlItem);nav.MenuItems.Add(routineItem);nav.MenuItems.Add(settingsItem);
        assessmentNavigation=Item("Movement test","\uE9D9",BuildAssessmentPage());nav.MenuItems.Add(assessmentNavigation);
        nav.SelectionChanged+=(_,e)=>{if(e.SelectedItem is NavigationViewItem item && item.Tag is UIElement page && !ReferenceEquals(pageHost.Content,page)){if(assessmentRunning&&!ReferenceEquals(page,assessmentPage))EndAssessmentUi(true);if(ReferenceEquals(page,assessmentPage))ShowAssessmentPage();else{pageHost.Content=null;pageHost.Content=page;}}};
        pageHost.Content=control;nav.Content=pageHost;
        Grid.SetRow(nav,1);root.Children.Add(nav);
        BuildControl();BuildRoutines();BuildPreferences();
        Diagnostic.Write("Pages constructed");
        var footer=new Grid{Margin=new Thickness(16,8,16,8)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        shortcut.FontSize=14;shortcut.MaxLines=1;var status=new StackPanel{VerticalAlignment=VerticalAlignment.Center};status.Children.Add(shortcut);footer.Children.Add(status);
        Grid.SetRow(footer,2);root.Children.Add(footer);
        windowRoot=root;Grid.SetRowSpan(assessmentFocusHost,3);root.Children.Add(assessmentFocusHost);
        Content=root;Closed+=(_,_)=>{closed=true;cameraDialog?.Hide();tutorialDialog?.Hide();speech.Dispose();timer.Stop();dock?.Close();connection.Dispose();};timer.Tick+=async(_,_)=>await Refresh();
        var launchArgs=Environment.GetCommandLineArgs();
        if(launchArgs.Contains("--layout-check")&&launchArgs.Contains("--no-input")&&launchArgs.Contains("--test-source")&&launchArgs.Contains("--diagnose-no-engine"))root.Loaded+=async(_,_)=>await CheckLayout();
        Diagnostic.Write("Content set");
        if(!Environment.GetCommandLineArgs().Contains("--diagnose-no-engine"))try{connection.Start();timer.Start();}catch(Exception e){title.Text="Qadr could not start";help.Text=e.Message;}
    }
    static NavigationViewItem Item(string name,string glyph,UIElement page)=>new(){Content=name,Icon=new FontIcon{Glyph=glyph},Tag=page};
    static TextBlock Text(string text,double size,SolidColorBrush? color=null)=>new(){Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap};
    static Button Button(string label,Action action,bool primary=false)
    {
        var button=new Button{Content=label,MinHeight=48,Padding=new Thickness(18,10,18,10),CornerRadius=new(12),HorizontalAlignment=HorizontalAlignment.Stretch,FontSize=16};
        if(primary){button.Background=Emerald;button.Foreground=OnPrimary;}
        button.Click+=(_,_)=>action();AutomationProperties.SetName(button,label);return button;
    }
    static Border Card(UIElement child)=>new(){Child=child,Background=White,CornerRadius=new(20),Padding=new Thickness(22),BorderBrush=Brush(223,234,227),BorderThickness=new Thickness(1)};
    static StackPanel Stack(double spacing=14)=>new(){Spacing=spacing};
    static void Page(Grid grid){grid.Margin=new Thickness(24);}
    void BuildControl()
    {
        control.Margin=new Thickness(16,8,16,8);control.RowSpacing=8;
        foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto,GridLength.Auto})control.RowDefinitions.Add(new(){Height=height});
        var stepHeader=Stack(4);title.FontSize=22;title.MaxLines=1;title.TextTrimming=TextTrimming.CharacterEllipsis;stepHeader.Children.Add(title);stepHeader.Children.Add(steps);help.FontSize=16;help.MaxLines=2;stepHeader.Children.Add(help);trackingNotice.MaxLines=2;trackingNotice.TextTrimming=TextTrimming.CharacterEllipsis;stepHeader.Children.Add(trackingNotice);AutomationProperties.SetLiveSetting(trackingNotice,AutomationLiveSetting.Polite);control.Children.Add(stepHeader);
        previewBody=new Grid{ColumnSpacing=16};previewBody.ColumnDefinitions.Add(new(){Width=new GridLength(3,GridUnitType.Star)});previewBody.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        // Keep the full 4:3 camera image. Cropping it to 16:9 would remove face geometry.
        var frame=new Grid{Width=480,Height=360};frame.Children.Add(preview);frame.Children.Add(faceGuide);
        previewPlaceholder.HorizontalAlignment=HorizontalAlignment.Center;previewPlaceholder.VerticalAlignment=VerticalAlignment.Center;
        var live=Text("Live · private on your PC",14,White);live.Margin=new Thickness(10);live.VerticalAlignment=VerticalAlignment.Bottom;live.HorizontalAlignment=HorizontalAlignment.Left;
        var picture=new Grid{RowSpacing=0};picture.RowDefinitions.Add(new(){Height=GridLength.Auto});picture.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var banner=new Border{Background=Brush(8,43,33),Padding=new Thickness(16,12,16,12),MinHeight=64,Child=instruction};picture.Children.Add(banner);AutomationProperties.SetLiveSetting(instruction,AutomationLiveSetting.Polite);
        cameraBanner=banner;
        var imageBox=new Viewbox{Stretch=Stretch.Uniform,Child=frame};Grid.SetRow(imageBox,1);picture.Children.Add(imageBox);Grid.SetRow(live,1);picture.Children.Add(live);Grid.SetRow(previewPlaceholder,1);picture.Children.Add(previewPlaceholder);
        previewBody.Children.Add(new Border{Background=Brush(8,43,33),CornerRadius=new(16),Child=picture});AutomationProperties.SetName(preview,"Live mirrored face preview with target and detected face outlines");
        setupChecks=new Grid{RowSpacing=8};setupChecks.RowDefinitions.Add(new(){Height=GridLength.Auto});setupChecks.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});setupChecks.RowDefinitions.Add(new(){Height=GridLength.Auto});
        setupChecks.Children.Add(calibration);var checklist=Stack(8);checklist.Children.Add(Text("Setup checks",20));checklist.Children.Add(checks);coach.Visibility=Visibility.Collapsed;checklist.Children.Add(coach);Grid.SetRow(checklist,1);setupChecks.Children.Add(checklist);
        var repeat=Button("Repeat instruction",()=>_ = speech.Say(currentInstruction));var dwellOnly=Button("Use dwell only",()=>connection.Send(new("useDwell")));
        Grid.SetColumn(setupChecks,1);previewBody.Children.Add(setupChecks);Grid.SetRow(previewBody,1);control.Children.Add(previewBody);
        controlTools=new Grid{ColumnSpacing=8,MaxWidth=820,HorizontalAlignment=HorizontalAlignment.Center};for(int i=0;i<4;i++)controlTools.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        resumeButton=Button("Resume",()=>connection.Send(new(shown?.State=="Tracking"?"stop":"resume")),true);resumeButton.TabIndex=1;controlTools.Children.Add(resumeButton);
        var relearn=Button("Mouse tools",()=>{if(dock is null)ToggleDock();if(dock is not null&&!dock.Expanded)dock.TogglePanel();});Grid.SetColumn(relearn,1);controlTools.Children.Add(relearn);
        var configure=Button("Setup / gestures",()=>{});configure.Flyout=new Flyout{Content=SetupPanel(),Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top};Grid.SetColumn(configure,2);controlTools.Children.Add(configure);
        var floating=Button("Help me adjust",()=>{});Grid.SetColumn(floating,3);controlTools.Children.Add(floating);BuildSimpleControls(configure,floating);Grid.SetRow(controlTools,2);control.Children.Add(controlTools);
        Grid.SetColumn(repeat,0);controlTools.Children.Add(repeat);Grid.SetColumn(dwellOnly,1);controlTools.Children.Add(dwellOnly);
        var checkHelp=Button("Setup checks",()=>{});checkDetails.Width=300;checkHelp.Flyout=new Flyout{Content=checkDetails};Grid.SetColumn(checkHelp,3);controlTools.Children.Add(checkHelp);
        foreach(var button in controlTools.Children.OfType<Button>()){button.FontSize=16;button.Padding=new Thickness(6);button.Content=new TextBlock{Text=(string)button.Content,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};}
        palette=new(4,id=>connection.Send(new("mouseMode",Name:id)),Emerald,White,Ink,OnPrimary,primaryOnly:true);palette.MaxWidth=820;palette.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetRow(palette,3);control.Children.Add(palette);
        actionFeedback=new Grid{ColumnSpacing=12};actionFeedback.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actionFeedback.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var selected=Stack(2);mouseHelp.FontSize=14;mouseHelp.MaxLines=2;selected.Children.Add(mouseMode);selected.Children.Add(mouseHelp);actionFeedback.Children.Add(selected);
        var clicks=Stack(2);dwell.Width=dwell.Height=32;clickFeedback.FontSize=14;clickFeedback.MaxLines=1;eyeFeedback.FontSize=14;eyeFeedback.MaxLines=2;
        var progressRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};progressRow.Children.Add(dwell);progressRow.Children.Add(clickFeedback);clicks.Children.Add(progressRow);clicks.Children.Add(rightProgress);clicks.Children.Add(eyeFeedback);Grid.SetColumn(clicks,1);actionFeedback.Children.Add(clicks);Grid.SetRow(actionFeedback,4);control.Children.Add(actionFeedback);
        control.SizeChanged+=(_,_)=>ApplyControlLayout();SetPresentation(new("Starting","Starting","",0,0,0,false,"",0,0,"",[],null,[],"Microphone off"));
    }
    void ApplyControlLayout(){
        bool compact=control.ActualHeight<540;
        previewBody.Visibility=Visibility.Visible;
        cameraBanner.Visibility=setupMode?Visibility.Visible:Visibility.Collapsed;
        previewBody.MaxHeight=setupMode?double.PositiveInfinity:double.PositiveInfinity;
        previewBody.Width=double.NaN;
        previewBody.HorizontalAlignment=HorizontalAlignment.Stretch;
        setupChecks.Visibility=setupMode&&!compact?Visibility.Visible:Visibility.Collapsed;
        previewBody.ColumnDefinitions[1].Width=setupMode&&!compact?new GridLength(1,GridUnitType.Star):new GridLength(0);
        controlTools.ColumnDefinitions[3].Width=new GridLength(1,GridUnitType.Star);
        steps.Visibility=setupMode?Visibility.Visible:Visibility.Collapsed;
        palette.Visibility=Visibility.Collapsed;
        actionFeedback.Visibility=setupMode?Visibility.Collapsed:Visibility.Visible;
        // Camera/gesture recovery remains reachable during setup, but Resume cannot interrupt it.
        resumeButton.Visibility=setupMode?Visibility.Collapsed:Visibility.Visible;
        controlTools.Children[1].Visibility=setupMode?Visibility.Collapsed:Visibility.Visible;
        controlTools.Children[2].Visibility=Visibility.Visible;Grid.SetColumn((FrameworkElement)controlTools.Children[2],setupMode?2:3);
        controlTools.Children[3].Visibility=setupMode?Visibility.Collapsed:Visibility.Visible;
        adjustmentButton.Visibility=setupMode?Visibility.Collapsed:Visibility.Visible;
        foreach(var index in new[]{4,5,6})controlTools.Children[index].Visibility=setupMode?Visibility.Visible:Visibility.Collapsed;
    }
    void SetPresentation(EngineStatus s){
        var model=SetupPresentation.From(s);state.Text=s.State;setupMode=model.Setup;currentInstruction=model.Instruction;instruction.Text=currentInstruction;title.Text=model.Heading;checks.Text=model.Checks;
        checkDetails.Text=model.Checks+"\n\nWhite outline: your detected face. Colored outline: learned target. Lighting is not measured.";
        help.Text=s.Help;help.Visibility=s.State is "Stopped" or "Lost" or "Degraded"?Visibility.Visible:Visibility.Collapsed;
        steps.Text=$"Step {model.Step} of 4 · "+string.Join("   ",new[]{"Position","Range","Gestures","Relax"}.Select((name,i)=>(i+1<model.Step?"✓ ":i+1==model.Step?"● ":"○ ")+name));
        coach.Visibility=s.State=="LearnClick"?Visibility.Visible:Visibility.Collapsed;
        resumeButton.Content=s.State=="Tracking"?"Pause":"Resume";
        UpdateSimpleFeedback(s);ApplyControlLayout();
    }
    FrameworkElement SetupPanel(){
        var panel=Stack(8);panel.Width=340;panel.Children.Add(Text("Camera and click setup",19));panel.Children.Add(camera);
        camera.SelectionChanged+=(_,_)=>{if(!updating && camera.SelectedItem is CameraDevice device)connection.Send(new("camera",CameraId:device.Id));};camera.DisplayMemberPath="Name";
        panel.Children.Add(ClickChoice(false));panel.Children.Add(ClickChoice(true));
        panel.Children.Add(Button("Learn drag gesture",()=>connection.Send(new("learnDrag"))));panel.Children.Add(Button("Learn scroll gesture",()=>connection.Send(new("learnScroll"))));
        panel.Children.Add(Button("Learn eyebrow double-click",()=>connection.Send(new("learnEyebrows"))));
        panel.Children.Add(Button("Recentre in my comfortable position",()=>connection.Send(new("recentre"))));
        panel.Children.Add(Button("Relearn movement range",()=>connection.Send(new("calibrate"))));
        panel.Children.Add(Button("Fit my comfortable position",()=>connection.Send(new("comfortablePosition"))));panel.Children.Add(Button("Learn my smile",()=>connection.Send(new("learnSmile"))));panel.Children.Add(Button("Learn my wink / both eyes",()=>connection.Send(new("learnEyes"))));
        panel.Children.Add(Button("Use dwell only — no eye gestures",()=>connection.Send(new("useDwell"))));panel.Children.Add(Button("Refresh cameras",()=>connection.Send(new("refresh"))));
        panel.Children.Add(microphone);panel.Children.Add(voiceText);panel.Children.Add(metrics);microphone.Toggled+=(_,_)=>{if(!updating)connection.Send(new("voice",Enabled:microphone.IsOn));};
        return new ScrollViewer{Content=panel,MaxHeight=540,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    void ToggleDock(){
        if(dock is not null){dock.Close();dock=null;return;}
        dock=new(id=>connection.Send(new("mouseMode",Name:id)),()=>connection.Send(new("stop")),Emerald,White,Ink,OnPrimary,White,AppWindow.Id);dock.Closed+=(_,_)=>dock=null;if(shown is not null)dock.Update(shown);dock.Activate();
    }
    void BuildRoutines()
    {
        Page(routines);var stack=Stack(18);routines.Children.Add(stack);stack.Children.Add(Text("Do it once. Use it again.",28));stack.Children.Add(Text("Record a short task and run it by voice. Review the steps before running it.",17,Muted));
        var create=Stack();create.Children.Add(Text("Create a routine",22));create.Children.Add(routineName);create.Children.Add(recordText);
        routineName.LostFocus+=(_,_)=>connection.Send(new("routineDraft",Name:routineName.Text,Enabled:recordText.IsChecked==true));
        recordText.Click+=(_,_)=>connection.Send(new("routineDraft",Name:routineName.Text,Enabled:recordText.IsChecked==true));
        create.Children.Add(Text("Text recording is optional. It only reads supported non-password text fields. Keep private information out of routines.",14,Muted));
        create.Children.Add(Button("Start recording",()=>{connection.Send(new("record",Name:routineName.Text,Enabled:recordText.IsChecked==true));recordingStatus.Text="Recording requested. Perform the steps, then save.";},true));
        create.Children.Add(Button("Save recording",()=>{connection.Send(new("saveRecording"));recordingStatus.Text="Recording saved if supported steps were captured.";}));create.Children.Add(recordingStatus);stack.Children.Add(Card(create));
        var saved=Stack();saved.Children.Add(Text("Saved routines",22));saved.Children.Add(routineList);
        saved.Children.Add(Button("Review selected routine",()=>{if(routineList.SelectedItem is string name)connection.Send(new("reviewRoutine",Name:name));}));
        saved.Children.Add(routineReview);
        saved.Children.Add(Button("Confirm reviewed routine or step",()=>connection.Send(new("confirm")),true));saved.Children.Add(Text("Qadr stops if a target is missing. Sending, deleting and paying steps ask for another confirmation. Esc cancels playback.",14,Muted));stack.Children.Add(Card(saved));
    }
    void BuildPreferences()
    {
        Page(preferences);var stack=Stack(18);preferences.Children.Add(stack);stack.Children.Add(Text("Make Qadr feel right for you",28));
        stack.Children.Add(CursorComfortSettings());
        var spoken=new ToggleSwitch{Header="Spoken startup and calibration guidance",IsOn=settings.SpokenGuidance};spoken.Toggled+=(_,_)=>{if(!updating){settings=settings with{SpokenGuidance=spoken.IsOn};speech.Enabled=spoken.IsOn;if(!spoken.IsOn)speech.Cancel();connection.Send(new("settings",Settings:settings));}};settingControls.Add(s=>spoken.IsOn=s.SpokenGuidance);stack.Children.Add(spoken);
        var clicks=Stack();clicks.Children.Add(Text("How would you like to click?",22));
        clicks.Children.Add(ClickChoice(false));clicks.Children.Add(ClickChoice(true));
        clicks.Children.Add(Text("Left click: rest on your target, smile, or open your mouth. Right click: close your chosen eye(s) deliberately, hold, then reopen. Ordinary short blinks do not click.",15,Muted));
        var dragBinding=new ToggleSwitch{Header="Pucker lips to pick up / drop",IsOn=settings.PuckerDrag};dragBinding.Toggled+=(_,_)=>{if(!updating){settings=settings with{PuckerDrag=dragBinding.IsOn};SaveSettings();}};settingControls.Add(s=>dragBinding.IsOn=s.PuckerDrag);clicks.Children.Add(dragBinding);
        clicks.Children.Add(Button("Learn my lip pucker for drag",()=>connection.Send(new("learnDrag"))));
        var scrollBinding=new ToggleSwitch{Header="Open mouth to toggle head scrolling",IsOn=settings.MouthScroll};scrollBinding.Toggled+=(_,_)=>{if(!updating){settings=settings with{MouthScroll=scrollBinding.IsOn};SaveSettings();}};settingControls.Add(s=>scrollBinding.IsOn=s.MouthScroll);clicks.Children.Add(scrollBinding);
        clicks.Children.Add(Button("Learn my mouth opening for scroll",()=>connection.Send(new("learnScroll"))));
        clicks.Children.Add(Text("Learn once: pucker and relax to pick up; repeat to drop. Open mouth and relax to enter scroll mode; gently move head up/down; repeat to exit. The pointer stays over the page. Esc or tracking loss releases drag and stops scrolling. Mouth opening cannot also be assigned to left-click.",14,Muted));
        clicks.Children.Add(Slider("Drag / scroll gesture hold",.2,.8,settings.ToggleGestureHold,v=>settings=settings with{ToggleGestureHold=v}));
        var browToggle=new ToggleSwitch{Header="Raise eyebrows to double-click",IsOn=settings.EyebrowDoubleClick};browToggle.Toggled+=(_,_)=>{if(!updating){settings=settings with{EyebrowDoubleClick=browToggle.IsOn};SaveSettings();}};settingControls.Add(s=>browToggle.IsOn=s.EyebrowDoubleClick);clicks.Children.Add(browToggle);
        clicks.Children.Add(Button("Learn my eyebrow raise",()=>connection.Send(new("learnEyebrows"))));clicks.Children.Add(Text("Raise, hold briefly, then relax to double-click once. Learn your comfortable raise first; relax before repeating. No mouse-tool selection is needed.",14,Muted));
        clicks.Children.Add(Slider("Eyebrow hold in seconds",.2,.8,settings.EyebrowHold,v=>settings=settings with{EyebrowHold=v}));
        clicks.Children.Add(Button("Learn my subtle smile",()=>connection.Send(new("learnSmile"))));clicks.Children.Add(Button("Learn my wink or eye closure",()=>connection.Send(new("learnEyes"))));
        clicks.Children.Add(Slider("Wink hold in seconds",.25,1.5,settings.RightClickHold,v=>settings=settings with{RightClickHold=v}));
        clicks.Children.Add(Text("Both eyes require at least 0.4 seconds to help reject ordinary blinks. Right click fires when you reopen.",14,Muted));
        clicks.Children.Add(Slider("Time between right clicks",.25,2,settings.RightClickCooldown,v=>settings=settings with{RightClickCooldown=v}));
        clicks.Children.Add(Slider("Dwell time",.6,3,settings.DwellSeconds,v=>settings=settings with{DwellSeconds=v}));
        clicks.Children.Add(Text("For a discreet left click, learn a tiny smile. Dwell needs no expression and remains available. Lower strength means a smaller smile; test for unintended clicks.",14,Muted));
        clicks.Children.Add(Slider("Smile strength",.02,.9,settings.SmileThreshold,v=>settings=settings with{SmileThreshold=v,SmileLearned=true}));
        clicks.Children.Add(Slider("Mouth strength",.2,.9,settings.MouthThreshold,v=>settings=settings with{MouthThreshold=v}));
        clicks.Children.Add(Slider("Blink strength",.2,.95,settings.BlinkThreshold,v=>settings=settings with{BlinkThreshold=v,EyesLearned=true}));stack.Children.Add(Card(clicks));
        var movement=Stack();movement.Children.Add(Text("Comfortable movement",22));
        var handsFree=new ToggleSwitch{Header="Hands-free resume and stop microphone",IsOn=settings.HandsFreeVoice};
        handsFree.Toggled+=(_,_)=>{if(!updating){settings=settings with{HandsFreeVoice=handsFree.IsOn};SaveSettings();}};
        settingControls.Add(s=>handsFree.IsOn=s.HandsFreeVoice);movement.Children.Add(handsFree);
        movement.Children.Add(Text("Say resume to start control, or stop the app to stop it. Speech stays on this PC. Listening pauses during spoken guidance; Esc always stops control.",14,Muted));
        Choice[] algorithms=[new("adaptive","Adaptive precision"),new("classic","Classic smoothing")];
        var algorithm=new ComboBox{Header="Pointer smoothing",ItemsSource=algorithms,DisplayMemberPath="Label",SelectedItem=algorithms.First(v=>v.Value==settings.MovementAlgorithm),MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch};
        algorithm.SelectionChanged+=(_,_)=>{if(!updating&&algorithm.SelectedItem is Choice selected){settings=settings with{MovementAlgorithm=selected.Value};SaveSettings();}};
        settingControls.Add(s=>algorithm.SelectedItem=algorithms.First(v=>v.Value==s.MovementAlgorithm));movement.Children.Add(algorithm);
        movement.Children.Add(Text("Adaptive precision steadies small movements and responds faster as you move. Classic smoothing is available if it feels more comfortable.",14,Muted));
        var direct=new ToggleSwitch{Header="Direct response (lower movement delay)",IsOn=settings.DirectResponse};direct.Toggled+=(_,_)=>{if(!updating){settings=settings with{DirectResponse=direct.IsOn};SaveSettings();}};settingControls.Add(s=>direct.IsOn=s.DirectResponse);movement.Children.Add(direct);
        var balance=new ToggleSwitch{Header="Balance left and right response",IsOn=settings.BalanceHorizontal};balance.Toggled+=(_,_)=>{if(!updating){settings=settings with{BalanceHorizontal=balance.IsOn};SaveSettings();}};settingControls.Add(s=>balance.IsOn=s.BalanceHorizontal);movement.Children.Add(balance);movement.Children.Add(Text("Balance uses the geometric mean of both learned ranges. Switch off to keep separate sensitivity for each direction.",14,Muted));
        var verticalBalance=new ToggleSwitch{Header="Balance up and down response",IsOn=settings.BalanceVertical};verticalBalance.Toggled+=(_,_)=>{if(!updating){settings=settings with{BalanceVertical=verticalBalance.IsOn};SaveSettings();}};settingControls.Add(s=>verticalBalance.IsOn=s.BalanceVertical);movement.Children.Add(verticalBalance);
        var personalize=new ToggleSwitch{Header="Use my local resting-movement model",IsOn=settings.PersonalizeMovement};personalize.Toggled+=(_,_)=>{if(!updating){settings=settings with{PersonalizeMovement=personalize.IsOn};SaveSettings();}};settingControls.Add(s=>personalize.IsOn=s.PersonalizeMovement);movement.Children.Add(personalize);
        movement.Children.Add(Text("Learns only numerical rest-noise statistics on this PC. It does not save video or guess targets. Use the four-second rest lesson to personalize it.",14,Muted));
        movement.Children.Add(Button("Learn my resting steadiness",()=>connection.Send(new("learnRest"))));movement.Children.Add(Button("Reset resting model",()=>connection.Send(new("resetRest"))));
        movement.Children.Add(Slider("Responsiveness",.05,1,settings.Responsiveness,v=>settings=settings with{Responsiveness=v}));movement.Children.Add(Slider("Rest zone",.01,.12,settings.Deadzone,v=>settings=settings with{Deadzone=v}));
        movement.Children.Add(Text("Reach boost applies to deliberate travel. Slow adjustments keep their original sensitivity; stop your head to settle the pointer. Increase reach if you cannot reach the screen edges. 1 turns the boost off. It does not move the pointer by itself.",14,Muted));
        var jumpLimit=new ToggleSwitch{Header="Limit fast pointer jumps",IsOn=settings.RelativeSpeedLimit>0};jumpLimit.Toggled+=(_,_)=>{if(!updating){settings=settings with{RelativeSpeedLimit=jumpLimit.IsOn?1000:0};SaveSettings();}};settingControls.Add(s=>jumpLimit.IsOn=s.RelativeSpeedLimit>0);movement.Children.Add(jumpLimit);
        movement.Children.Add(Slider("Maximum movement speed (pixels/second)",200,1500,settings.RelativeSpeedLimit>0?settings.RelativeSpeedLimit:550,v=>settings=settings with{RelativeSpeedLimit=v}));
        movement.Children.Add(Text("Applies to Move with my head. Limits sudden travel without storing a backlog. Fast turns may travel less; increase the limit if you need faster movement. Switch off to restore unrestricted relative movement.",14,Muted));
        Choice[] movementStyles=[new("position","Head position: forward is screen centre"),new("relative","Move with my head"),new("rate","Hold to keep moving")];
        var mapping=new ComboBox{Header="Movement style",ItemsSource=movementStyles,DisplayMemberPath="Label",SelectedItem=movementStyles.First(v=>v.Value==settings.Mapping),MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch};mapping.SelectionChanged+=(_,_)=>{if(!updating&&mapping.SelectedItem is Choice selected){settings=settings with{Mapping=selected.Value};SaveSettings();}};settingControls.Add(s=>mapping.SelectedItem=movementStyles.First(v=>v.Value==s.Mapping));movement.Children.Add(mapping);
        var recoveryCentre=new ToggleSwitch{Header="Centre pointer after tracking recovery",IsOn=settings.CentreOnRecovery};
        recoveryCentre.Toggled+=(_,_)=>{if(!updating){settings=settings with{CentreOnRecovery=recoveryCentre.IsOn};SaveSettings();}};
        settingControls.Add(s=>recoveryCentre.IsOn=s.CentreOnRecovery);movement.Children.Add(recoveryCentre);
        movement.Children.Add(Text("On: after stable recovery, the pointer returns to the screen centre. Off: keep its position and rebase your comfortable head reference. Tests preserve pointer position. Say centre pointer for deliberate recentring.",14,Muted));
        var previewRates=new[]{15,30,60};var previewRate=new ComboBox{Header="Camera preview rate (pointer tracking stays unchanged)",ItemsSource=previewRates,SelectedItem=settings.PreviewFps,MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch};
        previewRate.SelectionChanged+=(_,_)=>{if(!updating&&previewRate.SelectedItem is int rate){settings=settings with{PreviewFps=rate};SaveSettings();}};
        settingControls.Add(s=>previewRate.SelectedItem=s.PreviewFps);movement.Children.Add(previewRate);
        movement.Children.Add(Text("15 uses less display decoding; 30 is the default. 60 is optional and depends on available camera/results. This does not change AI inference or pointer speed.",14,Muted));
        movement.Children.Add(Text("Head position: your comfortable forward pose maps to screen centre. Turn to reach an edge and return to the same pose to return to centre. Ranges below are in degrees; smaller means less head movement. Start with comfortable values. Screen reach and maximum speed above apply to Relative control. Position mode keeps its origin during recovery regardless of the recovery-centre preference.",14,Muted));
        movement.Children.Add(Slider("Position horizontal range (degrees)",.025*180/Math.PI,.6*180/Math.PI,settings.PositionHorizontalRange*180/Math.PI,v=>settings=settings with{PositionHorizontalRange=v*Math.PI/180}));
        movement.Children.Add(Slider("Position upward range (degrees)",.025*180/Math.PI,.6*180/Math.PI,settings.PositionUpRange*180/Math.PI,v=>settings=settings with{PositionUpRange=v*Math.PI/180}));
        movement.Children.Add(Slider("Position downward range (degrees)",.025*180/Math.PI,.6*180/Math.PI,settings.PositionDownRange*180/Math.PI,v=>settings=settings with{PositionDownRange=v*Math.PI/180}));
        var axisAssist=new ToggleSwitch{Header="Gentle vertical-line assistance (position mode)",IsOn=settings.PositionAxisAssist};axisAssist.Toggled+=(_,_)=>{if(!updating){settings=settings with{PositionAxisAssist=axisAssist.IsOn};SaveSettings();}};settingControls.Add(s=>axisAssist.IsOn=s.PositionAxisAssist);movement.Children.Add(axisAssist);
        var eyesUnavailable=new ToggleSwitch{Header="Do not use eye or eyebrow actions",IsOn=settings.EyeActionsUnavailable};eyesUnavailable.Toggled+=(_,_)=>{if(!updating){settings=settings with{EyeActionsUnavailable=eyesUnavailable.IsOn};SaveSettings();}};settingControls.Add(s=>eyesUnavailable.IsOn=s.EyeActionsUnavailable);movement.Children.Add(eyesUnavailable);
        movement.Children.Add(Text("For obscured eyes, sunglasses or uncomfortable eye movement: this disables wink/right-eye and eyebrow actions while preserving learned bindings. Dwell or a learned smile can remain available. It does not make the face detector reliable through sunglasses; if pose tracking fails, control pauses.",14,Muted));
        movement.Children.Add(Text("Move with my head: move to guide the pointer, then settle to stop and dwell-click. It continues beyond the learned exploration range. Hold to keep moving: hold your head away from centre to travel; return to your relaxed centre to stop. Recentre resets your comfortable origin without relearning your movement.",14,Muted));
        var invertX=new CheckBox{Content="Reverse left and right",IsChecked=settings.InvertX};invertX.Click+=(_,_)=>{settings=settings with{InvertX=invertX.IsChecked==true};SaveSettings();};movement.Children.Add(invertX);
        var invertY=new CheckBox{Content="Reverse up and down",IsChecked=settings.InvertY};invertY.Click+=(_,_)=>{settings=settings with{InvertY=invertY.IsChecked==true};SaveSettings();};movement.Children.Add(invertY);
        movement.Children.Add(Button("Restore my saved movement profile",()=>connection.Send(new("restoreProfile"))));stack.Children.Add(Card(movement));
        stack.Children.Add(MovementDiagnostics());
        stack.Children.Add(Practice());
    }
    sealed record Choice(string Value,string Label);
    FrameworkElement ClickChoice(bool right){
        Choice[] choices=right?[new("either","Wink or both eyes"),new("left","Left eye only"),new("right","Right eye only"),new("both","Both eyes"),new("none","No right-click")]:[new("dwell","Rest on target (dwell)"),new("smile","Smile"),new("mouth","Open mouth"),new("none","No left-click")];
        var box=new ComboBox{Header=right?"Right click":"Left click",ItemsSource=choices,DisplayMemberPath="Label",SelectedItem=choices.FirstOrDefault(v=>v.Value==(right?settings.RightClickMethod:settings.ClickMethod)),MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch};
        box.SelectionChanged+=(_,_)=>{if(!updating&&box.SelectedItem is Choice selected){settings=right?settings with{RightClickMethod=selected.Value}:settings with{ClickMethod=selected.Value};SaveSettings();}};
        settingControls.Add(s=>box.SelectedItem=choices.FirstOrDefault(v=>v.Value==(right?s.RightClickMethod:s.ClickMethod)));return box;
    }
    FrameworkElement Practice(){
        var content=Stack(8);content.Children.Add(Text("Try your clicks here",18));var result=Text("Left: rest here. Right: hold a wink, then reopen.",14,Muted);content.Children.Add(result);int left=0,right=0;
        var target=Button("◎  Practice target",()=>result.Text=$"Left clicks: {++left}   Right clicks: {right}",true);target.MinHeight=68;
        var menu=new MenuFlyout();menu.Items.Add(new MenuFlyoutItem{Text="✓ Right-click detected"});target.ContextFlyout=menu;target.RightTapped+=(_,_)=>result.Text=$"Left clicks: {left}   Right clicks: {++right}";content.Children.Add(target);return Card(content);
    }
    FrameworkElement Slider(string label,double min,double max,double value,Action<double> set)
    {
        var slider=new Slider{Header=label,Minimum=min,Maximum=max,Value=value,StepFrequency=.01};AutomationProperties.SetName(slider,label);
        slider.ValueChanged+=(_,e)=>{if(!updating){set(e.NewValue);SaveSettings();}};
        settingControls.Add(s=>slider.Value=label switch{"Right-click hold"=>s.RightClickHold,"Dwell time"=>s.DwellSeconds,"Smile strength"=>s.SmileThreshold,"Mouth strength"=>s.MouthThreshold,"Blink strength"=>s.BlinkThreshold,"Cursor speed"=>s.Sensitivity,"Screen reach"=>s.RelativeTravelBoost,"Maximum movement speed (pixels/second)"=>s.RelativeSpeedLimit>0?s.RelativeSpeedLimit:550,"Position horizontal range (degrees)"=>s.PositionHorizontalRange*180/Math.PI,"Position upward range (degrees)"=>s.PositionUpRange*180/Math.PI,"Position downward range (degrees)"=>s.PositionDownRange*180/Math.PI,"Responsiveness"=>s.Responsiveness,"Rest zone"=>s.Deadzone,_=>value});return slider;
    }
    void SaveSettings()=>connection.Send(new("settings",Settings:settings.Validate()));
    async Task Refresh()
    {
        connection.Send(new("heartbeat"));
        if(refreshing)return;refreshing=true;
        try {
        if(connection.Error is not null){state.Text="Stopped";SetPresentation(new("Stopped","Control is safely stopped",connection.Error,0,0,0,false,"",0,0,"Esc",[],null,[],"Microphone off"));return;}
        EngineStatus? s=Volatile.Read(ref connection.Latest);if(s is null)return;
        if(assessmentFocusHost.Visibility!=Visibility.Visible && ReferenceEquals(pageHost.Content,control) && s.Preview is not null && s.Preview!=lastPreview && previewSchedule.Due(Arguments.Now,s.CurrentSettings?.PreviewFps??30)) {
            if(lastPreview is null)Diagnostic.Write("First preview received");
            lastPreview=s.Preview;byte[] bytes=Convert.FromBase64String(s.Preview);
            using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)){writer.WriteBytes(bytes);await writer.StoreAsync();writer.DetachStream();}
            stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);preview.Source=bitmap;previewPlaceholder.Visibility=Visibility.Collapsed;
        }
        if(timer.Interval.TotalMilliseconds!=(s.CurrentSettings?.TrackingFps==60?16:33))timer.Interval=TimeSpan.FromMilliseconds(s.CurrentSettings?.TrackingFps==60?16:33);
        if(ReferenceEquals(s,shown)||s.State==shown?.State&&Arguments.Now-lastUiUpdate<.1)return;lastUiUpdate=Arguments.Now;
        if(shown?.State!=s.State)Diagnostic.Write("State: "+s.State+"; "+s.Title+"; "+s.Help);shown=s;
        state.Text=s.State;title.Text=s.Title;help.Text=s.Help;face.Text=s.Face?"Face found":"Bring your face into view";distance.Text=s.Distance;metrics.Text=$"{s.Fps:F0} tracking FPS · {s.InferenceMs:F0} ms inference · {s.LatencyMs:F0} ms processing";shortcut.Text=$"{s.StopShortcut} · {s.CaptureFps:F0} camera / {s.Fps:F0} tracking FPS"+(s.MovementRecording?.Recording==true?" · Recording motion":"");voiceText.Text=s.Voice;UpdateMovementRecording(s);UpdateAssessment(s);
        SetPresentation(s);faceGuide.Update(s.Position??new("find","Bring your face into the oval",false),s.Cue,s.DetectedFace);coach.Update(s.Cue,s.CurrentSettings?.ClickMethod??settings.ClickMethod);clickFeedback.Text=s.State=="Tracking"?s.ClickFeedback:s.State=="LearnClick"?"Follow the gesture lesson above":s.State is "Starting" or "Ready" or "Positioning" or "Calibrating"?"Control starts automatically after setup and a short rest":"Say resume, or choose Resume, to start control";rightProgress.Value=s.RightClickProgress*100;
        var current=s.CurrentSettings??settings;
        eyeFeedback.Text=current.EyeActionsUnavailable?"Eye actions are off. Choose a click action in Mouse tools.":current.RightClickMethod=="none"?"Right click is off":!current.EyesLearned?"Learn your wink / both eyes before using right click":s.RightClickProgress>=1?"Reopen now to right-click":s.RightClickProgress>0?"Keep closed until the bar fills":"Wink or close both eyes, hold, then reopen";
        routineReview.Text=s.Title+"\n"+s.Help;
        calibration.Value=s.Progress*100;dwell.Value=s.Dwell*100;AutomationProperties.SetName(dwell,$"Dwell click {s.Dwell:P0}");
        palette.Update(s.MouseMode,s.Dragging);mouseMode.Text=s.HeadScrolling?"Scroll mode · move head up/down":s.Dragging?"Holding item — click to drop":"Mode: "+MouseActions.Modes.First(m=>m.Id==s.MouseMode).Label;mouseHelp.Text=s.MouseHelp;UpdateSimpleFeedback(s);dock?.Update(s);
        UpdateAdjustmentPanel(s);
        if(s.DockToggleSequence!=seenDockToggle){seenDockToggle=s.DockToggleSequence;dock?.TogglePanel();}
        var zones=ReferenceEquals(pageHost.Content,control)?palette.Zones(this,"main").ToList():new List<MouseZone>();
        if(MousePalette.Zone(this,stopButton,"main:stop") is {} stopZone)zones.Add(stopZone);
        if(dock is not null){zones.AddRange(dock.Palette.Zones(dock,"dock"));if(MousePalette.Zone(dock,dock.Halt,"dock:stop") is {} dockStop)zones.Add(dockStop);if(MousePalette.Zone(dock,dock.Handle,"dock:toggle") is {} handleZone)zones.Add(handleZone);}
        connection.Send(new("mouseZones",Zones:zones.ToArray()));
        updating=true;
        try {
            settings=s.CurrentSettings??settings;foreach(var update in settingControls)update(settings);microphone.IsOn=s.RoutineVoiceEnabled;
            string[] ids=s.Cameras.Select(d=>d.Id).ToArray();if(!ids.SequenceEqual(lastCameras)){lastCameras=ids;camera.ItemsSource=s.Cameras;if(s.Cameras.Length==1)camera.SelectedIndex=0;}
            if(!s.Routines.SequenceEqual(lastRoutines)){lastRoutines=s.Routines;routineList.ItemsSource=s.Routines;}
        }finally{updating=false;}
        speech.Enabled=settings.SpokenGuidance&&!Environment.GetCommandLineArgs().Contains("--test-source");
        UpdateTutorial(s);
        Guide(s);
        if(s.State=="ChooseCamera" && s.Cameras.Length>1 && !cameraDialogShown && tutorialDialog is null){cameraDialogShown=true;_ = ShowCameraPicker(s.Cameras);}
        if(s.State!="ChooseCamera")cameraDialogShown=false;
        }catch(Exception e){Diagnostic.Error("Refresh",e);connection.Send(new("stop"));state.Text="Stopped";help.Text="Qadr could not update the display. Close and reopen the app.";}finally{refreshing=false;}
    }
}


