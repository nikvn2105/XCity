using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace XCity.Tests
{
    public class CitySimulationTests
    {
        // A column of four land tiles with land to its east, so a road at x and buildings at x + 1 both fit.
        private static (int X, int Z) FindLandPlot()
        {
            int center = CitySimulation.GridSize / 2;
            for (int x = center; x < CitySimulation.GridSize - 2; x++)
            for (int z = center; z < CitySimulation.GridSize - 4; z++)
            {
                bool fits = true;
                for (int dx = 0; dx <= 1 && fits; dx++)
                for (int dz = 0; dz < 4 && fits; dz++)
                    fits = CitySimulation.IsBuildable(x + dx, z + dz);
                if (fits) return (x, z);
            }
            throw new InvalidOperationException("No land plot found");
        }

        private static (int X, int Z) FindWater()
        {
            for (int x = 0; x < CitySimulation.GridSize; x++)
            for (int z = 0; z < CitySimulation.GridSize; z++)
                if (!WorldTerrain.IsLand(x, z)) return (x, z);
            throw new InvalidOperationException("No water found");
        }

        // Land bank, 2..10 water tiles, land bank, along X.
        private static (int StartX, int EndX, int Z) FindBridgeSpan()
        {
            for (int z = 0; z < CitySimulation.GridSize; z++)
            for (int x = 0; x < CitySimulation.GridSize - 2; x++)
            {
                if (!CitySimulation.IsBuildable(x, z) || WorldTerrain.IsLand(x + 1, z)) continue;
                int end = x + 1;
                while (end < CitySimulation.GridSize && !WorldTerrain.IsLand(end, z)) end++;
                int water = end - x - 1;
                if (end < CitySimulation.GridSize && water >= 2 && water <= 10 && CitySimulation.IsBuildable(end, z))
                    return (x, end, z);
            }
            throw new InvalidOperationException("No bridge span found");
        }

        private static CitySimulation WithMoney(CitySimulation city, int money)
        {
            CitySnapshot snapshot = city.CreateSnapshot();
            snapshot.money = money;
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            return restored;
        }

        [Fact]
        public void Undo_RemainsAvailableAfterSimulationTicks()
        {
            var city = new CitySimulation();
            var (x, z) = FindLandPlot();
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            for (int i = 0; i < 5; i++) city.Tick();

            Assert.True(city.CanUndoBuild);
            int money = city.Money;
            Assert.True(city.TryUndoBuild());
            Assert.Equal(CellType.Empty, city.GetCell(x, z).Type);
            Assert.Equal(money + CitySimulation.RoadCost, city.Money);
        }

        [Fact]
        public void Undo_RevertsStepsNewestFirst()
        {
            var city = new CitySimulation();
            var (x, z) = FindLandPlot();
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            Assert.True(city.TrySetCell(x, z, CellType.Empty));
            Assert.True(city.TrySetCell(x, z, CellType.Residential, BuildingModel.Villa));

            Assert.True(city.TryUndoBuild());
            Assert.Equal(CellType.Empty, city.GetCell(x, z).Type);
            Assert.True(city.TryUndoBuild());
            Assert.Equal(CellType.Road, city.GetCell(x, z).Type);
            Assert.True(city.TryUndoBuild());
            Assert.Equal(CellType.Empty, city.GetCell(x, z).Type);
            Assert.False(city.TryUndoBuild());
            Assert.Equal(50000, city.Money);
        }

        [Fact]
        public void Undo_HistoryIsBounded()
        {
            var city = new CitySimulation();
            var (x, z) = FindLandPlot();
            for (int i = 0; i < CitySimulation.MaxUndoSteps + 5; i++)
                Assert.True(city.TrySetCell(x, z, i % 2 == 0 ? CellType.Road : CellType.Empty));

            int undone = 0;
            while (city.TryUndoBuild()) undone++;
            Assert.Equal(CitySimulation.MaxUndoSteps, undone);
        }

        [Fact]
        public void Undo_OfDevelopedHomeRemovesItsResidents()
        {
            var city = new CitySimulation(7);
            var (x, z) = FindLandPlot();
            Assert.True(city.TryApplyBuild(Enumerable.Range(z, 4).Select(dz => (x, dz)), CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.PowerPlant));
            Assert.True(city.TrySetCell(x + 1, z + 1, CellType.Residential, BuildingModel.Villa));
            for (int i = 0; i < 100 && city.Population == 0; i++) city.Tick();
            Assert.True(city.Population > 0);

            Assert.True(city.TryUndoBuild());
            Assert.Equal(0, city.Population);
            Assert.Equal(CellType.Empty, city.GetCell(x + 1, z + 1).Type);
        }

        [Fact]
        public void Bridge_CanBeUndoneWithFullRefund()
        {
            var city = new CitySimulation();
            var (startX, endX, z) = FindBridgeSpan();
            int money = city.Money;
            Assert.True(city.TryBuildBridge(startX, z, endX, z));
            Assert.True(city.GetCell(startX + 1, z).IsBridge);

            Assert.True(city.TryUndoBuild());
            for (int x = startX; x <= endX; x++)
                Assert.Equal(CellType.Empty, city.GetCell(x, z).Type);
            Assert.Equal(money, city.Money);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(0)]
        [InlineData(-500)]
        public void Demolition_NeverPushesMoneyFurtherNegative(int money)
        {
            var built = new CitySimulation();
            var (x, z) = FindLandPlot();
            Assert.True(built.TrySetCell(x, z, CellType.Road));
            var city = WithMoney(built, money);

            Assert.True(city.TrySetCell(x, z, CellType.Empty));
            Assert.Equal(Math.Min(money, 0), city.Money);
        }

        [Fact]
        public void BridgeDemolition_NeverMakesMoneyNegative()
        {
            var built = new CitySimulation();
            var (startX, _, z) = FindBridgeSpan();
            Assert.True(built.TryBuildBridge(startX, z, FindBridgeSpan().EndX, z));
            var city = WithMoney(built, 5);

            Assert.True(city.TrySetCell(startX + 1, z, CellType.Empty));
            Assert.False(city.GetCell(startX + 1, z).IsBridge);
            Assert.Equal(0, city.Money);
        }

        [Fact]
        public void Building_RequiresFunds()
        {
            var city = WithMoney(new CitySimulation(), CitySimulation.RoadCost - 1);
            var (x, z) = FindLandPlot();
            Assert.False(city.TrySetCell(x, z, CellType.Road));
            Assert.False(city.CanUndoBuild);
        }

        [Fact]
        public void Restore_RoundTripsCity()
        {
            var city = UnlockTests.UnlockedCity();
            var (x, z) = FindLandPlot();
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            Assert.True(city.TrySetCell(x + 1, z, CellType.Commercial, BuildingModel.Office));

            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
            Assert.Equal(city.Money, restored.Money);
            Assert.Equal(CellType.Road, restored.GetCell(x, z).Type);
            Assert.Equal(BuildingModel.Office, restored.GetCell(x + 1, z).Model);
            Assert.Equal(0, restored.DroppedCellsOnLoad);
        }

        [Fact]
        public void Restore_DropsCellsThatNoLongerFitTerrainInsteadOfRejectingSave()
        {
            var city = new CitySimulation();
            var (x, z) = FindLandPlot();
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            CitySnapshot snapshot = city.CreateSnapshot();

            // Simulate a terrain change that turned a built tile into water.
            var (wx, wz) = FindWater();
            var entries = snapshot.cellIndices.Zip(snapshot.cells, (i, c) => (Index: i, Cell: c)).ToList();
            entries.Add((CitySimulation.CellIndex(wx, wz), new CityCell { Type = CellType.Road }));
            entries.Sort((a, b) => a.Index.CompareTo(b.Index));
            snapshot.cellIndices = entries.Select(e => e.Index).ToArray();
            snapshot.cells = entries.Select(e => e.Cell).ToArray();

            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Equal(1, restored.DroppedCellsOnLoad);
            Assert.Equal(CellType.Road, restored.GetCell(x, z).Type);
            Assert.Equal(CellType.Empty, restored.GetCell(wx, wz).Type);
        }

        [Fact]
        public void Restore_StillRejectsCorruptSaves()
        {
            var city = new CitySimulation();
            var (x, z) = FindLandPlot();
            Assert.True(city.TryApplyBuild(new[] { (x, z), (x, z + 1) }, CellType.Road));
            CitySnapshot snapshot = city.CreateSnapshot();
            Array.Reverse(snapshot.cellIndices);

            Assert.False(CitySimulation.TryRestore(snapshot, out _));
        }
    }
}
