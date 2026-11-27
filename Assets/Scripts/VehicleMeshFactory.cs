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
        public float Speed;
    }

    internal static class VehicleMeshFactory
    {
        private static readonly Color32 WheelDark = new Color32(25, 25, 25, 255);
        private static readonly Color32 GlassDark = new Color32(40, 50, 65, 255);
        private static readonly Color32 HeadlightWarm = new Color32(255, 240, 180, 255);
        private static readonly Color32 TaillightRed = new Color32(210, 45, 45, 255);

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
            new Color32(60, 165, 100, 255),
            new Color32(230, 180, 50, 255)
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

            var list = new List<WeightedTemplate>(10);
            // Scooters: 25% total (split across 2 variants)
            list.Add(new WeightedTemplate(BuildScooter(new Color32(40, 150, 90, 255), ScooterShirtColors[0]), 13));
            list.Add(new WeightedTemplate(BuildScooter(new Color32(195, 55, 55, 255), ScooterShirtColors[1]), 12));

            // Sedans: 45% total (9% each across 5 colours)
            for (int i = 0; i < SedanColors.Length; i++)
                list.Add(new WeightedTemplate(BuildSedan(SedanColors[i], false), 9));

            // Taxi: 8%
            list.Add(new WeightedTemplate(BuildSedan(new Color32(240, 190, 40, 255), true), 8));

            // Truck: 12%
            list.Add(new WeightedTemplate(BuildTruck(), 12));

            // Bus: 10%
            list.Add(new WeightedTemplate(BuildBus(), 10));

            _pool = list.ToArray();
            _totalWeight = 0;
            for (int i = 0; i < _pool.Length; i++)
                _totalWeight += _pool[i].Weight;
        }

        private static VehicleTemplate BuildSedan(Color32 bodyColor, bool isTaxi)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);

            // Body box: 1.7 L x 0.35 H x 0.8 W at y = 0.12..0.47
            Vector3 bodyCenter = new Vector3(0f, 0.295f, 0f);
            Vector3 bodySize = new Vector3(0.8f, 0.35f, 1.7f);
            b.AddBox(bodyCenter, bodySize, bodyColor, bodyColor);

            // Cabin: 0.9 L x 0.3 H x 0.7 W set slightly back
            Vector3 cabinCenter = new Vector3(0f, 0.62f, -0.08f);
            Vector3 cabinSize = new Vector3(0.7f, 0.30f, 0.9f);
            b.AddBox(cabinCenter, cabinSize, bodyColor, bodyColor);

            // Dark glass band on all 4 cabin sides
            b.AddFacadeRect(cabinCenter, cabinSize, 0, -0.29f, 0.29f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 1, -0.29f, 0.29f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 2, -0.37f, 0.37f, 0.51f, 0.73f, GlassDark);
            b.AddFacadeRect(cabinCenter, cabinSize, 3, -0.37f, 0.37f, 0.51f, 0.73f, GlassDark);

            if (isTaxi)
            {
                b.AddBox(new Vector3(0f, 0.81f, -0.02f), new Vector3(0.28f, 0.08f, 0.14f),
                    new Color32(250, 245, 220, 255), new Color32(250, 245, 220, 255));
            }

            // Headlights & taillights
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.32f, -0.16f, 0.22f, 0.34f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 0, 0.16f, 0.32f, 0.22f, 0.34f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.32f, -0.16f, 0.22f, 0.34f, TaillightRed);
            b.AddFacadeRect(bodyCenter, bodySize, 1, 0.16f, 0.32f, 0.22f, 0.34f, TaillightRed);

            // 4 wheels 0.18^3
            AddWheels(b, 0.35f, 0.50f, 0.18f);

            return Bake(b, 6.0f);
        }

        private static VehicleTemplate BuildTruck()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);

            Color32 cabColor = new Color32(50, 90, 160, 255);
            Color32 cargoColor = new Color32(230, 230, 225, 255);

            // Front cab
            Vector3 cabCenter = new Vector3(0f, 0.48f, 0.78f);
            Vector3 cabSize = new Vector3(0.76f, 0.72f, 0.76f);
            b.AddBox(cabCenter, cabSize, cabColor, cabColor);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.32f, 0.32f, 0.48f, 0.76f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 2, -0.25f, 0.25f, 0.48f, 0.74f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 3, -0.25f, 0.25f, 0.48f, 0.74f, GlassDark);
            b.AddFacadeRect(cabCenter, cabSize, 0, -0.30f, -0.16f, 0.20f, 0.32f, HeadlightWarm);
            b.AddFacadeRect(cabCenter, cabSize, 0, 0.16f, 0.30f, 0.20f, 0.32f, HeadlightWarm);

            // White cargo box 1.6 L x 0.9 H x 0.85 W
            b.AddBox(new Vector3(0f, 0.59f, -0.40f), new Vector3(0.85f, 0.90f, 1.60f), cargoColor, cargoColor);

            // 4 wheels
            AddWheels(b, 0.36f, 0.75f, 0.22f);

            return Bake(b, 4.5f);
        }

        private static VehicleTemplate BuildBus()
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);

            Color32 busColor = new Color32(230, 130, 40, 255);
            Vector3 bodyCenter = new Vector3(0f, 0.59f, 0f);
            Vector3 bodySize = new Vector3(0.90f, 0.90f, 2.80f);
            b.AddBox(bodyCenter, bodySize, busColor, new Color32(240, 240, 235, 255));

            // Window bands
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.38f, 0.38f, 0.52f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.38f, 0.38f, 0.58f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 2, -1.24f, 1.24f, 0.56f, 0.88f, GlassDark);
            b.AddFacadeRect(bodyCenter, bodySize, 3, -1.24f, 1.24f, 0.56f, 0.88f, GlassDark);

            // Lights
            b.AddFacadeRect(bodyCenter, bodySize, 0, -0.36f, -0.20f, 0.24f, 0.36f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 0, 0.20f, 0.36f, 0.24f, 0.36f, HeadlightWarm);
            b.AddFacadeRect(bodyCenter, bodySize, 1, -0.36f, -0.20f, 0.24f, 0.36f, TaillightRed);
            b.AddFacadeRect(bodyCenter, bodySize, 1, 0.20f, 0.36f, 0.24f, 0.36f, TaillightRed);

            AddWheels(b, 0.38f, 0.90f, 0.22f);

            return Bake(b, 4.0f);
        }

        private static VehicleTemplate BuildScooter(Color32 scooterColor, Color32 shirtColor)
        {
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);

            // Thin scooter body
            b.AddBox(new Vector3(0f, 0.22f, 0f), new Vector3(0.22f, 0.22f, 0.58f), scooterColor, scooterColor);
            // Front shield
            b.AddBox(new Vector3(0f, 0.42f, 0.22f), new Vector3(0.22f, 0.26f, 0.12f), scooterColor, scooterColor);
            // Wheels
            b.AddBox(new Vector3(0f, 0.09f, 0.24f), new Vector3(0.10f, 0.18f, 0.18f), WheelDark, WheelDark);
            b.AddBox(new Vector3(0f, 0.09f, -0.24f), new Vector3(0.10f, 0.18f, 0.18f), WheelDark, WheelDark);
            // Rider torso & helmet
            b.AddBox(new Vector3(0f, 0.50f, -0.04f), new Vector3(0.25f, 0.34f, 0.22f), shirtColor, shirtColor);
            b.AddBox(new Vector3(0f, 0.76f, -0.02f), new Vector3(0.18f, 0.18f, 0.18f),
                new Color32(240, 240, 240, 255), new Color32(240, 240, 240, 255));

            return Bake(b, 5.5f);
        }

        private static void AddWheels(MeshBuilder b, float xOffset, float zOffset, float size)
        {
            float y = size * 0.5f;
            Vector3 s = new Vector3(size, size, size);
            b.AddBox(new Vector3(-xOffset, y, zOffset), s, WheelDark, WheelDark);
            b.AddBox(new Vector3(xOffset, y, zOffset), s, WheelDark, WheelDark);
            b.AddBox(new Vector3(-xOffset, y, -zOffset), s, WheelDark, WheelDark);
            b.AddBox(new Vector3(xOffset, y, -zOffset), s, WheelDark, WheelDark);
        }

        private static VehicleTemplate Bake(MeshBuilder b, float speed)
        {
            var mesh = new Mesh();
            b.ApplyTo(mesh);
            var tpl = new VehicleTemplate
            {
                Vertices = mesh.vertices,
                Normals = mesh.normals,
                Colors = mesh.colors32,
                Triangles = mesh.triangles,
                Speed = speed
            };
            UnityEngine.Object.Destroy(mesh);
            return tpl;
        }
    }
}
