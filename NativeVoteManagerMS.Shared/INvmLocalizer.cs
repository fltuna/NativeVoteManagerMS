namespace NativeVoteManagerMS.Shared;

/// <summary>
/// Framework-agnostic localizer abstraction consumed by the vote manager core.
/// Implemented by compat modules (e.g. NvmWulingCompat) that bridge to an actual
/// localization backend, so the core stays independent of any specific framework.
/// </summary>
public interface INvmLocalizer
{
    /// <summary>
    /// Resolves the localized string for the given player's language.
    /// </summary>
    /// <param name="steamId">Target player's SteamID64.</param>
    /// <param name="key">Localization key.</param>
    /// <param name="args">Optional format arguments.</param>
    /// <returns>Localized (and formatted) string.</returns>
    string ForPlayer(ulong steamId, string key, params object[] args);
}
