using NativeVoteManagerMS.Shared;
using Wuling.Abstract.Tianshi.Localizer;

namespace NvmWulingCompat;

/// <summary>
/// Bridges the Wuling <see cref="IStringLocalizer"/> to the framework-agnostic
/// <see cref="INvmLocalizer"/> consumed by the NativeVoteManagerMS core.
/// </summary>
internal sealed class WulingLocalizerCompat(IStringLocalizer localizer) : INvmLocalizer
{
    public string ForPlayer(ulong steamId, string key, params object[] args)
        => localizer.ForPlayer(steamId, key, args);
}
