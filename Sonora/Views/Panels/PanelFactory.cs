using System.Windows;
using Sonora.Appearance.Layout;

namespace Sonora.Views.Panels;

/// <summary>One cached instance per panel, so a panel keeps its state (scroll, selection) when it is moved.</summary>
public static class PanelFactory
{
    static readonly Dictionary<string, PanelView> Cache = [];

    public static PanelView Get(string panelId)
    {
        if (Cache.TryGetValue(panelId, out var view)) return view;
        view = panelId switch
        {
            PanelIds.Navigation => new NavigationPanel(),
            PanelIds.Library => new LibraryPanel(),
            PanelIds.Queue => new QueuePanel(),
            PanelIds.Player => new PlayerPanel(),
            PanelIds.Lyrics => new LyricsPanel(),
            _ => throw new ArgumentOutOfRangeException(nameof(panelId), panelId, null),
        };
        Cache[panelId] = view;
        return view;
    }

    /// <summary>Detaches a cached panel from its previous host before it is placed elsewhere.</summary>
    public static void Detach(PanelView view)
    {
        switch (view.Parent)
        {
            case System.Windows.Controls.Panel p: p.Children.Remove(view); break;
            case System.Windows.Controls.ContentControl c: c.Content = null; break;
            case System.Windows.Controls.Decorator d: d.Child = null; break;
        }
    }

    public static string Title(string panelId) => panelId switch
    {
        PanelIds.Navigation => "Navigation",
        PanelIds.Library => "Bibliothèque",
        PanelIds.Queue => "File d'attente",
        PanelIds.Player => "Lecture",
        PanelIds.Lyrics => "Paroles",
        _ => panelId,
    };

    public static string Glyph(string panelId) => panelId switch
    {
        PanelIds.Navigation => "",
        PanelIds.Library => "",
        PanelIds.Queue => "",
        PanelIds.Player => "",
        PanelIds.Lyrics => "",
        _ => "",
    };
}
