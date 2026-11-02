using System;
using System.IO;
using Qadr.Core;

namespace Qadr.Engine;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		NativeInput.EnablePhysicalPixels();
		string text = Arguments.DataDirectory(args);
		try
		{
			using Controller controller = new Controller(args);
			controller.Run();
			return 0;
		}
		catch (Exception ex)
		{
			Directory.CreateDirectory(text);
			File.WriteAllText(Path.Combine(text, "engine-error.txt"), ex.ToString());
			return 1;
		}
	}
}
