using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>Outcome of parsing an ip-list: valid entries plus any lines that failed to parse.</summary>
public sealed record IpListParseResult(
    IReadOnlyList<CidrEntry> Entries,
    IReadOnlyList<string> Invalid);

/// <summary>
/// A pluggable ip-list parser. Each supported input format is one implementation, registered in
/// <see cref="IpListParserRegistry"/>. This is the seam that lets users pick a format up front and
/// lets new formats be added without touching the commands.
/// </summary>
public interface IIpListParser
{
    /// <summary>Stable identifier used on the command line (e.g. "json", "plain").</summary>
    string FormatId { get; }

    /// <summary>Human-readable name for interactive selection.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Heuristic: true when this parser looks able to handle <paramref name="content"/>.
    /// Used by auto-detection; need not be exhaustive.
    /// </summary>
    bool CanParse(string content);

    IpListParseResult Parse(string content);
}
