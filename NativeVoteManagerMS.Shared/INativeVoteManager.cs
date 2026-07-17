using NativeVoteManagerMS.Shared.Types;

namespace NativeVoteManagerMS.Shared;

public interface INativeVoteManager
{
    const string ModSharpModuleIdentity = "NativeVoteManagerMS.Shared.INativeVoteManager";

    VoteInitiateResult InitiateYesNoVote(YesNoVoteOptions options);

    VoteInitiateResult InitiateMultiChoiceVote(MultiChoiceVoteOptions options);
    VoteInitiateResult InitiateMultiChoiceVote(MultiChoiceVoteOptions options, IMenuCompat customMenuCompat);

    VoteCancelResult CancelVote();
    VoteEndResult EndVote();

    bool IsAnyVoteInProgress { get; }
    YesNoVoteState? GetYesNoVoteState();
    MultiChoiceVoteState? GetMultiChoiceVoteState();

    void SetDefaultMenuCompat(IMenuCompat menuCompat);
    void SetDefaultPermissionCompat(IPermissionCompat permissionCompat);

    /// <summary>
    /// Absolute directory this module was loaded from. Used by compat modules to locate
    /// the bundled <c>lang/</c> translation files.
    /// </summary>
    string ModuleDirectory { get; }

    /// <summary>
    /// Injects the localizer used for player-facing messages. Provided by a compat module
    /// (e.g. NvmWulingCompat). When no localizer is set, raw keys are shown.
    /// </summary>
    void SetLocalizer(INvmLocalizer localizer);
}
