using System.IO;
using System.Text.Json;

namespace Qadr.Core;

public static class Protocol
{
	public const int Version = 1;

	public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public static string Encode<T>(T message)
	{
		return JsonSerializer.Serialize(message, Json);
	}

	public static T Decode<T>(string message)
	{
		T val = JsonSerializer.Deserialize<T>(message, Json);
		if (val == null)
		{
			throw new InvalidDataException("Empty message");
		}
		return val;
	}
}
