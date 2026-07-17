using System;
using System.Collections.Generic;
using System.Globalization;
using Sharp.Shared.Objects;

namespace NativeVoteManagerMS.Shared.Types;

/// <summary>
/// Options for native YesNo vote
/// </summary>
public record YesNoVoteOptions
{
    /// <summary>
    /// SFUI key used as the CS2 native vote UI title (DispStr).
    /// Must be a CS2 translation key starting with '#' (e.g. '#SFUI_vote_kick_player').
    /// Client-side translation is performed by the game engine, not by this plugin's localizer.
    /// Defaults to '#SFUI_Vote_None' (no specific vote type).
    /// </summary>
    public string Title { get; init; } = "#SFUI_Vote_None";

    /// <summary>
    /// Argument passed to the CS2 native vote UI (DetailsStr) — typically substituted into the Title's SFUI template.
    /// Resolved per-participant when set. Leave null if the Title's template takes no argument.
    /// </summary>
    public LocalizedString? Description { get; init; }

    /// <summary>
    /// SFUI key used as the CS2 native vote pass UI title (DispStr in CCSUsrMsg_VotePass).
    /// Must be a CS2 translation key starting with '#'.
    /// Defaults to '#SFUI_vote_passed'.
    /// </summary>
    public string PassTitle { get; init; } = "#SFUI_vote_passed";

    /// <summary>
    /// Argument passed to the CS2 native vote pass UI (DetailsStr) — typically substituted into the PassTitle's SFUI template.
    /// Resolved per-participant when set. Leave null if the PassTitle's template takes no argument.
    /// </summary>
    public LocalizedString? PassDescription { get; init; }

    /// <summary>Duration of this vote. Set to below zero to never ends until INativeVoteManager.EndVote() called.</summary>
    public required float VoteDuration { get; init; }

    /// <summary>Handler of this vote.</summary>
    public required IYesNoVoteHandler VoteHandler { get; init; }

    /// <summary>Vote initiator slot. Defaults to 99 (server).</summary>
    public int VoteInitiator { get; init; } = 99;

    /// <summary>Condition to determine if the vote passes. If null, defaults to majority vote.</summary>
    public VotePassCondition? PassCondition { get; init; }

    /// <summary>Custom participant list. If null, all connected players will be included as a participant.</summary>
    public IReadOnlyList<IGameClient>? Participants { get; init; }

    /// <summary>
    /// Maps a participant to the culture used when resolving <see cref="Description"/> and
    /// <see cref="PassDescription"/> for that participant. When null, strings resolve with
    /// a null culture (the <see cref="LocalizedStringFunc"/>'s own default).
    /// </summary>
    public Func<IGameClient, CultureInfo?>? CultureProvider { get; init; }
}
