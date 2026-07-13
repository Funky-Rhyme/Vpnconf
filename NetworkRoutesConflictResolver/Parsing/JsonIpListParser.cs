using System.Text.Json;
using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>
/// Parses the JSON split-tunnel list: an array of <c>{ "hostname": "&lt;cidr&gt;", "ip": "" }</c>.
/// Invalid or non-IPv4 hostnames are collected rather than throwing, so one bad row never
/// aborts the whole run.
/// </summary>
public sealed class JsonIpListParser : IIpListParser
{
    public string FormatId => "json";

    public string DisplayName => "JSON list ([{ \"hostname\": \"<cidr>\", \"ip\": \"\" }])";

    public bool CanParse(string content)
    {
        var trimmed = content.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '[' || trimmed[0] == '{');
    }

    public IpListParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        IpListItem[]? items;
        try
        {
            items = JsonSerializer.Deserialize(content, IpListJsonContext.Default.IpListItemArray);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"ip-list is not valid JSON: {ex.Message}", ex);
        }

        if (items is null)
        {
            return new IpListParseResult([], []);
        }

        var entries = new List<CidrEntry>(items.Length);
        var invalid = new List<string>();

        foreach (var item in items)
        {
            var hostname = item.Hostname?.Trim();
            if (string.IsNullOrEmpty(hostname))
            {
                continue;
            }

            if (Ipv4Cidr.TryParse(hostname, out var cidr))
            {
                entries.Add(new CidrEntry(hostname, cidr));
            }
            else
            {
                invalid.Add(hostname);
            }
        }

        return new IpListParseResult(entries, invalid);
    }
}
