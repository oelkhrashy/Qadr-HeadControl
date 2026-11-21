using Qadr.Core;

namespace Qadr.Shell;
internal static class Diagnostic
{
    public static void Error(string context,Exception error){
        try{
            string directory=Arguments.DataDirectory(Environment.GetCommandLineArgs());Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"shell-error.txt");
            if(File.Exists(path)&&new FileInfo(path).Length>262144)File.Move(path,path+".previous",true);
            File.AppendAllText(path,DateTime.UtcNow.ToString("O")+" "+context+"\n"+error+"\n");
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static void Write(string message){if(!Environment.GetCommandLineArgs().Contains("--diagnose"))return;
        try{string directory=Arguments.DataDirectory(Environment.GetCommandLineArgs());System.IO.Directory.CreateDirectory(directory);System.IO.File.AppendAllText(System.IO.Path.Combine(directory,"startup.txt"),DateTime.UtcNow.ToString("O")+" "+message+"\n");}catch(System.IO.IOException){}}
}
