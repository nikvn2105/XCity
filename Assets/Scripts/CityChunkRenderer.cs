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
        internal const float RoadY = 0.048f;
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
        private static readonly Color32 MeadowWarm = new Color32(130, 152, 88, 255);
        private static readonly Color32 BeachSand = new Color32(200, 184, 142, 255);
        private static readonly Color32 BeachWetSand = new Color32(168, 152, 114, 255);
        private static readonly Color32 BankEarth = new Color32(124, 134, 110, 255);
        private static readonly Color32 FoamWhite = new Color32(226, 236, 240, 255);

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
        private readonly Texture2D _shoreTexture;

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
            _shoreTexture = CreateShoreTexture();
            Shader.SetGlobalTexture("_XCityShoreTex", _shoreTexture);
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
                    float elevation = WorldTerrain.SurfaceY(x, z);
                    var center = new Vector3((x - startX + 0.5f) * _cellSize, elevation, (z - startZ + 0.5f) * _cellSize);
                    _surface.SetFrame(center, 0);
                    if (emitScenery) _props.SetFrame(center, 0);
                    AddFreshWater(_surface, x, z, elevation);
                    if (_city.TryGetBridgeProfile(x, z, out BridgeProfile bridge))
                    {
                        center.y = 0f; // Bridge profiles already include bank elevation.
                        AddBridge(center, bridge, full, emitScenery, x, z);
                        continue;
                    }
                    if (!WorldTerrain.IsLand(x, z)) continue;
                    RoadGrade grade = _city.GetRoadGrade(x, z);
                    AddTerraceWalls(_surface, x, z, grade, includeBanks: false);
                    AddCoast(x, z, cell.Type);
                    if (cell.Type == CellType.Road)
                    {
                        if (grade.IsRamp) AddRamp(_surface, grade, x, z);
                        else if (full) AddStreet(center, x, z, emitScenery);
                        else AddOverviewStreet(center, x, z);
                    }
                    else
                        AddLot(cell, x, z, full, emitScenery);
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

                    float elevation = WorldTerrain.SurfaceY(x, z);
                    _farSurface.SetFrame(new Vector3(x0 + half, elevation, z0 + half), 0);
                    AddFreshWater(_farSurface, x, z, elevation);
                    RoadGrade grade = _city.GetRoadGrade(x, z);
                    if (WorldTerrain.IsLand(x, z)) AddTerraceWalls(_farSurface, x, z, grade, includeBanks: true);
                    if (grade.IsRamp && !cell.IsBridge)
                    {
                        _farSurface.SetFrame(Vector3.zero, 0);
                        FlushSpan();
                        _farSurface.SetFrame(new Vector3(x0 + half, elevation, z0 + half), 0);
                        AddRamp(_farSurface, grade, x, z);
                    }
                    _farSurface.SetFrame(Vector3.zero, 0);
                    if (grade.IsRamp && !cell.IsBridge) continue;

                    if (_city.TryGetBridgeProfile(x, z, out BridgeProfile bridge))
                    {
                        FlushSpan();
                        Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
                        int turn = bridge.Axis == CitySimulation.BridgeAlongX ? 1 : 0;
                        _farSurface.SetFrame(new Vector3(x0 + half, 0f, z0 + half), turn);
                        Strip(-half, 0f, bridge.Start + RoadY, bridge.Middle + RoadY);
                        Strip(0f, half, bridge.Middle + RoadY, bridge.End + RoadY);
                        _farSurface.SetFrame(Vector3.zero, 0);

                        void Strip(float from, float to, float lower, float upper) =>
                            _farSurface.AddQuad(new Vector3(-lane, lower, from), new Vector3(-lane, upper, to),
                                new Vector3(lane, upper, to), new Vector3(lane, lower, from), Vector3.up, roadColor);
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
                        _farSurface.AddFlatQuad(x0, z0, x1, z1, elevation + 0.007f, groundUnderRoad);
                        Color32 roadColor = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
                        float centerX = x0 + half;
                        float centerZ = z0 + half;
                        RoadConnections links = _city.Roads.GetConnections(x, z);
                        float rx0 = (links & RoadConnections.West) != 0 ? x0 : centerX - lane;
                        float rx1 = (links & RoadConnections.East) != 0 ? x1 : centerX + lane;
                        float rz0 = (links & RoadConnections.South) != 0 ? z0 : centerZ - lane;
                        float rz1 = (links & RoadConnections.North) != 0 ? z1 : centerZ + lane;
                        _farSurface.AddFlatQuad(rx0, centerZ - lane, rx1, centerZ + lane, elevation + RoadY, roadColor);
                        if (rz0 < centerZ - lane || rz1 > centerZ + lane)
                            _farSurface.AddFlatQuad(centerX - lane, rz0, centerX + lane, rz1, elevation + RoadY, roadColor);
                        continue;
                    }

                    float tileY = elevation + (cell.Type == CellType.Empty ? 0.007f : BuildingBase);
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

            if (full && emitScenery && bridge.HasNavigationOpening &&
                Mathf.Abs(bridge.Index + 0.5f - bridge.Count * 0.5f) <= 0.55f)
            {
                var channelGreen = new Color32(58, 242, 134, 0);
                float beaconY = bridge.Middle + RoadY - BridgeProfile.DeckThickness - 0.06f;
                _props.AddBox(new Vector3(-edge, beaconY, 0f), new Vector3(0.14f, 0.12f, 0.24f), channelGreen, channelGreen);
                _props.AddBox(new Vector3(edge, beaconY, 0f), new Vector3(0.14f, 0.12f, 0.24f), channelGreen, channelGreen);
            }

            bool isGatewayTower = full && bridge.Count >= 5 && !bridge.HasNavigationOpening &&
                (IsNavigationIndex(bridge.Index - 1, bridge.Count) || IsNavigationIndex(bridge.Index + 1, bridge.Count));
            if (bridge.HasNavigationOpening || (!isGatewayTower && bridge.Index % 2 != 0 && bridge.Index != bridge.Count - 1))
                return;
            if (!full || emitScenery)
            {
                float pierBase = WorldTerrain.SurfaceY(x, z) + BridgeProfile.WaterSurface - 0.30f;
                float bearing = bridge.Middle + RoadY - BridgeProfile.DeckThickness;
                MeshBuilder support = full ? _props : _surface;
                support.AddBox(new Vector3(0f, (pierBase + bearing) * 0.5f, 0f),
                    new Vector3(0.72f, bearing - pierBase, 0.70f), concrete, concrete);
                support.AddBox(new Vector3(0f, bearing - 0.09f, 0f),
                    new Vector3(edge * 1.55f, 0.18f, 0.57f), concrete, concrete);
                // Towers are scenery props, so they follow the same emitScenery split as the piers.
                if (isGatewayTower && emitScenery)
                    AddBridgeTowers(bridge.Middle, edge, bearing, concrete, steel);
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

        internal static void ApplyRoadGrade(CitySimulation city, Vector2Int cell, float cellSize,
            ref Vector3 position, ref Vector3 tangent)
        {
            if (!city.TryGetBridgeProfile(cell.x, cell.y, out BridgeProfile bridge))
            {
                RoadGrade grade = city.GetRoadGrade(cell.x, cell.y);
                float u = position.x / cellSize - cell.x - 0.5f;
                float v = position.z / cellSize - cell.y - 0.5f;
                float surface = grade.Sample(u, v);
                position.y += surface;
                if (grade.IsRamp)
                {
                    const float step = 0.01f;
                    float ahead = grade.Sample(u + tangent.x * step, v + tangent.z * step);
                    tangent.y += (ahead - surface) / (cellSize * step);
                }
                return;
            }
            bool xAxis = bridge.Axis == CitySimulation.BridgeAlongX;
            float progress = Mathf.Clamp01(xAxis ? position.x / cellSize - cell.x : position.z / cellSize - cell.y);
            position.y += bridge.HeightAt(progress);
            tangent.y += (xAxis ? tangent.x : tangent.z) * bridge.SlopeAt(progress) / cellSize;
        }

        private void AddBridgeTowers(float deckY, float edge, float bearing, Color32 concrete, Color32 steel)
        {
            float towerTop = deckY + 3.85f;
            float pylonX = edge - 0.05f;
            var cableSilver = new Color32(198, 206, 214, 255);
            var aviationRed = new Color32(255, 58, 52, 0);

            for (int side = -1; side <= 1; side += 2)
            {
                float px = side * pylonX;
                float lowerTop = deckY + 1.1f;
                _props.AddBox(new Vector3(px, (bearing + lowerTop) * 0.5f, 0f),
                    new Vector3(0.24f, lowerTop - bearing, 0.28f), concrete, concrete);
                _props.AddBox(new Vector3(px, (lowerTop + towerTop) * 0.5f, 0f),
                    new Vector3(0.18f, towerTop - lowerTop, 0.22f), steel, steel);
                _props.AddBox(new Vector3(px, towerTop + 0.09f, 0f),
                    new Vector3(0.11f, 0.12f, 0.11f), aviationRed, aviationRed);

                // Stay cables fanning along the bridge deck edge without obstructing traffic lanes.
                for (int c = 1; c <= 3; c++)
                {
                    float reach = c * 0.60f;
                    float anchorY = deckY + 3.55f - c * 0.22f;
                    float deckAnchorY = deckY + 0.30f;
                    AddStayCable(px, anchorY, reach, deckAnchorY, cableSilver);
                    AddStayCable(px, anchorY, -reach, deckAnchorY, cableSilver);
                }
            }

            // High portal crossbeam clear above all trucks/buses.
            _props.AddBox(new Vector3(0f, deckY + 3.15f, 0f),
                new Vector3(pylonX * 2f - 0.12f, 0.22f, 0.20f), steel, concrete);
        }

        private void AddStayCable(float x, float topY, float zAnchor, float botY, Color32 color)
        {
            const float hw = 0.024f;
            _props.AddQuad(
                new Vector3(x - hw, topY, 0f),
                new Vector3(x + hw, topY, 0f),
                new Vector3(x + hw, botY, zAnchor),
                new Vector3(x - hw, botY, zAnchor),
                Vector3.up, color);
        }

        private static bool IsNavigationIndex(int index, int count) =>
            index >= 0 && index < count && Math.Abs(index + 0.5f - count * 0.5f) <= 1.5f;

        private void AddCoast(int x, int z, CellType cellType)
        {
            float landH = WorldTerrain.LandHeight(x, z);
            bool waterE = !WorldTerrain.IsLand(x + 1, z);
            bool waterW = !WorldTerrain.IsLand(x - 1, z);
            bool waterN = !WorldTerrain.IsLand(x, z + 1);
            bool waterS = !WorldTerrain.IsLand(x, z - 1);
            bool waterNE = !WorldTerrain.IsLand(x + 1, z + 1);
            bool waterNW = !WorldTerrain.IsLand(x - 1, z + 1);
            bool waterSE = !WorldTerrain.IsLand(x + 1, z - 1);
            bool waterSW = !WorldTerrain.IsLand(x - 1, z - 1);
            if (!(waterE || waterW || waterN || waterS || waterNE || waterNW || waterSE || waterSW))
                return;

            float edge = _cellSize * 0.5f;
            const float bankTop = 0.009f;
            const float foamY = -1.024f;
            bool lowBeach = landH < 0.14f;
            bool seaE = SeaAt(x + 1, z), seaW = SeaAt(x - 1, z);
            bool seaN = SeaAt(x, z + 1), seaS = SeaAt(x, z - 1);
            Color32 bankColor = lowBeach ? BeachWetSand : BankEarth;

            if (waterE) CoastEdge(edge, -edge, edge, edge, Vector3.right, x + 1, z);
            if (waterW) CoastEdge(-edge, edge, -edge, -edge, Vector3.left, x - 1, z);
            if (waterN) CoastEdge(edge, edge, -edge, edge, Vector3.forward, x, z + 1);
            if (waterS) CoastEdge(-edge, -edge, edge, -edge, Vector3.back, x, z - 1);

            if (!_infoView && landH < 0.22f)
            {
                float sandWidth = cellType == CellType.Empty ? _cellSize * 0.36f : 0.28f;
                float shade = (BuildingMeshFactory.Hash(x, z, 601) & 15u) / 15f;
                Color32 sandCol = Color32.Lerp(BeachWetSand, BeachSand, 0.72f + shade * 0.28f);
                const float sandY = 0.022f;

                if (seaE) Rect(_surface, edge - sandWidth, -edge, edge, edge, sandY, sandCol);
                if (seaW) Rect(_surface, -edge, -edge, -edge + sandWidth, edge, sandY, sandCol);
                if (seaN) Rect(_surface, -edge, edge - sandWidth, edge, edge, sandY, sandCol);
                if (seaS) Rect(_surface, -edge, -edge, edge, -edge + sandWidth, sandY, sandCol);

                if (cellType == CellType.Empty)
                {
                    if (waterNE && SeaAt(x + 1, z + 1) && !waterN && !waterE) Rect(_surface, edge - sandWidth, edge - sandWidth, edge, edge, sandY, sandCol);
                    if (waterNW && SeaAt(x - 1, z + 1) && !waterN && !waterW) Rect(_surface, -edge, edge - sandWidth, -edge + sandWidth, edge, sandY, sandCol);
                    if (waterSE && SeaAt(x + 1, z - 1) && !waterS && !waterE) Rect(_surface, edge - sandWidth, -edge, edge, -edge + sandWidth, sandY, sandCol);
                    if (waterSW && SeaAt(x - 1, z - 1) && !waterS && !waterW) Rect(_surface, -edge, -edge, -edge + sandWidth, -edge + sandWidth, sandY, sandCol);
                }
            }

            bool SeaAt(int bx, int bz) => WorldTerrain.WaterKind(bx, bz) == WaterKind.Sea;

            void CoastEdge(float x0, float z0, float x1, float z1, Vector3 normal, int nx, int nz)
            {
                bool sea = SeaAt(nx, nz);
                float waterline = -1.14f + WorldTerrain.SurfaceY(nx, nz) - WorldTerrain.SurfaceY(x, z);
                if (sea && landH > 0.35f) return;
                Vector3 bankOut = normal * 0.38f;
                Vector3 foamInner = normal * 0.16f;
                Vector3 foamOuter = normal * 0.40f;
                Vector3 slopeNormal = (normal + Vector3.up * 0.65f).normalized;

                var topA = new Vector3(x0, bankTop, z0);
                var topB = new Vector3(x1, bankTop, z1);
                var botA = new Vector3(x0, waterline, z0) + bankOut;
                var botB = new Vector3(x1, waterline, z1) + bankOut;
                _surface.AddQuad(botA, botB, topB, topA, slopeNormal, sea ? bankColor : new Color32(78, 113, 60, 255));
                if (!sea) return;

                var f0 = new Vector3(x0, foamY, z0) + foamInner;
                var f1 = new Vector3(x1, foamY, z1) + foamInner;
                _surface.AddQuad(f0, f1, new Vector3(x1, foamY, z1) + foamOuter, new Vector3(x0, foamY, z0) + foamOuter,
                    Vector3.up, FoamWhite);
            }
        }

        private void AddFreshWater(MeshBuilder mesh, int x, int z, float elevation)
        {
            // Tier zero already has the ocean plane. Elevated freshwater shares the ground mesh.
            if (elevation == 0f || !WorldTerrain.IsFreshWater(x, z)) return;
            float half = _cellSize * 0.5f;
            const float water = BridgeProfile.WaterSurface;
            var tint = new Color32(55, 119, 146, 255);
            mesh.AddFlatQuad(-half, -half, half, half, water, tint);
            Drop(x + 1, z, half, -half, half, half, Vector3.right);
            Drop(x - 1, z, -half, half, -half, -half, Vector3.left);
            Drop(x, z + 1, half, half, -half, half, Vector3.forward);
            Drop(x, z - 1, -half, -half, half, -half, Vector3.back);

            void Drop(int nx, int nz, float x0, float z0, float x1, float z1, Vector3 normal)
            {
                float lower = water + WorldTerrain.SurfaceY(nx, nz) - elevation;
                if (!WorldTerrain.IsFreshWater(nx, nz) || lower >= water) return;
                mesh.AddQuad(new Vector3(x0, lower, z0), new Vector3(x1, lower, z1), new Vector3(x1, water, z1),
                    new Vector3(x0, water, z0), normal, tint);
            }
        }

        private void AddTerraceWalls(MeshBuilder mesh, int x, int z, RoadGrade grade, bool includeBanks)
        {
            if (!WorldTerrain.IsSlope(x, z)) return;
            Edge(x + 1, z, true, 0.5f, Vector3.right);
            Edge(x - 1, z, true, -0.5f, Vector3.left);
            Edge(x, z + 1, false, 0.5f, Vector3.forward);
            Edge(x, z - 1, false, -0.5f, Vector3.back);

            void Edge(int nx, int nz, bool alongZ, float fixedCoord, Vector3 normal)
            {
                bool land = WorldTerrain.IsLand(nx, nz);
                float lower = WorldTerrain.SurfaceY(nx, nz);
                if (lower >= grade.Base || (!land && !includeBanks)) return;
                RoadGrade other = land ? _city.GetRoadGrade(nx, nz) : new RoadGrade(lower + BridgeProfile.WaterSurface);
                float offset = land ? 0.007f : 0f;
                var stone = new Color32(108, 103, 91, 255);
                float edge = alongZ ? (fixedCoord < 0 ? grade.West : grade.East) :
                    (fixedCoord < 0 ? grade.South : grade.North);
                if (edge == 0f)
                    Wall(-0.5f, 0.5f);
                else
                {
                    // Leave the connected road lane open; only its tapered shoulders retain rock.
                    Wall(-0.5f, -RoadGrade.SharedEdge);
                    Wall(RoadGrade.SharedEdge, 0.5f);
                }

                void Wall(float from, float to) =>
                    mesh.AddQuad(Point(from, other, -fixedCoord), Point(to, other, -fixedCoord),
                        Point(to, grade, fixedCoord), Point(from, grade, fixedCoord), normal, stone);

                Vector3 Point(float at, RoadGrade surface, float sample) => alongZ
                    ? new Vector3(fixedCoord * _cellSize, surface.Sample(sample, at) - grade.Base + offset, at * _cellSize)
                    : new Vector3(at * _cellSize, surface.Sample(at, sample) - grade.Base + offset, fixedCoord * _cellSize);
            }
        }

        private void AddRamp(MeshBuilder mesh, RoadGrade grade, int x, int z)
        {
            Color32 paving = _infoView ? GetInfoGroundColor(_city.GetCell(x, z), x, z) : Paving;
            Color32 road = _infoView ? GetInfoRoadColor(x, z) : Asphalt;
            RoadConnections links = _city.Roads.GetConnections(x, z);
            for (int ix = 0; ix < RoadGrade.PatchCount; ix++)
            for (int iz = 0; iz < RoadGrade.PatchCount; iz++)
            {
                bool inX = ix >= 1 && ix < RoadGrade.PatchCount - 1;
                bool inZ = iz >= 1 && iz < RoadGrade.PatchCount - 1;
                bool driveable = (inX && inZ) || (inZ && (links & (ix == 0 ? RoadConnections.West : RoadConnections.East)) != 0) ||
                    (inX && (links & (iz == 0 ? RoadConnections.South : RoadConnections.North)) != 0);
                float x0 = RoadGrade.Boundary(ix), x1 = RoadGrade.Boundary(ix + 1);
                float z0 = RoadGrade.Boundary(iz), z1 = RoadGrade.Boundary(iz + 1);
                Vector3 a = Point(x0, z0), b = Point(x0, z1), c = Point(x1, z1), d = Point(x1, z0);
                if (RoadGrade.FlipDiagonal(ix, iz))
                    mesh.AddQuad(b, c, d, a, Vector3.up, paving);
                else
                    mesh.AddQuad(a, b, c, d, Vector3.up, driveable ? road : paving);
            }

            Vector3 Point(float u, float v) => new Vector3(u * _cellSize,
                grade.Sample(u, v) - grade.Base + RoadY, v * _cellSize);
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
            var lampWarm = new Color32(255, 240, 198, 0);
            _props.AddBox(new Vector3(offset, 0.17f, offset), new Vector3(0.19f, 0.14f, 0.19f), Curb, Paving);
            _props.AddBox(new Vector3(offset, 1.10f, offset), new Vector3(0.08f, 1.84f, 0.08f), metal, metal);
            _props.AddBox(new Vector3(offset - 0.18f, 2.04f, offset), new Vector3(0.43f, 0.09f, 0.08f), metal, metal);
            _props.AddBox(new Vector3(offset - 0.39f, 2.02f, offset), new Vector3(0.24f, 0.10f, 0.16f), lampWarm, metal);
        }

        private void AddLot(CityCell cell, int x, int z, bool full, bool emitScenery)
        {
            CellType type = cell.Type;
            float half = _cellSize * 0.5f;
            Color32 ground = _infoView
                ? GetInfoGroundColor(cell, x, z)
                : GetNaturalGroundColor(x, z);
            Rect(_surface, -half, -half, half, half, 0.007f, ground);

            if (type != CellType.Empty)
            {
                float plotWidth = _cellSize * 0.95f;
                Color32 plot = _infoView ? GetInfoLotColor(cell, x, z) : GetStandardPlotColor(type);
                _surface.AddBox(new Vector3(0, BuildingBase * 0.5f, 0),
                    new Vector3(plotWidth, BuildingBase, plotWidth), Curb, plot);
                return;
            }

            if (!full) return;
            uint seed = BuildingMeshFactory.Hash(x, z, 921);
            if (_city.Roads.HasRoadAccess(x, z) && CountAdjacentBuildings(x, z) >= 2)
            {
                AddPocketPark(half, seed, emitScenery);
                return;
            }
            if (!emitScenery) return;

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

        private int CountAdjacentBuildings(int x, int z)
        {
            int count = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0);
                int nz = z + (d == 2 ? -1 : d == 3 ? 1 : 0);
                if (nx >= 0 && nz >= 0 && nx < CitySimulation.GridSize && nz < CitySimulation.GridSize &&
                    _city.GetCell(nx, nz).Height > 0f)
                    count++;
            }
            return count;
        }

        private void AddPocketPark(float half, uint seed, bool emitScenery)
        {
            var wood = new Color32(146, 102, 62, 255);
            var fountainWater = new Color32(94, 184, 212, 255);
            var spray = new Color32(236, 248, 254, 255);
            var lamp = new Color32(255, 240, 198, 0);

            // Cross stone walkways
            Rect(_surface, -0.30f, -half * 0.92f, 0.30f, half * 0.92f, 0.026f, Paving);
            Rect(_surface, -half * 0.92f, -0.30f, half * 0.92f, 0.30f, 0.026f, Paving);
            _surface.AddCylinder(new Vector3(0f, 0.026f, 0f), 0.72f, 0.72f, 0.04f, 8, Paving, true, Curb);
            if (!emitScenery) return;

            // Fountain plaza vs planter court
            if ((seed & 1u) == 0u)
            {
                _props.AddCylinder(new Vector3(0f, 0.06f, 0f), 0.56f, 0.56f, 0.16f, 8, Concrete(), true, fountainWater);
                _props.AddCylinder(new Vector3(0f, 0.20f, 0f), 0.12f, 0.06f, 0.28f, 6, spray, true, spray);
            }
            else
            {
                _props.AddBox(new Vector3(0f, 0.14f, 0f), new Vector3(0.84f, 0.16f, 0.84f), Curb, GrassLight);
                TreeMeshFactory.AddStreetTree(_props, new Vector3(0f, 0.22f, 0f), 0.62f, seed);
            }

            // Wooden benches & park lantern
            _props.AddBox(new Vector3(-0.78f, 0.11f, 0.44f), new Vector3(0.42f, 0.12f, 0.16f), wood, wood);
            _props.AddBox(new Vector3(0.78f, 0.11f, -0.44f), new Vector3(0.42f, 0.12f, 0.16f), wood, wood);
            _props.AddBox(new Vector3(-0.78f, 0.52f, -0.78f), new Vector3(0.06f, 0.96f, 0.06f), Curb, Curb);
            _props.AddBox(new Vector3(-0.78f, 1.04f, -0.78f), new Vector3(0.14f, 0.14f, 0.14f), lamp, lamp);

            // Corner park trees
            TreeMeshFactory.AddWildTree(_props, new Vector3(1.18f, 0.02f, 1.18f), 0.58f, seed ^ 0x517u, 0.4f);
            TreeMeshFactory.AddWildTree(_props, new Vector3(-1.18f, 0.02f, -1.18f), 0.54f, seed ^ 0xA23u, 0.7f);
            if ((seed & 2u) == 0u)
                TreeMeshFactory.AddWildTree(_props, new Vector3(1.18f, 0.02f, -1.18f), 0.52f, seed ^ 0x7E9u, 0.6f);
        }

        private static Color32 Concrete() => new Color32(182, 184, 178, 255);

        private Color32 GetFarTileColor(CityCell cell, int x, int z)
        {
            if (_infoView) return GetInfoLotColor(cell, x, z);
            if (cell.Type != CellType.Empty) return GetStandardPlotColor(cell.Type);
            if (!WorldTerrain.IsLand(x + 1, z) || !WorldTerrain.IsLand(x - 1, z) ||
                !WorldTerrain.IsLand(x, z + 1) || !WorldTerrain.IsLand(x, z - 1))
                return Color32.Lerp(GrassLight, BeachSand, 0.72f);

            float veg = Mathf.Round(WorldTerrain.Vegetation(x, z) * 6f) / 6f;
            float meadow = Mathf.Round(Mathf.Clamp01(WorldTerrain.LandHeight(x, z) * 1.4f) * 3f) / 3f;
            Color32 baseGrass = Color32.Lerp(GrassDark, GrassLight, veg);
            return Color32.Lerp(baseGrass, MeadowWarm, meadow * 0.25f);
        }

        private static Color32 GetNaturalGroundColor(int x, int z)
        {
            float veg = WorldTerrain.Vegetation(x, z);
            float h = WorldTerrain.LandHeight(x, z);
            Color32 col = Color32.Lerp(GrassDark, GrassLight, veg);
            float meadow = Mathf.Clamp01((veg - 0.35f) * 1.6f) * Mathf.Clamp01(h * 1.6f);
            col = Color32.Lerp(col, MeadowWarm, meadow * 0.30f);
            if (h < 0.09f)
                col = Color32.Lerp(col, BeachSand, (0.09f - h) * 3.2f);

            uint hash = BuildingMeshFactory.Hash(x, z, 313);
            int jitter = (int)(hash % 11u) - 5;
            return new Color32(
                (byte)Mathf.Clamp(col.r + jitter, 0, 255),
                (byte)Mathf.Clamp(col.g + jitter, 0, 255),
                (byte)Mathf.Clamp(col.b + jitter / 2, 0, 255),
                255);
        }

        private static Texture2D CreateShoreTexture()
        {
            const int size = CitySimulation.GridSize;
            const float maxDist = 11f;
            const float diag = 1.4142135f;
            var dist = new float[size * size];

            for (int z = 0; z < size; z++)
            {
                int row = z * size;
                for (int x = 0; x < size; x++)
                    dist[row + x] = WorldTerrain.IsLand(x, z) ? 0f : maxDist + 1f;
            }

            for (int z = 0; z < size; z++)
            {
                int row = z * size;
                for (int x = 0; x < size; x++)
                {
                    int idx = row + x;
                    float d = dist[idx];
                    if (d <= 0f) continue;
                    if (x > 0) d = Mathf.Min(d, dist[idx - 1] + 1f);
                    if (z > 0)
                    {
                        int prev = (z - 1) * size + x;
                        d = Mathf.Min(d, dist[prev] + 1f);
                        if (x > 0) d = Mathf.Min(d, dist[prev - 1] + diag);
                        if (x < size - 1) d = Mathf.Min(d, dist[prev + 1] + diag);
                    }
                    dist[idx] = d;
                }
            }

            for (int z = size - 1; z >= 0; z--)
            {
                int row = z * size;
                for (int x = size - 1; x >= 0; x--)
                {
                    int idx = row + x;
                    float d = dist[idx];
                    if (d <= 0f) continue;
                    if (x < size - 1) d = Mathf.Min(d, dist[idx + 1] + 1f);
                    if (z < size - 1)
                    {
                        int next = (z + 1) * size + x;
                        d = Mathf.Min(d, dist[next] + 1f);
                        if (x < size - 1) d = Mathf.Min(d, dist[next + 1] + diag);
                        if (x > 0) d = Mathf.Min(d, dist[next - 1] + diag);
                    }
                    dist[idx] = d;
                }
            }

            var pixels = new byte[size * size];
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                if (WorldTerrain.IsFreshWater(x, z)) continue; // No surf foam on inland water.
                int i = z * size + x;
                pixels[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - dist[i] / maxDist) * 255f);
            }

            var tex = new Texture2D(size, size, TextureFormat.R8, mipChain: false, linear: true)
            {
                name = "ShoreDistance",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.LoadRawTextureData(pixels);
            tex.Apply(false, true);
            return tex;
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
                    BuildingModel.Hospital or BuildingModel.Clinic => CoverageHealth,
                    BuildingModel.PoliceStation or BuildingModel.PolicePost => CoveragePolice,
                    BuildingModel.FireStation or BuildingModel.FireStationSmall => CoverageFire,
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
            // Happiness runs 10-100 once missing utilities and traffic jams count against it.
            float t = Mathf.InverseLerp(10f, 100f, happiness);
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
            if (_shoreTexture != null) UnityEngine.Object.Destroy(_shoreTexture);
            UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
