using DeskKeeper.Domain.Contracts;

namespace DeskKeeper.Infrastructure.Windows;

public sealed class SystemLayoutParametersProvider : ILayoutParametersProvider
{
    public LayoutParameters Get()
    {
        var (area, cellW, cellH) = NativeMethods.GetLayoutParameters();
        return new LayoutParameters(
            area.Left,
            area.Top,
            Math.Max(1, area.Right - area.Left),
            Math.Max(1, area.Bottom - area.Top),
            cellW,
            cellH);
    }
}

