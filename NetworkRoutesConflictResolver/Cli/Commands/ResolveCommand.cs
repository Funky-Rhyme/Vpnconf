using NetworkRoutesConflictResolver.Engine;
using NetworkRoutesConflictResolver.Infrastructure.Routes;
using NetworkRoutesConflictResolver.Output;
using NetworkRoutesConflictResolver.Parsing;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli.Commands;

/// <summary>
/// One-shot workflow: read the live routing table, let the user pick the corporate VPN adapter(s),
/// detect conflicts with the personal ip-list, build a minimal patch, and optionally write a
/// patched ip-list. This matches the "connect corporate VPN, then run once" usage.
/// </summary>
public sealed class ResolveCommand
{
    private static readonly IReadOnlySet<string> Flags =
        new HashSet<string> { "dry-run", "backup", "help", "h" };

    private readonly IpListParserRegistry _parsers = new();
    private readonly ConflictDetector _detector = new();
    private readonly PatchPlanner _planner = new();
    private readonly PatchApplier _applier = new();
    private readonly IpListSerializer _ipListSerializer = new();
    private readonly ReportWriter _report = new();

    public async Task<int> ExecuteAsync(string[] args)
    {
        if (Array.Exists(args, x => x is "--help" or "-h"))
        {
            PrintHelp();
            return 0;
        }

        var map = ArgMap.Parse(args, Flags);
        var dryRun = map.HasFlag("dry-run");

        // The ip-list is optional up front: we can still read routes from the VPN and extract them,
        // then ask for the ip-list path afterwards.
        var ipListPath = map.Get("ip-list");
        if (ipListPath is null)
        {
            _report.Warn("No ip-list (CIDR list) provided. The VPN routes will still be extracted; "
                         + "you'll be asked for the ip-list path afterwards.");
        }

        var provider = RouteTableProviderFactory.Create();
        var routes = await provider.GetRoutesAsync();
        var byAdapter = RoutePrompts.GroupByAdapter(routes);

        if (byAdapter.Length == 0)
        {
            _report.Error("No routes were read from the system routing table.");
            return 1;
        }

        _report.WriteAdapters(byAdapter.Select(a => (a.Name, a.Routes.Length)).ToArray());

        var selected = SelectAdapters(map.Get("interface"), byAdapter);
        if (selected.Count == 0)
        {
            _report.Warn("No adapter selected; nothing to resolve.");
            return 0;
        }

        var vpnRoutes = RoutePrompts.ExtractVpnRoutes(selected);
        _report.Success($"Extracted {vpnRoutes.Length} route(s) from: {string.Join(", ", selected.Select(s => s.Name))}");
        _report.WriteExtractedRoutes(vpnRoutes);

        var extractOut = map.Get("extract-out");
        if (extractOut is not null)
        {
            File.WriteAllLines(extractOut, vpnRoutes.Select(c => c.ToString()));
            _report.Success($"Wrote {vpnRoutes.Length} extracted VPN CIDR(s) to {extractOut}");
        }

        // Ask for the ip-list path now if it was not supplied at launch.
        ipListPath ??= RoutePrompts.PromptIpListPath();
        if (ipListPath is null)
        {
            _report.Warn("No ip-list provided — showing extracted VPN routes only. "
                         + "Re-run with --ip-list <file> to resolve conflicts.");
            return 0;
        }

        var ipContent = SafeFile.ReadRequired(ipListPath);
        var parser = RoutePrompts.ResolveParser(_parsers, map.Get("format"), ipContent);
        _report.Success($"Reading ip-list as: {parser.DisplayName}");
        var ipList = parser.Parse(ipContent);
        if (ipList.Invalid.Count > 0)
        {
            _report.Warn($"Skipped {ipList.Invalid.Count} unparseable ip-list entries.");
        }

        var conflicts = _detector.DetectConflicts(vpnRoutes, ipList.Entries);
        _report.WriteConflicts(conflicts);

        if (conflicts.Count == 0)
        {
            return 0;
        }

        var plan = _planner.BuildPlan(conflicts);
        _report.WritePatchPlan(plan);

        var outPath = map.Get("out");
        if (outPath is null)
        {
            _report.Warn("No --out specified; showing plan only. Re-run with --out <file> to write a patched ip-list.");
            return 0;
        }

        if (dryRun)
        {
            _report.Warn($"Dry-run: patched ip-list NOT written to {outPath}.");
            return 0;
        }

        var patched = _applier.Apply(ipList.Entries, plan);
        SafeFile.Write(outPath, _ipListSerializer.Serialize(patched), map.HasFlag("backup"));
        _report.Success($"Wrote patched ip-list ({patched.Count} entries) to {outPath}");
        return 0;
    }

    /// <summary>Uses the explicit <c>--interface</c> list when given; otherwise prompts interactively.</summary>
    private static List<RoutePrompts.Adapter> SelectAdapters(string? interfaceOption, RoutePrompts.Adapter[] adapters)
    {
        if (interfaceOption is not null)
        {
            var wanted = interfaceOption
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return adapters.Where(a => wanted.Contains(a.Name)).ToList();
        }

        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            throw new InvalidOperationException(
                "No interactive terminal available. Pass --interface \"<adapter name>\" (comma-separated for several).");
        }

        return RoutePrompts.SelectAdaptersInteractive(adapters);
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("Usage: [green]vpnconf resolve [[--ip-list <file>]] [[--interface <name>]] [[--out <file>]] [[--extract-out <file>]] [[--backup]] [[--dry-run]][/]");
        AnsiConsole.MarkupLine("Read live routes, pick the corporate VPN adapter, and resolve conflicts against the personal ip-list.");
        AnsiConsole.MarkupLine("[grey]--ip-list is optional: without it, the VPN routes are still extracted and the path is asked interactively.[/]");
        AnsiConsole.MarkupLine("[grey]--extract-out <file> writes the extracted VPN CIDRs (one per line).[/]");
        AnsiConsole.MarkupLine("[grey]--format <auto|json|plain> selects the ip-list parser (default: ask/auto-detect).[/]");
    }
}
