using HarmonyLib;
using RimWorld;
using Verse;
using System;

namespace WallVentLink
{
    [StaticConstructorOnStartup]
    public static class WallVentLinkHarmonyPatches
    {
        static WallVentLinkHarmonyPatches()
        {
            var harmony = new Harmony("com.wallventlink.rimworld");
            harmony.PatchAll();
            Log.Message("[WallVentLink] Harmony patches applied");
        }

        // Keep the MapInterface update wrapper robust: don't let exceptions here stop other mods
        [HarmonyPatch(typeof(MapInterface), "MapInterfaceUpdate")]
        static class MapInterfaceUpdate_DrawCustom
        {
            static void Postfix()
            {
                try
                {
                    foreach (Map map in Find.Maps)
                    {
                        map.GetComponent<WallVentLinkMapComponent>()?.DrawAllCustom();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[WallVentLink] Exception in MapInterfaceUpdate_DrawCustom postfix: {ex}");
                }
            }
        }

        // Helper to detect smoothed/rock-smooth walls (kept local and explicit)
        private static bool IsSmoothedWall(Thing t)
        {
            if (t == null) return false;
            string tex = t.def?.graphicData?.texPath ?? "";
            string defName = t.def?.defName ?? "";
            if (!string.IsNullOrEmpty(tex) && tex.IndexOf("RockSmooth_Atlas", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(defName) && defName.IndexOf("Smoothed", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void RegisterWallOrVent(Building building)
        {
            if (building?.Map == null) return;
            var comp = building.Map.GetComponent<WallVentLinkMapComponent>();
            if (comp == null) return;

            // Register anything that links as a wall
            if (building.def.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false)
                comp.RegisterWall(building);

            // Register vents/coolers
            if (WallVentLinkMapComponent.IsVentOrCooler(building))
                comp.RegisterVent(building);

            // Explicitly register smoothed walls (some defs do not set link flags)
            if (IsSmoothedWall(building))
                comp.RegisterWall(building);
        }

        private static void UnregisterWallOrVent(Thing thing)
        {
            if (thing?.Map == null) return;
            var comp = thing.Map.GetComponent<WallVentLinkMapComponent>();
            if (comp == null) return;

            bool isWallLikeFlag = thing.def.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false;
            bool isSmoothed = IsSmoothedWall(thing);

            if (isWallLikeFlag || isSmoothed)
            {
                comp.UnregisterWall(thing);
            }

            if (WallVentLinkMapComponent.IsVentOrCooler(thing))
                comp.UnregisterVent(thing);
        }

        [HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
        public static class Building_SpawnSetup_Register
        {
            public static void Postfix(Building __instance, Map map, bool respawningAfterLoad)
            {
                // Register newly spawned building as wall/vent as appropriate
                RegisterWallOrVent(__instance);
            }
        }

        // Two explicit patches: DeSpawn and Destroy. Keep them separate and safe.
        [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
        public static class Thing_DeSpawn_Unregister
        {
            public static void Prefix(Thing __instance)
            {
                UnregisterWallOrVent(__instance);
            }
        }

        [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
        public static class Thing_Destroy_Unregister
        {
            public static void Prefix(Thing __instance)
            {
                UnregisterWallOrVent(__instance);
            }
        }

        [HarmonyPatch(typeof(Graphic_Linked), "ShouldLinkWith")]
        public static class Graphic_Linked_ShouldLinkWith_Patch
        {
            static void Postfix(IntVec3 c, Thing parent, ref bool __result)
            {
                try
                {
                    if (__result) return;
                    if (parent?.Map == null) return;

                    IntVec3 offset = c - parent.Position;
                    if (Math.Abs(offset.x) + Math.Abs(offset.z) != 1) return; // cardinal only

                    bool parentIsWallLike = parent.def.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false;
                    bool parentIsSmoothedWall = IsSmoothedWall(parent);

                    if (!(parentIsWallLike || parentIsSmoothedWall)) return;

                    var thingsAtCell = parent.Map.thingGrid.ThingsListAtFast(c);
                    foreach (var t in thingsAtCell)
                    {
                        if (WallVentLinkMapComponent.IsVentOrCooler(t))
                        {
                            __result = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[WallVentLink] Exception in Graphic_Linked_ShouldLinkWith_Patch postfix: {ex}");
                }
            }
        }
    }
}