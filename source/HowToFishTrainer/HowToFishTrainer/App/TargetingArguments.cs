using System.Collections.Generic;
using System.Text.Json;

namespace HowToFishTrainer.App;

internal static class TargetingArguments
{
    public static JsonElement WithTeammates(JsonElement applied, bool enabled, double entityOverlayDistance)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(applied.GetRawText())!;
        values["targetTeammates"] = JsonSerializer.SerializeToElement(enabled);
        values["entityOverlayDistance"] = JsonSerializer.SerializeToElement(entityOverlayDistance);
        return JsonSerializer.SerializeToElement(values);
    }

    public static bool Teammates(JsonElement? applied) => applied.HasValue &&
        applied.Value.TryGetProperty("targetTeammates", out var value) && value.ValueKind == JsonValueKind.True;
}
