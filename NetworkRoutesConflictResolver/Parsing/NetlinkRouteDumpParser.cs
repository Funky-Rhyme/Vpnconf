using System.Buffers.Binary;
using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>
/// Pure parser for a <c>NETLINK_ROUTE</c> <c>RTM_GETROUTE</c> dump buffer (concatenated
/// <c>nlmsghdr</c>/<c>rtmsg</c>/<c>rtattr</c> records, per
/// .claude/tasks/live-routes-linux-macos/research-notes.md §1). Deliberately has no OS/socket
/// dependency — <see cref="Infrastructure.Routes.LinuxRouteTableProvider"/> owns the socket I/O and
/// hands the raw bytes here, which keeps this half unit-testable on any platform (mirrors
/// <see cref="RouteTableParser"/>'s "pure parsing separate from I/O" precedent).
/// </summary>
public static class NetlinkRouteDumpParser
{
    private const byte AfInet = 2;
    private const byte RtnUnicast = 1;

    private const ushort NlmsgError = 2;
    private const ushort NlmsgDone = 3;
    private const ushort RtmNewRoute = 24;

    private const ushort NlmFDumpIntr = 0x10;

    private const ushort RtaDst = 1;
    private const ushort RtaOif = 4;
    private const ushort RtaGateway = 5;
    private const ushort RtaPriority = 6;

    /// <summary>
    /// Walks a concatenated netlink response buffer and produces routes plus whether the dump was
    /// flagged inconsistent (<c>NLM_F_DUMP_INTR</c>) and should be retried by the caller.
    /// </summary>
    public static (List<RouteEntry> Routes, bool Interrupted) ParseRouteDump(
        ReadOnlySpan<byte> buffer, Func<int, string> resolveInterfaceName)
    {
        var routes = new List<RouteEntry>();
        var offset = 0;

        while (offset + 16 <= buffer.Length)
        {
            var msgLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset, 4));
            if (msgLen < 16 || offset + msgLen > buffer.Length)
            {
                break;
            }

            var type = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 4, 2));
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 6, 2));

            if (type == NlmsgDone)
            {
                return (routes, (flags & NlmFDumpIntr) != 0);
            }

            if (type == NlmsgError)
            {
                var errorCode = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset + 16, 4));
                if (errorCode != 0)
                {
                    throw new InvalidOperationException($"Netlink returned error {errorCode} for RTM_GETROUTE.");
                }
            }
            else if (type == RtmNewRoute)
            {
                TryParseRoute(buffer.Slice(offset + 16, msgLen - 16), resolveInterfaceName, routes);
            }

            offset += Align4(msgLen);
        }

        return (routes, false);
    }

    /// <summary>Detects whether a single received chunk contains the dump terminator message.</summary>
    public static bool ChunkContainsDone(ReadOnlySpan<byte> chunk)
    {
        var offset = 0;
        while (offset + 16 <= chunk.Length)
        {
            var msgLen = BinaryPrimitives.ReadInt32LittleEndian(chunk.Slice(offset, 4));
            if (msgLen < 16 || offset + msgLen > chunk.Length)
            {
                break;
            }

            if (BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(offset + 4, 2)) == NlmsgDone)
            {
                return true;
            }

            offset += Align4(msgLen);
        }

        return false;
    }

    private static void TryParseRoute(ReadOnlySpan<byte> rtm, Func<int, string> resolveInterfaceName, List<RouteEntry> routes)
    {
        if (rtm.Length < 12)
        {
            return;
        }

        var family = rtm[0];
        var dstLen = rtm[1];
        var routeType = rtm[7];

        // Only unicast forwarding entries are real routes — local/broadcast/unreachable/blackhole
        // entries (which can appear in any table, including the "local" table 255) are skipped here.
        if (family != AfInet || routeType != RtnUnicast)
        {
            return;
        }

        uint? destination = null;
        uint? gateway = null;
        int? outputInterface = null;
        var metric = 0;

        var attrs = rtm[12..];
        var attrOffset = 0;
        while (attrOffset + 4 <= attrs.Length)
        {
            var rtaLen = BinaryPrimitives.ReadUInt16LittleEndian(attrs.Slice(attrOffset, 2));
            var rtaType = BinaryPrimitives.ReadUInt16LittleEndian(attrs.Slice(attrOffset + 2, 2));
            if (rtaLen < 4 || attrOffset + rtaLen > attrs.Length)
            {
                break;
            }

            var payload = attrs.Slice(attrOffset + 4, rtaLen - 4);
            switch (rtaType)
            {
                case RtaDst when payload.Length >= 4:
                    destination = BinaryPrimitives.ReadUInt32BigEndian(payload);
                    break;
                case RtaGateway when payload.Length >= 4:
                    gateway = BinaryPrimitives.ReadUInt32BigEndian(payload);
                    break;
                case RtaOif when payload.Length >= 4:
                    outputInterface = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    break;
                case RtaPriority when payload.Length >= 4:
                    metric = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    break;
            }

            attrOffset += Align4(rtaLen);
        }

        // No RTA_OIF (e.g. a multipath RTA_MULTIPATH route with several nexthops) — skip rather
        // than guess an interface, matching RouteTableParser's "skip unparseable rather than throw".
        if (outputInterface is null)
        {
            return;
        }

        // Absent RTA_DST with dst_len == 0 is the default route (0.0.0.0/0). Absent RTA_GATEWAY is
        // an on-link route — same "0.0.0.0" convention RouteTableParser's fallback branch uses.
        var destinationAddress = destination ?? 0u;
        var gatewayText = gateway is { } g ? Ipv4Cidr.FormatAddress(g) : "0.0.0.0";
        var interfaceName = resolveInterfaceName(outputInterface.Value);

        routes.Add(new RouteEntry(
            new Ipv4Cidr(destinationAddress, dstLen),
            gatewayText,
            interfaceName,
            outputInterface.Value,
            metric));
    }

    private static int Align4(int length) => (length + 3) & ~3;
}
