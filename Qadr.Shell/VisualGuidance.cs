using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Qadr.Core;

namespace Qadr.Shell;

/// <summary>Coordinates share the preview's 480 by 360 viewbox, including letterboxing.</summary>
internal sealed class FaceGuide : Canvas
{
    readonly Ellipse targetEdge=new(){Stroke=new SolidColorBrush(Microsoft.UI.Colors.Black),StrokeThickness=7};
    readonly Ellipse target=new(){StrokeThickness=4};
    readonly Ellipse detectedEdge=new(){Stroke=new SolidColorBrush(Microsoft.UI.Colors.Black),StrokeThickness=5};
    readonly Ellipse detected=new(){Stroke=new SolidColorBrush(Microsoft.UI.Colors.White),StrokeThickness=2};
    readonly TextBlock arrow=new(){FontSize=44,FontWeight=Microsoft.UI.Text.FontWeights.Bold};
    readonly SolidColorBrush good,warning;
    public FaceGuide(SolidColorBrush good,SolidColorBrush warning){
        this.good=good;this.warning=warning;Width=480;Height=360;IsHitTestVisible=false;
        Children.Add(detectedEdge);Children.Add(detected);Children.Add(targetEdge);Children.Add(target);
        var chip=new Border{Background=new SolidColorBrush(Windows.UI.Color.FromArgb(235,8,43,33)),CornerRadius=new(10),Padding=new Thickness(8,0,8,0),Child=arrow};SetLeft(chip,12);SetTop(chip,12);Children.Add(chip);
        Update(new("find","",false),"position");
    }
    static void Place(Ellipse shape,FacePosition p){shape.Width=p.Width*480;shape.Height=p.Height*360;SetLeft(shape,p.X*480-shape.Width/2);SetTop(shape,p.Y*360-shape.Height/2);}
    public void Update(FacePosition p,string cue,FacePosition? actual=null){
        Place(targetEdge,p);Place(target,p);target.Stroke=p.Ready?good:warning;
        detected.Visibility=detectedEdge.Visibility=actual is null?Visibility.Collapsed:Visibility.Visible;
        if(actual is not null){Place(detectedEdge,actual);Place(detected,actual);}
        arrow.Foreground=p.Ready?good:warning;arrow.Text=p.Ready?"✓":p.Code switch{"right"=>"→","left"=>"←","up"=>"↑","down"=>"↓","closer"=>"＋","farther"=>"−","frame"=>"↔","angle"=>"↶",_=>"◎"};
        AutomationProperties.SetName(this,"Colored outline: learned target. White outline: detected face. "+p.Hint);
    }
}

internal sealed class VisualCoach : Grid
{
    readonly Canvas drawing=new(){Width=94,Height=106};
    readonly TextBlock heading=new(){FontSize=17,TextWrapping=TextWrapping.Wrap};
    readonly TextBlock detail=new(){FontSize=14,TextWrapping=TextWrapping.Wrap};
    readonly SolidColorBrush ink,pale;
    string? last;
    public VisualCoach(SolidColorBrush ink,SolidColorBrush pale){
        this.ink=ink;this.pale=pale;ColumnSpacing=12;
        ColumnDefinitions.Add(new(){Width=GridLength.Auto});ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        Children.Add(new Viewbox{Child=drawing,Width=160,Height=180,Stretch=Stretch.Uniform});var words=new StackPanel{Visibility=Visibility.Collapsed,Spacing=5,VerticalAlignment=VerticalAlignment.Center};heading.Foreground=detail.Foreground=ink;words.Children.Add(heading);words.Children.Add(detail);SetColumn(words,1);Children.Add(words);Update("position");
    }
    void Line(double x1,double y1,double x2,double y2){drawing.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=ink,StrokeThickness=3});}
    public void Update(string cue,string leftMethod="dwell"){if(cue+leftMethod==last)return;last=cue+leftMethod;drawing.Children.Clear();
        var head=new Ellipse{Width=60,Height=76,Fill=pale,Stroke=ink,StrokeThickness=2};Canvas.SetLeft(head,17);Canvas.SetTop(head,14);drawing.Children.Add(head);
        foreach(double x in new[]{34.0,58.0}){
            if(cue=="eyes"&&x==34)Line(x-5,42,x+5,42);
            else{var eye=new Ellipse{Width=6,Height=6,Fill=ink};Canvas.SetLeft(eye,x-3);Canvas.SetTop(eye,39);drawing.Children.Add(eye);}
        }
        if(cue=="smile"){
            var f=new PathFigure{StartPoint=new(31,62)};f.Segments.Add(new BezierSegment{Point1=new(36,77),Point2=new(57,77),Point3=new(62,62)});var g=new PathGeometry();g.Figures.Add(f);drawing.Children.Add(new Microsoft.UI.Xaml.Shapes.Path{Data=g,Stroke=ink,StrokeThickness=3});
        }else Line(37,66,57,66);
        var arrows=new TextBlock{FontSize=24,Foreground=ink,Text=cue switch{"sides"=>"←     →","vertical"=>"↑     ↓","control"=>"↖   ↗","eyes"=>"…  ✓","smile"=>"✓",_=>"◎"}};Canvas.SetTop(arrows,80);Canvas.SetLeft(arrows,9);drawing.Children.Add(arrows);
        string leftHint=leftMethod switch{"smile"=>"Smile gently for left-click.","mouth"=>"Open your mouth gently for left-click.","none"=>"Left clicks are disabled.",_=>"Rest on a target for left-click."};
        (heading.Text,detail.Text)=cue switch{
            "rest"=>("2  Relax","Settle in a comfortable position. Qadr uses your current position as the centre."),
            "sides"=>("3  Turn gently","← Left, then right →. Use only a comfortable range."),
            "vertical"=>("3  Look up and down","↑ Gently up, then down ↓. Small movements are enough."),
            "smile"=>("Learn a left-click","Smile gently, hold, then relax."),
            "eyes"=>("Learn a right-click","Close one eye or both. Hold briefly, then reopen."),
            "click"=>("4  Try your clicks",leftHint+" Hold a wink, then reopen for right-click."),
            "control"=>("You are in control",leftHint+" Esc stops."),
            "stop"=>("Control is stopped","Choose Resume when you are ready."),
            _=>("1  Get comfortable","Hold briefly. The oval fits your face and distance automatically.")};
        AutomationProperties.SetName(drawing,heading.Text+". "+detail.Text);
    }
}
