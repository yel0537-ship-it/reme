using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Процедурные меши-заглушки: скошенные коробки (как модульные блоки на референсах), цилиндры, усечённые конусы,
    /// шестигранные призмы, тор. Меши кэшируются по параметрам, поэтому одинаковые детали делят один меш.
    /// </summary>
    public static class MeshFactory
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();
        static Mesh _sphere, _capsule, _quad;

        class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = c; c = t; }
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                N.Add(n); N.Add(n); N.Add(n);
                U.Add(Uv(a, n)); U.Add(Uv(b, n)); U.Add(Uv(c, n));
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                Tri(a, b, c, n);
                Tri(a, c, d, n);
            }

            static Vector2 Uv(Vector3 p, Vector3 n)
            {
                var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                if (a.x >= a.y && a.x >= a.z) return new Vector2(p.z, p.y);
                if (a.y >= a.z) return new Vector2(p.x, p.z);
                return new Vector2(p.x, p.y);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, U);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static string Key(string kind, params float[] p)
        {
            var sb = new System.Text.StringBuilder(kind);
            foreach (var f in p) sb.Append('_').Append(Mathf.Round(f * 100f) / 100f);
            return sb.ToString();
        }

        /// <summary>Коробка со скошенными рёбрами и углами (жёсткие нормали — «модульный» вид).</summary>
        public static Mesh BeveledBox(Vector3 size, float bevel)
        {
            string key = Key("bbox", size.x, size.y, size.z, bevel);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var h = size * 0.5f;
            float b = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            var bd = new Builder();
            if (b <= 0.001f)
            {
                AddPlainBox(bd, h);
                return Cache[key] = bd.Build(key);
            }
            float ix = h.x - b, iy = h.y - b, iz = h.z - b;

            // 6 основных граней
            bd.Quad(new Vector3(h.x, -iy, -iz), new Vector3(h.x, iy, -iz), new Vector3(h.x, iy, iz), new Vector3(h.x, -iy, iz), Vector3.right);
            bd.Quad(new Vector3(-h.x, -iy, -iz), new Vector3(-h.x, -iy, iz), new Vector3(-h.x, iy, iz), new Vector3(-h.x, iy, -iz), Vector3.left);
            bd.Quad(new Vector3(-ix, h.y, -iz), new Vector3(-ix, h.y, iz), new Vector3(ix, h.y, iz), new Vector3(ix, h.y, -iz), Vector3.up);
            bd.Quad(new Vector3(-ix, -h.y, -iz), new Vector3(ix, -h.y, -iz), new Vector3(ix, -h.y, iz), new Vector3(-ix, -h.y, iz), Vector3.down);
            bd.Quad(new Vector3(-ix, -iy, h.z), new Vector3(ix, -iy, h.z), new Vector3(ix, iy, h.z), new Vector3(-ix, iy, h.z), Vector3.forward);
            bd.Quad(new Vector3(-ix, -iy, -h.z), new Vector3(-ix, iy, -h.z), new Vector3(ix, iy, -h.z), new Vector3(ix, -iy, -h.z), Vector3.back);

            // 12 фасок по рёбрам
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                // рёбра вдоль Z
                var n = new Vector3(sx, sy, 0).normalized;
                bd.Quad(new Vector3(sx * h.x, sy * iy, -iz), new Vector3(sx * ix, sy * h.y, -iz),
                        new Vector3(sx * ix, sy * h.y, iz), new Vector3(sx * h.x, sy * iy, iz), n);
            }
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                // рёбра вдоль Y
                var n = new Vector3(sx, 0, sz).normalized;
                bd.Quad(new Vector3(sx * h.x, -iy, sz * iz), new Vector3(sx * ix, -iy, sz * h.z),
                        new Vector3(sx * ix, iy, sz * h.z), new Vector3(sx * h.x, iy, sz * iz), n);
            }
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                // рёбра вдоль X
                var n = new Vector3(0, sy, sz).normalized;
                bd.Quad(new Vector3(-ix, sy * h.y, sz * iz), new Vector3(-ix, sy * iy, sz * h.z),
                        new Vector3(ix, sy * iy, sz * h.z), new Vector3(ix, sy * h.y, sz * iz), n);
            }
            // 8 угловых треугольников
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var n = new Vector3(sx, sy, sz).normalized;
                bd.Tri(new Vector3(sx * h.x, sy * iy, sz * iz), new Vector3(sx * ix, sy * h.y, sz * iz), new Vector3(sx * ix, sy * iy, sz * h.z), n);
            }
            return Cache[key] = bd.Build(key);
        }

        static void AddPlainBox(Builder bd, Vector3 h)
        {
            bd.Quad(new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, -h.y, h.z), Vector3.right);
            bd.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), Vector3.left);
            bd.Quad(new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), Vector3.up);
            bd.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), Vector3.down);
            bd.Quad(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.forward);
            bd.Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, -h.y, -h.z), Vector3.back);
        }

        /// <summary>Усечённый конус/цилиндр вдоль оси Y, центр в середине высоты. Гладкие боковые нормали.</summary>
        public static Mesh Frustum(float rBottom, float rTop, float height, int segments = 16, bool caps = true)
        {
            string key = Key("frus", rBottom, rTop, height, segments, caps ? 1 : 0);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bd = new Builder();
            float hy = height * 0.5f;
            float slope = (rBottom - rTop) / Mathf.Max(0.0001f, height);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var p0 = d0 * rBottom + Vector3.down * hy;
                var p1 = d1 * rBottom + Vector3.down * hy;
                var p2 = d1 * rTop + Vector3.up * hy;
                var p3 = d0 * rTop + Vector3.up * hy;
                var n0 = (d0 + Vector3.up * slope).normalized;
                var n1 = (d1 + Vector3.up * slope).normalized;
                // гладкие нормали: по вершинам
                int vi = bd.V.Count;
                bd.V.Add(p0); bd.V.Add(p1); bd.V.Add(p2); bd.V.Add(p3);
                bd.N.Add(n0); bd.N.Add(n1); bd.N.Add(n1); bd.N.Add(n0);
                float u0 = (float)i / segments, u1 = (float)(i + 1) / segments;
                bd.U.Add(new Vector2(u0, 0)); bd.U.Add(new Vector2(u1, 0)); bd.U.Add(new Vector2(u1, 1)); bd.U.Add(new Vector2(u0, 1));
                // Порядок для внешней нормали (по часовой при взгляде снаружи)
                var avgN = (n0 + n1).normalized;
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), avgN) >= 0)
                { bd.T.Add(vi); bd.T.Add(vi + 1); bd.T.Add(vi + 2); bd.T.Add(vi); bd.T.Add(vi + 2); bd.T.Add(vi + 3); }
                else
                { bd.T.Add(vi); bd.T.Add(vi + 2); bd.T.Add(vi + 1); bd.T.Add(vi); bd.T.Add(vi + 3); bd.T.Add(vi + 2); }
                if (caps)
                {
                    if (rTop > 0.001f) bd.Tri(Vector3.up * hy, p3, p2, Vector3.up);
                    if (rBottom > 0.001f) bd.Tri(Vector3.down * hy, p0, p1, Vector3.down);
                }
            }
            return Cache[key] = bd.Build(key);
        }

        public static Mesh Cylinder(float radius, float height, int segments = 16) => Frustum(radius, radius, height, segments);

        /// <summary>Шестигранная призма (плоские грани), ось Y.</summary>
        public static Mesh HexPrism(float radius, float height)
        {
            string key = Key("hex", radius, height);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bd = new Builder();
            float hy = height * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                var n = ((d0 + d1) * 0.5f).normalized;
                bd.Quad(d0 + Vector3.down * hy, d1 + Vector3.down * hy, d1 + Vector3.up * hy, d0 + Vector3.up * hy, n);
                bd.Tri(Vector3.up * hy, d0 + Vector3.up * hy, d1 + Vector3.up * hy, Vector3.up);
                bd.Tri(Vector3.down * hy, d0 + Vector3.down * hy, d1 + Vector3.down * hy, Vector3.down);
            }
            return Cache[key] = bd.Build(key);
        }

        /// <summary>Тор в плоскости XZ.</summary>
        public static Mesh Torus(float radius, float tube, int seg = 24, int sides = 8)
        {
            string key = Key("torus", radius, tube, seg, sides);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bd = new Builder();
            for (int i = 0; i < seg; i++)
            for (int j = 0; j < sides; j++)
            {
                Vector3 P(int a, int b, out Vector3 nn)
                {
                    float u = a * Mathf.PI * 2f / seg, v = b * Mathf.PI * 2f / sides;
                    var c = new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u));
                    nn = (c * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v)).normalized;
                    return c * radius + nn * tube;
                }
                var p0 = P(i, j, out var n0); var p1 = P(i + 1, j, out _); var p2 = P(i + 1, j + 1, out _); var p3 = P(i, j + 1, out _);
                var c0 = new Vector3(Mathf.Cos((i + 0.5f) * Mathf.PI * 2f / seg), 0, Mathf.Sin((i + 0.5f) * Mathf.PI * 2f / seg)) * radius;
                var n = ((p0 + p1 + p2 + p3) * 0.25f - c0).normalized;
                bd.Quad(p0, p1, p2, p3, n);
            }
            return Cache[key] = bd.Build(key);
        }

        /// <summary>Пучок скрещённых двусторонних квадов — травяная кочка. Основание в нуле.</summary>
        public static Mesh GrassTuft()
        {
            const string key = "grass_tuft";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bd = new Builder();
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI / 3f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.35f;
                var n = Vector3.Cross(d, Vector3.up).normalized;
                var top = Vector3.up * 0.55f;
                var lean = d.normalized * 0.08f;
                bd.Quad(-d, d, d * 0.3f + top + lean, -d * 0.3f + top + lean, n);
                bd.Quad(-d, -d * 0.3f + top + lean, d * 0.3f + top + lean, d, -n);
            }
            var m = bd.Build(key);
            // Нормали травы смотрят вверх, чтобы она ровно освещалась
            var normals = new Vector3[m.vertexCount];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            m.normals = normals;
            return Cache[key] = m;
        }

        /// <summary>Неровный «валун»: искажённая коробка с фасками.</summary>
        public static Mesh Boulder(int variant)
        {
            string key = "boulder_" + variant;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var src = BeveledBox(Vector3.one, 0.28f);
            var m = Object.Instantiate(src);
            m.name = key;
            var v = m.vertices;
            var rnd = new System.Random(variant * 7919 + 13);
            var offsets = new Dictionary<Vector3, Vector3>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3(Mathf.Round(v[i].x * 100), Mathf.Round(v[i].y * 100), Mathf.Round(v[i].z * 100));
                if (!offsets.TryGetValue(k, out var o))
                {
                    o = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.25f;
                    offsets[k] = o;
                }
                v[i] += o;
                if (v[i].y < -0.4f) v[i].y = -0.5f;
            }
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return Cache[key] = m;
        }

        /// <summary>Кристаллическая друза: вытянутые шестигранные призмы с острыми вершинами.</summary>
        public static Mesh CrystalShard()
        {
            const string key = "crystal";
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bd = new Builder();
            float r = 0.18f, h = 0.9f, tip = 0.35f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * r;
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * r;
                var n = ((d0 + d1) * 0.5f).normalized;
                bd.Quad(d0, d1, d1 + Vector3.up * h, d0 + Vector3.up * h, n);
                var apex = Vector3.up * (h + tip);
                var tn = (n + Vector3.up * 0.5f).normalized;
                bd.Tri(d0 + Vector3.up * h, d1 + Vector3.up * h, apex, tn);
            }
            return Cache[key] = bd.Build(key);
        }

        static Mesh Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            return mesh;
        }

        public static Mesh Sphere => _sphere != null ? _sphere : (_sphere = Primitive(PrimitiveType.Sphere));
        public static Mesh Capsule => _capsule != null ? _capsule : (_capsule = Primitive(PrimitiveType.Capsule));
        public static Mesh QuadMesh => _quad != null ? _quad : (_quad = Primitive(PrimitiveType.Quad));
    }
}
