using System.Buffers.Binary;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Infrastructure.Routes;

/// <summary>
/// Reads the live IPv4 routing table via the iphlpapi <c>GetIpForwardTable</c> API. This is
/// locale-independent (unlike parsing <c>route print</c> text) and provides the interface index
/// directly, which we map to a human-readable adapter name for interactive selection.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsRouteTableProvider : IRouteTableProvider
{
    // MIB_IPFORWARDROW is 14 consecutive DWORDs.
    private const int RowSize = 56;
    private const int ErrorInsufficientBuffer = 122;

    [LibraryImport("iphlpapi.dll")]
    private static partial int GetIpForwardTable(IntPtr pIpForwardTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder);

    public Task<IReadOnlyList<RouteEntry>> GetRoutesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ReadRoutes());

    private static IReadOnlyList<RouteEntry> ReadRoutes()
    {
        var size = 0;
        var probe = GetIpForwardTable(IntPtr.Zero, ref size, true);
        if (probe != ErrorInsufficientBuffer || size == 0)
        {
            throw new InvalidOperationException($"GetIpForwardTable size probe failed (code {probe}).");
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var rc = GetIpForwardTable(buffer, ref size, true);
            if (rc != 0)
            {
                throw new InvalidOperationException($"GetIpForwardTable failed (code {rc}).");
            }

            var data = new byte[size];
            Marshal.Copy(buffer, data, 0, size);

            var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
            var names = BuildInterfaceNameMap();

            var routes = new List<RouteEntry>(count);
            for (var i = 0; i < count; i++)
            {
                var row = data.AsSpan(4 + (i * RowSize), RowSize);

                // dwForwardDest / dwForwardMask / dwForwardNextHop are stored in network byte order.
                var destination = BinaryPrimitives.ReadUInt32BigEndian(row.Slice(0, 4));
                var maskBits = BitOperations.PopCount(BinaryPrimitives.ReadUInt32BigEndian(row.Slice(4, 4)));
                var nextHop = BinaryPrimitives.ReadUInt32BigEndian(row.Slice(12, 4));
                var ifIndex = BinaryPrimitives.ReadInt32LittleEndian(row.Slice(16, 4));
                var metric = BinaryPrimitives.ReadInt32LittleEndian(row.Slice(36, 4));

                var name = names.TryGetValue(ifIndex, out var n) ? n : $"if#{ifIndex}";

                routes.Add(new RouteEntry(
                    new Ipv4Cidr(destination, maskBits),
                    Ipv4Cidr.FormatAddress(nextHop),
                    name,
                    ifIndex,
                    metric));
            }

            return routes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Dictionary<int, string> BuildInterfaceNameMap()
    {
        var map = new Dictionary<int, string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var index = nic.GetIPProperties().GetIPv4Properties()?.Index;
                if (index.HasValue)
                {
                    map[index.Value] = nic.Name;
                }
            }
            catch (NetworkInformationException)
            {
                // Interface without IPv4 properties; skip — routes on it fall back to "if#<index>".
            }
        }

        return map;
    }
}
