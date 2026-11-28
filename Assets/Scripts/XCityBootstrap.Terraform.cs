using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    public partial class XCityBootstrap
    {
        private readonly Dictionary<int, byte> _plannedTerraform = new Dictionary<int, byte>(PreviewPoolCapacity);

        private static bool IsTerraformAction(BuildTool tool) =>
            tool is BuildTool.RaiseTerrain or BuildTool.LowerTerrain or BuildTool.LevelTerrain;

        private TerrainTool SelectedTerrainTool => _activeTool switch
        {
            BuildTool.RaiseTerrain => TerrainTool.Raise,
            BuildTool.LowerTerrain => TerrainTool.Lower,
            _ => TerrainTool.Level
        };

        private Material QuoteTerraformMarker(Vector2Int cell)
        {
            if (_strokeOverBudget) return _invalidPreviewMat;
            Vector2Int start = _stroke[0];
            if (_activeTool == BuildTool.LevelTerrain && !WorldTerrain.IsLand(start.x, start.y))
                return _invalidPreviewMat;

            byte target = CitySimulation.TerraformTarget(SelectedTerrainTool, WorldTerrain.Tier(cell.x, cell.y),
                WorldTerrain.Tier(start.x, start.y));
            if (!_city.TryQuoteTerraform(cell.x, cell.y, target, out int cost, _plannedTerraform))
                return _invalidPreviewMat;
            if (cost > _strokeBudget)
            {
                _strokeOverBudget = true;
                return _invalidPreviewMat;
            }

            _strokeCost += cost;
            _strokeBudget -= cost;
            _plannedTerraform.Add(CitySimulation.CellIndex(cell.x, cell.y), target);
            return _roadPreviewMat;
        }
    }
}
