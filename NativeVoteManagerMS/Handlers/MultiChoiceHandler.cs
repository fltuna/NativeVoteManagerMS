using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NativeVoteManagerMS.Shared;
using NativeVoteManagerMS.Shared.Types;
using Sharp.Shared.Objects;

namespace NativeVoteManagerMS.Handlers;

internal class MultiChoiceHandler : IVoteTypeHandler
{
    internal readonly IMenuCompat MenuCompat;
    private readonly ILogger _logger;
    private readonly MultiChoiceVoteOptions _options;
    private readonly Dictionary<VoteContent, List<IGameClient>> _votes = new();
    private readonly List<IGameClient> _participants = new();

    public MultiChoiceHandler(IMenuCompat menuCompat, ILogger logger, MultiChoiceVoteOptions options)
    {
        MenuCompat = menuCompat;
        _logger = logger;
        _options = options;
    }

    public float Duration => _options.VoteDuration;
    public Action? OnAllVoted { get; set; }

    public void Start()
    {
        _participants.AddRange(_options.Participants!);

        foreach (var content in _options.VoteContents)
        {
            _votes[content] = new List<IGameClient>();
        }

        // OnChoice/SetVoteOptions failures are fatal for the whole vote — let them
        // propagate so NativeVoteManager.StartVote can roll back and report InternalError.
        MenuCompat.OnChoice = OnPlayerChoice;
        MenuCompat.SetVoteOptions(_options);
        foreach (var pa in _participants)
        {
            ExternalCall.Run(_logger, () => MenuCompat.OpenMenu(pa));
        }
        ExternalCall.Run(_logger, _options.VoteHandler.OnVoteInitiated);
    }

    private void OnPlayerChoice(IGameClient chooser, VoteContent content)
    {
        foreach (var voters in _votes.Values)
        {
            voters.Remove(chooser);
        }

        if (_votes.TryGetValue(content, out var list))
        {
            list.Add(chooser);
        }

        ExternalCall.Run(_logger, () => MenuCompat.CloseMenu(chooser));
        ExternalCall.Run(_logger, () => _options.VoteHandler.OnChoice(chooser, content, GetState()));

        if (HaveAllParticipantsVoted())
            OnAllVoted?.Invoke();
    }


    public MultiChoiceVoteState GetState()
    {
        var choices = _votes
            .Select(kv => new VoteChoiceResult(kv.Key, kv.Value.AsReadOnly()))
            .ToList()
            .AsReadOnly();

        var votedCount = _votes.Values.Sum(v => v.Count);

        return new MultiChoiceVoteState(
            _options,
            choices,
            votedCount,
            _participants.Count
        );
    }

    public VoteResult BuildResult()
    {
        var choices = _votes
            .Select(kv => new VoteChoiceResult(kv.Key, kv.Value.AsReadOnly()))
            .ToList()
            .AsReadOnly();

        var winner = choices
            .Where(c => c.Voters.Count > 0)
            .OrderByDescending(c => c.Voters.Count)
            .FirstOrDefault()
            ?.Content;

        return new VoteResult(choices, _participants.AsReadOnly(), winner);
    }

    public bool CheckPassCondition(VoteResult result) =>
        ExternalCall.Run(_logger, () => (_options.PassCondition ?? VotePassConditions.Default())(result), false);

    public void OnVotePassed(VoteResult result) =>
        ExternalCall.Run(_logger, () => _options.VoteHandler.OnVotePassed(result));

    public void OnVoteFailed(VoteResult result) =>
        ExternalCall.Run(_logger, () => _options.VoteHandler.OnVoteFailed(result));

    public void OnVoteCancelled() =>
        ExternalCall.Run(_logger, _options.VoteHandler.OnVoteCancelled);

    public bool HaveAllParticipantsVoted()
        => _participants.Count > 0 && _votes.Values.Sum(v => v.Count) >= _participants.Count;

    public void OnParticipantDisconnected(IGameClient client)
    {
        if (!_participants.Remove(client))
            return;

        foreach (var voters in _votes.Values)
        {
            voters.Remove(client);
        }

        ExternalCall.Run(_logger, () => _options.VoteHandler.OnParticipantDisconnected(client, GetState()));
    }

    public void Close()
    {
        foreach (var participant in _participants)
        {
            ExternalCall.Run(_logger, () => MenuCompat.CloseMenu(participant));
        }
    }

    public void Cleanup()
    {
        ExternalCall.Run(_logger, () => MenuCompat.OnChoice = (_, _) => { });
        ExternalCall.Run(_logger, MenuCompat.Cleanup);
        _votes.Clear();
        _participants.Clear();
    }
}
