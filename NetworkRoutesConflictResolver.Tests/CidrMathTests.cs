using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Tests;

public sealed class CidrMathTests
{
    [Theory]
    [InlineData("10.0.0.0/8", 0x0A000000u, 8)]
    [InlineData("192.168.1.0/24", 0xC0A80100u, 24)]
    [InlineData("1.0.0.0/9", 0x01000000u, 9)]
    [InlineData("192.168.1.5", 0xC0A80105u, 32)]
    public void TryParse_valid(string text, uint network, int prefix)
    {
        Assert.True(Ipv4Cidr.TryParse(text, out var cidr));
        Assert.Equal(network, cidr.Network);
        Assert.Equal(prefix, cidr.Prefix);
    }

    [Fact]
    public void Constructor_masks_host_bits()
    {
        // 10.0.0.5/8 canonicalizes to 10.0.0.0/8.
        var cidr = new Ipv4Cidr(0x0A000005, 8);
        Assert.Equal(0x0A000000u, cidr.Network);
        Assert.Equal("10.0.0.0/8", cidr.ToString());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("10.0.0.0/33", false)]
    [InlineData("300.0.0.0/8", false)]
    [InlineData("not-an-ip", false)]
    public void TryParse_invalid(string text, bool expected)
        => Assert.Equal(expected, Ipv4Cidr.TryParse(text, out _));

    [Fact]
    public void First_and_last_cover_whole_block()
    {
        var cidr = Ipv4Cidr.Parse("10.1.2.0/24");
        Assert.Equal(Ipv4Cidr.Parse("10.1.2.0/32").Network, cidr.First);
        Assert.Equal(Ipv4Cidr.Parse("10.1.2.255/32").Network, cidr.Last);
        Assert.Equal(256UL, cidr.Count);
    }

    [Fact]
    public void Full_space_has_expected_bounds()
    {
        var all = new Ipv4Cidr(0, 0);
        Assert.Equal(0u, all.First);
        Assert.Equal(uint.MaxValue, all.Last);
        Assert.Equal(1UL << 32, all.Count);
    }

    [Fact]
    public void Contains_and_overlaps()
    {
        var outer = Ipv4Cidr.Parse("10.0.0.0/8");
        var inner = Ipv4Cidr.Parse("10.1.2.0/24");
        var disjoint = Ipv4Cidr.Parse("11.0.0.0/8");

        Assert.True(outer.Contains(inner));
        Assert.False(inner.Contains(outer));
        Assert.True(outer.Overlaps(inner));
        Assert.False(outer.Overlaps(disjoint));
    }

    [Fact]
    public void AppendRangeAsCidrs_full_space_is_single_block()
    {
        var result = new List<Ipv4Cidr>();
        Ipv4Ranges.AppendRangeAsCidrs(0, uint.MaxValue, result);
        Assert.Equal(new[] { new Ipv4Cidr(0, 0) }, result);
    }

    [Fact]
    public void AppendRangeAsCidrs_aligned_block()
    {
        var result = new List<Ipv4Cidr>();
        var block = Ipv4Cidr.Parse("10.0.0.0/30");
        Ipv4Ranges.AppendRangeAsCidrs(block.First, block.Last, result);
        Assert.Equal(new[] { block }, result);
    }

    [Fact]
    public void AppendRangeAsCidrs_unaligned_splits_into_host_routes()
    {
        // 10.0.0.1 .. 10.0.0.2 cannot be one block; it is two /32s.
        var result = new List<Ipv4Cidr>();
        Ipv4Ranges.AppendRangeAsCidrs(
            Ipv4Cidr.Parse("10.0.0.1").First,
            Ipv4Cidr.Parse("10.0.0.2").First,
            result);
        Assert.Equal(
            new[] { Ipv4Cidr.Parse("10.0.0.1/32"), Ipv4Cidr.Parse("10.0.0.2/32") },
            result);
    }
}
