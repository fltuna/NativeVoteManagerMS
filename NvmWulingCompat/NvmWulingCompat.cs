using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NativeVoteManagerMS.Shared;
using Sharp.Shared;
using Wuling.Abstract;

namespace NvmWulingCompat;

public class NvmWulingCompat : IModSharpModule
{
    public NvmWulingCompat(
        ISharedSystem sharedSystem,
        string dllPath,
        string sharpPath,
        Version? version,
        IConfiguration coreConfiguration,
        bool hotReload)
    {
        ArgumentNullException.ThrowIfNull(dllPath);
        ArgumentNullException.ThrowIfNull(sharpPath);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(coreConfiguration);
        ArgumentNullException.ThrowIfNull(sharedSystem);
        _sharedSystem = sharedSystem;

        var factory = _sharedSystem.GetLoggerFactory();
        _logger = factory.CreateLogger(DisplayName);
    }

    public string DisplayName => "NativeVoteManagerMS - WulingCompat";
    public string DisplayAuthor => "faketuna";

    private readonly ISharedSystem _sharedSystem;
    private readonly ILogger _logger;

    public bool Init() => true;

    public void PostInit()
    {
    }

    public void OnAllModulesLoaded()
    {
        var nvm = _sharedSystem.GetSharpModuleManager()
            .GetRequiredSharpModuleInterface<INativeVoteManager>(INativeVoteManager.ModSharpModuleIdentity).Instance!;

        var wuling = _sharedSystem.GetSharpModuleManager()
            .GetRequiredSharpModuleInterface<IWuling>(IWuling.Identity).Instance!;

        nvm.SetDefaultMenuCompat(new WulingMenuCompat(wuling.Menu, wuling.Registry));
        nvm.SetDefaultPermissionCompat(new WulingPermissionCompat(wuling.Authority));

        var stringLocalizer = wuling.Localizer.CreateStringLocalizer(nvm.ModuleDirectory, appendLangDir: true);
        nvm.SetLocalizer(new WulingLocalizerCompat(stringLocalizer));

        _logger.LogInformation("Registered Wuling menu, permission and localizer compat for NativeVoteManagerMS.");
    }

    public void Shutdown()
    {
    }
}
