using NetworkRoutesConflictResolver.Model;
using Spectre.Console;

namespace NetworkRoutesConflictResolver.Output;

/// <summary>User-facing terminal reporting. Colors are used semantically, never as the only signal.</summary>
public sealed class ReportWriter
{
    public void WriteAnalyzeSummary(int beforeCount, int afterCount, int addedCount, int conflictCount)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Analyze summary[/]");
        table.AddColumn("Metric");
        table.AddColumn(new TableColumn("Value").RightAligned());
        table.AddRow("Routes before", beforeCount.ToString());
        table.AddRow("Routes after", afterCount.ToString());
        table.AddRow("VPN-added routes", addedCount.ToString());
        table.AddRow(
            conflictCount > 0 ? "[red]Conflicting ip-list CIDRs[/]" : "[green]Conflicting ip-list CIDRs[/]",
            conflictCount.ToString());
        AnsiConsole.Write(table);
    }

    public void WriteConflicts(IReadOnlyList<RouteConflict> conflicts)
    {
        if (conflicts.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No conflicts:[/] the VPN routes do not overlap the personal ip-list.");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Conflicts[/]");
        table.AddColumn("Personal CIDR");
        table.AddColumn("Overlapping VPN routes");

        foreach (var conflict in conflicts)
        {
            table.AddRow(
                Markup.Escape(conflict.PersonalCidr.ToString()),
                Markup.Escape(string.Join(", ", conflict.ConflictingRoutes)));
        }

        AnsiConsole.Write(table);
    }

    public void WritePatchPlan(PatchPlan plan)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Patch plan[/]");
        table.AddColumn("[red]Remove[/]");
        table.AddColumn("[green]Add (replacement)[/]");

        foreach (var detail in plan.Details)
        {
            var replacement = detail.Replacement.Count == 0
                ? "[grey](nothing — fully covered by VPN)[/]"
                : Markup.Escape(string.Join("\n", detail.Replacement));
            table.AddRow(Markup.Escape(detail.Original), replacement);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"Plan: remove [red]{plan.Remove.Count}[/] CIDR(s), add [green]{plan.Add.Count}[/] CIDR(s).");
    }

    public void WriteAdapters(IReadOnlyList<(string Name, int RouteCount)> adapters)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Detected adapters[/]");
        table.AddColumn("Adapter");
        table.AddColumn(new TableColumn("Routes").RightAligned());
        foreach (var (name, count) in adapters)
        {
            table.AddRow(Markup.Escape(name), count.ToString());
        }

        AnsiConsole.Write(table);
    }

    public void WriteExtractedRoutes(IReadOnlyList<Ipv4Cidr> routes)
    {
        if (routes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No relevant IPv4 routes found on the selected adapter(s).[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded)
            .Title($"[bold]Extracted VPN routes ({routes.Count})[/]");
        table.AddColumn("CIDR");
        foreach (var route in routes)
        {
            table.AddRow(Markup.Escape(route.ToString()));
        }

        AnsiConsole.Write(table);
    }

    public void Success(string message) => AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");

    public void Warn(string message) => AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");

    public void Error(string message) => AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
}
