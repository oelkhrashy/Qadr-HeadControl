namespace Qadr.Core;

public readonly record struct DockBounds(int X,int Y,int Width,int Height);
public static class EdgeDockLayout
{
    public static DockBounds Calculate(int x,int y,int width,int height,double scale,bool expanded){
        scale=double.IsFinite(scale)?Math.Clamp(scale,.75,3):1;
        int margin=(int)Math.Ceiling(8*scale);
        int w=Math.Min(Math.Max(48,width-2*margin),(int)Math.Ceiling((expanded?240:136)*scale));
        int h=expanded?Math.Max(96,height-2*margin):Math.Min(Math.Max(96,height-2*margin),(int)Math.Ceiling(112*scale));
        return new(x+width-margin-w,y+(height-h)/2,w,h);
    }
}
