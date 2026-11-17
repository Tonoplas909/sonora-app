namespace Sonora.Appearance.Layout;

/// <summary>Mutations of a <see cref="DockLayout"/>: moving, tabbing, closing, showing and repairing panels.</summary>
public static class DockLayoutOps
{
    static readonly DockZone[] Zones = [DockZone.Left, DockZone.Right, DockZone.Top, DockZone.Bottom];

    public static (DockZone Zone, int Group, int Index)? Find(DockLayout l, string panel)
    {
        foreach (var z in Zones)
        {
            var groups = l.Zone(z).Groups;
            for (int g = 0; g < groups.Count; g++)
            {
                var i = groups[g].Panels.IndexOf(panel);
                if (i >= 0) return (z, g, i);
            }
        }
        return null;
    }

    public static void MoveToZone(DockLayout l, string panel, DockZone zone)
    {
        Remove(l, panel);
        var z = l.Zone(zone);
        z.Groups.Add(new DockGroup { Panels = [panel] });
        z.IsCollapsed = false;
        if (z.Size <= 0) z.Size = DefaultSize(zone);
        l.LastZone[panel] = zone;
    }

    public static void MoveToGroup(DockLayout l, string panel, DockZone zone, int group, DropPosition pos)
    {
        var z = l.Zone(zone);
        if (group < 0 || group >= z.Groups.Count) { MoveToZone(l, panel, zone); return; }
        var target = z.Groups[group];

        if (target.Panels.Contains(panel))
        {
            if (target.Panels.Count == 1) return; // dropped onto itself
            if (pos == DropPosition.Tab) { target.ActiveIndex = target.Panels.IndexOf(panel); return; }
        }

        Remove(l, panel);
        var index = z.Groups.IndexOf(target); // removal may have shifted groups
        if (pos == DropPosition.Tab)
        {
            target.Panels.Add(panel);
            target.ActiveIndex = target.Panels.Count - 1;
        }
        else
        {
            var half = target.Ratio / 2;
            target.Ratio = half;
            z.Groups.Insert(pos == DropPosition.Before ? index : index + 1, new DockGroup { Panels = [panel], Ratio = half });
        }
        z.IsCollapsed = false;
        if (z.Size <= 0) z.Size = DefaultSize(zone);
        l.LastZone[panel] = zone;
    }

    public static void Close(DockLayout l, string panel)
    {
        if (Find(l, panel) is { } at) l.LastZone[panel] = at.Zone;
        Remove(l, panel);
        if (!l.Hidden.Contains(panel)) l.Hidden.Add(panel);
    }

    public static void Show(DockLayout l, string panel)
    {
        if (Find(l, panel) != null) return;
        var zone = l.LastZone.TryGetValue(panel, out var z) ? z : PanelIds.DefaultZone(panel);
        MoveToZone(l, panel, zone);
    }

    public static void SetActive(DockLayout l, string panel)
    {
        if (Find(l, panel) is { } at) l.Zone(at.Zone).Groups[at.Group].ActiveIndex = at.Index;
    }

    /// <summary>
    /// Makes any stored layout usable: unknown or duplicate panels removed, missing ones restored to their
    /// default zone, the player never hidden, empty groups dropped, sizes clamped to the window.
    /// </summary>
    public static DockLayout Repair(DockLayout? layout, double width, double height)
    {
        var l = layout?.Clone() ?? DockLayout.Default();
        // A hand-edited or truncated file may contain nulls anywhere.
        l.Left ??= new DockZoneState();
        l.Right ??= new DockZoneState();
        l.Top ??= new DockZoneState();
        l.Bottom ??= new DockZoneState();
        foreach (var z in Zones)
        {
            var zone = l.Zone(z);
            zone.Groups ??= [];
            zone.Groups.RemoveAll(g => g is null);
            if (double.IsNaN(zone.Size) || zone.Size < 0) zone.Size = 0;
        }
        var seen = new HashSet<string>();

        foreach (var z in Zones)
        {
            var zone = l.Zone(z);
            zone.Groups ??= [];
            foreach (var g in zone.Groups)
            {
                g.Panels = (g.Panels ?? []).Where(p => PanelIds.All.Contains(p) && seen.Add(p)).ToList();
                if (g.Ratio <= 0 || double.IsNaN(g.Ratio)) g.Ratio = 1;
                g.ActiveIndex = Math.Clamp(g.ActiveIndex, 0, Math.Max(0, g.Panels.Count - 1));
            }
            zone.Groups.RemoveAll(g => g.Panels.Count == 0);
        }

        l.LastZone ??= [];
        l.Hidden = (l.Hidden ?? []).Where(p => PanelIds.All.Contains(p) && p != PanelIds.Player && seen.Add(p)).ToList();

        foreach (var p in PanelIds.All)
        {
            if (seen.Contains(p)) continue;
            if (PanelIds.HiddenByDefault(p)) l.Hidden.Add(p); // optional panels stay closed until asked for
            else MoveToZone(l, p, l.LastZone.TryGetValue(p, out var last) ? last : PanelIds.DefaultZone(p));
        }

        foreach (var z in Zones)
        {
            var zone = l.Zone(z);
            if (zone.Groups.Count == 0) continue;
            bool side = z is DockZone.Left or DockZone.Right;
            double min = side ? 200 : 72, max = side ? width * 0.45 : height * 0.40;
            zone.Size = Math.Clamp(zone.Size, min, Math.Max(min, max));
        }
        return l;
    }

    static void Remove(DockLayout l, string panel)
    {
        l.Hidden ??= [];
        l.Hidden.Remove(panel);
        foreach (var z in Zones)
        {
            var groups = l.Zone(z).Groups;
            foreach (var g in groups)
            {
                var i = g.Panels.IndexOf(panel);
                if (i < 0) continue;
                g.Panels.RemoveAt(i);
                if (i < g.ActiveIndex) g.ActiveIndex--; // the active tab keeps its panel
                if (g.ActiveIndex >= g.Panels.Count) g.ActiveIndex = Math.Max(0, g.Panels.Count - 1);
            }
            groups.RemoveAll(g => g.Panels.Count == 0);
        }
    }

    static double DefaultSize(DockZone z) => z is DockZone.Left or DockZone.Right ? 300 : 88;
}
