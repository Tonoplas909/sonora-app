using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sonora.Views;

/// <summary>
/// A shelf of the home page that only creates its cards, and so decodes their covers, once it is about to come
/// into view. A home page holds around eight shelves of ten cards but shows two or three of them: building the
/// rest up front kept some sixty covers in memory that nobody had looked at. Set on the Border that wraps the
/// shelf's ItemsControl, which has no ItemsSource of its own: this binds it to the shelf's "Items" on demand.
/// </summary>
public static class LazyShelf
{
    /// <summary>How far outside the visible area a shelf is built, so it is ready before it scrolls in.</summary>
    const double Reach = 300;

    /// <summary>Height of a built shelf once one has been measured: the stand-in of the others, so scrolling does not jump.</summary>
    static double _knownHeight;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(LazyShelf), new PropertyMetadata(false, OnEnabled));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    static readonly DependencyProperty BuiltProperty =
        DependencyProperty.RegisterAttached("Built", typeof(bool), typeof(LazyShelf), new PropertyMetadata(false));

    static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // The Border's child does not exist yet while its template is being read: wait for it to be loaded.
        if (d is Border shelf && e.NewValue is true) shelf.Loaded += OnLoaded;
    }

    static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var shelf = (Border)sender;
        if ((bool)shelf.GetValue(BuiltProperty) || shelf.Child is not ItemsControl list) return;

        var scroller = FindScrollViewer(shelf);
        if (scroller == null) { Build(shelf, list); return; } // nothing scrolls it: no reason to wait

        // Holds the room of a built shelf while empty, so the page keeps its length and the scroll bar does not jump.
        shelf.MinHeight = _knownHeight > 0 ? _knownHeight : (App.Themes?.CardPixels ?? 160) + 56;

        ScrollChangedEventHandler? onScroll = null;
        bool Check()
        {
            if ((bool)shelf.GetValue(BuiltProperty)) return true;
            if (!shelf.IsLoaded || !scroller.IsAncestorOf(shelf)) return false;
            double top = shelf.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y;
            double height = Math.Max(shelf.ActualHeight, shelf.MinHeight);
            if (top >= scroller.ViewportHeight + Reach || top + height <= -Reach) return false;
            Build(shelf, list);
            return true;
        }
        onScroll = (_, _) => { if (Check()) scroller.ScrollChanged -= onScroll; };
        shelf.Unloaded += (_, _) => scroller.ScrollChanged -= onScroll;

        if (!Check()) scroller.ScrollChanged += onScroll;
    }

    static void Build(Border shelf, ItemsControl list)
    {
        shelf.SetValue(BuiltProperty, true);
        shelf.ClearValue(FrameworkElement.MinHeightProperty);
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Items"));
        // Once laid out, its real height replaces the estimate for the shelves still to come.
        shelf.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (shelf.ActualHeight > 0) _knownHeight = shelf.ActualHeight; });
    }

    static ScrollViewer? FindScrollViewer(DependencyObject from)
    {
        for (var d = VisualTreeHelper.GetParent(from); d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer sv) return sv;
        return null;
    }
}
