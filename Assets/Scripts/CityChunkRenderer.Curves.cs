using System;
using UnityEngine;

namespace XCity
{
    internal sealed partial class CityChunkRenderer
    {
        private RoadCurveGeometry _curves;

        private void OnRoadSegmentChanged(int id, RoadSegment segment, bool added)
        {
            foreach (var (x, z) in CitySimulation.RasterizeSegment(segment))
            {
                MarkChunk(x, z, includeScenery: true);
                // A junction can also change the end of an adjoining road slice.
                if (_city.SegmentAt(x - 1, z) != 0) MarkChunk(x - 1, z, includeScenery: true);
                if (_city.SegmentAt(x + 1, z) != 0) MarkChunk(x + 1, z, includeScenery: true);
                if (_city.SegmentAt(x, z - 1) != 0) MarkChunk(x, z - 1, includeScenery: true);
                if (_city.SegmentAt(x, z + 1) != 0) MarkChunk(x, z + 1, includeScenery: true);
            }
        }

        private bool AddCurvedRoadTile(MeshBuilder mesh, int x, int z, TerrainPatch surface,
            Vector3 center, CityCell cell, bool scenery)
        {
            bool junction = _curves.IsJunction(x, z);
            if (!_curves.TryGet(x, z, out RoadCurveCell span) && !junction) return false;

            Color32 asphalt = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
            if (junction)
            {
                // A single full-square patch on flat land. On slopes the same patch is
                // subdivided so each point on the perimeter agrees with the adjoining road.
                int divisions = surface.IsSlope ? 2 : 1;
                for (int u = 0; u < divisions; u++)
                for (int v = 0; v < divisions; v++)
                    AddTerrainQuad(mesh, surface, center.y, u / (float)divisions, v / (float)divisions,
                        (u + 1) / (float)divisions, (v + 1) / (float)divisions, RoadY, asphalt, false);
                return true;
            }

            AddSlopedGround(mesh, surface, _infoView ? GetInfoGroundColor(cell, x, z) : GetNaturalGroundColor(x, z));
            AddRoadRibbon(mesh, span, x, z, center, cell.RoadType, asphalt, scenery);
            return true;
        }

        private void AddRoadRibbon(MeshBuilder mesh, RoadCurveCell span, int x, int z, Vector3 center,
            RoadType type, Color32 asphalt, bool scenery)
        {
            float half = RoadHalfWidthRatio(type);
            float outside = type == RoadType.Avenue ? 0.49f : 0.445f;
            bool joinStart = _curves.Join(span.Previous, span.HasPrevious);
            bool joinEnd = _curves.Join(span.Next, span.HasNext);
            float length = span.End - span.Start;
            int steps = span.RibbonSteps(joinStart || joinEnd);
            Span<RoadPoint> polygon = stackalloc RoadPoint[4];

            for (int i = 0; i < steps; i++)
            {
                float a = i / (float)steps, b = (i + 1) / (float)steps;
                PaintStrip(polygon, a, b, -half, half, RoadY, asphalt);
                PaintStrip(polygon, a, b, -outside, -half, CurbY - 0.012f, Paving);
                PaintStrip(polygon, a, b, half, outside, CurbY - 0.012f, Paving);

                if (_infoView) continue;
                bool dash = span.DashAt(i, steps);
                if (type == RoadType.Avenue)
                {
                    PaintStrip(polygon, a, b, -0.025f, 0.025f, PaintY + 0.012f, Curb);
                    if (dash)
                    {
                        PaintStrip(polygon, a, b, -0.204f, -0.186f, PaintY, Paint);
                        PaintStrip(polygon, a, b, 0.186f, 0.204f, PaintY, Paint);
                    }
                }
                else if (type == RoadType.OneWay)
                {
                    PaintStrip(polygon, a, b, -0.269f, -0.251f, PaintY, Paint);
                    PaintStrip(polygon, a, b, 0.251f, 0.269f, PaintY, Paint);
                }
                else if (dash) PaintStrip(polygon, a, b, -0.010f, 0.010f, PaintY, LanePaint);
            }

            if (!_infoView && type == RoadType.OneWay && length >= 0.32f)
            {
                bool forward = !span.HasNext || (int)_city.GetCell(x, z).OneWayExit ==
                    Direction(span.Next - span.Center);
                float tail = forward ? 0.32f : 0.68f, head = forward ? 0.68f : 0.32f;
                float neck = 0.5f;
                PaintStrip(polygon, Math.Min(tail, neck), Math.Max(tail, neck), -0.014f, 0.014f, PaintY, Paint);
                polygon[0] = Sample(neck, -0.063f);
                polygon[1] = Sample(neck, 0.063f);
                polygon[2] = Sample(head, 0f);
                EmitRibbonPolygon(mesh, polygon.Slice(0, 3), x, z, center, PaintY, Paint);
            }

            if (scenery && type == RoadType.Boulevard && length >= 0.45f)
            {
                uint seed = BuildingMeshFactory.Hash(x, z, 419);
                for (int side = -1; side <= 1; side += 2)
                {
                    RoadPoint p = Sample(0.5f, side * 0.39f);
                    if (!WorldTerrain.IsLand((int)p.X, (int)p.Z) || _curves.IsJunction((int)p.X, (int)p.Z)) continue;
                    CityCell lot = _city.GetCell((int)p.X, (int)p.Z);
                    if (lot.Type != CellType.Empty && lot.Type != CellType.Road) continue;
                    TreeMeshFactory.AddStreetTree(_props, LocalRibbonPoint(p, x, z, center, CurbY), 0.48f, seed + (uint)(side + 1));
                }
            }

            RoadPoint Sample(float t, float side) => span.Sample(t, side, joinStart, joinEnd);

            void PaintStrip(Span<RoadPoint> quad, float from, float to, float left, float right, float lift, Color32 color)
            {
                quad[0] = Sample(from, left);
                quad[1] = Sample(to, left);
                quad[2] = Sample(to, right);
                quad[3] = Sample(from, right);
                EmitRibbonPolygon(mesh, quad, x, z, center, lift, color);
            }

            static int Direction(RoadPoint step) => step.X < 0f ? 1 : step.X > 0f ? 2 : step.Z < 0f ? 4 : 8;
        }

        private Vector3 LocalRibbonPoint(RoadPoint point, int x, int z, Vector3 center, float lift)
        {
            float worldX = point.X * _cellSize, worldZ = point.Z * _cellSize;
            return new Vector3((point.X - x - 0.5f) * _cellSize,
                WorldTerrain.SurfaceY(worldX, worldZ, _cellSize) - center.y + lift,
                (point.Z - z - 0.5f) * _cellSize);
        }

        private void EmitRibbonPolygon(MeshBuilder mesh, ReadOnlySpan<RoadPoint> polygon, int x, int z,
            Vector3 center, float lift, Color32 color)
        {
            // Remove any portion inside a junction square, including wide shoulders that
            // reach a junction diagonally. Both meshes meet on the same terrain samples.
            EmitOutside(polygon, 0);

            void EmitOutside(ReadOnlySpan<RoadPoint> points, int after)
            {
                if (points.Length < 3) return;
                float minX = points[0].X, maxX = minX, minZ = points[0].Z, maxZ = minZ;
                foreach (RoadPoint p in points)
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
                }
                for (int ix = Math.Max(0, (int)Math.Floor(minX)); ix <= (int)Math.Floor(maxX); ix++)
                for (int iz = Math.Max(0, (int)Math.Floor(minZ)); iz <= (int)Math.Floor(maxZ); iz++)
                {
                    int key = CitySimulation.CellIndex(ix, iz) + 1;
                    if (key <= after || minX >= ix + 1 || maxX <= ix || minZ >= iz + 1 || maxZ <= iz ||
                        !_curves.IsJunction(ix, iz)) continue;

                    Span<RoadPoint> remaining = stackalloc RoadPoint[points.Length + 4];
                    Span<RoadPoint> inside = stackalloc RoadPoint[remaining.Length];
                    Span<RoadPoint> outside = stackalloc RoadPoint[remaining.Length];
                    points.CopyTo(remaining);
                    int count = points.Length;
                    for (int edge = 0; edge < 4 && count >= 3; edge++)
                    {
                        bool useX = edge < 2, positive = edge % 2 == 0;
                        float boundary = (useX ? ix : iz) + (positive ? 0 : 1);
                        int countInside = RoadRibbonClip.HalfPlane(remaining.Slice(0, count), inside, useX, boundary, positive);
                        int countOutside = RoadRibbonClip.HalfPlane(remaining.Slice(0, count), outside, useX, boundary, !positive);
                        EmitOutside(outside.Slice(0, countOutside), key);
                        inside.Slice(0, countInside).CopyTo(remaining);
                        count = countInside;
                    }
                    return;
                }

                Vector3 a = LocalRibbonPoint(points[0], x, z, center, lift);
                for (int i = 2; i < points.Length; i++)
                {
                    RoadPoint ab = points[i - 1] - points[0], ac = points[i] - points[0];
                    if (Math.Abs(ab.X * ac.Z - ab.Z * ac.X) < 1e-7f) continue;
                    mesh.AddTriangle(a, LocalRibbonPoint(points[i - 1], x, z, center, lift),
                        LocalRibbonPoint(points[i], x, z, center, lift), Vector3.up, color);
                }
            }
        }
    }
}
