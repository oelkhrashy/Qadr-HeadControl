namespace Qadr.Core;

/// <summary>Prefer usable real-time formats near the existing 640x480 budget.
/// Unknown frame-rate metadata remains a fallback, never an invented guarantee.</summary>
public static class CaptureMode
{
 public static double Score(uint width,uint height,double fps,int targetFps=30){
  if(width<160||height<120||width>1920||height>1080||!double.IsFinite(fps)||fps<0)return double.NegativeInfinity;
  double known=fps>0?fps:15;
  double scale=Math.Min(1,Math.Min(640d/width,480d/height));
  double quality=width*scale*height*scale/(640*480);
  int target=targetFps==60?60:30;
  // Do not trade away most face detail just to display a higher FPS number.
  if(target==60&&quality<.5)known=Math.Min(known,24);
  double sourceCost=Math.Max(0,width*(double)height/(640*480)-1)*500;
  return(known>=target*.8?100000:Math.Min(known,target*.8)*1000)+quality*10000+Math.Min(known,target)*100-Math.Max(0,known-target)*5-sourceCost;
 }
}
