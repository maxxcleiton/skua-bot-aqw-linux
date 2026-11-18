using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.GameProxy;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Options;
using Skua.Core.Plugins;
using Skua.Core.Scripts;
using Skua.Core.Scripts.Helpers;
using Skua.Core.Services;
using Skua.Core.Skills;
using Skua.Core.Utils;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using System.Reflection;

namespace Skua.Core.AppStartup;

public static class Services
{
    public static IServiceCollection AddCommonServices(this IServiceCollection services)
    {
        services.AddTransient(typeof(Lazy<>), typeof(LazyInstance<>));

        services.AddSingleton(typeof(IMessenger), WeakReferenceMessenger.Default);
        services.AddSingleton(typeof(StrongReferenceMessenger), StrongReferenceMessenger.Default);

        services.AddSingleton<IDecamelizer, Decamelizer>();
        services.AddSingleton<IGetScriptsService, GetScriptsService>();
        services.AddSingleton<IProcessService, ProcessStartService>();

        return services;
    }

    public static IServiceCollection AddCompiler(this IServiceCollection services)
    {
        services.AddTransient(CreateCompiler);

        return services;
    }

    public static IServiceCollection AddScriptableObjects(this IServiceCollection services)
    {
        services.AddSingleton<IScriptInterface, ScriptInterface>();
        services.AddSingleton<IScriptManager, ScriptManager>();
        services.AddSingleton<IScriptStatus, ScriptManager>();

        services.AddSingleton<IScriptInventoryHelper, ScriptInventoryHelper>();
        services.AddSingleton<IScriptInventory, ScriptInventory>();
        services.AddSingleton<IScriptHouseInv, ScriptHouseInv>();
        services.AddSingleton<IScriptTempInv, ScriptTempInv>();
        services.AddSingleton<IScriptBank, ScriptBank>();

        services.AddSingleton<IAdvancedSkillContainer, AdvancedSkillContainer>();
        services.AddSingleton<IUltraBossHelper, UltraBossHelper>();
        services.AddSingleton<IScriptCombat, ScriptCombat>();
        services.AddSingleton<IScriptKill, ScriptKill>();
        services.AddSingleton<IScriptHunt, ScriptHunt>();
        services.AddSingleton<IScriptSkill, ScriptSkill>();
        services.AddSingleton<IScriptAuto, ScriptAuto>();
        services.AddSingleton<IScriptSelfAuras, ScriptSelfAuras>();
        services.AddSingleton<IScriptTargetAuras, ScriptTargetAuras>();

        services.AddSingleton<IScriptFaction, ScriptFaction>();
        services.AddSingleton<IScriptMonster, ScriptMonster>();
        services.AddSingleton<IScriptPlayer, ScriptPlayer>();
        services.AddSingleton<IScriptQuest, ScriptQuest>();
        services.AddSingleton<IScriptBoost, ScriptBoost>();
        services.AddSingleton<IScriptShop, ScriptShop>();
        services.AddSingleton<IScriptDrop, ScriptDrop>();
        services.AddSingleton<IScriptMap, ScriptMap>();

        services.AddSingleton<IScriptServers, ScriptServers>();
        services.AddSingleton<IScriptEvent, ScriptEvent>();
        services.AddSingleton<IScriptSend, ScriptSend>();

        services.AddTransient<IScriptOptionContainer, ScriptOptionContainer>();
        services.AddTransient<IOptionContainer, OptionContainer>();
        services.AddSingleton<IScriptOption, ScriptOption>();
        services.AddSingleton<IScriptLite, ScriptLite>();

        services.AddSingleton<IScriptBotStats, ScriptBotStats>();
        services.AddSingleton<IScriptHandlers, ScriptHandlers>();
        services.AddSingleton<IScriptWait, ScriptWait>();
        services.AddSingleton<IScriptAccounts, ScriptAccounts>();

        services.AddSingleton<ICaptureProxy, CaptureProxy>();

        services.AddSingleton<IPluginManager, PluginManager>();
        services.AddTransient<IPluginContainer, PluginContainer>();
        services.AddSingleton<IPluginHelper, PluginHelper>();

        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IQuestDataLoaderService, QuestDataLoaderService>();
        services.AddSingleton<IGrabberService, GrabberService>();
        services.AddSingleton<IClientFilesService, ClientFilesService>();
        services.AddSingleton<IAuraMonitorService, AuraMonitorService>();
        services.AddSingleton<IJunkService, JunkService>();
        services.AddSingleton<BackgroundThemeService>();

        return services;
    }

    public static IServiceCollection AddSkuaMainAppViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton(MainMenu.CreateViewModel);
        services.AddTransient<BotWindowViewModel>();
        services.AddSingleton<IEnumerable<BotControlViewModelBase>>(s => new List<BotControlViewModelBase>()
        {
            s.GetRequiredService<ScriptLoaderViewModel>(),
            s.GetRequiredService<ScriptRepoViewModel>(),
            s.GetRequiredService<LogsViewModel>(),
            s.GetRequiredService<AutoViewModel>(),
            s.GetRequiredService<JumpViewModel>(),
            s.GetRequiredService<FastTravelViewModel>(),
            s.GetRequiredService<CurrentDropsViewModel>(),
            s.GetRequiredService<JunkItemsViewModel>(),
            s.GetRequiredService<RuntimeHelpersViewModel>(),
            s.GetRequiredService<LoaderViewModel>(),
            s.GetRequiredService<GrabberViewModel>(),
            s.GetRequiredService<GameOptionsViewModel>(),
            s.GetRequiredService<ApplicationOptionsViewModel>(),
            s.GetRequiredService<ConsoleViewModel>(),
            s.GetRequiredService<AdvancedSkillsViewModel>(),
            s.GetRequiredService<PacketInterceptorViewModel>(),
            s.GetRequiredService<PacketSpammerViewModel>(),
            s.GetRequiredService<PacketLoggerViewModel>(),
            s.GetRequiredService<ApplicationThemesViewModel>(),
            s.GetRequiredService<HotKeysViewModel>(),
            s.GetRequiredService<PluginsViewModel>()
        });

        services.AddTransient<LoaderViewModel>();

        services.AddTransient(Grabber.CreateViewModel);
        services.AddSingleton(Grabber.CreateListViewModels);

        services.AddSingleton<JumpViewModel>();

        services.AddSingleton<FastTravelViewModel>();
        services.AddTransient<FastTravelEditorViewModel>();
        services.AddTransient<FastTravelEditorDialogViewModel>();

        services.AddSingleton<LogsViewModel>();
        services.AddSingleton(LogTabs.CreateViewModels);

        services.AddSingleton(Options.CreateGameOptions);
        services.AddSingleton(Options.CreateAppOptions);

        services.AddSingleton(PacketLogger.CreateViewModel);
        services.AddSingleton<PacketSpammerViewModel>();
        services.AddSingleton(PacketInterceptor.CreateViewModel);

        services.AddTransient<ConsoleViewModel>();

        services.AddSingleton<ScriptRepoViewModel>();
        services.AddSingleton<ScriptLoaderViewModel>();

        services.AddSingleton<AdvancedSkillsViewModel>();
        services.AddSingleton<AdvancedSkillEditorViewModel>();
        services.AddSingleton<SavedAdvancedSkillsViewModel>();
        services.AddTransient<SkillRulesViewModel>();

        services.AddSingleton<AutoViewModel>();

        services.AddSingleton<BoostsViewModel>();
        services.AddSingleton<ScriptStatsViewModel>();
        services.AddSingleton<RuntimeHelpersViewModel>();
        services.AddSingleton<NotifyDropViewModel>();
        services.AddSingleton<ToPickupDropsViewModel>();
        services.AddSingleton<RegisteredQuestsViewModel>();
        services.AddSingleton<CurrentDropsViewModel>();
        services.AddSingleton<JunkItemsViewModel>();

        services.AddThemeViewModels();

        services.AddSingleton<PluginsViewModel>();

        services.AddSingleton<HotKeysViewModel>();
        services.AddSingleton(HotKeys.CreateHotKeys);

        services.AddSingleton(CoreBots.CreateViewModel);
        services.AddSingleton(CoreBots.CreateOptions);
        services.AddSingleton<CBOClassEquipmentViewModel>();
        services.AddSingleton<CBOClassSelectViewModel>();
        services.AddSingleton<CBOLoadoutViewModel>();

        return services;
    }

    public static IServiceCollection AddThemeViewModels(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationThemesViewModel>();
        services.AddSingleton<ThemeSettingsViewModel>();
        services.AddSingleton<ColorSchemeEditorViewModel>();
        services.AddSingleton<BackgroundThemeViewModel>();

        return services;
    }

    public static IServiceCollection AddSkuaManagerViewModels(this IServiceCollection services)
    {
        services.AddThemeViewModels();
        services.AddSingleton<AccountManagerViewModel>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<IClientUpdateService, ClientUpdateService>();
        services.AddSingleton<IClientFilesService, ClientFilesService>();
        services.AddSingleton<ClientUpdatesViewModel>();
        services.AddSingleton<GitHubAuthViewModel>();
        services.AddSingleton<ScriptRepoViewModel>(s =>
        {
            ScriptRepoViewModel vm = new(s.GetRequiredService<IGetScriptsService>(), s.GetRequiredService<IProcessService>())
            {
                IsManagerMode = true
            };
            return vm;
        });
        services.AddSingleton<GoalsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<ChangeLogsViewModel>();
        services.AddSingleton(SkuaManager.CreateViewModel);
        services.AddSingleton(SkuaManager.CreateOptionsViewModel);

        return services;
    }

    private static List<PortableExecutableReference>? _cachedBaseReferences;
    private static readonly object _referenceCacheLock = new();

    private static Compiler CreateCompiler(IServiceProvider s)
    {
        Compiler compiler = new();

        if (_cachedBaseReferences == null)
        {
            lock (_referenceCacheLock)
            {
                if (_cachedBaseReferences == null)
                {
                    string runtimeDir = Path.GetDirectoryName(typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly.Location)!;
                    string[] refPaths = {
                        typeof(object).GetTypeInfo().Assembly.Location,
                        typeof(Console).GetTypeInfo().Assembly.Location,
                        typeof(Enumerable).Assembly.Location,
                        typeof(ScriptManager).Assembly.Location,
                        typeof(IScriptInterface).Assembly.Location,
                        typeof(Skua.Core.Models.ClientFileSources).Assembly.Location,
                        typeof(Skua.Core.Utils.RangedObservableCollection<>).Assembly.Location,
                        typeof(Newtonsoft.Json.JsonConvert).Assembly.Location,
                        typeof(CommunityToolkit.Mvvm.DependencyInjection.Ioc).Assembly.Location,
                        typeof(System.Drawing.Color).Assembly.Location,
                        typeof(System.Net.Http.HttpClient).Assembly.Location,
                        typeof(System.Net.Sockets.TcpClient).Assembly.Location,
                        typeof(System.Data.Common.DbConnection).Assembly.Location,
                        typeof(System.Diagnostics.Process).Assembly.Location,
                        typeof(System.Timers.Timer).Assembly.Location,
                        typeof(System.Xml.Linq.XDocument).Assembly.Location,
                        typeof(System.IO.Compression.ZipFile).Assembly.Location,
                        typeof(System.Text.RegularExpressions.Regex).Assembly.Location,
                        typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location,
                        Path.Combine(runtimeDir, "System.Runtime.dll"),
                        Path.Combine(runtimeDir, "netstandard.dll"),
                        Path.Combine(runtimeDir, "System.Collections.dll"),
                        Path.Combine(runtimeDir, "System.Collections.Concurrent.dll"),
                        Path.Combine(runtimeDir, "System.ComponentModel.dll"),
                        Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll"),
                        Path.Combine(runtimeDir, "System.ComponentModel.TypeConverter.dll"),
                        Path.Combine(runtimeDir, "System.Diagnostics.Process.dll"),
                        Path.Combine(runtimeDir, "System.ObjectModel.dll"),
                        Path.Combine(runtimeDir, "System.Net.Primitives.dll"),
                        Path.Combine(runtimeDir, "System.Net.Requests.dll"),
                        Path.Combine(runtimeDir, "System.Net.WebClient.dll"),
                        Path.Combine(runtimeDir, "System.Security.Cryptography.dll"),
                        Path.Combine(runtimeDir, "System.Text.Json.dll"),
                        Path.Combine(runtimeDir, "System.Threading.dll"),
                        Path.Combine(runtimeDir, "System.Threading.Thread.dll")
                    };

                    HashSet<string> addedLocations = new(StringComparer.OrdinalIgnoreCase);
                    List<PortableExecutableReference> refs = new();

                    void TryAddRef(string? path)
                    {
                        if (!string.IsNullOrEmpty(path) && File.Exists(path) && addedLocations.Add(path))
                            refs.Add(MetadataReference.CreateFromFile(path));
                    }

                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location) && !asm.Location.Contains("xunit"))
                            TryAddRef(asm.Location);
                    }

                    foreach (string path in refPaths)
                        TryAddRef(path);

                    _cachedBaseReferences = refs;
                }
            }
        }

        compiler.AddAssemblies(_cachedBaseReferences);
        compiler.AddNamespaces(new[]
        {
            "System",
            "System.Collections",
            "System.Collections.Generic",
            "System.Diagnostics",
            "System.Drawing",
            "System.Dynamic",
            "System.Globalization",
            "System.IO",
            "System.Linq",
            "System.Net",
            "System.Net.Http",
            "System.Reflection",
            "System.Runtime.CompilerServices",
            "System.Text",
            "System.Text.RegularExpressions",
            "System.Threading",
            "System.Threading.Tasks",
            "System.Timers",
            "System.Windows.Forms",
            "Skua.Core",
            "Skua.Core.Interfaces",
            "Skua.Core.Models",
            "Skua.Core.Models.Items",
            "Skua.Core.Models.Monsters",
            "Skua.Core.Models.Players",
            "Skua.Core.Models.Quests",
            "Skua.Core.Models.Servers",
            "Skua.Core.Models.Shops",
            "Skua.Core.Models.Skills",
            "Skua.Core.Models.Auras",
            "Skua.Core.Models.Factions",
            "Skua.Core.Options",
            "Skua.Core.Utils",
            "CommunityToolkit.Mvvm.DependencyInjection",
            "Newtonsoft.Json",
            "Newtonsoft.Json.Linq",
        });
        compiler.SaveGeneratedCode = true;
        return compiler;
    }
}