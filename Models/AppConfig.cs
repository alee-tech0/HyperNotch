namespace HyperNotch.Models;

public enum IslandTheme
{
    OledBlack,
    AcrylicGlass,
    FrutigerAero,
    RgbBorder,
    Custom
}

public sealed class AppConfig
{
    public int MonitorIndex { get; set; } = 0;
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 10;
    public bool NotchMode { get; set; } = false;
    public bool AutoStart { get; set; } = false;
    public IslandTheme Theme { get; set; } = IslandTheme.OledBlack;
    public bool AutoGameMode { get; set; } = true;
    public string Language { get; set; } = "it";

    public string CustomBackgroundHex { get; set; } = "#B91C1C";
    public string CustomBorderHex { get; set; } = "#FCA5A5";
    public string CustomAccentHex { get; set; } = "#FDE047";
    public string CustomBannerImage { get; set; } = "none";
    public double CustomBannerOpacity { get; set; } = 0.85;
    public string QuickAppPath { get; set; } = "";
}
