using System.Buffers.Binary;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NetworkRoutesConflictResolver.Model;
using NetworkRoutesConflictResolver.Parsing;

namespace NetworkRoutesConflictResolver.Infrastructure.Routes;

/// <summary>
/// Reads the live IPv4 routing table via a <c>NETLINK_ROUTE</c> socket (<c>RTM_GETROUTE</c> dump).
/// Locale- and distro-independent (unlike parsing <c>ip route</c> text, which varies between
/// iproute2 and busybox builds). The dump spans every routing table, not just <c>main</c>, since
/// VPN clients commonly add corporate routes via policy routing into a separate table. Owns only
/// the socket I/O; buffer parsing lives in <see cref="NetlinkRouteDumpParser"/> so it stays
/// unit-testable without a Linux socket.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxRouteTableProvider : IRouteTableProvider
{
    private const int AfNetlink = 16;
    private const int SockRaw = 3;
    private const int NetlinkRoute = 0;
    private const byte AfInet = 2;

    private const ushort RtmGetRoute = 26;
    private const ushort NlmFRequest = 0x1;
    private const ushort NlmFDump = 0x300; // NLM_F_ROOT | NLM_F_MATCH

    private const int ReceiveBufferSize = 64 * 1024;
    private const int MaxDumpAttempts = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct SockAddrNl
    {
        public ushort Family;
        public ushort Pad;
        public uint Pid;
        public uint Groups;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int socket(int domain, int type, int protocol);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int bind(int sockfd, ref SockAddrNl addr, int addrlen);

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint send(int sockfd, byte[] buf, nint len, int flags);

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint recv(int sockfd, byte[] buf, nint len, int flags);

    [LibraryImport("libc")]
    private static partial int close(int fd);

    public Task<IReadOnlyList<RouteEntry>> GetRoutesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ReadRoutes());

    private static IReadOnlyList<RouteEntry> ReadRoutes()
    {
        var fd = socket(AfNetlink, SockRaw, NetlinkRoute);
        if (fd < 0)
        {
            throw new InvalidOperationException($"socket(AF_NETLINK) failed (errno {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            var local = new SockAddrNl { Family = AfNetlink };
            if (bind(fd, ref local, Marshal.SizeOf<SockAddrNl>()) != 0)
            {
                throw new InvalidOperationException($"bind(AF_NETLINK) failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            var names = BuildInterfaceNameMap();

            for (var attempt = 1; attempt <= MaxDumpAttempts; attempt++)
            {
                SendRouteDumpRequest(fd, sequence: attempt);
                var dump = ReceiveDump(fd);
                var (routes, interrupted) = NetlinkRouteDumpParser.ParseRouteDump(
                    dump, index => names.TryGetValue(index, out var n) ? n : $"if#{index}");

                if (!interrupted)
                {
                    return routes;
                }
            }

            throw new InvalidOperationException(
                $"Netlink route dump was repeatedly interrupted (NLM_F_DUMP_INTR) after {MaxDumpAttempts} attempts.");
        }
        finally
        {
            close(fd);
        }
    }

    private static void SendRouteDumpRequest(int fd, int sequence)
    {
        // nlmsghdr (16 bytes) + rtgenmsg (1 byte payload: rtgen_family).
        var request = new byte[17];
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(0, 4), request.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4, 2), RtmGetRoute);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6, 2), (ushort)(NlmFRequest | NlmFDump));
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(8, 4), sequence);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(12, 4), 0);
        request[16] = AfInet;

        var sent = send(fd, request, request.Length, 0);
        if (sent < 0)
        {
            throw new InvalidOperationException($"send(RTM_GETROUTE) failed (errno {Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>
    /// Drains netlink messages into one buffer until <c>NLMSG_DONE</c> arrives. A single dump
    /// spans several <c>recv</c> calls; a plain <c>recv</c> return never splits a message header
    /// mid-struct, so scanning each freshly-received chunk for the terminating message is safe.
    /// </summary>
    private static byte[] ReceiveDump(int fd)
    {
        using var received = new MemoryStream();
        var buffer = new byte[ReceiveBufferSize];

        while (true)
        {
            var n = recv(fd, buffer, buffer.Length, 0);
            if (n < 0)
            {
                throw new InvalidOperationException($"recv() failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            if (n == 0)
            {
                break;
            }

            received.Write(buffer, 0, (int)n);

            if (NetlinkRouteDumpParser.ChunkContainsDone(buffer.AsSpan(0, (int)n)))
            {
                break;
            }
        }

        return received.ToArray();
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
