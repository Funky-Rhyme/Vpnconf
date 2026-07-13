namespace NetworkRoutesConflictResolver.Cli;

/// <summary>
/// Minimal, dependency-free parser for <c>--key value</c> options and <c>--flag</c> switches.
/// Keeps the Cli layer focused on argument handling without pulling a full parsing framework.
/// </summary>
public sealed class ArgMap
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public static ArgMap Parse(string[] args, IReadOnlySet<string> knownFlags)
    {
        var map = new ArgMap();
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token[2..];
            if (knownFlags.Contains(key))
            {
                map._flags.Add(key);
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map._options[key] = args[i + 1];
                i++;
            }
            else
            {
                // Unknown trailing switch; treat as a flag so callers can validate.
                map._flags.Add(key);
            }
        }

        return map;
    }

    public bool HasFlag(string name) => _flags.Contains(name);

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public string Require(string name)
        => Get(name) ?? throw new ArgumentException($"Missing required option --{name}.");
}
