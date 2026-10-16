using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tandem.Core.Storage;

/// <summary>Small JSON files written atomically (write to a temp file, then replace).</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new TolerantEnumConverterFactory() },
    };

    /// <summary>
    /// Another Tandem process (the link handler, a shortcut) may be replacing the same file at
    /// this moment; a sharing violation clears up within milliseconds.
    /// </summary>
    private const int Attempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);

    public static T? Read<T>(string path) => Retry(() =>
    {
        // Shared for writing and deleting, so a reader never blocks the writer's replace.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Options);
    });

    public static void Write<T>(string path, T value) =>
        WriteAllTextAtomic(path, JsonSerializer.Serialize(value, Options));

    public static void WriteAllTextAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            Retry(() =>
            {
                if (File.Exists(path))
                {
                    File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, path);
                }

                return true;
            });
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Renames an unreadable file out of the way (never deletes it) and returns the new path.
    /// </summary>
    public static string Quarantine(string path, DateTimeOffset now)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var target = Path.Combine(dir, $"{name}.corrupt-{now:yyyyMMdd-HHmmss}.json");
        for (var i = 2; File.Exists(target); i++)
        {
            target = Path.Combine(dir, $"{name}.corrupt-{now:yyyyMMdd-HHmmss}-{i}.json");
        }

        File.Move(path, target);
        return target;
    }

    private static T Retry<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException ex) when (attempt < Attempts && ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                Thread.Sleep(RetryDelay * attempt);
            }
        }
    }
}
