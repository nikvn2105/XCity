using System;
using Xunit;
using Xunit.Abstractions;

namespace XCity.Tests
{
    [Collection("WorldMap")]
    public sealed class RoadCurveGeometryTests
    {
        private readonly ITestOutputHelper _output;

        public RoadCurveGeometryTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void EqualArcStepsProduceNearlyEqualDistanceEvenWithUnevenControlPoint()
        {
            var arc = new RoadArc(new RoadSegment(1.5f, 2.5f, 1.8f, 2.5f, 10.5f, 9.5f, RoadType.Street));
            float step = arc.Length / 32;
            RoadPoint previous = arc.Sample(0f, out _);
            for (int i = 1; i <= 32; i++)
            {
                RoadPoint point = arc.Sample(i * step, out RoadPoint tangent);
                Assert.InRange((point - previous).Length, step * 0.97f, step * 1.03f);
                Assert.InRange(tangent.Length, 0.999f, 1.001f);
                previous = point;
            }
            Assert.InRange((previous - new RoadPoint(10.5f, 9.5f)).Length, 0f, 0.0001f);
        }

        [Fact]
        public void JunctionMouthUsesTheSharedEdgeForBothDirectionsAndLaneOffsets()
        {
            var arc = new RoadArc(new RoadSegment(1.5f, 2.5f, 4.5f, 2.5f, 6.5f, 7.5f, RoadType.Avenue));
            var previous = new RoadPoint(1.5f, 2.5f);
            var center = new RoadPoint(2.5f, 2.5f);
            var next = new RoadPoint(2.5f, 3.5f);
            var span = new RoadCurveCell(arc, center, previous, next, true, true, 0.42f, 1.19f);

            AssertMouth(span.Sample(0f, 0.29f, true, true, startSide: 0.17f), 2f, 2.33f);
            AssertMouth(span.Sample(1f, -0.29f, true, true, endSide: -0.17f), 2.33f, 3f);

            static void AssertMouth(RoadPoint point, float x, float z)
            {
                Assert.Equal(x, point.X, 5);
                Assert.Equal(z, point.Z, 5);
            }
        }

        [Fact]
        public void ClipEndsExactlyOnGridEdge()
        {
            var quad = new[] { new RoadPoint(1.5f, 1.25f), new RoadPoint(2.5f, 1.75f),
                new RoadPoint(2.5f, 2.25f), new RoadPoint(1.5f, 1.75f) };
            var output = new RoadPoint[6];
            int count = RoadRibbonClip.HalfPlane(quad, output, true, 2f, false);
            Assert.Equal(4, count);
            Assert.Equal(2f, output[1].X);
            Assert.Equal(1.5f, output[1].Z);
            Assert.Equal(2f, output[2].X);
            Assert.Equal(2f, output[2].Z);
        }

        [Theory]
        [InlineData(3.5f, 1.5f)]
        [InlineData(3.5f, 5.5f)]
        public void LotFacesAcrossTheClosestTangent(float x, float z)
        {
            var arc = new RoadArc(new RoadSegment(1.5f, 2.5f, 4.5f, 2.5f, 6.5f, 7.5f, RoadType.Street));
            var lot = new RoadPoint(x, z);
            RoadPoint facing = arc.Facing(lot, out float squared);
            RoadPoint onRoad = arc.Sample(arc.ClosestDistance(lot), out RoadPoint tangent);
            Assert.InRange(Math.Abs(RoadPoint.Dot(tangent, facing)), 0f, 0.00001f);
            Assert.True(RoadPoint.Dot(onRoad - lot, facing) > 0f);
            Assert.Equal((onRoad - lot).LengthSquared, squared, 5);
            Assert.InRange(Math.Abs(facing.X), 0.01f, 0.99f);
        }

        [Fact]
        public void ConsecutiveSlicesShareBothLanePointsAndRemovalInvalidatesWholeArc()
        {
            var city = new CitySimulation();
            var (x, z) = EconomyTests.FindLand(12, 12);
            var segment = new RoadSegment(x + 0.5f, z + 0.5f, x + 5.5f, z + 0.5f,
                x + 5.5f, z + 4.5f, RoadType.Street);
            var geometry = city.RoadCurves;
            Assert.True(city.TryBuildSegment(segment, out _));
            var path = CitySimulation.RasterizeSegment(segment);
            Assert.Equal(10, path.Count);
            RoadCurveCell previous = null;
            int steps = 0, dashes = 0, arrows = 0;
            foreach (var tile in path)
            {
                Assert.True(geometry.TryGet(tile.X, tile.Z, out var span));
                Assert.False(geometry.IsJunction(tile.X, tile.Z));
                if (previous != null)
                {
                    foreach (float lane in new[] { -0.17f, 0.17f, 0.39f })
                        Assert.InRange((previous.Sample(1f, lane, false, false) -
                            span.Sample(0f, lane, false, false)).Length, 0f, 0.00001f);
                }
                int sections = span.RibbonSteps(false);
                steps += sections;
                for (int i = 0; i < sections; i++)
                    if (span.DashAt(i, sections)) dashes++;
                if (span.End - span.Start >= 0.32f) arrows++;
                previous = span;
            }
            _output.WriteLine($"Steps {steps}; dashes {dashes}; arrow marks {arrows}.");
            _output.WriteLine($"Street/Boulevard {6 * steps + 2 * dashes}; Avenue {8 * steps + 4 * dashes}; OneWay {10 * steps + 3 * arrows} ribbon triangles.");

            Assert.True(city.TrySetCell(x - 1, z, CellType.Road));
            Assert.True(geometry.IsJunction(x, z));
            Assert.True(city.TrySetCell(x, z, CellType.Empty));
            foreach (var tile in path) Assert.False(geometry.TryGet(tile.X, tile.Z, out _));
            Assert.False(geometry.IsJunction(x + 1, z));
        }
    }
}
