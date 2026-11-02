using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Qadr.Core;

public sealed class Storage(string directory)
{
	private sealed record Envelope(int Schema, string Payload, string Hash);

	public string DirectoryPath { get; } = Path.GetFullPath(directory);

	public string? LastWarning { get; private set; }

	public T? Load<T>(string name)
	{
		string text = SafePath(name);
		string[] array = new string[2]
		{
			text,
			text + ".bak"
		};
		foreach (string text2 in array)
		{
			if (!File.Exists(text2))
			{
				continue;
			}
			try
			{
				Envelope envelope = Protocol.Decode<Envelope>(File.ReadAllText(text2));
				if (envelope.Schema != 1 || !envelope.Hash.Equals(Hash(envelope.Payload), StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Unsupported version or damaged data");
				}
				return Protocol.Decode<T>(envelope.Payload);
			}
			catch (Exception ex) when ((ex is IOException || ex is JsonException || ex is InvalidDataException || ex is ArgumentException) ? true : false)
			{
				LastWarning = "A saved file was damaged. Qadr used its backup or safe defaults.";
				try
				{
					File.Copy(text2, text2 + ".quarantine-" + DateTime.UtcNow.Ticks, overwrite: false);
				}
				catch (IOException)
				{
				}
			}
		}
		return default;
	}

	public void Save<T>(string name, T value)
	{
		string text = SafePath(name);
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		string text2 = Protocol.Encode(value);
		string text3 = text + ".tmp";
		using (FileStream fileStream = new FileStream(text3, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
		{
			byte[] bytes = Encoding.UTF8.GetBytes(Protocol.Encode(new Envelope(1, text2, Hash(text2))));
			fileStream.Write(bytes);
			fileStream.Flush(flushToDisk: true);
		}
		if (File.Exists(text))
		{
			try
			{
				Envelope envelope = Protocol.Decode<Envelope>(File.ReadAllText(text));
				if (envelope.Schema == 1 && envelope.Hash == Hash(envelope.Payload))
				{
					File.Copy(text, text + ".bak.tmp", overwrite: true);
					File.Move(text + ".bak.tmp", text + ".bak", overwrite: true);
				}
			}
			catch (JsonException)
			{
			}
		}
		File.Move(text3, text, overwrite: true);
	}

	public string[] Names(string subfolder)
	{
		if (!Directory.Exists(SafePath(subfolder)))
		{
			return Array.Empty<string>();
		}
		return Directory.GetFiles(SafePath(subfolder), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
			.ToArray();
	}

	public static string DeviceKey(string id)
	{
		return Hash(id).Substring(0, 16);
	}

	private string SafePath(string name)
	{
		string fullPath = Path.GetFullPath(Path.Combine(DirectoryPath, name));
		if (!fullPath.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Invalid storage path");
		}
		return fullPath;
	}

	private static string Hash(string value)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
	}
}
