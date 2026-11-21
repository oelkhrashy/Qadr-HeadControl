namespace Qadr.Core;

public sealed record MouseModeDefinition(string Id,string Label,string Hint);
public sealed record MouseStep(string Kind,string Button="left",int Amount=0,double Delay=0);
public sealed record MousePlan(string Feedback,MouseStep[] Steps);

/// <summary>Pure mouse action policy. Windows injection belongs to the Engine.</summary>
public sealed class MouseActions
{
    public static readonly MouseModeDefinition[] Modes=[
        new("left","Left click","Rest or gesture on the target to left-click."),
        new("right","Right click","Your next click opens the target's context menu."),
        new("double","Double-click","Your next click performs a double-click, then returns to left click."),
        new("drag","Drag / drop","Click the item to hold it, move to its destination, then click again to drop."),
        new("middle","Middle click","Your next click presses the mouse wheel button."),
        new("scroll-up","↑ Scroll up","Rest on content to scroll repeatedly. Move to stop; choose Left click to leave scroll mode."),
        new("scroll-down","↓ Scroll down","Rest on content to scroll repeatedly. Move to stop; choose Left click to leave scroll mode."),
        new("scroll-left","← Scroll left","Rest on content to scroll sideways. Move to stop; choose Left click to leave scroll mode."),
        new("scroll-right","→ Scroll right","Rest on content to scroll sideways. Move to stop; choose Left click to leave scroll mode."),
        new("back","Back","Your next click sends the mouse Back button to the application."),
        new("forward","Forward","Your next click sends the mouse Forward button to the application.")];
    public string Mode {get;private set;}="left";
    public bool Dragging {get;private set;}
    public bool Scrolling=>Mode.StartsWith("scroll-",StringComparison.Ordinal);
    public string Help=>Dragging?"Dragging — move to the destination and click to drop. Esc releases the button.":Modes.First(m=>m.Id==Mode).Hint;
    public bool Select(string id){if(!Modes.Any(m=>m.Id==id))return false;Mode=id;return true;}
    public void Reset(){Mode="left";Dragging=false;}
    public MousePlan Trigger(string source,int notches=1,double doubleGap=.06)
    {
        if(Dragging){Reset();return new("Dropped — left click",[new("up")]);}
        string action=source is "right" or "double" or "drag"?source:Mode;
        if(action=="drag"){Dragging=true;return new("Dragging — click again to drop",[new("down")]);}
        if(action.StartsWith("scroll-",StringComparison.Ordinal)){
            bool horizontal=action is "scroll-left" or "scroll-right";
            int direction=action is "scroll-up" or "scroll-right"?1:-1;
            return new(action.Replace('-',' '),[new(horizontal?"horizontal":"vertical",Amount:direction*Math.Clamp(notches,1,5))]);
        }
        Mode="left";
        string button=action is "right" or "middle" or "back" or "forward"?action:"left";
        if(action=="double")return new("Double-click",[new("down"),new("up"),new("down",Delay:doubleGap),new("up",Delay:doubleGap)]);
        return new(button+" click",[new("down",button),new("up",button)]);
    }
}
