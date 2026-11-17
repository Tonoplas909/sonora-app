using System.Text.Json;

namespace Sonora.Appearance.Theming;

/// <summary>Built-in themes (embedded, read-only) plus the user's themes stored as JSON files in a folder.</summary>
public sealed class ThemeCatalog(string userDirectory)
{
    /// <summary>Built-in sub-themes, grouped by theme (style) in the order of <see cref="ThemeStyles.All"/>.</summary>
    public static readonly string[] BuiltInIds =
    [
        "liquide", "liquide-clair",
        "studio", "studio-ambre", "studio-cyan",
        "affiche", "affiche-tomate",
        "soul", "soul-lagune",
        "fonction", "fonction-nuit",
        "encre", "encre-sombre",
        "clair", "oled", "papier", "synthwave", "gta6",
    ];

    /// <summary>Sub-themes (colour variants) of one theme: built-in ones first, then the user's by name.</summary>
    public IReadOnlyList<ThemeDefinition> PalettesFor(string style) => Themes.Where(t => t.Style == style).ToList();

    readonly List<ThemeDefinition> _builtIn = [];
    readonly List<ThemeDefinition> _user = [];
    readonly List<string> _errors = [];

    public IReadOnlyList<ThemeDefinition> Themes =>
        [.. _builtIn, .. _user.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<string> LoadErrors => _errors;

    public string UserDirectory => userDirectory;

    /// <summary>The default theme, and the base every other theme inherits unset fields from.</summary>
    public static ThemeDefinition BuiltInDefault() => ReadEmbedded("liquide", null);

    /// <summary>Themes that no longer exist, and the one that replaced each.</summary>
    static readonly Dictionary<string, string> Retired = new()
    {
        ["verre"] = "liquide", ["verre-aube"] = "liquide-clair",
        ["immersif"] = "liquide", ["immersif-ocre"] = "liquide-clair",
    };

    /// <summary>The id to use now for a saved one: a retired theme maps to its replacement, anything else is unchanged.</summary>
    public static string Resolve(string id) => Retired.GetValueOrDefault(id, id);

    /// <summary>True for the retired Immersif themes, whose cover-as-background became an option of Verre liquide.</summary>
    public static bool WasImmersive(string id) => id is "immersif" or "immersif-ocre";

    public void Load()
    {
        _builtIn.Clear();
        _user.Clear();
        _errors.Clear();

        var baseTheme = BuiltInDefault();
        foreach (var id in BuiltInIds)
        {
            var t = id == "liquide" ? baseTheme : ReadEmbedded(id, baseTheme);
            t.BuiltIn = true;
            _builtIn.Add(t);
        }

        if (!Directory.Exists(userDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(userDirectory, "*.json"))
        {
            var name = Path.GetFileName(file);
            try
            {
                var json = File.ReadAllText(file);
                var raw = RawErrors(json);
                if (raw.Count > 0) { _errors.Add($"{name} : {raw[0]}"); continue; }
                var t = ThemeDefinition.Parse(json, baseTheme);
                t.Id = Slug(HasId(json) ? t.Id : Path.GetFileNameWithoutExtension(file));
                if (BuiltInIds.Contains(t.Id)) { _errors.Add($"{name} : l'identifiant « {t.Id} » est réservé à un thème fourni."); continue; }
                if (_user.Any(u => u.Id == t.Id)) { _errors.Add($"{name} : identifiant « {t.Id} » en double."); continue; }
                t.BuiltIn = false;
                _user.Add(t);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _errors.Add($"{name} : {ex.Message}");
            }
        }
    }

    public ThemeDefinition Get(string id)
    {
        ThemeDefinition? Find(string wanted) => _builtIn.Concat(_user).FirstOrDefault(t => t.Id == wanted);
        return Find(id) ?? Find(Resolve(id)) ?? _builtIn.FirstOrDefault() ?? BuiltInDefault();
    }

    public ThemeDefinition Duplicate(string id)
    {
        var source = Get(id);
        var copy = source.Clone();
        copy.Id = UniqueId(source.Id + "-copie");
        copy.Name = source.Name + " (copie)";
        copy.BuiltIn = false;
        copy.BasedOn = source.BasedOn ?? source.Id;
        _user.Add(copy);
        Save(copy);
        return copy;
    }

    /// <summary>
    /// Puts back the values of the theme this one was duplicated from, keeping its identifier and name.
    /// Without a known origin, the theme is read again from its file.
    /// </summary>
    public ThemeDefinition ResetToBase(string id)
    {
        EnsureEditable(id);
        var current = Get(id);
        var origin = current.BasedOn is { } b && Themes.Any(t => t.Id == b && t.Id != id) ? Get(b) : null;
        ThemeDefinition reset;
        if (origin != null)
        {
            reset = origin.Clone();
        }
        else
        {
            var file = PathOf(id);
            reset = File.Exists(file) ? ThemeDefinition.Parse(File.ReadAllText(file), BuiltInDefault()) : current.Clone();
        }
        reset.Id = current.Id;
        reset.Name = current.Name;
        reset.BuiltIn = false;
        reset.BasedOn = current.BasedOn;
        _user.RemoveAll(u => u.Id == id);
        _user.Add(reset);
        Save(reset);
        return reset;
    }

    static bool HasId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(v.GetString());
        }
        catch (JsonException) { return false; }
    }

    public void Save(ThemeDefinition t)
    {
        EnsureEditable(t.Id);
        if (!_user.Contains(t))
        {
            _user.RemoveAll(u => u.Id == t.Id);
            _user.Add(t);
        }
        Directory.CreateDirectory(userDirectory);
        File.WriteAllText(PathOf(t.Id), t.ToJson());
    }

    public void Rename(string id, string name)
    {
        EnsureEditable(id);
        var t = Get(id);
        t.Name = name.Trim();
        Save(t);
    }

    public void Delete(string id)
    {
        EnsureEditable(id);
        _user.RemoveAll(u => u.Id == id);
        if (File.Exists(PathOf(id))) File.Delete(PathOf(id));
    }

    public void Export(string id, string path) => File.WriteAllText(path, Get(id).ToJson());

    public ThemeDefinition Import(string path)
    {
        var json = File.ReadAllText(path);
        var raw = RawErrors(json);
        if (raw.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, raw));
        var t = ThemeDefinition.Parse(json, Get("liquide"));
        var errors = t.Validate();
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        t.BuiltIn = false;
        t.Id = UniqueId(Slug(HasId(json) ? t.Id : Path.GetFileNameWithoutExtension(path)));
        _user.Add(t);
        Save(t);
        return t;
    }

    // ---------------- helpers ----------------

    void EnsureEditable(string id)
    {
        if (BuiltInIds.Contains(id)) throw new InvalidOperationException("Les thèmes fournis ne peuvent pas être modifiés.");
    }

    /// <summary>File of a theme, guaranteed to stay inside the themes folder whatever the id contains.</summary>
    string PathOf(string id)
    {
        var dir = Path.GetFullPath(userDirectory);
        var path = Path.GetFullPath(Path.Combine(dir, Slug(id) + ".json"));
        if (!path.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Identifiant de thème invalide.");
        return path;
    }

    /// <summary>Theme ids are file names: lower-case letters, digits and dashes only.</summary>
    public static string Slug(string? id)
    {
        var chars = (id ?? "").ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray();
        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "theme" : slug;
    }

    string UniqueId(string baseId)
    {
        bool Taken(string id) => BuiltInIds.Contains(id) || _user.Any(u => u.Id == id);
        if (!Taken(baseId)) return baseId;
        for (int n = 2; ; n++)
            if (!Taken($"{baseId}-{n}")) return $"{baseId}-{n}";
    }

    /// <summary>Checks the file as written: valid JSON object and every colour that is present is well formed.</summary>
    static List<string> RawErrors(string json)
    {
        var errors = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { errors.Add("le fichier n'est pas un thème."); return errors; }
            if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
                foreach (var p in colors.EnumerateObject())
                    if (p.Value.ValueKind != JsonValueKind.String || !Argb.TryParse(p.Value.GetString(), out _))
                        errors.Add($"Couleur « {p.Name} » invalide : « {p.Value} » (format attendu #RRGGBB ou #AARRGGBB).");
        }
        catch (JsonException ex)
        {
            errors.Add("JSON invalide (" + ex.Message + ").");
        }
        return errors;
    }

    static ThemeDefinition ReadEmbedded(string id, ThemeDefinition? fallback)
    {
        var asm = typeof(ThemeCatalog).Assembly;
        using var s = asm.GetManifestResourceStream($"Sonora.Appearance.Theming.BuiltIn.{id}.json")
                      ?? throw new InvalidOperationException($"Thème fourni introuvable : {id}");
        using var reader = new StreamReader(s);
        return ThemeDefinition.Parse(reader.ReadToEnd(), fallback ?? new ThemeDefinition());
    }
}
