using System.Windows;

namespace Vestigium.Logging.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }
}
