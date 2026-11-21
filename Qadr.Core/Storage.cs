using System.Security.Cryptography;
using System.Text;

namespace Qadr.Core;

public sealed class Storage(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? LastWarning { get; private set; }
    public T? Load<T>(string name)
    {
        string path = SafePath(name);
        foreach (string candidate in new[] { path, path + ".bak" }) {
            if (!File.Exists(candidate)) continue;
            try {
                var envelope = Protocol.Decode<Envelope>(File.ReadAllText(candidate));
                if (envelope.Schema != 1 || !envelope.Hash.Equals(Hash(envelope.Payload), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsupported version or damaged data");
                return Protocol.Decode<T>(envelope.Payload);
            } catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException) {
                LastWarning = "A saved file was damaged. Qadr used its backup or safe defaults.";
                try { File.Copy(candidate, candidate + ".quarantine-" + DateTime.UtcNow.Ticks, false); } catch (IOException) { }
            }
        }
        return default;
    }
    public void Save<T>(string name, T value)
    {
        string path = SafePath(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string payload = Protocol.Encode(value), temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
            byte[] bytes = Encoding.UTF8.GetBytes(Protocol.Encode(new Envelope(1, payload, Hash(payload))));
            stream.Write(bytes); stream.Flush(true);
        }
        if (File.Exists(path)) {
            // Rename within the same directory is atomic and does not require
            // copying the old file's ACL, unlike ReplaceFile on restricted PCs.
            try {
                var old=Protocol.Decode<Envelope>(File.ReadAllText(path));
                if(old.Schema==1 && old.Hash==Hash(old.Payload)) {
                    File.Copy(path,path+".bak.tmp",true);File.Move(path+".bak.tmp",path+".bak",true);
                }
            }catch(System.Text.Json.JsonException){ }
        }
        File.Move(temp,path,true);
    }
    public string[] Names(string subfolder) => Directory.Exists(SafePath(subfolder)) ? Directory.GetFiles(SafePath(subfolder), "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray() : [];
    public static string DeviceKey(string id) => Hash(id)[..16];
    private string SafePath(string name)
    {
        string path = Path.GetFullPath(Path.Combine(DirectoryPath, name));
        if (!path.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid storage path");
        return path;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Envelope(int Schema, string Payload, string Hash);
}
