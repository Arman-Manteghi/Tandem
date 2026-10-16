using System.Security.Cryptography;

namespace Tandem.Core.Profiles;

internal static class ProfileIds
{
    // No look-alike characters (0/o, 1/l), so ids are easy to read in folder names.
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    private const int Length = 8;

    /// <summary>Ids are also folder names, so only lower-case letters and digits are allowed.</summary>
    public static bool IsValidCustomId(string? id) =>
        id is { Length: >= 6 and <= 32 }
        && id != Profile.DefaultId
        && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    public static string NewUnique(IEnumerable<string> existingIds, Func<string, bool> isTaken)
    {
        var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var chars = new char[Length];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            var id = new string(chars);
            if (!existing.Contains(id) && !isTaken(id))
            {
                return id;
            }
        }
    }
}
