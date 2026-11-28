using UnityEngine;

namespace XCity
{
    // Shared positioning rules for cars and walkers. Their route and occupancy remain cell based.
    internal readonly struct CurvedRoadMotion
    {
        private readonly RoadCurveCell _span;
        private readonly bool _reverse, _joinStart, _joinEnd;
        private readonly float _offset, _startOffset, _endOffset, _cellSize;

        private CurvedRoadMotion(RoadCurveCell span, bool reverse, bool joinStart, bool joinEnd, float offset, float enterOffset, float exitOffset, float cellSize)
        {
            _span = span; _reverse = reverse; _joinStart = joinStart; _joinEnd = joinEnd;
            _offset = reverse ? -offset : offset;
            _startOffset = reverse ? -exitOffset : enterOffset;
            _endOffset = reverse ? -enterOffset : exitOffset;
            _cellSize = cellSize;
        }

        internal float Length => (_span.End - _span.Start) * _cellSize;

        internal static bool TryCreate(CitySimulation city, Vector2Int cell, int incoming, int outgoing,
            float side, float enterSide, float exitSide, float cellSize, out CurvedRoadMotion motion)
        {
            motion = default;
            var geometry = city.RoadCurves;
            if (city.SegmentAt(cell.x, cell.y) == 0 || !geometry.TryGet(cell.x, cell.y, out var span) || geometry.IsJunction(cell.x, cell.y)) return false;

            RoadPoint comeFrom = span.Center - RoadCurveGeometry.Step(incoming);
            RoadPoint goTo = span.Center + RoadCurveGeometry.Step(outgoing);
            bool forward = Matches(comeFrom, span.Previous, span.HasPrevious) && Matches(goTo, span.Next, span.HasNext);
            bool reverse = Matches(comeFrom, span.Next, span.HasNext) && Matches(goTo, span.Previous, span.HasPrevious);
            if (!forward && !reverse) return false;

            motion = new CurvedRoadMotion(span, !forward, geometry.Join(span.Previous, span.HasPrevious),
                geometry.Join(span.Next, span.HasNext), side, enterSide, exitSide, cellSize);
            return true;

            static bool Matches(RoadPoint expected, RoadPoint other, bool exists) =>
                !exists || (expected - other).LengthSquared == 0f;
        }

        internal void Evaluate(float progress, float lift, out Vector3 position, out Vector3 tangent)
        {
            progress = Mathf.Clamp01(progress);
            position = Point(progress, lift);
            tangent = Point(Mathf.Min(1f, progress + 0.02f), lift) -
                Point(Mathf.Max(0f, progress - 0.02f), lift);
        }

        private Vector3 Point(float t, float lift)
        {
            RoadPoint point = _span.Sample(_reverse ? 1f - t : t, _offset, _joinStart, _joinEnd, _startOffset, _endOffset);
            float x = point.X * _cellSize, z = point.Z * _cellSize;
            return new Vector3(x, WorldTerrain.SurfaceY(x, z, _cellSize) + lift, z);
        }

        // A U-turn or spawn heading can use the old in-cell arc. Join that arc to the same lane
        // at the neighboring spline exit, even for raster cells created only to connect a diagonal.
        internal static void MatchNeighbor(CitySimulation city, Vector2Int cell, int direction, bool entering,
            float laneOffset, float cellSize, ref Vector3 endpoint)
        {
            if (city.SegmentAt(cell.x, cell.y) == 0) return;
            var geometry = city.RoadCurves;
            if (geometry.IsJunction(cell.x, cell.y)) return;
            RoadPoint step = RoadCurveGeometry.Step(direction) * (entering ? -1f : 1f);
            int x = cell.x + (int)step.X, z = cell.y + (int)step.Z;
            if (!geometry.TryGet(x, z, out var span) || geometry.IsJunction(x, z)) return;
            var center = new RoadPoint(cell.x + 0.5f, cell.y + 0.5f);
            bool atStart = span.HasPrevious && (span.Previous - center).LengthSquared == 0f;
            if (!atStart && (!span.HasNext || (span.Next - center).LengthSquared != 0f)) return;
            bool forward = entering != atStart;
            RoadPoint point = span.Sample(atStart ? 0f : 1f, forward ? laneOffset : -laneOffset, false, false);
            endpoint.x = point.X * cellSize;
            endpoint.z = point.Z * cellSize;
        }
    }
}
