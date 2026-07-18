namespace NativeVoteManagerMS.Shared.Types;

public enum VoteInitiateResult
{
    Success,
    VoteAlreadyInProgress,
    NoMenuCompatSet,

    /// <summary>Vote setup failed (e.g. an external handler or menu compat threw during start). The vote was rolled back.</summary>
    InternalError,
}
