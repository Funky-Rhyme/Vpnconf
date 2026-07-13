using System.Numerics;
using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Parsing;

/// <summary>
/// Best-effort parser for textual route dumps (Windows <c>route print</c>, Linux <c>ip route</c>,
/// or a plain one-CIDR-per-line list). Used by the file-based analyze workflow. The live workflow
/// uses <c>IRouteTableProvider</c> instead, which avoids fragile locale-dependent text parsing.
/// Lines that do not yield a destination are skipped rather than throwing.
/// </summary>
public sealed class RouteTableParser
{
    public IReadOnlyList<RouteEntry> Parse(string routeDump)
    {
        ArgumentNullException.ThrowIfNull(routeDump);

        var routes = new List<RouteEntry>();
        foreach (var raw in routeDump.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseLine(line, out var entry))
            {
                routes.Add(entry);
            }
        }

        return routes;
    }

    private static bool TryParseLine(string line, out RouteEntry entry)
    {
        entry = null!;
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        // Linux `ip route`: "<dest>|default [via <gw>] [dev <iface>] [metric <n>] ..."
        if (Array.IndexOf(tokens, "dev") >= 0 || Array.IndexOf(tokens, "via") >= 0 ||
            tokens[0].Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseIpRoute(tokens, out entry);
        }

        // Windows `route print` IPv4 table row: dest netmask gateway iface metric
        if (tokens.Length >= 5 &&
            Ipv4Cidr.TryParse(tokens[0], out var winDest) &&
            TryParseMask(tokens[1], out var winPrefix) &&
            IsIpv4(tokens[3]))
        {
            var metric = int.TryParse(tokens[4], out var m) ? m : 0;
            entry = new RouteEntry(
                new Ipv4Cidr(winDest.Network, winPrefix),
                tokens[2],
                tokens[3],
                0,
                metric);
            return true;
        }

        // Generic fallback: first token that parses as a CIDR (or bare address = /32).
        if (Ipv4Cidr.TryParse(tokens[0], out var cidr))
        {
            entry = new RouteEntry(cidr, "0.0.0.0", string.Empty, 0, 0);
            return true;
        }

        return false;
    }

    private static bool TryParseIpRoute(string[] tokens, out RouteEntry entry)
    {
        entry = null!;

        var destText = tokens[0].Equals("default", StringComparison.OrdinalIgnoreCase) ? "0.0.0.0/0" : tokens[0];
        if (!Ipv4Cidr.TryParse(destText, out var dest))
        {
            return false;
        }

        var gateway = "0.0.0.0";
        var iface = string.Empty;
        var metric = 0;

        for (var i = 1; i < tokens.Length - 1; i++)
        {
            switch (tokens[i])
            {
                case "via":
                    gateway = tokens[i + 1];
                    break;
                case "dev":
                    iface = tokens[i + 1];
                    break;
                case "metric":
                    _ = int.TryParse(tokens[i + 1], out metric);
                    break;
            }
        }

        entry = new RouteEntry(dest, gateway, iface, 0, metric);
        return true;
    }

    private static bool TryParseMask(string text, out int prefix)
    {
        prefix = 0;
        if (!Ipv4Cidr.TryParse(text, out var mask))
        {
            return false;
        }

        // Reject non-contiguous masks (would not be a valid prefix).
        prefix = BitOperations.PopCount(mask.Network);
        return Ipv4Cidr.MaskFor(prefix) == mask.Network;
    }

    private static bool IsIpv4(string text) => Ipv4Cidr.TryParse(text, out var c) && c.Prefix == 32;
}
