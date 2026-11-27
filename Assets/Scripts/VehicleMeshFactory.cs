using System;
using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
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
        public bool IsTwoWheeler;
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

        private static readonly Color32 WheelDark = new Color32(28, 30, 34, 255);
        private static readonly Color32 WheelHub = new Color32(105, 110, 118, 255);
        private static readonly Color32 GlassDark = new Color32(40, 50, 65, 255);
        private static readonly Color32 HeadlightWarm = new Color32(255, 240, 180, 255);
        private static readonly Color32 TaillightRed = new Color32(210, 45, 45, 255);
        private static readonly Color32 StrobeBlue = new Color32(45, 130, 255, 255);
        private static readonly Color32 StrobeRed = new Color32(255, 45, 45, 255);
        private static readonly Color32 StrobeWhite = new Color32(245, 248, 255, 255);

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
            new Color32(210, 75, 60, 255)
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

        public static VehicleTemplate PickRandom(System.Random rng)
        {
            EnsureBuilt();
            int roll = rng.Next(_totalWeight);
            int acc = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                acc += _pool[i].Weight;
                if (roll < acc) return _pool[i].Template;
            }
            return _pool[0].Template;
        }

        private static void EnsureBuilt()
        {
            if (_pool != null) return;

            var list = new List<WeightedTemplate>(12);
            // Scooters: 25%
            list.Add(new WeightedTemplate(BuildScooter(new Color32(40, 150, 90, 255), ScooterShirtColors[0]), 13));
            list.Add(new WeightedTemplate(BuildScooter(new Color32(195, 55, 55, 255), ScooterShirtColors[1]), 12));

            // Sedans: 35% (7% each across 5 colours)
            for (int i = 0; i < SedanColors.Length; i++)
                list.Add(new WeightedTemplate(BuildSedan(SedanColors[i], false, false), 7));

            // Taxi: 8%
            list.Add(new WeightedTemplate(BuildSedan(new Color32(240, 190, 40, 255), true, false), 8));

            // Police Car: 5%
            list.Add(new WeightedTemplate(BuildSedan(new Color32(238, 240, 244, 255), false, true), 5));

            // Ambulance: 5%
            list.Add(new WeightedTemplate(BuildAmbulance(), 5));

            // Truck: 12%
            list.Add(new WeightedTemplate(BuildTruck(), 12));

            // Bus: 10%
            list.Add(new WeightedTemplate(BuildBus(), 10));

            _pool = list.ToArray();
            _totalWeight = 0;
            for (int i = 0; i < _pool.Length; i++)
                _totalWeight += _pool[i].Weight;
        }

        private sealed class PartTracker
        {
            private readonly List<byte> _parts = new List<byte>(256);
            public readonly Vector3[] Pivots = new Vector3[7];

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
            Vector3 bodySize = new Vector3(0.8f, 0.35f, 1.7f);
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
            Vector3 cabinSize = new Vector3(0.7f, 0.30f, 0.9f);
            b.AddBox(cabinCenter, cabinSize, bodyColor, bodyColor);

            b.AddFacadeRect(cabinCenter, cabinSize, 0, -0.29f, 0.29f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 1, -0.29f, 0.29f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.37f, 0.37f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.37f, 0.37f, 0.51f, 0.73f, GlassDark);

            if (isTaxi)
            {
                b.AddBox(new Vector3(0f, 0.81f, -0.02f), new Vector3(0.28f, 0.08f, 0.14f),
                    new Color32(250, 245, 220, 255), new Color32(250, 245, 220, 255));
            }

            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.32f, -0.16f, 0.22f, 0.34f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 0, 0.16f, 0.32f, 0.22f, 0.34f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.32f, -0.16f, 0.22f, 0.34f, TaillightRed);
            b.AddFacadeRect(bodyCenter, bodySize, 1, 0.16f, 0.32f, 0.22f, 0.34f, TaillightRed);
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

            return Bake(b, tracker, 6.0f, wheelRadius, 1.7f, false, isPolice, StrobeBlue, StrobeRed);
        }

        private static VehicleTemplate BuildAmbulance()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 white = new Color32(242, 244, 246, 255);
            Color32 stripeRed = new Color32(215, 48, 48, 255);

            // Van body 2.1 L x 0.82 W x 0.72 H
            Vector3 bodyCenter = new Vector3(0f, 0.50f, -0.04f);
            Vector3 bodySize = new Vector3(0.82f, 0.72f, 2.08f);
            b.AddBox(bodyCenter, bodySize, white, white);

            // Red stripe & cross on sides
            b.AddFacadeRect(bodyCenter, bodySize, 2, -0.92f, 0.45f, 0.36f, 0.46f, stripeRed);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -0.92f, 0.45f, 0.36f, 0.46f, stripeRed);
            b.AddFacadeRect(bodyCenter, bodySize, 2, -0.45f, -0.15f, 0.54f, 0.72f, stripeRed);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -0.45f, -0.15f, 0.54f, 0.72f, stripeRed);

            // Windshield & lights
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.34f, 0.34f, 0.50f, 0.78f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.34f, -0.18f, 0.22f, 0.34f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 0, 0.18f, 0.34f, 0.22f, 0.34f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.34f, -0.18f, 0.22f, 0.34f, TaillightRed);
            b.AddFacadeRect(bodyCenter, bodySize, 1, 0.18f, 0.34f, 0.22f, 0.34f, TaillightRed);
            tracker.AssignFrom(b, 0, PartBody);

            int sL = b.VertexCount;
            b.AddBox(new Vector3(-0.16f, 0.90f, 0.62f), new Vector3(0.24f, 0.08f, 0.16f), StrobeRed, StrobeRed);
            tracker.AssignFrom(b, sL, PartStrobeL);

            int sR = b.VertexCount;
            b.AddBox(new Vector3(0.16f, 0.90f, 0.62f), new Vector3(0.24f, 0.08f, 0.16f), StrobeWhite, StrobeWhite);
            tracker.AssignFrom(b, sR, PartStrobeR);

            const float wheelRadius = 0.12f;
            AddFourWheels(b, tracker, 0.37f, 0.65f, wheelRadius, 0.11f);

            return Bake(b, tracker, 6.2f, wheelRadius, 2.08f, false, true, StrobeRed, StrobeWhite);
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

            return Bake(b, tracker, 4.5f, wheelRadius, 2.4f, false, false, default, default);
        }

        private static VehicleTemplate BuildBus()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new PartTracker();

            Color32 busColor = new Color32(230, 130, 40, 255);
            Vector3 bodyCenter = new Vector3(0f, 0.59f, 0f);
            Vector3 bodySize = new Vector3(0.90f, 0.90f, 2.80f);
            b.AddBox(bodyCenter, bodySize, busColor, new Color32(240, 240, 235, 255));

            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.38f, 0.38f, 0.52f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.38f, 0.38f, 0.58f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 2, -1.24f, 1.24f, 0.56f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -1.24f, 1.24f, 0.56f, 0.88f, GlassDark);

            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.36f, -0.20f, 0.24f, 0.36f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 0, 0.20f, 0.36f, 0.24f, 0.36f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.36f, -0.20f, 0.24f, 0.36f, TaillightRed);
            b.AddFacadeRect(bodyCenter, bodySize, 1, 0.20f, 0.36f, 0.24f, 0.36f, TaillightRed);
            tracker.AssignFrom(b, 0, PartBody);

            const float wheelRadius = 0.13f;
            AddFourWheels(b, tracker, 0.39f, 0.90f, wheelRadius, 0.12f);

            return Bake(b, tracker, 4.0f, wheelRadius, 2.8f, false, false, default, default);
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

            return Bake(b, tracker, 5.5f, wheelRadius, 0.7f, true, false, default, default);
        }

        private static void AddFourWheels(MeshBuilder b, PartTracker tracker, float xOffset, float zOffset, float radius, float width)
        {
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, zOffset), radius, width, PartWheelFL);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, zOffset), radius, width, PartWheelFR);
            AddWheelPart(b, tracker, new Vector3(-xOffset, radius, -zOffset), radius, width, PartWheelRL);
            AddWheelPart(b, tracker, new Vector3(xOffset, radius, -zOffset), radius, width, PartWheelRR);
        }

        private static void AddWheelPart(MeshBuilder b, PartTracker tracker, Vector3 center, float radius, float width, byte partId)
        {
            int start = b.VertexCount;
            tracker.Pivots[partId] = center;
            b.AddWheelCylinder(center, radius, width, 6, WheelDark, WheelHub);
            tracker.AssignFrom(b, start, partId);
        }

        private static VehicleTemplate Bake(
            MeshBuilder b,
            PartTracker tracker,
            float speed,
            float wheelRadius,
            float length,
            bool isTwoWheeler,
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
                IsTwoWheeler = isTwoWheeler,
                HasEmergencyLights = hasEmergencyLights,
                StrobeLeftColor = strobeLeft,
                StrobeRightColor = strobeRight
            };
            UnityEngine.Object.Destroy(mesh);
            return tpl;
        }
    }
}
