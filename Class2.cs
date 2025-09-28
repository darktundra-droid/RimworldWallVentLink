using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RimWorld;
using Verse;

namespace WallVentLink
{
    public class WallVentLinkMapComponent : MapComponent
    {
        private static Mesh trapezoidMesh;

        private readonly HashSet<Thing> trackedWalls = new HashSet<Thing>();
        private readonly HashSet<Thing> trackedVents = new HashSet<Thing>();

        private readonly Dictionary<string, Material> trapezoidMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<int, Material> materialCache = new Dictionary<int, Material>();

        private Material outlineMaterial;
        private Mesh edgeQuadMesh;

        private const float BaseVentYOffset = 0.02f;
        private const float VentYOffsetIncrement = 0.001f;
        private const float TrapezoidYOffset = 0.03f;
        private const float OutlineYIncrement = 0.0015f;

        private const int VentRenderQueue = 2450;
        private const int TrapezoidRenderQueue = 2460;
        private const int OutlineRenderQueue = 2470;

        public WallVentLinkMapComponent(Map map) : base(map) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RefreshWallsFromMap();
            RefreshVentsFromMap();
        }

        #region registration
        public void RegisterWall(Thing wall) { if (wall != null) trackedWalls.Add(wall); }
        public void UnregisterWall(Thing wall) { if (wall != null) trackedWalls.Remove(wall); }
        public void RegisterVent(Thing vent) { if (vent != null) trackedVents.Add(vent); }
        public void UnregisterVent(Thing vent) { if (vent != null) trackedVents.Remove(vent); }
        #endregion

        public void DrawAllCustom()
        {
            if (Find.CurrentMap != map) return;

            if (trapezoidMesh == null)
                trapezoidMesh = GenerateTrapezoidMesh();

            if (edgeQuadMesh == null)
                edgeQuadMesh = new Mesh();

            if (trackedVents.Count == 0)
                RefreshVentsFromMap();
            if (trackedWalls.Count == 0)
                RefreshWallsFromMap();

            var ventsByX = trackedVents.OfType<Building>()
                .Where(b => b.Rotation == Rot4.East || b.Rotation == Rot4.West)
                .GroupBy(b => b.Position.x)
                .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Position.z).ToList());

            foreach (var column in ventsByX.Values)
            {
                for (int i = 0; i < column.Count; i++)
                {
                    var vent = column[i];
                    float yOffset = BaseVentYOffset + i * VentYOffsetIncrement;
                    DrawScaledVentOrCooler(vent, 0.125f, yOffset);
                }
            }

            foreach (var wall in trackedWalls.ToList())
            {
                if (!IsWallLike(wall))
                {
                    trackedWalls.Remove(wall);
                    continue;
                }
                DrawTrapezoidAndOutline(wall);
            }
        }

        private void RefreshWallsFromMap()
        {
            trackedWalls.Clear();
            foreach (var t in map.listerThings.AllThings)
                if (IsWallLike(t))
                    trackedWalls.Add(t);
        }

        private void RefreshVentsFromMap()
        {
            trackedVents.Clear();
            foreach (var t in map.listerThings.AllThings)
                if (IsVentOrCooler(t))
                    trackedVents.Add(t);
        }

        #region vents/coolers
        private void DrawScaledVentOrCooler(Building b, float northOffset, float yOffset)
        {
            if (b == null || b.Map != map) return;

            Material ventMat = b.Graphic?.MatAt(b.Rotation, b);
            if (ventMat == null) return;

            Material mat = GetOrCreateCachedMaterial(ventMat, VentRenderQueue);

            Vector3 pos = b.TrueCenter();
            pos.y = Altitudes.AltitudeFor(AltitudeLayer.Building) + yOffset;
            pos.z += northOffset;

            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(1f, 1f, 1.25f));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }
        #endregion

        #region trapezoid + outline
        private void DrawTrapezoidAndOutline(Thing wall)
        {
            if (wall == null || wall.Map != map) return;

            IntVec3 northCell = wall.Position + IntVec3.North;
            Thing northBuilding = northCell.InBounds(map)
                ? map.thingGrid.ThingsListAtFast(northCell)
                    .FirstOrDefault(t => t?.def?.category == ThingCategory.Building)
                : null;

            bool northHasVent = northBuilding != null && IsVentOrCooler(northBuilding);
            bool shouldDrawTrapezoid = northBuilding != null && (IsWallLike(northBuilding) || northHasVent);
            if (!shouldDrawTrapezoid) return;

            Vector3 trapezoidPos = wall.TrueCenter();
            trapezoidPos.y = Altitudes.AltitudeFor(AltitudeLayer.Building) + TrapezoidYOffset;
            trapezoidPos.z += 0.5f + 0.225f * 0.5f;

            Material trapezoidMat = GetTrapezoidMaterialForWall(wall);
            Graphics.DrawMesh(trapezoidMesh, trapezoidPos, Quaternion.identity, trapezoidMat, 0);

            if (northHasVent)
            {
                Material oMat = GetOrCreateOutlineMaterial();
                Vector3 outlinePos = trapezoidPos;
                outlinePos.y += OutlineYIncrement;
                DrawOutlineQuads(outlinePos, oMat);
            }
        }

        private Material GetTrapezoidMaterialForWall(Thing wall)
        {
            if (wall == null) return BaseContent.BadMat;

            string key = wall.def.defName;
            if (wall.Stuff != null) key += "_" + wall.Stuff.defName;

            if (!trapezoidMaterials.TryGetValue(key, out Material mat))
            {
                Material baseMat = GetWallMaterial(wall) ?? BaseContent.BadMat;
                mat = GetOrCreateCachedMaterial(baseMat, TrapezoidRenderQueue);
                trapezoidMaterials[key] = mat;
            }
            return mat;
        }

        private void DrawOutlineQuads(Vector3 center, Material outlineMat)
        {
            float thickness = 3f / 64f;
            float southWidth = 1f;
            float northWidth = 0.55f;
            float halfDepth = 0.225f * 0.5f;

            Vector3 ss = new Vector3(-southWidth * 0.5f, 0f, -halfDepth);
            Vector3 sr = new Vector3(southWidth * 0.5f, 0f, -halfDepth);
            Vector3 ns = new Vector3(-northWidth * 0.5f, 0f, halfDepth);
            Vector3 nr = new Vector3(northWidth * 0.5f, 0f, halfDepth);

            DrawEdgeQuad(ns, nr, center, thickness, outlineMat, OutlineRenderQueue); // north
            //DrawEdgeQuad(ss, sr, center, thickness, outlineMat, OutlineRenderQueue); // south
            DrawEdgeQuad(ss, ns, center, thickness, outlineMat, OutlineRenderQueue); // west
            DrawEdgeQuad(sr, nr, center, thickness, outlineMat, OutlineRenderQueue); // east
        }

        // Draw a flat (face-up) quad aligned with an edge of the trapezoid.
        private void DrawEdgeQuad(
    Vector3 aLocal, Vector3 bLocal, Vector3 worldCenter,
    float thickness, Material baseMat, int renderQueue)
        {
            // World positions
            Vector3 aWorld = worldCenter + aLocal;
            Vector3 bWorld = worldCenter + bLocal;

            // Midpoint
            Vector3 midPoint = (aWorld + bWorld) * 0.5f;

            // Edge direction in XZ plane
            Vector3 edgeDir = (bWorld - aWorld);
            edgeDir.y = 0f;
            float edgeLength = edgeDir.magnitude;

            if (edgeLength < 0.0001f) return; // safety guard

            // Rotation: rotate around Y only
            float angle = Mathf.Atan2(edgeDir.x, edgeDir.z) * Mathf.Rad2Deg + 90f;
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);

            // Scale: X = length of edge, Z = thickness
            Vector3 scale = new Vector3(edgeLength, 1f, thickness);

            // Cached material with correct renderQueue
            Material mat = GetOrCreateCachedMaterial(baseMat, renderQueue);

            // Face-up, long edge along trapezoid edge
            Matrix4x4 matrix = Matrix4x4.TRS(midPoint, rotation, scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }

        #endregion

        #region detection helpers
        public static bool IsVentOrCooler(Thing t)
        {
            if (t == null || !(t is Building)) return false;
            if (t.def == ThingDefOf.Vent) return true;
            string defName = t.def.defName ?? "";
            if (defName.Equals("Cooler", StringComparison.OrdinalIgnoreCase)) return true;
            if (defName.IndexOf("Vent", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsSmoothedNaturalStoneWall(Thing wall)
        {
            if (wall == null) return false;
            string tex = wall.def?.graphicData?.texPath ?? "";
            string defName = wall.def?.defName ?? "";
            return tex.IndexOf("RockSmooth_Atlas", StringComparison.OrdinalIgnoreCase) >= 0
                || defName.StartsWith("Smoothed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWallLike(Thing t)
        {
            if (t == null) return false;
            if (t is Blueprint || t is Frame) return false; // skip blueprints/frames
            if (!t.def.IsEdifice()) return false;           // only real edifices

            if (t.def == ThingDefOf.Wall) return true;
            if (IsSmoothedNaturalStoneWall(t)) return true;
            if (t.def?.graphicData?.linkFlags.HasFlag(LinkFlags.Wall) ?? false) return true;

            return false;
        }

        private Material GetWallMaterial(Thing wall)
        {
            if (wall == null) return BaseContent.BadMat;

            string atlasPath = null;
            if (IsSmoothedNaturalStoneWall(wall))
                atlasPath = "Things/Building/Linked/RockSmooth_Atlas";
            else if (wall.Stuff != null)
            {
                string stuffName = wall.Stuff.defName.ToLowerInvariant();
                if (stuffName.Contains("blocks") || stuffName.Contains("brick"))
                    atlasPath = "Things/Building/Linked/Wall/Wall_Atlas_Bricks";
                else if (stuffName.Contains("plank") || stuffName.Contains("wood"))
                    atlasPath = "Things/Building/Linked/Wall/Wall_Atlas_Planks";
                else
                    atlasPath = "Things/Building/Linked/Wall/Wall_Atlas_Smooth";
            }

            Color tint = wall.DrawColor;
            if (!string.IsNullOrEmpty(atlasPath))
                return MaterialPool.MatFrom(atlasPath, ShaderDatabase.Cutout, tint);

            if (wall.Graphic is Graphic_Multi gm) return gm.MatAt(Rot4.North, wall);
            if (wall.Graphic is Graphic_Single gs) return gs.MatSingle;

            return BaseContent.BadMat;
        }
        #endregion

        #region mesh generation
        private static Mesh GenerateTrapezoidMesh()
        {
            float southWidth = 1f;
            float northWidth = 0.55f;
            float depth = 0.225f;

            Mesh mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-southWidth * 0.5f, 0f, -depth * 0.5f),
                    new Vector3( southWidth * 0.5f, 0f, -depth * 0.5f),
                    new Vector3(-northWidth * 0.5f, 0f,  depth * 0.5f),
                    new Vector3( northWidth * 0.5f, 0f,  depth * 0.5f)
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 }
            };

            float tileSize = 0.25f;
            float atlasPixels = 320f;
            float margin = 10f / atlasPixels;
            int col = 1, row = 1;

            float uStart = col * tileSize + margin;
            float uEnd = (col + 1) * tileSize - margin;
            float southUVWidth = uEnd - uStart;
            float northUVWidth = southUVWidth * northWidth / southWidth;
            float uNorthStart = uStart + (southUVWidth - northUVWidth) * 0.5f;
            float uNorthEnd = uNorthStart + northUVWidth;
            float vStart = row * tileSize + margin;
            float vEnd = vStart + 0.0421875f;

            mesh.uv = new[]
            {
                new Vector2(uStart, vStart),
                new Vector2(uEnd, vStart),
                new Vector2(uNorthStart, vEnd),
                new Vector2(uNorthEnd, vEnd)
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        #endregion

        #region MaterialAllocator & caching
        public static class MaterialAllocator
        {
            public static Material CreateWithRenderQueue(Material source, int renderQueue)
            {
                if (source == null) return null;
                Material m = new Material(source);
                m.renderQueue = renderQueue;
                return m;
            }

            public static Material CreateSolid(Color color, int renderQueue)
            {
                Material m = SolidColorMaterials.SimpleSolidColorMaterial(color, false);
                m.renderQueue = renderQueue;
                return m;
            }
        }

        private Material GetOrCreateCachedMaterial(Material source, int renderQueue)
        {
            if (source == null) return null;
            int key = source.GetInstanceID() ^ renderQueue;
            if (!materialCache.TryGetValue(key, out Material cached))
            {
                cached = new Material(source) { renderQueue = renderQueue };
                materialCache[key] = cached;
            }
            return cached;
        }

        private Material GetOrCreateOutlineMaterial()
        {
            if (outlineMaterial == null)
                outlineMaterial = MaterialAllocator.CreateSolid(Color.black, OutlineRenderQueue);
            return outlineMaterial;
        }
        #endregion
    }
}
