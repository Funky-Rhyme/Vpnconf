using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NetworkRoutesConflictResolver.Model;
using NetworkRoutesConflictResolver.Parsing;

namespace NetworkRoutesConflictResolver.Output;

/// <summary>
/// Serializes an ip-list back to the original on-disk format: a JSON array of
/// <c>{ "hostname": "&lt;cidr&gt;", "ip": "" }</c>. Output is deterministic.
/// </summary>
public sealed class IpListSerializer
{
    // Match the source file's 4-space indentation while staying AOT-safe: copy the generated
    // context's options (keeping its source-gen resolver), tweak indentation, and resolve a
    // strongly-typed JsonTypeInfo so no reflection-based serialization path is used.
    private static readonly JsonTypeInfo<IpListItem[]> ArrayTypeInfo = BuildTypeInfo();

    private static JsonTypeInfo<IpListItem[]> BuildTypeInfo()
    {
        var options = new JsonSerializerOptions(IpListJsonContext.Default.Options)
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 4,
        };

        return (JsonTypeInfo<IpListItem[]>)options.GetTypeInfo(typeof(IpListItem[]));
    }

    public string Serialize(IReadOnlyList<CidrEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var items = new IpListItem[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            items[i] = new IpListItem { Hostname = entries[i].Hostname, Ip = string.Empty };
        }

        return JsonSerializer.Serialize(items, ArrayTypeInfo);
    }
}
