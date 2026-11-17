using System.Windows.Threading;
using Sonora.Appearance.Layout;
using Sonora.Appearance.Theming;
using Sonora.Core;
using Sonora.Services;

namespace Sonora.ViewModels;

/// <summary>The panel arrangement the window is built from. Every change is saved 1 s later.</summary>
public sealed class LayoutViewModel : ObservableObject
{
    readonly DispatcherTimer _save;
    double _width = 1320, _height = 840;

    public LayoutViewModel(Dispatcher ui)
    {
        _save = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
        _save.Tick += (_, _) => { _save.Stop(); if (!SaveDisabled) StateStore.SaveLayout(Layout); };
    }

    public DockLayout Layout { get; private set; } = DockLayout.Default();

    /// <summary>Set by diagnostics renders so they never overwrite the user's layout.</summary>
    public bool SaveDisabled { get; set; }

    public event Action? Changed;

    public void Load(DockLayout? stored, double width, double height)
    {
        _width = width;
        _height = height;
        Layout = DockLayoutOps.Repair(stored, width, height);
        Raise(save: false);
    }

    /// <summary>Bounds used by <see cref="DockLayoutOps.Repair"/>: the real window size, not the initial one.</summary>
    public void UpdateWindowSize(double width, double height)
    {
        if (width > 0) _width = width;
        if (height > 0) _height = height;
    }

    public void Move(string panel, DockZone zone) => Mutate(l => DockLayoutOps.MoveToZone(l, panel, zone));

    public void MoveToGroup(string panel, DockZone zone, int group, DropPosition pos) =>
        Mutate(l => DockLayoutOps.MoveToGroup(l, panel, zone, group, pos));

    public void Close(string panel) => Mutate(l => DockLayoutOps.Close(l, panel));

    public void Show(string panel) => Mutate(l =>
    {
        if (DockLayoutOps.Find(l, panel) is { } at)
        {
            l.Zone(at.Zone).IsCollapsed = false;
            DockLayoutOps.SetActive(l, panel);
        }
        else DockLayoutOps.Show(l, panel);
    });

    public void SetActive(string panel) => Mutate(l => DockLayoutOps.SetActive(l, panel));

    public bool IsVisible(string panel) =>
        DockLayoutOps.Find(Layout, panel) is { } at && !Layout.Zone(at.Zone).IsCollapsed
        && Layout.Zone(at.Zone).Groups[at.Group].Panels[Layout.Zone(at.Zone).Groups[at.Group].ActiveIndex] == panel;

    public void Toggle(string panel)
    {
        if (IsVisible(panel)) Close(panel);
        else Show(panel);
    }

    public void Reset(ThemeDefinition theme) =>
        Mutate(l => { var fresh = theme.DefaultLayout?.Clone() ?? DockLayout.Default(); Copy(fresh, l); });

    public void SetZoneSize(DockZone zone, double size)
    {
        Layout.Zone(zone).Size = Math.Max(0, size);
        ScheduleSave();
    }

    public void SetGroupRatios(DockZone zone, IReadOnlyList<double> ratios)
    {
        var groups = Layout.Zone(zone).Groups;
        for (int i = 0; i < groups.Count && i < ratios.Count; i++) groups[i].Ratio = Math.Max(0.05, ratios[i]);
        ScheduleSave();
    }

    public void ToggleCollapsed(DockZone zone) => Mutate(l => l.Zone(zone).IsCollapsed = !l.Zone(zone).IsCollapsed);

    void Mutate(Action<DockLayout> change)
    {
        change(Layout);
        Layout = DockLayoutOps.Repair(Layout, _width, _height);
        Raise(save: true);
    }

    void Raise(bool save)
    {
        Changed?.Invoke();
        OnPropertyChanged(nameof(Layout));
        if (save) ScheduleSave();
    }

    void ScheduleSave()
    {
        _save.Stop();
        _save.Start();
    }

    static void Copy(DockLayout from, DockLayout to)
    {
        to.Left = from.Left; to.Right = from.Right; to.Top = from.Top; to.Bottom = from.Bottom;
        to.Hidden = from.Hidden; to.LastZone = from.LastZone;
    }
}
