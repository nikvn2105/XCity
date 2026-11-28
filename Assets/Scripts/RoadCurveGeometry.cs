using System;
using System.Collections.Generic;

namespace XCity
{
    internal readonly struct RoadPoint
    {
        internal readonly float X, Z;
        internal RoadPoint(float x, float z) { X = x; Z = z; }
        internal float LengthSquared => X * X + Z * Z;
        internal float Length => (float)Math.Sqrt(LengthSquared);
        internal RoadPoint Unit => LengthSquared > 1e-10f ? this / Length : new RoadPoint(1f, 0f);
        internal RoadPoint Right => new RoadPoint(Z, -X);
        internal static float Dot(RoadPoint a, RoadPoint b) => a.X * b.X + a.Z * b.Z;
        public static RoadPoint operator +(RoadPoint a, RoadPoint b) => new RoadPoint(a.X + b.X, a.Z + b.Z);
        public static RoadPoint operator -(RoadPoint a, RoadPoint b) => new RoadPoint(a.X - b.X, a.Z - b.Z);
        public static RoadPoint operator *(RoadPoint a, float b) => new RoadPoint(a.X * b, a.Z * b);
        public static RoadPoint operator /(RoadPoint a, float b) => a * (1f / b);
    }

    // Distances are in cell units. The table is shared by meshes, walkers and all traffic lanes.
    internal sealed class RoadArc
    {
        private readonly RoadSegment _segment;
        private readonly float[] _distances;
        internal float Length => _distances[_distances.Length - 1];

        internal RoadArc(RoadSegment segment)
        {
            _segment = segment;
            var a = new RoadPoint(segment.ax, segment.az);
            var b = new RoadPoint(segment.bx, segment.bz);
            var c = new RoadPoint(segment.cx, segment.cz);
            int steps = Math.Max(16, (int)Math.Ceiling(8f * ((a - c).Length + (c - b).Length)));
            _distances = new float[steps + 1];
            var previous = a;
            for (int i = 1; i <= steps; i++)
            {
                RoadPoint point = At(i / (float)steps);
                _distances[i] = _distances[i - 1] + (point - previous).Length;
                previous = point;
            }
        }

        private RoadPoint At(float t)
        {
            var (x, z) = _segment.At(t);
            return new RoadPoint(x, z);
        }

        internal RoadPoint Sample(float distance, out RoadPoint forward)
        {
            distance = Math.Max(0f, Math.Min(Length, distance));
            int index = Array.BinarySearch(_distances, distance);
            if (index < 0) index = ~index;
            index = Math.Max(1, index);
            float from = _distances[index - 1], to = _distances[index];
            float t = (index - 1 + (to > from ? (distance - from) / (to - from) : 0f)) / (_distances.Length - 1);
            forward = (new RoadPoint(_segment.cx - _segment.ax, _segment.cz - _segment.az) * (1f - t) +
                new RoadPoint(_segment.bx - _segment.cx, _segment.bz - _segment.cz) * t).Unit;
            return At(t);
        }

        internal float ClosestDistance(RoadPoint point)
        {
            float best = float.PositiveInfinity, distance = 0f;
            RoadPoint a = At(0f);
            for (int i = 1; i < _distances.Length; i++)
            {
                RoadPoint b = At(i / (float)(_distances.Length - 1));
                RoadPoint ab = b - a;
                float t = ab.LengthSquared > 0f
                    ? Math.Max(0f, Math.Min(1f, RoadPoint.Dot(point - a, ab) / ab.LengthSquared)) : 0f;
                float error = (a + ab * t - point).LengthSquared;
                if (error < best)
                {
                    best = error;
                    distance = _distances[i - 1] + t * (_distances[i] - _distances[i - 1]);
                }
                a = b;
            }
            return distance;
        }

        // Both adjoining cells use the same mouth, on their shared edge.
        internal static RoadPoint Edge(RoadPoint a, RoadPoint b) => (a + b) * 0.5f;

        internal RoadPoint Facing(RoadPoint lot, out float distanceSquared)
        {
            RoadPoint point = Sample(ClosestDistance(lot), out RoadPoint tangent);
            RoadPoint toRoad = point - lot;
            distanceSquared = toRoad.LengthSquared;
            RoadPoint normal = tangent.Right;
            return RoadPoint.Dot(normal, toRoad) >= 0f ? normal : normal * -1f;
        }
    }

    internal sealed class RoadCurveCell
    {
        internal readonly RoadArc Arc;
        internal readonly RoadPoint Center, Previous, Next;
        internal readonly float Start, End;
        internal readonly bool HasPrevious, HasNext;

        internal RoadCurveCell(RoadArc arc, RoadPoint center, RoadPoint previous, RoadPoint next,
            bool hasPrevious, bool hasNext, float start, float end)
        {
            Arc = arc; Center = center; Previous = previous; Next = next;
            HasPrevious = hasPrevious; HasNext = hasNext; Start = start; End = end;
        }

        internal int RibbonSteps(bool joined)
        {
            Arc.Sample(Start, out RoadPoint entry);
            Arc.Sample(End, out RoadPoint exit);
            float turn = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, RoadPoint.Dot(entry, exit))));
            return Math.Max(joined ? 4 : 2, Math.Max((int)Math.Ceiling((End - Start) * 2f), (int)Math.Ceiling(turn * 6f)));
        }

        internal bool DashAt(int step, int steps) =>
            (Start + (End - Start) * (step + 0.5f) / steps) % 1.10f < 0.55f;

        internal RoadPoint Sample(float progress, float side, bool joinStart, bool joinEnd,
            float? startSide = null, float? endSide = null)
        {
            float distance = progress == 1f ? End : Start + (End - Start) * progress;
            float blendRange = Math.Min(0.5f, (End - Start) * 0.5f);
            if (joinStart && progress == 0f) return Mouth(Previous, Center, startSide ?? side);
            if (joinEnd && progress == 1f) return Mouth(Center, Next, endSide ?? side);

            RoadPoint point = Rail(distance);
            if (blendRange <= 1e-5f) return point;
            if (joinStart && distance - Start < blendRange)
                point += Correction(Start, Previous, Center, startSide ?? side, distance - Start, 1f);
            if (joinEnd && End - distance < blendRange)
                point += Correction(End, Center, Next, endSide ?? side, End - distance, -1f);
            return point;

            RoadPoint Rail(float at)
            {
                RoadPoint onArc = Arc.Sample(at, out RoadPoint direction);
                return onArc + direction.Right * side;
            }

            static RoadPoint Mouth(RoadPoint a, RoadPoint b, float mouthSide) =>
                RoadArc.Edge(a, b) + (b - a).Right * mouthSide;

            RoadPoint Correction(float at, RoadPoint a, RoadPoint b, float mouthSide, float fromEdge, float sign)
            {
                RoadPoint rail = Rail(at);
                float delta = Math.Min(0.01f, blendRange);
                RoadPoint towardCurve = (Rail(at + delta * sign) - rail) / delta;
                float u = fromEdge / blendRange, weight = 1f - u;
                // Hermite correction: match the grid approach direction at the seam,
                // then merge back into the Bézier with continuous direction.
                return (Mouth(a, b, mouthSide) - rail) * (weight * weight * (1f + 2f * u)) +
                    ((b - a) * sign - towardCurve) * (blendRange * u * weight * weight);
            }
        }
    }

    internal static class RoadRibbonClip
    {
        internal static int HalfPlane(ReadOnlySpan<RoadPoint> input, Span<RoadPoint> output,
            bool useX, float boundary, bool positive)
        {
            int count = 0;
            RoadPoint previous = input[input.Length - 1];
            float a = Coordinate(previous) - boundary;
            foreach (RoadPoint point in input)
            {
                float b = Coordinate(point) - boundary;
                if ((a < 0f && b > 0f) || (a > 0f && b < 0f))
                {
                    RoadPoint crossing = previous + (point - previous) * (a / (a - b));
                    output[count++] = useX ? new RoadPoint(boundary, crossing.Z) : new RoadPoint(crossing.X, boundary);
                }
                if (positive ? b >= 0f : b <= 0f) output[count++] = point;
                previous = point;
                a = b;
            }
            return count;

            float Coordinate(RoadPoint p) => useX ? p.X : p.Z;
        }
    }

    internal sealed class RoadCurveGeometry
    {
        private readonly CitySimulation _city;
        private readonly Dictionary<int, RoadCurveCell> _cells = new Dictionary<int, RoadCurveCell>();
        private readonly Dictionary<int, RoadArc> _arcs = new Dictionary<int, RoadArc>();
        private readonly Dictionary<int, int> _memberships = new Dictionary<int, int>();
        private readonly Dictionary<int, List<(int X, int Z)>> _paths = new Dictionary<int, List<(int X, int Z)>>();

        internal RoadCurveGeometry(CitySimulation city)
        {
            _city = city;
            foreach (var entry in city.RoadSegments) Add(entry.Key, entry.Value);
            city.RoadSegmentChanged += Changed;
        }

        private void Changed(int id, RoadSegment segment, bool added)
        {
            if (added) { Add(id, segment); return; }
            foreach (var (x, z) in _paths[id])
            {
                int index = CitySimulation.CellIndex(x, z);
                if (_cells.TryGetValue(index, out var span) && span.Arc == _arcs[id]) _cells.Remove(index);
                if (_memberships[index] == 1) _memberships.Remove(index);
                else _memberships[index]--;
            }
            _paths.Remove(id);
            _arcs.Remove(id);
        }

        private void Add(int id, RoadSegment segment)
        {
            var arc = new RoadArc(segment);
            var path = CitySimulation.RasterizeSegment(segment);
            _arcs[id] = arc;
            _paths[id] = path;
            var cuts = new float[path.Count + 1];
            cuts[path.Count] = arc.Length;
            float minimum = arc.Length / (path.Count * 32f);
            for (int i = 1; i < path.Count; i++)
            {
                float nearEdge = arc.ClosestDistance(RoadArc.Edge(Center(path[i - 1]), Center(path[i])));
                cuts[i] = Math.Max(cuts[i - 1] + minimum, Math.Min(arc.Length - (path.Count - i) * minimum, nearEdge));
            }
            for (int i = 0; i < path.Count; i++)
            {
                var (x, z) = path[i];
                int index = CitySimulation.CellIndex(x, z);
                _memberships.TryGetValue(index, out int count);
                _memberships[index] = count + 1;
                if (_city.SegmentAt(x, z) != id || _cells.ContainsKey(index)) continue;
                bool before = i > 0, after = i + 1 < path.Count;
                _cells[index] = new RoadCurveCell(arc, Center(path[i]), before ? Center(path[i - 1]) : default,
                    after ? Center(path[i + 1]) : default, before, after, cuts[i], cuts[i + 1]);
            }

            static RoadPoint Center((int X, int Z) p) => new RoadPoint(p.X + 0.5f, p.Z + 0.5f);
        }

        internal bool TryGet(int x, int z, out RoadCurveCell cell)
        {
            cell = null;
            return (uint)x < CitySimulation.GridSize && (uint)z < CitySimulation.GridSize &&
                _cells.TryGetValue(CitySimulation.CellIndex(x, z), out cell);
        }

        internal bool IsJunction(int x, int z)
        {
            if ((uint)x >= CitySimulation.GridSize || (uint)z >= CitySimulation.GridSize || _city.GetCell(x, z).Type != CellType.Road) return false;
            int owner = _city.SegmentAt(x, z);
            _memberships.TryGetValue(CitySimulation.CellIndex(x, z), out int count);
            if (count > 1 || (count > 0 && owner == 0)) return true;
            if (owner == 0) return false;

            if (!TryGet(x, z, out RoadCurveCell span)) return false;
            int links = (int)_city.Roads.GetConnections(x, z), degree = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                if ((links & (1 << dir)) == 0) continue;
                degree++;
                RoadPoint step = Step(dir);
                RoadPoint neighbor = span.Center + step;
                int nx = x + (int)step.X, nz = z + (int)step.Z;
                if (_city.SegmentAt(nx, nz) != owner ||
                    _city.GetCell(nx, nz).RoadType != _city.GetCell(x, z).RoadType ||
                    !(span.HasPrevious && (span.Previous - neighbor).LengthSquared == 0f ||
                      span.HasNext && (span.Next - neighbor).LengthSquared == 0f)) return true;
            }
            return degree >= 3;
        }

        internal bool Join(RoadPoint neighbor, bool hasNeighbor) =>
            hasNeighbor && IsJunction((int)neighbor.X, (int)neighbor.Z);

        internal bool TryFacing(int x, int z, out RoadPoint facing)
        {
            facing = default;
            float best = float.PositiveInfinity;
            for (int dir = 0; dir < 4; dir++)
            {
                RoadPoint step = Step(dir);
                int owner = _city.SegmentAt(x + (int)step.X, z + (int)step.Z);
                if (owner == 0 || !_arcs.TryGetValue(owner, out RoadArc arc)) continue;
                RoadPoint normal = arc.Facing(new RoadPoint(x + 0.5f, z + 0.5f), out float distance);
                if (distance >= best) continue;
                facing = normal;
                best = distance;
            }
            return best < float.PositiveInfinity;
        }

        internal static RoadPoint Step(int dir) => dir switch
        {
            0 => new RoadPoint(-1f, 0f), 1 => new RoadPoint(1f, 0f),
            2 => new RoadPoint(0f, -1f), _ => new RoadPoint(0f, 1f)
        };
    }

    public sealed partial class CitySimulation
    {
        private RoadCurveGeometry _roadCurves;
        internal RoadCurveGeometry RoadCurves => _roadCurves ??= new RoadCurveGeometry(this);
    }
}
