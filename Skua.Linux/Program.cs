using System;
using Avalonia;
using Newtonsoft.Json;
using Skua.Core.Models.Converters;

namespace Skua.Linux;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings
        {
            Converters = { new IntConverter() }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
