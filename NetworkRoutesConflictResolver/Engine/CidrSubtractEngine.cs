using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Engine;

/// <summary>
/// Subtracts a set of blocked CIDR blocks from a source CIDR and returns the remaining coverage
/// as a minimal set of aligned CIDR blocks. This is how we "punch holes" in a broad personal
/// route so a more specific corporate subnet can keep working.
/// Pure and deterministic.
/// </summary>
public sealed class CidrSubtractEngine
{
    public IReadOnlyList<Ipv4Cidr> Subtract(Ipv4Cidr source, IReadOnlyList<Ipv4Cidr> blocked)
    {
        ArgumentNullException.ThrowIfNull(blocked);

        // Clip blocked blocks to the source range, drop the rest, and sort by start address.
        var holes = new List<(uint Start, uint End)>(blocked.Count);
        foreach (var block in blocked)
        {
            if (!block.Overlaps(source))
            {
                continue;
            }

            var start = Math.Max(block.First, source.First);
            var end = Math.Min(block.Last, source.Last);
            holes.Add((start, end));
        }

        var result = new List<Ipv4Cidr>();
        if (holes.Count == 0)
        {
            result.Add(source);
            return result;
        }

        holes.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Walk the source range left to right, emitting the gaps between merged holes.
        ulong cursor = source.First;
        foreach (var (holeStart, holeEnd) in holes)
        {
            if (holeStart > cursor)
            {
                Ipv4Ranges.AppendRangeAsCidrs((uint)cursor, holeStart - 1, result);
            }

            // Advance past this hole; holes may overlap, so never move the cursor backwards.
            var next = holeEnd + 1UL;
            if (next > cursor)
            {
                cursor = next;
            }

            if (cursor > source.Last)
            {
                break;
            }
        }

        if (cursor <= source.Last)
        {
            Ipv4Ranges.AppendRangeAsCidrs((uint)cursor, source.Last, result);
        }

        result.Sort();
        return result;
    }
}
