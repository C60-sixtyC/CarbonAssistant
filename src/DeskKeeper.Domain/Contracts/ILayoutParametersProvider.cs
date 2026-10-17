namespace DeskKeeper.Domain.Contracts;

public sealed record LayoutParameters(
    double WorkAreaLeft,
    double WorkAreaTop,
    double WorkAreaWidth,
    double WorkAreaHeight,
    double CellWidth,
    double CellHeight);

public interface ILayoutParametersProvider
{
    LayoutParameters Get();
}

