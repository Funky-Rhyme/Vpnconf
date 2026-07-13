using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Engine;

/// <summary>
/// Applies a <see cref="PatchPlan"/> to an ip-list: removes the conflicting CIDRs and inserts the
/// replacements. Pure and deterministic — the result is sorted and deduplicated by block.
/// </summary>
public sealed class PatchApplier
{
    public IReadOnlyList<CidrEntry> Apply(IReadOnlyList<CidrEntry> original, PatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(plan);

        var removed = new HashSet<Ipv4Cidr>();
        foreach (var cidr in plan.Remove)
        {
            if (Ipv4Cidr.TryParse(cidr, out var parsed))
            {
                removed.Add(parsed);
            }
        }

        // Deduplicate by block; the hostname text mirrors the canonical block form.
        var result = new SortedDictionary<Ipv4Cidr, CidrEntry>();

        foreach (var entry in original)
        {
            if (!removed.Contains(entry.Cidr))
            {
                result[entry.Cidr] = entry;
            }
        }

        foreach (var cidr in plan.Add)
        {
            if (Ipv4Cidr.TryParse(cidr, out var parsed))
            {
                result[parsed] = new CidrEntry(parsed.ToString(), parsed);
            }
        }

        return result.Values.ToArray();
    }
}
