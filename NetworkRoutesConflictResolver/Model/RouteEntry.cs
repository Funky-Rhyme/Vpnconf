namespace NetworkRoutesConflictResolver.Model;

/// <summary>
/// A single routing-table entry, normalized across OS route sources.
/// <see cref="InterfaceIndex"/> is the reliable key for grouping/isolating routes by adapter;
/// <see cref="InterfaceName"/> is the human-facing label shown for interactive selection.
/// </summary>
public sealed record RouteEntry(
    Ipv4Cidr Destination,
    string Gateway,
    string InterfaceName,
    int InterfaceIndex,
    int Metric);
