namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>
/// Central registry of ip-list formats. Commands resolve a parser by format id (from <c>--format</c>)
/// or fall back to content-based auto-detection. Add a new <see cref="IIpListParser"/> here and it
/// becomes available everywhere — including future interactive format selection.
/// </summary>
public sealed class IpListParserRegistry
{
    public const string AutoFormatId = "auto";

    private readonly IReadOnlyList<IIpListParser> _parsers;

    public IpListParserRegistry()
        : this(new IIpListParser[] { new JsonIpListParser(), new PlainTextIpListParser() })
    {
    }

    public IpListParserRegistry(IReadOnlyList<IIpListParser> parsers)
    {
        _parsers = parsers;
    }

    /// <summary>Format ids available for selection, plus the special "auto".</summary>
    public IReadOnlyList<string> AvailableFormats
        => _parsers.Select(p => p.FormatId).Prepend(AutoFormatId).ToArray();

    public IReadOnlyList<IIpListParser> Parsers => _parsers;

    /// <summary>
    /// Resolves a parser. When <paramref name="format"/> is null or "auto", the format is detected
    /// from <paramref name="content"/>; otherwise the parser with the matching id is returned.
    /// </summary>
    public IIpListParser Resolve(string? format, string content)
    {
        if (format is null || format.Equals(AutoFormatId, StringComparison.OrdinalIgnoreCase))
        {
            return AutoDetect(content);
        }

        foreach (var parser in _parsers)
        {
            if (parser.FormatId.Equals(format, StringComparison.OrdinalIgnoreCase))
            {
                return parser;
            }
        }

        throw new ArgumentException(
            $"Unknown ip-list format '{format}'. Available: {string.Join(", ", AvailableFormats)}.");
    }

    /// <summary>Picks the first parser that recognizes the content; defaults to JSON.</summary>
    public IIpListParser AutoDetect(string content)
    {
        foreach (var parser in _parsers)
        {
            if (parser.CanParse(content))
            {
                return parser;
            }
        }

        return _parsers[0];
    }
}
