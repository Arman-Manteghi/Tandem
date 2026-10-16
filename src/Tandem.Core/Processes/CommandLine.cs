using System.Text;

namespace Tandem.Core.Processes;

/// <summary>
/// Windows command-line splitting and quoting (the rules of CommandLineToArgvW / the MSVC runtime),
/// plus Chromium-style switch lookup.
/// </summary>
public static class CommandLine
{
    /// <summary>Splits a full command line. Element 0 is the program.</summary>
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(commandLine))
        {
            return result;
        }

        var s = commandLine;
        var i = 0;
        var sb = new StringBuilder();

        while (i < s.Length && IsBlank(s[i]))
        {
            i++;
        }

        if (i >= s.Length)
        {
            return result;
        }

        // The program name has simpler rules: quotes group, backslashes are literal.
        if (s[i] == '"')
        {
            i++;
            while (i < s.Length && s[i] != '"')
            {
                sb.Append(s[i++]);
            }

            if (i < s.Length)
            {
                i++;
            }
        }
        else
        {
            while (i < s.Length && !IsBlank(s[i]))
            {
                sb.Append(s[i++]);
            }
        }

        result.Add(sb.ToString());

        while (true)
        {
            while (i < s.Length && IsBlank(s[i]))
            {
                i++;
            }

            if (i >= s.Length)
            {
                break;
            }

            sb.Clear();
            var inQuotes = false;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\\')
                {
                    var start = i;
                    while (i < s.Length && s[i] == '\\')
                    {
                        i++;
                    }

                    var count = i - start;
                    if (i < s.Length && s[i] == '"')
                    {
                        sb.Append('\\', count / 2);
                        if (count % 2 == 1)
                        {
                            sb.Append('"');
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append('\\', count);
                    }

                    continue;
                }

                if (c == '"')
                {
                    if (inQuotes && i + 1 < s.Length && s[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (!inQuotes && IsBlank(c))
                {
                    break;
                }

                sb.Append(c);
                i++;
            }

            result.Add(sb.ToString());
        }

        return result;
    }

    /// <summary>Quotes one argument so that <see cref="Split"/> returns it unchanged.</summary>
    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var sb = new StringBuilder("\"");
        for (var i = 0; ; i++)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                sb.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(argument[i]);
            }
        }

        return sb.Append('"').ToString();
    }

    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    /// <summary>
    /// Value of a Chromium switch (<c>--name=value</c>, also <c>-name=</c> and <c>/name=</c>; names are
    /// case-insensitive on Windows). Element 0 is skipped; parsing stops at a bare <c>--</c>.
    /// </summary>
    public static string? GetSwitchValue(IReadOnlyList<string> args, string name)
    {
        foreach (var (switchName, value) in EnumerateSwitches(args))
        {
            if (string.Equals(switchName, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    public static bool HasSwitch(IReadOnlyList<string> args, string name) =>
        EnumerateSwitches(args).Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Name, string? Value)> EnumerateSwitches(IReadOnlyList<string> args)
    {
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                yield break;
            }

            var prefixLength = arg.StartsWith("--", StringComparison.Ordinal) ? 2
                : arg.StartsWith('-') || arg.StartsWith('/') ? 1
                : 0;
            if (prefixLength == 0 || arg.Length == prefixLength)
            {
                continue;
            }

            var body = arg[prefixLength..];
            var eq = body.IndexOf('=');
            yield return eq < 0 ? (body, null) : (body[..eq], body[(eq + 1)..]);
        }
    }

    private static bool IsBlank(char c) => c is ' ' or '\t';
}
