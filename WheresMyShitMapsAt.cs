using System;
using System.Collections.Generic;
using System.Linq;
using WheresMyShitMapsAt.Core;
using WheresMyShitMapsAt.Types;
using WheresMyShitMapsAt.Cache;
using WheresMyShitMapsAt.Settings;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory;
using ExileCore.Shared.Enums;
using ExileCore;
using ExileCore.Shared.Helpers;

namespace WheresMyShitMapsAt;

public sealed class WheresMyShitMapsAt : BaseSettingsPlugin<WheresMyShitMapsAtSettings>
{
    private static WheresMyShitMapsAt _instance;
    private readonly HighlightCache _highlightCache;
    private readonly MapHighlighter _highlighter;
    private NormalInventoryItem _previewItem = null;

    public static WheresMyShitMapsAt Instance { get => _instance; set => _instance = value; }

    public WheresMyShitMapsAt()
    {
        _highlightCache = new HighlightCache();
        _highlighter = new MapHighlighter();

        Instance = this;
    }

    public override bool Initialise()
    {
        _highlighter.Initialise(Graphics);

        return true;
    }

    public override Job Tick()
    {
        if (!Settings.Enable.Value)
            return null;

        var newHighlights = new Dictionary<long, MapHighlightInfo>();

        ProcessInventory(newHighlights);
        ProcessStash(newHighlights);
        ProcessShops(newHighlights); // <--- Add this line

        _highlightCache.Update(newHighlights);

        if (Settings.PreviewHotkey.PressedOnce())
        {
            var element = GameController.IngameState.UIHoverElement;
            if (element?.AsObject<Element>() is { } hoveredElement)
            {
                _previewItem = hoveredElement.AsObject<NormalInventoryItem>();
            }
        }

        return null;
    }

    public NormalInventoryItem GetPreviewItem() => _previewItem;

    private void ProcessInventory(Dictionary<long, MapHighlightInfo> highlights)
    {
        if (!Settings.FilterInventory.Value || !GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
            return;

        var inventoryItems = GameController.IngameState.IngameUi
            .InventoryPanel[InventoryIndex.PlayerInventory]
            .VisibleInventoryItems;

        ProcessItems(inventoryItems, highlights);
    }

    private void ProcessStash(Dictionary<long, MapHighlightInfo> highlights)
    {
        var stashElement = GameController.IngameState.IngameUi.StashElement;
        if (!Settings.FilterStash.Value || stashElement?.IsVisible != true)
            return;

        // 1. Clear current highlights for the stash to prevent ghosting
        // (This is handled by the 'new highlights' dict in Tick, so we just fill it)

        // 2. Start the search from the root of the Stash UI
        // This is exactly what made MapNotify work for specialized tabs
        FindMapsInElement(stashElement, highlights);
    }
    private void ProcessShops(Dictionary<long, MapHighlightInfo> highlights)
    {
        var ui = GameController.IngameState.IngameUi;

        // 1. Check for Kingsmarch / Offline Merchant (MapNotify logic)
        var merchantPanel = ui.OfflineMerchantPanel;
        if (merchantPanel != null && merchantPanel.IsVisible)
        {
            FindMapsInElement(merchantPanel, highlights);
        }

        // 2. Check for Purchase/Haggle Windows
        Element shopWindow = null;
        if (ui.PurchaseWindow?.IsVisible == true)
            shopWindow = ui.PurchaseWindow;
        else if (ui.PurchaseWindowHideout?.IsVisible == true)
            shopWindow = ui.PurchaseWindowHideout;
        else if (ui.HaggleWindow?.IsVisible == true)
            shopWindow = ui.HaggleWindow;

        if (shopWindow != null)
        {
            // We use the recursive search starting from the window root
            // This is safer than the hardcoded GetChildFromIndices(8, 1) path
            FindMapsInElement(shopWindow, highlights);
        }
    }

    private void ProcessItems(
        IEnumerable<NormalInventoryItem> items,
        Dictionary<long, MapHighlightInfo> highlights)
    {
        foreach (var item in items.Where(IsValidMap))
        {
            var mods = item.Item.GetComponent<Mods>();
            var modMatch = MapModMatcher.MatchMods(mods, Settings.Entries);

            if (modMatch.HasAnyMatch)
            {
                highlights[item.Item.Address] = new MapHighlightInfo(
                    Center: item.GetClientRectCache.Center.ToVector2Num(),
                    HasBadMod: modMatch.HasBadMod,
                    HasGoodMod: modMatch.HasGoodMod,
                    Item: item);
            }
        }
    }

    public override void Render()
    {
        if (!Settings.Enable.Value)
            return;

        if (!Settings.FilterInventory.Value && !Settings.FilterStash.Value)
            return;

        _highlighter.RenderHighlights(_highlightCache.GetCurrentHighlights());
    }

    private static bool IsValidMap(NormalInventoryItem inventoryItem)
    {
        try
        {
            return inventoryItem?.Item != null
                && inventoryItem.Item.TryGetComponent(out Mods mods)
                && mods.Identified
                && inventoryItem.Item.HasComponent<MapKey>(); // Uses the specific component
        }
        catch (Exception)
        {
            return false;
        }
    }
    private void FindMapsInElement(Element element, Dictionary<long, MapHighlightInfo> highlights)
    {
        if (element == null || !element.IsVisible) return;

        var item = element.AsObject<NormalInventoryItem>();
        if (item?.Item != null && item.Address != 0)
        {
            if (IsValidMap(item))
            {
                if (!highlights.ContainsKey(item.Item.Address))
                {
                    var mods = item.Item.GetComponent<Mods>();
                    var modMatch = MapModMatcher.MatchMods(mods, Settings.Entries);

                    if (modMatch.HasAnyMatch)
                    {
                        highlights[item.Item.Address] = new MapHighlightInfo(
                            Center: item.GetClientRectCache.Center.ToVector2Num(),
                            HasBadMod: modMatch.HasBadMod,
                            HasGoodMod: modMatch.HasGoodMod,
                            Item: item
                        );
                    }
                }
            }
            // Do NOT return here. Shop windows sometimes have complex nesting.
        }

        if (element.ChildCount > 0)
        {
            foreach (var child in element.Children)
            {
                FindMapsInElement(child, highlights);
            }
        }
    }
}
