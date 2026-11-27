using System;
using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal enum VehicleCategory
    {
        Any,
        Residential,
        Commercial,
        Industrial,
        Civic
    }

    internal sealed class VehicleTemplate
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Color32[] Colors;
        public int[] Triangles;
        public byte[] PartIds;
        public Vector3[] PartPivots;
        public float Speed;
        public float WheelRadius;
        public float Length;
        public VehicleCategory Category;
        public string DisplayName;
        public bool IsFireTruck;
        public bool IsTwoWheeler;
        public bool HasTrailer;
        public bool HasEmergencyLights;
        public Color32 StrobeLeftColor;
        public Color32 StrobeRightColor;
    }

    internal static class VehicleMeshFactory
    {
        public const byte PartBody = 0;
        public const byte PartWheelFL = 1;
        public const byte PartWheelFR = 2;
        public const byte PartWheelRL = 3;
        public const byte PartWheelRR = 4;
        public const byte PartStrobeL = 5;
        public const byte PartStrobeR = 6;
        public const byte PartWheelML = 7;
        public const byte PartWheelMR = 8;
        public const byte PartTrailer = 9;
        public const byte PartTrailerWheelFL = 10;
        public const byte PartTrailerWheelFR = 11;
        public const byte PartTrailerWheelRL = 12;
        public const byte PartTrailerWheelRR = 13;
        public const int PartCount = 14;

        private static readonly Color32 WheelDark = new Color32(28, 30, 34, 255);
        private static readonly Color32 WheelHub = new Color32(118, 124, 132, 255);
        private static readonly Color32 ChassisDark = new Color32(44, 48, 54, 255);
        private static readonly Color32 ChromeSilver = new Color32(204, 210, 218, 255);
        private static readonly Color32 SteelGrey = new Color32(82, 90, 100, 255);
        private static readonly Color32 GlassDark = new Color32(40, 52, 68, 255);
        private static readonly Color32 HeadlightWarm = new Color32(255, 240, 180, 0);
        private static readonly Color32 TaillightRed = new Color32(215, 45, 45, 0);
        private static readonly Color32 StrobeBlue = new Color32(45, 130, 255, 0);
        private static readonly Color32 StrobeRed = new Color32(255, 45, 45, 0);
        private static readonly Color32 StrobeWhite = new Color32(245, 248, 255, 0);
        private static readonly Color32 StrobeAmber = new Color32(255, 178, 36, 0);

        private static readonly Color32[] SedanColors =
        {
            new Color32(235, 235, 235, 255), // white
            new Color32(190, 40, 40, 255),   // red
            new Color32(40, 80, 170, 255),   // blue
            new Color32(170, 175, 180, 255), // silver
            new Color32(50, 55, 60, 255)     // dark
        };

        private static readonly Color32[] ScooterShirtColors =
        {
            new Color32(45, 130, 200, 255),
            new Color32(210, 75, 60, 255),
            new Color32(235, 175, 50, 255)
        };

        private readonly struct WeightedTemplate
        {
            public readonly VehicleTemplate Template;
            public readonly int Weight;

            public WeightedTemplate(VehicleTemplate template, int weight)
            {
                Template = template;
                Weight = weight;
            }
        }

        private static WeightedTemplate[] _pool;
        private static int _totalWeight;
        private static WeightedTemplate[][] _categoryPools;
        private static int[] _categoryWeights;

        public static VehicleTemplate PickRandom(System.Random rng) =>
            PickForDistrict(rng, VehicleCategory.Any);

        public static VehicleTemplate PickForDistrict(System.Random rng, VehicleCategory district)
        {
            EnsureBuilt();
            int cat = (int)district;
            if (cat > 0 && cat < _categoryPools.Length && _categoryWeights[cat] > 0 && rng.Next(100) < 74)
                return PickFromPool(rng, _categoryPools[cat], _categoryWeights[cat]);
            return PickFromPool(rng, _pool, _totalWeight);
        }

        private static VehicleTemplate PickFromPool(System.Random rng, WeightedTemplate[] pool, int totalWeight)
        {
            int roll = rng.Next(totalWeight);
            int acc = 0;
            for (int i = 0; i < pool.Length; i++)
            {
                acc += pool[i].Weight;
                if (roll < acc) return pool[i].Template;
            }
            return pool[0].Template;
        }

        private static void EnsureBuilt()
        {
            if (_pool != null) return;

            var list = new List<WeightedTemplate>(32);

            // Scooters (Residential)
            list.Add(new WeightedTemplate(BuildScooter(new Color32(40, 150, 90, 255), ScooterShirtColors[0]), 6));
            list.Add(new WeightedTemplate(BuildScooter(new Color32(195, 55, 55, 255), ScooterShirtColors[1]), 6));
            list.Add(new WeightedTemplate(BuildScooter(new Color32(48, 118, 196, 255), ScooterShirtColors[2]), 5));

            // Sedans across 5 colors (Residential & general)
            for (int i = 0; i < SedanColors.Length; i++)
                list.Add(new WeightedTemplate(BuildSedan(SedanColors[i], false, false), 4));

            // Pickups & SUVs
            list.Add(new WeightedTemplate(BuildPickup(new Color32(178, 56, 46, 255), VehicleCategory.Residential), 5));
            list.Add(new WeightedTemplate(BuildPickup(new Color32(52, 92, 146, 255), VehicleCategory.Industrial), 4));
            list.Add(new WeightedTemplate(BuildSuv(new Color32(72, 84, 98, 255)), 5));
            list.Add(new WeightedTemplate(BuildSuv(new Color32(218, 222, 226, 255)), 4));

            // Taxi & Commercial Delivery Vans
            list.Add(new WeightedTemplate(BuildSedan(new Color32(240, 190, 40, 255), true, false), 6));
            list.Add(new WeightedTemplate(BuildDeliveryVan(new Color32(92, 62, 46, 255), new Color32(236, 186, 46, 255)), 5));
            list.Add(new WeightedTemplate(BuildDeliveryVan(new Color32(240, 244, 248, 255), new Color32(44, 126, 206, 255)), 5));

            // Civic & Emergency Fleet (Police, Ambulance, Fire Truck, School Bus)
            list.Add(new WeightedTemplate(BuildSedan(new Color32(238, 240, 244, 255), false, true), 5));
            list.Add(new WeightedTemplate(BuildAmbulance(), 5));
            list.Add(new WeightedTemplate(BuildFireTruck(), 6));
            list.Add(new WeightedTemplate(BuildSchoolBus(), 5));

            // Municipal & Transit
            list.Add(new WeightedTemplate(BuildBus(new Color32(230, 130, 40, 255)), 4));
            list.Add(new WeightedTemplate(BuildBus(new Color32(46, 132, 178, 255)), 4));
            list.Add(new WeightedTemplate(BuildGarbageTruck(), 5));

            // Heavy Commercial & Industrial Freight (Box Truck, Dump Truck, Tanker Semi, Container Semis x3)
            list.Add(new WeightedTemplate(BuildTruck(), 5));
            list.Add(new WeightedTemplate(BuildDumpTruck(), 5));
            list.Add(new WeightedTemplate(BuildTankerSemi(), 5));
            list.Add(new WeightedTemplate(BuildContainerSemi(
                new Color32(240, 242, 245, 255),
                new Color32(52, 146, 202, 255),
                new Color32(246, 248, 252, 255)), 5)); // Maersk blue
            list.Add(new WeightedTemplate(BuildContainerSemi(
                new Color32(48, 58, 74, 255),
                new Color32(204, 84, 42, 255),
                new Color32(244, 224, 198, 255)), 5)); // Corten rust-orange
            list.Add(new WeightedTemplate(BuildContainerSemi(
                new Color32(178, 44, 42, 255),
                new Color32(38, 134, 86, 255),
                new Color32(242, 246, 242, 255)), 5)); // Evergreen sea-green

            _pool = list.ToArray();
            _totalWeight = 0;
            for (int i = 0; i < _pool.Length; i++)
                _totalWeight += _pool[i].Weight;

            int categoryCount = 5;
            var catLists = new List<WeightedTemplate>[categoryCount];
            _categoryWeights = new int[categoryCount];
            for (int c = 0; c < categoryCount; c++)
                catLists[c] = new List<WeightedTemplate>(8);

            for (int i = 0; i < _pool.Length; i++)
            {
                int c = (int)_pool[i].Template.Category;
                if (c <= 0 || c >= categoryCount) continue;
                catLists[c].Add(_pool[i]);
                _categoryWeights[c] += _pool[i].Weight;
            }

            _categoryPools = new WeightedTemplate[categoryCount][];
            for (int c = 0; c < categoryCount; c++)
                _categoryPools[c] = catLists[c].ToArray();
        }

        private sealed class PartTracker
        {
            private readonly List<byte> _parts = new List<byte>(384);
            public readonly Vector3[] Pivots = new Vector3[PartCount];

            public void AssignFrom(MeshBuilder b, int startVert, byte partId)
            {
                int end = b.VertexCount;
                while (_parts.Count < end)
                    _parts.Add(partId);
                for (int i = startVert; i < end; i++)
                    _parts[i] = partId;
            }

            public byte[] ToArray(int vertexCount)
            {
                while (_parts.Count < vertexCount)
                    _parts.Add(PartBody);
                return _parts.ToArray();
            }
        }

        private static VehicleTemplate BuildSedan(Color32 bodyColor, bool isTaxi, bool isPolice)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Vector3 bodyCenter = new Vector3(0f, 0.295f, 0f);
            Vector3 bodySize = new Vector3(0.80f, 0.35f, 1.70f);
            if (isPolice)
            {
                b.AddBox(bodyCenter, bodySize, new Color32(34, 42, 56, 255), bodyColor);
                b.AddFacadeRect(bodyCenter, bodySize, 2, -0.35f, 0.35f, 0.14f, 0.45f, bodyColor);
                b.AddFacadeRect(bodyCenter, bodySize, 3, -0.35f, 0.35f, 0.14f, 0.45f, bodyColor);
            }
            else
            {
                b.AddBox(bodyCenter, bodySize, bodyColor, bodyColor);
            }

            Vector3 cabinCenter = new Vector3(0f, 0.62f, -0.08f);
            Vector3 cabinSize = new Vector3(0.70f, 0.30f, 0.90f);
            b.AddBox(cabinCenter, cabinSize, bodyColor, bodyColor);

            b.AddFacadeRect(cabinCenter, cabinSize, 0, -0.29f, 0.29f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 1, -0.29f, 0.29f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.37f, 0.37f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.37f, 0.37f, 0.51f, 0.73f, GlassDark);

            if (isTaxi)
            {
                b.AddBox(new Vector3(0f, 0.81f, -0.02f), new Vector3(0.28f, 0.08f, 0.14f),
                    new Color32(250, 232, 140, 0), new Color32(250, 232, 140, 0));
            }

            AddHeadAndTailLights(b, bodyCenter, bodySize, 0.22f, 0.34f);
            tracker.AssignFrom(b, 0, PartBody);

            if (isPolice)
            {
                int sL = b.VertexCount;
                b.AddBox(new Vector3(-0.15f, 0.81f, -0.04f), new Vector3(0.24f, 0.08f, 0.14f), StrobeBlue, StrobeBlue);
                tracker.AssignFrom(b, sL, PartStrobeL);

                int sR = b.VertexCount;
                b.AddBox(new Vector3(0.15f, 0.81f, -0.04f), new Vector3(0.24f, 0.08f, 0.14f), StrobeRed, StrobeRed);
                tracker.AssignFrom(b, sR, PartStrobeR);
            }

            const float wheelRadius = 0.11f;
            AddFourWheels(b, tracker, 0.36f, 0.50f, wheelRadius, 0.10f);

            VehicleCategory cat = isPolice ? VehicleCategory.Civic : (isTaxi ? VehicleCategory.Commercial : VehicleCategory.Residential);
            return Bake(b, tracker, 6.0f, wheelRadius, 1.70f, cat, false, false, isPolice, StrobeBlue, StrobeRed);
        }

        private static VehicleTemplate BuildPickup(Color32 bodyColor, VehicleCategory category)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 crateWood = new Color32(174, 128, 76, 255);
            Vector3 hoodCenter = new Vector3(0f, 0.36f, 0.36f);
            Vector3 hoodSize = new Vector3(0.82f, 0.42f, 1.16f);
            b.AddBox(hoodCenter, hoodSize, bodyColor, bodyColor);

            // Cab
            Vector3 cabCenter = new Vector3(0f, 0.69f, 0.24f);
            Vector3 cabSize = new Vector3(0.76f, 0.30f, 0.62f);
            b.AddBox(cabCenter, cabSize, bodyColor, bodyColor);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.31f, 0.31f, 0.58f, 0.80f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 1, -0.31f, 0.31f, 0.58f, 0.80f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.24f, 0.24f, 0.58f, 0.80f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.24f, 0.24f, 0.58f, 0.80f, GlassDark);

            // Open bed walls + cargo crates
            b.AddBox(new Vector3(0f, 0.26f, -0.58f), new Vector3(0.82f, 0.20f, 0.78f), bodyColor, ChassisDark);
            b.AddBox(new Vector3(-0.37f, 0.43f, -0.58f), new Vector3(0.08f, 0.16f, 0.78f), bodyColor, bodyColor);
            b.AddBox(new Vector3(0.37f, 0.43f, -0.58f), new Vector3(0.08f, 0.16f, 0.78f), bodyColor, bodyColor);
            b.AddBox(new Vector3(0f, 0.43f, -0.93f), new Vector3(0.82f, 0.16f, 0.08f), bodyColor, bodyColor);
            b.AddBox(new Vector3(-0.10f, 0.45f, -0.52f), new Vector3(0.36f, 0.22f, 0.40f), crateWood, Darken(crateWood, 1.08f));

            b.AddFacadeRect(hoodCenter, hoodSize, 0, -0.34f, -0.18f, 0.26f, 0.38f, HeadlightWarm);
            b.AddFacadeRect(hoodCenter, hoodSize, 0, 0.18f, 0.34f, 0.26f, 0.38f, HeadlightWarm);
            Vector3 bedTailCenter = new Vector3(0f, 0.33f, -0.58f);
            Vector3 bedTailSize = new Vector3(0.82f, 0.34f, 0.78f);
            b.AddFacadeRect(bedTailCenter, bedTailSize, 1, -0.34f, -0.20f, 0.28f, 0.40f, TaillightRed);
            b.AddFacadeRect(bedTailCenter, bedTailSize, 1, 0.20f, 0.34f, 0.28f, 0.40f, TaillightRed);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.125f;
            AddFourWheels(b, tracker, 0.38f, 0.58f, wheelRadius, 0.11f);
            return Bake(b, tracker, 5.7f, wheelRadius, 1.94f, category, false, false, false, default, default);
        }

        private static VehicleTemplate BuildSuv(Color32 bodyColor)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Vector3 bodyCenter = new Vector3(0f, 0.34f, 0f);
            Vector3 bodySize = new Vector3(0.84f, 0.38f, 1.84f);
            b.AddBox(bodyCenter, bodySize, bodyColor, bodyColor);

            Vector3 cabinCenter = new Vector3(0f, 0.69f, -0.14f);
            Vector3 cabinSize = new Vector3(0.76f, 0.34f, 1.24f);
            b.AddBox(cabinCenter, cabinSize, bodyColor, bodyColor);
            b.AddFacadeRect(cabinCenter, cabinSize, 0, -0.32f, 0.32f, 0.56f, 0.81f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 1, -0.32f, 0.32f, 0.56f, 0.81f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.54f, 0.54f, 0.56f, 0.81f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.54f, 0.54f, 0.56f, 0.81f, GlassDark);

            // Roof rack rails & rear spare wheel cover
            b.AddBox(new Vector3(-0.30f, 0.88f, -0.16f), new Vector3(0.05f, 0.04f, 0.94f), ChassisDark, ChassisDark);
            b.AddBox(new Vector3(0.30f, 0.88f, -0.16f), new Vector3(0.05f, 0.04f, 0.94f), ChassisDark, ChassisDark);
            b.AddBox(new Vector3(0f, 0.44f, -0.96f), new Vector3(0.32f, 0.32f, 0.10f), ChassisDark, ChassisDark);

            AddHeadAndTailLights(b, bodyCenter, bodySize, 0.24f, 0.36f);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.125f;
            AddFourWheels(b, tracker, 0.38f, 0.56f, wheelRadius, 0.11f);
            return Bake(b, tracker, 5.8f, wheelRadius, 1.84f, VehicleCategory.Residential, false, false, false, default, default);
        }

        private static VehicleTemplate BuildAmbulance()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 white = new Color32(242, 244, 246, 255);
            Color32 stripeRed = new Color32(215, 48, 48, 255);

            Vector3 bodyCenter = new Vector3(0f, 0.50f, -0.04f);
            Vector3 bodySize = new Vector3(0.82f, 0.72f, 2.08f);
            b.AddBox(bodyCenter, bodySize, white, white);

            b.AddFacadeRect(bodyCenter, bodySize, 2, -0.92f, 0.45f, 0.36f, 0.46f, stripeRed);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -0.92f, 0.45f, 0.36f, 0.46f, stripeRed);
            b.AddFacadeRect(bodyCenter, bodySize, 2, -0.45f, -0.15f, 0.54f, 0.72f, stripeRed);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -0.45f, -0.15f, 0.54f, 0.72f, stripeRed);

            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.34f, 0.34f, 0.50f, 0.78f, GlassDark);
            AddHeadAndTailLights(b, bodyCenter, bodySize, 0.22f, 0.34f);
            tracker.AssignFrom(b, 0, PartBody);

            int sL = b.VertexCount;
            b.AddBox(new Vector3(-0.16f, 0.90f, 0.62f), new Vector3(0.24f, 0.08f, 0.16f), StrobeRed, StrobeRed);
            tracker.AssignFrom(b, sL, PartStrobeL);

            int sR = b.VertexCount;
            b.AddBox(new Vector3(0.16f, 0.90f, 0.62f), new Vector3(0.24f, 0.08f, 0.16f), StrobeWhite, StrobeWhite);
            tracker.AssignFrom(b, sR, PartStrobeR);

            const float wheelRadius = 0.12f;
            AddFourWheels(b, tracker, 0.37f, 0.65f, wheelRadius, 0.11f);

            return Bake(b, tracker, 6.2f, wheelRadius, 2.08f, VehicleCategory.Civic, false, false, true, StrobeRed, StrobeWhite);
        }

        private static VehicleTemplate BuildFireTruck()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 fireRed = new Color32(208, 36, 32, 255);
            Color32 fireDark = new Color32(154, 24, 24, 255);
            Color32 whiteCap = new Color32(244, 246, 248, 255);
            Color32 hazardYellow = new Color32(246, 204, 46, 255);
            Color32 shutterSilver = new Color32(186, 194, 204, 255);

            // Lower chassis apron
            b.AddBox(new Vector3(0f, 0.21f, -0.02f), new Vector3(0.84f, 0.14f, 2.70f), ChassisDark, ChassisDark);

            // Double crew cab at front
            Vector3 cabCenter = new Vector3(0f, 0.56f, 0.92f);
            Vector3 cabSize = new Vector3(0.86f, 0.72f, 0.84f);
            b.AddBox(cabCenter, cabSize, fireRed, whiteCap);
            b.AddBox(new Vector3(0f, 0.94f, 0.90f), new Vector3(0.84f, 0.05f, 0.78f), whiteCap, whiteCap);

            // Heavy chrome bumper & grille
            b.AddBox(new Vector3(0f, 0.22f, 1.38f), new Vector3(0.90f, 0.14f, 0.08f), ChromeSilver, ChromeSilver);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.24f, 0.24f, 0.28f, 0.48f, ChromeSilver);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.36f, 0.36f, 0.54f, 0.84f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.34f, 0.32f, 0.56f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.34f, 0.32f, 0.56f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.38f, -0.26f, 0.28f, 0.40f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.26f, 0.38f, 0.28f, 0.40f, HeadlightWarm);

            // Midship pump control panel
            b.AddBox(new Vector3(0f, 0.52f, 0.40f), new Vector3(0.84f, 0.62f, 0.22f), SteelGrey, ChassisDark);
            b.AddBox(new Vector3(0f, 0.58f, 0.40f), new Vector3(0.88f, 0.22f, 0.14f), ChromeSilver, SteelGrey);

            // Rear compartment body with 3 silver roll-up equipment bays per side
            Vector3 rearCenter = new Vector3(0f, 0.55f, -0.52f);
            Vector3 rearSize = new Vector3(0.88f, 0.68f, 1.64f);
            b.AddBox(rearCenter, rearSize, fireRed, fireDark);
            for (int bay = -1; bay <= 1; bay++)
            {
                float uCenter = bay * 0.52f;
                b.AddFacadeRect(rearCenter, rearSize, 2, uCenter - 0.20f, uCenter + 0.20f, 0.28f, 0.78f, shutterSilver);
                b.AddFacadeRect(rearCenter, rearSize, 3, uCenter - 0.20f, uCenter + 0.20f, 0.28f, 0.78f, shutterSilver);
            }
            // Reflective yellow/white safety stripe on sides & rear
            b.AddFacadeRect(rearCenter, rearSize, 2, -0.78f, 0.78f, 0.22f, 0.27f, hazardYellow);
            b.AddFacadeRect(rearCenter, rearSize, 3, -0.78f, 0.78f, 0.22f, 0.27f, hazardYellow);
            b.AddFacadeRect(rearCenter, rearSize, 1, -0.36f, 0.36f, 0.24f, 0.36f, hazardYellow);
            b.AddFacadeRect(rearCenter, rearSize, 1, -0.38f, -0.22f, 0.26f, 0.38f, TaillightRed);
            b.AddFacadeRect(rearCenter, rearSize, 1, 0.22f, 0.38f, 0.26f, 0.38f, TaillightRed);

            // Turntable pedestal & aerial extension ladder
            b.AddBox(new Vector3(0f, 0.93f, -0.84f), new Vector3(0.46f, 0.08f, 0.46f), SteelGrey, ChassisDark);
            b.AddBox(new Vector3(-0.15f, 1.02f, -0.04f), new Vector3(0.055f, 0.11f, 2.34f), ChromeSilver, whiteCap);
            b.AddBox(new Vector3(0.15f, 1.02f, -0.04f), new Vector3(0.055f, 0.11f, 2.34f), ChromeSilver, whiteCap);
            for (int rung = 0; rung < 9; rung++)
            {
                float rz = -1.08f + rung * 0.26f;
                b.AddBox(new Vector3(0f, 1.00f, rz), new Vector3(0.28f, 0.045f, 0.05f), ChromeSilver, ChromeSilver);
            }
            // Ladder nozzle / tip platform
            b.AddBox(new Vector3(0f, 1.05f, 1.12f), new Vector3(0.32f, 0.10f, 0.16f), fireRed, ChromeSilver);
            tracker.AssignFrom(b, 0, PartBody);

            // Alternating emergency lightbars (cab front & rear deck)
            int sL = b.VertexCount;
            b.AddBox(new Vector3(-0.22f, 1.00f, 1.16f), new Vector3(0.24f, 0.08f, 0.14f), StrobeRed, StrobeRed);
            b.AddBox(new Vector3(-0.32f, 0.94f, -1.26f), new Vector3(0.14f, 0.08f, 0.12f), StrobeRed, StrobeRed);
            tracker.AssignFrom(b, sL, PartStrobeL);

            int sR = b.VertexCount;
            b.AddBox(new Vector3(0.22f, 1.00f, 1.16f), new Vector3(0.24f, 0.08f, 0.14f), StrobeWhite, StrobeWhite);
            b.AddBox(new Vector3(0.32f, 0.94f, -1.26f), new Vector3(0.14f, 0.08f, 0.12f), StrobeWhite, StrobeWhite);
            tracker.AssignFrom(b, sR, PartStrobeR);

            const float wheelRadius = 0.13f;
            AddSixWheels(b, tracker, 0.39f, 0.94f, -0.58f, -0.96f, wheelRadius, 0.12f);

            return Bake(b, tracker, 5.6f, wheelRadius, 2.76f, VehicleCategory.Civic, false, false, true, StrobeRed, StrobeWhite);
        }

        private static VehicleTemplate BuildContainerSemi(Color32 tractorColor, Color32 containerColor, Color32 stripeColor)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 containerDark = Darken(containerColor, 0.78f);
            Color32 containerTop = Darken(containerColor, 1.06f);
            const float kingpinZ = 0.48f;
            tracker.Pivots[PartTrailer] = new Vector3(0f, 0f, kingpinZ);

            // Tractor frame & fifth wheel plate
            b.AddBox(new Vector3(0f, 0.21f, 0.94f), new Vector3(0.56f, 0.14f, 1.32f), ChassisDark, ChassisDark);
            b.AddBox(new Vector3(0f, 0.31f, kingpinZ), new Vector3(0.46f, 0.06f, 0.32f), SteelGrey, SteelGrey);

            // Heavy tractor cab
            Vector3 cabCenter = new Vector3(0f, 0.59f, 1.18f);
            Vector3 cabSize = new Vector3(0.84f, 0.74f, 0.78f);
            b.AddBox(cabCenter, cabSize, tractorColor, tractorColor);

            // Aerodynamic roof wind deflector & sun visor
            b.AddBox(new Vector3(0f, 1.03f, 1.06f), new Vector3(0.78f, 0.15f, 0.48f), tractorColor, tractorColor);
            b.AddBox(new Vector3(0f, 0.87f, 1.59f), new Vector3(0.78f, 0.06f, 0.05f), ChassisDark, ChassisDark);

            // Front bumper, chrome grille, windshield & windows
            b.AddBox(new Vector3(0f, 0.22f, 1.60f), new Vector3(0.88f, 0.14f, 0.08f), ChromeSilver, ChromeSilver);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.24f, 0.24f, 0.28f, 0.50f, ChromeSilver);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.35f, 0.35f, 0.56f, 0.84f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.26f, 0.32f, 0.58f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.26f, 0.32f, 0.58f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.37f, -0.26f, 0.28f, 0.40f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.26f, 0.37f, 0.28f, 0.40f, HeadlightWarm);

            // Fuel tanks & twin vertical chrome exhaust stacks behind sleeper
            b.AddBox(new Vector3(-0.34f, 0.25f, 0.68f), new Vector3(0.14f, 0.14f, 0.34f), ChromeSilver, ChromeSilver);
            b.AddBox(new Vector3(0.34f, 0.25f, 0.68f), new Vector3(0.14f, 0.14f, 0.34f), ChromeSilver, ChromeSilver);
            b.AddBox(new Vector3(-0.35f, 0.76f, 0.73f), new Vector3(0.065f, 0.76f, 0.065f), ChromeSilver, ChassisDark);
            b.AddBox(new Vector3(0.35f, 0.76f, 0.73f), new Vector3(0.065f, 0.76f, 0.065f), ChromeSilver, ChassisDark);
            tracker.AssignFrom(b, 0, PartBody);

            // Articulated container trailer
            int trailerStart = b.VertexCount;
            b.AddBox(new Vector3(0f, 0.29f, -0.54f), new Vector3(0.76f, 0.10f, 2.16f), ChassisDark, ChassisDark);
            b.AddBox(new Vector3(-0.26f, 0.18f, 0.16f), new Vector3(0.08f, 0.18f, 0.08f), SteelGrey, SteelGrey);
            b.AddBox(new Vector3(0.26f, 0.18f, 0.16f), new Vector3(0.08f, 0.18f, 0.08f), SteelGrey, SteelGrey);
            b.AddBox(new Vector3(0f, 0.21f, -1.61f), new Vector3(0.84f, 0.10f, 0.06f), ChromeSilver, ChassisDark);

            // 40ft ribbed ISO container (gap between z=+0.55 and exhaust at z=+0.73)
            Vector3 boxCenter = new Vector3(0f, 0.75f, -0.52f);
            Vector3 boxSize = new Vector3(0.86f, 0.82f, 2.14f);
            b.AddBox(boxCenter, boxSize, containerColor, containerTop);

            // Corner casting frames & corrugation ribs
            b.AddBox(new Vector3(0f, 0.75f, 0.54f), new Vector3(0.88f, 0.84f, 0.06f), containerDark, containerDark);
            b.AddBox(new Vector3(0f, 0.75f, -1.58f), new Vector3(0.88f, 0.84f, 0.06f), containerDark, containerDark);
            for (int r = -3; r <= 3; r++)
            {
                float u = r * 0.26f;
                b.AddFacadeRect(boxCenter, boxSize, 2, u - 0.055f, u + 0.055f, 0.40f, 1.12f, containerDark);
                b.AddFacadeRect(boxCenter, boxSize, 3, u - 0.055f, u + 0.055f, 0.40f, 1.12f, containerDark);
            }
            // Shipping line stripe + rear door locking bars
            b.AddFacadeRect(boxCenter, boxSize, 2, -0.78f, 0.78f, 0.68f, 0.82f, stripeColor);
            b.AddFacadeRect(boxCenter, boxSize, 3, -0.78f, 0.78f, 0.68f, 0.82f, stripeColor);
            b.AddFacadeRect(boxCenter, boxSize, 1, -0.14f, -0.07f, 0.42f, 1.10f, ChromeSilver);
            b.AddFacadeRect(boxCenter, boxSize, 1, 0.07f, 0.14f, 0.42f, 1.10f, ChromeSilver);

            Vector3 tailBarCenter = new Vector3(0f, 0.29f, -0.54f);
            Vector3 tailBarSize = new Vector3(0.84f, 0.12f, 2.16f);
            b.AddFacadeRect(tailBarCenter, tailBarSize, 1, -0.36f, -0.22f, 0.22f, 0.32f, TaillightRed);
            b.AddFacadeRect(tailBarCenter, tailBarSize, 1, 0.22f, 0.36f, 0.22f, 0.32f, TaillightRed);
            tracker.AssignFrom(b, trailerStart, PartTrailer);

            const float wheelRadius = 0.135f;
            AddWheelPart(b, tracker, new Vector3(-0.39f, wheelRadius, 1.28f), wheelRadius, 0.12f, PartWheelFL);
            AddWheelPart(b, tracker, new Vector3(0.39f, wheelRadius, 1.28f), wheelRadius, 0.12f, PartWheelFR);
            AddWheelPart(b, tracker, new Vector3(-0.38f, wheelRadius, kingpinZ), wheelRadius, 0.16f, PartWheelML);
            AddWheelPart(b, tracker, new Vector3(0.38f, wheelRadius, kingpinZ), wheelRadius, 0.16f, PartWheelMR);

            AddWheelPart(b, tracker, new Vector3(-0.38f, wheelRadius, -0.94f), wheelRadius, 0.14f, PartTrailerWheelFL);
            AddWheelPart(b, tracker, new Vector3(0.38f, wheelRadius, -0.94f), wheelRadius, 0.14f, PartTrailerWheelFR);
            AddWheelPart(b, tracker, new Vector3(-0.38f, wheelRadius, -1.28f), wheelRadius, 0.14f, PartTrailerWheelRL);
            AddWheelPart(b, tracker, new Vector3(0.38f, wheelRadius, -1.28f), wheelRadius, 0.14f, PartTrailerWheelRR);

            return Bake(b, tracker, 4.3f, wheelRadius, 3.28f, VehicleCategory.Industrial, false, true, false, default, default);
        }

        private static VehicleTemplate BuildTankerSemi()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 cabOrange = new Color32(224, 118, 36, 255);
            Color32 tankSilver = new Color32(226, 232, 238, 255);
            Color32 tankEnd = new Color32(182, 190, 200, 255);
            Color32 hazmatRed = new Color32(204, 48, 44, 255);
            const float kingpinZ = 0.48f;
            tracker.Pivots[PartTrailer] = new Vector3(0f, 0f, kingpinZ);

            // Tractor
            b.AddBox(new Vector3(0f, 0.21f, 0.94f), new Vector3(0.56f, 0.14f, 1.32f), ChassisDark, ChassisDark);
            b.AddBox(new Vector3(0f, 0.31f, kingpinZ), new Vector3(0.46f, 0.06f, 0.32f), SteelGrey, SteelGrey);

            Vector3 cabCenter = new Vector3(0f, 0.58f, 1.18f);
            Vector3 cabSize = new Vector3(0.84f, 0.72f, 0.76f);
            b.AddBox(cabCenter, cabSize, cabOrange, cabOrange);
            b.AddBox(new Vector3(0f, 0.22f, 1.59f), new Vector3(0.88f, 0.14f, 0.08f), ChromeSilver, ChromeSilver);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.24f, 0.24f, 0.28f, 0.48f, ChassisDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.35f, 0.35f, 0.55f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.26f, 0.30f, 0.57f, 0.80f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.26f, 0.30f, 0.57f, 0.80f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.37f, -0.25f, 0.27f, 0.39f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.25f, 0.37f, 0.27f, 0.39f, HeadlightWarm);
            b.AddBox(new Vector3(-0.34f, 0.74f, 0.74f), new Vector3(0.06f, 0.70f, 0.06f), ChromeSilver, ChassisDark);
            b.AddBox(new Vector3(0.34f, 0.74f, 0.74f), new Vector3(0.06f, 0.70f, 0.06f), ChromeSilver, ChassisDark);
            tracker.AssignFrom(b, 0, PartBody);

            // Articulated Tank Trailer
            int trailerStart = b.VertexCount;
            b.AddBox(new Vector3(0f, 0.29f, -0.54f), new Vector3(0.76f, 0.10f, 2.12f), ChassisDark, ChassisDark);
            b.AddBox(new Vector3(-0.26f, 0.18f, 0.16f), new Vector3(0.08f, 0.18f, 0.08f), SteelGrey, SteelGrey);
            b.AddBox(new Vector3(0.26f, 0.18f, 0.16f), new Vector3(0.08f, 0.18f, 0.08f), SteelGrey, SteelGrey);
            b.AddBox(new Vector3(0f, 0.21f, -1.59f), new Vector3(0.84f, 0.10f, 0.06f), ChromeSilver, ChassisDark);

            AddHorizontalCylinderZ(b, new Vector3(0f, 0.73f, -0.52f), 0.42f, 0.37f, 2.04f, 8, tankSilver, tankEnd);
            // Side hazard band & top catwalk hatches
            b.AddBox(new Vector3(0f, 0.73f, -0.52f), new Vector3(0.86f, 0.12f, 1.72f), hazmatRed, tankSilver);
            b.AddBox(new Vector3(0f, 1.12f, -0.52f), new Vector3(0.24f, 0.04f, 1.76f), SteelGrey, SteelGrey);
            for (int d = -1; d <= 1; d++)
                b.AddCylinder(new Vector3(0f, 1.14f, -0.52f + d * 0.56f), 0.10f, 0.10f, 0.05f, 6, ChromeSilver, true, SteelGrey);

            Vector3 tailBarCenter = new Vector3(0f, 0.29f, -0.54f);
            Vector3 tailBarSize = new Vector3(0.84f, 0.12f, 2.12f);
            b.AddFacadeRect(tailBarCenter, tailBarSize, 1, -0.36f, -0.22f, 0.22f, 0.32f, TaillightRed);
            b.AddFacadeRect(tailBarCenter, tailBarSize, 1, 0.22f, 0.36f, 0.22f, 0.32f, TaillightRed);
            tracker.AssignFrom(b, trailerStart, PartTrailer);

            const float wheelRadius = 0.135f;
            AddWheelPart(b, tracker, new Vector3(-0.39f, wheelRadius, 1.26f), wheelRadius, 0.12f, PartWheelFL);
            AddWheelPart(b, tracker, new Vector3(0.39f, wheelRadius, 1.26f), wheelRadius, 0.12f, PartWheelFR);
            AddWheelPart(b, tracker, new Vector3(-0.38f, wheelRadius, kingpinZ), wheelRadius, 0.16f, PartWheelML);
            AddWheelPart(b, tracker, new Vector3(0.38f, wheelRadius, kingpinZ), wheelRadius, 0.16f, PartWheelMR);

            AddWheelPart(b, tracker, new Vector3(-0.38f, wheelRadius, -0.92f), wheelRadius, 0.14f, PartTrailerWheelFL);
            AddWheelPart(b, tracker, new Vector3(0.38f, wheelRadius, -0.92f), wheelRadius, 0.14f, PartTrailerWheelFR);
            AddWheelPart(b, tracker, new Vector3(-0.38f, wheelRadius, -1.26f), wheelRadius, 0.14f, PartTrailerWheelRL);
            AddWheelPart(b, tracker, new Vector3(0.38f, wheelRadius, -1.26f), wheelRadius, 0.14f, PartTrailerWheelRR);

            return Bake(b, tracker, 4.4f, wheelRadius, 3.22f, VehicleCategory.Industrial, false, true, false, default, default);
        }

        private static VehicleTemplate BuildDumpTruck()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 yellow = new Color32(234, 174, 28, 255);
            Color32 bedGrey = new Color32(110, 118, 128, 255);
            Color32 sand = new Color32(198, 174, 124, 255);

            b.AddBox(new Vector3(0f, 0.21f, -0.02f), new Vector3(0.68f, 0.14f, 2.38f), ChassisDark, ChassisDark);

            Vector3 cabCenter = new Vector3(0f, 0.56f, 0.80f);
            Vector3 cabSize = new Vector3(0.80f, 0.68f, 0.72f);
            b.AddBox(cabCenter, cabSize, yellow, yellow);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.32f, 0.32f, 0.54f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.24f, 0.24f, 0.56f, 0.78f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.24f, 0.24f, 0.56f, 0.78f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.34f, -0.20f, 0.28f, 0.40f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.20f, 0.34f, 0.28f, 0.40f, HeadlightWarm);

            // Dump bed + cab overhang canopy + sand load
            Vector3 bedCenter = new Vector3(0f, 0.56f, -0.42f);
            Vector3 bedSize = new Vector3(0.86f, 0.56f, 1.50f);
            b.AddBox(bedCenter, bedSize, bedGrey, sand);
            b.AddBox(new Vector3(0f, 0.92f, 0.52f), new Vector3(0.84f, 0.06f, 0.44f), bedGrey, bedGrey);
            b.AddBox(new Vector3(0f, 0.90f, -0.42f), new Vector3(0.74f, 0.14f, 1.24f), sand, Darken(sand, 1.06f));
            b.AddFacadeRect(bedCenter, bedSize, 2, -0.64f, 0.64f, 0.48f, 0.58f, yellow);
            b.AddFacadeRect(bedCenter, bedSize, 3, -0.64f, 0.64f, 0.48f, 0.58f, yellow);
            b.AddFacadeRect(bedCenter, bedSize, 1, -0.34f, -0.20f, 0.30f, 0.40f, TaillightRed);
            b.AddFacadeRect(bedCenter, bedSize, 1, 0.20f, 0.34f, 0.30f, 0.40f, TaillightRed);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.13f;
            AddSixWheels(b, tracker, 0.38f, 0.78f, -0.48f, -0.86f, wheelRadius, 0.12f);

            return Bake(b, tracker, 4.6f, wheelRadius, 2.46f, VehicleCategory.Industrial, false, false, false, default, default);
        }

        private static VehicleTemplate BuildGarbageTruck()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 green = new Color32(40, 138, 78, 255);
            Color32 bodyWhite = new Color32(234, 238, 234, 255);

            b.AddBox(new Vector3(0f, 0.21f, -0.02f), new Vector3(0.70f, 0.14f, 2.40f), ChassisDark, ChassisDark);

            Vector3 cabCenter = new Vector3(0f, 0.56f, 0.84f);
            Vector3 cabSize = new Vector3(0.82f, 0.70f, 0.72f);
            b.AddBox(cabCenter, cabSize, green, green);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.34f, 0.34f, 0.52f, 0.82f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.24f, 0.24f, 0.54f, 0.78f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.24f, 0.24f, 0.54f, 0.78f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.34f, -0.20f, 0.26f, 0.38f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.20f, 0.34f, 0.26f, 0.38f, HeadlightWarm);

            // Compactor body + rear loader hopper
            Vector3 compCenter = new Vector3(0f, 0.62f, -0.32f);
            Vector3 compSize = new Vector3(0.86f, 0.78f, 1.38f);
            b.AddBox(compCenter, compSize, bodyWhite, bodyWhite);
            b.AddFacadeRect(compCenter, compSize, 2, -0.60f, 0.60f, 0.44f, 0.58f, green);
            b.AddFacadeRect(compCenter, compSize, 3, -0.60f, 0.60f, 0.44f, 0.58f, green);
            b.AddBox(new Vector3(0f, 0.54f, -1.14f), new Vector3(0.84f, 0.62f, 0.30f), green, green);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.13f;
            AddSixWheels(b, tracker, 0.38f, 0.82f, -0.46f, -0.84f, wheelRadius, 0.12f);

            return Bake(b, tracker, 4.2f, wheelRadius, 2.50f, VehicleCategory.Residential, false, false, false, default, default);
        }

        private static VehicleTemplate BuildTruck()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 cabColor = new Color32(50, 90, 160, 255);
            Color32 cargoColor = new Color32(230, 230, 225, 255);

            Vector3 cabCenter = new Vector3(0f, 0.48f, 0.78f);
            Vector3 cabSize = new Vector3(0.76f, 0.72f, 0.76f);
            b.AddBox(cabCenter, cabSize, cabColor, cabColor);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.32f, 0.32f, 0.48f, 0.76f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.25f, 0.25f, 0.48f, 0.74f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.25f, 0.25f, 0.48f, 0.74f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.30f, -0.16f, 0.20f, 0.32f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.16f, 0.30f, 0.20f, 0.32f, HeadlightWarm);

            b.AddBox(new Vector3(0f, 0.59f, -0.40f), new Vector3(0.85f, 0.90f, 1.60f), cargoColor, cargoColor);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.13f;
            AddFourWheels(b, tracker, 0.37f, 0.75f, wheelRadius, 0.12f);

            return Bake(b, tracker, 4.5f, wheelRadius, 2.40f, VehicleCategory.Industrial, false, false, false, default, default);
        }

        private static VehicleTemplate BuildDeliveryVan(Color32 bodyColor, Color32 stripeColor)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Vector3 bodyCenter = new Vector3(0f, 0.49f, -0.02f);
            Vector3 bodySize = new Vector3(0.80f, 0.70f, 2.02f);
            b.AddBox(bodyCenter, bodySize, bodyColor, Darken(bodyColor, 1.05f));

            b.AddFacadeRect(bodyCenter, bodySize, 2, -0.88f, 0.48f, 0.36f, 0.48f, stripeColor);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -0.88f, 0.48f, 0.36f, 0.48f, stripeColor);
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.34f, 0.34f, 0.48f, 0.76f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 2, 0.52f, 0.90f, 0.50f, 0.76f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 3, 0.52f, 0.90f, 0.50f, 0.76f, GlassDark);
            AddHeadAndTailLights(b, bodyCenter, bodySize, 0.20f, 0.32f);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.12f;
            AddFourWheels(b, tracker, 0.36f, 0.64f, wheelRadius, 0.11f);
            return Bake(b, tracker, 5.6f, wheelRadius, 2.02f, VehicleCategory.Commercial, false, false, false, default, default);
        }

        private static VehicleTemplate BuildBus(Color32 busColor)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Vector3 bodyCenter = new Vector3(0f, 0.59f, 0f);
            Vector3 bodySize = new Vector3(0.88f, 0.90f, 2.76f);
            b.AddBox(bodyCenter, bodySize, busColor, new Color32(240, 240, 235, 255));

            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.36f, 0.36f, 0.52f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.36f, 0.36f, 0.58f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 2, -1.20f, 1.20f, 0.56f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -1.20f, 1.20f, 0.56f, 0.88f, GlassDark);

            AddHeadAndTailLights(b, bodyCenter, bodySize, 0.24f, 0.36f);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.13f;
            AddFourWheels(b, tracker, 0.39f, 0.88f, wheelRadius, 0.12f);

            return Bake(b, tracker, 4.1f, wheelRadius, 2.76f, VehicleCategory.Commercial, false, false, false, default, default);
        }

        private static VehicleTemplate BuildSchoolBus()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 yellow = new Color32(244, 184, 28, 255);
            Color32 roofWhite = new Color32(242, 244, 246, 255);

            // Front engine nose + passenger coach
            b.AddBox(new Vector3(0f, 0.36f, 1.10f), new Vector3(0.76f, 0.42f, 0.46f), yellow, yellow);
            Vector3 coachCenter = new Vector3(0f, 0.58f, -0.18f);
            Vector3 coachSize = new Vector3(0.86f, 0.86f, 2.20f);
            b.AddBox(coachCenter, coachSize, yellow, roofWhite);

            b.AddFacadeRect(coachCenter, coachSize, 0, -0.36f, 0.36f, 0.58f, 0.88f, GlassDark);
            b.AddFacadeRect(coachCenter, coachSize, 1, -0.32f, 0.32f, 0.60f, 0.88f, GlassDark);
            b.AddFacadeRect(coachCenter, coachSize, 2, -0.96f, 0.96f, 0.58f, 0.86f, GlassDark);
            b.AddFacadeRect(coachCenter, coachSize, 3, -0.96f, 0.96f, 0.58f, 0.86f, GlassDark);
            b.AddFacadeRect(coachCenter, coachSize, 2, -1.04f, 1.04f, 0.42f, 0.48f, ChassisDark);
            b.AddFacadeRect(coachCenter, coachSize, 3, -1.04f, 1.04f, 0.42f, 0.48f, ChassisDark);

            b.AddBox(new Vector3(-0.24f, 0.30f, 1.34f), new Vector3(0.14f, 0.10f, 0.03f), HeadlightWarm, HeadlightWarm);
            b.AddBox(new Vector3(0.24f, 0.30f, 1.34f), new Vector3(0.14f, 0.10f, 0.03f), HeadlightWarm, HeadlightWarm);
            b.AddBox(new Vector3(-0.26f, 0.34f, -1.29f), new Vector3(0.14f, 0.10f, 0.03f), TaillightRed, TaillightRed);
            b.AddBox(new Vector3(0.26f, 0.34f, -1.29f), new Vector3(0.14f, 0.10f, 0.03f), TaillightRed, TaillightRed);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.13f;
            AddFourWheels(b, tracker, 0.38f, 0.82f, wheelRadius, 0.12f);
            return Bake(b, tracker, 4.3f, wheelRadius, 2.66f, VehicleCategory.Civic, false, false, false, default, default);
        }

        private static VehicleTemplate BuildScooter(Color32 scooterColor, Color32 shirtColor)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            b.AddBox(new Vector3(0f, 0.22f, 0f), new Vector3(0.22f, 0.22f, 0.58f), scooterColor, scooterColor);
            b.AddBox(new Vector3(0f, 0.42f, 0.22f), new Vector3(0.22f, 0.26f, 0.12f), scooterColor, scooterColor);
            b.AddBox(new Vector3(0f, 0.50f, -0.04f), new Vector3(0.25f, 0.34f, 0.22f), shirtColor, shirtColor);
            b.AddBox(new Vector3(0f, 0.76f, -0.02f), new Vector3(0.18f, 0.18f, 0.18f),
                new Color32(240, 240, 240, 255), new Color32(240, 240, 240, 255));
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.09f;
            AddWheelPart(b, tracker, new Vector3(0f, wheelRadius, 0.24f), wheelRadius, 0.09f, PartWheelFL);
            AddWheelPart(b, tracker, new Vector3(0f, wheelRadius, -0.24f), wheelRadius, 0.09f, PartWheelRL);

            return Bake(b, tracker, 5.5f, wheelRadius, 0.70f, VehicleCategory.Residential, true, false, false, default, default);
        }

        private static void AddHeadAndTailLights(MeshBuilder b, Vector3 bodyCenter, Vector3 bodySize, float y0, float y1)
        {
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.34f, -0.18f, y0, y1, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 0, 0.18f, 0.34f, y0, y1, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.34f, -0.18f, y0, y1, TaillightRed);
            b.AddFacadeRect(bodyCenter, bodySize, 1, 0.18f, 0.34f, y0, y1, TaillightRed);
        }

        private static void AddFourWheels(MeshBuilder b, PartTracker tracker, float xOffset, float zOffset, float radius, float width)
        {
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, zOffset), radius, width, PartWheelFL);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, zOffset), radius, width, PartWheelFR);
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, -zOffset), radius, width, PartWheelRL);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, -zOffset), radius, width, PartWheelRR);
        }

        private static void AddSixWheels(MeshBuilder b, PartTracker tracker, float xOffset, float zFront, float zMid, float zRear, float radius, float width)
        {
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, zFront), radius, width, PartWheelFL);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, zFront), radius, width, PartWheelFR);
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, zMid), radius, width, PartWheelML);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, zMid), radius, width, PartWheelMR);
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, zRear), radius, width, PartWheelRL);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, zRear), radius, width, PartWheelRR);
        }

        private static void AddWheelPart(MeshBuilder b, PartTracker tracker, Vector3 center, float radius, float width, byte partId)
        {
            int start = b.VertexCount;
            tracker.Pivots[partId] = center;
            b.AddWheelCylinder(center, radius, width, 6, WheelDark, WheelHub);
            tracker.AssignFrom(b, start, partId);
        }

        private static void AddHorizontalCylinderZ(
            MeshBuilder b,
            Vector3 center,
            float radiusX,
            float radiusY,
            float length,
            int segments,
            Color32 bodyColor,
            Color32 capColor)
        {
            float z0 = center.z - length * 0.5f;
            float z1 = center.z + length * 0.5f;
            Vector3 backCenter = new Vector3(center.x, center.y, z0);
            Vector3 frontCenter = new Vector3(center.x, center.y, z1);
            float step = Mathf.PI * 2f / segments;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                float mid = (a0 + a1) * 0.5f;

                float x0 = center.x + Mathf.Cos(a0) * radiusX;
                float y0 = center.y + Mathf.Sin(a0) * radiusY;
                float x1 = center.x + Mathf.Cos(a1) * radiusX;
                float y1 = center.y + Mathf.Sin(a1) * radiusY;

                Vector3 b0 = new Vector3(x0, y0, z0);
                Vector3 b1 = new Vector3(x1, y1, z0);
                Vector3 f0 = new Vector3(x0, y0, z1);
                Vector3 f1 = new Vector3(x1, y1, z1);
                Vector3 outward = new Vector3(Mathf.Cos(mid), Mathf.Sin(mid), 0f);

                b.AddQuad(b0, b1, f1, f0, outward, bodyColor);
                b.AddTriangle(frontCenter, f0, f1, Vector3.forward, capColor);
                b.AddTriangle(backCenter, b1, b0, Vector3.back, capColor);
            }
        }

        private static Color32 Darken(Color32 c, float factor)
        {
            byte Scale(byte v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * factor), 0, 255);
            return new Color32(Scale(c.r), Scale(c.g), Scale(c.b), 255);
        }

        private static VehicleTemplate Bake(
            MeshBuilder b,
            PartTracker tracker,
            float speed,
            float wheelRadius,
            float length,
            VehicleCategory category,
            bool isTwoWheeler,
            bool hasTrailer,
            bool hasEmergencyLights,
            Color32 strobeLeft,
            Color32 strobeRight)
        {
            var mesh = new Mesh();
            b.ApplyTo(mesh);
            Vector3[] verts = mesh.vertices;
            var tpl = new VehicleTemplate
            {
                Vertices = verts,
                Normals = mesh.normals,
                Colors = mesh.colors32,
                Triangles = mesh.triangles,
                PartIds = tracker.ToArray(verts.Length),
                PartPivots = tracker.Pivots,
                Speed = speed,
                WheelRadius = wheelRadius,
                Length = length,
                Category = category,
                IsFireTruck = hasEmergencyLights && strobeLeft.r > 200 && length > 2.4f,
                DisplayName = ResolveDisplayName(category, length, isTwoWheeler, hasTrailer, hasEmergencyLights, strobeLeft),
                IsTwoWheeler = isTwoWheeler,
                HasTrailer = hasTrailer,
                HasEmergencyLights = hasEmergencyLights,
                StrobeLeftColor = strobeLeft,
                StrobeRightColor = strobeRight
            };
            UnityEngine.Object.Destroy(mesh);
            return tpl;
        }

        private static string ResolveDisplayName(
            VehicleCategory category,
            float length,
            bool isTwoWheeler,
            bool hasTrailer,
            bool emergency,
            Color32 strobeLeft)
        {
            if (isTwoWheeler) return "Xe máy";
            if (emergency)
            {
                if (length > 2.4f) return "Xe cứu hỏa";
                return strobeLeft.b > strobeLeft.r ? "Xe cảnh sát" : "Xe cấp cứu";
            }
            if (hasTrailer) return length >= 3.26f ? "Xe đầu kéo Container" : "Xe bồn nhiên liệu";
            if (category == VehicleCategory.Civic) return "Xe buýt trường học";
            if (length >= 2.70f) return "Xe buýt nội đô";
            if (length >= 2.48f) return category == VehicleCategory.Residential ? "Xe chở rác" : "Xe tải ben";
            if (length >= 2.35f) return "Xe tải thùng";
            if (length >= 2.00f) return "Xe Van giao hàng";
            if (length >= 1.80f) return length >= 1.90f ? "Xe bán tải" : "Xe SUV";
            return category == VehicleCategory.Commercial ? "Taxi" : "Xe con";
        }
    }
}
