using NetworkRoutesConflictResolver.Engine;
using NetworkRoutesConflictResolver.Output;
using NetworkRoutesConflictResolver.Parsing;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli.Commands;

/// <summary>
/// Applies a patch plan to the personal ip-list and writes a patched file in the original format.
/// Never overwrites the original without an explicit target; creates a backup when overwriting.
/// </summary>
public sealed class ApplyCommand
{
    private static readonly IReadOnlySet<string> Flags = new HashSet<string> { "dry-run", "backup", "help", "h" };

    private readonly IpListParserRegistry _parsers = new();
    private readonly PatchPlanSerializer _planSerializer = new();
    private readonly PatchApplier _applier = new();
    private readonly IpListSerializer _ipListSerializer = new();
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
        var plan = _planSerializer.Deserialize(SafeFile.ReadRequired(map.Require("plan")));

        var patched = _applier.Apply(ipList.Entries, plan);
        _report.WriteAnalyzeSummary(ipList.Entries.Count, patched.Count, plan.Remove.Count, plan.Add.Count);

        var output = map.Require("output");
        if (dryRun)
        {
            _report.Warn($"Dry-run: patched ip-list ({patched.Count} entries) NOT written to {output}.");
            return Task.FromResult(0);
        }

        SafeFile.Write(output, _ipListSerializer.Serialize(patched), map.HasFlag("backup"));
        _report.Success($"Wrote patched ip-list ({patched.Count} entries) to {output}");
        return Task.FromResult(0);
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("Usage: [green]vpnconf apply --ip-list <file> --plan <file> --output <file> [--backup] [--dry-run] [--format <auto|json|plain>][/]");
        AnsiConsole.MarkupLine("Apply a patch plan to the personal ip-list and write a patched copy.");
    }
}
