using NetworkRoutesConflictResolver.Cli.Commands;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli;

public sealed class CliApplication
{
    private readonly InteractiveCommand _interactiveCommand;
    private readonly ResolveCommand _resolveCommand;
    private readonly AnalyzeCommand _analyzeCommand;
    private readonly PlanCommand _planCommand;
    private readonly ApplyCommand _applyCommand;

    private CliApplication(
        InteractiveCommand interactiveCommand,
        ResolveCommand resolveCommand,
        AnalyzeCommand analyzeCommand,
        PlanCommand planCommand,
        ApplyCommand applyCommand)
    {
        _interactiveCommand = interactiveCommand;
        _resolveCommand = resolveCommand;
        _analyzeCommand = analyzeCommand;
        _planCommand = planCommand;
        _applyCommand = applyCommand;
    }

    public static CliApplication CreateDefault()
    {
        return new CliApplication(
            new InteractiveCommand(),
            new ResolveCommand(),
            new AnalyzeCommand(),
            new PlanCommand(),
            new ApplyCommand());
    }

    public async Task<int> RunAsync(string[] args)
    {
        // No command: launch the interactive menu in a terminal; print help for scripts/pipes.
        if (args.Length == 0)
        {
            return AnsiConsole.Profile.Capabilities.Interactive
                ? await _interactiveCommand.ExecuteAsync([])
                : PrintHelp();
        }

        try
        {
            return args[0] switch
            {
                "menu" or "interactive" => await _interactiveCommand.ExecuteAsync(args.Skip(1).ToArray()),
                "resolve" => await _resolveCommand.ExecuteAsync(args.Skip(1).ToArray()),
                "analyze" => await _analyzeCommand.ExecuteAsync(args.Skip(1).ToArray()),
                "plan" => await _planCommand.ExecuteAsync(args.Skip(1).ToArray()),
                "apply" => await _applyCommand.ExecuteAsync(args.Skip(1).ToArray()),
                "--help" or "-h" => PrintHelp(),
                _ => PrintUnknownCommand(args[0])
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException
                                       or FormatException or InvalidOperationException
                                       or PlatformNotSupportedException)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold]vpnconf[/] - VPN route conflict resolver");
        AnsiConsole.MarkupLine("Usage: [green]vpnconf <command> [[options]][/]");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("Commands:");
        AnsiConsole.MarkupLine("  [yellow]menu[/]     Interactive mode: stays open so you can connect the VPN, then collect routes");
        AnsiConsole.MarkupLine("  [yellow]resolve[/]  Live one-shot: read routes, pick VPN adapter, resolve conflicts");
        AnsiConsole.MarkupLine("  [yellow]analyze[/]  Compare before/after route dumps and detect conflicts");
        AnsiConsole.MarkupLine("  [yellow]plan[/]     Build patch plan with remove/add CIDR sets");
        AnsiConsole.MarkupLine("  [yellow]apply[/]    Apply patch plan to ip-list");
        AnsiConsole.MarkupLine("");
        AnsiConsole.MarkupLine("Run [green]vpnconf <command> --help[/] for command options.");
        return 0;
    }

    private static int PrintUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(command)}");
        PrintHelp();
        return 2;
    }
}
