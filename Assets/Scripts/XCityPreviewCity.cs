using UnityEngine;

namespace XCity
{
    internal static class XCityPreviewCity
    {
        public static CitySimulation Create()
        {
            const int size = CitySimulation.GridSize;
            var cells = new CityCell[size * size];

            BuildingModel[,] displayBlock =
            {
                { BuildingModel.RowHouse, BuildingModel.Villa, BuildingModel.Apartment },
                { BuildingModel.Preschool, BuildingModel.PrimarySchool, BuildingModel.HighSchool },
                { BuildingModel.University, BuildingModel.Office, BuildingModel.OfficeTower },
                { BuildingModel.Market, BuildingModel.ShoppingMall, BuildingModel.Workshop },
                { BuildingModel.Warehouse, BuildingModel.Factory, BuildingModel.Default }
            };

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

                    if (x >= 7 && x < 12 && z >= 7 && z < 10)
                    {
                        BuildingModel model = displayBlock[x - 7, z - 7];
                        cells[idx] = PreviewBuilding(model);
                        continue;
                    }

                    if (x >= 19 && x <= 23 && z >= 7 && z <= 15)
                    {
                        cells[idx] = PreviewBuilding((x, z) switch
                        {
                            (19, 8) or (23, 9) => BuildingModel.OfficeTower,
                            (19, 11) or (23, 13) => BuildingModel.Market,
                            (20, 11) or (21, 11) or (22, 13) or (19, 14) => BuildingModel.HighStreet,
                            (22, 11) or (20, 13) => BuildingModel.ShoppingMall,
                            (23, 10) or (23, 15) => BuildingModel.Office,
                            _ => BuildingModel.Default
                        });
                        continue;
                    }

                    if (x >= 25 && x <= 29 && z >= 7 && z <= 15)
                    {
                        if ((x, z) == (29, 14))
                            cells[idx] = new CityCell { Type = CellType.PowerPlant, Level = 1, Height = CitySimulation.PowerPlantHeight };
                        else
                            cells[idx] = PreviewBuilding((x, z) switch
                            {
                                (26, 13) => BuildingModel.Preschool,
                                (27, 13) => BuildingModel.PrimarySchool,
                                (28, 13) => BuildingModel.HighSchool,
                                (29, 13) => BuildingModel.University,
                                (25, 11) => BuildingModel.PoliceStation,
                                (27, 11) => BuildingModel.Hospital,
                                (29, 11) => BuildingModel.FireStation,
                                _ => BuildingModel.Default
                            });
                        continue;
                    }

                    if (x >= 26 && z >= 25) continue;

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
                            cells[idx] = new CityCell { Type = CellType.PowerPlant, Level = 1, Height = CitySimulation.PowerPlantHeight };
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
                schemaVersion = CitySimulation.CurrentSchemaVersion,
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

        private static CityCell PreviewBuilding(BuildingModel model)
        {
            if (model == BuildingModel.Default) return default;
            var option = BuildingCatalog.Get(model);
            return new CityCell { Type = option.Type, Model = model, Height = option.Height, Level = 1 };
        }
    }
}
