using System.ComponentModel;

namespace FacePresence.Localization;

/// <summary>Binding source for XAML: <c>{Binding [card.camera], Source={x:Static loc:Loc.I}}</c>. Updates on language change.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private Loc() => L.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public string this[string key] => L.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}
