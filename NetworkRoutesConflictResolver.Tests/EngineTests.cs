using NetworkRoutesConflictResolver.Engine;
using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Tests;

public sealed class EngineTests
{
    private static Ipv4Cidr C(string s) => Ipv4Cidr.Parse(s);

    private static ulong Coverage(IEnumerable<Ipv4Cidr> cidrs)
        => cidrs.Aggregate(0UL, (sum, c) => sum + c.Count);

    private static bool AreDisjoint(IReadOnlyList<Ipv4Cidr> cidrs)
    {
        var sorted = cidrs.OrderBy(c => c).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].Overlaps(sorted[i - 1]))
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public void Subtract_punches_a_single_hole()
    {
        var engine = new CidrSubtractEngine();
        var source = C("10.0.0.0/16");
        var result = engine.Subtract(source, [C("10.0.5.0/24")]);

        // Coverage should be the source minus exactly the /24 hole, and blocks must be disjoint.
        Assert.Equal(source.Count - 256UL, Coverage(result));
        Assert.True(AreDisjoint(result));
        Assert.DoesNotContain(result, c => c.Overlaps(C("10.0.5.0/24")));
        Assert.Contains(result, c => c.Contains(C("10.0.4.0/24")));
    }

    [Fact]
    public void Subtract_entire_source_yields_empty()
    {
        var engine = new CidrSubtractEngine();
        var source = C("10.0.0.0/16");
        Assert.Empty(engine.Subtract(source, [source]));
    }

    [Fact]
    public void Subtract_multiple_and_overlapping_holes()
    {
        var engine = new CidrSubtractEngine();
        var source = C("10.0.0.0/16");
        // Overlapping blocks 10.0.5.0/24 and 10.0.5.128/25 remove 256 unique addresses total.
        var result = engine.Subtract(source, [C("10.0.5.0/24"), C("10.0.5.128/25"), C("10.0.9.0/24")]);
        Assert.Equal(source.Count - 256UL - 256UL, Coverage(result));
        Assert.True(AreDisjoint(result));
    }

    [Fact]
    public void Subtract_block_outside_source_is_noop()
    {
        var engine = new CidrSubtractEngine();
        var source = C("10.0.0.0/16");
        var result = engine.Subtract(source, [C("192.168.0.0/16")]);
        Assert.Equal(new[] { source }, result);
    }

    [Fact]
    public void Minimize_collapses_sibling_pair()
    {
        var minimizer = new CidrMinimizer();
        var result = minimizer.Minimize([C("10.0.0.0/25"), C("10.0.0.128/25")]);
        Assert.Equal(new[] { C("10.0.0.0/24") }, result);
    }

    [Fact]
    public void Minimize_absorbs_contained_and_dedups()
    {
        var minimizer = new CidrMinimizer();
        var result = minimizer.Minimize([C("10.0.0.0/24"), C("10.0.0.0/25"), C("10.0.0.0/24")]);
        Assert.Equal(new[] { C("10.0.0.0/24") }, result);
    }

    [Fact]
    public void Minimize_merges_adjacent_ranges()
    {
        var minimizer = new CidrMinimizer();
        // 10.0.0.0/24 and 10.0.1.0/24 are adjacent -> 10.0.0.0/23.
        var result = minimizer.Minimize([C("10.0.1.0/24"), C("10.0.0.0/24")]);
        Assert.Equal(new[] { C("10.0.0.0/23") }, result);
    }

    [Fact]
    public void DetectConflicts_finds_overlaps_only()
    {
        var detector = new ConflictDetector();
        var ipList = new[]
        {
            new CidrEntry("10.0.0.0/8", C("10.0.0.0/8")),
            new CidrEntry("192.168.0.0/16", C("192.168.0.0/16")),
        };

        var conflicts = detector.DetectConflicts([C("10.1.2.0/24"), C("172.16.0.0/12")], ipList);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(C("10.0.0.0/8"), conflict.PersonalCidr);
        Assert.Equal(new[] { C("10.1.2.0/24") }, conflict.ConflictingRoutes);
    }

    [Fact]
    public void ComputeAddedRoutes_is_after_minus_before_by_destination()
    {
        var detector = new ConflictDetector();
        RouteEntry R(string cidr) => new(C(cidr), "0.0.0.0", "eth0", 0, 0);

        var added = detector.ComputeAddedRoutes(
            before: [R("192.168.0.0/24")],
            after: [R("192.168.0.0/24"), R("10.8.0.0/16"), R("10.8.0.0/16")]);

        Assert.Equal(new[] { C("10.8.0.0/16") }, added.Select(r => r.Destination).ToArray());
    }

    [Fact]
    public void PatchPlanner_and_Applier_round_trip_excludes_conflict()
    {
        var detector = new ConflictDetector();
        var planner = new PatchPlanner();
        var applier = new PatchApplier();

        var ipList = new[]
        {
            new CidrEntry("10.0.0.0/8", C("10.0.0.0/8")),
            new CidrEntry("192.168.0.0/16", C("192.168.0.0/16")),
        };

        var conflicts = detector.DetectConflicts([C("10.5.0.0/16")], ipList);
        var plan = planner.BuildPlan(conflicts);
        var patched = applier.Apply(ipList, plan);

        var patchedCidrs = patched.Select(e => e.Cidr).ToArray();

        // Untouched entry survives; the conflicting /8 is gone; the hole is not re-covered.
        Assert.Contains(C("192.168.0.0/16"), patchedCidrs);
        Assert.DoesNotContain(C("10.0.0.0/8"), patchedCidrs);
        Assert.DoesNotContain(patchedCidrs, c => c.Overlaps(C("10.5.0.0/16")));
        // Total 10.x coverage = /8 minus the /16 hole.
        Assert.Equal(
            C("10.0.0.0/8").Count - C("10.5.0.0/16").Count,
            Coverage(patchedCidrs.Where(c => C("10.0.0.0/8").Contains(c))));
    }
}
