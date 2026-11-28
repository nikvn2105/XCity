using UnityEngine;

namespace XCity
{
    // Road by taps: the first tap sets a start point, each next tap builds an L-shaped road to it
    // (longer side first) and becomes the new start. Tapping the start again stops. Dragging still
    // draws freehand roads.
    public partial class XCityBootstrap
    {
        private Vector2Int? _roadAnchor;
        private MeshRenderer _roadAnchorMarker;
        private readonly MeshRenderer[] _roadGuides = new MeshRenderer[2];
        private readonly Mesh[] _roadGuideMeshes = new Mesh[2];
        private readonly MeshBuilder _roadGuideBuilder = new MeshBuilder();
        private const int RoadGuideCells = 60;

        private void HandleRoadTap(Vector2Int cell)
        {
            if (!_roadAnchor.HasValue)
            {
                SetRoadAnchor(cell);
                return;
            }

            Vector2Int start = _roadAnchor.Value;
            if (cell == start)
            {
                ClearRoadAnchor();
                return;
            }

            // Replace the touch-down sample with the full L path from the anchor.
            HideStreetPreview();
            _stroke.Clear();
            _strokeCells.Clear();
            _roadTrace.Clear();
            _strokeRoadExits.Clear();
            _quotedBridges.Clear();
            _strokeCost = 0;
            _strokeOverBudget = false;
            Vector2Int corner = Mathf.Abs(cell.x - start.x) >= Mathf.Abs(cell.y - start.y)
                ? new Vector2Int(cell.x, start.y)
                : new Vector2Int(start.x, cell.y);
            AddStrokeSample(start);
            AddStrokeSample(corner);
            AddStrokeSample(cell);
            // Keep going from the end point unless money ran out (an all-existing path builds nothing).
            if (ApplyBuildStroke() || !_strokeOverBudget) SetRoadAnchor(cell);
        }

        private void SetRoadAnchor(Vector2Int cell)
        {
            if (_roadAnchorMarker == null)
            {
                CreateStreetMarker();
                _roadAnchorMarker = _roadMarkers[_roadMarkers.Count - 1];
                _roadMarkers.RemoveAt(_roadMarkers.Count - 1);
                _roadAnchorMarker.name = "RoadAnchor";
                _roadAnchorMarker.transform.localScale = new Vector3(CellSize * 0.6f, 0.5f, CellSize * 0.6f);
            }
            _roadAnchor = cell;
            _roadAnchorMarker.transform.position = new Vector3((cell.x + 0.5f) * CellSize, PreviewRise(cell) + 0.3f, (cell.y + 0.5f) * CellSize);
            _roadAnchorMarker.transform.rotation = TerrainRotation(WorldTerrain.GetSurface(cell.x, cell.y));
            _roadAnchorMarker.gameObject.SetActive(true);
            // Alignment guides along the anchor's row and column, so the next tap lands straight.
            for (int axis = 0; axis < 2; axis++)
                UpdateRoadGuide(cell, axis == 0);
        }

        private void UpdateRoadGuide(Vector2Int cell, bool alongX)
        {
            int axis = alongX ? 0 : 1;
            if (_roadGuides[axis] == null)
            {
                var guide = new GameObject("RoadGuide");
                guide.transform.SetParent(transform, false);
                _roadGuideMeshes[axis] = new Mesh { name = "RoadGuide" };
                _roadGuideMeshes[axis].MarkDynamic();
                guide.AddComponent<MeshFilter>().sharedMesh = _roadGuideMeshes[axis];
                _roadGuides[axis] = guide.AddComponent<MeshRenderer>();
                _roadGuides[axis].sharedMaterial = _roadPreviewMat;
                _roadGuides[axis].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _roadGuideBuilder.Clear();
            int anchor = alongX ? cell.x : cell.y;
            for (int i = Mathf.Max(0, anchor - RoadGuideCells); i <= Mathf.Min(GridSize - 1, anchor + RoadGuideCells); i++)
            {
                int x = alongX ? i : cell.x, z = alongX ? cell.y : i;
                if (!WorldTerrain.IsLand(x, z)) continue;
                TerrainPatch surface = WorldTerrain.GetSurface(x, z);
                if (alongX) Strip(0f, 0.46f, 1f, 0.54f);
                else Strip(0.46f, 0f, 0.54f, 1f);

                void Strip(float u0, float v0, float u1, float v1) =>
                    _roadGuideBuilder.AddQuad(Point(u0, v0), Point(u0, v1), Point(u1, v1), Point(u1, v0),
                        Vector3.up, Color.white);

                Vector3 Point(float u, float v) => new Vector3((x + u) * CellSize, surface.Sample(u, v) + 0.10f, (z + v) * CellSize);
            }
            _roadGuideBuilder.ApplyTo(_roadGuideMeshes[axis]);
            _roadGuides[axis].gameObject.SetActive(true);
        }

        private void ClearRoadAnchor()
        {
            _roadAnchor = null;
            if (_roadAnchorMarker != null) _roadAnchorMarker.gameObject.SetActive(false);
            foreach (MeshRenderer guide in _roadGuides)
                if (guide != null) guide.gameObject.SetActive(false);
        }

        private string RoadHint => _roadAnchor.HasValue ? UiText.RoadTapEnd : UiText.RoadTapStart;
    }
}
