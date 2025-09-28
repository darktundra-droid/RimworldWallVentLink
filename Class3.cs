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

        [HarmonyPatch(typeof(MapInterface), "MapInterfaceUpdate")]
        static class MapInterfaceUpdate_DrawCustom
        {
            static void Postfix()
            {
                foreach (Map map in Find.Maps)
                    map.GetComponent<WallVentLinkMapComponent>()?.DrawAllCustom();
            }
        }

        private static void RegisterWallOrVent(Building building)
        {
            if (building?.Map == null) return;
            var comp = building.Map.GetComponent<WallVentLinkMapComponent>();
            if (comp == null) return;

            if (building.def.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false)
                comp.RegisterWall(building);

            if (WallVentLinkMapComponent.IsVentOrCooler(building))
                comp.RegisterVent(building);
        }

        private static void UnregisterWallOrVent(Thing thing)
        {
            if (thing?.Map == null) return;
            var comp = thing.Map.GetComponent<WallVentLinkMapComponent>();
            if (comp == null) return;

            if (thing.def.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false ||
                (!string.IsNullOrEmpty(thing.def.graphicData?.texPath) && thing.def.graphicData.texPath.IndexOf("RockSmooth", StringComparison.OrdinalIgnoreCase) >= 0))
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
                RegisterWallOrVent(__instance);

                // Explicitly register smoothed walls
                if (!string.IsNullOrEmpty(__instance.def?.graphicData?.texPath) &&
                    __instance.def.graphicData.texPath.IndexOf("RockSmooth_Atlas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(__instance.def?.defName) &&
                    __instance.def.defName.IndexOf("Smoothed", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    map.GetComponent<WallVentLinkMapComponent>()?.RegisterWall(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
        [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
        public static class Thing_Unregister
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
                if (__result) return;
                if (parent?.Map == null) return;

                IntVec3 offset = c - parent.Position;
                if (Math.Abs(offset.x) + Math.Abs(offset.z) != 1) return; // cardinal only

                bool parentIsWallLike = parent.def.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false;
                bool parentIsSmoothedWall = !string.IsNullOrEmpty(parent.def?.defName) &&
                    parent.def.defName.IndexOf("Smoothed", StringComparison.OrdinalIgnoreCase) >= 0;

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
        }
    }
}
