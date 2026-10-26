using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicBookCreator;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        Resources.Add("Not", new NotConverter());
        InitializeComponent();
        DataContext = viewModel = new MainViewModel();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (viewModel.IsBusy
            && MessageBox.Show("Die Verarbeitung läuft noch. Trotzdem beenden?", AppInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        viewModel.SaveSettings();
        base.OnClosing(e);
    }
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
