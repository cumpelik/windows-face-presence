using System.IO;
using FacePresence.App;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FacePresence.UI;

/// <summary>Shows the MIT license and the third-party notices embedded in the EXE.</summary>
public partial class LicensesWindow : FluentWindow
{
    public LicensesWindow()
    {
        InitializeComponent();
        Icon = TrayIcons.AppImage;
        TitleBar.Icon = new ImageIcon { Source = TrayIcons.AppImage };
        SourceInitialized += (_, _) => SystemThemeWatcher.Watch(this);
        Text.Text = Read("LICENSE.txt") + "\n\n\n" + Read("THIRD-PARTY-NOTICES.txt");
    }

    private static string Read(string name)
    {
        using var stream = typeof(LicensesWindow).Assembly.GetManifestResourceStream(name);
        if (stream == null) return name + " not found.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
