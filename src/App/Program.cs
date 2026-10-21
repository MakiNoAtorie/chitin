using Avalonia;

namespace Chitin;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--selftest")
        {
            return SelfTest.Run(args[1]);
        }

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.WriteLine("Usage: Chitin [--selftest <path>]");
            Console.WriteLine("  --selftest <path>  Render the deterministic test scene to <path>.");
            Console.WriteLine("  --help             Show this help.");
            return 0;
        }

        if (args.Length > 0)
        {
            Console.Error.WriteLine("Usage: Chitin [--selftest <path>]");
            return 2;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
