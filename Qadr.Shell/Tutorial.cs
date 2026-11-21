using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qadr.Core;

namespace Qadr.Shell;
internal sealed partial class MainWindow
{
    ContentDialog? tutorialDialog;
    readonly TextBlock tutorialStep=Text("",14),tutorialTitle=Text("",25),tutorialSymbol=Text("",42,Emerald),tutorialVisual=Text("",20,Emerald),tutorialInstruction=Text("",18),tutorialTip=Text("",15,Muted),tutorialVoiceStatus=Text("",14,Muted);
    string? tutorialPhase;
    int tutorialPromptGeneration;
    Grid TutorialContent(){
        var outer=new Grid{MaxWidth=560,RowSpacing=8};outer.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});outer.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var stack=new StackPanel{Spacing=8};stack.Children.Add(tutorialStep);stack.Children.Add(tutorialTitle);
        tutorialSymbol.FontSize=30;tutorialVisual.FontSize=17;tutorialInstruction.FontSize=16;tutorialTip.FontSize=14;
        stack.Children.Add(new Border{Background=Pale,CornerRadius=new(16),Padding=new Thickness(12),Child=new StackPanel{Spacing=4,Children={tutorialSymbol,tutorialVisual}}});
        stack.Children.Add(tutorialInstruction);stack.Children.Add(tutorialTip);stack.Children.Add(tutorialVoiceStatus);
        outer.Children.Add(new ScrollViewer{Content=stack,MaxHeight=300,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var actions=new Grid{ColumnSpacing=8};actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});actions.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var skip=Button("Start now",()=>{StopTutorialPrompt();connection.Send(new("tutorialSkip"));});actions.Children.Add(skip);
        var again=Button("Listen again",()=>{tutorialPhase=null;if(shown is not null)UpdateTutorial(shown);});Grid.SetColumn(again,1);actions.Children.Add(again);Grid.SetRow(actions,1);outer.Children.Add(actions);return outer;
    }
    void RenderTutorial(bool choice,int page){
        tutorialStep.Text=choice?"Welcome · optional tutorial":$"Step {page+1} of {TutorialFlow.Pages.Length}";
        var p=TutorialFlow.Pages[page];tutorialTitle.Text=choice?"Would you like a tutorial?":p.Title;
        tutorialSymbol.Text=choice?"?":p.Symbol;tutorialVisual.Text=choice?"Yes → guided tour     No → camera setup":p.Visual;
        tutorialInstruction.Text=choice?"Say yes for a short guided tour, or no to go straight to camera selection and calibration. You can also choose with the buttons below.":p.Instruction;
        tutorialTip.Text=choice?"Mouse control stays stopped while you decide. There is no time limit. Esc stops setup.":p.Tip;
    }
    void UpdateTutorial(EngineStatus s){
        bool active=s.State is "TutorialChoice" or "Tutorial";
        if(!active){tutorialPhase=null;tutorialPromptGeneration++;if(tutorialDialog is not null){speech.Cancel();tutorialDialog.Hide();}return;}
        bool choice=s.State=="TutorialChoice";int page=TutorialFlow.ReadPage(s.Cue);string key=s.State+":"+page;
        tutorialVoiceStatus.Text=s.Voice=="Listening for tutorial choice"?"Listening · say yes or no":s.Voice=="Listening for tutorial navigation"?"Listening · say next, back, or start":s.Voice.Contains("unavailable",StringComparison.OrdinalIgnoreCase)?"Microphone unavailable · use the buttons below":speech.Error??"Spoken and manual guidance";
        if(key==tutorialPhase)return;tutorialPhase=key;
        if(tutorialDialog is null){
            var buttons=new Style(typeof(Button)){Setters={new Setter(FrameworkElement.MinHeightProperty,48d),new Setter(Control.FontSizeProperty,16d)}};
            tutorialDialog=new ContentDialog{Title="Qadr guide",Content=TutorialContent(),XamlRoot=((FrameworkElement)Content).XamlRoot,CloseButtonText="Stop · Esc",PrimaryButtonStyle=buttons,SecondaryButtonStyle=buttons,CloseButtonStyle=buttons};
            tutorialDialog.PrimaryButtonClick+=(_,e)=>{e.Cancel=true;if(tutorialDialog is not null)tutorialDialog.IsPrimaryButtonEnabled=false;StopTutorialPrompt();connection.Send(new(shown?.State=="TutorialChoice"?"tutorialStart":"tutorialNext"));};
            tutorialDialog.SecondaryButtonClick+=(_,e)=>{e.Cancel=true;if(tutorialDialog is not null)tutorialDialog.IsPrimaryButtonEnabled=false;StopTutorialPrompt();connection.Send(new(shown?.State=="TutorialChoice"?"tutorialSkip":"tutorialBack"));};
            tutorialDialog.CloseButtonClick+=(_,_)=>{StopTutorialPrompt();connection.Send(new("stop"));};
            _=ShowTutorialDialog(tutorialDialog);
        }
        RenderTutorial(choice,page);tutorialDialog.IsPrimaryButtonEnabled=true;tutorialDialog.PrimaryButtonText=choice?"Yes · show tutorial":page==TutorialFlow.Pages.Length-1?"Start camera setup":"Next";
        tutorialDialog.SecondaryButtonText=choice?"No · start now":"Back";tutorialDialog.IsSecondaryButtonEnabled=choice||page>0;
        _=PromptTutorial(s,key);
    }
    async Task ShowTutorialDialog(ContentDialog dialog){try{await dialog.ShowAsync();}catch(Exception e){Diagnostic.Write("Tutorial: "+e.Message);connection.Send(new("stop"));}finally{if(ReferenceEquals(tutorialDialog,dialog))tutorialDialog=null;}}
    void StopTutorialPrompt(){tutorialPromptGeneration++;speech.Cancel();connection.Send(new("tutorialVoice",Enabled:false));}
    async Task PromptTutorial(EngineStatus s,string key){
        int generation=++tutorialPromptGeneration;connection.Send(new("tutorialVoice",Enabled:false));
        bool choice=s.State=="TutorialChoice";var p=TutorialFlow.Pages[TutorialFlow.ReadPage(s.Cue)];
        await speech.Say(choice?"Welcome to Qadr. Would you like a tutorial? After this prompt, say yes for the tutorial, or no to start camera setup immediately. You can also choose manually.":p.Title+". "+p.Instruction+". "+p.Tip+". Say next to continue, back to repeat the previous step, or start to begin camera setup.");
        if(generation==tutorialPromptGeneration&&!closed&&tutorialPhase==key&&tutorialDialog is not null)connection.Send(new("tutorialVoice",Enabled:true));
    }
}
