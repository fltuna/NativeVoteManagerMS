using System;
using System.Collections.Generic;
using System.Linq;
using NativeVoteManagerMS.Shared;
using NativeVoteManagerMS.Shared.Types;
using Sharp.Shared.Objects;
using Wuling.Abstract.Tianshi.Menu;
using Wuling.Abstract.Tianshi.Registry;

namespace NvmWulingCompat;

public sealed class WulingMenuCompat(IMenu menu, IRegistry registry) : IMenuCompat
{
    private MultiChoiceVoteOptions _voteOptions = null!;
    private readonly Dictionary<int, IMenuInstance> _menuCaches = new();

    public void OpenMenu(IGameClient target)
    {
        var player = registry.GetPlayer(target);
        if (player is null)
            return;

        if (_menuCaches.TryGetValue(target.Slot, out var existing) && !existing.IsClosed)
        {
            if (menu.GetActiveMenu(player) == existing)
                return;

            existing.DisplayToPlayer(player);
            return;
        }

        var instance = menu.CreateMenu();
        instance.Title = _voteOptions.Title.Resolve();

        var contents = _voteOptions.RandomShuffle
            ? _voteOptions.VoteContents.Shuffle()
            : _voteOptions.VoteContents;

        foreach (var content in contents)
        {
            instance.AddItem(MenuItemStyleFlags.Active | MenuItemStyleFlags.HasNumber, content.VisibleName.Resolve(),
                (_, _, _, _) =>
                {
                    OnChoice(target, content);
                });
        }

        _menuCaches[target.Slot] = instance;
        instance.DisplayToPlayer(player);
    }

    public void CloseMenu(IGameClient target)
    {
        if (!_menuCaches.TryGetValue(target.Slot, out var instance))
            return;

        if (!instance.IsClosed)
            instance.Close();

        _menuCaches.Remove(target.Slot);
    }

    public void SetVoteOptions(MultiChoiceVoteOptions options)
    {
        _voteOptions = options;
    }

    public void Cleanup()
    {
        foreach (var instance in _menuCaches.Values)
        {
            if (!instance.IsClosed)
                instance.Close();
        }

        _menuCaches.Clear();
    }

    public Action<IGameClient, VoteContent> OnChoice { get; set; } = null!;
}
