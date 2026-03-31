using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using WheresMyShitMapsAt.Settings;

namespace WheresMyShitMapsAt.Core;

public sealed class MapModMatcher
{
    public readonly record struct ModMatchResult(bool HasBadMod, bool HasGoodMod)
    {
        public bool HasAnyMatch => HasBadMod || HasGoodMod;
    }

    public static ModMatchResult MatchMods(Mods mods, List<TableEntry> badMods, List<TableEntry> goodMods)
    {
        var explicitMods = mods.ExplicitMods;

        return new ModMatchResult(
            HasBadMod: HasMatchingMod(explicitMods, badMods),
            HasGoodMod: HasMatchingMod(explicitMods, goodMods)
        );
    }

    private static bool HasMatchingMod(
        List<ItemMod> explicitMods,
        List<TableEntry> targetMods)
    {
        if (explicitMods.Count == 0 || targetMods.Count == 0)
            return false;

        foreach (var entry in targetMods)
        {
            foreach (var mod in explicitMods)
            {
                if (mod.Name.Contains(entry.Name, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
