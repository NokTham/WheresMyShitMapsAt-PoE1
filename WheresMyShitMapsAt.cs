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
    private DateTime _lastScanTime = DateTime.MinValue;

    public override Job Tick()
    {
        // 1. Core toggle check
        if (!Settings.Enable.Value)
            return null;

        if ((DateTime.Now - _lastScanTime).TotalMilliseconds < Settings.ScanInterval.Value)
            return null;

        _lastScanTime = DateTime.Now;

        var activeEntries = Settings.Entries.Where(x => x.Active).ToList();
        var newHighlights = new Dictionary<long, MapHighlightInfo>();

        // 4. Process various UI elements
        ProcessInventory(newHighlights, activeEntries);
        ProcessStash(newHighlights, activeEntries);
        ProcessShops(newHighlights, activeEntries);
        ProcessTrade(newHighlights, activeEntries);

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

    private void ProcessInventory(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> activeEntries)
    {
        if (!Settings.FilterInventory.Value || !GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
            return;

        var inventoryItems = GameController.IngameState.IngameUi
            .InventoryPanel[InventoryIndex.PlayerInventory]
            .VisibleInventoryItems;

        // Update ProcessItems to also take activeEntries or just use the logic directly
        ProcessItems(inventoryItems, highlights, activeEntries);
    }

    private void ProcessStash(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> activeEntries)
    {
        var stashElement = GameController.IngameState.IngameUi.StashElement;
        if (!Settings.FilterStash.Value || stashElement?.IsVisible != true)
            return;

        FindMapsInElement(stashElement, highlights, activeEntries); // Fixed call
    }

    private void ProcessShops(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> activeEntries)
    {
        if (!Settings.FilterShops.Value) return;

        var ui = GameController.IngameState.IngameUi;

        // Check various shop windows and pass the list
        if (ui.OfflineMerchantPanel?.IsVisible == true)
            FindMapsInElement(ui.OfflineMerchantPanel, highlights, activeEntries);

        Element shopWindow = ui.PurchaseWindow?.IsVisible == true ? ui.PurchaseWindow :
                            ui.PurchaseWindowHideout?.IsVisible == true ? ui.PurchaseWindowHideout :
                            ui.HaggleWindow?.IsVisible == true ? ui.HaggleWindow : null;

        if (shopWindow != null)
            FindMapsInElement(shopWindow, highlights, activeEntries);
    }

    private void ProcessTrade(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> activeEntries)
    {
        if (!Settings.FilterTrade.Value) return;

        var tradeWindow = GameController.IngameState.IngameUi.TradeWindow;
        if (tradeWindow != null && tradeWindow.IsVisible)
        {
            FindMapsInElement(tradeWindow, highlights, activeEntries);
        }
    }
    private void ProcessItems(
    IEnumerable<NormalInventoryItem> items,
    Dictionary<long, MapHighlightInfo> highlights,
    List<TableEntry> activeEntries)
    {
        foreach (var item in items.Where(IsValidMap))
        {
            var mods = item.Item.GetComponent<Mods>();
            // Pass the pre-filtered activeEntries here
            var modMatch = MapModMatcher.MatchMods(mods, activeEntries);

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

        // Ensure we don't exit early if ANY of the filters are active
        if (!Settings.FilterInventory.Value &&
            !Settings.FilterStash.Value &&
            !Settings.FilterShops.Value &&
            !Settings.FilterTrade.Value)
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
    private void FindMapsInElement(Element element, Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> activeEntries)
    {
        // 1. Basic visibility check
        if (element == null || !element.IsVisible || element.Address == 0) return;
        if (element.ChildCount <= 3)
        {
            var item = element.AsObject<NormalInventoryItem>();
            if (item?.Item != null && IsValidMap(item))
            {
                long addr = item.Item.Address;
                if (!highlights.ContainsKey(addr))
                {
                    var mods = item.Item.GetComponent<Mods>();
                    var modMatch = MapModMatcher.MatchMods(mods, activeEntries);

                    if (modMatch.HasAnyMatch)
                    {
                        highlights[addr] = new MapHighlightInfo(
                            Center: item.GetClientRectCache.Center.ToVector2Num(),
                            HasBadMod: modMatch.HasBadMod,
                            HasGoodMod: modMatch.HasGoodMod,
                            Item: item
                        );
                    }
                }
            }
        }
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            FindMapsInElement(children[i], highlights, activeEntries);
        }
    }
}
