using Xunit;

namespace XCity.Tests
{
    public class VehicleLaneTests
    {
        private const int W = 0, E = 1, S = 2, N = 3;

        [Theory]
        [InlineData(S, E, N)] // southbound on the north arm, with an east-to-north car in the bend
        [InlineData(E, S, W)] // eastbound on the west arm, with a south-to-west car in the bend
        public void Bend_DoesNotBlockAnApproachingCarFromTheOppositeLane(int approach, int enter, int exit)
        {
            // These headings and progress values satisfy the old broad crossing check.
            Assert.NotEqual(approach ^ 1, enter);
            Assert.False(VehicleLaneRules.BlocksEntry(approach, enter, exit, 0.84f, 0.55f, (1 << W) | (1 << N)));
        }

        [Fact]
        public void Bend_ReversePathsDoNotCrossInsideTheSameTile()
        {
            Assert.False(VehicleLaneRules.BlocksAcrossCell(N, S, W, 0.35f, 0.35f));
            Assert.False(VehicleLaneRules.BlocksAcrossCell(W, E, N, 0.35f, 0.35f));
        }

        [Fact]
        public void CrossingTrafficStillBlocksAndSameLaneUsesFollowingDistance()
        {
            Assert.True(VehicleLaneRules.BlocksEntry(N, E, E, 0.84f, 0.55f, 15));
            Assert.True(VehicleLaneRules.BlocksEntry(S, E, N, 0.84f, 0.55f, 15));
            Assert.True(VehicleLaneRules.BlocksAcrossCell(N, E, E, 0.35f, 0.55f));
            Assert.False(VehicleLaneRules.BlocksEntry(N, N, E, 0.84f, 0.55f, 15));
        }

        [Theory]
        [InlineData(N, E)]
        [InlineData(W, S)]
        public void Bend_SpawnStartsOnTheCorrectLane(int exit, int expectedHeading)
        {
            Assert.Equal(expectedHeading, VehicleLaneRules.SpawnHeading(exit, (1 << W) | (1 << N), 2));
        }
    }
}
