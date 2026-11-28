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
            _roadAnchorMarker.gameObject.SetActive(true);
            // Alignment guides along the anchor's row and column, so the next tap lands straight.
            for (int axis = 0; axis < 2; axis++)
            {
                if (_roadGuides[axis] == null)
                {
                    CreateStreetMarker();
                    _roadGuides[axis] = _roadMarkers[_roadMarkers.Count - 1];
                    _roadMarkers.RemoveAt(_roadMarkers.Count - 1);
                    _roadGuides[axis].name = "RoadGuide";
                    float length = RoadGuideCells * 2 * CellSize, width = CellSize * 0.08f;
                    _roadGuides[axis].transform.localScale = axis == 0
                        ? new Vector3(length, 0.04f, width) : new Vector3(width, 0.04f, length);
                }
                _roadGuides[axis].transform.position = _roadAnchorMarker.transform.position - new Vector3(0f, 0.22f, 0f);
                _roadGuides[axis].gameObject.SetActive(true);
            }
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
