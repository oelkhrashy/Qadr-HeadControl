using Qadr.Core;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly:SupportedOSPlatform("windows")]

namespace Qadr.MovementLab;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        NativeInput.EnablePhysicalPixels();
        var app=new System.Windows.Application();
        if(args.Length==2&&args[0]=="--self-test"){LabSimulation.Run(args[1]);return;}
        if(args.Length==2&&args[0]=="--preview")
        {
            var window=new LabWindow(true){Width=1280,Height=900,WindowState=WindowState.Normal};
            var visual=(FrameworkElement)window.Content;
            visual.Measure(new Size(1280,900));visual.Arrange(new Rect(0,0,1280,900));visual.UpdateLayout();
            window.DrawPreview();window.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1280,900,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(args[1]);encoder.Save(stream);
            return;
        }
        app.Run(new LabWindow(false));
    }
}
