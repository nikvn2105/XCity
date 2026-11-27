using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class CityChunkRenderer : IDisposable
    {
        internal const float BuildingWidthRatio = 0.72f;
        internal const float BuildingBase = 0.1f;
        private const int ChunkSize = ChunkRenderScheduler.Size;
        private const int ChunkCount = ChunkRenderScheduler.Count;
        private const int FarBlockChunks = 8;
        private const int FarBlockCount = (ChunkCount + FarBlockChunks - 1) / FarBlockChunks;
        private const float RoadY = 0.048f;
        private const float CurbY = 0.106f;
        private const float PaintY = RoadY + 0.004f;
        private static readonly RoadConnections[] Directions =
        {
            RoadConnections.North, RoadConnections.East, RoadConnections.South, RoadConnections.West
        };
        private static readonly Color32 Asphalt = new Color32(62, 73, 84, 255);
        private static readonly Color32 Paving = new Color32(167, 163, 151, 255);
        private static readonly Color32 Curb = new Color32(135, 135, 127, 255);
        private static readonly Color32 Paint = new Color32(240, 231, 200, 255);
        private static readonly Color32 LanePaint = new Color32(236, 189, 98, 255);
        private static readonly Color32 GrassLight = new Color32(111, 144, 92, 255);
        private static readonly Color32 GrassDark = new Color32(79, 115, 77, 255);

        private static readonly Color32 HappinessRed = new Color32(214, 58, 52, 255);
        private static readonly Color32 HappinessYellow = new Color32(236, 198, 56, 255);
        private static readonly Color32 HappinessGreen = new Color32(66, 196, 92, 255);
        private static readonly Color32 NeutralLand = new Color32(124, 128, 134, 255);
        private static readonly Color32 NeutralLot = new Color32(142, 146, 152, 255);
        private static readonly Color32 NeutralRoad = new Color32(82, 86, 92, 255);
        private static readonly Color32 CoverageEducation = new Color32(64, 146, 232, 255);
        private static readonly Color32 CoverageHealth = new Color32(52, 202, 174, 255);
        private static readonly Color32 CoveragePolice = new Color32(94, 108, 236, 255);
        private static readonly Color32 CoverageFire = new Color32(236, 134, 52, 255);

        private readonly HashSet<Vector2Int> _dirtySurfaces = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _dirtyScenery = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _dirtyFar = new HashSet<Vector2Int>();
        private readonly Mesh[,] _surfaces = new Mesh[ChunkCount, ChunkCount];
        private readonly Mesh[,] _scenery = new Mesh[ChunkCount, ChunkCount];
        private readonly GameObject[,] _groundLayers = new GameObject[ChunkCount, ChunkCount];
        private readonly GameObject[,] _sceneryLayers = new GameObject[ChunkCount, ChunkCount];
        private readonly Mesh[,] _farMeshes = new Mesh[FarBlockCount, FarBlockCount];
        private readonly GameObject[,] _farLayers = new GameObject[FarBlockCount, FarBlockCount];
        private readonly ChunkDetail[,] _details = new ChunkDetail[ChunkCount, ChunkCount];
        private readonly CellType[,] _renderedTypes = new CellType[CitySimulation.GridSize, CitySimulation.GridSize];
        private readonly MeshBuilder _surface = new MeshBuilder();
        private readonly MeshBuilder _props = new MeshBuilder();
        private readonly MeshBuilder _farSurface = new MeshBuilder();
        private readonly CitySimulation _city;
        private readonly Material _material;
        private readonly Transform _root;
        private readonly float _cellSize;

        private bool _infoView;
        private long _lastInfoTick = -1;
        private Vector3 _eye;

        public CityChunkRenderer(CitySimulation city, float cellSize, Material material, Transform parent)
        {
            _city = city;
            _cellSize = cellSize;
            _material = material;
            _root = new GameObject("Streets and Landscape").transform;
            _root.SetParent(parent, false);
            _city.CellChanged += MarkDirty;
        }

        public bool InfoViewEnabled => _infoView;

        public void SetCameraPosition(Vector3 eye) => _eye = eye;

        public void SetInfoView(bool enabled)
        {
            if (_infoView == enabled) return;
            _infoView = enabled;
            _lastInfoTick = _city.TickCount;
            MarkAllActiveInfoDirty();
            RebuildDirtyChunks(int.MaxValue);
        }

        public void SetDetail(int x, int z, ChunkDetail detail)
        {
            ChunkDetail prev = _details[x, z];
            if (prev == detail) return;
            _details[x, z] = detail;
            var chunk = new Vector2Int(x, z);
            _dirtySurfaces.Remove(chunk);
            _dirtyScenery.Remove(chunk);

            if (detail != ChunkDetail.Full)
                ReleaseLayer(ref _scenery[x, z], ref _sceneryLayers[x, z]);

            if (detail != ChunkDetail.Full && detail != ChunkDetail.Overview)
                ReleaseLayer(ref _surfaces[x, z], ref _groundLayers[x, z]);
            else
                RebuildChunk(chunk, rebuildScenery: detail == ChunkDetail.Full);

            if (prev == ChunkDetail.Far || detail == ChunkDetail.Far)
                _dirtyFar.Add(new Vector2Int(x / FarBlockChunks, z / FarBlockChunks));
        }

        private static void ReleaseLayer(ref Mesh mesh, ref GameObject layer)
        {
            if (layer != null)
            {
                layer.SetActive(false);
                UnityEngine.Object.Destroy(layer);
                layer = null;
            }
            if (mesh != null)
            {
                UnityEngine.Object.Destroy(mesh);
                mesh = null;
            }
        }

        private void MarkDirty(int x, int z)
        {
            CellType before = _renderedTypes[x, z];
            CellType after = _city.GetCell(x, z).Type;
            bool roadTouch = before == CellType.Road || after == CellType.Road;

            if (before != after)
            {
                MarkChunk(x, z, includeScenery: true);
                if (roadTouch)
                {
                    // A connection changes the facing arm of the road across the chunk boundary too.
                    MarkChunk(x - 1, z, includeScenery: true);
                    MarkChunk(x + 1, z, includeScenery: true);
                    MarkChunk(x, z - 1, includeScenery: true);
                    MarkChunk(x, z + 1, includeScenery: true);
                }
            }
            else if (_infoView)
            {
                MarkChunk(x, z, includeScenery: false);
            }

            if (_infoView && (roadTouch || BuildingCatalog.IsPublicService(before) || BuildingCatalog.IsPublicService(after)))
                MarkAllActiveInfoDirty();
        }

        private void MarkChunk(int x, int z, bool includeScenery)
        {
            if (x < 0 || z < 0 || x >= CitySimulation.GridSize || z >= CitySimulation.GridSize) return;
            int cx = x / ChunkSize;
            int cz = z / ChunkSize;
            ChunkDetail detail = _details[cx, cz];
            if (detail == ChunkDetail.Full || detail == ChunkDetail.Overview)
            {
                var chunk = new Vector2Int(cx, cz);
                _dirtySurfaces.Add(chunk);
                if (includeScenery && detail == ChunkDetail.Full) _dirtyScenery.Add(chunk);
            }
            else if (detail == ChunkDetail.Far)
            {
                _dirtyFar.Add(new Vector2Int(cx / FarBlockChunks, cz / FarBlockChunks));
            }
        }

        private void MarkAllActiveInfoDirty()
        {
            for (int cx = 0; cx < ChunkCount; cx++)
            {
                for (int cz = 0; cz < ChunkCount; cz++)
                {
                    ChunkDetail detail = _details[cx, cz];
                    if (detail == ChunkDetail.Full || detail == ChunkDetail.Overview)
                        _dirtySurfaces.Add(new Vector2Int(cx, cz));
                    else if (detail == ChunkDetail.Far)
                        _dirtyFar.Add(new Vector2Int(cx / FarBlockChunks, cz / FarBlockChunks));
                }
            }
        }

        public void RebuildDirtyChunks(int budget)
        {
            if (_infoView && _city.TickCount != _lastInfoTick)
            {
                _lastInfoTick = _city.TickCount;
                MarkAllActiveInfoDirty();
            }

            while (_dirtySurfaces.Count > 0 && budget > 0)
            {
                budget--;
                var pending = _dirtySurfaces.GetEnumerator();
                pending.MoveNext();
                Vector2Int chunk = pending.Current;
                _dirtySurfaces.Remove(chunk);
                bool rebuildScenery = _dirtyScenery.Remove(chunk) && _details[chunk.x, chunk.y] == ChunkDetail.Full;
                RebuildChunk(chunk, rebuildScenery);
            }

            while (_dirtyFar.Count > 0 && budget > 0)
            {
                budget--;
                Vector2Int block = PopClosestFarBlock();
                RebuildFarBlock(block);
            }
        }

        private Vector2Int PopClosestFarBlock()
        {
            float blockSpan = FarBlockChunks * ChunkSize * _cellSize;
            Vector2Int best = default;
            float bestDistSq = float.MaxValue;
            foreach (Vector2Int block in _dirtyFar)
            {
                float bx = (block.x + 0.5f) * blockSpan - _eye.x;
                float bz = (block.y + 0.5f) * blockSpan - _eye.z;
                float distSq = bx * bx + bz * bz;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = block;
                }
            }
            _dirtyFar.Remove(best);
            return best;
        }

        private void RebuildChunk(Vector2Int chunk, bool rebuildScenery)
        {
            _surface.Clear();
            if (rebuildScenery) _props.Clear();
            bool full = _details[chunk.x, chunk.y] == ChunkDetail.Full;
            bool emitScenery = full && rebuildScenery;
            int startX = chunk.x * ChunkSize;
            int startZ = chunk.y * ChunkSize;
            for (int x = startX; x < Mathf.Min(startX + ChunkSize, CitySimulation.GridSize); x++)
            {
                for (int z = startZ; z < Mathf.Min(startZ + ChunkSize, CitySimulation.GridSize); z++)
                {
                    CityCell cell = _city.GetCell(x, z);
                    _renderedTypes[x, z] = cell.Type;
                    var center = new Vector3((x - startX + 0.5f) * _cellSize, 0, (z - startZ + 0.5f) * _cellSize);
                    _surface.SetFrame(center, 0);
                    if (emitScenery) _props.SetFrame(center, 0);
                    if (_city.TryGetBridgeProfile(x, z, out BridgeProfile bridge))
                    {
                        AddBridge(center, bridge, full, emitScenery, x, z);
                        continue;
                    }
                    if (!WorldTerrain.IsLand(x, z)) continue;
                    AddCoast(x, z);
                    if (cell.Type == CellType.Road)
                    {
                        if (full) AddStreet(center, x, z, emitScenery);
                        else AddOverviewStreet(center, x, z);
                    }
                    else
                        AddLot(cell, x, z, emitScenery);
                }
            }

            var origin = new Vector3(startX * _cellSize, 0, startZ * _cellSize);
            ApplyLayer(_surface, ref _surfaces[chunk.x, chunk.y], ref _groundLayers[chunk.x, chunk.y], $"Ground_{chunk.x}_{chunk.y}", origin, ShadowCastingMode.Off);
            if (emitScenery) ApplyLayer(_props, ref _scenery[chunk.x, chunk.y], ref _sceneryLayers[chunk.x, chunk.y], $"Scenery_{chunk.x}_{chunk.y}", origin, ShadowCastingMode.On);
        }

        private void RebuildFarBlock(Vector2Int block)
        {
            _farSurface.Clear();
            _farSurface.SetFrame(Vector3.zero, 0);

            int startCx = block.x * FarBlockChunks;
            int startCz = block.y * FarBlockChunks;
            int endCx = Mathf.Min(startCx + FarBlockChunks, ChunkCount);
            int endCz = Mathf.Min(startCz + FarBlockChunks, ChunkCount);
            int originX = startCx * ChunkSize;
            int originZ = startCz * ChunkSize;
            int minTileX = originX;
            int maxTileX = Mathf.Min(endCx * ChunkSize, CitySimulation.GridSize);
            int minTileZ = originZ;
            int maxTileZ = Mathf.Min(endCz * ChunkSize, CitySimulation.GridSize);

            float half = _cellSize * 0.5f;
            float lane = half * 0.67f;

            for (int x = minTileX; x < maxTileX; x++)
            {
                int cx = x / ChunkSize;
                float x0 = (x - originX) * _cellSize;
                float x1 = x0 + _cellSize;

                bool hasSpan = false;
                float spanZ0 = 0f;
                float spanZ1 = 0f;
                float spanY = 0f;
                uint spanColorKey = 0u;
                Color32 spanColor = default;

                for (int z = minTileZ; z < maxTileZ; z++)
                {
                    int cz = z / ChunkSize;
                    if (_details[cx, cz] != ChunkDetail.Far)
                    {
                        FlushSpan();
                        continue;
                    }

                    CityCell cell = _city.GetCell(x, z);
                    _renderedTypes[x, z] = cell.Type;
                    float z0 = (z - originZ) * _cellSize;
                    float z1 = z0 + _cellSize;

                    if (_city.TryGetBridgeProfile(x, z, out BridgeProfile bridge))
                    {
                        FlushSpan();
                        Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
                        float bridgeY = bridge.Middle + RoadY;
                        if (bridge.Axis == CitySimulation.BridgeAlongX)
                            _farSurface.AddFlatQuad(x0, z0 + half - lane, x1, z0 + half + lane, bridgeY, roadColor);
                        else
                            _farSurface.AddFlatQuad(x0 + half - lane, z0, x0 + half + lane, z1, bridgeY, roadColor);
                        continue;
                    }

                    if (!WorldTerrain.IsLand(x, z))
                    {
                        FlushSpan();
                        continue;
                    }

                    if (cell.Type == CellType.Road)
                    {
                        FlushSpan();
                        Color32 groundUnderRoad = _infoView ? GetInfoGroundColor(cell, x, z) : Paving;
                        _farSurface.AddFlatQuad(x0, z0, x1, z1, 0.007f, groundUnderRoad);
                        Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
                        float centerX = x0 + half;
                        float centerZ = z0 + half;
                        RoadConnections links = _city.Roads.GetConnections(x, z);
                        float rx0 = (links & RoadConnections.West) != 0 ? x0 : centerX - lane;
                        float rx1 = (links & RoadConnections.East) != 0 ? x1 : centerX + lane;
                        float rz0 = (links & RoadConnections.South) != 0 ? z0 : centerZ - lane;
                        float rz1 = (links & RoadConnections.North) != 0 ? z1 : centerZ + lane;
                        _farSurface.AddFlatQuad(rx0, centerZ - lane, rx1, centerZ + lane, RoadY, roadColor);
                        if (rz0 < centerZ - lane || rz1 > centerZ + lane)
                            _farSurface.AddFlatQuad(centerX - lane, rz0, centerX + lane, rz1, RoadY, roadColor);
                        continue;
                    }

                    float tileY = cell.Type == CellType.Empty ? 0.007f : BuildingBase;
                    Color32 tileColor = GetFarTileColor(cell, x, z);
                    uint colorKey = ((uint)tileColor.r << 16) | ((uint)tileColor.g << 8) | tileColor.b;

                    if (hasSpan && Mathf.Approximately(spanZ1, z0) && Mathf.Approximately(spanY, tileY) && spanColorKey == colorKey)
                    {
                        spanZ1 = z1;
                    }
                    else
                    {
                        FlushSpan();
                        hasSpan = true;
                        spanZ0 = z0;
                        spanZ1 = z1;
                        spanY = tileY;
                        spanColorKey = colorKey;
                        spanColor = tileColor;
                    }
                }

                FlushSpan();

                void FlushSpan()
                {
                    if (!hasSpan) return;
                    _farSurface.AddFlatQuad(x0, spanZ0, x1, spanZ1, spanY, spanColor);
                    hasSpan = false;
                }
            }

            if (_farSurface.VertexCount == 0)
            {
                ReleaseLayer(ref _farMeshes[block.x, block.y], ref _farLayers[block.x, block.y]);
                return;
            }

            var origin = new Vector3(originX * _cellSize, 0f, originZ * _cellSize);
            ApplyLayer(_farSurface, ref _farMeshes[block.x, block.y], ref _farLayers[block.x, block.y], $"GroundFar_{block.x}_{block.y}", origin, ShadowCastingMode.Off);
        }

        private void ApplyLayer(MeshBuilder builder, ref Mesh mesh, ref GameObject layer, string name, Vector3 origin, ShadowCastingMode shadows)
        {
            if (mesh == null)
            {
                if (builder.VertexCount == 0) return;
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
                layer = new GameObject(name);
                layer.transform.SetParent(_root, false);
                layer.transform.localPosition = origin;
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = shadows;
            }
            builder.ApplyTo(mesh);
        }

        private void AddBridge(Vector3 center, BridgeProfile bridge, bool full, bool emitScenery, int x, int z)
        {
            int turn = bridge.Axis == CitySimulation.BridgeAlongX ? 1 : 0;
            _surface.SetFrame(center, turn);
            if (emitScenery) _props.SetFrame(center, turn);
            float half = _cellSize * 0.5f;
            float lane = half * 0.67f;
            float edge = half * 0.94f;
            var concrete = new Color32(147, 153, 150, 255);
            var steel = new Color32(77, 94, 97, 255);
            Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
            Color32 walkwayColor = _infoView ? GetInfoGroundColor(_city.GetCell(x, z), x, z) : Paving;

            // Keep the rendered road and its traffic on the same piecewise-linear grade.
            DeckHalf(-half, 0f);
            DeckHalf(0f, half);

            if (bridge.HasNavigationOpening || (bridge.Index % 2 != 0 && bridge.Index != bridge.Count - 1))
                return;
            if (!full || emitScenery)
            {
                const float pierBase = BridgeProfile.WaterSurface - 0.30f;
                float bearing = bridge.Middle + RoadY - BridgeProfile.DeckThickness;
                MeshBuilder support = full ? _props : _surface;
                support.AddBox(new Vector3(0f, (pierBase + bearing) * 0.5f, 0f),
                    new Vector3(0.72f, bearing - pierBase, 0.70f), concrete, concrete);
                support.AddBox(new Vector3(0f, bearing - 0.09f, 0f),
                    new Vector3(edge * 1.55f, 0.18f, 0.57f), concrete, concrete);
            }

            float Grade(float zPos) => bridge.HeightAt((zPos + half) / _cellSize);

            void DeckHalf(float from, float to)
            {
                Strip(-lane, lane, from, to, RoadY, roadColor);
                for (int side = -1; side <= 1; side += 2)
                {
                    float curb = side * lane;
                    float rim = side * edge;
                    Strip(Mathf.Min(curb, rim), Mathf.Max(curb, rim), from, to, CurbY, walkwayColor);
                    float low = RoadY - BridgeProfile.DeckThickness;
                    Side(rim, from, to, low, CurbY, side * Vector3.right, concrete);
                    Side(curb, from, to, RoadY, CurbY, -side * Vector3.right, Curb);
                    if (emitScenery)
                    {
                        float rail = rim - side * 0.065f;
                        Side(rail, from, to, 0.27f, 0.33f, Vector3.right, steel);
                        Side(rail, from, to, 0.27f, 0.33f, Vector3.left, steel);
                        float post = (from + to) * 0.5f;
                        _props.AddBox(new Vector3(rail, Grade(post) + 0.21f, post),
                            new Vector3(0.061f, 0.22f, 0.065f), steel, steel);
                    }
                }

                float bottom = RoadY - BridgeProfile.DeckThickness;
                _surface.AddQuad(new Vector3(-edge, Grade(from) + bottom, from), new Vector3(-edge, Grade(to) + bottom, to),
                    new Vector3(edge, Grade(to) + bottom, to), new Vector3(edge, Grade(from) + bottom, from),
                    Vector3.down, concrete);
                if (full)
                    Strip(-0.041f, 0.041f, Mathf.Max(from, -0.31f), Mathf.Min(to, 0.31f), PaintY, LanePaint);
            }

            void Strip(float left, float right, float from, float to, float y, Color32 color)
            {
                _surface.AddQuad(new Vector3(left, Grade(from) + y, from), new Vector3(left, Grade(to) + y, to),
                    new Vector3(right, Grade(to) + y, to), new Vector3(right, Grade(from) + y, from), Vector3.up, color);
            }

            void Side(float xPos, float from, float to, float bottom, float top, Vector3 normal, Color32 color)
            {
                _surface.AddQuad(new Vector3(xPos, Grade(from) + bottom, from), new Vector3(xPos, Grade(to) + bottom, to),
                    new Vector3(xPos, Grade(to) + top, to), new Vector3(xPos, Grade(from) + top, from), normal, color);
            }
        }

        internal static void ApplyBridgeGrade(CitySimulation city, Vector2Int cell, float cellSize,
            ref Vector3 position, ref Vector3 tangent)
        {
            if (!city.TryGetBridgeProfile(cell.x, cell.y, out BridgeProfile bridge)) return;
            bool xAxis = bridge.Axis == CitySimulation.BridgeAlongX;
            float progress = Mathf.Clamp01(xAxis ? position.x / cellSize - cell.x : position.z / cellSize - cell.y);
            position.y += bridge.HeightAt(progress);
            tangent.y += (xAxis ? tangent.x : tangent.z) * bridge.SlopeAt(progress) / cellSize;
        }

        private void AddCoast(int x, int z)
        {
            if (WorldTerrain.LandHeight(x, z) > 0.13f) return;
            float edge = _cellSize * 0.5f;
            const float waterline = -1.08f;
            var sand = new Color32(164, 153, 119, 255);
            var shore = new Color32(122, 134, 106, 255);
            if (!WorldTerrain.IsLand(x + 1, z)) Cliff(edge, -edge, edge, edge, Vector3.right);
            if (!WorldTerrain.IsLand(x - 1, z)) Cliff(-edge, edge, -edge, -edge, Vector3.left);
            if (!WorldTerrain.IsLand(x, z + 1)) Cliff(edge, edge, -edge, edge, Vector3.forward);
            if (!WorldTerrain.IsLand(x, z - 1)) Cliff(-edge, -edge, edge, -edge, Vector3.back);

            void Cliff(float x0, float z0, float x1, float z1, Vector3 normal)
            {
                _surface.AddQuad(new Vector3(x0, waterline, z0), new Vector3(x1, waterline, z1),
                    new Vector3(x1, 0.007f, z1), new Vector3(x0, 0.007f, z0), normal, shore);
                Vector3 outside = normal * (_cellSize * 0.18f);
                var a = new Vector3(x0, -0.97f, z0);
                var b = new Vector3(x1, -0.97f, z1);
                _surface.AddQuad(a, b, b + outside, a + outside, Vector3.up, sand);
            }
        }

        private void AddOverviewStreet(Vector3 center, int x, int z)
        {
            RoadConnections links = _city.Roads.GetConnections(x, z);
            float half = _cellSize * 0.5f;
            float lane = half * 0.67f;
            Color32 sidewalk = _infoView ? GetInfoGroundColor(_city.GetCell(x, z), x, z) : Paving;
            Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
            Rect(_surface, -half, -half, half, half, RoadY - 0.012f, sidewalk);
            Rect(_surface, -lane, -lane, lane, lane, RoadY, roadColor);
            for (int turn = 0; turn < Directions.Length; turn++)
            {
                if ((links & Directions[turn]) == 0) continue;
                _surface.SetFrame(center, turn);
                Rect(_surface, -lane, lane, lane, half, RoadY, roadColor);
            }
        }

        private void AddStreet(Vector3 center, int x, int z, bool emitScenery)
        {
            RoadConnections links = _city.Roads.GetConnections(x, z);
            float half = _cellSize * 0.5f;
            float lane = half * 0.67f;
            float shoulder = (half + lane) * 0.5f;
            int degree = 0;
            foreach (RoadConnections direction in Directions)
                if ((links & direction) != 0) degree++;

            Color32 sidewalk = _infoView ? GetInfoGroundColor(_city.GetCell(x, z), x, z) : Paving;
            Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;

            Rect(_surface, -lane, -lane, lane, lane, RoadY, roadColor);
            for (int turn = 0; turn < Directions.Length; turn++)
            {
                _surface.SetFrame(center, turn);
                Rect(_surface, lane, lane, half, half, CurbY, sidewalk);

                bool connected = (links & Directions[turn]) != 0;
                bool rightConnected = (links & Directions[(turn + 1) % 4]) != 0;
                if (connected)
                {
                    Rect(_surface, -lane, lane, lane, half, RoadY, roadColor);
                    AddCurb(lane, lane, lane, half, Vector3.left);
                    AddCurb(-lane, half, -lane, lane, Vector3.right);

                    if (degree >= 3)
                        AddCrosswalk(lane, half);
                    else
                        Rect(_surface, -0.044f, shoulder - 0.17f, 0.044f, shoulder + 0.17f, PaintY, LanePaint);
                }
                else
                {
                    Rect(_surface, -lane, lane, lane, half, CurbY, sidewalk);
                    AddCurb(-lane, lane, lane, lane, Vector3.back);
                }

                // At the inner corner of a bend the two curb faces meet without covering the road.
                if (degree == 2 && connected && rightConnected)
                    Rect(_surface, lane + 0.12f, lane + 0.12f, half - 0.12f, half - 0.12f,
                        CurbY + 0.003f, Curb);
            }

            if (!emitScenery) return;

            uint seed = BuildingMeshFactory.Hash(x, z, 412);
            if (degree > 1 && seed % 5u == 0)
            {
                _props.SetFrame(center, (int)((seed >> 8) & 3u));
                AddStreetlight(shoulder);
            }
            else if (degree >= 2 && seed % 7u == 3u)
            {
                _props.SetFrame(center, (int)((seed >> 8) & 3u));
                var planterBase = new Vector3(shoulder, CurbY, shoulder);
                TreeMeshFactory.AddStreetTree(_props, planterBase, 0.50f, seed);
            }
        }

        private void AddCrosswalk(float lane, float half)
        {
            float near = Mathf.Lerp(lane, half, 0.32f);
            float far = Mathf.Lerp(lane, half, 0.77f);
            float stripe = lane * 0.17f;
            for (int i = -2; i <= 2; i++)
            {
                float x = i * lane * 0.34f;
                Rect(_surface, x - stripe * 0.5f, near, x + stripe * 0.5f, far, PaintY, Paint);
            }
        }

        private void AddCurb(float x0, float z0, float x1, float z1, Vector3 outward)
        {
            _surface.AddQuad(new Vector3(x0, RoadY, z0), new Vector3(x1, RoadY, z1),
                new Vector3(x1, CurbY, z1), new Vector3(x0, CurbY, z0), outward, Curb);
        }

        private void AddStreetlight(float offset)
        {
            var metal = new Color32(52, 72, 79, 255);
            _props.AddBox(new Vector3(offset, 0.17f, offset), new Vector3(0.19f, 0.14f, 0.19f), Curb, Paving);
            _props.AddBox(new Vector3(offset, 1.10f, offset), new Vector3(0.08f, 1.84f, 0.08f), metal, metal);
            _props.AddBox(new Vector3(offset - 0.18f, 2.04f, offset), new Vector3(0.43f, 0.09f, 0.08f), metal, metal);
            _props.AddBox(new Vector3(offset - 0.39f, 2.02f, offset), new Vector3(0.24f, 0.10f, 0.16f), Paint, metal);
        }

        private void AddLot(CityCell cell, int x, int z, bool emitScenery)
        {
            CellType type = cell.Type;
            float half = _cellSize * 0.5f;
            Color32 ground = _infoView
                ? GetInfoGroundColor(cell, x, z)
                : Color32.Lerp(GrassDark, GrassLight, WorldTerrain.Vegetation(x, z));
            Rect(_surface, -half, -half, half, half, 0.007f, ground);

            if (type != CellType.Empty)
            {
                float plotWidth = _cellSize * 0.95f;
                Color32 plot = _infoView ? GetInfoLotColor(cell, x, z) : GetStandardPlotColor(type);
                _surface.AddBox(new Vector3(0, BuildingBase * 0.5f, 0),
                    new Vector3(plotWidth, BuildingBase, plotWidth), Curb, plot);
                return;
            }

            if (!emitScenery) return;

            uint seed = BuildingMeshFactory.Hash(x, z, 921);
            float grove = WorldTerrain.Vegetation(x, z);
            float threshold = _city.Roads.HasRoadAccess(x, z) ? 0.53f : 0.39f;
            if (grove > threshold && seed % 5u < 3u)
            {
                float dx = ((seed >> 8 & 255u) / 255f - 0.5f) * half * 0.85f;
                float dz = ((seed >> 16 & 255u) / 255f - 0.5f) * half * 0.85f;
                float size = 0.78f + (seed >> 24 & 255u) / 255f * 0.44f;
                TreeMeshFactory.AddWildTree(_props, new Vector3(dx, 0.008f, dz), size, seed, grove);
                if (grove > 0.62f && seed % 3u == 0)
                {
                    float sdx = dx * -0.55f;
                    float sdz = dz * -0.55f + (dz > 0 ? -0.67f : 0.67f);
                    TreeMeshFactory.AddWildTree(_props, new Vector3(sdx, 0.008f, sdz), size * 0.64f, seed ^ 0x9E3779B9u, grove + 0.12f);
                }
            }
            else if (seed % 19u == 0)
            {
                var rock = new Color32(138, 149, 132, 255);
                _props.AddCylinder(new Vector3(0.37f, 0.01f, -0.44f), 0.34f, 0.22f, 0.22f, 5, rock, true, Paving);
            }
        }

        private Color32 GetFarTileColor(CityCell cell, int x, int z)
        {
            if (_infoView) return GetInfoLotColor(cell, x, z);
            if (cell.Type != CellType.Empty) return GetStandardPlotColor(cell.Type);
            float quantizedVeg = Mathf.Round(WorldTerrain.Vegetation(x, z) * 6f) / 6f;
            return Color32.Lerp(GrassDark, GrassLight, quantizedVeg);
        }

        private static Color32 GetStandardPlotColor(CellType type) => type switch
        {
            CellType.Residential => new Color32(105, 153, 106, 255),
            CellType.Commercial => new Color32(126, 151, 161, 255),
            CellType.Industrial => new Color32(163, 146, 113, 255),
            CellType.School => new Color32(136, 151, 116, 255),
            CellType.CivicService => new Color32(155, 159, 158, 255),
            _ => new Color32(146, 145, 144, 255)
        };

        private Color32 GetInfoGroundColor(CityCell cell, int x, int z)
        {
            if (cell.Type == CellType.Residential && cell.Level > 0)
                return GetHappinessColor(x, z);
            return ApplyCoverageTint(NeutralLand, _city.GetCoverage(x, z), 0.50f);
        }

        private Color32 GetInfoLotColor(CityCell cell, int x, int z)
        {
            if (cell.Type == CellType.Residential && cell.Level > 0)
                return GetHappinessColor(x, z);

            if (cell.Type == CellType.School)
                return Color32.Lerp(NeutralLot, CoverageEducation, 0.85f);

            if (cell.Type == CellType.CivicService)
            {
                Color32 badge = cell.Model switch
                {
                    BuildingModel.Hospital => CoverageHealth,
                    BuildingModel.PoliceStation => CoveragePolice,
                    BuildingModel.FireStation => CoverageFire,
                    _ => CoverageHealth
                };
                return Color32.Lerp(NeutralLot, badge, 0.85f);
            }

            Color32 baseGrey = cell.Type == CellType.Empty ? NeutralLand : NeutralLot;
            return ApplyCoverageTint(baseGrey, _city.GetCoverage(x, z), 0.50f);
        }

        private Color32 GetInfoRoadColor(int x, int z) =>
            ApplyCoverageTint(NeutralRoad, _city.GetCoverage(x, z), 0.25f);

        private Color32 GetHappinessColor(int x, int z)
        {
            int happiness = _city.GetHappiness(x, z);
            float t = Mathf.InverseLerp(40f, 100f, happiness);
            return t < 0.5f
                ? Color32.Lerp(HappinessRed, HappinessYellow, t * 2f)
                : Color32.Lerp(HappinessYellow, HappinessGreen, (t - 0.5f) * 2f);
        }

        private static Color32 ApplyCoverageTint(Color32 baseGrey, ServiceCoverage coverage, float strength)
        {
            if (coverage == ServiceCoverage.None) return baseGrey;

            int r = 0;
            int g = 0;
            int b = 0;
            int count = 0;
            if ((coverage & ServiceCoverage.Education) != 0)
            {
                r += CoverageEducation.r;
                g += CoverageEducation.g;
                b += CoverageEducation.b;
                count++;
            }
            if ((coverage & ServiceCoverage.Health) != 0)
            {
                r += CoverageHealth.r;
                g += CoverageHealth.g;
                b += CoverageHealth.b;
                count++;
            }
            if ((coverage & ServiceCoverage.Police) != 0)
            {
                r += CoveragePolice.r;
                g += CoveragePolice.g;
                b += CoveragePolice.b;
                count++;
            }
            if ((coverage & ServiceCoverage.Fire) != 0)
            {
                r += CoverageFire.r;
                g += CoverageFire.g;
                b += CoverageFire.b;
                count++;
            }

            if (count == 0) return baseGrey;
            var avg = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
            float weight = Mathf.Clamp01(strength + (count - 1) * 0.10f);
            return Color32.Lerp(baseGrey, avg, weight);
        }

        private static void Rect(MeshBuilder mesh, float x0, float z0, float x1, float z1, float y, Color32 color)
        {
            mesh.AddQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z1),
                new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, color);
        }

        public void Dispose()
        {
            _city.CellChanged -= MarkDirty;
            foreach (Mesh mesh in _surfaces)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (Mesh mesh in _scenery)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            foreach (Mesh mesh in _farMeshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
