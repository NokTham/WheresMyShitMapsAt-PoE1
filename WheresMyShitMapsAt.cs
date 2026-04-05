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
using MapModType = WheresMyShitMapsAt.Settings.ModType;

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

        if (Settings.PreviewHotkey.PressedOnce())
        {
            var element = GameController.IngameState.UIHoverElement;
            if (element?.AsObject<Element>() is { } hoveredElement)
            {
                _previewItem = hoveredElement.AsObject<NormalInventoryItem>();
            }
        }

        var stash = GameController.IngameState.IngameUi.StashElement;
        var isMapStashOpen = stash != null && stash.IsVisible && stash.VisibleStash?.InvType == InventoryType.MapStash;
        var currentInterval = isMapStashOpen ? Settings.MapStashScanInterval.Value : Settings.ScanInterval.Value;

        if ((DateTime.Now - _lastScanTime).TotalMilliseconds < currentInterval)
            return null;

        _lastScanTime = DateTime.Now;

        var activeBadMods = Settings.Entries.Where(x => x.Active && x.Type == MapModType.Bad).ToList();
        var activeGoodMods = Settings.Entries.Where(x => x.Active && x.Type == MapModType.Good).ToList();

        var newHighlights = new Dictionary<long, MapHighlightInfo>();

        // 4. Process various UI elements
        ProcessInventory(newHighlights, activeBadMods, activeGoodMods);
        ProcessStash(newHighlights, activeBadMods, activeGoodMods);
        ProcessShops(newHighlights, activeBadMods, activeGoodMods);
        ProcessTrade(newHighlights, activeBadMods, activeGoodMods);

        _highlightCache.Update(newHighlights);

        return null;
    }

    public NormalInventoryItem GetPreviewItem() => _previewItem;

    private void ProcessInventory(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        if (!Settings.FilterInventory.Value || !GameController.IngameState.IngameUi.InventoryPanel.IsVisible)
            return;

        var inventoryItems = GameController.IngameState.IngameUi
            .InventoryPanel[InventoryIndex.PlayerInventory]
            .VisibleInventoryItems;

        ProcessItems(inventoryItems, highlights, badMods, goodMods);
    }

    private void ProcessStash(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        var stashElement = GameController.IngameState.IngameUi.StashElement;
        if (stashElement == null || !stashElement.IsVisible || stashElement.VisibleStash == null)
            return;

        var visibleStash = stashElement.VisibleStash;
        var isMapStash = visibleStash.InvType == InventoryType.MapStash;
        var shouldScan = isMapStash ? Settings.FilterMapStash.Value : Settings.FilterStash.Value;

        if (shouldScan)
            FindMapsInElement(stashElement, highlights, badMods, goodMods);
    }

    private void ProcessShops(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        if (!Settings.FilterShops.Value) return;

        var ui = GameController.IngameState.IngameUi;

        // Check various shop windows and pass the list
        if (ui.OfflineMerchantPanel?.IsVisible == true)
            FindMapsInElement(ui.OfflineMerchantPanel, highlights, badMods, goodMods);

        Element shopWindow = ui.PurchaseWindow?.IsVisible == true ? ui.PurchaseWindow :
                            ui.PurchaseWindowHideout?.IsVisible == true ? ui.PurchaseWindowHideout :
                            ui.HaggleWindow?.IsVisible == true ? ui.HaggleWindow : null;

        if (shopWindow != null)
            FindMapsInElement(shopWindow, highlights, badMods, goodMods);
    }

    private void ProcessTrade(Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        if (!Settings.FilterTrade.Value) return;

        var tradeWindow = GameController.IngameState.IngameUi.TradeWindow;
        if (tradeWindow != null && tradeWindow.IsVisible)
        {
            FindMapsInElement(tradeWindow, highlights, badMods, goodMods);
        }
    }
    private void ProcessItems(
    IEnumerable<NormalInventoryItem> items,
    Dictionary<long, MapHighlightInfo> highlights,
    List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        foreach (var item in items.Where(IsValidMap))
        {
            var mods = item.Item.GetComponent<Mods>();
            var modMatch = MapModMatcher.MatchMods(mods, badMods, goodMods);

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
            !Settings.FilterMapStash.Value &&
            !Settings.FilterShops.Value &&
            !Settings.FilterTrade.Value)
            return;

        _highlighter.RenderHighlights(_highlightCache.GetCurrentHighlights());
    }

    private static bool IsValidMap(NormalInventoryItem inventoryItem)
    {
        try
        {
            var item = inventoryItem?.Item;
            return item != null
                && item.TryGetComponent(out Mods mods)
                && mods.Identified
                && item.Path.StartsWith("Metadata/Items/Maps/", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private void FindMapsInElement(Element element, Dictionary<long, MapHighlightInfo> highlights, List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        // 1. Basic visibility check
        if (element == null || element.Address == 0 || !element.IsVisible) return;

        if (element.ChildCount <= 5)
        {
            var item = element.AsObject<NormalInventoryItem>();
            long addr = item?.Item?.Address ?? 0;
            if (addr != 0 && !highlights.ContainsKey(addr) && IsValidMap(item))
            {
                var mods = item.Item.GetComponent<Mods>();
                var modMatch = MapModMatcher.MatchMods(mods, badMods, goodMods);

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

        foreach (var child in element.Children)
        {
            FindMapsInElement(child, highlights, badMods, goodMods);
        }
    }
}
