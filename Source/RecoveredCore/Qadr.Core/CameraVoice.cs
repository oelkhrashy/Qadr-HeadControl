using System;
using System.Globalization;
using System.Linq;

namespace Qadr.Core;

public static class CameraVoice
{
	private static readonly string[] Numbers = new string[20]
	{
		"one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
		"eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"
	};

	public static string Number(int index)
	{
		if (index < 0 || index >= Numbers.Length)
		{
			return (index + 1).ToString(CultureInfo.InvariantCulture);
		}
		return Numbers[index];
	}

	public static string[] Phrases(int count)
	{
		return Enumerable.Range(0, Math.Clamp(count, 0, Numbers.Length)).SelectMany((int i) => new string[2]
		{
			"camera " + Number(i),
			"choose camera " + Number(i)
		}).Append("computer stop")
			.ToArray();
	}

	public static int Resolve(string text, int count)
	{
		string text2 = string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
		if (text2.StartsWith("choose ", StringComparison.Ordinal))
		{
			text2 = text2.Substring(7);
		}
		if (!text2.StartsWith("camera ", StringComparison.Ordinal))
		{
			return -1;
		}
		string text3 = text2.Substring(7);
		int num = Array.IndexOf(Numbers, text3);
		if (num < 0 && int.TryParse(text3, out var result))
		{
			num = result - 1;
		}
		if (num < 0 || num >= Math.Min(count, Numbers.Length))
		{
			return -1;
		}
		return num;
	}
}
