using System;

namespace XCity
{
    public enum RoadType : byte { Street, Avenue, OneWay, Boulevard }

    public static class RoadCatalog
    {
        private const int AvenuePrice = 250, BoulevardPrice = 200;
        private const int AvenueUpkeep = 4, BoulevardUpkeep = 3;
        private const int AvenueCapacity = 20, OneWayCapacity = 15;
        internal const int AvenueResidentialNoise = 5, BoulevardLandBonus = 8;

        public static int Cost(RoadType type) => type switch
        {
            RoadType.Avenue => AvenuePrice,
            RoadType.Boulevard => BoulevardPrice,
            _ => CitySimulation.RoadCost
        };

        public static int Upkeep(RoadType type) => type switch
        {
            RoadType.Avenue => AvenueUpkeep,
            RoadType.Boulevard => BoulevardUpkeep,
            _ => CitySimulation.RoadUpkeep
        };

        public static int Capacity(RoadType type) => type switch
        {
            RoadType.Avenue => AvenueCapacity,
            RoadType.OneWay => OneWayCapacity,
            _ => CitySimulation.CommutersPerRoad
        };

        internal static int UpgradeCost(RoadType previous, RoadType next) => Math.Max(0, Cost(next) - Cost(previous));

        internal static bool IsValid(RoadType type, RoadConnections exit) =>
            type == RoadType.OneWay
                ? exit is RoadConnections.West or RoadConnections.East or RoadConnections.South or RoadConnections.North
                : type <= RoadType.Boulevard && exit == RoadConnections.None;

        internal static RoadConnections Direction(int dx, int dz)
        {
            if (dz == 0) return dx == 1 ? RoadConnections.East : dx == -1 ? RoadConnections.West : RoadConnections.None;
            if (dx == 0) return dz == 1 ? RoadConnections.North : dz == -1 ? RoadConnections.South : RoadConnections.None;
            return RoadConnections.None;
        }

        internal static RoadConnections ExitForStep(RoadType type, int dx, int dz, RoadConnections fallback)
        {
            if (type != RoadType.OneWay) return RoadConnections.None;
            RoadConnections direction = Direction(dx, dz);
            return direction == RoadConnections.None ? fallback : direction;
        }
    }
}
