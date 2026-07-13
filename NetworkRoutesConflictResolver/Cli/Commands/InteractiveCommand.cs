using NetworkRoutesConflictResolver.Engine;
using NetworkRoutesConflictResolver.Infrastructure.Routes;
using NetworkRoutesConflictResolver.Model;
using NetworkRoutesConflictResolver.Output;
using NetworkRoutesConflictResolver.Parsing;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli.Commands;

/// <summary>
/// Interactive menu that stays open so the user can launch the app, go connect the corporate VPN,
/// come back, and only then trigger route collection and conflict resolution. State (the extracted
/// VPN routes) persists between menu actions until the user exits.
/// </summary>
public sealed class InteractiveCommand
{
    private const string ActionCollect = "Collect routes from system (do this AFTER connecting the corporate VPN)";
    private const string ActionResolve = "Resolve conflicts against an ip-list";
    private const string ActionSave = "Save extracted VPN routes to a file";
    private const string ActionHelp = "Help";
    private const string ActionExit = "Exit";

    private readonly IpListParserRegistry _parsers = new();
    private readonly ConflictDetector _detector = new();
    private readonly PatchPlanner _planner = new();
    private readonly PatchApplier _applier = new();
    private readonly IpListSerializer _ipListSerializer = new();
    private readonly ReportWriter _report = new();

    // Session state carried across menu iterations.
    private Ipv4Cidr[] _vpnRoutes = [];
    private string _lastSource = string.Empty;

    public async Task<int> ExecuteAsync(string[] args)
    {
        if (Array.Exists(args, x => x is "--help" or "-h"))
        {
            PrintHelp();
            return 0;
        }

        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            _report.Error("The interactive menu requires a terminal. Use 'resolve'/'analyze' for non-interactive runs.");
            return 2;
        }

        AnsiConsole.Write(new Rule("[bold]vpnconf[/] — interactive mode").LeftJustified());
        AnsiConsole.MarkupLine("[grey]Tip: launch this, connect your corporate VPN, then choose \"Collect routes\".[/]");

        while (true)
        {
            var action = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("\n[bold]Main menu[/] — what would you like to do?")
                    .AddChoices(ActionCollect, ActionResolve, ActionSave, ActionHelp, ActionExit));

            try
            {
                switch (action)
                {
                    case ActionCollect:
                        await CollectAsync();
                        break;
                    case ActionResolve:
                        Resolve();
                        break;
                    case ActionSave:
                        SaveExtracted();
                        break;
                    case ActionHelp:
                        PrintHelp();
                        break;
                    case ActionExit:
                        AnsiConsole.MarkupLine("[grey]Bye.[/]");
                        return 0;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException
                                           or FormatException or InvalidOperationException
                                           or PlatformNotSupportedException)
            {
                // Keep the menu alive on recoverable errors.
                _report.Error(ex.Message);
            }
        }
    }

    private async Task CollectAsync()
    {
        var provider = RouteTableProviderFactory.Create();
        var routes = await provider.GetRoutesAsync();
        var byAdapter = RoutePrompts.GroupByAdapter(routes);

        if (byAdapter.Length == 0)
        {
            _report.Error("No routes were read from the system routing table.");
            return;
        }

        _report.WriteAdapters(byAdapter.Select(a => (a.Name, a.Routes.Length)).ToArray());

        var selected = RoutePrompts.SelectAdaptersInteractive(byAdapter);
        if (selected.Count == 0)
        {
            _report.Warn("No adapter selected; extracted routes unchanged.");
            return;
        }

        _vpnRoutes = RoutePrompts.ExtractVpnRoutes(selected);
        _lastSource = string.Join(", ", selected.Select(s => s.Name));
        _report.Success($"Extracted {_vpnRoutes.Length} route(s) from: {_lastSource}");
        _report.WriteExtractedRoutes(_vpnRoutes);
    }

    private void Resolve()
    {
        if (_vpnRoutes.Length == 0)
        {
            _report.Warn("No routes collected yet. Choose \"Collect routes\" first.");
            return;
        }

        var path = RoutePrompts.PromptIpListPath();
        if (path is null)
        {
            _report.Warn("No ip-list provided; resolution skipped.");
            return;
        }

        var content = SafeFile.ReadRequired(path);
        var parser = RoutePrompts.ResolveParser(_parsers, format: null, content);
        _report.Success($"Reading ip-list as: {parser.DisplayName}");
        var ipList = parser.Parse(content);
        if (ipList.Invalid.Count > 0)
        {
            _report.Warn($"Skipped {ipList.Invalid.Count} unparseable ip-list entries.");
        }

        var conflicts = _detector.DetectConflicts(_vpnRoutes, ipList.Entries);
        _report.WriteConflicts(conflicts);
        if (conflicts.Count == 0)
        {
            return;
        }

        var plan = _planner.BuildPlan(conflicts);
        _report.WritePatchPlan(plan);

        if (!AnsiConsole.Confirm("Write a patched ip-list to a file?", defaultValue: false))
        {
            return;
        }

        var outPath = AnsiConsole.Ask<string>("Output file path:");
        var backup = false;
        if (File.Exists(outPath))
        {
            if (!AnsiConsole.Confirm($"'{outPath}' exists. Create a .bak and overwrite?", defaultValue: false))
            {
                _report.Warn("Write cancelled.");
                return;
            }

            backup = true;
        }

        var patched = _applier.Apply(ipList.Entries, plan);
        SafeFile.Write(outPath, _ipListSerializer.Serialize(patched), backup);
        _report.Success($"Wrote patched ip-list ({patched.Count} entries) to {outPath}");
    }

    private void SaveExtracted()
    {
        if (_vpnRoutes.Length == 0)
        {
            _report.Warn("No routes collected yet. Choose \"Collect routes\" first.");
            return;
        }

        var outPath = AnsiConsole.Ask<string>("Save extracted VPN routes to file:");
        File.WriteAllLines(outPath, _vpnRoutes.Select(c => c.ToString()));
        _report.Success($"Wrote {_vpnRoutes.Length} extracted VPN CIDR(s) to {outPath}");
    }

    private static void PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold]Interactive workflow:[/]");
        AnsiConsole.MarkupLine("  1. Launch the app (you are here).");
        AnsiConsole.MarkupLine("  2. Connect your corporate VPN in the OS.");
        AnsiConsole.MarkupLine("  3. Choose [yellow]Collect routes[/] — pick the VPN adapter(s); routes are extracted.");
        AnsiConsole.MarkupLine("  4. Choose [yellow]Resolve conflicts[/] — provide the ip-list; review and optionally write a patched copy.");
        AnsiConsole.MarkupLine("[grey]Also available non-interactively: vpnconf resolve/analyze/plan/apply.[/]");
    }
}
