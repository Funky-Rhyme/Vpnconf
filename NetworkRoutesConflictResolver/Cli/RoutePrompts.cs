using NetworkRoutesConflictResolver.Model;
using NetworkRoutesConflictResolver.Parsing;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Cli;

/// <summary>
/// Reusable route/ip-list helpers shared by the one-shot <c>resolve</c> command and the interactive
/// menu. Keeps adapter grouping, VPN-route extraction, and interactive prompts in one place.
/// </summary>
public static class RoutePrompts
{
    public readonly record struct Adapter(string Name, RouteEntry[] Routes);

    /// <summary>Groups routes by adapter, ordered by descending route count for a useful default.</summary>
    public static Adapter[] GroupByAdapter(IReadOnlyList<RouteEntry> routes)
        => routes
            .GroupBy(r => r.InterfaceName)
            .Select(g => new Adapter(g.Key, g.ToArray()))
            .OrderByDescending(a => a.Routes.Length)
            .ToArray();

    /// <summary>Distinct, sorted, conflict-relevant destination blocks from the given adapters.</summary>
    public static Ipv4Cidr[] ExtractVpnRoutes(IEnumerable<Adapter> selected)
        => selected
            .SelectMany(a => a.Routes)
            .Select(r => r.Destination)
            .Where(IsRelevant)
            .Distinct()
            .OrderBy(c => c)
            .ToArray();

    /// <summary>Interactive multi-select of adapters whose routes should be treated as the VPN's.</summary>
    public static List<Adapter> SelectAdaptersInteractive(IReadOnlyList<Adapter> adapters)
    {
        var chosen = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select the [green]corporate VPN[/] adapter(s) whose routes to resolve:")
                .NotRequired()
                .InstructionsText("[grey](space to toggle, enter to confirm)[/]")
                .AddChoices(adapters.Select(a => a.Name)));

        var chosenSet = chosen.ToHashSet(StringComparer.Ordinal);
        return adapters.Where(a => chosenSet.Contains(a.Name)).ToList();
    }

    /// <summary>
    /// Interactively asks for the ip-list path. Returns the entered path, or <c>null</c> if the
    /// user skips it or there is no interactive terminal.
    /// </summary>
    public static string? PromptIpListPath()
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return null;
        }

        var path = AnsiConsole.Prompt(
            new TextPrompt<string>("Path to the [green]ip-list (CIDR)[/] file (leave empty to skip):")
                .AllowEmpty()
                .Validate(p => string.IsNullOrWhiteSpace(p) || File.Exists(p.Trim())
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]File not found — try again or leave empty to skip[/]")));

        path = path.Trim();
        return path.Length == 0 ? null : path;
    }

    /// <summary>
    /// Chooses the ip-list parser: an explicit <paramref name="format"/> wins; otherwise, in an
    /// interactive terminal the user picks the format (including "auto"); else we auto-detect.
    /// </summary>
    public static IIpListParser ResolveParser(IpListParserRegistry registry, string? format, string content)
    {
        if (format is not null)
        {
            return registry.Resolve(format, content);
        }

        if (AnsiConsole.Profile.Capabilities.Interactive)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select the [green]ip-list format[/]:")
                    .AddChoices(registry.AvailableFormats));
            return registry.Resolve(choice, content);
        }

        return registry.AutoDetect(content);
    }

    /// <summary>
    /// Excludes routes that would not represent a meaningful split-tunnel conflict: the default
    /// route, broadcast, and multicast. A default route means a full-tunnel VPN — a different case.
    /// </summary>
    public static bool IsRelevant(Ipv4Cidr cidr)
    {
        if (cidr.Prefix == 0)
        {
            return false;
        }

        var broadcast = new Ipv4Cidr(0xFFFFFFFF, 32);
        var multicast = new Ipv4Cidr(0xE0000000, 4); // 224.0.0.0/4
        return cidr != broadcast && !multicast.Contains(cidr);
    }
}
