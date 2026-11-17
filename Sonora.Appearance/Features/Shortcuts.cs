namespace Sonora.Appearance.Features;

/// <summary>One thing a keyboard shortcut can do, with the keys it uses out of the box.</summary>
public sealed record ShortcutAction(string Id, string Label, string Default);

/// <summary>
/// The keyboard map. Gestures are plain strings ("Ctrl+Q") so this stays free of any UI framework: the
/// window builds the same string from the key it just received and asks which action it belongs to.
/// </summary>
public sealed class Shortcuts
{
    /// <summary>Every action that can be rebound, in the order the settings page lists them.</summary>
    public static readonly ShortcutAction[] Actions =
    [
        new("playPause", "Lecture / pause", "Space"),
        new("next", "Titre suivant", "Ctrl+Right"),
        new("previous", "Titre précédent", "Ctrl+Left"),
        new("volumeUp", "Monter le volume", "Ctrl+Up"),
        new("volumeDown", "Baisser le volume", "Ctrl+Down"),
        new("shuffle", "Lecture aléatoire", "Ctrl+S"),
        new("repeat", "Répétition", "Ctrl+R"),
        new("queue", "File d'attente", "Ctrl+Q"),
        new("lyrics", "Paroles", "Ctrl+P"),
        new("search", "Rechercher", "Ctrl+L"),
        new("filter", "Filtrer la liste", "Ctrl+F"),
        new("miniPlayer", "Mini-lecteur", "Ctrl+M"),
        new("fullScreen", "Plein écran", "F11"),
    ];

    /// <summary>Gestures the user changed; anything absent keeps its default.</summary>
    public Dictionary<string, string> Bindings { get; set; } = [];

    /// <summary>
    /// The gesture for an action: the one it was given, the default when it was never changed, and
    /// nothing at all when it was deliberately unbound (an empty binding, not a missing one).
    /// </summary>
    public string Gesture(string actionId)
    {
        if (Bindings.TryGetValue(actionId, out var custom)) return custom;
        return Actions.FirstOrDefault(a => a.Id == actionId)?.Default ?? "";
    }

    /// <summary>The action a gesture triggers, or null when nothing is bound to it.</summary>
    public string? ActionFor(string? gesture)
    {
        var g = Normalize(gesture);
        if (g.Length == 0) return null;
        foreach (var action in Actions)
            if (Gesture(action.Id) == g) return action.Id;
        return null;
    }

    /// <summary>Binds a gesture, taking it away from whatever held it: two actions never share one.</summary>
    public void Bind(string actionId, string? gesture)
    {
        var g = Normalize(gesture);
        if (g.Length == 0) { Reset(actionId); return; }

        foreach (var other in Actions)
            if (other.Id != actionId && Gesture(other.Id) == g)
                Bindings[other.Id] = ""; // unbound rather than left on a key it no longer owns
        Bindings[actionId] = g;
    }

    /// <summary>Puts one action back on its default gesture.</summary>
    public void Reset(string actionId) => Bindings.Remove(actionId);

    /// <summary>Puts every action back on its default.</summary>
    public void ResetAll() => Bindings.Clear();

    /// <summary>True when the action no longer runs on any key.</summary>
    public bool IsUnbound(string actionId) =>
        Bindings.TryGetValue(actionId, out var g) && g.Length == 0;

    /// <summary>
    /// "ctrl + q" and "CTRL+Q" are the same gesture: modifiers come first, in a fixed order, then the key.
    /// An empty or modifier-only gesture normalises to nothing.
    /// </summary>
    public static string Normalize(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "";
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool ctrl = false, shift = false, alt = false;
        string key = "";
        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default: key = Capitalise(raw); break;
            }
        }
        if (key.Length == 0) return "";

        var text = "";
        if (ctrl) text += "Ctrl+";
        if (alt) text += "Alt+";
        if (shift) text += "Shift+";
        return text + key;
    }

    /// <summary>Builds the gesture string the map is keyed by, from the modifiers and key of a key press.</summary>
    public static string Build(bool ctrl, bool alt, bool shift, string keyName) =>
        Normalize((ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + keyName);

    /// <summary>Removes bindings for actions that no longer exist, and normalises what is left.</summary>
    public void Repair()
    {
        var known = Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        Bindings = (Bindings ?? [])
            .Where(b => known.Contains(b.Key))
            .ToDictionary(b => b.Key, b => Normalize(b.Value), StringComparer.Ordinal);
    }

    static string Capitalise(string text) =>
        text.Length == 1 ? text.ToUpperInvariant() : char.ToUpperInvariant(text[0]) + text[1..];
}
