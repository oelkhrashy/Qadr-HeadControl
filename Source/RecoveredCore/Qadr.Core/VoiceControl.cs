using System;
using System.Collections.Generic;
using System.Linq;

namespace Qadr.Core;

public static class VoiceControl
{
	public const uint StopVirtualKey = 27u;

	public static readonly string[] StopPhrases = new string[3] { "stop the app", "computer stop", "computer pause pointer" };

	public static readonly string[] ResumePhrases = new string[3] { "resume", "resume the app", "computer resume pointer" };

	public const string RecentrePhrase = "center pointer";

	public static readonly string[] MovementPhrases = new string[9] { "undo movement settings", "start motion test", "save motion test", "mark rest", "mark movement", "start pointing test", "skip target", "save pointing test", "center pointer" };

	public static string Normalize(string text)
	{
		return string.Join(' ', text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
	}

	public static bool IsStop(string text)
	{
		return StopPhrases.Contains(Normalize(text));
	}

	public static bool IsResume(string text)
	{
		return ResumePhrases.Contains(Normalize(text));
	}

	public static string[] Phrases(bool handsFree, bool routines, string[] names, string[]? phase = null)
	{
		IEnumerable<string> first;
		if (!handsFree)
		{
			IEnumerable<string> enumerable = Enumerable.Empty<string>();
			first = enumerable;
		}
		else
		{
			first = StopPhrases.Concat(ResumePhrases).Concat(MovementPhrases);
		}
		IEnumerable<string> first2 = first.Concat(phase ?? Array.Empty<string>());
		IEnumerable<string> second;
		if (!routines)
		{
			IEnumerable<string> enumerable = Enumerable.Empty<string>();
			second = enumerable;
		}
		else
		{
			second = new string[3] { "computer confirm", "computer start recording", "computer stop recording" }.Concat(names.Select((string n) => "computer run " + n));
		}
		return first2.Concat(second).Distinct().ToArray();
	}
}
