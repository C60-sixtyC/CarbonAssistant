using DeskKeeper.Domain.Sorting;

namespace DeskKeeper.Infrastructure.Windows;

public sealed class NaturalStringComparer : INaturalStringComparer
{
    public int Compare(string? x, string? y)
    {
        if (x is null && y is null)
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return NativeMethods.StrCmpLogicalW(x, y);
    }
}

