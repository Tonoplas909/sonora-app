using System.ComponentModel;
using System.Windows.Data;
using Sonora.Appearance.Localization;

namespace Sonora.Localization;

/// <summary>
/// What the interface's texts are bound to: <c>{loc:T nav.home}</c> reads <c>LocSource.Instance["nav.home"]</c>. When
/// the language changes it says that every indexed value changed, and each bound text redraws itself in the new
/// language without the window being rebuilt.
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    LocSource() => Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
