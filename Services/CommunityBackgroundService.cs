using System.IO;
using System.Text.Json;

namespace HyperNotch.Services;

public sealed class CommunityBackground
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Author { get; set; } = "Community";
    public string BackgroundHex { get; set; } = "#1E1B4B";
    public string BorderHex { get; set; } = "#818CF8";
    public string AccentHex { get; set; } = "#38BDF8";
    public string ImagePath { get; set; } = "";
    public bool IsUserCreated { get; set; }
}

public static class CommunityBackgroundService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HyperNotch",
        "community_backgrounds.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly List<CommunityBackground> BuiltInCommunity =
    [
        new() { Id = "comm-cyberpunk", Name = "Cyberpunk Neon", Author = "Community", BackgroundHex = "#1A0B2E", BorderHex = "#F43F5E", AccentHex = "#22D3EE" },
        new() { Id = "comm-synthwave", Name = "Tramonto Synthwave", Author = "Community", BackgroundHex = "#3B0764", BorderHex = "#FB7185", AccentHex = "#FDE047" },
        new() { Id = "comm-nordic", Name = "Foresta Nordica", Author = "Community", BackgroundHex = "#064E3B", BorderHex = "#34D399", AccentHex = "#A7F3D0" },
        new() { Id = "comm-ocean", Name = "Oceano Profondo", Author = "Community", BackgroundHex = "#0C4A6E", BorderHex = "#38BDF8", AccentHex = "#7DD3FC" },
        new() { Id = "comm-lava", Name = "Fuoco Lavico", Author = "Community", BackgroundHex = "#7F1D1D", BorderHex = "#FB923C", AccentHex = "#FBBF24" },
        new() { Id = "comm-ice", Name = "Minimal Ghiaccio", Author = "Community", BackgroundHex = "#1E293B", BorderHex = "#CBD5E1", AccentHex = "#E2E8F0" }
    ];

    private static readonly CommunityBackground GtaViEntry = new()
    {
        Id = "comm-gtavi",
        Name = "GTA VI",
        Author = "ItsAlee",
        BackgroundHex = "#4A154B",
        BorderHex = "#FB7185",
        AccentHex = "#FDBA74",
        ImagePath = "preset:gtavi",
        IsUserCreated = false
    };

    public static IReadOnlyList<CommunityBackground> GetBuiltInPresets() => BuiltInCommunity;

    public static List<CommunityBackground> LoadAll()
    {
        var list = LoadUserBackgrounds();
        list.Add(GtaViEntry);
        return list;
    }

    public static List<CommunityBackground> LoadUserBackgrounds()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var items = JsonSerializer.Deserialize<List<CommunityBackground>>(json);
                if (items is not null)
                {
                    foreach (var item in items)
                        item.IsUserCreated = true;
                    return items;
                }
            }
        }
        catch { }
        return new List<CommunityBackground>();
    }

    public static void SaveUserBackground(CommunityBackground bg)
    {
        var userItems = LoadUserBackgrounds();
        bg.IsUserCreated = true;
        if (string.IsNullOrWhiteSpace(bg.Id))
            bg.Id = Guid.NewGuid().ToString("N");

        userItems.Insert(0, bg);
        PersistUserBackgrounds(userItems);
    }

    public static void DeleteUserBackground(string id)
    {
        var userItems = LoadUserBackgrounds();
        userItems.RemoveAll(x => x.Id == id);
        PersistUserBackgrounds(userItems);
    }

    private static void PersistUserBackgrounds(List<CommunityBackground> items)
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch { }
    }
}
