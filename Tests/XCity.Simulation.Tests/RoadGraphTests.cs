using System.Collections.Generic;
using Xunit;

namespace XCity.Tests
{
    public class RoadGraphTests
    {
        private const int W = 0, E = 1, S = 2, N = 3;

        private static RoadGraph Graph(params (int X, int Z)[] roads)
        {
            var graph = new RoadGraph(16);
            foreach (var (x, z) in roads) graph.SetRoad(x, z, true);
            return graph;
        }

        [Fact]
        public void FindsShortestRouteAroundACorner()
        {
            // An L from (1,1) east to (4,1) then north to (4,3), plus a longer detour that must not be taken.
            var graph = Graph((1, 1), (2, 1), (3, 1), (4, 1), (4, 2), (4, 3),
                (1, 2), (1, 3), (1, 4), (1, 5), (2, 5), (3, 5), (4, 5), (4, 4));
            var route = new List<int>();

            Assert.True(graph.TryFindPath(1, 1, 4, 3, route));
            Assert.Equal(new[] { E, E, E, N, N }, route);
        }

        [Fact]
        public void FollowsRouteBackwards()
        {
            var graph = Graph((5, 5), (6, 5), (7, 5));
            var route = new List<int>();
            Assert.True(graph.TryFindPath(7, 5, 5, 5, route));
            Assert.Equal(new[] { W, W }, route);
        }

        [Fact]
        public void FailsBetweenDisconnectedRoads()
        {
            var graph = Graph((1, 1), (2, 1), (5, 5), (5, 6));
            var route = new List<int> { 99 };
            Assert.False(graph.TryFindPath(1, 1, 5, 6, route));
            Assert.Empty(route);
        }

        [Fact]
        public void BridgesOnlyConnectAlongTheirAxis()
        {
            var graph = new RoadGraph(16);
            graph.SetRoad(2, 2, true);
            graph.SetRoad(3, 2, true, RoadConnections.West | RoadConnections.East);
            graph.SetRoad(4, 2, true);
            graph.SetRoad(3, 3, true);
            var route = new List<int>();

            Assert.True(graph.TryFindPath(2, 2, 4, 2, route));
            Assert.Equal(new[] { E, E }, route);
            Assert.False(graph.TryFindPath(2, 2, 3, 3, route));
        }

        [Fact]
        public void RepeatedSearchesDoNotReuseStaleVisits()
        {
            var graph = Graph((1, 1), (2, 1), (3, 1));
            var route = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                Assert.True(graph.TryFindPath(1, 1, 3, 1, route));
                Assert.Equal(new[] { E, E }, route);
            }
        }
    }
}
