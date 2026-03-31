using ExileCore.Shared.Helpers;
using System.Collections.Generic;
using System.Drawing;
using WheresMyShitMapsAt.Types;
using Graphics = ExileCore.Graphics;

namespace WheresMyShitMapsAt.Core;
public sealed class MapHighlighter
{
    private SharpDX.Color _badModColor;
    private SharpDX.Color _goodModColor;
    private Graphics _graphics;

    public MapHighlighter()
    {
        var bad = Color.FromArgb(
            MapConstants.Alpha,
            MapConstants.Colors.Bad.Red,
            MapConstants.Colors.Bad.Green,
            MapConstants.Colors.Bad.Blue
        );
        _badModColor = bad.ToSharpDx();

        var good = Color.FromArgb(
            MapConstants.Alpha,
            MapConstants.Colors.Good.Red,
            MapConstants.Colors.Good.Green,
            MapConstants.Colors.Good.Blue
        );
        _goodModColor = good.ToSharpDx();
    }

    public void Initialise(Graphics graphics)
    {
        _graphics = graphics;
    }

    public void RenderHighlights(IReadOnlyDictionary<long, MapHighlightInfo> highlights)
    {
        foreach (var highlight in highlights.Values)
        {
            try
            {
                if (highlight.HasBadMod && highlight.HasGoodMod)
                {
                    _graphics.DrawRectFilledMultiColor(highlight.Item.GetClientRectCache.TopLeft.ToVector2Num(),
                        highlight.Item.GetClientRectCache.BottomRight.ToVector2Num(),
                        _goodModColor,
                        _badModColor,
                        _goodModColor,
                        _badModColor);
                }
                else if (highlight.HasBadMod)
                {
                    _graphics.DrawBox(highlight.Item.GetClientRectCache.TopLeft.ToVector2Num(), highlight.Item.GetClientRectCache.BottomRight.ToVector2Num(), _badModColor);
                }
                else if (highlight.HasGoodMod)
                {
                    _graphics.DrawBox(highlight.Item.GetClientRectCache.TopLeft.ToVector2Num(), highlight.Item.GetClientRectCache.BottomRight.ToVector2Num(), _goodModColor);
                }
            }
            catch { }
        }
    }
}
