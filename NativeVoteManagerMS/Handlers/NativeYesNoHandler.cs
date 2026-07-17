using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NativeVoteManagerMS.Shared.Types;
using Sharp.Shared;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;

namespace NativeVoteManagerMS.Handlers;

internal class NativeYesNoHandler : IVoteTypeHandler
{
    private readonly ISharedSystem _sharedSystem;
    private readonly ILogger _logger;
    private readonly YesNoVoteOptions _options;

    private readonly List<IGameClient> _yesVoters = new();
    private readonly List<IGameClient> _noVoters = new();
    private readonly List<IGameClient> _participants = new();

    private static readonly VoteContent YesContent = new() { Index = 0, InternalName = "yes", VisibleName = LocalizedString.From(_ =>"Yes") };
    private static readonly VoteContent NoContent = new() { Index = 1, InternalName = "no", VisibleName = LocalizedString.From(_ =>"No") };

    public NativeYesNoHandler(ISharedSystem sharedSystem, ILogger logger, YesNoVoteOptions options)
    {
        _sharedSystem = sharedSystem;
        _logger = logger;
        _options = options;
    }

    public float Duration => _options.VoteDuration;
    public Action? OnAllVoted { get; set; }

    public void Start()
    {
        _participants.AddRange(_options.Participants!);

        RefreshVotes();

        foreach (var participant in _participants)
        {
            var description = _options.Description?.Resolve(_options.CultureProvider?.Invoke(participant)) ?? "";
            SendVoteStartUm(participant, _options.Title, description);
        }

        _options.VoteHandler.OnVoteInitiated();
    }

    public void OnVoteCast(IGameClient chooser, bool isYes)
    {
        if (isYes && _yesVoters.Contains(chooser))
            return;
        if (!isYes && _noVoters.Contains(chooser))
            return;

        _yesVoters.Remove(chooser);
        _noVoters.Remove(chooser);

        if (isYes)
            _yesVoters.Add(chooser);
        else
            _noVoters.Add(chooser);

        var voteCastEvent = _sharedSystem.GetEventManager().CreateEvent("vote_cast", true);
        if (voteCastEvent != null)
        {
            voteCastEvent.SetInt("vote_option", isYes ? 0 : 1);
            voteCastEvent.SetInt("team", -1);
            voteCastEvent.SetPlayer("userid", chooser.GetPlayerController()!);
            voteCastEvent.Fire(false);
        }

        RefreshVotes();
        _options.VoteHandler.OnChoice(chooser, isYes, GetState());

        if (HaveAllParticipantsVoted())
            OnAllVoted?.Invoke();
    }

    public bool HaveAllParticipantsVoted()
        => _participants.Count > 0 && _yesVoters.Count + _noVoters.Count >= _participants.Count;

    public YesNoVoteState GetState()
    {
        return new YesNoVoteState(
            _options,
            _yesVoters.Count,
            _noVoters.Count,
            _yesVoters.Count + _noVoters.Count,
            _participants.Count
        );
    }

    public VoteResult BuildResult()
    {
        var choices = new List<VoteChoiceResult>
        {
            new(YesContent, _yesVoters.AsReadOnly()),
            new(NoContent, _noVoters.AsReadOnly())
        }.AsReadOnly();

        return new VoteResult(choices, _participants.AsReadOnly(), YesContent);
    }

    public bool CheckPassCondition(VoteResult result) =>
        (_options.PassCondition ?? VotePassConditions.Default())(result);

    public void OnVotePassed(VoteResult result)
    {
        foreach (var participant in _participants)
        {
            var passDescription = _options.PassDescription?.Resolve(_options.CultureProvider?.Invoke(participant)) ?? "";
            SendVotePassedUm(participant, _options.PassTitle, passDescription);
        }

        _options.VoteHandler.OnVotePassed(result);
    }

    public void OnVoteFailed(VoteResult result)
    {
        foreach (var participant in _participants)
            SendVoteFailedUm(participant);

        _options.VoteHandler.OnVoteFailed(result);
    }

    public void OnVoteCancelled()
    {
        foreach (var participant in _participants)
            SendVoteFailedUm(participant, NativeYesNoEndReason.NoReason);

        _options.VoteHandler.OnVoteCancelled();
    }

    public void OnParticipantDisconnected(IGameClient client)
    {
        if (!_participants.Remove(client))
            return;

        _yesVoters.Remove(client);
        _noVoters.Remove(client);

        RefreshVotes();
        _options.VoteHandler.OnParticipantDisconnected(client, GetState());
    }

    public void Close()
    {
    }

    public void Cleanup()
    {
        _yesVoters.Clear();
        _noVoters.Clear();
        _participants.Clear();
    }

    private void RefreshVotes()
    {
        var @event = _sharedSystem.GetEventManager().CreateEvent("vote_changed", true);
        if (@event is null) return;

        @event.SetInt("vote_option1", _yesVoters.Count);
        @event.SetInt("vote_option2", _noVoters.Count);
        @event.SetInt("vote_option3", 0);
        @event.SetInt("vote_option4", 0);
        @event.SetInt("vote_option5", 0);
        @event.SetInt("potentialVotes", _participants.Count);

        @event.Fire(false);
    }

    private void SendVoteStartUm(IGameClient target, string dispStrOverride = "", string detailStrOverride = "")
    {
        var start = new CCSUsrMsg_VoteStart
        {
            Team = -1,
            PlayerSlot = _options.VoteInitiator,
            VoteType = 2,
            DispStr = "#SFUI_Vote_None",
            DetailsStr = "",
            OtherTeamStr = "#SFUI_otherteam_vote_unimplemented",
            IsYesNoVote = true
        };

        if (dispStrOverride != "")
            start.DispStr = dispStrOverride;

        if (detailStrOverride != "")
            start.DetailsStr = detailStrOverride;

        _sharedSystem.GetModSharp().SendNetMessage(new RecipientFilter(target.GetPlayerController()!), start);
    }

    private void SendVoteFailedUm(IGameClient target, NativeYesNoEndReason reason = NativeYesNoEndReason.NotEnoughPlayersVoted)
    {
        var failed = new CCSUsrMsg_VoteFailed
        {
            Reason = (int)reason,
            Team = -1,
        };
        _sharedSystem.GetModSharp().SendNetMessage(new RecipientFilter(target.GetPlayerController()!), failed);
    }

    private void SendVotePassedUm(IGameClient target, string dispStrOverride = "", string detailStrOverride = "")
    {
        var passed = new CCSUsrMsg_VotePass
        {
            Team = -1,
            VoteType = 2,
            DispStr = "#SFUI_vote_passed",
            DetailsStr = ""
        };

        if (dispStrOverride != "")
            passed.DispStr = dispStrOverride;

        if (detailStrOverride != "")
            passed.DetailsStr = detailStrOverride;

        _sharedSystem.GetModSharp().SendNetMessage(new RecipientFilter(target.GetPlayerController()!), passed);
    }
}
