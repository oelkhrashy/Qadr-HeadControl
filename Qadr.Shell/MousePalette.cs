using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Qadr.Core;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace Qadr.Shell;

internal sealed class MousePalette:Grid
{
    readonly List<(Button Button,string Id,string Label)> buttons=[];
    readonly SolidColorBrush emerald,pale,ink,onPrimary;
    (string Mode,bool Dragging)? previous;
    public MousePalette(int columns,Action<string> select,SolidColorBrush emerald,SolidColorBrush pale,SolidColorBrush ink,SolidColorBrush onPrimary,bool primaryOnly=false){
        this.emerald=emerald;this.pale=pale;this.ink=ink;this.onPrimary=onPrimary;ColumnSpacing=8;RowSpacing=8;
        for(int c=0;c<columns;c++)ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var modes=MouseActions.Modes.Select(m=>(m.Id,m.Label,m.Hint)).Append(("release","Release / left","Release a held item at the current position and return to left click.")).ToArray();
        if(columns==4){string[] order=["left","right","double","middle","drag","release","back","forward","scroll-left","scroll-up","scroll-down","scroll-right"];modes=order.Select(id=>modes.First(m=>m.Item1==id)).ToArray();}
        if(primaryOnly)modes=modes.Where(m=>m.Item1 is "left" or "right" or "double" or "drag").ToArray();
        for(int r=0;r<(modes.Length+columns-1)/columns;r++)RowDefinitions.Add(new(){Height=new GridLength(50)});
        for(int i=0;i<modes.Length;i++){
            var (id,label,hint)=modes[i];
            var button=new Button{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center},FontSize=16,MinHeight=48,MinWidth=48,Padding=new Thickness(5,3,5,3),CornerRadius=new(10),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=pale,Foreground=ink};
            AutomationProperties.SetName(button,label);AutomationProperties.SetHelpText(button,hint);ToolTipService.SetToolTip(button,hint);
            button.TabIndex=2;
            button.Click+=(_,_)=>select(id);SetColumn(button,i%columns);SetRow(button,i/columns);Children.Add(button);buttons.Add((button,id,label));
        }
    }
    public void Update(string mode,bool dragging){if(previous==(mode,dragging))return;previous=(mode,dragging);foreach(var (button,id,label) in buttons){bool selected=mode==id;button.Background=selected?emerald:pale;button.BorderBrush=selected?emerald:pale;button.BorderThickness=new Thickness(1);AutomationProperties.SetItemStatus(button,selected?"Selected":"Not selected");button.Foreground=selected?onPrimary:ink;((TextBlock)button.Content).Text=(selected?"✓ ":"")+(dragging&&id=="drag"?"Drop item":label);}}
    public Button[] Targets=>buttons.Select(b=>b.Button).ToArray();
    public MouseZone[] Zones(Window window,string prefix){
        if(window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter{State:Microsoft.UI.Windowing.OverlappedPresenterState.Minimized})return [];
        return buttons.Select(b=>Zone(window,b.Button,prefix+":"+b.Id)).OfType<MouseZone>().ToArray();
    }
    public static MouseZone? Zone(Window window,FrameworkElement button,string id){
        if(!button.IsLoaded||button is Control control&&!control.IsEnabled||button.ActualWidth<=0||button.Visibility!=Visibility.Visible)return null;for(DependencyObject? parent=button;parent is not null;parent=VisualTreeHelper.GetParent(parent))if(parent is UIElement element&&element.Visibility!=Visibility.Visible)return null;
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(window);var origin=new ClientPoint();if(!ClientToScreen(handle,ref origin))return null;
        var position=button.TransformToVisual((UIElement)window.Content).TransformPoint(new Windows.Foundation.Point());double scale=button.XamlRoot?.RasterizationScale??1;
        var root=(FrameworkElement)window.Content;if(position.X<0||position.Y<0||position.X+button.ActualWidth>root.ActualWidth+.5||position.Y+button.ActualHeight>root.ActualHeight+.5)return null;
        for(DependencyObject? parent=button;parent is not null;parent=VisualTreeHelper.GetParent(parent))if(parent is ScrollViewer scroll){var inside=button.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());if(inside.X<0||inside.Y<0||inside.X+button.ActualWidth>scroll.ActualWidth+.5||inside.Y+button.ActualHeight>scroll.ActualHeight+.5)return null;}
        return new(id,origin.X+position.X*scale,origin.Y+position.Y*scale,button.ActualWidth*scale,button.ActualHeight*scale,handle.ToInt64());
    }
    [StructLayout(LayoutKind.Sequential)] struct ClientPoint{public int X,Y;}
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint window,ref ClientPoint point);
}

internal sealed class MouseDock:Window
{
    public readonly MousePalette Palette;
    public readonly Button Halt;
    public readonly Button Handle;
    readonly TextBlock mode=new(){FontSize=14,TextWrapping=TextWrapping.Wrap};
    readonly Grid root,tools;
    readonly Microsoft.UI.WindowId anchor;
    DockBounds? lastBounds;
    public bool Expanded {get;private set;}
    public MouseDock(Action<string> select,Action stop,SolidColorBrush emerald,SolidColorBrush pale,SolidColorBrush ink,SolidColorBrush onPrimary,SolidColorBrush surface,Microsoft.UI.WindowId anchor){
        this.anchor=anchor;Title="Qadr edge mouse tools";
        try{AppWindow.IsShownInSwitchers=false;}catch(NotImplementedException){Diagnostic.Write("Floating tools: switcher visibility is unavailable on this Windows build");}
        if(AppWindow.Presenter is OverlappedPresenter presenter){presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.SetBorderAndTitleBar(false,false);}
        root=new Grid{Padding=new Thickness(4),Background=surface,ColumnSpacing=8};root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});root.ColumnDefinitions.Add(new(){Width=new GridLength(48)});
        tools=new Grid{RowSpacing=8,Visibility=Visibility.Collapsed};tools.RowDefinitions.Add(new(){Height=GridLength.Auto});tools.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});mode.Foreground=ink;tools.Children.Add(mode);
        Palette=new(1,select,emerald,pale,ink,onPrimary);var scrolling=new ScrollViewer{Content=Palette,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scrolling,1);tools.Children.Add(scrolling);root.Children.Add(tools);
        var tab=new StackPanel{Spacing=8,VerticalAlignment=VerticalAlignment.Center};Handle=new Button{Content="‹",MinHeight=48,MinWidth=48,FontSize=26,HorizontalAlignment=HorizontalAlignment.Stretch,Background=emerald,Foreground=onPrimary};Handle.Click+=(_,_)=>TogglePanel();Handle.TabIndex=1;tab.Children.Add(Handle);
        Halt=new Button{Content=new TextBlock{Text="■\nEsc",TextAlignment=TextAlignment.Center},MinHeight=48,MinWidth=48,FontSize=14,Padding=new Thickness(2),HorizontalAlignment=HorizontalAlignment.Stretch,Background=new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast?emerald:new SolidColorBrush(Windows.UI.Color.FromArgb(255,153,27,27)),Foreground=onPrimary};Halt.TabIndex=0;Halt.Click+=(_,_)=>stop();AutomationProperties.SetName(Halt,"Stop control · Esc");tab.Children.Add(Halt);Grid.SetColumn(tab,1);root.Children.Add(tab);Content=root;
        root.Loaded+=(_,_)=>{ArrangeEdge();root.XamlRoot.Changed+=(_,_)=>ArrangeEdge();};UpdateHandle();
    }
    void UpdateHandle(){Handle.Content=Expanded?"›":"‹";AutomationProperties.SetName(Handle,Expanded?"Hide mouse tools":"Show mouse tools");tools.Visibility=Expanded?Visibility.Visible:Visibility.Collapsed;root.ColumnDefinitions[0].Width=Expanded?new GridLength(1,GridUnitType.Star):new GridLength(0);root.ColumnSpacing=Expanded?8:0;}
    public void TogglePanel(){Expanded=!Expanded;UpdateHandle();ArrangeEdge();}
    void ArrangeEdge(){var area=DisplayArea.GetFromWindowId(anchor,DisplayAreaFallback.Primary).WorkArea;var bounds=EdgeDockLayout.Calculate(area.X,area.Y,area.Width,area.Height,root.XamlRoot?.RasterizationScale??1,Expanded);if(lastBounds==bounds)return;lastBounds=bounds;AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(bounds.X,bounds.Y,bounds.Width,bounds.Height));}
    public void Update(EngineStatus status){Palette.Update(status.MouseMode,status.Dragging);mode.Text=status.HeadScrolling?"Head scrolling · repeat mouth gesture to exit":status.Dragging?"Holding item — click to drop":"Mode: "+MouseActions.Modes.First(m=>m.Id==status.MouseMode).Label;}
}
