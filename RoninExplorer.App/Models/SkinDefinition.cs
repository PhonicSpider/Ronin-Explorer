namespace RoninExplorer.App.Models;

/// <summary>
/// A customizable skin: per-aspect colors plus an optional background image
/// behind the file list. Kept as an open, flat set of hex-string properties
/// (not a strict schema) so new customizable aspects can be added later
/// without a migration — matches what the owner asked for ("can go over what
/// can be customized when we build it").
/// </summary>
public sealed class SkinDefinition
{
    public string Name { get; set; } = "Default";

    public string NavPaneBackground { get; set; } = "#00000000";
    public string FileListBackground { get; set; } = "#00000000";
    // Subtle white tint (not fully transparent like the other panes) — real
    // Explorer's details pane in Win11 is a visibly distinct card, not flush
    // against the Mica surface the way the nav pane and file list are.
    public string PanelBackground { get; set; } = "#14FFFFFF";
    public string AccentColor { get; set; } = "#FF3D5AFE";
    public string TextPrimary { get; set; } = "#FFFFFFFF";

    public string? BackgroundImagePath { get; set; }
    public double BackgroundImageOpacity { get; set; } = 1.0;

    public static SkinDefinition Default => new();
}
