using System;
using Xunit;

namespace XCity.Tests
{
    public class RoadSegmentTests
    {
        private static RoadSegment Curve(int x, int z) =>
            new RoadSegment(x + 0.5f, z + 0.5f, x + 10.5f, z + 0.5f, x + 10.5f, z + 10.5f, RoadType.Street);

        [Fact]
        public void Rasterize_IsFourConnectedFromStartToEnd()
        {
            var cells = CitySimulation.RasterizeSegment(Curve(100, 100));
            Assert.Equal((100, 100), cells[0]);
            Assert.Equal((110, 110), cells[cells.Count - 1]);
            for (int i = 1; i < cells.Count; i++)
                Assert.Equal(1, Math.Abs(cells[i].X - cells[i - 1].X) + Math.Abs(cells[i].Z - cells[i - 1].Z));
        }

        [Fact]
        public void BuildSegment_MakesRoadsOwnedByTheCurve_AndBulldozeDropsIt()
        {
            var (x, z) = EconomyTests.FindLand(12, 12);
            var city = new CitySimulation();
            Assert.True(city.TryBuildSegment(Curve(x, z), out int id));
            Assert.Equal(CellType.Road, city.GetCell(x + 10, z + 10).Type);
            Assert.Equal(id, city.SegmentAt(x, z));
            Assert.Single(city.RoadSegments);

            var mid = CitySimulation.RasterizeSegment(Curve(x, z))[8];
            Assert.True(city.TrySetCell(mid.X, mid.Z, CellType.Empty));
            Assert.Empty(city.RoadSegments);
            Assert.Equal(0, city.SegmentAt(x, z));
        }

        [Fact]
        public void UndoOfASegment_RemovesRoadsAndCurve()
        {
            var (x, z) = EconomyTests.FindLand(12, 12);
            var city = new CitySimulation();
            int money = city.Money;
            Assert.True(city.TryBuildSegment(Curve(x, z), out _));
            Assert.True(city.TryUndoBuild());
            Assert.Empty(city.RoadSegments);
            Assert.Equal(CellType.Empty, city.GetCell(x, z).Type);
            Assert.Equal(money, city.Money);
        }

        [Fact]
        public void Segments_SurviveSaveAndLoad()
        {
            var (x, z) = EconomyTests.FindLand(12, 12);
            var city = new CitySimulation();
            Assert.True(city.TryBuildSegment(Curve(x, z), out _));
            CitySnapshot snapshot = city.CreateSnapshot();
            Assert.Equal(18, snapshot.schemaVersion);
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Single(restored.RoadSegments);
            var mid = CitySimulation.RasterizeSegment(Curve(x, z))[8];
            Assert.NotEqual(0, restored.SegmentAt(mid.X, mid.Z));

            snapshot.schemaVersion = 17; // An older save has no curves; its roads load as plain grid road.
            snapshot.roadSegments = null;
            Assert.True(CitySimulation.TryRestore(snapshot, out var old));
            Assert.Empty(old.RoadSegments);
            Assert.Equal(CellType.Road, old.GetCell(x, z).Type);
        }
    }
}
