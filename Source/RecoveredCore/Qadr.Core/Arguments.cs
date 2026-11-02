using System;
using System.Diagnostics;
using System.IO;

namespace Qadr.Core;

public static class Arguments
{
	public static double Now => (double)Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

	public static string? Get(string[] args, string key)
	{
		int num = Array.IndexOf(args, key);
		if (num < 0 || num + 1 >= args.Length)
		{
			return null;
		}
		return args[num + 1];
	}

	public static string DataDirectory(string[] args)
	{
		// Keep the portable learning build separate from other Qadr copies.
		return Get(args, "--data-dir") ?? Path.Combine(AppContext.BaseDirectory, "Data");
	}
}
