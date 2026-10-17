using DeskKeeper.Domain.Models;

namespace DeskKeeper.Domain.Contracts;

public interface IDesktopItemSource
{
    IReadOnlyList<DesktopItem> Enumerate();
}

