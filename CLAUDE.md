# CLAUDE.md — agent onboarding

`vpnconf` — cross-platform C# / .NET 10 CLI that lets a personal VPN coexist with a corporate one.
It reads routes the corporate VPN added, finds overlaps with a personal split-tunnel CIDR list, and
"punches holes": subtracts the corporate subnets from the personal CIDRs and re-covers the remainder
with a minimal CIDR set. Scope is **IPv4 only**.

Read this first, then `README.md` (usage), `.cursor/docs/vpn-route-conflict-cli-spec.md` (full spec +
"Реализовано" + "Extending"), and `.cursor/rules/project-standards.mdc` (quality bar).

## Hard invariants — do not violate

- **Never modify `.cursor/11.02.2026_proxy-list.json`.** It is reference test data (2667 CIDRs).
- **Never overwrite a file without `--backup`.** File writes for the ip-list go through
  `Cli/SafeFile.Write`, which refuses to clobber an existing file unless a backup is made. Keep this.
- **`Engine` stays pure and deterministic** — no I/O, no OS calls, same input ⇒ same output. OS- and
  filesystem-specific code lives behind adapters in `Infrastructure` / `Cli`.
- **Keep it AOT-safe.** `PublishAot=true`. Use System.Text.Json source generation (see
  `Parsing/IpListJson.cs`, `Output/PatchPlanJson.cs`), not reflection-based (de)serialization. No
  `JsonSerializer.Serialize(object, Type, options)` overloads — they warn under AOT.
- **IPv4 only** right now. IPv6 is out of scope unless explicitly asked.

## Build / test / run

```
dotnet build                                   # whole solution
dotnet test                                     # xUnit engine/parser tests (currently 32)
dotnet run --project NetworkRoutesConflictResolver -- <command> [options]
./publish-aot.ps1                               # native AOT binary (needs VS C++ workload)
```

The output binary is named `vpnconf` (via `<AssemblyName>` in the csproj), not the project name.
The interactive menu (`vpnconf` with no args, or `vpnconf menu`) needs a real terminal (TTY).

## Module map (modular monolith)

| Project folder | Responsibility | Key types |
|---|---|---|
| `Model` | Immutable data contracts | `Ipv4Cidr` + `Ipv4Ranges` (the CIDR workhorse), `RouteEntry`, `CidrEntry`, `PatchPlan`, `RouteConflict` |
| `Engine` | Pure algorithms | `ConflictDetector`, `CidrSubtractEngine`, `CidrMinimizer`, `PatchPlanner`, `PatchApplier` |
| `Parsing` | Input normalization | `IIpListParser`/`IpListParserRegistry` (+ `Json`/`PlainText` impls), `RouteTableParser` (text dumps) |
| `Infrastructure/Routes` | OS adapters | `IRouteTableProvider` → `WindowsRouteTableProvider` (P/Invoke `GetIpForwardTable`); `RouteTableProviderFactory` |
| `Output` | Reports + serialization | `ReportWriter` (Spectre.Console), `IpListSerializer`, `PatchPlanSerializer` |
| `Cli` | Args + commands | `CliApplication`, `Commands/*` (`Interactive`,`Resolve`,`Analyze`,`Plan`,`Apply`), `ArgMap`, `SafeFile`, `RoutePrompts` |

Data flow: routes (live provider or text dump) + personal ip-list → `ConflictDetector` → `PatchPlanner`
→ `PatchApplier` → `IpListSerializer`. `RoutePrompts` holds the interactive helpers shared by
`ResolveCommand` and `InteractiveCommand`.

## Extension points (where to add things)

- **New ip-list format:** implement `IIpListParser`, register it in `IpListParserRegistry`. It becomes
  available via `--format` and in the interactive format menu automatically. See spec → "Extending".
- **Live routes on macOS/Linux:** implement `IRouteTableProvider` (return `RouteEntry[]` directly),
  wire it into `RouteTableProviderFactory`. Concrete recipe in the spec → "Extending".
- **Writing patched output in a non-JSON format** is not implemented yet — `IpListSerializer` always
  writes JSON. A symmetric writer layer would be the next step (spec notes this).

## Conventions

- Comments and code idiom match the surrounding files; UI strings are English, docs/spec are Russian.
- Every non-trivial engine change should keep the xUnit suite green and add cases for new behavior.
- Deterministic, sorted, deduplicated output where order matters (see `project-standards.mdc`).
- **Git commits: no email addresses in commit messages** — do not add `Co-Authored-By`, `Signed-off-by`,
  or any other email/trailer lines. Keep the message to the subject and body only.
