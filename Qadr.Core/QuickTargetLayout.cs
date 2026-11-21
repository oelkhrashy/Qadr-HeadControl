namespace Qadr.Core;

/// <summary>Five separated circles in logical units; never clamp top/bottom
/// onto the centre. Return no plan when the surface cannot fit accessible targets.</summary>
public sealed record QuickTargetPlan(double Size,AssessmentTarget[] Targets);
public static class QuickTargetLayout
{
    public static QuickTargetPlan? Create(double width,double height){
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<176||height<176)return null;
        double min=Math.Min(width,height),size=min>=296?88:min>=224?64:48;
        double left=8+size/2,top=8+size/2,right=width-left,bottom=height-top;
        (double X,double Y)[] centres=[(width/2,height/2),(left,height/2),(right,height/2),(width/2,top),(width/2,bottom)];
        return new(size,centres.Select((p,i)=>new AssessmentTarget(((char)('A'+i)).ToString(),"move",p.X-size/2,p.Y-size/2,size,size)).ToArray());
    }
    public static bool Comparable(IReadOnlyList<AssessmentTarget> targets){
        if(targets.Count!=5||targets.Any(t=>!t.Valid))return false;
        for(int i=0;i<5;i++)for(int j=i+1;j<5;j++){
            var a=targets[i];var b=targets[j];
            double dx=a.X+a.Width/2-b.X-b.Width/2,dy=a.Y+a.Height/2-b.Y-b.Height/2;
            double separation=(Math.Max(a.Width,a.Height)+Math.Max(b.Width,b.Height))/2;
            if(dx*dx+dy*dy<(separation+1)*(separation+1))return false;
        }
        var centre=targets[0];double cx=centre.X+centre.Width/2,cy=centre.Y+centre.Height/2;
        return targets[1].X+targets[1].Width/2<cx&&targets[2].X+targets[2].Width/2>cx&&targets[3].Y+targets[3].Height/2<cy&&targets[4].Y+targets[4].Height/2>cy;
    }
}
