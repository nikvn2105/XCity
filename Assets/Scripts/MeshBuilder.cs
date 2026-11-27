using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace XCity
{
    internal sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(1024);
        private readonly List<Vector3> _normals = new List<Vector3>(1024);
        private readonly List<Color32> _colors = new List<Color32>(1024);
        private readonly List<int> _triangles = new List<int>(1536);

        private Vector3 _origin;
        private int _quarterTurns;

        public int VertexCount => _vertices.Count;

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _colors.Clear();
            _triangles.Clear();
            _origin = Vector3.zero;
            _quarterTurns = 0;
        }

        public void SetFrame(Vector3 origin, int quarterTurns)
        {
            _origin = origin;
            _quarterTurns = ((quarterTurns % 4) + 4) % 4;
        }

        public void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 outward, Color32 color)
        {
            Vector3 p0 = TransformPoint(v0);
            Vector3 p1 = TransformPoint(v1);
            Vector3 p2 = TransformPoint(v2);
            Vector3 p3 = TransformPoint(v3);
            Vector3 outW = Rotate(outward);

            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
            if (Vector3.Dot(n, outW) < 0f)
            {
                Vector3 tmp = p1;
                p1 = p3;
                p3 = tmp;
                n = -n;
            }

            Vector3 normal = n.sqrMagnitude > 1e-10f ? n.normalized : outW.normalized;
            int index = _vertices.Count;
            _vertices.Add(p0);
            _vertices.Add(p1);
            _vertices.Add(p2);
            _vertices.Add(p3);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _triangles.Add(index);
            _triangles.Add(index + 1);
            _triangles.Add(index + 2);
            _triangles.Add(index);
            _triangles.Add(index + 2);
            _triangles.Add(index + 3);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color32 color)
        {
            Vector3 pa = TransformPoint(a);
            Vector3 pb = TransformPoint(b);
            Vector3 pc = TransformPoint(c);
            Vector3 outW = Rotate(outward);

            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (Vector3.Dot(n, outW) < 0f)
            {
                Vector3 tmp = pb;
                pb = pc;
                pc = tmp;
                n = -n;
            }

            Vector3 normal = n.sqrMagnitude > 1e-10f ? n.normalized : outW.normalized;
            int index = _vertices.Count;
            _vertices.Add(pa);
            _vertices.Add(pb);
            _vertices.Add(pc);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _triangles.Add(index);
            _triangles.Add(index + 1);
            _triangles.Add(index + 2);
        }

        public void AddBox(Vector3 center, Vector3 size, Color32 side, Color32 top, bool bottom = false)
        {
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            float hz = size.z * 0.5f;

            float x0 = center.x - hx;
            float x1 = center.x + hx;
            float y0 = center.y - hy;
            float y1 = center.y + hy;
            float z0 = center.z - hz;
            float z1 = center.z + hz;

            // +Z
            AddQuad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, side);
            // -Z
            AddQuad(new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), Vector3.back, side);
            // +X
            AddQuad(new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), Vector3.right, side);
            // -X
            AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, side);
            // +Y
            AddQuad(new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.up, top);

            if (bottom)
                AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, side);
        }

        /// <summary>
        /// Emits a decal quad offset 0.02 units outward on a box face.
        /// sideIndex: 0=+Z, 1=-Z, 2=+X, 3=-X. u0..u1 is relative to boxCenter along the face's horizontal axis; y0..y1 is local Y.
        /// </summary>
        public void AddFacadeRect(Vector3 boxCenter, Vector3 boxSize, int sideIndex, float u0, float u1, float y0, float y1, Color32 color)
        {
            const float offset = 0.02f;
            float hx = boxSize.x * 0.5f;
            float hz = boxSize.z * 0.5f;

            switch (sideIndex)
            {
                case 0: // +Z
                {
                    float z = boxCenter.z + hz + offset;
                    AddQuad(
                        new Vector3(boxCenter.x + u0, y0, z),
                        new Vector3(boxCenter.x + u1, y0, z),
                        new Vector3(boxCenter.x + u1, y1, z),
                        new Vector3(boxCenter.x + u0, y1, z),
                        Vector3.forward, color);
                    break;
                }
                case 1: // -Z
                {
                    float z = boxCenter.z - hz - offset;
                    AddQuad(
                        new Vector3(boxCenter.x + u1, y0, z),
                        new Vector3(boxCenter.x + u0, y0, z),
                        new Vector3(boxCenter.x + u0, y1, z),
                        new Vector3(boxCenter.x + u1, y1, z),
                        Vector3.back, color);
                    break;
                }
                case 2: // +X
                {
                    float x = boxCenter.x + hx + offset;
                    AddQuad(
                        new Vector3(x, y0, boxCenter.z + u1),
                        new Vector3(x, y0, boxCenter.z + u0),
                        new Vector3(x, y1, boxCenter.z + u0),
                        new Vector3(x, y1, boxCenter.z + u1),
                        Vector3.right, color);
                    break;
                }
                default: // -X
                {
                    float x = boxCenter.x - hx - offset;
                    AddQuad(
                        new Vector3(x, y0, boxCenter.z + u0),
                        new Vector3(x, y0, boxCenter.z + u1),
                        new Vector3(x, y1, boxCenter.z + u1),
                        new Vector3(x, y1, boxCenter.z + u0),
                        Vector3.left, color);
                    break;
                }
            }
        }

        public void AddGableRoof(Vector3 baseCenter, float width, float depth, float height, float overhang, Color32 roof, Color32 gable)
        {
            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            float x0 = baseCenter.x - hx - overhang;
            float x1 = baseCenter.x + hx + overhang;
            float z0 = baseCenter.z - hz - overhang;
            float z1 = baseCenter.z + hz + overhang;
            float yBase = baseCenter.y;
            float yTop = baseCenter.y + height;

            // Two roof slopes (ridge along X)
            AddQuad(
                new Vector3(x0, yBase, z1),
                new Vector3(x1, yBase, z1),
                new Vector3(x1, yTop, baseCenter.z),
                new Vector3(x0, yTop, baseCenter.z),
                new Vector3(0f, 1f, 1f), roof);

            AddQuad(
                new Vector3(x1, yBase, z0),
                new Vector3(x0, yBase, z0),
                new Vector3(x0, yTop, baseCenter.z),
                new Vector3(x1, yTop, baseCenter.z),
                new Vector3(0f, 1f, -1f), roof);

            // Two gable triangles flush with the wall ends
            float gx0 = baseCenter.x - hx;
            float gx1 = baseCenter.x + hx;
            float gz0 = baseCenter.z - hz;
            float gz1 = baseCenter.z + hz;
            AddTriangle(
                new Vector3(gx1, yBase, gz1),
                new Vector3(gx1, yBase, gz0),
                new Vector3(gx1, yTop, baseCenter.z),
                Vector3.right, gable);
            AddTriangle(
                new Vector3(gx0, yBase, gz0),
                new Vector3(gx0, yBase, gz1),
                new Vector3(gx0, yTop, baseCenter.z),
                Vector3.left, gable);
        }

        public void AddCylinder(Vector3 baseCenter, float rBottom, float rTop, float height, int segments, Color32 color, bool capTop, Color32 capColor)
        {
            float step = Mathf.PI * 2f / segments;
            float y0 = baseCenter.y;
            float y1 = baseCenter.y + height;
            Vector3 topCenter = new Vector3(baseCenter.x, y1, baseCenter.z);

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;
                float mid = (a0 + a1) * 0.5f;

                float c0 = Mathf.Cos(a0);
                float s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1);
                float s1 = Mathf.Sin(a1);

                Vector3 p00 = new Vector3(baseCenter.x + c0 * rBottom, y0, baseCenter.z + s0 * rBottom);
                Vector3 p10 = new Vector3(baseCenter.x + c1 * rBottom, y0, baseCenter.z + s1 * rBottom);
                Vector3 p11 = new Vector3(baseCenter.x + c1 * rTop, y1, baseCenter.z + s1 * rTop);
                Vector3 p01 = new Vector3(baseCenter.x + c0 * rTop, y1, baseCenter.z + s0 * rTop);

                Vector3 outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                AddQuad(p10, p00, p01, p11, outward, color);

                if (capTop)
                    AddTriangle(topCenter, p11, p01, Vector3.up, capColor);
            }
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            if (_vertices.Count == 0) return;

            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        private Vector3 Rotate(Vector3 v)
        {
            switch (_quarterTurns)
            {
                case 1: return new Vector3(v.z, v.y, -v.x);
                case 2: return new Vector3(-v.x, v.y, -v.z);
                case 3: return new Vector3(-v.z, v.y, v.x);
                default: return v;
            }
        }

        private Vector3 TransformPoint(Vector3 v) => Rotate(v) + _origin;
    }
}
