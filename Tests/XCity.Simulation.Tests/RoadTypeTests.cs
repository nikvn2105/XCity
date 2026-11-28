using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace XCity.Tests
{
    [Collection("WorldMap")]
    public class RoadTypeTests
    {
        [Theory]
        [InlineData(RoadType.Street, 100, 2, 10)]
        [InlineData(RoadType.Avenue, 250, 4, 20)]
        [InlineData(RoadType.OneWay, 100, 2, 15)]
        [InlineData(RoadType.Boulevard, 200, 3, 10)]
        public void Roads_ChargePriceAndUpkeep(RoadType type, int price, int upkeep, int capacity)
        {
            var (x, z) = Inland();
            var city = new CitySimulation();
            int before = city.Money;
            Assert.True(city.TryBuildRoad(new[] { (x, z), (x + 1, z) }, type));
            Assert.Equal(before - price * 2, city.Money);
            Assert.Equal(capacity * 2, city.RoadCapacityAt(x, z));
            city.Tick();
            Assert.Equal(upkeep * 2, city.LastBudget.Roads);
        }

        [Fact]
        public void RoadCapacity_AddsMixedTilesAndRebuildsAfterUpgradeAndUndo()
        {
            var (x, z) = Inland();
            var city = new CitySimulation();
            Assert.True(city.TrySetCell(x, z, CellType.Road));
            Assert.True(city.TryBuildRoad(new[] { (x + 1, z), (x + 2, z) }, RoadType.OneWay));
            Assert.True(city.TryBuildRoad(new[] { (x + 3, z) }, RoadType.Boulevard));
            Assert.Equal(50, city.RoadCapacityAt(x, z));

            int money = city.Money;
            Assert.True(city.TryBuildRoad(new[] { (x + 1, z) }, RoadType.Avenue));
            Assert.Equal(money - 150, city.Money);
            Assert.Equal(55, city.RoadCapacityAt(x, z));
            Assert.True(city.TryUndoBuild());
            Assert.Equal(money, city.Money);
            Assert.Equal(RoadType.OneWay, city.GetCell(x + 1, z).RoadType);
            Assert.Equal(RoadConnections.East, city.GetCell(x + 1, z).OneWayExit);
            Assert.Equal(50, city.RoadCapacityAt(x, z));
        }

        [Fact]
        public void AvenueNoiseIsResidentialAndBoulevardBenefitDoesNotStack()
        {
            var (x, z) = Inland();
            var city = new CitySimulation();
            Assert.True(city.TrySetCell(x, z, CellType.Residential));
            Assert.True(city.TrySetCell(x + 2, z, CellType.Commercial));
            int residential = city.GetLandValue(x, z), commercial = city.GetLandValue(x + 2, z);
            Assert.True(city.TryBuildRoad(new[] { (x + 1, z) }, RoadType.Avenue));
            Assert.Equal(residential - 5, city.GetLandValue(x, z));
            Assert.Equal(commercial, city.GetLandValue(x + 2, z));
            Assert.True(city.TryBuildRoad(new[] { (x, z + 1), (x, z - 1) }, RoadType.Boulevard));
            Assert.Equal(residential + 3, city.GetLandValue(x, z));
        }

        [Fact]
        public void OneWay_CornerWorksForwardAndPhysicalAccessRemainsBidirectional()
        {
            var (x, z) = Inland();
            var city = new CitySimulation();
            Assert.True(city.TryBuildRoad(new[] { (x, z), (x + 1, z), (x + 1, z + 1) }, RoadType.OneWay));
            Assert.True(city.TrySetCell(x + 1, z + 2, CellType.Road));
            var route = new List<int>();
            Assert.True(city.Roads.TryFindPath(x, z, x + 1, z + 2, route));
            Assert.Equal(new[] { 1, 3, 3 }, route);
            Assert.False(city.Roads.TryFindPath(x + 1, z + 2, x, z, route));
            Assert.Equal(RoadConnections.West | RoadConnections.North, city.Roads.GetConnections(x + 1, z));
            Assert.True(city.Roads.HasRoadAccess(x + 2, z));
            // Power still crosses the same network in the direction forbidden to vehicles.
            Assert.True(city.TrySetCell(x + 2, z + 2, CellType.PowerPlant));
            Assert.True(city.TrySetCell(x, z - 1, CellType.Residential));
            Assert.Equal(ZoneStatus.WaitingForDevelopment, city.GetZoneStatus(x, z - 1));
        }

        [Theory]
        [InlineData(16)]
        [InlineData(17)]
        public void TypedRoadsRoundTripAndSave15LoadsRoadsAsStreet(int schema)
        {
            var (x, z) = Inland();
            var city = new CitySimulation();
            Assert.True(city.TryBuildRoad(new[] { (x, z), (x + 1, z) }, RoadType.OneWay));
            Assert.True(city.TryBuildRoad(new[] { (x + 2, z) }, RoadType.Boulevard));
            Assert.True(city.TryBuildRoad(new[] { (x + 3, z) }, RoadType.Avenue));

            var options = new JsonSerializerOptions { IncludeFields = true };
            var snapshot = JsonSerializer.Deserialize<CitySnapshot>(JsonSerializer.Serialize(city.CreateSnapshot(), options), options);
            Assert.Equal(CitySimulation.CurrentSchemaVersion, snapshot.schemaVersion);
            snapshot.schemaVersion = schema;
            Assert.True(CitySimulation.TryRestore(snapshot, out var restored));
            Assert.Equal(RoadConnections.East, restored.GetCell(x, z).OneWayExit);
            Assert.Equal(RoadType.OneWay, restored.GetCell(x, z).RoadType);
            Assert.Equal(RoadType.Boulevard, restored.GetCell(x + 2, z).RoadType);
            Assert.Equal(RoadType.Avenue, restored.GetCell(x + 3, z).RoadType);

            snapshot.schemaVersion = 15;
            Assert.True(CitySimulation.TryRestore(snapshot, out var old));
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(RoadType.Street, old.GetCell(x + i, z).RoadType);
                Assert.Equal(RoadConnections.None, old.GetCell(x + i, z).OneWayExit);
            }
            Assert.True(old.Roads.TryFindPath(x + 3, z, x, z, new List<int>()));
        }

        [Fact]
        public void OneWayBridge_KeepsTypeAndDirection()
        {
            var crossing = (from bankX in Enumerable.Range(1, CitySimulation.GridSize - 14)
                                from bankZ in Enumerable.Range(1, CitySimulation.GridSize - 2)
                                where WorldTerrain.IsLand(bankX, bankZ) && !WorldTerrain.IsLand(bankX + 1, bankZ)
                                from distance in Enumerable.Range(2, 12)
                                where WorldTerrain.IsLand(bankX + distance, bankZ) &&
                                    Enumerable.Range(1, distance - 1).All(i => !WorldTerrain.IsLand(bankX + i, bankZ))
                                select (bankX, bankZ, distance)).First();
            var (x, z, span) = crossing;
            var city = new CitySimulation();
            Assert.True(city.TryBuildBridge(x, z, x + span, z, RoadType.OneWay));
            Assert.True(city.GetCell(x + 1, z).IsBridge);
            Assert.Equal(RoadType.OneWay, city.GetCell(x + 1, z).RoadType);
            Assert.True(city.Roads.TryFindPath(x, z, x + span, z, new List<int>()));
            Assert.False(city.Roads.TryFindPath(x + span, z, x, z, new List<int>()));
            Assert.True(CitySimulation.TryRestore(city.CreateSnapshot(), out var restored));
            Assert.Equal(RoadConnections.East, restored.GetCell(x + 1, z).OneWayExit);

            int money = restored.Money;
            Assert.True(restored.TryBuildBridge(x, z, x + span, z, RoadType.Avenue));
            Assert.Equal(money - 150 * (span + 1), restored.Money);
            Assert.Equal(RoadType.Avenue, restored.GetCell(x + 1, z).RoadType);
            Assert.True(restored.TryUndoBuild());
            Assert.Equal(RoadType.OneWay, restored.GetCell(x + 1, z).RoadType);
        }

        private static (int X, int Z) Inland() => WorldTerrain.Start;
    }
}
