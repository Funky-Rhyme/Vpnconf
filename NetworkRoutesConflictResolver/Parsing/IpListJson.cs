using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>
/// DTO for one ip-list item: <c>{ "hostname": "&lt;cidr&gt;", "ip": "" }</c>.
/// Kept nullable so malformed items can be filtered instead of throwing.
/// </summary>
public sealed class IpListItem
{
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("ip")]
    public string? Ip { get; set; }
}

/// <summary>
/// Source-generated JSON context so (de)serialization works under PublishAot with no reflection.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(IpListItem[]))]
[JsonSerializable(typeof(List<IpListItem>))]
public sealed partial class IpListJsonContext : JsonSerializerContext
{
}
