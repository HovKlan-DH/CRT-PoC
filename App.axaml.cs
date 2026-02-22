using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace VRT
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // ###########################################################################################
        // Shows the splash screen for 10 seconds, then switches to the main window.
        // ###########################################################################################
        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var splash = new Splash();
                desktop.MainWindow = splash;
                splash.Show();

                await Task.Delay(500);

                var main = new Main();
                desktop.MainWindow = main;
                main.Show();
                splash.Close();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}