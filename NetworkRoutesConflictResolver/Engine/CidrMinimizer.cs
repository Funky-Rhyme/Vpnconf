using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Engine;

/// <summary>
/// Reduces a set of IPv4 CIDR blocks to the minimal equivalent set: overlapping and adjacent
/// blocks are merged, and aligned sibling pairs are aggregated into a shorter prefix.
/// Pure and deterministic — identical input always yields identical, sorted output.
/// </summary>
public sealed class CidrMinimizer
{
    public IReadOnlyList<Ipv4Cidr> Minimize(IEnumerable<Ipv4Cidr> cidrs)
    {
        ArgumentNullException.ThrowIfNull(cidrs);

        // 1) Collapse to a set of non-overlapping inclusive ranges by sweeping in address order.
        var sorted = new List<Ipv4Cidr>(cidrs);
        sorted.Sort();

        var merged = new List<(uint Start, uint End)>();
        foreach (var cidr in sorted)
        {
            if (merged.Count == 0)
            {
                merged.Add((cidr.First, cidr.Last));
                continue;
            }

            var (start, end) = merged[^1];

            // Merge when overlapping or directly adjacent (end + 1 == next.First), guarding uint overflow.
            if (cidr.First <= end || (end != uint.MaxValue && cidr.First == end + 1))
            {
                if (cidr.Last > end)
                {
                    merged[^1] = (start, cidr.Last);
                }
            }
            else
            {
                merged.Add((cidr.First, cidr.Last));
            }
        }

        // 2) Re-express each merged range as the minimal aligned CIDR set.
        var result = new List<Ipv4Cidr>();
        foreach (var (start, end) in merged)
        {
            Ipv4Ranges.AppendRangeAsCidrs(start, end, result);
        }

        result.Sort();
        return result;
    }
}
