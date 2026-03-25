using System.IO;
using System.Text.Json;
using DEADSKY.Core.Campaign;

namespace DEADSKY.App.Services;

public sealed class CampaignPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SavePath { get; }

    public CampaignPersistenceService(string? savePath = null)
    {
        SavePath = savePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DEADSKY",
            "campaign_state.json");
    }

    public CampaignState? Load()
    {
        if (!File.Exists(SavePath))
            return null;

        try
        {
            var json = File.ReadAllText(SavePath);
            return JsonSerializer.Deserialize<CampaignState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool Save(CampaignState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(SavePath, JsonSerializer.Serialize(state, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
