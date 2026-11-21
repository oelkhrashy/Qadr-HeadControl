using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Qadr.Shell;

internal sealed partial class MainWindow
{
    Border CursorComfortSettings(){
        var content=Stack(10);content.Children.Add(Text("Cursor speed and screen reach",22));
        var grid=new Grid{ColumnSpacing=24};grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var speed=Stack(8);var reach=Stack(8);Grid.SetColumn(reach,1);grid.Children.Add(speed);grid.Children.Add(reach);
        var speedValue=Text($"Speed: {settings.Sensitivity*100:F0}%",18);speed.Children.Add(speedValue);
        settingControls.Add(s=>speedValue.Text=$"Speed: {s.Sensitivity*100:F0}%");
        speed.Children.Add(Slider("Cursor speed",.3,2.5,settings.Sensitivity,v=>settings=settings with{Sensitivity=v}));
        var buttons=Stack(8);buttons.Orientation=Orientation.Horizontal;
        buttons.Children.Add(Button("Slower −",()=>{settings=settings with{Sensitivity=Math.Max(.3,Math.Round(settings.Sensitivity-.05,2))};SaveSettings();}));
        buttons.Children.Add(Button("Faster +",()=>{settings=settings with{Sensitivity=Math.Min(2.5,Math.Round(settings.Sensitivity+.05,2))};SaveSettings();}));speed.Children.Add(buttons);
        speed.Children.Add(Text("Lower = slower. 100% is standard. Saved automatically.",14,Muted));
        var reachValue=Text($"Reach: {settings.RelativeTravelBoost:F2}×",18);reach.Children.Add(reachValue);
        settingControls.Add(s=>reachValue.Text=$"Reach: {s.RelativeTravelBoost:F2}×");
        reach.Children.Add(Slider("Screen reach",1,4,settings.RelativeTravelBoost,v=>settings=settings with{RelativeTravelBoost=v}));
        reach.Children.Add(Text("Increase reach for longer deliberate travel. Tiny corrections keep their slow speed. Settle to stop; Esc stops control.",14,Muted));
        content.Children.Add(grid);return Card(content);
    }
}
