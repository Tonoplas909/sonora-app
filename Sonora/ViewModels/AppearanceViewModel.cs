using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Sonora.Appearance.Content;
using Sonora.Appearance.Layout;
using Sonora.Appearance.Theming;
using Sonora.Core;
using Sonora.Views.Panels;
using Sonora.Appearance.Localization;

namespace Sonora.ViewModels;

/// <summary>A theme (structural style) card, previewed with one of its sub-themes.</summary>
public sealed class StyleTile(ThemeStyleInfo info, ThemeDefinition preview, bool isActive)
{
    public ThemeStyleInfo Info { get; } = info;
    public ThemeDefinition Preview { get; } = preview;
    public bool IsActive { get; } = isActive;
}

public sealed class ThemeTile(ThemeDefinition theme, bool isActive)
{
    public ThemeDefinition Theme { get; } = theme;
    public bool IsActive { get; } = isActive;
}

public sealed class ColorSlot(string key, string label, string hex, Action<ColorSlot> changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    string _hex = hex;
    public string Hex
    {
        get => _hex;
        set
        {
            if (!Argb.TryParse(value, out _) || !Set(ref _hex, value.ToUpperInvariant())) return;
            changed(this);
        }
    }
}

public sealed class HomeSectionItem(string id, string title, bool visible, Action changed) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;

    bool _visible = visible;
    public bool Visible { get => _visible; set { if (Set(ref _visible, value)) changed(); } }
}

public sealed class PanelItem(string id, LayoutViewModel layout) : ObservableObject
{
    public string Id { get; } = id;
    public string Title => PanelFactory.Title(Id);

    public bool IsShown
    {
        get => DockLayoutOps.Find(layout.Layout, Id) is { } at && !layout.Layout.Zone(at.Zone).IsCollapsed;
        set
        {
            if (value == IsShown) return;
            if (value) layout.Show(Id); else layout.Close(Id);
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsShown));
}

/// <summary>Everything the "Apparence" page edits: themes, colours, type and shapes, background, layout and content.</summary>
public sealed class AppearanceViewModel : ObservableObject
{
    static readonly (string Key, string Label)[] ColorLabels =
    [
        ("window", "appearance.color_window"), ("surface", "appearance.color_surface"), ("surfaceAlt", "appearance.color_surfaceAlt"), ("hover", "appearance.color_hover"),
        ("selected", "appearance.color_selected"), ("border", "appearance.color_border"), ("textPrimary", "appearance.color_textPrimary"), ("textSecondary", "appearance.color_textSecondary"),
        ("textMuted", "appearance.color_textMuted"), ("accent", "appearance.color_accent"), ("onAccent", "appearance.color_onAccent"), ("rail", "appearance.color_rail"),
    ];

    static readonly Dictionary<string, string> SectionTitles = new()
    {
        [HomeSectionIds.Recent] = "home.recently_played",
        [HomeSectionIds.Playlists] = "home.your_playlists",
        [HomeSectionIds.SavedAlbums] = "home.your_albums",
        [HomeSectionIds.Rediscover] = "home.rediscover",
        [HomeSectionIds.Artists] = "pages.your_artists",
        [HomeSectionIds.NewReleases] = "pages.new_releases_from_your_artists",
        [HomeSectionIds.TopArtists] = "pages.your_most_played_artists",
        [HomeSectionIds.Shows] = "home.your_podcasts",
    };

    readonly MainViewModel _main;
    readonly DispatcherTimer _save;
    bool _loading;

    public AppearanceViewModel(MainViewModel main)
    {
        _main = main;
        Current = App.Themes.Current;
        _save = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _save.Tick += (_, _) => Flush();

        FontNames = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        ApplyThemeCommand = new RelayCommand(p => { if (p is ThemeTile t) Apply(t.Theme); });
        SelectStyleCommand = new RelayCommand(p => { if (p is StyleTile s) SelectStyle(s.Info.Id); });
        DuplicateCommand = new RelayCommand(Duplicate);
        RenameCommand = new RelayCommand(Rename, () => IsEditable);
        DeleteCommand = new RelayCommand(Delete, () => IsEditable);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ResetThemeCommand = new RelayCommand(ResetTheme);
        MoveSectionCommand = new RelayCommand(p => { if (p is object[] { Length: 2 } a && a[0] is string id && a[1] is int d) MoveSection(id, d); });
        MoveUpCommand = new RelayCommand(p => { if (p is HomeSectionItem s) MoveSection(s.Id, -1); });
        MoveDownCommand = new RelayCommand(p => { if (p is HomeSectionItem s) MoveSection(s.Id, +1); });
        ResetLayoutCommand = new RelayCommand(() => _main.Layout.Reset(Current));

        // The player can't be hidden, so it is not offered here.
        Panels = new ObservableCollection<PanelItem>(PanelIds.All.Where(id => id != PanelIds.Player).Select(id => new PanelItem(id, _main.Layout)));
        _main.Layout.Changed += () => { foreach (var p in Panels) p.Refresh(); };

        LoadEditor();
        LoadContent();
    }

    // ---------------- themes ----------------

    /// <summary>Level 1: the themes (complete styles).</summary>
    public ObservableCollection<StyleTile> StyleTiles { get; } = [];
    public ICommand SelectStyleCommand { get; }
    public string PaletteSectionTitle => Loc.T("appearance.palette_title", ThemeStyles.Get(Current.Style).Name);

    /// <summary>Level 2: the sub-themes (colour variants) of the current theme.</summary>
    public ObservableCollection<ThemeTile> Themes { get; } = [];

    /// <summary>Choosing a theme applies the sub-theme last used with it, otherwise its first one.</summary>
    void SelectStyle(string style)
    {
        if (style == Current.Style) return;
        var palettes = App.Catalog.PalettesFor(style);
        if (palettes.Count == 0) return;
        var remembered = _main.State.PaletteByStyle.GetValueOrDefault(style);
        Apply(palettes.FirstOrDefault(p => p.Id == remembered) ?? palettes[0]);
    }
    public ThemeDefinition Current { get; private set; }
    public bool IsEditable => !Current.BuiltIn;
    public bool IsLocked => Current.BuiltIn;

    public ICommand ApplyThemeCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ResetThemeCommand { get; }

    ThemeDefinition? _pending;

    /// <summary>Writes the last edited theme now (before switching theme or closing Sonora).</summary>
    public void Flush()
    {
        _save.Stop();
        var theme = _pending;
        _pending = null;
        if (theme == null || theme.BuiltIn || _main.SnapshotMode) return;
        Guard(() => App.Catalog.Save(theme));
    }

    void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or InvalidOperationException or InvalidDataException)
        {
            _main.ReportError(ex.Message);
        }
    }

    void Apply(ThemeDefinition t)
    {
        Flush();
        Current = t;
        _main.State.ThemeId = t.Id;
        _main.State.PaletteByStyle[t.Style] = t.Id;
        App.Themes.Apply(t);
        _main.ScheduleSave();
        LoadEditor();
    }

    void Duplicate()
    {
        Flush();
        Guard(() => Apply(App.Catalog.Duplicate(Current.Id)));
    }

    void Rename()
    {
        var name = Prompt.Show(Application.Current.MainWindow, Loc.T("appearance.rename_theme"), Current.Name);
        if (name == null) return;
        Flush();
        Guard(() => App.Catalog.Rename(Current.Id, name));
        LoadEditor();
    }

    void Delete()
    {
        var r = MessageBox.Show(Application.Current.MainWindow, Loc.T("appearance.delete_theme_confirm", Current.Name), Loc.T("appearance.delete_theme"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _pending = null;
        _save.Stop();
        Guard(() => App.Catalog.Delete(Current.Id));
        Apply(App.Catalog.Get("liquide"));
    }

    void Export()
    {
        var dlg = new SaveFileDialog { Title = Loc.T("appearance.export_theme"), Filter = Loc.T("appearance.theme_file") + " (*.json)|*.json", FileName = Current.Id + ".json" };
        if (dlg.ShowDialog() != true) return;
        Flush();
        Guard(() =>
        {
            App.Catalog.Export(Current.Id, dlg.FileName);
            _main.ReportError(Loc.T("appearance.exported", dlg.FileName));
        });
    }

    void Import()
    {
        var dlg = new OpenFileDialog { Title = Loc.T("appearance.import_theme"), Filter = Loc.T("appearance.theme_file") + " (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try { Apply(App.Catalog.Import(dlg.FileName)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            _main.ReportError(Loc.T("appearance.import_failed", ex.Message));
        }
    }

    /// <summary>Built-in theme: reload it. User theme: back to the values of the theme it was duplicated from.</summary>
    void ResetTheme()
    {
        var id = Current.Id;
        _pending = null;
        _save.Stop();
        if (Current.BuiltIn)
        {
            App.Catalog.Load();
            Apply(App.Catalog.Get(id));
            return;
        }
        try { Apply(App.Catalog.ResetToBase(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _main.ReportError(Loc.T("appearance.reset_failed", ex.Message));
        }
    }

    // ---------------- editor ----------------

    public ObservableCollection<ColorSlot> Colors { get; } = [];
    public List<string> FontNames { get; }
    public string[] Densities { get; } = ["compact", "normal", "confort"];
    public string[] BackgroundModes { get; } = ["solid", "gradient", "artwork"];

    public string[] StyleOptions { get; } = ThemeDefinition.Styles;
    public string ThemeStyle { get => Current.Style; set => Edit(() => Current.Style = value); }
    public string FontUi { get => Current.Fonts.Ui; set => Edit(() => Current.Fonts.Ui = value); }
    public string FontDisplay { get => Current.Fonts.Display; set => Edit(() => Current.Fonts.Display = value); }
    public string FontNumbers { get => Current.Fonts.Numbers; set => Edit(() => Current.Fonts.Numbers = value); }
    public double FontScale { get => Current.FontScale; set => Edit(() => Current.FontScale = Math.Clamp(Math.Round(value, 2), 0.85, 1.3)); }
    public double CornerRadius { get => Current.CornerRadius; set => Edit(() => Current.CornerRadius = Math.Clamp(Math.Round(value), 0, 24)); }
    public string Density { get => Current.Density; set => Edit(() => Current.Density = value); }
    public double PanelGap { get => Current.PanelGap; set => Edit(() => Current.PanelGap = Math.Clamp(Math.Round(value), 0, 32)); }
    public bool Animations { get => Current.Animations; set => Edit(() => Current.Animations = value); }
    public bool AccentFromArtwork { get => Current.AccentFromArtwork; set => Edit(() => Current.AccentFromArtwork = value); }
    public string BackgroundMode
    {
        get => Current.Background.Mode;
        set
        {
            Edit(() => Current.Background.Mode = value);
            App.Themes.SetCustomBackground(_main.State.BackgroundImage, _main.State.BackgroundImageStrength);
            OnPropertyChanged(nameof(CustomBackgroundAvailable));
        }
    }

    // ---- the cover as the background of Verre liquide: a preference, so it works on the built-in palettes ----

    public bool CoverBackgroundAvailable => Current.Style == "liquid";

    /// <summary>The icon of the window follows the theme (colour of the accent, shape of the style).</summary>
    public bool ThemedIcon
    {
        get => _main.State.ThemedIcon;
        set
        {
            if (_main.State.ThemedIcon == value) return;
            _main.State.ThemedIcon = value;
            App.Themes.ThemedIcon = value;
            _main.ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool CoverBackground
    {
        get => _main.State.CoverBackground;
        set
        {
            if (_main.State.CoverBackground == value) return;
            _main.State.CoverBackground = value;
            App.Themes.SetCoverBackground(value, _main.State.CoverBackgroundStrength, _main.State.CoverBackgroundBlur);
            _main.ScheduleSave();
            OnPropertyChanged();
        }
    }

    public double CoverBackgroundStrength
    {
        get => _main.State.CoverBackgroundStrength;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0, 1);
            if (Math.Abs(value - _main.State.CoverBackgroundStrength) < 0.001) return;
            _main.State.CoverBackgroundStrength = value;
            App.Themes.SetCoverBackground(_main.State.CoverBackground, value, _main.State.CoverBackgroundBlur);
            _main.ScheduleSave();
            OnPropertyChanged();
        }
    }

    public double CoverBackgroundBlur
    {
        get => _main.State.CoverBackgroundBlur;
        set
        {
            value = Math.Clamp(Math.Round(value / 5) * 5, 0, 120);
            if (Math.Abs(value - _main.State.CoverBackgroundBlur) < 0.001) return;
            _main.State.CoverBackgroundBlur = value;
            App.Themes.SetCoverBackground(_main.State.CoverBackground, _main.State.CoverBackgroundStrength, value);
            _main.ScheduleSave();
            OnPropertyChanged();
        }
    }

    // ---- custom background picture: a user preference, so it works on built-in themes too ----

    /// <summary>The picture can only replace a plain one-colour background.</summary>
    public bool CustomBackgroundAvailable => Current.Background.Mode == "solid";
    public bool HasBackgroundImage => _main.State.BackgroundImage.Length > 0;
    public string BackgroundImageName => HasBackgroundImage ? System.IO.Path.GetFileName(_main.State.BackgroundImage) : Loc.T("appearance.no_image");

    public double BackgroundImageStrength
    {
        get => _main.State.BackgroundImageStrength;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0, 1);
            if (Math.Abs(value - _main.State.BackgroundImageStrength) < 0.001) return;
            _main.State.BackgroundImageStrength = value;
            App.Themes.SetCustomBackground(_main.State.BackgroundImage, value);
            _main.ScheduleSave();
            OnPropertyChanged();
        }
    }

    public ICommand ChooseBackgroundImageCommand => new RelayCommand(ChooseBackgroundImage);
    public ICommand ClearBackgroundImageCommand => new RelayCommand(() => SetBackgroundImage(""), () => HasBackgroundImage);

    void ChooseBackgroundImage()
    {
        var dlg = new OpenFileDialog { Title = Loc.T("appearance.choose_background"), Filter = Loc.T("dialog.images") + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp|" + Loc.T("dialog.all_files") + "|*.*" };
        if (dlg.ShowDialog() == true) SetBackgroundImage(dlg.FileName);
    }

    void SetBackgroundImage(string path)
    {
        if (!App.Themes.SetCustomBackground(path, _main.State.BackgroundImageStrength))
        {
            _main.ReportError(Loc.T("appearance.image_unreadable"));
            return;
        }
        _main.State.BackgroundImage = path;
        _main.ScheduleSave();
        OnPropertyChanged(nameof(HasBackgroundImage));
        OnPropertyChanged(nameof(BackgroundImageName));
        CommandManager.InvalidateRequerySuggested();
    }
    public double ArtworkStrength { get => Current.Background.ArtworkStrength; set => Edit(() => Current.Background.ArtworkStrength = Math.Clamp(Math.Round(value, 2), 0, 1)); }

    public string Background1
    {
        get => Current.Background.Colors.ElementAtOrDefault(0) ?? Current.Colors.Window;
        set { if (Argb.TryParse(value, out _)) Edit(() => SetBackground(0, value)); }
    }

    public string Background2
    {
        get => Current.Background.Colors.ElementAtOrDefault(1) ?? Background1;
        set { if (Argb.TryParse(value, out _)) Edit(() => SetBackground(1, value)); }
    }

    void SetBackground(int index, string hex)
    {
        var list = Current.Background.Colors;
        while (list.Count <= index) list.Add(list.Count > 0 ? list[^1] : Current.Colors.Window);
        list[index] = hex;
    }

    void Edit(Action change)
    {
        if (_loading || !IsEditable) return;
        change();
        App.Themes.Apply(Current);
        _pending = Current; // the theme being edited, saved even if another theme is applied meanwhile
        _save.Stop();
        _save.Start();
        // Refresh the edited theme's thumbnail
        for (int i = 0; i < Themes.Count; i++)
            if (Themes[i].Theme.Id == Current.Id) { Themes[i] = new ThemeTile(Current, true); break; }
        RaiseEditor();
    }

    void LoadEditor()
    {
        _loading = true;
        StyleTiles.Clear();
        foreach (var s in ThemeStyles.All)
        {
            var palettes = App.Catalog.PalettesFor(s.Id);
            if (palettes.Count == 0) continue;
            var preview = s.Id == Current.Style ? Current : palettes[0];
            StyleTiles.Add(new StyleTile(s, preview, s.Id == Current.Style));
        }
        Themes.Clear();
        foreach (var t in App.Catalog.PalettesFor(Current.Style)) Themes.Add(new ThemeTile(t, t.Id == Current.Id));
        OnPropertyChanged(nameof(PaletteSectionTitle));
        Colors.Clear();
        foreach (var (key, label) in ColorLabels)
            Colors.Add(new ColorSlot(key, Loc.T(label), Current.Colors.Get(key), slot => Edit(() => Current.Colors.Set(slot.Key, slot.Hex))));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsLocked));
        RaiseEditor();
        CommandManager.InvalidateRequerySuggested();
        _loading = false;
    }

    void RaiseEditor()
    {
        foreach (var p in new[] { nameof(ThemeStyle), nameof(FontUi), nameof(FontDisplay), nameof(FontNumbers), nameof(FontScale), nameof(CornerRadius),
                     nameof(Density), nameof(PanelGap), nameof(Animations), nameof(AccentFromArtwork), nameof(BackgroundMode),
                     nameof(ArtworkStrength), nameof(Background1), nameof(Background2), nameof(CustomBackgroundAvailable), nameof(CoverBackgroundAvailable) })
            OnPropertyChanged(p);
    }

    // ---------------- content ----------------

    public ObservableCollection<HomeSectionItem> HomeSections { get; } = [];
    public ICommand MoveSectionCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    ContentSettings Content => _main.State.Content;

    public bool ShowNumber { get => Content.ShowNumber; set { Content.ShowNumber = value; ContentChanged(); } }
    public bool ShowArt { get => Content.ShowArt; set { Content.ShowArt = value; ContentChanged(); } }
    public bool ShowAlbum { get => Content.ShowAlbum; set { Content.ShowAlbum = value; ContentChanged(); } }
    public bool ShowLike { get => Content.ShowLike; set { Content.ShowLike = value; ContentChanged(); } }
    public bool ShowDuration { get => Content.ShowDuration; set { Content.ShowDuration = value; ContentChanged(); } }
    public string[] CardSizes { get; } = ["small", "medium", "large"];
    public string CardSize { get => Content.CardSize; set { Content.CardSize = value; ContentChanged(); } }

    void LoadContent()
    {
        HomeSections.Clear();
        foreach (var s in Content.HomeSections)
        {
            var section = s;
            HomeSections.Add(new HomeSectionItem(s.Id, Loc.T(SectionTitles.GetValueOrDefault(s.Id, s.Id)), s.Visible, () =>
            {
                section.Visible = HomeSections.First(h => h.Id == section.Id).Visible;
                ContentChanged();
            }));
        }
    }

    public void MoveSection(string id, int delta)
    {
        Content.MoveSection(id, delta);
        LoadContent();
        ContentChanged();
    }

    /// <summary>Moves a section to an absolute position (drag and drop in the list).</summary>
    public void MoveSectionTo(string id, int index)
    {
        var from = Content.HomeSections.FindIndex(s => s.Id == id);
        if (from < 0) return;
        MoveSection(id, index - from);
    }

    void ContentChanged()
    {
        _main.NotifyContentChanged();
        foreach (var p in new[] { nameof(ShowNumber), nameof(ShowArt), nameof(ShowAlbum), nameof(ShowLike), nameof(ShowDuration), nameof(CardSize) })
            OnPropertyChanged(p);
    }

    // ---------------- layout ----------------

    public ObservableCollection<PanelItem> Panels { get; }
    public ICommand ResetLayoutCommand { get; }
}
