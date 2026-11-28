using System;

namespace XCity
{
    // Render-only ground. Shared edge/corner heights are the lowest touching land tier.
    internal readonly struct SoftCliff
    {
        private readonly float _west, _east, _south, _north, _sw, _se, _nw, _ne;

        public SoftCliff(int x, int z)
        {
            this = default;
            if (WorldTerrain.Current.Style == MapStyle.Classic || !WorldTerrain.IsLand(x, z)) return;
            float level = WorldTerrain.SurfaceY(x, z);
            if (level == 0f) return;
            _west = Lower(x - 1, z);
            _east = Lower(x + 1, z);
            _south = Lower(x, z - 1);
            _north = Lower(x, z + 1);
            _sw = Min(_west, _south, Lower(x - 1, z - 1));
            _se = Min(_east, _south, Lower(x + 1, z - 1));
            _nw = Min(_west, _north, Lower(x - 1, z + 1));
            _ne = Min(_east, _north, Lower(x + 1, z + 1));

            float Lower(int nx, int nz) => WorldTerrain.IsLand(nx, nz)
                ? Math.Min(0f, WorldTerrain.SurfaceY(nx, nz) - level) : 0f;
        }

        public bool Active => _sw < 0f || _se < 0f || _nw < 0f || _ne < 0f;

        private static float Min(float a, float b, float c) => Math.Min(Math.Min(a, b), c);
        private static float Weight(float u) => Math.Max(0f, (Math.Abs(u) - 0.21f) / 0.29f);
        private static float Corner(float u) => Math.Max(0f, (Math.Abs(u) - RoadGrade.SharedEdge) / (0.5f - RoadGrade.SharedEdge));

        public float Vertex(float u, float v)
        {
            float x = Weight(u), z = Weight(v);
            float sideX = u < 0 ? _west : _east, sideZ = v < 0 ? _south : _north;
            float corner = v < 0 ? (u < 0 ? _sw : _se) : (u < 0 ? _nw : _ne);
            if (Math.Abs(u) == 0.5f) return sideX + (corner - sideX) * Corner(v);
            if (Math.Abs(v) == 0.5f) return sideZ + (corner - sideZ) * Corner(u);
            return Math.Min(sideX * x, sideZ * z);
        }

        public float RoadY(RoadGrade grade, RoadConnections links, float u, float v)
        {
            float road = grade.Sample(u, v);
            if (Math.Abs(u) <= RoadGrade.SharedEdge && Math.Abs(v) <= RoadGrade.SharedEdge) return road;
            if (Math.Abs(v) <= RoadGrade.SharedEdge && (links & (u < 0 ? RoadConnections.West : RoadConnections.East)) != 0) return road;
            if (Math.Abs(u) <= RoadGrade.SharedEdge && (links & (v < 0 ? RoadConnections.South : RoadConnections.North)) != 0) return road;
            return Math.Min(road, grade.Base + Vertex(u, v));
        }

        public float Sample(float u, float v)
        {
            if (!Active) return 0f;
            int ix = Segment(u), iz = Segment(v);
            float x0 = RoadGrade.Boundary(ix), x1 = RoadGrade.Boundary(ix + 1);
            float z0 = RoadGrade.Boundary(iz), z1 = RoadGrade.Boundary(iz + 1);
            float x = (u - x0) / (x1 - x0), z = (v - z0) / (z1 - z0);
            float a = Vertex(x0, z0), b = Vertex(x0, z1), c = Vertex(x1, z1), d = Vertex(x1, z0);
            if (RoadGrade.FlipDiagonal(ix, iz))
                return x + z <= 1f ? a + (d - a) * x + (b - a) * z : c + (b - c) * (1f - x) + (d - c) * (1f - z);
            return x <= z ? a + (c - b) * x + (b - a) * z : a + (d - a) * x + (c - d) * z;

            static int Segment(float p)
            {
                for (int i = 0; i < RoadGrade.PatchCount - 1; i++)
                    if (p <= RoadGrade.Boundary(i + 1)) return i;
                return RoadGrade.PatchCount - 1;
            }
        }
    }
}
