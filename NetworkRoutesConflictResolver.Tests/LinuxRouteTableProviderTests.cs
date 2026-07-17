using System.Buffers.Binary;
using NetworkRoutesConflictResolver.Parsing;

namespace NetworkRoutesConflictResolver.Tests;

/// <summary>
/// Exercises <see cref="NetlinkRouteDumpParser.ParseRouteDump"/> — the pure, socket-free half of
/// the Linux live-route provider — against hand-built netlink byte buffers. Netlink route dumps
/// can't be captured on this Windows dev machine, so these buffers are constructed field-by-field
/// per the documented <c>nlmsghdr</c>/<c>rtmsg</c>/<c>rtattr</c> layout
/// (.claude/tasks/live-routes-linux-macos/research-notes.md §1). Replace/augment with a real
/// captured fixture once a Linux box is available.
/// </summary>
public sealed class LinuxRouteTableProviderTests
{
    private const ushort RtmNewRoute = 24;
    private const ushort NlmsgDone = 3;
    private const ushort NlmsgError = 2;
    private const ushort NlmFRequest = 0x1;
    private const ushort NlmFDump = 0x300;
    private const ushort NlmFDumpIntr = 0x10;

    private const byte AfInet = 2;
    private const byte RtnUnicast = 1;
    private const byte RtnBroadcast = 3;

    private const ushort RtaDst = 1;
    private const ushort RtaOif = 4;
    private const ushort RtaGateway = 5;
    private const ushort RtaPriority = 6;

    private static string ResolveName(int index) => $"eth{index}";

    [Fact]
    public void Parses_unicast_route_with_gateway_and_metric()
    {
        var buffer = Concat(
            NewRouteMessage(dstLen: 24, routeType: RtnUnicast, attrs:
            [
                Attr(RtaDst, Ipv4Bytes(10, 0, 0, 0)),
                Attr(RtaGateway, Ipv4Bytes(192, 168, 1, 1)),
                Attr(RtaOif, IntBytes(3)),
                Attr(RtaPriority, IntBytes(50)),
            ]),
            DoneMessage());

        var (routes, interrupted) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.False(interrupted);
        var route = Assert.Single(routes);
        Assert.Equal("10.0.0.0/24", route.Destination.ToString());
        Assert.Equal("192.168.1.1", route.Gateway);
        Assert.Equal("eth3", route.InterfaceName);
        Assert.Equal(3, route.InterfaceIndex);
        Assert.Equal(50, route.Metric);
    }

    [Fact]
    public void Default_route_has_no_RTA_DST_and_is_reported_as_zero_slash_zero()
    {
        var buffer = Concat(
            NewRouteMessage(dstLen: 0, routeType: RtnUnicast, attrs:
            [
                Attr(RtaGateway, Ipv4Bytes(192, 168, 1, 1)),
                Attr(RtaOif, IntBytes(3)),
            ]),
            DoneMessage());

        var (routes, _) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        var route = Assert.Single(routes);
        Assert.Equal("0.0.0.0/0", route.Destination.ToString());
    }

    [Fact]
    public void Onlink_route_without_gateway_reports_zero_address_gateway()
    {
        var buffer = Concat(
            NewRouteMessage(dstLen: 24, routeType: RtnUnicast, attrs:
            [
                Attr(RtaDst, Ipv4Bytes(10, 0, 0, 0)),
                Attr(RtaOif, IntBytes(3)),
            ]),
            DoneMessage());

        var (routes, _) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.Equal("0.0.0.0", Assert.Single(routes).Gateway);
    }

    [Fact]
    public void Non_unicast_route_type_is_skipped()
    {
        var buffer = Concat(
            NewRouteMessage(dstLen: 32, routeType: RtnBroadcast, attrs:
            [
                Attr(RtaDst, Ipv4Bytes(10, 0, 0, 255)),
                Attr(RtaOif, IntBytes(3)),
            ]),
            DoneMessage());

        var (routes, _) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.Empty(routes);
    }

    [Fact]
    public void Route_without_RTA_OIF_is_skipped_rather_than_throwing()
    {
        var buffer = Concat(
            NewRouteMessage(dstLen: 24, routeType: RtnUnicast, attrs:
            [
                Attr(RtaDst, Ipv4Bytes(10, 0, 0, 0)),
                Attr(RtaGateway, Ipv4Bytes(192, 168, 1, 1)),
            ]),
            DoneMessage());

        var (routes, _) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.Empty(routes);
    }

    [Fact]
    public void Routes_from_a_non_main_table_are_kept_not_filtered_out()
    {
        // Policy-routing table (e.g. a VPN client's split-tunnel table), rtm_table = 100.
        var buffer = Concat(
            NewRouteMessage(dstLen: 24, routeType: RtnUnicast, table: 100, attrs:
            [
                Attr(RtaDst, Ipv4Bytes(172, 16, 0, 0)),
                Attr(RtaOif, IntBytes(7)),
            ]),
            DoneMessage());

        var (routes, _) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.Equal("172.16.0.0/24", Assert.Single(routes).Destination.ToString());
    }

    [Fact]
    public void NLMSG_ERROR_with_nonzero_code_throws()
    {
        var buffer = Concat(ErrorMessage(errorCode: -1));

        Assert.Throws<InvalidOperationException>(() => NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName));
    }

    [Fact]
    public void NLMSG_ERROR_ack_with_zero_code_does_not_throw()
    {
        var buffer = Concat(ErrorMessage(errorCode: 0), DoneMessage());

        var (routes, interrupted) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.Empty(routes);
        Assert.False(interrupted);
    }

    [Fact]
    public void DONE_with_DUMP_INTR_flag_reports_interrupted()
    {
        var buffer = Concat(
            NewRouteMessage(dstLen: 24, routeType: RtnUnicast, attrs:
            [
                Attr(RtaDst, Ipv4Bytes(10, 0, 0, 0)),
                Attr(RtaOif, IntBytes(3)),
            ]),
            DoneMessage(flags: NlmFDumpIntr));

        var (routes, interrupted) = NetlinkRouteDumpParser.ParseRouteDump(buffer, ResolveName);

        Assert.Single(routes);
        Assert.True(interrupted);
    }

    // ---- netlink message builders --------------------------------------------------------

    private static byte[] Concat(params byte[][] messages) => messages.SelectMany(m => m).ToArray();

    private static byte[] NewRouteMessage(byte dstLen, byte routeType, byte[][] attrs, byte table = 254)
    {
        var attrBytes = attrs.SelectMany(a => a).ToArray();
        var rtm = new byte[12 + attrBytes.Length];
        rtm[0] = AfInet;   // rtm_family
        rtm[1] = dstLen;   // rtm_dst_len
        rtm[2] = 0;        // rtm_src_len
        rtm[3] = 0;        // rtm_tos
        rtm[4] = table;    // rtm_table
        rtm[5] = 0;        // rtm_protocol
        rtm[6] = 0;        // rtm_scope
        rtm[7] = routeType; // rtm_type
        // rtm_flags (4 bytes) left as 0
        attrBytes.CopyTo(rtm, 12);

        return NlHeader(RtmNewRoute, (ushort)(NlmFRequest | NlmFDump), rtm);
    }

    private static byte[] DoneMessage(ushort flags = 0) => NlHeader(NlmsgDone, flags, []);

    private static byte[] ErrorMessage(int errorCode)
    {
        var payload = new byte[20]; // error (4B) + echoed nlmsghdr (16B, zeroed — unused by the parser)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), errorCode);
        return NlHeader(NlmsgError, 0, payload);
    }

    private static byte[] NlHeader(ushort type, ushort flags, byte[] payload)
    {
        var message = new byte[16 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(0, 4), message.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(6, 2), flags);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(8, 4), 1); // nlmsg_seq
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(12, 4), 0); // nlmsg_pid
        payload.CopyTo(message, 16);
        return message;
    }

    private static byte[] Attr(ushort type, byte[] payload)
    {
        var attr = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(attr.AsSpan(0, 2), (ushort)attr.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(attr.AsSpan(2, 2), type);
        payload.CopyTo(attr, 4);
        return attr; // rtattr payloads used here are all 4 bytes, already 4-byte aligned
    }

    private static byte[] Ipv4Bytes(byte a, byte b, byte c, byte d) => [a, b, c, d];

    private static byte[] IntBytes(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
