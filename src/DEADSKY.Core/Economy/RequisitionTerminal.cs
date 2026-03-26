using System;
using System.Linq;
using DEADSKY.Core.Entities;
using DEADSKY.Core.Progression;
using DEADSKY.Core.Simulation;
using DEADSKY.Core.Weapons;

namespace DEADSKY.Core.Economy;

public enum RequisitionCategory
{
    Radar,
    Missiles,
    Launchers,
    Electronics
}

public sealed record RequisitionItem(
    string Id,
    string DisplayName,
    RequisitionCategory Category,
    int Cost,
    string Description,
    PlayerRank MinimumRank = PlayerRank.Lieutenant,
    int LeadTimeMissions = 0);

public sealed record RequisitionResult(bool Success, string Message);
public sealed record PendingRequisitionDelivery(
    string ItemId,
    string DisplayName,
    int RequestedAtMission,
    int EtaMission,
    DateTime OrderedAtUtc)
{
    public PendingRequisitionDelivery(string itemId, string displayName, int requestedAtMission, int etaMission)
        : this(itemId, displayName, requestedAtMission, etaMission, DateTime.UtcNow)
    {
    }
}

public sealed class RequisitionTerminal
{
    private readonly HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingRequisitionDelivery> _pending = new();

    public IReadOnlyCollection<string> OwnedItemIds => _owned;
    public IReadOnlyList<PendingRequisitionDelivery> PendingDeliveries => _pending;

    public IReadOnlyList<RequisitionItem> Catalog { get; } = new List<RequisitionItem>
    {
        new("low_alt_module", "Low-Altitude Detection Module", RequisitionCategory.Radar, 5000,
            "Improves low-altitude tracking and terrain-mask resistance."),
        new("eccm_suite", "ECCM Processing Suite", RequisitionCategory.Radar, 8000,
            "Reduces the effect of hostile jamming on the search picture.",
            LeadTimeMissions: 1),
        new("rapid_reload", "Rapid Reload Mechanism", RequisitionCategory.Launchers, 7000,
            "Cuts launcher reload time for sustained engagements.",
            LeadTimeMissions: 1),
        new("reserve_missile_crate", "Reserve Missile Crate", RequisitionCategory.Missiles, 3200,
            "Adds four reserve missiles to the battery magazine."),
        new("long_range_missiles", "48N6 Long-Range Missile Pack", RequisitionCategory.Missiles, 6400,
            "Unlocks a long-range active-radar missile option for early outer-ring shots.",
            PlayerRank.Lieutenant,
            LeadTimeMissions: 1),
        new("ir_point_defense", "9M331 IR Point-Defense Pack", RequisitionCategory.Missiles, 5200,
            "Unlocks a short-range infrared missile for close-in or degraded-radar fights.",
            PlayerRank.Lieutenant,
            LeadTimeMissions: 1),
        new("proximity_frag_upgrade", "Proximity Frag Warhead Upgrade", RequisitionCategory.Missiles, 4000,
            "Improves single-shot PK with a better proximity-fragmentation package.",
            LeadTimeMissions: 1),
        new("backup_power", "Backup Power Generator", RequisitionCategory.Electronics, 4000,
            "Keeps critical systems alive after power disruption.",
            LeadTimeMissions: 1),
        new("hardened_comms", "Hardened Communications", RequisitionCategory.Electronics, 5000,
            "Improves command-net survivability and support reliability under strain.",
            LeadTimeMissions: 1),
        new("data_link", "Data Link Terminal", RequisitionCategory.Electronics, 7000,
            "Improves shared operational picture quality for command, intel, and support coordination.",
            PlayerRank.Captain,
            LeadTimeMissions: 2),
        new("decoy_emitter", "Decoy Emitter", RequisitionCategory.Electronics, 9000,
            "Improves decoying and support survivability against hostile targeting.",
            PlayerRank.Captain,
            LeadTimeMissions: 1)
    };

    public bool IsOwned(string itemId) => _owned.Contains(itemId);
    public bool IsPending(string itemId) => _pending.Any(entry => entry.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));

    public void ReplaceOwnedItems(IEnumerable<string> itemIds)
    {
        _owned.Clear();
        foreach (var itemId in itemIds.Where(id => Catalog.Any(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            _owned.Add(itemId);
    }

    public void ReplacePendingDeliveries(IEnumerable<PendingRequisitionDelivery> deliveries)
    {
        _pending.Clear();
        foreach (var delivery in deliveries)
        {
            if (_owned.Contains(delivery.ItemId))
                continue;

            if (Catalog.Any(item => item.Id.Equals(delivery.ItemId, StringComparison.OrdinalIgnoreCase)))
                _pending.Add(delivery);
        }
    }

    public RequisitionResult Purchase(
        string itemId,
        BudgetSystem budget,
        PlayerProfile profile,
        SimulationEngine sim)
    {
        var item = Catalog.FirstOrDefault(entry => entry.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return new RequisitionResult(false, "UNKNOWN REQUISITION ITEM");

        if (IsOwned(item.Id))
            return new RequisitionResult(false, $"{item.DisplayName.ToUpperInvariant()} ALREADY INSTALLED");

        if (IsPending(item.Id))
            return new RequisitionResult(false, $"{item.DisplayName.ToUpperInvariant()} ALREADY IN TRANSIT");

        if (profile.Rank < item.MinimumRank)
            return new RequisitionResult(false, $"RANK LOCKED: REQUIRES {item.MinimumRank.ToString().ToUpperInvariant()}");

        if (!budget.Spend(item.Cost, item.DisplayName))
            return new RequisitionResult(false, "INSUFFICIENT OPERATIONAL BUDGET");

        if (item.LeadTimeMissions > 0)
        {
            int etaMission = profile.MissionsCompleted + item.LeadTimeMissions;
            _pending.Add(new PendingRequisitionDelivery(item.Id, item.DisplayName, profile.MissionsCompleted, etaMission));
            string suffix = item.LeadTimeMissions == 1 ? "1 OPERATION" : $"{item.LeadTimeMissions} OPERATIONS";
            return new RequisitionResult(true, $"{item.DisplayName.ToUpperInvariant()} ORDERED // ETA AFTER {suffix}");
        }

        _owned.Add(item.Id);
        ApplyItem(sim, item.Id);
        return new RequisitionResult(true, $"{item.DisplayName.ToUpperInvariant()} INSTALLED");
    }

    public void ApplyOwnedUpgrades(SimulationEngine sim)
    {
        foreach (var itemId in _owned)
            ApplyItem(sim, itemId);
    }

    public IReadOnlyList<PendingRequisitionDelivery> ProcessMissionTurnover(PlayerProfile profile, SimulationEngine sim)
    {
        var delivered = _pending
            .Where(entry => entry.EtaMission <= profile.MissionsCompleted)
            .ToList();

        foreach (var entry in delivered)
        {
            if (!_owned.Contains(entry.ItemId))
            {
                _owned.Add(entry.ItemId);
                ApplyItem(sim, entry.ItemId);
            }
        }

        _pending.RemoveAll(entry => delivered.Contains(entry));
        return delivered;
    }

    public string BuildOwnedSummary()
    {
        if (_owned.Count == 0)
            return "OWNED UPGRADES: NONE";

        var names = Catalog
            .Where(item => _owned.Contains(item.Id))
            .Select(item => item.DisplayName)
            .ToList();

        return $"OWNED UPGRADES: {string.Join(" | ", names)}";
    }

    public string BuildPendingSummary()
    {
        if (_pending.Count == 0)
            return "PENDING DELIVERIES: NONE";

        var lines = _pending
            .OrderBy(entry => entry.EtaMission)
            .Select(entry => $"{entry.DisplayName.ToUpperInvariant()} ETA M{entry.EtaMission}")
            .ToList();

        return $"PENDING DELIVERIES: {string.Join(" | ", lines)}";
    }

    private static void ApplyItem(SimulationEngine sim, string itemId)
    {
        var battery = sim.Entities.GetPlayerBattery();
        if (battery == null)
            return;

        switch (itemId.ToLowerInvariant())
        {
            case "low_alt_module":
                battery.HasLowAltitudeModule = true;
                sim.Radar.Model.HasLowAltModule = true;
                break;
            case "eccm_suite":
                battery.HasECCMSuite = true;
                sim.Radar.Model.HasECCM = true;
                break;
            case "rapid_reload":
                foreach (var launcher in battery.Launchers)
                    launcher.HasRapidReload = true;
                break;
            case "reserve_missile_crate":
                battery.ReserveMissiles += 4;
                break;
            case "long_range_missiles":
                WeaponCatalog.Unlock(battery, WeaponCatalog.LongRangeSarhWeaponId);
                break;
            case "ir_point_defense":
                WeaponCatalog.Unlock(battery, WeaponCatalog.ShortRangeIrWeaponId);
                break;
            case "proximity_frag_upgrade":
                battery.MissileSingleShotPk = Math.Min(0.95, battery.MissileSingleShotPk + 0.05);
                break;
            case "backup_power":
                battery.HasBackupPower = true;
                break;
            case "hardened_comms":
                battery.HasHardenedComms = true;
                break;
            case "data_link":
                battery.HasDataLink = true;
                break;
            case "decoy_emitter":
                battery.HasDecoyEmitter = true;
                break;
        }
    }
}
