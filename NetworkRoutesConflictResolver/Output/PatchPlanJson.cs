using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkRoutesConflictResolver.Model;

namespace NetworkRoutesConflictResolver.Output;

/// <summary>Source-generated JSON context for the patch plan (AOT-safe, camelCase, indented).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PatchPlan))]
public sealed partial class PatchPlanJsonContext : JsonSerializerContext
{
}

/// <summary>Reads and writes <see cref="PatchPlan"/> as deterministic JSON.</summary>
public sealed class PatchPlanSerializer
{
    public string Serialize(PatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, PatchPlanJsonContext.Default.PatchPlan);
    }

    public PatchPlan Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize(json, PatchPlanJsonContext.Default.PatchPlan)
                   ?? throw new FormatException("Patch plan JSON deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Patch plan is not valid JSON: {ex.Message}", ex);
        }
    }
}
