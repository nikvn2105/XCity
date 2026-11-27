using System;
using System.Collections.Generic;
using UnityEngine;

namespace XCity
{
    internal enum CitizenGroup
    {
        Residential,
        Commercial,
        Industrial,
        Civic
    }

    internal sealed class CitizenTemplate
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Color32[] Colors;
        public int[] Triangles;
        public byte[] PartIds;
        public Vector3[] Pivots;
        public float WalkSpeed;
        public float StrideFreq;
        public float LegSwingDeg;
        public float ArmSwingDeg;
        public float RightArmSwingScale;
        public float BaseTorsoPitch;
        public bool UsesCane;
        public CitizenGroup Group;
    }

    internal static class CitizenMeshFactory
    {
        public const byte PartTorso = 0;
        public const byte PartHead = 1;
        public const byte PartLeftArm = 2;
        public const byte PartRightArm = 3;
        public const byte PartLeftLeg = 4;
        public const byte PartRightLeg = 5;
        public const byte PartShadow = 6;

        private static readonly Color32 ShadowTint = new Color32(42, 46, 50, 255);
        private static readonly Color32 SkinLight = new Color32(240, 202, 170, 255);
        private static readonly Color32 SkinWarm = new Color32(222, 178, 138, 255);
        private static readonly Color32 SkinTan = new Color32(196, 146, 108, 255);
        private static readonly Color32 HairDark = new Color32(44, 38, 36, 255);
        private static readonly Color32 HairBrown = new Color32(96, 62, 40, 255);
        private static readonly Color32 HairSilver = new Color32(215, 218, 224, 255);
        private static readonly Color32 WhiteColor = new Color32(242, 244, 246, 255);
        private static readonly Color32 NavyColor = new Color32(36, 52, 84, 255);
        private static readonly Color32 CharcoalColor = new Color32(58, 64, 74, 255);
        private static readonly Color32 CaneWood = new Color32(130, 88, 50, 255);

        private static CitizenTemplate[] _all;
        private static CitizenTemplate[][] _byGroup;

        public static CitizenTemplate PickForGroup(System.Random rng, CitizenGroup group)
        {
            EnsureBuilt();
            if (rng.Next(100) < 65)
            {
                CitizenTemplate[] groupList = _byGroup[(int)group];
                if (groupList.Length > 0)
                    return groupList[rng.Next(groupList.Length)];
            }
            return _all[rng.Next(_all.Length)];
        }

        private static void EnsureBuilt()
        {
            if (_all != null) return;

            var list = new List<CitizenTemplate>(20)
            {
                // 1-2: Children / Students
                BuildChild(false),
                BuildChild(true),
                // 3-4: Young Adults (Jogger & University Student)
                BuildJogger(),
                BuildUniversityStudent(),
                // 5-6: Elderly (with walking canes)
                BuildElderly(false),
                BuildElderly(true),
                // 7-8: Office Workers (Male Suit & Female Blazer)
                BuildOfficeWorker(false),
                BuildOfficeWorker(true),
                // 9: Construction Worker
                BuildConstructionWorker(),
                // 10: Doctor / Medical
                BuildDoctor(),
                // 11: Police Officer
                BuildPoliceOfficer(),
                // 12: Firefighter
                BuildFirefighter(),
                // 13-14: Chef & Market Vendor
                BuildChef(),
                BuildVendor(),
                // 15: Delivery Courier
                BuildCourier(),
                // 16: Factory Engineer
                BuildEngineer(),
                // 17-18: Casual Citizens
                BuildCasual(new Color32(215, 86, 74, 255), new Color32(66, 98, 148, 255), SkinLight, HairBrown),
                BuildCasual(new Color32(72, 166, 138, 255), new Color32(54, 58, 66, 255), SkinTan, HairDark)
            };

            _all = list.ToArray();
            var buckets = new List<CitizenTemplate>[4];
            for (int i = 0; i < 4; i++) buckets[i] = new List<CitizenTemplate>(8);
            for (int i = 0; i < _all.Length; i++)
            {
                CitizenTemplate tpl = _all[i];
                buckets[(int)tpl.Group].Add(tpl);
                if (tpl.Group == CitizenGroup.Civic)
                {
                    buckets[(int)CitizenGroup.Residential].Add(tpl);
                    buckets[(int)CitizenGroup.Commercial].Add(tpl);
                }
            }

            _byGroup = new CitizenTemplate[4][];
            for (int i = 0; i < 4; i++) _byGroup[i] = buckets[i].ToArray();
        }

        private sealed class RigTracker
        {
            private readonly List<byte> _parts = new List<byte>(192);
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
                    _parts.Add(PartTorso);
                return _parts.ToArray();
            }
        }

        private struct RigSpec
        {
            public float Scale;
            public Color32 Skin;
            public Color32 Hair;
            public Color32 Torso;
            public Color32 Pants;
            public Color32 Sleeve;
            public Color32 Shoes;
            public float WalkSpeed;
            public float StrideFreq;
            public float LegSwingDeg;
            public float ArmSwingDeg;
            public float RightArmSwingScale;
            public float BaseTorsoPitch;
            public bool UsesCane;
            public CitizenGroup Group;
        }

        private static RigSpec DefaultAdultSpec(CitizenGroup group) => new RigSpec
        {
            Scale = 1.0f,
            Skin = SkinWarm,
            Hair = HairDark,
            Torso = WhiteColor,
            Pants = CharcoalColor,
            Sleeve = WhiteColor,
            Shoes = HairDark,
            WalkSpeed = 1.12f,
            StrideFreq = 7.4f,
            LegSwingDeg = 30f,
            ArmSwingDeg = 28f,
            RightArmSwingScale = 1.0f,
            BaseTorsoPitch = 0f,
            UsesCane = false,
            Group = group
        };

        private static CitizenTemplate BuildChild(bool girl)
        {
            var spec = DefaultAdultSpec(CitizenGroup.Residential);
            spec.Scale = 0.69f;
            spec.Skin = SkinLight;
            spec.Hair = HairDark;
            spec.Torso = WhiteColor;
            spec.Sleeve = WhiteColor;
            spec.Pants = NavyColor;
            spec.WalkSpeed = 1.35f;
            spec.StrideFreq = 9.6f;
            spec.LegSwingDeg = 38f;
            spec.ArmSwingDeg = 36f;

            return BuildRig(spec, (b, t, s) =>
            {
                // Red pioneer neckerchief
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.17f * s, 0.06f * s),
                    new Vector3(0.11f * s, 0.06f * s, 0.03f * s),
                    new Color32(215, 42, 42, 255), new Color32(215, 42, 42, 255));
                // School backpack
                Color32 pack = girl ? new Color32(232, 108, 152, 255) : new Color32(48, 118, 205, 255);
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.11f * s, -0.08f * s),
                    new Vector3(0.14f * s, 0.16f * s, 0.07f * s), pack, pack);
            }, (b, t, s) =>
            {
                if (girl)
                {
                    b.AddBox(t.Pivots[PartHead] + new Vector3(-0.08f * s, 0.11f * s, -0.02f * s),
                        new Vector3(0.05f * s, 0.06f * s, 0.05f * s), HairDark, HairDark);
                    b.AddBox(t.Pivots[PartHead] + new Vector3(0.08f * s, 0.11f * s, -0.02f * s),
                        new Vector3(0.05f * s, 0.06f * s, 0.05f * s), HairDark, HairDark);
                }
                else
                {
                    Color32 cap = new Color32(242, 196, 44, 255);
                    b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.13f * s, 0f),
                        new Vector3(0.14f * s, 0.045f * s, 0.14f * s), cap, cap);
                    b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.115f * s, 0.07f * s),
                        new Vector3(0.12f * s, 0.02f * s, 0.05f * s), cap, cap);
                }
            }, null);
        }

        private static CitizenTemplate BuildJogger()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Residential);
            spec.Scale = 0.94f;
            spec.Skin = SkinWarm;
            spec.Hair = HairBrown;
            spec.Torso = new Color32(44, 174, 210, 255);
            spec.Sleeve = new Color32(44, 174, 210, 255);
            spec.Pants = new Color32(42, 46, 54, 255);
            spec.Shoes = new Color32(240, 112, 40, 255);
            spec.WalkSpeed = 1.95f;
            spec.StrideFreq = 10.6f;
            spec.LegSwingDeg = 45f;
            spec.ArmSwingDeg = 48f;
            spec.BaseTorsoPitch = 6f;

            return BuildRig(spec, null, (b, t, s) =>
            {
                Color32 band = new Color32(245, 108, 36, 255);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.09f * s, 0f),
                    new Vector3(0.136f * s, 0.026f * s, 0.136f * s), band, band);
            }, null);
        }

        private static CitizenTemplate BuildUniversityStudent()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Commercial);
            spec.Scale = 0.95f;
            spec.Skin = SkinLight;
            spec.Hair = HairDark;
            spec.Torso = new Color32(228, 172, 68, 255);
            spec.Sleeve = new Color32(228, 172, 68, 255);
            spec.Pants = new Color32(62, 92, 138, 255);
            spec.WalkSpeed = 1.18f;

            return BuildRig(spec, (b, t, s) =>
            {
                // Messenger bag
                b.AddBox(t.Pivots[PartTorso] + new Vector3(-0.09f * s, 0.05f * s, -0.02f * s),
                    new Vector3(0.05f * s, 0.13f * s, 0.14f * s), new Color32(72, 54, 42, 255), new Color32(72, 54, 42, 255));
            }, null, null);
        }

        private static CitizenTemplate BuildElderly(bool female)
        {
            var spec = DefaultAdultSpec(CitizenGroup.Residential);
            spec.Scale = female ? 0.88f : 0.91f;
            spec.Skin = SkinLight;
            spec.Hair = HairSilver;
            spec.Torso = female ? new Color32(154, 88, 128, 255) : new Color32(174, 152, 118, 255);
            spec.Sleeve = spec.Torso;
            spec.Pants = new Color32(68, 66, 64, 255);
            spec.WalkSpeed = 0.72f;
            spec.StrideFreq = 5.4f;
            spec.LegSwingDeg = 18f;
            spec.ArmSwingDeg = 18f;
            spec.RightArmSwingScale = 0.55f;
            spec.BaseTorsoPitch = 8f;
            spec.UsesCane = true;

            return BuildRig(spec, null, female ? (Action<MeshBuilder, RigTracker, float>)((b, t, s) =>
            {
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.09f * s, -0.075f * s),
                    new Vector3(0.07f * s, 0.07f * s, 0.06f * s), HairSilver, HairSilver);
            }) : null, (b, t, s) =>
            {
                // Walking cane held in right hand reaching down near ground
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0.01f * s, -0.24f * s, 0.05f * s),
                    new Vector3(0.024f * s, 0.34f * s, 0.024f * s), CaneWood, CaneWood);
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0.01f * s, -0.07f * s, 0.03f * s),
                    new Vector3(0.024f * s, 0.025f * s, 0.06f * s), CaneWood, CaneWood);
            });
        }

        private static CitizenTemplate BuildOfficeWorker(bool female)
        {
            var spec = DefaultAdultSpec(CitizenGroup.Commercial);
            spec.Scale = female ? 0.97f : 1.0f;
            spec.Skin = SkinWarm;
            spec.Hair = female ? HairBrown : HairDark;
            spec.Torso = female ? new Color32(58, 72, 92, 255) : NavyColor;
            spec.Sleeve = spec.Torso;
            spec.Pants = female ? new Color32(46, 108, 118, 255) : new Color32(40, 48, 64, 255);
            spec.RightArmSwingScale = 0.35f;

            return BuildRig(spec, (b, t, s) =>
            {
                // Shirt + tie on front
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.14f * s, 0.055f * s),
                    new Vector3(0.06f * s, 0.12f * s, 0.015f * s), WhiteColor, WhiteColor);
                if (!female)
                {
                    b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.12f * s, 0.064f * s),
                        new Vector3(0.022f * s, 0.11f * s, 0.012f * s), new Color32(195, 42, 42, 255), new Color32(195, 42, 42, 255));
                }
            }, null, (b, t, s) =>
            {
                // Briefcase in right hand
                Color32 leather = new Color32(114, 72, 42, 255);
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0.02f * s, -0.22f * s, 0f),
                    new Vector3(0.05f * s, 0.12f * s, 0.15f * s), leather, leather);
            });
        }

        private static CitizenTemplate BuildConstructionWorker()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Industrial);
            spec.Torso = new Color32(236, 134, 34, 255);
            spec.Sleeve = new Color32(62, 104, 158, 255);
            spec.Pants = new Color32(48, 76, 126, 255);
            spec.RightArmSwingScale = 0.45f;

            return BuildRig(spec, (b, t, s) =>
            {
                // Reflective grey band on vest
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.09f * s, 0f),
                    new Vector3(0.195f * s, 0.038f * s, 0.118f * s),
                    new Color32(215, 222, 226, 255), new Color32(215, 222, 226, 255));
            }, (b, t, s) =>
            {
                Color32 helmet = new Color32(246, 206, 44, 255);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.135f * s, 0f),
                    new Vector3(0.155f * s, 0.055f * s, 0.155f * s), helmet, helmet);
            }, (b, t, s) =>
            {
                // Hammer in right hand
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0f, -0.20f * s, 0.04f * s),
                    new Vector3(0.025f * s, 0.025f * s, 0.12f * s), CaneWood, CaneWood);
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0f, -0.20f * s, 0.10f * s),
                    new Vector3(0.06f * s, 0.04f * s, 0.035f * s), CharcoalColor, CharcoalColor);
            });
        }

        private static CitizenTemplate BuildDoctor()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Civic);
            spec.Scale = 0.98f;
            spec.Skin = SkinLight;
            spec.Torso = WhiteColor;
            spec.Sleeve = WhiteColor;
            spec.Pants = new Color32(58, 134, 168, 255);
            spec.RightArmSwingScale = 0.40f;

            return BuildRig(spec, (b, t, s) =>
            {
                // Blue scrubs collar & red medical cross
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.16f * s, 0.056f * s),
                    new Vector3(0.065f * s, 0.07f * s, 0.014f * s),
                    new Color32(58, 134, 168, 255), new Color32(58, 134, 168, 255));
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0.05f * s, 0.15f * s, 0.058f * s),
                    new Vector3(0.032f * s, 0.032f * s, 0.012f * s),
                    new Color32(215, 42, 42, 255), new Color32(215, 42, 42, 255));
            }, null, (b, t, s) =>
            {
                // Clipboard in right hand
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0.01f * s, -0.19f * s, 0.05f * s),
                    new Vector3(0.025f * s, 0.11f * s, 0.09f * s), CaneWood, WhiteColor);
            });
        }

        private static CitizenTemplate BuildPoliceOfficer()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Civic);
            spec.Scale = 1.02f;
            spec.Torso = new Color32(42, 82, 56, 255);
            spec.Sleeve = spec.Torso;
            spec.Pants = new Color32(36, 68, 46, 255);

            return BuildRig(spec, (b, t, s) =>
            {
                // Utility belt & gold badge
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.02f * s, 0f),
                    new Vector3(0.195f * s, 0.035f * s, 0.118f * s), HairDark, HairDark);
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0.048f * s, 0.15f * s, 0.058f * s),
                    new Vector3(0.028f * s, 0.032f * s, 0.012f * s),
                    new Color32(242, 198, 48, 255), new Color32(242, 198, 48, 255));
            }, (b, t, s) =>
            {
                Color32 cap = new Color32(34, 64, 44, 255);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.135f * s, 0f),
                    new Vector3(0.15f * s, 0.05f * s, 0.15f * s), cap, cap);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.11f * s, 0.075f * s),
                    new Vector3(0.12f * s, 0.02f * s, 0.05f * s), HairDark, HairDark);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.14f * s, 0.078f * s),
                    new Vector3(0.028f * s, 0.028f * s, 0.012f * s),
                    new Color32(242, 198, 48, 255), new Color32(242, 198, 48, 255));
            }, null);
        }

        private static CitizenTemplate BuildFirefighter()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Civic);
            spec.Scale = 1.02f;
            spec.Torso = new Color32(168, 40, 38, 255);
            spec.Sleeve = spec.Torso;
            spec.Pants = new Color32(48, 52, 58, 255);

            return BuildRig(spec, (b, t, s) =>
            {
                // Yellow reflective stripes
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.07f * s, 0f),
                    new Vector3(0.195f * s, 0.036f * s, 0.118f * s),
                    new Color32(244, 212, 48, 255), new Color32(244, 212, 48, 255));
            }, (b, t, s) =>
            {
                Color32 helmet = new Color32(215, 44, 38, 255);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.135f * s, 0f),
                    new Vector3(0.155f * s, 0.055f * s, 0.155f * s), helmet, helmet);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.115f * s, -0.08f * s),
                    new Vector3(0.15f * s, 0.02f * s, 0.06f * s), helmet, helmet);
            }, null);
        }

        private static CitizenTemplate BuildChef()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Commercial);
            spec.Scale = 0.98f;
            spec.Torso = WhiteColor;
            spec.Sleeve = WhiteColor;
            spec.Pants = new Color32(52, 56, 64, 255);

            return BuildRig(spec, (b, t, s) =>
            {
                // Apron front
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.07f * s, 0.055f * s),
                    new Vector3(0.14f * s, 0.15f * s, 0.015f * s),
                    new Color32(226, 228, 232, 255), new Color32(226, 228, 232, 255));
            }, (b, t, s) =>
            {
                // Tall chef toque
                b.AddCylinder(t.Pivots[PartHead] + new Vector3(0f, 0.11f * s, 0f),
                    0.065f * s, 0.082f * s, 0.11f * s, 6, WhiteColor, true, WhiteColor);
            }, null);
        }

        private static CitizenTemplate BuildVendor()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Commercial);
            spec.Scale = 0.95f;
            spec.Skin = SkinTan;
            spec.Torso = new Color32(54, 152, 138, 255);
            spec.Sleeve = spec.Torso;
            spec.Pants = HairDark;
            spec.RightArmSwingScale = 0.35f;

            return BuildRig(spec, null, (b, t, s) =>
            {
                // Conical straw hat
                Color32 straw = new Color32(228, 198, 124, 255);
                b.AddCylinder(t.Pivots[PartHead] + new Vector3(0f, 0.115f * s, 0f),
                    0.15f * s, 0.02f * s, 0.085f * s, 8, straw, true, straw);
            }, (b, t, s) =>
            {
                // Small market basket
                Color32 basket = new Color32(182, 136, 74, 255);
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0.02f * s, -0.21f * s, 0f),
                    new Vector3(0.09f * s, 0.09f * s, 0.11f * s), basket, new Color32(95, 165, 78, 255));
            });
        }

        private static CitizenTemplate BuildCourier()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Commercial);
            spec.Scale = 0.98f;
            spec.Skin = SkinTan;
            spec.Torso = new Color32(46, 174, 86, 255);
            spec.Sleeve = spec.Torso;
            spec.Pants = CharcoalColor;
            spec.WalkSpeed = 1.28f;

            return BuildRig(spec, (b, t, s) =>
            {
                // Thermal delivery backpack
                Color32 boxGreen = new Color32(34, 146, 72, 255);
                b.AddBox(t.Pivots[PartTorso] + new Vector3(0f, 0.12f * s, -0.11f * s),
                    new Vector3(0.18f * s, 0.20f * s, 0.14f * s), boxGreen, new Color32(236, 240, 242, 255));
            }, (b, t, s) =>
            {
                Color32 helmet = new Color32(34, 146, 72, 255);
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.125f * s, 0f),
                    new Vector3(0.155f * s, 0.065f * s, 0.155f * s), helmet, helmet);
            }, null);
        }

        private static CitizenTemplate BuildEngineer()
        {
            var spec = DefaultAdultSpec(CitizenGroup.Industrial);
            spec.Torso = new Color32(52, 98, 166, 255);
            spec.Sleeve = spec.Torso;
            spec.Pants = new Color32(44, 84, 146, 255);
            spec.RightArmSwingScale = 0.45f;

            return BuildRig(spec, null, (b, t, s) =>
            {
                b.AddBox(t.Pivots[PartHead] + new Vector3(0f, 0.135f * s, 0f),
                    new Vector3(0.15f * s, 0.055f * s, 0.15f * s), WhiteColor, WhiteColor);
            }, (b, t, s) =>
            {
                // Tablet / wrench
                b.AddBox(t.Pivots[PartRightArm] + new Vector3(0f, -0.19f * s, 0.04f * s),
                    new Vector3(0.03f * s, 0.09f * s, 0.08f * s), CharcoalColor, new Color32(110, 175, 220, 255));
            });
        }

        private static CitizenTemplate BuildCasual(Color32 shirt, Color32 pants, Color32 skin, Color32 hair)
        {
            var spec = DefaultAdultSpec(CitizenGroup.Residential);
            spec.Skin = skin;
            spec.Hair = hair;
            spec.Torso = shirt;
            spec.Sleeve = shirt;
            spec.Pants = pants;
            return BuildRig(spec, null, null, null);
        }

        private static CitizenTemplate BuildRig(
            RigSpec spec,
            Action<MeshBuilder, RigTracker, float> torsoExtra,
            Action<MeshBuilder, RigTracker, float> headExtra,
            Action<MeshBuilder, RigTracker, float> rightArmExtra)
        {
            float s = spec.Scale;
            var b = new MeshBuilder();
            b.SetFrame(Vector3.zero, 0);
            var tracker = new RigTracker();

            float legH = 0.23f * s;
            float torsoH = 0.22f * s;
            float hipY = legH;
            float neckY = hipY + torsoH;
            float shoulderY = neckY - 0.025f * s;

            tracker.Pivots[PartTorso] = new Vector3(0f, hipY, 0f);
            tracker.Pivots[PartHead] = new Vector3(0f, neckY, 0f);
            tracker.Pivots[PartLeftArm] = new Vector3(-0.125f * s, shoulderY, 0f);
            tracker.Pivots[PartRightArm] = new Vector3(0.125f * s, shoulderY, 0f);
            tracker.Pivots[PartLeftLeg] = new Vector3(-0.048f * s, hipY, 0f);
            tracker.Pivots[PartRightLeg] = new Vector3(0.048f * s, hipY, 0f);
            tracker.Pivots[PartShadow] = Vector3.zero;

            // Torso
            int start = b.VertexCount;
            b.AddBox(new Vector3(0f, hipY + torsoH * 0.5f, 0f),
                new Vector3(0.19f * s, torsoH, 0.11f * s), spec.Torso, spec.Torso);
            torsoExtra?.Invoke(b, tracker, s);
            tracker.AssignFrom(b, start, PartTorso);

            // Head & Hair
            start = b.VertexCount;
            b.AddBox(new Vector3(0f, neckY + 0.062f * s, 0f),
                new Vector3(0.13f * s, 0.124f * s, 0.13f * s), spec.Skin, spec.Hair);
            b.AddBox(new Vector3(0f, neckY + 0.112f * s, -0.01f * s),
                new Vector3(0.135f * s, 0.036f * s, 0.138f * s), spec.Hair, spec.Hair);
            b.AddBox(new Vector3(0f, neckY + 0.075f * s, -0.056f * s),
                new Vector3(0.132f * s, 0.085f * s, 0.03f * s), spec.Hair, spec.Hair);
            headExtra?.Invoke(b, tracker, s);
            tracker.AssignFrom(b, start, PartHead);

            // Left Arm
            start = b.VertexCount;
            AddArm(b, tracker.Pivots[PartLeftArm], s, spec.Sleeve, spec.Skin);
            tracker.AssignFrom(b, start, PartLeftArm);

            // Right Arm + optional prop
            start = b.VertexCount;
            AddArm(b, tracker.Pivots[PartRightArm], s, spec.Sleeve, spec.Skin);
            rightArmExtra?.Invoke(b, tracker, s);
            tracker.AssignFrom(b, start, PartRightArm);

            // Left Leg
            start = b.VertexCount;
            AddLeg(b, tracker.Pivots[PartLeftLeg], legH, s, spec.Pants, spec.Shoes);
            tracker.AssignFrom(b, start, PartLeftLeg);

            // Right Leg
            start = b.VertexCount;
            AddLeg(b, tracker.Pivots[PartRightLeg], legH, s, spec.Pants, spec.Shoes);
            tracker.AssignFrom(b, start, PartRightLeg);

            // Ground shadow quad
            start = b.VertexCount;
            float sh = 0.12f * s;
            b.AddQuad(
                new Vector3(-sh, 0.005f, -sh),
                new Vector3(-sh, 0.005f, sh),
                new Vector3(sh, 0.005f, sh),
                new Vector3(sh, 0.005f, -sh),
                Vector3.up, ShadowTint);
            tracker.AssignFrom(b, start, PartShadow);

            var mesh = new Mesh();
            b.ApplyTo(mesh);
            Vector3[] verts = mesh.vertices;
            var tpl = new CitizenTemplate
            {
                Vertices = verts,
                Normals = mesh.normals,
                Colors = mesh.colors32,
                Triangles = mesh.triangles,
                PartIds = tracker.ToArray(verts.Length),
                Pivots = tracker.Pivots,
                WalkSpeed = spec.WalkSpeed,
                StrideFreq = spec.StrideFreq,
                LegSwingDeg = spec.LegSwingDeg,
                ArmSwingDeg = spec.ArmSwingDeg,
                RightArmSwingScale = spec.RightArmSwingScale,
                BaseTorsoPitch = spec.BaseTorsoPitch,
                UsesCane = spec.UsesCane,
                Group = spec.Group
            };
            UnityEngine.Object.Destroy(mesh);
            return tpl;
        }

        private static void AddArm(MeshBuilder b, Vector3 shoulder, float s, Color32 sleeve, Color32 skin)
        {
            float upperH = 0.11f * s;
            float lowerH = 0.08f * s;
            b.AddBox(shoulder + new Vector3(0f, -upperH * 0.5f, 0f),
                new Vector3(0.058f * s, upperH, 0.06f * s), sleeve, sleeve);
            b.AddBox(shoulder + new Vector3(0f, -upperH - lowerH * 0.5f, 0f),
                new Vector3(0.052f * s, lowerH, 0.054f * s), skin, skin);
        }

        private static void AddLeg(MeshBuilder b, Vector3 hip, float legH, float s, Color32 pants, Color32 shoes)
        {
            float pantsH = legH - 0.045f * s;
            b.AddBox(hip + new Vector3(0f, -pantsH * 0.5f, 0f),
                new Vector3(0.072f * s, pantsH, 0.076f * s), pants, pants);
            b.AddBox(hip + new Vector3(0f, -legH + 0.0225f * s, 0.012f * s),
                new Vector3(0.074f * s, 0.045f * s, 0.10f * s), shoes, shoes);
        }
    }
}
