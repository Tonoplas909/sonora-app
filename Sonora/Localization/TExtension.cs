using System.Windows.Data;
using System.Windows.Markup;

namespace Sonora.Localization;

/// <summary>
/// A text of the interface, in the language in use: <c>Text="{loc:T nav.home}"</c> (needs
/// <c>xmlns:loc="clr-namespace:Sonora.Localization"</c>). It is a one-way binding, so the text follows a change of language.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
