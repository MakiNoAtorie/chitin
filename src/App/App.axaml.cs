using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Chitin;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        MainWindow? window = null;
        // A WinExe has no console on Windows: the status line is where the user sees the error.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Console.Error.WriteLine(args.Exception);
            window?.Viewport.World.SetError($"Error: {args.Exception.Message}");
            args.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            window = new MainWindow();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
