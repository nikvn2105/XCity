namespace XCity
{
    internal static class VehicleLaneRules
    {
        // In/Out encode travel headings, not the sides of the square tile.
        public static bool BlocksAcrossCell(int exit, int otherEnter, int otherExit, float progress, float otherProgress) =>
            otherExit != exit && otherEnter != (exit ^ 1) &&
            progress < 0.42f && otherProgress >= 0.25f && otherProgress <= 0.78f;

        // In a two-leg bend, a car leaving toward the approach uses the other lane.
        public static bool BlocksEntry(int approach, int otherEnter, int otherExit, float progress, float otherProgress, int connections) =>
            otherEnter != approach && otherEnter != (approach ^ 1) &&
            !(otherExit == (approach ^ 1) && connections == ((1 << (otherEnter ^ 1)) | (1 << otherExit))) &&
            1f - progress < 0.38f && otherProgress >= 0.12f && otherProgress <= 0.88f;

        // Spawn on the bend itself using the heading from its other connected side.
        public static int SpawnHeading(int exit, int connections, int degree)
        {
            if (degree != 2) return exit;
            for (int side = 0; side < 4; side++)
                if (side != exit && (connections & (1 << side)) != 0)
                    return side ^ 1;
            return exit;
        }
    }
}
