using System;
using Microsoft.Extensions.Logging;
using NativeVoteManagerMS.Handlers;
using NativeVoteManagerMS.Shared;
using NativeVoteManagerMS.Shared.Types;
using Sharp.Shared;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.Listeners;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;

namespace NativeVoteManagerMS;

public class NativeVoteManager(ISharedSystem sharedSystem, ILogger logger, string moduleDirectory) : INativeVoteManager, IGameListener, IClientListener
{
    private IMenuCompat? _defaultMenuCompat;
    private IPermissionCompat? _defaultPermissionCompat;
    private INvmLocalizer? _localizer;
    private IVoteTypeHandler? _activeHandler;
    private Guid? _voteTimerId;

    public void SetDefaultMenuCompat(IMenuCompat menuCompat)
    {
        _defaultMenuCompat = menuCompat;
        logger.LogInformation($"Default menu compat has been set by {menuCompat.GetType().Assembly.GetName().FullName}");
    }

    public void SetDefaultPermissionCompat(IPermissionCompat permissionCompat)
    {
        _defaultPermissionCompat = permissionCompat;
        logger.LogInformation($"Default permission compat has been set by {permissionCompat.GetType().Assembly.GetName().FullName}");
    }

    public string ModuleDirectory => moduleDirectory;

    public void SetLocalizer(INvmLocalizer localizer)
    {
        _localizer = localizer;
    }

    private string Localize(IGameClient client, string key, params ReadOnlySpan<object?> args)
    {
        if (_localizer is null)
            return args.Length > 0 ? string.Format(key, args.ToArray()) : key;

        return _localizer.ForPlayer(client.SteamId, key, args.ToArray()!);
    }

    private string LocalizeWithPrefix(IGameClient client, string key, params ReadOnlySpan<object?> args)
    {
        var prefix = Localize(client, "Nvm.Chat.Prefix");
        var message = Localize(client, key, args);
        return $"{prefix} {message}";
    }

    public VoteInitiateResult InitiateYesNoVote(YesNoVoteOptions options)
    {
        if (_activeHandler is not null)
            return VoteInitiateResult.VoteAlreadyInProgress;

        if (options.Participants is null)
        {
            options = options with
            {
                Participants = sharedSystem.GetModSharp().GetIServer().GetGameClients(true, true)
            };
        }

        var handler = new NativeYesNoHandler(sharedSystem, logger, options);
        StartVote(handler);
        return VoteInitiateResult.Success;
    }

    public VoteInitiateResult InitiateMultiChoiceVote(MultiChoiceVoteOptions options)
    {
        if (_defaultMenuCompat is null)
            return VoteInitiateResult.NoMenuCompatSet;

        return InitiateMultiChoiceVote(options, _defaultMenuCompat);
    }

    public VoteInitiateResult InitiateMultiChoiceVote(MultiChoiceVoteOptions options, IMenuCompat customMenuCompat)
    {
        if (_activeHandler is not null)
            return VoteInitiateResult.VoteAlreadyInProgress;

        if (options.Participants is null)
        {
            options = options with
            {
                Participants = sharedSystem.GetModSharp().GetIServer().GetGameClients(true, true)
            };
        }

        var handler = new MultiChoiceHandler(customMenuCompat, options);
        StartVote(handler);
        return VoteInitiateResult.Success;
    }

    private void StartVote(IVoteTypeHandler handler)
    {
        _activeHandler = handler;
        handler.OnAllVoted = () => EndVote();
        handler.Start();

        if (handler.Duration > 0)
        {
            _voteTimerId = sharedSystem.GetModSharp().PushTimer(() => EndVote(), handler.Duration);
        }
    }

    public bool IsAnyVoteInProgress => _activeHandler is not null;

    public YesNoVoteState? GetYesNoVoteState() =>
        (_activeHandler as NativeYesNoHandler)?.GetState();

    public MultiChoiceVoteState? GetMultiChoiceVoteState() =>
        (_activeHandler as MultiChoiceHandler)?.GetState();

    public VoteEndResult EndVote()
    {
        if (_activeHandler is null) return VoteEndResult.NoVoteInProgress;

        StopTimer();

        var result = _activeHandler.BuildResult();
        var passed = _activeHandler.CheckPassCondition(result);

        if (passed)
        {
            _activeHandler.OnVotePassed(result);
        }
        else
        {
            _activeHandler.OnVoteFailed(result);
        }

        Cleanup();
        return VoteEndResult.Success;
    }

    public VoteCancelResult CancelVote()
    {
        if (_activeHandler is null) return VoteCancelResult.NoVoteInProgress;

        StopTimer();
        _activeHandler.OnVoteCancelled();
        Cleanup();
        return VoteCancelResult.Success;
    }

    public ECommandAction OnRevoteCommand(IGameClient client, StringCommand command)
    {
        if (_activeHandler is not MultiChoiceHandler handler)
        {
            client.Print(HudPrintChannel.Chat, LocalizeWithPrefix(client, "Nvm.Command.NoMultiChoiceVote"));
            return ECommandAction.Handled;
        }

        handler.MenuCompat.OpenMenu(client);
        return ECommandAction.Handled;
    }

    public ECommandAction OnCancelVoteCommand(IGameClient client, StringCommand command)
    {
        if (_defaultPermissionCompat is not null && !_defaultPermissionCompat.HasPermission(client, "nvm.vote.cancel"))
        {
            client.Print(HudPrintChannel.Chat, LocalizeWithPrefix(client, "Nvm.Command.NotEnoughPermission"));
            return ECommandAction.Handled;
        }

        if (_activeHandler is null)
        {
            client.Print(HudPrintChannel.Chat, LocalizeWithPrefix(client, "Nvm.Command.NoVoteInProgress"));
            return ECommandAction.Handled;
        }

        CancelVote();
        foreach (var target in sharedSystem.GetModSharp().GetIServer().GetGameClients(true, true))
        {
            target.Print(HudPrintChannel.Chat, LocalizeWithPrefix(target, "Nvm.Broadcast.Vote.Cancelled", client.Name));
        }
        return ECommandAction.Handled;
    }

    public ECommandAction OnVoteCommand(IGameClient client, StringCommand command)
    {
        if (_activeHandler is not NativeYesNoHandler handler)
            return ECommandAction.Handled;

        var arg = command.GetArg(1);

        bool isYes;
        if (arg is "option1" or "yes")
            isYes = true;
        else if (arg is "option2" or "no")
            isYes = false;
        else
            return ECommandAction.Handled;

        handler.OnVoteCast(client, isYes);
        return ECommandAction.Handled;
    }

    public void OnGameDeactivate()
    {
        if (_activeHandler is null) return;
        StopTimer();
        Cleanup();
    }

    void IClientListener.OnClientDisconnecting(IGameClient client, NetworkDisconnectionReason reason)
    {
        _activeHandler?.OnParticipantDisconnected(client);
    }

    private void StopTimer()
    {
        if (_voteTimerId is { } timerId)
        {
            sharedSystem.GetModSharp().StopTimer(timerId);
            _voteTimerId = null;
        }
    }

    private void Cleanup()
    {
        _activeHandler?.Close();
        _activeHandler?.Cleanup();
        _activeHandler = null;
    }

    int IGameListener.ListenerVersion => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    int IClientListener.ListenerVersion => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;
}
