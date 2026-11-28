using System;
using System.Collections.Generic;

namespace XCity
{
    /// <summary>
    /// Phase 4b: a road drawn as one quadratic curve (start, control, end in cell units, cell centres at .5).
    /// The curve is laid onto the grid as ordinary road cells, so access, utilities, traffic and pathing are
    /// unchanged; the curve itself is only for drawing and vehicles (phase 4d).
    /// </summary>
    [Serializable]
    public struct RoadSegment
    {
        public float ax, az, cx, cz, bx, bz;
        public RoadType type;

        public RoadSegment(float ax, float az, float cx, float cz, float bx, float bz, RoadType type)
        {
            this.ax = ax; this.az = az; this.cx = cx; this.cz = cz; this.bx = bx; this.bz = bz; this.type = type;
        }

        public (float X, float Z) At(float t)
        {
            float u = 1f - t;
            return (u * u * ax + 2f * u * t * cx + t * t * bx, u * u * az + 2f * u * t * cz + t * t * bz);
        }
    }

    public sealed partial class CitySimulation
    {
        internal const int MaxSegmentCells = 256;

        private readonly Dictionary<int, RoadSegment> _segments = new Dictionary<int, RoadSegment>();
        private readonly Dictionary<int, List<int>> _segmentCells = new Dictionary<int, List<int>>();
        private readonly int[] _cellSegment = new int[GridSize * GridSize];
        private int _nextSegmentId = 1;

        public IReadOnlyDictionary<int, RoadSegment> RoadSegments => _segments;

        /// <summary>Segment drawn through this road cell, 0 for plain grid roads.</summary>
        public int SegmentAt(int x, int z) => IsInBounds(x, z) ? _cellSegment[CellIndex(x, z)] : 0;

        /// <summary>
        /// Grid cells along the curve as a 4-connected path from start to end (a diagonal step gets the
        /// cell on the x side first). Empty when an endpoint is off the map or the path is too long.
        /// </summary>
        internal static List<(int X, int Z)> RasterizeSegment(RoadSegment segment)
        {
            var cells = new List<(int X, int Z)>();
            float length = Distance(segment.ax, segment.az, segment.cx, segment.cz) +
                Distance(segment.cx, segment.cz, segment.bx, segment.bz);
            int samples = Math.Max(1, (int)Math.Ceiling(length * 4f));
            for (int i = 0; i <= samples; i++)
            {
                var (fx, fz) = segment.At(i / (float)samples);
                int x = (int)Math.Floor(fx), z = (int)Math.Floor(fz);
                if (!IsInBounds(x, z)) return new List<(int X, int Z)>();
                if (cells.Count > 0)
                {
                    var (px, pz) = cells[cells.Count - 1];
                    if (px == x && pz == z) continue;
                    // Keep every step to a shared edge so the cells form one connected road.
                    while (Math.Abs(x - px) + Math.Abs(z - pz) > 1)
                    {
                        if (x != px) px += Math.Sign(x - px);
                        else pz += Math.Sign(z - pz);
                        cells.Add((px, pz));
                    }
                }
                cells.Add((x, z));
                if (cells.Count > MaxSegmentCells) return new List<(int X, int Z)>();
            }
            return cells;
        }

        private static float Distance(float ax, float az, float bx, float bz) =>
            (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));

        /// <summary>Builds the curve as road cells (one undo step) and records it over the cells it now owns.</summary>
        public bool TryBuildSegment(RoadSegment segment, out int id)
        {
            id = 0;
            List<(int X, int Z)> cells = RasterizeSegment(segment);
            if (cells.Count < 2) return false;
            // Curves stay on land for now (bridges remain straight spans), so nothing is half-built.
            foreach (var (x, z) in cells)
                if (!IsBuildable(x, z) && _cells[x, z].Type != CellType.Road) return false;
            if (!TryBuildRoad(cells, segment.type)) return false;
            id = AttachSegment(segment, cells);
            return id != 0;
        }

        // Junction cells already owned by an earlier curve keep that owner.
        private int AttachSegment(RoadSegment segment, List<(int X, int Z)> cells)
        {
            foreach (var (x, z) in cells)
                if (_cells[x, z].Type != CellType.Road) return 0;
            int id = _nextSegmentId++;
            _segments[id] = segment;
            var owned = new List<int>(cells.Count);
            foreach (var (x, z) in cells)
            {
                int index = CellIndex(x, z);
                if (_cellSegment[index] != 0) continue;
                _cellSegment[index] = id;
                owned.Add(index);
            }
            _segmentCells[id] = owned;
            return id;
        }

        // Called from TrackCell: a curve that loses any of its road cells falls back to plain grid road.
        // shortcut: undoing a bulldoze restores the road cells but not the curve, until 4e rebuilds curves from runs.
        private void TrackSegmentCell(int index, CellType type)
        {
            int id = _cellSegment[index];
            if (id == 0 || type == CellType.Road) return;
            _segments.Remove(id);
            foreach (int cell in _segmentCells[id]) _cellSegment[cell] = 0;
            _segmentCells.Remove(id);
        }

        private void WriteRoadSegments(CitySnapshot snapshot)
        {
            if (_segments.Count == 0) return;
            var ids = new List<int>(_segments.Keys);
            ids.Sort();
            snapshot.roadSegments = new RoadSegment[ids.Count];
            for (int i = 0; i < ids.Count; i++) snapshot.roadSegments[i] = _segments[ids[i]];
        }

        // Saved curves are re-laid on the restored grid; one whose cells are no longer all road is dropped.
        private void RestoreRoadSegments(CitySnapshot snapshot)
        {
            if (snapshot.schemaVersion < 18 || snapshot.roadSegments == null) return;
            foreach (RoadSegment segment in snapshot.roadSegments)
            {
                if (segment.type > RoadType.Boulevard) continue;
                List<(int X, int Z)> cells = RasterizeSegment(segment);
                if (cells.Count >= 2) AttachSegment(segment, cells);
            }
        }
    }
}
