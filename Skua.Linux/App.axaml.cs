using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using Skua.Core.ViewModels.Manager;
using Skua.Linux.Services;
using Westwind.Scripting;

namespace Skua.Linux;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }
    private IScriptInterface? _bot;
    private SkuaLinuxStartupHandler? _startup;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        Services = ConfigureServices();
        Services.GetRequiredService<ISettingsService>().SetApplicationVersion();
        FlashTrustManager.EnsureTrustFile();

        Services.GetRequiredService<IClientFilesService>().CreateDirectories();
        Services.GetRequiredService<IClientFilesService>().CreateFiles();
        _ = Task.Run(async () => await Services.GetRequiredService<IScriptServers>().GetServers());

        _bot = Services.GetRequiredService<IScriptInterface>();
        _ = Services.GetRequiredService<ILogService>();

        string[] args = Environment.GetCommandLineArgs();
        _startup = new SkuaLinuxStartupHandler(args, _bot, Services.GetRequiredService<ISettingsService>());
        _startup.Execute();

        RoslynLifetimeManager.WarmupRoslyn();
        ManagedWindows.Register(Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Services);
            desktop.Exit += OnExit;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                IGetScriptsService getScripts = Services.GetRequiredService<IGetScriptsService>();
                await getScripts.UpdateQuestDataFile();
            }
            catch { }
        });

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (Services is null)
            return;

        try
        {
            Services.GetRequiredService<IFlashUtil>().Dispose();
            Services.GetRequiredService<ICaptureProxy>().Stop();
            Services.GetService<AutoCombatPotionService>()?.Dispose();
            await ((IAsyncDisposable)Services.GetRequiredService<IScriptBoost>()).DisposeAsync();
            await ((IAsyncDisposable)Services.GetRequiredService<IScriptBotStats>()).DisposeAsync();
            await ((IAsyncDisposable)Services.GetRequiredService<IScriptDrop>()).DisposeAsync();
            await Ioc.Default.GetRequiredService<IScriptManager>().StopScript();
            if (_bot is IScriptInterfaceManager botMgr)
                await botMgr.StopTimerAsync();
            _startup?.Dispose();
            WeakReferenceMessenger.Default.Cleanup();
            WeakReferenceMessenger.Default.Reset();
            StrongReferenceMessenger.Default.Reset();
            RoslynLifetimeManager.ShutdownRoslyn();
        }
        catch { }
    }

    private static IServiceProvider ConfigureServices()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddLinuxServices();
        services.AddCommonServices();
        services.AddScriptableObjects();
        services.AddCompiler();
        services.AddSkuaMainAppViewModels();
        services.AddSingleton<AccountManagerViewModel>();

        ServiceProvider provider = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(provider);
        return provider;
    }
}
