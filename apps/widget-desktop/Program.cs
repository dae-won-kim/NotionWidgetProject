using Avalonia;
using System;

namespace WidgetDesktop;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // 콘솔에서 실행 시 바로 보임
            Console.WriteLine("=== UNHANDLED EXCEPTION ===");
            Console.WriteLine(ex.ToString());

            // 더블클릭 실행 시에도 확인 가능하게
            System.IO.File.WriteAllText(
                "startup-error.txt",
                ex.ToString()
            );

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
