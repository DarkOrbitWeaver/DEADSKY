using DEADSKY.Core.Entities;

namespace DEADSKY.Core.Weapons;

public sealed record WeaponDefinition(
    string Id,
    string DisplayName,
    string ShortCode,
    GuidanceMode GuidanceMode,
    double MinRangeNm,
    double MaxRangeNm,
    double MinAltitudeFt,
    double MaxAltitudeFt,
    double BaseSingleShotPk,
    bool RequiresRadarSupport,
    bool SupportsTerminalHandoff,
    bool CanAbortInFlight,
    bool SusceptibleToChaff,
    bool SusceptibleToFlares,
    string Description,
    string Tooltip);

public sealed record WeaponSelectionState(
    string CurrentWeaponId,
    IReadOnlyList<string> AvailableWeaponIds);

public static class WeaponCatalog
{
    public const string BaselineSarhWeaponId = "9m38_sarh";
    public const string LongRangeSarhWeaponId = "48n6_long_range";
    public const string ShortRangeIrWeaponId = "9m331_ir";

    private static readonly IReadOnlyDictionary<string, WeaponDefinition> Definitions =
        new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [BaselineSarhWeaponId] = new(
                Id: BaselineSarhWeaponId,
                DisplayName: "9M38 Medium-Range SAM",
                ShortCode: "9M38",
                GuidanceMode: GuidanceMode.SemiActiveRadar,
                MinRangeNm: 2.0,
                MaxRangeNm: 18.0,
                MinAltitudeFt: 50,
                MaxAltitudeFt: 72_000,
                BaseSingleShotPk: 0.70,
                RequiresRadarSupport: true,
                SupportsTerminalHandoff: false,
                CanAbortInFlight: true,
                SusceptibleToChaff: true,
                SusceptibleToFlares: false,
                Description: "Baseline medium-range radar-guided missile for most intercepts.",
                Tooltip: "Needs radar support from TWS/STT. Strong all-rounder, but chaff and broken illumination can spoil the shot."),
            [LongRangeSarhWeaponId] = new(
                Id: LongRangeSarhWeaponId,
                DisplayName: "48N6 Long-Range SAM",
                ShortCode: "48N6",
                GuidanceMode: GuidanceMode.ActiveRadar,
                MinRangeNm: 5.0,
                MaxRangeNm: 32.0,
                MinAltitudeFt: 200,
                MaxAltitudeFt: 90_000,
                BaseSingleShotPk: 0.78,
                RequiresRadarSupport: true,
                SupportsTerminalHandoff: true,
                CanAbortInFlight: true,
                SusceptibleToChaff: true,
                SusceptibleToFlares: false,
                Description: "Longer-legged missile with better terminal autonomy and harder punch.",
                Tooltip: "Prefers clean tracks and early support. Best used before the raid collapses into the inner ring."),
            [ShortRangeIrWeaponId] = new(
                Id: ShortRangeIrWeaponId,
                DisplayName: "9M331 IR Point-Defense Missile",
                ShortCode: "9M331-IR",
                GuidanceMode: GuidanceMode.Infrared,
                MinRangeNm: 0.6,
                MaxRangeNm: 8.0,
                MinAltitudeFt: 25,
                MaxAltitudeFt: 30_000,
                BaseSingleShotPk: 0.64,
                RequiresRadarSupport: false,
                SupportsTerminalHandoff: false,
                CanAbortInFlight: false,
                SusceptibleToChaff: false,
                SusceptibleToFlares: true,
                Description: "Short-range heat-seeking missile for close defense and degraded-radar fights.",
                Tooltip: "Does not need continuous radar support, but cold-aspect targets and flares reduce acquisition quality.")
        };

    public static IReadOnlyList<WeaponDefinition> DefaultLoadout { get; } =
    [
        Definitions[BaselineSarhWeaponId]
    ];

    public static WeaponDefinition Get(string weaponId)
    {
        if (Definitions.TryGetValue(weaponId, out var definition))
            return definition;

        if (Definitions.TryGetValue(BaselineSarhWeaponId, out var fallback))
            return fallback;

        throw new InvalidOperationException("Weapon catalog is missing the baseline entry.");
    }

    public static WeaponDefinition FromLegacyType(string legacyMissileType) => legacyMissileType.ToUpperInvariant() switch
    {
        "48N6" => Get(LongRangeSarhWeaponId),
        "9M331-IR" or "9M331" => Get(ShortRangeIrWeaponId),
        _ => Get(BaselineSarhWeaponId)
    };

    public static IReadOnlyList<WeaponDefinition> ResolveAvailable(IEnumerable<string>? weaponIds)
    {
        var resolved = new List<WeaponDefinition>();
        if (weaponIds != null)
        {
            foreach (var weaponId in weaponIds)
            {
                var definition = Get(weaponId);
                if (resolved.All(existing => !existing.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)))
                    resolved.Add(definition);
            }
        }

        if (resolved.Count == 0)
            resolved.Add(Get(BaselineSarhWeaponId));

        return resolved;
    }

    public static void EnsureBatteryWeapons(SAMBattery battery)
    {
        if (battery.AvailableWeaponIds.Count == 0)
            battery.AvailableWeaponIds.Add(BaselineSarhWeaponId);

        if (string.IsNullOrWhiteSpace(battery.CurrentWeaponId))
            battery.CurrentWeaponId = battery.AvailableWeaponIds[0];

        if (battery.AvailableWeaponIds.All(id => !id.Equals(battery.CurrentWeaponId, StringComparison.OrdinalIgnoreCase)))
            battery.AvailableWeaponIds.Insert(0, battery.CurrentWeaponId);

        ApplyToBattery(battery, Get(battery.CurrentWeaponId));
    }

    public static bool TrySelect(SAMBattery battery, string weaponId)
    {
        if (battery.AvailableWeaponIds.All(id => !id.Equals(weaponId, StringComparison.OrdinalIgnoreCase)))
            return false;

        battery.CurrentWeaponId = weaponId;
        ApplyToBattery(battery, Get(weaponId));
        return true;
    }

    public static void Unlock(SAMBattery battery, string weaponId, bool autoSelect = false)
    {
        if (battery.AvailableWeaponIds.All(id => !id.Equals(weaponId, StringComparison.OrdinalIgnoreCase)))
            battery.AvailableWeaponIds.Add(weaponId);

        if (autoSelect)
            battery.CurrentWeaponId = weaponId;

        EnsureBatteryWeapons(battery);
    }

    public static string BuildCountermeasureRiskText(WeaponDefinition weapon) =>
        weapon.SusceptibleToChaff && weapon.SusceptibleToFlares
            ? "Countermeasure risk: chaff and flares."
            : weapon.SusceptibleToChaff
                ? "Countermeasure risk: chaff."
                : weapon.SusceptibleToFlares
                    ? "Countermeasure risk: flares."
                    : "Countermeasure risk: low.";

    private static void ApplyToBattery(SAMBattery battery, WeaponDefinition definition)
    {
        battery.CurrentMissileType = definition.ShortCode;
        battery.MissileMinRangeNm = definition.MinRangeNm;
        battery.MissileMaxRangeNm = definition.MaxRangeNm;
        battery.MissileMinAltFt = definition.MinAltitudeFt;
        battery.MissileMaxAltFt = definition.MaxAltitudeFt;
        battery.MissileSingleShotPk = definition.BaseSingleShotPk;

        foreach (var launcher in battery.Launchers)
        {
            if (launcher.State == LauncherState.Ready)
                launcher.MissileType = definition.ShortCode;
        }
    }
}
