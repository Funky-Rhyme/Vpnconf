using NetworkRoutesConflictResolver.Engine;
using NetworkRoutesConflictResolver.Output;
using NetworkRoutesConflictResolver.Parsing;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli.Commands;

/// <summary>
/// Compares before/after route dumps, finds VPN-added routes that overlap the personal ip-list,
/// and writes the conflicting VPN CIDRs to a file consumable by <c>plan</c>.
/// </summary>
public sealed class AnalyzeCommand
{
    private static readonly IReadOnlySet<string> Flags = new HashSet<string> { "help", "h" };

    private readonly IpListParserRegistry _parsers = new();
    private readonly RouteTableParser _routeParser = new();
    private readonly ConflictDetector _detector = new();
    private readonly CidrMinimizer _minimizer = new();
    private readonly ReportWriter _report = new();

    public Task<int> ExecuteAsync(string[] args)
    {
        if (Array.Exists(args, x => x is "--help" or "-h"))
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        var map = ArgMap.Parse(args, Flags);

        var ipContent = SafeFile.ReadRequired(map.Require("ip-list"));
        var ipList = _parsers.Resolve(map.Get("format"), ipContent).Parse(ipContent);
        var before = _routeParser.Parse(SafeFile.ReadRequired(map.Require("before")));
        var after = _routeParser.Parse(SafeFile.ReadRequired(map.Require("after")));

        var added = _detector.ComputeAddedRoutes(before, after);
        var conflicts = _detector.DetectConflicts(added.Select(r => r.Destination), ipList.Entries);

        _report.WriteAnalyzeSummary(before.Count, after.Count, added.Count, conflicts.Count);
        _report.WriteConflicts(conflicts);

        if (ipList.Invalid.Count > 0)
        {
            _report.Warn($"Skipped {ipList.Invalid.Count} unparseable ip-list entries.");
        }

        var outPath = map.Get("out");
        if (outPath is not null)
        {
            var blocks = _minimizer.Minimize(conflicts.SelectMany(c => c.ConflictingRoutes));
            File.WriteAllLines(outPath, blocks.Select(b => b.ToString()));
            _report.Success($"Wrote {blocks.Count} conflicting VPN CIDR(s) to {outPath}");
        }

        return Task.FromResult(0);
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("Usage: [green]vpnconf analyze --ip-list <file> --before <file> --after <file> [--out <file>] [--format <auto|json|plain>][/]");
        AnsiConsole.MarkupLine("Compare before/after route dumps and report conflicts with the personal ip-list.");
    }
}
