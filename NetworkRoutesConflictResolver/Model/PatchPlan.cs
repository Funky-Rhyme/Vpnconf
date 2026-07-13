namespace NetworkRoutesConflictResolver.Model;

/// <summary>
/// Replacement for a single conflicting personal CIDR: the original block is removed and the
/// (possibly empty) set of remaining blocks is added in its place.
/// </summary>
public sealed record PatchEntry(
    string Original,
    IReadOnlyList<string> Replacement);

/// <summary>
/// Minimal patch for the personal ip-list: <see cref="Remove"/> lists CIDRs to drop,
/// <see cref="Add"/> lists CIDRs to insert. <see cref="Details"/> keeps the per-CIDR mapping
/// for reporting. All collections are sorted and deduplicated for reproducible output.
/// </summary>
public sealed record PatchPlan(
    IReadOnlyList<string> Remove,
    IReadOnlyList<string> Add,
    IReadOnlyList<PatchEntry> Details);
