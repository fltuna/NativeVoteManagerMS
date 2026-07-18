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
        var argsArray = args.ToArray();

        if (_localizer is null)
        {
            try
            {
                return argsArray.Length > 0 ? string.Format(key, argsArray) : key;
            }
            catch (FormatException)
            {
                return key;
            }
        }

        var localizer = _localizer;
        return ExternalCall.Run(logger, () => localizer.ForPlayer(client.SteamId, key, argsArray!), key);
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
        return StartVote(handler) ? VoteInitiateResult.Success : VoteInitiateResult.InternalError;
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

        var handler = new MultiChoiceHandler(customMenuCompat, logger, options);
        return StartVote(handler) ? VoteInitiateResult.Success : VoteInitiateResult.InternalError;
    }

    private bool StartVote(IVoteTypeHandler handler)
    {
        _activeHandler = handler;
        handler.OnAllVoted = () => EndVote();

        try
        {
            handler.Start();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Vote start failed, rolling back");
            Cleanup();
            return false;
        }

        if (handler.Duration > 0)
        {
            _voteTimerId = sharedSystem.GetModSharp().PushTimer(() => EndVote(), handler.Duration);
        }

        return true;
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

        // EndVote also runs inside timer/command callbacks, so nothing may escape here,
        // and Cleanup must run even if a handler misbehaves.
        try
        {
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
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception while ending vote");
        }
        finally
        {
            Cleanup();
        }

        return VoteEndResult.Success;
    }

    public VoteCancelResult CancelVote()
    {
        if (_activeHandler is null) return VoteCancelResult.NoVoteInProgress;

        StopTimer();

        try
        {
            _activeHandler.OnVoteCancelled();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception while cancelling vote");
        }
        finally
        {
            Cleanup();
        }

        return VoteCancelResult.Success;
    }

    public ECommandAction OnRevoteCommand(IGameClient client, StringCommand command)
    {
        if (_activeHandler is not MultiChoiceHandler handler)
        {
            client.Print(HudPrintChannel.Chat, LocalizeWithPrefix(client, "Nvm.Command.NoMultiChoiceVote"));
            return ECommandAction.Handled;
        }

        ExternalCall.Run(logger, () => handler.MenuCompat.OpenMenu(client));
        return ECommandAction.Handled;
    }

    public ECommandAction OnCancelVoteCommand(IGameClient client, StringCommand command)
    {
        // On compat failure, fail closed: treat as no permission.
        if (_defaultPermissionCompat is { } permissionCompat
            && !ExternalCall.Run(logger, () => permissionCompat.HasPermission(client, "nvm.vote.cancel"), false))
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
        var handler = _activeHandler;
        _activeHandler = null;
        if (handler is null) return;

        try
        {
            handler.Close();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception while closing vote handler");
        }

        try
        {
            handler.Cleanup();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception while cleaning up vote handler");
        }
    }

    int IGameListener.ListenerVersion => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    int IClientListener.ListenerVersion => IClientListener.ApiVersion;
    int IClientListener.ListenerPriority => 0;
}
