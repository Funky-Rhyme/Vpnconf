using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>
/// Parses a plain-text ip-list: one CIDR (or bare IPv4 = /32) per line. Blank lines and comments
/// (starting with <c>#</c> or <c>//</c>) are ignored; a trailing comment after a CIDR is allowed.
/// Demonstrates the pluggable parser seam; more formats can be added the same way.
/// </summary>
public sealed class PlainTextIpListParser : IIpListParser
{
    public string FormatId => "plain";

    public string DisplayName => "Plain text (one CIDR per line)";

    public bool CanParse(string content)
    {
        // Anything that is not obviously JSON and has at least one parseable CIDR line.
        var trimmed = content.AsSpan().TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '[' || trimmed[0] == '{'))
        {
            return false;
        }

        foreach (var line in content.Split('\n'))
        {
            if (TryFirstToken(line, out var token) && Ipv4Cidr.TryParse(token, out _))
            {
                return true;
            }
        }

        return false;
    }

    public IpListParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var entries = new List<CidrEntry>();
        var invalid = new List<string>();

        foreach (var line in content.Split('\n'))
        {
            if (!TryFirstToken(line, out var token))
            {
                continue;
            }

            if (Ipv4Cidr.TryParse(token, out var cidr))
            {
                entries.Add(new CidrEntry(token, cidr));
            }
            else
            {
                invalid.Add(token);
            }
        }

        return new IpListParseResult(entries, invalid);
    }

    /// <summary>Extracts the first whitespace-delimited token of a line, skipping blanks/comments.</summary>
    private static bool TryFirstToken(string line, out string token)
    {
        token = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var space = trimmed.IndexOfAny([' ', '\t', ',', ';']);
        token = space >= 0 ? trimmed[..space] : trimmed;
        return token.Length > 0;
    }
}
