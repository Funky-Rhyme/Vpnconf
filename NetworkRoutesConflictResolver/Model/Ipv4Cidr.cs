using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace NetworkRoutesConflictResolver.Model;

/// <summary>
/// Immutable IPv4 CIDR block, stored as a canonical 32-bit network address plus prefix length.
/// This is the workhorse value type for all conflict/subtract/minimize math and is deliberately
/// allocation-free so it stays cheap across ~10k-entry datasets.
/// </summary>
public readonly struct Ipv4Cidr : IEquatable<Ipv4Cidr>, IComparable<Ipv4Cidr>
{
    /// <summary>Canonical (masked) network address as a big-endian 32-bit integer.</summary>
    public uint Network { get; }

    /// <summary>Prefix length in bits, 0..32.</summary>
    public int Prefix { get; }

    public Ipv4Cidr(uint network, int prefix)
    {
        if (prefix is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "IPv4 prefix must be in range 0..32.");
        }

        Prefix = prefix;
        Network = network & MaskFor(prefix);
    }

    /// <summary>First address of the block (inclusive).</summary>
    public uint First => Network;

    /// <summary>Last address of the block (inclusive).</summary>
    public uint Last => Network | ~MaskFor(Prefix);

    /// <summary>Number of addresses covered by the block.</summary>
    public ulong Count => (ulong)(Last - First) + 1UL;

    /// <summary>Subnet mask for a given prefix length as a big-endian 32-bit integer.</summary>
    public static uint MaskFor(int prefix) => prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);

    /// <summary>True when this block fully contains <paramref name="other"/>.</summary>
    public bool Contains(Ipv4Cidr other) => other.First >= First && other.Last <= Last;

    /// <summary>True when the two blocks share at least one address.</summary>
    public bool Overlaps(Ipv4Cidr other) => First <= other.Last && other.First <= Last;

    public static Ipv4Cidr Parse(ReadOnlySpan<char> text)
        => TryParse(text, out var cidr)
            ? cidr
            : throw new FormatException($"Invalid IPv4 CIDR: '{text.ToString()}'.");

    /// <summary>
    /// Parses "a.b.c.d/nn" or a bare "a.b.c.d" (treated as /32). Whitespace is trimmed.
    /// JSON unescaping already turns "\/" into "/", so callers pass a normal slash here.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out Ipv4Cidr cidr)
    {
        cidr = default;
        text = text.Trim();
        if (text.IsEmpty)
        {
            return false;
        }

        var slash = text.IndexOf('/');
        var addressPart = slash >= 0 ? text[..slash] : text;
        var prefix = 32;

        if (slash >= 0)
        {
            var prefixPart = text[(slash + 1)..];
            if (!int.TryParse(prefixPart, out prefix) || prefix is < 0 or > 32)
            {
                return false;
            }
        }

        if (!IPAddress.TryParse(addressPart, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[4];
        if (!ip.TryWriteBytes(bytes, out var written) || written != 4)
        {
            return false;
        }

        cidr = new Ipv4Cidr(BinaryPrimitives.ReadUInt32BigEndian(bytes), prefix);
        return true;
    }

    public static string FormatAddress(uint address)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, address);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    public bool Equals(Ipv4Cidr other) => Network == other.Network && Prefix == other.Prefix;

    public override bool Equals(object? obj) => obj is Ipv4Cidr other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Network, Prefix);

    /// <summary>Orders by network address, then by prefix (shorter/broader prefixes first).</summary>
    public int CompareTo(Ipv4Cidr other)
    {
        var byNetwork = Network.CompareTo(other.Network);
        return byNetwork != 0 ? byNetwork : Prefix.CompareTo(other.Prefix);
    }

    public override string ToString() => $"{FormatAddress(Network)}/{Prefix}";

    public static bool operator ==(Ipv4Cidr left, Ipv4Cidr right) => left.Equals(right);

    public static bool operator !=(Ipv4Cidr left, Ipv4Cidr right) => !left.Equals(right);
}

/// <summary>
/// Helpers to translate between arbitrary inclusive [start, end] address ranges and the
/// minimal set of aligned CIDR blocks that exactly cover them. Used by subtract/minimize.
/// </summary>
public static class Ipv4Ranges
{
    /// <summary>
    /// Decomposes an inclusive address range into the minimal, ordered set of CIDR blocks.
    /// Uses <see cref="ulong"/> arithmetic internally so ranges touching 255.255.255.255 do not overflow.
    /// </summary>
    public static void AppendRangeAsCidrs(uint start, uint end, List<Ipv4Cidr> destination)
    {
        if (start > end)
        {
            return;
        }

        ulong current = start;
        ulong last = end;

        while (current <= last)
        {
            // Largest block permitted by the alignment of the current start address.
            var maxHostBitsByAlignment = current == 0 ? 32 : BitOperations.TrailingZeroCount((uint)current);

            // Shrink until the block also fits inside the remaining span.
            var remaining = last - current + 1UL;
            var hostBits = maxHostBitsByAlignment;
            while (hostBits > 0 && (1UL << hostBits) > remaining)
            {
                hostBits--;
            }

            destination.Add(new Ipv4Cidr((uint)current, 32 - hostBits));
            current += 1UL << hostBits;
        }
    }
}
