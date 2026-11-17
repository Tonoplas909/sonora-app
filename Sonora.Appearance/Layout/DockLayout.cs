using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sonora.Appearance.Layout;

public enum DockZone { Left, Right, Top, Bottom }

/// <summary>Where a dragged panel lands relative to an existing group.</summary>
public enum DropPosition { Before, After, Tab }

public static class PanelIds
{
    public const string Navigation = "navigation";
    public const string Library = "library";
    public const string Queue = "queue";
    public const string Player = "player";
    public const string Lyrics = "lyrics";

    public static IReadOnlyList<string> All { get; } = [Navigation, Library, Queue, Player, Lyrics];

    public static DockZone DefaultZone(string id) => id switch
    {
        Navigation or Library => DockZone.Left,
        Queue or Lyrics => DockZone.Right,
        _ => DockZone.Bottom,
    };

    /// <summary>Optional panels: closed until the user opens them (also when a saved layout predates them).</summary>
    public static bool HiddenByDefault(string id) => id is Lyrics;
}

public sealed class DockGroup
{
    public List<string> Panels { get; set; } = [];
    public int ActiveIndex { get; set; }
    public double Ratio { get; set; } = 1;
}

public sealed class DockZoneState
{
    public double Size { get; set; }
    public bool IsCollapsed { get; set; }
    public List<DockGroup> Groups { get; set; } = [];
}

/// <summary>Arrangement of the movable panels around the fixed centre page.</summary>
public sealed class DockLayout
{
    public DockZoneState Left { get; set; } = new();
    public DockZoneState Right { get; set; } = new();
    public DockZoneState Top { get; set; } = new();
    public DockZoneState Bottom { get; set; } = new();
    public List<string> Hidden { get; set; } = [];
    public Dictionary<string, DockZone> LastZone { get; set; } = [];

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DockZoneState Zone(DockZone z) => z switch
    {
        DockZone.Left => Left,
        DockZone.Right => Right,
        DockZone.Top => Top,
        _ => Bottom,
    };

    public static DockLayout Default() => new()
    {
        Left = new DockZoneState
        {
            Size = 300,
            Groups =
            [
                new DockGroup { Panels = [PanelIds.Navigation], Ratio = 0.34 },
                new DockGroup { Panels = [PanelIds.Library], Ratio = 0.66 },
            ],
        },
        Right = new DockZoneState { Size = 340, IsCollapsed = true, Groups = [new DockGroup { Panels = [PanelIds.Queue] }] },
        Top = new DockZoneState { Size = 0 },
        Bottom = new DockZoneState { Size = 88, Groups = [new DockGroup { Panels = [PanelIds.Player] }] },
        Hidden = [PanelIds.Lyrics],
        LastZone = new Dictionary<string, DockZone> { [PanelIds.Lyrics] = DockZone.Right },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static DockLayout? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<DockLayout>(json, Options); }
        catch (JsonException) { return null; }
    }

    public DockLayout Clone() => Parse(ToJson())!;
}
