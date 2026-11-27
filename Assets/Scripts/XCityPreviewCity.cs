using UnityEngine;

namespace XCity
{
    internal static class XCityPreviewCity
    {
        public static CitySimulation Create()
        {
            const int size = CitySimulation.GridSize;
            var cells = new CityCell[size * size];

            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    int idx = x * size + z;
                    if (x % 6 == 0 || z % 6 == 0)
                    {
                        cells[idx] = new CityCell { Type = CellType.Road };
                        continue;
                    }

                    uint h = BuildingMeshFactory.Hash(x, z, 777);
                    float u = (h & 0xFFFFu) / 65535f;

                    if (x < 16 && z < 16)
                    {
                        int level = 1 + (int)((h >> 16) % 3u);
                        float l1Height = 2.4f + u * 2.1f;
                        float height = level switch
                        {
                            1 => l1Height,
                            2 => Mathf.Min(l1Height + 0.9f, 7.5f),
                            _ => Mathf.Min(l1Height + 1.8f, 7.5f)
                        };
                        cells[idx] = new CityCell { Type = CellType.Residential, Level = level, Height = height };
                    }
                    else if (x >= 16 && z < 16)
                    {
                        float height = 4.0f + u * 3.5f;
                        cells[idx] = new CityCell { Type = CellType.Commercial, Level = 1, Height = height };
                    }
                    else if (x < 16 && z >= 16)
                    {
                        if ((x == 8 && z == 20) || (x == 9 && z == 20) || (x == 2 && z == 26))
                        {
                            cells[idx] = new CityCell { Type = CellType.PowerPlant, Level = 1, Height = 3.2f };
                        }
                        else
                        {
                            float height = 2.2f + u * 1.6f;
                            cells[idx] = new CityCell { Type = CellType.Industrial, Level = 1, Height = height };
                        }
                    }
                    else
                    {
                        bool undeveloped = ((h >> 16) % 4u) == 0u;
                        cells[idx] = undeveloped
                            ? new CityCell { Type = CellType.Residential, Level = 0, Height = 0f }
                            : new CityCell { Type = CellType.Residential, Level = 1, Height = 2.4f + u * 2.1f };
                    }
                }
            }

            var snapshot = new CitySnapshot
            {
                schemaVersion = 3,
                money = 50000,
                tick = 0,
                randomState = 12345,
                lastTaxIncome = 0,
                lastNetIncome = 0,
                cells = cells
            };

            if (CitySimulation.TryRestore(snapshot, out var city))
                return city;

            Debug.LogError("XCityPreviewCity snapshot failed TryRestore validation.");
            return new CitySimulation();
        }
    }
}
