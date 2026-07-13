namespace NetworkRoutesConflictResolver.Model;

/// <summary>
/// A personal (split-tunnel) list entry. <see cref="Hostname"/> keeps the original text exactly
/// as it appeared in the source file so the patched output round-trips the format;
/// <see cref="Cidr"/> is the parsed block used for all math.
/// </summary>
public sealed record CidrEntry(
    string Hostname,
    Ipv4Cidr Cidr);
