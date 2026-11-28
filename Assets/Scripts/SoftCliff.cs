using System;

namespace XCity
{
    // Absolute heights on the shared lattice are independent of which chunk builds them.
    internal readonly struct SoftCliff
    {
        internal const int GroundPatchCount = 3;
        private const float Crest = 0.21f;
        private readonly float _west, _east, _south, _north, _sw, _se, _nw, _ne;

        public SoftCliff(int x, int z)
        {
            this = default;
            if (WorldTerrain.Current.Style == MapStyle.Classic || !WorldTerrain.IsLand(x, z) ||
                !WorldTerrain.IsSlope(x, z)) return;
            float level = WorldTerrain.SurfaceY(x, z);
            float west = LandLevel(x - 1, z), east = LandLevel(x + 1, z);
            float south = LandLevel(x, z - 1), north = LandLevel(x, z + 1);
            _west = Join(level, west) - level;
            _east = Join(level, east) - level;
            _south = Join(level, south) - level;
            _north = Join(level, north) - level;
            _sw = Corner(x, z) - level;
            _se = Corner(x + 1, z) - level;
            _nw = Corner(x, z + 1) - level;
            _ne = Corner(x + 1, z + 1) - level;

            float LandLevel(int nx, int nz) => WorldTerrain.IsLand(nx, nz) ? WorldTerrain.SurfaceY(nx, nz) : float.NaN;
        }

        public bool Active => _west != 0f || _east != 0f || _south != 0f || _north != 0f ||
            _sw != 0f || _se != 0f || _nw != 0f || _ne != 0f;

        private static float Join(float a, float b) =>
            float.IsNaN(b) ? a : Math.Min(a, b) + Math.Abs(a - b) * 0.4f;

        private static float Corner(int x, int z)
        {
            float a = WorldTerrain.SurfaceY(x - 1, z - 1), b = WorldTerrain.SurfaceY(x, z - 1);
            float c = WorldTerrain.SurfaceY(x - 1, z), d = WorldTerrain.SurfaceY(x, z);
            bool landA = WorldTerrain.IsLand(x - 1, z - 1), landB = WorldTerrain.IsLand(x, z - 1);
            bool landC = WorldTerrain.IsLand(x - 1, z), landD = WorldTerrain.IsLand(x, z);

            // An incident land cell with two matching edges can remain an untouched plateau.
            if (landA && a == b && a == c) return a;
            if (landB && b == a && b == d) return b;
            if (landC && c == a && c == d) return c;
            if (landD && d == b && d == c) return d;
            float low = float.PositiveInfinity, high = float.NegativeInfinity, sum = 0f;
            int count = 0;
            if (landA) Accumulate(a);
            if (landB) Accumulate(b);
            if (landC) Accumulate(c);
            if (landD) Accumulate(d);
            return (sum / count + low + (high - low) * 0.4f) * 0.5f;

            void Accumulate(float height)
            {
                low = Math.Min(low, height);
                high = Math.Max(high, height);
                sum += height;
                count++;
            }
        }

        internal static float GroundBoundary(int i) => i == 0 ? -0.5f : i == 1 ? -Crest : i == 2 ? Crest : 0.5f;
        internal static bool GroundFlip(int x, int z) => x == z && x != 1;

        private float GroundVertex(float u, float v)
        {
            float sideX = u < 0 ? _west : _east, sideZ = v < 0 ? _south : _north;
            if (Math.Abs(u) == 0.5f && Math.Abs(v) == 0.5f)
                return v < 0 ? (u < 0 ? _sw : _se) : (u < 0 ? _nw : _ne);
            if (Math.Abs(u) == 0.5f) return sideX;
            if (Math.Abs(v) == 0.5f) return sideZ;
            return 0f;
        }

        // The picking grid samples the same coarse slope through this existing entry point.
        public float Vertex(float u, float v) => Sample(u, v);

        public float RoadY(RoadGrade grade, RoadConnections links, float u, float v)
        {
            float road = grade.Sample(u, v);
            if (Math.Abs(u) <= RoadGrade.SharedEdge && Math.Abs(v) <= RoadGrade.SharedEdge) return road;
            if (Math.Abs(v) <= RoadGrade.SharedEdge && (links & (u < 0 ? RoadConnections.West : RoadConnections.East)) != 0) return road;
            if (Math.Abs(u) <= RoadGrade.SharedEdge && (links & (v < 0 ? RoadConnections.South : RoadConnections.North)) != 0) return road;
            return Active ? grade.Base + Sample(u, v) + 0.007f - CityChunkRenderer.RoadY : road;
        }

        public float Sample(float u, float v)
        {
            if (!Active) return 0f;
            int ix = u < -Crest ? 0 : u > Crest ? 2 : 1;
            int iz = v < -Crest ? 0 : v > Crest ? 2 : 1;
            float x0 = GroundBoundary(ix), x1 = GroundBoundary(ix + 1);
            float z0 = GroundBoundary(iz), z1 = GroundBoundary(iz + 1);
            float x = (u - x0) / (x1 - x0), z = (v - z0) / (z1 - z0);
            float a = GroundVertex(x0, z0), b = GroundVertex(x0, z1);
            float c = GroundVertex(x1, z1), d = GroundVertex(x1, z0);
            if (GroundFlip(ix, iz))
                return x + z <= 1f ? a + (d - a) * x + (b - a) * z : c + (b - c) * (1f - x) + (d - c) * (1f - z);
            return x <= z ? a + (c - b) * x + (b - a) * z : a + (d - a) * x + (c - d) * z;
        }
    }
}
