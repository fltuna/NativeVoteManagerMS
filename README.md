# NativeVoteManagerMS

A [ModSharp](https://github.com/Kxnrl/modsharp-public) module for Counter-Strike 2 that provides a native vote management system. Supports both YesNo votes (using CS2's built-in vote UI) and MultiChoice votes (using a pluggable menu system).

## Features

- **YesNo Vote** - Uses CS2's native vote UI (`CCSUsrMsg_VoteStart` / `CCSUsrMsg_VotePass` / `CCSUsrMsg_VoteFailed`)
- **MultiChoice Vote** - Menu-based voting with configurable options
- **Pluggable Architecture** - Menu system and permission system are abstracted via `IMenuCompat` / `IPermissionCompat`
- **Localization** - Per-client localized chat messages via per-language `lang/*.json` files, provided by a compat module (`NvmWulingCompat` bridges to Wuling's localizer)
- **Commands** - `!cancelvote` (with permission check), `!revote` (reopen MultiChoice menu)
- **Automatic Cleanup** - Vote state is cleaned up on map change

## Project Structure

| Project                      | Description                                                                                               |
|------------------------------|-----------------------------------------------------------------------------------------------------------|
| `NativeVoteManagerMS`        | Core module - vote lifecycle, commands, localization                                                      |
| `NativeVoteManagerMS.Shared` | Public API - `INativeVoteManager`, types, compat interfaces                                               |
| `NvmFPMCompat`               | FPM connector - implements `IMenuCompat` and `IPermissionCompat` using FPM's MenuManager and AdminManager |
| `NvmWulingCompat`            | Wuling connector - implements `IMenuCompat` / `IPermissionCompat` and provides localization via Wuling's `IStringLocalizer` |
| `NativeVoteExample`          | Example module demonstrating how to use the API                                                           |

## Dependencies

- `ModSharp.Sharp.Shared` >= 2.1.118
- `ModSharp.Sharp.Modules.AdminManager.Shared` >= 2.1.118 (NvmFPMCompat only)
- `ModSharp.Sharp.Modules.MenuManager.Shared` >= 2.1.118 (NvmFPMCompat only)
- [Wuling](https://github.com/possession-community/Wuling) framework — only for `NvmWulingCompat` (localization / menu / permission)

## Installation

Grab the latest release zip from [Releases](https://github.com/possession-community/NativeVoteManagerMS/releases/latest) (or build from source), then:

1. Deploy `NativeVoteManagerMS.dll` and `NativeVoteManagerMS.Shared.dll` to your ModSharp modules directory
2. Deploy `NvmFPMCompat.dll` (or your own compat implementation)

Translations ship as `lang/*.json` next to `NativeVoteManagerMS.dll` (`modules/NativeVoteManagerMS/lang/`) — no extra step needed. Localized chat messages require a compat module that provides a localizer: install the [Wuling](https://github.com/possession-community/Wuling) framework together with `NvmWulingCompat.dll`, which reads those `lang/*.json` files. Without a localizer compat, raw localization keys are shown instead of translated text.

## For Plugin Developers (consuming the API)

If you're writing a plugin that uses `INativeVoteManager`, add the Shared package from NuGet:

```xml
<ItemGroup>
  <PackageReference Include="NativeVoteManagerMS.Shared" Version="*" ExcludeAssets="runtime" />
</ItemGroup>
```

Or via CLI:

```bash
dotnet add package NativeVoteManagerMS.Shared
```

`ExcludeAssets="runtime"` is important — the Shared DLL is already shipped by the server-side module, so you must not bundle it with your plugin.

Check [NuGet.org](https://www.nuget.org/packages/NativeVoteManagerMS.Shared) for the latest version.

## Usage

### Getting the API

```csharp
public void OnAllModulesLoaded()
{
    var voteManager = _sharedSystem.GetSharpModuleManager()
        .GetRequiredSharpModuleInterface<INativeVoteManager>(INativeVoteManager.ModSharpModuleIdentity)
        .Instance!;
}
```

### YesNo Vote

YesNo votes use CS2's native vote UI, which only accepts **SFUI translation keys** (strings starting with `#`). The game engine performs client-side translation — the plugin's localizer is **not** applied to `Title` / `Description`.

- `Title` — SFUI key for the vote question (e.g. `#SFUI_vote_kick_player`). Defaults to `#SFUI_Vote_None`.
- `Description` — argument substituted into the Title template (`%s1%`). Leave empty for templates that take no argument.

```csharp
var options = new YesNoVoteOptions
{
    Title = "#SFUI_vote_kick_player",  // SFUI key — engine translates per client
    Description = targetPlayer.Name,   // %s1% substitution in the SFUI template
    Participants = null, // null = all players
    PassCondition = result => result.Choices[0].Voters.Count > result.Choices[1].Voters.Count,
    VoteDuration = 10.0F,
    VoteHandler = new YesNoVoteHandler(sharedSystem),
    VoteInitiator = executor.Slot
};

var result = voteManager.InitiateYesNoVote(options);
if (result != VoteInitiateResult.Success)
{
    // Handle error
}
```

#### YesNo Vote Handler

```csharp
public class YesNoVoteHandler(ISharedSystem sharedSystem) : IYesNoVoteHandler
{
    public void OnVoteInitiated() { }

    public void OnChoice(IGameClient chooser, bool isYes, YesNoVoteState state)
    {
        // Called each time a player votes
    }

    public void OnVotePassed(VoteResult result) { }
    public void OnVoteFailed(VoteResult result) { }
    public void OnVoteCancelled() { }
}
```

### MultiChoice Vote

```csharp
var voteContents = new List<VoteContent>
{
    new() { Index = 0, InternalName = "map_dust2", VisibleName = LocalizedString.From(_ => "Dust 2") },
    new() { Index = 1, InternalName = "map_mirage", VisibleName = LocalizedString.From(_ => "Mirage") },
    new() { Index = 2, InternalName = "map_inferno", VisibleName = LocalizedString.From(_ => "Inferno") },
};

var options = new MultiChoiceVoteOptions
{
    Title = LocalizedString.From(_ => "Next Map?"),
    Description = LocalizedString.From(_ => "Choose the next map"),
    Participants = null,
    VoteDuration = 15.0F,
    VoteHandler = new MultiChoiceVoteHandler(sharedSystem),
    VoteContents = voteContents,
    RandomShuffle = true
};

voteManager.InitiateMultiChoiceVote(options);
```

#### MultiChoice Vote Handler

```csharp
public class MultiChoiceVoteHandler(ISharedSystem sharedSystem) : IMultiChoiceVoteHandler
{
    public void OnVoteInitiated() { }

    public void OnChoice(IGameClient chooser, VoteContent content, MultiChoiceVoteState state)
    {
        // Called each time a player selects an option
    }

    public void OnVotePassed(VoteResult result)
    {
        // result.Winner contains the most voted VoteContent
    }

    public void OnVoteFailed(VoteResult result) { }
    public void OnVoteCancelled() { }
}
```

### Return Values

All vote operations return result enums instead of throwing exceptions:

```csharp
// Initiate
VoteInitiateResult.Success
VoteInitiateResult.VoteAlreadyInProgress
VoteInitiateResult.NoMenuCompatSet

// Cancel
VoteCancelResult.Success
VoteCancelResult.NoVoteInProgress

// End
VoteEndResult.Success
VoteEndResult.NoVoteInProgress
```

## Custom Compat Implementation

NativeVoteManagerMS uses a pluggable architecture. If you don't use FPM modules, you can implement your own compat layer.

### IMenuCompat

Required for MultiChoice votes. See [NvmFPMCompat/FpmMenuCompat.cs](NvmFPMCompat/FPMMenuCompat.cs) for a reference implementation.

```csharp
public interface IMenuCompat
{
    void OpenMenu(IGameClient target);
    void CloseMenu(IGameClient target);
    void SetVoteOptions(MultiChoiceVoteOptions options);
    void Cleanup();
    Action<IGameClient, VoteContent> OnChoice { get; set; }
}
```

### IPermissionCompat

Required for permission-gated commands (`!cancelvote`). See [NvmFPMCompat/FPMPermissionCompat.cs](NvmFPMCompat/FPMPermissionCompat.cs) for a reference implementation.

```csharp
public interface IPermissionCompat
{
    bool HasPermission(IGameClient client, string permission);
}
```

### Registering Compat

```csharp
public void OnAllModulesLoaded()
{
    var voteManager = _sharedSystem.GetSharpModuleManager()
        .GetRequiredSharpModuleInterface<INativeVoteManager>(INativeVoteManager.ModSharpModuleIdentity)
        .Instance!;

    voteManager.SetDefaultMenuCompat(new YourMenuCompat());
    voteManager.SetDefaultPermissionCompat(new YourPermissionCompat());
}
```

## Permissions

| Permission        | Description                       |
|-------------------|-----------------------------------|
| `nvm.vote.cancel` | Allow player to use `!cancelvote` |

## License

Copyright (c) 2025-2026 faketuna
