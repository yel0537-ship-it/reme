using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Echo7
{
    /// <summary>
    /// Сборка модели-заглушки из деталей. Все визуальные модели прототипа (постройки, юниты, существа)
    /// собираются через этот класс — при переходе на реальные ассеты эти вызовы заменяются на Instantiate(prefab).
    /// </summary>
    public class ModelBuilder
    {
        public readonly Transform Root;
        public bool CastShadows = true;

        public ModelBuilder(Transform parent, string name = "Model")
        {
            Root = new GameObject(name).transform;
            Root.SetParent(parent, false);
        }

        public ModelBuilder(Transform existingRoot, bool wrap)
        {
            Root = existingRoot;
        }

        public Transform Group(string name, Vector3 localPos, Transform parent = null, Vector3? euler = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent != null ? parent : Root, false);
            t.localPosition = localPos;
            if (euler.HasValue) t.localEulerAngles = euler.Value;
            return t;
        }

        public Transform Mesh(Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Transform parent = null, Vector3? euler = null, string name = "part")
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent != null ? parent : Root, false);
            t.localPosition = pos;
            t.localScale = scale;
            if (euler.HasValue) t.localEulerAngles = euler.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return t;
        }

        /// <summary>Скошенная коробка. pos — центр.</summary>
        public Transform Box(Vector3 pos, Vector3 size, Material mat, float bevel = 0.08f, Transform parent = null, Vector3? euler = null)
            => Mesh(MeshFactory.BeveledBox(size, bevel), mat, pos, Vector3.one, parent, euler, "box");

        /// <summary>Цилиндр вдоль Y. pos — центр.</summary>
        public Transform Cyl(Vector3 pos, float radius, float height, Material mat, Transform parent = null, Vector3? euler = null, int seg = 16)
            => Mesh(MeshFactory.Cylinder(radius, height, seg), mat, pos, Vector3.one, parent, euler, "cyl");

        public Transform Frustum(Vector3 pos, float rBottom, float rTop, float height, Material mat, Transform parent = null, Vector3? euler = null, int seg = 16)
            => Mesh(MeshFactory.Frustum(rBottom, rTop, height, seg), mat, pos, Vector3.one, parent, euler, "frustum");

        public Transform Sphere(Vector3 pos, Vector3 scale, Material mat, Transform parent = null, Vector3? euler = null)
            => Mesh(MeshFactory.Sphere, mat, pos, scale, parent, euler, "sphere");

        public Transform Capsule(Vector3 pos, Vector3 scale, Material mat, Transform parent = null, Vector3? euler = null)
            => Mesh(MeshFactory.Capsule, mat, pos, scale, parent, euler, "capsule");

        public Transform Hex(Vector3 pos, float radius, float height, Material mat, Transform parent = null, Vector3? euler = null)
            => Mesh(MeshFactory.HexPrism(radius, height), mat, pos, Vector3.one, parent, euler, "hex");

        public Transform Torus(Vector3 pos, float radius, float tube, Material mat, Transform parent = null, Vector3? euler = null)
            => Mesh(MeshFactory.Torus(radius, tube), mat, pos, Vector3.one, parent, euler, "torus");

        /// <summary>Светящаяся полоска (оранжевая/голубая) — фирменная деталь референсов.</summary>
        public Transform Strip(Vector3 pos, Vector3 size, Material glow = null, Transform parent = null, Vector3? euler = null)
        {
            var t = Mesh(MeshFactory.BeveledBox(size, 0f), glow != null ? glow : Palette.Orange, pos, Vector3.one, parent, euler, "glow");
            t.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        /// <summary>Точечный свет (по возможности экономно — у Built-in ограничено число пиксельных источников).</summary>
        public Light PointLight(Vector3 pos, Color color, float range, float intensity, Transform parent = null)
        {
            var t = Group("light", pos, parent);
            var l = t.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            return l;
        }

        /// <summary>
        /// Сливает статические детали в один меш на материал (меньше draw calls).
        /// Подвижные узлы (их имя начинается с '#') и их потомки не трогаются.
        /// </summary>
        public static void CombineStatic(Transform root)
        {
            var byMat = new Dictionary<Material, List<CombineInstance>>();
            var toRemove = new List<GameObject>();
            var toWorld = root.worldToLocalMatrix;
            Collect(root, root, byMat, toRemove, toWorld);
            if (toRemove.Count < 3) return;

            foreach (var kv in byMat)
            {
                var go = new GameObject("combined_" + kv.Key.name);
                go.transform.SetParent(root, false);
                var mesh = new Mesh { name = go.name };
                int verts = 0;
                foreach (var ci in kv.Value) verts += ci.mesh.vertexCount;
                if (verts > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                if (kv.Key == Palette.Orange || kv.Key == Palette.Blue || kv.Key == Palette.Red)
                    r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var g in toRemove)
            {
                // DestroyImmediate: модель собирается за один кадр, иначе старые детали отрисуются ещё раз
                if (g.transform.childCount == 0)
                    Object.DestroyImmediate(g);
                else
                {
                    Object.DestroyImmediate(g.GetComponent<MeshRenderer>());
                    Object.DestroyImmediate(g.GetComponent<MeshFilter>());
                }
            }
        }

        static void Collect(Transform root, Transform t, Dictionary<Material, List<CombineInstance>> byMat, List<GameObject> toRemove, Matrix4x4 toLocal)
        {
            if (t != root && t.name.StartsWith("#")) return;
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mf != null && mr != null && mf.sharedMesh != null && mr.sharedMaterial != null)
            {
                if (!byMat.TryGetValue(mr.sharedMaterial, out var list)) byMat[mr.sharedMaterial] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * t.localToWorldMatrix });
                toRemove.Add(t.gameObject);
            }
            for (int i = 0; i < t.childCount; i++) Collect(root, t.GetChild(i), byMat, toRemove, toLocal);
        }

        /// <summary>Подменить все материалы модели (голограмма при строительстве) с возможностью вернуть.</summary>
        public static MaterialSwap SwapAll(Transform root, Material m)
        {
            var swap = new MaterialSwap();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer) continue;
                swap.Renderers.Add(r);
                swap.Original.Add(r.sharedMaterials);
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = m;
                r.sharedMaterials = arr;
            }
            return swap;
        }

        public class MaterialSwap
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Material[]> Original = new List<Material[]>();

            public void Apply(Material m)
            {
                foreach (var r in Renderers)
                {
                    if (r == null) continue;
                    var arr = r.sharedMaterials;
                    for (int i = 0; i < arr.Length; i++) arr[i] = m;
                    r.sharedMaterials = arr;
                }
            }

            public void Restore()
            {
                for (int i = 0; i < Renderers.Count; i++)
                    if (Renderers[i] != null) Renderers[i].sharedMaterials = Original[i];
            }
        }
    }
}
