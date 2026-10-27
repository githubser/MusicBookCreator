using System.Globalization;
using System.Windows;

namespace MusicBookCreator;

public partial class App : Application
{
    public App()
    {
        // The UI is English; format numbers consistently ("1.2°") regardless of the Windows region.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}
