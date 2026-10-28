namespace InputFusion.Core.Layout;

/// <summary>Eine Kante eines Monitors. Die Werte gehen als Byte über das Netz und bleiben deshalb fest.</summary>
public enum EdgeSide : Byte
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

public static class EdgeSideExtensions
{
    /// <summary>Die gegenüberliegende Kante: Liegt der Client rechts am Host, ist es seine linke.</summary>
    public static EdgeSide Opposite(this EdgeSide side) => side switch
    {
        EdgeSide.Left => EdgeSide.Right,
        EdgeSide.Right => EdgeSide.Left,
        EdgeSide.Top => EdgeSide.Bottom,
        _ => EdgeSide.Top,
    };

    public static Boolean IsVertical(this EdgeSide side) => side is EdgeSide.Left or EdgeSide.Right;
}
