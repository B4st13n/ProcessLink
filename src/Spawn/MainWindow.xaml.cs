using System.ComponentModel;
using System.Windows;
using Spawn.ViewModels;

namespace Spawn;

public partial class MainWindow : Window
{
    /// <summary>Set to true when the app is quitting for real (tray → Quit); otherwise closing hides to the tray.</summary>
    public bool AllowClose { get; set; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        Icon = TrayIconFactory.CreateImageSource();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
