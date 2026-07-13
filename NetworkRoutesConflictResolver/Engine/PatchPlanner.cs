using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Engine;

/// <summary>
/// Turns detected conflicts into a minimal patch plan: each conflicting personal CIDR is removed
/// and replaced by the minimal set of CIDRs covering it minus the conflicting VPN routes.
/// Pure and deterministic; output collections are sorted and deduplicated.
/// </summary>
public sealed class PatchPlanner
{
    private readonly CidrSubtractEngine _subtractEngine;
    private readonly CidrMinimizer _minimizer;

    public PatchPlanner()
        : this(new CidrSubtractEngine(), new CidrMinimizer())
    {
    }

    private PatchPlanner(CidrSubtractEngine subtractEngine, CidrMinimizer minimizer)
    {
        _subtractEngine = subtractEngine;
        _minimizer = minimizer;
    }

    public PatchPlan BuildPlan(IReadOnlyList<RouteConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        var remove = new SortedSet<Ipv4Cidr>();
        var add = new SortedSet<Ipv4Cidr>();
        var details = new List<PatchEntry>();

        foreach (var conflict in conflicts)
        {
            var remainder = _minimizer.Minimize(
                _subtractEngine.Subtract(conflict.PersonalCidr, conflict.ConflictingRoutes));

            remove.Add(conflict.PersonalCidr);
            foreach (var cidr in remainder)
            {
                add.Add(cidr);
            }

            details.Add(new PatchEntry(
                conflict.PersonalCidr.ToString(),
                remainder.Select(c => c.ToString()).ToArray()));
        }

        // A block that survives as itself (no effective reduction) should not appear in both sets.
        add.ExceptWith(remove);

        return new PatchPlan(
            remove.Select(c => c.ToString()).ToArray(),
            add.Select(c => c.ToString()).ToArray(),
            details);
    }
}
