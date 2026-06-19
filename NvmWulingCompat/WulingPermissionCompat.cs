using NativeVoteManagerMS.Shared;
using Sharp.Shared.Objects;
using Wuling.Abstract.Tianshi.Authority;

namespace NvmWulingCompat;

public sealed class WulingPermissionCompat(IAuthority authority) : IPermissionCompat
{
    public bool HasPermission(IGameClient client, string permission)
    {
        return authority.PlayerHasPermission(client.SteamId, permission);
    }
}
