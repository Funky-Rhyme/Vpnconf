using NetworkRoutesConflictResolver.Engine;
using NetworkRoutesConflictResolver.Model;
using NetworkRoutesConflictResolver.Output;
using NetworkRoutesConflictResolver.Parsing;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli.Commands;

/// <summary>
/// Builds a minimal patch plan: subtracts the conflicting VPN CIDRs from each affected personal
/// CIDR and emits remove/add sets as JSON.
/// </summary>
public sealed class PlanCommand
{
    private static readonly IReadOnlySet<string> Flags = new HashSet<string> { "dry-run", "help", "h" };

    private readonly IpListParserRegistry _parsers = new();
    private readonly ConflictDetector _detector = new();
    private readonly PatchPlanner _planner = new();
    private readonly PatchPlanSerializer _planSerializer = new();
    private readonly ReportWriter _report = new();

    public Task<int> ExecuteAsync(string[] args)
    {
        if (Array.Exists(args, x => x is "--help" or "-h"))
        {
            PrintHelp();
            return Task.FromResult(0);
        }

        var map = ArgMap.Parse(args, Flags);
        var dryRun = map.HasFlag("dry-run");

        var ipContent = SafeFile.ReadRequired(map.Require("ip-list"));
        var ipList = _parsers.Resolve(map.Get("format"), ipContent).Parse(ipContent);
        var blocked = ReadConflictBlocks(SafeFile.ReadRequired(map.Require("conflicts")));

        var conflicts = _detector.DetectConflicts(blocked, ipList.Entries);
        var plan = _planner.BuildPlan(conflicts);

        _report.WritePatchPlan(plan);

        var outPath = map.Get("out");
        if (outPath is null)
        {
            return Task.FromResult(0);
        }

        if (dryRun)
        {
            _report.Warn($"Dry-run: patch plan NOT written to {outPath}.");
        }
        else
        {
            File.WriteAllText(outPath, _planSerializer.Serialize(plan));
            _report.Success($"Wrote patch plan to {outPath}");
        }

        return Task.FromResult(0);
    }

    private static List<Ipv4Cidr> ReadConflictBlocks(string content)
    {
        var blocks = new List<Ipv4Cidr>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && Ipv4Cidr.TryParse(trimmed, out var cidr))
            {
                blocks.Add(cidr);
            }
        }

        return blocks;
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("Usage: [green]vpnconf plan --ip-list <file> --conflicts <file> [--out <file>] [--dry-run] [--format <auto|json|plain>][/]");
        AnsiConsole.MarkupLine("Subtract conflicting VPN CIDRs from the personal ip-list and emit a remove/add patch plan.");
    }
}
