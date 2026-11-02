using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Echo7
{
    /// <summary>Озеро: уровень воды падает в пекло и восстанавливается в дожди.</summary>
    public class Lake
    {
        public Vector3 Center;
        public float Radius;
        public float FullLevel;
        public Transform Water;
    }

    /// <summary>
    /// Генерация планеты: саванна с небольшими лесными массивами, озёрами, тропами, скалами,
    /// залежами ресурсов и пещерами. База — в центре карты на выровненной площадке.
    /// </summary>
    public class WorldGenerator : MonoBehaviour
    {
        public const float WorldSize = 256f;
        public const int GridCells = 128;
        public const float TerrainHeight = 40f;
        public const float BaseFlatRadius = 38f;

        public Terrain Terrain { get; private set; }
        public float BaseHeight { get; private set; }
        public Vector3 Center => new Vector3(WorldSize * 0.5f, BaseHeight, WorldSize * 0.5f);
        public Vector3 LandingZone { get; private set; }
        public readonly List<Lake> Lakes = new List<Lake>();
        public readonly List<CaveEntrance> Caves = new List<CaveEntrance>();

        System.Random _rnd;
        TerrainData _data;
        int _alphaRes = 128;
        float[,] _slope, _dirt, _shore, _patch;
        readonly List<Vector2[]> _trails = new List<Vector2[]>();
        float _lastGreen = -1f;

        // растительность
        readonly List<Transform> _canopies = new List<Transform>();
        readonly List<Vector3> _canopyBaseScale = new List<Vector3>();
        Material _canopyMat, _grassMat;
        readonly List<Matrix4x4[]> _grassBatches = new List<Matrix4x4[]>();
        readonly List<Vector3> _grassScales = new List<Vector3>();
        Mesh _grassMesh;
        float _greenness = 1f;

        static readonly Color GrassLush = new Color(0.47f, 0.55f, 0.22f);
        static readonly Color GrassDry = new Color(0.72f, 0.60f, 0.32f);
        static readonly Color CanopyLush = new Color(0.26f, 0.38f, 0.14f);
        static readonly Color CanopyDry = new Color(0.50f, 0.40f, 0.20f);

        float R() => (float)_rnd.NextDouble();
        float R(float a, float b) => a + (b - a) * R();

        public void Generate(int seed)
        {
            _rnd = new System.Random(seed);
            BuildTerrain(seed);
            var grid = new WorldGrid(GridCells, Vector3.zero, Terrain);
            Game.Grid = grid;
            BuildLakes(grid);
            PaintTerrain(1f);
            BuildTrees(grid);
            BuildRocks(grid);
            BuildGrass();
            BuildDeposits(grid);
            BuildCaves(grid);
            LandingZone = FindLandingZone(grid);
            grid.RefreshHeights();
        }

        // ---------------------------------------------------------------- рельеф

        float RawHeight(float x, float z, float seedOff)
        {
            float h = 6f;
            h += (Noise2.Fbm(x * 0.008f + seedOff, z * 0.008f + seedOff, 4) - 0.5f) * 14f;
            h += Noise2.Ridged(x * 0.012f + seedOff * 2f, z * 0.012f, 3) * 5f;
            // окраины карты выше — естественная граница
            float ex = Mathf.Abs(x - WorldSize * 0.5f) / (WorldSize * 0.5f);
            float ez = Mathf.Abs(z - WorldSize * 0.5f) / (WorldSize * 0.5f);
            float edge = Noise2.Smooth(0.82f, 1f, Mathf.Max(ex, ez));
            h += edge * 12f;
            return h;
        }

        void BuildTerrain(int seed)
        {
            int res = 257;
            _data = new TerrainData { heightmapResolution = res };
            _data.size = new Vector3(WorldSize, TerrainHeight, WorldSize);
            float so = seed * 0.37f % 100f;
            float centerRaw = RawHeight(WorldSize * 0.5f, WorldSize * 0.5f, so);
            BaseHeight = Mathf.Clamp(centerRaw, 5f, 9f);
            var heights = new float[res, res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = (float)x / (res - 1) * WorldSize;
                float wz = (float)y / (res - 1) * WorldSize;
                float h = RawHeight(wx, wz, so);
                float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(WorldSize * 0.5f, WorldSize * 0.5f));
                float flat = 1f - Noise2.Smooth(BaseFlatRadius, BaseFlatRadius + 16f, d);
                h = Mathf.Lerp(h, BaseHeight + (Noise2.Fbm(wx * 0.05f, wz * 0.05f, 2) - 0.5f) * 0.25f, flat);
                heights[y, x] = Mathf.Clamp01(h / TerrainHeight);
            }
            _data.SetHeights(0, 0, heights);

            var go = Terrain.CreateTerrainGameObject(_data);
            go.name = "Terrain";
            go.transform.SetParent(transform, false);
            go.layer = Layers.Ground;
            Terrain = go.GetComponent<Terrain>();
            Terrain.heightmapPixelError = 4f;
            Terrain.basemapDistance = 400f;
            Terrain.drawTreesAndFoliage = false;
            Terrain.shadowCastingMode = ShadowCastingMode.On;
            var sh = MaterialFactory.TerrainShader;
            if (sh != null) Terrain.materialTemplate = new Material(sh);

            // Текстуры-заглушки (замените на свои TerrainLayer — индексы слоёв сохраняются)
            var layers = new[]
            {
                Layer(ProceduralTextures.Ground(256, new Color(0.40f, 0.47f, 0.20f), new Color(0.58f, 0.60f, 0.28f), 6f, seed + 1, new Color(0.3f, 0.38f, 0.15f), 0.05f), 7f),
                Layer(ProceduralTextures.Ground(256, new Color(0.70f, 0.58f, 0.33f), new Color(0.82f, 0.70f, 0.42f), 7f, seed + 2, new Color(0.55f, 0.45f, 0.25f), 0.04f), 7f),
                Layer(ProceduralTextures.Ground(256, new Color(0.50f, 0.36f, 0.24f), new Color(0.62f, 0.47f, 0.33f), 5f, seed + 3, new Color(0.35f, 0.28f, 0.2f), 0.06f), 6f),
                Layer(ProceduralTextures.Ground(256, new Color(0.80f, 0.70f, 0.52f), new Color(0.88f, 0.79f, 0.60f), 4f, seed + 4, new Color(0.7f, 0.6f, 0.45f), 0.03f), 9f),
                Layer(ProceduralTextures.Ground(256, new Color(0.40f, 0.37f, 0.34f), new Color(0.55f, 0.52f, 0.48f), 8f, seed + 5, new Color(0.3f, 0.28f, 0.26f), 0.08f), 5f),
            };
            _data.terrainLayers = layers;
            _data.alphamapResolution = _alphaRes;
            PrecomputeSplatFactors(seed);
        }

        static TerrainLayer Layer(Texture2D tex, float tile)
        {
            return new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = 0.05f, metallic = 0f };
        }

        void PrecomputeSplatFactors(int seed)
        {
            int n = _alphaRes;
            _slope = new float[n, n];
            _dirt = new float[n, n];
            _shore = new float[n, n];
            _patch = new float[n, n];

            // Тропы от базы к краям карты
            for (int i = 0; i < 4; i++)
            {
                float ang = i * Mathf.PI * 0.5f + R(-0.4f, 0.4f);
                var pts = new Vector2[16];
                var c = new Vector2(WorldSize * 0.5f, WorldSize * 0.5f);
                for (int k = 0; k < pts.Length; k++)
                {
                    float t = k / (float)(pts.Length - 1);
                    float r = Mathf.Lerp(BaseFlatRadius * 0.6f, WorldSize * 0.5f, t);
                    float a = ang + (Mathf.PerlinNoise(t * 3f + i * 5f, seed * 0.1f) - 0.5f) * 0.9f;
                    pts[k] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                _trails.Add(pts);
            }

            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float wx = u * WorldSize, wz = v * WorldSize;
                _slope[y, x] = Noise2.Smooth(22f, 38f, _data.GetSteepness(u, v));
                float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(WorldSize * 0.5f, WorldSize * 0.5f));
                float baseDirt = 1f - Noise2.Smooth(BaseFlatRadius - 14f, BaseFlatRadius + 2f, d);
                float trail = 0f;
                foreach (var tr in _trails)
                    trail = Mathf.Max(trail, 1f - Noise2.Smooth(1.2f, 3.2f, DistToPolyline(new Vector2(wx, wz), tr)));
                _dirt[y, x] = Mathf.Max(baseDirt * (0.65f + 0.35f * Mathf.PerlinNoise(wx * 0.1f, wz * 0.1f)), trail * 0.85f);
                _patch[y, x] = Noise2.Fbm(wx * 0.03f + seed, wz * 0.03f, 3);
            }
        }

        static float DistToPolyline(Vector2 p, Vector2[] pts)
        {
            float best = float.MaxValue;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>Перекраска поверхности по «зелёности» (1 — луга, 0 — пустыня после пекла).</summary>
        public void PaintTerrain(float green)
        {
            if (Mathf.Abs(green - _lastGreen) < 0.015f) return;
            _lastGreen = green;
            int n = _alphaRes;
            var maps = new float[n, n, 5];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float p = _patch[y, x];
                float rock = _slope[y, x];
                float dirt = _dirt[y, x] * (1f - rock);
                float shore = _shore[y, x];
                float rest = Mathf.Max(0f, 1f - rock - dirt);
                float lush = rest * green * Mathf.Lerp(0.35f, 1f, p);
                float dry = rest * (green * (1f - Mathf.Lerp(0.35f, 1f, p)) + (1f - green) * (1f - p) * 0.6f);
                float sand = rest * (1f - green) * (0.4f + p * 0.6f) + shore * (0.4f + 0.6f * (1f - green));
                dirt += shore * 0.3f;
                float sum = lush + dry + dirt + sand + rock + 0.0001f;
                maps[y, x, 0] = lush / sum;
                maps[y, x, 1] = dry / sum;
                maps[y, x, 2] = dirt / sum;
                maps[y, x, 3] = sand / sum;
                maps[y, x, 4] = rock / sum;
            }
            _data.SetAlphamaps(0, 0, maps);
        }

        // ---------------------------------------------------------------- озёра

        void BuildLakes(WorldGrid grid)
        {
            int count = 4;
            var heights = _data.GetHeights(0, 0, _data.heightmapResolution, _data.heightmapResolution);
            int res = _data.heightmapResolution;
            for (int i = 0; i < count; i++)
            {
                Vector3 c = Vector3.zero;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float a = R(0f, Mathf.PI * 2f), d = R(58f, 100f);
                    c = Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                    bool ok = true;
                    foreach (var l in Lakes) if (Vector3.Distance(l.Center, c) < l.Radius + 30f) ok = false;
                    if (ok) break;
                }
                float radius = R(9f, 15f);
                float depth = 3.2f;
                float baseH = Terrain.SampleHeight(c);
                // выкапываем чашу
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float wx = (float)x / (res - 1) * WorldSize, wz = (float)y / (res - 1) * WorldSize;
                    float dd = Vector2.Distance(new Vector2(wx, wz), new Vector2(c.x, c.z));
                    float r2 = radius * (1f + (Mathf.PerlinNoise(wx * 0.15f, wz * 0.15f) - 0.5f) * 0.35f);
                    if (dd > r2 * 1.6f) continue;
                    float k = 1f - Noise2.Smooth(r2 * 0.35f, r2 * 1.5f, dd);
                    float target = (baseH - depth * k) / TerrainHeight;
                    heights[y, x] = Mathf.Min(heights[y, x], Mathf.Lerp(heights[y, x], target, Mathf.Clamp01(k * 1.4f)));
                }
                var lake = new Lake { Center = new Vector3(c.x, baseH, c.z), Radius = radius, FullLevel = baseH - 0.7f };
                Lakes.Add(lake);
            }
            _data.SetHeights(0, 0, heights);

            // уровень воды — чуть ниже самой низкой точки берега, чтобы вода не «висела» над склоном
            foreach (var lake in Lakes)
            {
                float rimMin = float.MaxValue;
                for (int k = 0; k < 20; k++)
                {
                    float a = k * Mathf.PI * 2f / 20f;
                    var rp = lake.Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * lake.Radius * 1.15f;
                    rimMin = Mathf.Min(rimMin, Terrain.SampleHeight(rp));
                }
                lake.FullLevel = Mathf.Max(lake.Center.y - 2.6f, Mathf.Min(rimMin - 0.25f, lake.Center.y - 0.7f));
            }

            foreach (var lake in Lakes)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(w.GetComponent<Collider>());
                w.name = "Lake";
                w.transform.SetParent(transform, false);
                w.transform.position = new Vector3(lake.Center.x, lake.FullLevel, lake.Center.z);
                w.transform.localScale = new Vector3(lake.Radius * 2.3f, 0.02f, lake.Radius * 2.3f);
                var r = w.GetComponent<MeshRenderer>();
                r.sharedMaterial = Palette.Water;
                r.shadowCastingMode = ShadowCastingMode.Off;
                lake.Water = w.transform;
                grid.SetBlocked(lake.Center, lake.Radius * 0.75f, true);
            }

            // песчаные берега в сплат-карте
            int n = _alphaRes;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float wx = (x + 0.5f) / n * WorldSize, wz = (y + 0.5f) / n * WorldSize;
                float s = 0f;
                foreach (var l in Lakes)
                {
                    float dd = Vector2.Distance(new Vector2(wx, wz), new Vector2(l.Center.x, l.Center.z));
                    s = Mathf.Max(s, 1f - Noise2.Smooth(l.Radius * 0.9f, l.Radius * 1.6f, dd));
                }
                _shore[y, x] = s;
            }
        }

        /// <summary>Уровень воды 0..1 (0 — озёра пересохли).</summary>
        public void ApplyWater(float level)
        {
            foreach (var l in Lakes)
            {
                if (l.Water == null) continue;
                float k = Mathf.Clamp01(level);
                l.Water.gameObject.SetActive(k > 0.03f);
                l.Water.position = new Vector3(l.Center.x, l.FullLevel - (1f - k) * 2.3f, l.Center.z);
                float s = l.Radius * 2.3f * Mathf.Lerp(0.35f, 1f, k);
                l.Water.localScale = new Vector3(s, 0.02f, s);
            }
        }

        public Lake NearestLake(Vector3 p, out float dist)
        {
            Lake best = null; dist = float.MaxValue;
            foreach (var l in Lakes)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(l.Center.x, l.Center.z)) - l.Radius;
                if (d < dist) { dist = d; best = l; }
            }
            return best;
        }

        // ---------------------------------------------------------------- растительность

        bool FarFromLakes(Vector3 p, float margin)
        {
            foreach (var l in Lakes)
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(l.Center.x, l.Center.z)) < l.Radius + margin) return false;
            return true;
        }

        void BuildTrees(WorldGrid grid)
        {
            _canopyMat = MaterialFactory.Lit(CanopyLush, 0f, 0.1f);
            _canopyMat.name = "canopy";
            var bark = MaterialFactory.Lit(new Color(0.24f, 0.19f, 0.14f), 0f, 0.1f);
            var root = new GameObject("Trees").transform;
            root.SetParent(transform, false);

            // Лесные массивы — кластеры вокруг нескольких центров + одиночные акации в саванне
            var clusters = new List<Vector3>();
            for (int i = 0; i < 7; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(55f, 112f);
                clusters.Add(Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d);
            }
            int placed = 0;
            for (int attempt = 0; attempt < 2600 && placed < 230; attempt++)
            {
                Vector3 p;
                if (R() < 0.7f)
                {
                    var c = clusters[_rnd.Next(clusters.Count)];
                    var off = Random2(R(), R()) * R(2f, 16f);
                    p = c + new Vector3(off.x, 0, off.y);
                }
                else p = new Vector3(R(8f, WorldSize - 8f), 0, R(8f, WorldSize - 8f));

                if (p.x < 6f || p.z < 6f || p.x > WorldSize - 6f || p.z > WorldSize - 6f) continue;
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Center.x, Center.z)) < BaseFlatRadius + 8f) continue;
                if (!FarFromLakes(p, 3f)) continue;
                var cell = grid.WorldToCell(p);
                if (grid.IsBlocked(cell.x, cell.y)) continue;
                p.y = Terrain.SampleHeight(p);
                if (_data.GetSteepness(p.x / WorldSize, p.z / WorldSize) > 25f) continue;
                BuildAcacia(root, p, bark, R(0.75f, 1.35f));
                grid.SetBlocked(p, 0.9f, true);
                placed++;
            }
        }

        static Vector2 Random2(float a, float b)
        {
            float ang = a * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Mathf.Sqrt(b);
        }

        void BuildAcacia(Transform parent, Vector3 pos, Material bark, float scale)
        {
            var mb = new ModelBuilder(parent, "Acacia");
            mb.Root.position = pos;
            mb.Root.localScale = Vector3.one * scale;
            mb.Root.localEulerAngles = new Vector3(0, R(0f, 360f), 0);
            float trunkH = R(3.2f, 4.5f);
            float lean = R(-8f, 8f);
            mb.Frustum(new Vector3(0, trunkH * 0.5f, 0), 0.22f, 0.13f, trunkH, bark, null, new Vector3(lean, 0, R(-6f, 6f)), 7);
            // ветви
            int branches = 2 + _rnd.Next(2);
            var canopyRoot = mb.Group("#canopy", new Vector3(0, trunkH, 0));
            for (int i = 0; i < branches; i++)
            {
                float a = i * 360f / branches + R(-20f, 20f);
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                mb.Frustum(new Vector3(0, trunkH - 0.2f, 0) + dir * 0.6f + Vector3.up * 0.3f, 0.1f, 0.06f, 1.6f, bark, null,
                    Quaternion.LookRotation(dir + Vector3.up * 0.9f).eulerAngles + new Vector3(90, 0, 0), 5);
            }
            // плоская широкая крона, как у акаций на референсах
            int blobs = 3 + _rnd.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                var off = Random2(R(), R()) * 1.8f;
                float w = R(2.4f, 3.6f);
                mb.Sphere(new Vector3(off.x, R(0.4f, 1.0f), off.y), new Vector3(w, R(0.8f, 1.2f), w * R(0.8f, 1.1f)), _canopyMat, canopyRoot);
            }
            ModelBuilder.CombineStatic(mb.Root);
            _canopies.Add(canopyRoot);
            _canopyBaseScale.Add(canopyRoot.localScale);
            var col = mb.Root.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, trunkH * 0.5f, 0);
            col.radius = 0.3f;
            col.height = trunkH;
        }

        void BuildGrass()
        {
            _grassMesh = MeshFactory.GrassTuft();
            _grassMat = MaterialFactory.Lit(GrassLush, 0f, 0.05f);
            _grassMat.enableInstancing = true;
            var list = new List<Matrix4x4>();
            int target = 9000;
            for (int i = 0; i < target * 3 && list.Count < target; i++)
            {
                var p = new Vector3(R(4f, WorldSize - 4f), 0, R(4f, WorldSize - 4f));
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Center.x, Center.z));
                if (d < BaseFlatRadius - 6f && R() < 0.85f) continue;
                if (!FarFromLakes(p, 0.5f)) continue;
                float patch = Noise2.Fbm(p.x * 0.03f, p.z * 0.03f, 3);
                if (R() > patch * 1.4f) continue;
                p.y = Terrain.SampleHeight(p);
                if (_data.GetSteepness(p.x / WorldSize, p.z / WorldSize) > 30f) continue;
                var s = new Vector3(R(0.8f, 1.6f), R(0.7f, 1.8f), R(0.8f, 1.6f));
                list.Add(Matrix4x4.TRS(p, Quaternion.Euler(0, R(0f, 360f), 0), s));
                _grassScales.Add(s);
            }
            for (int i = 0; i < list.Count; i += 1023)
            {
                int cnt = Mathf.Min(1023, list.Count - i);
                var arr = new Matrix4x4[cnt];
                list.CopyTo(i, arr, 0, cnt);
                _grassBatches.Add(arr);
            }
        }

        /// <summary>Применить «зелёность»: цвет крон, их размер, высоту и цвет травы, сплат-карту.</summary>
        public void ApplyGreenness(float green)
        {
            green = Mathf.Clamp01(green);
            _greenness = green;
            if (_canopyMat != null) MaterialFactory.SetColor(_canopyMat, Color.Lerp(CanopyDry, CanopyLush, green));
            if (_grassMat != null) MaterialFactory.SetColor(_grassMat, Color.Lerp(GrassDry, GrassLush, green));
            for (int i = 0; i < _canopies.Count; i++)
            {
                if (_canopies[i] == null) continue;
                _canopies[i].localScale = _canopyBaseScale[i] * Mathf.Lerp(0.45f, 1f, green);
            }
            PaintTerrain(green);
        }

        void Update()
        {
            if (_grassMesh == null || _grassMat == null) return;
            // в пекло трава «выгорает»: рисуем меньше кочек
            float frac = Mathf.Lerp(0.25f, 1f, _greenness);
            for (int b = 0; b < _grassBatches.Count; b++)
            {
                var arr = _grassBatches[b];
                int cnt = Mathf.Max(1, Mathf.RoundToInt(arr.Length * frac));
                Graphics.DrawMeshInstanced(_grassMesh, 0, _grassMat, arr, cnt, null, ShadowCastingMode.Off, true, 0);
            }
        }

        // ---------------------------------------------------------------- скалы, залежи, пещеры

        void BuildRocks(WorldGrid grid)
        {
            var root = new GameObject("Rocks").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 110; i++)
            {
                var p = new Vector3(R(6f, WorldSize - 6f), 0, R(6f, WorldSize - 6f));
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Center.x, Center.z)) < BaseFlatRadius + 4f) continue;
                if (!FarFromLakes(p, 1f)) continue;
                bool big = R() < 0.18f;
                float s = big ? R(2.2f, 4f) : R(0.4f, 1.2f);
                p.y = Terrain.SampleHeight(p) + s * 0.25f;
                var t = new GameObject(big ? "BigRock" : "Rock").transform;
                t.SetParent(root, false);
                t.position = p;
                t.rotation = Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f));
                t.localScale = new Vector3(s * R(1f, 1.6f), s * R(0.6f, 1f), s * R(1f, 1.4f));
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Boulder(_rnd.Next(6));
                t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = R() < 0.5f ? Palette.Rock : Palette.RockDark;
                if (big)
                {
                    t.gameObject.AddComponent<BoxCollider>();
                    grid.SetBlocked(p, s * 0.7f, true);
                }
            }
        }

        void BuildDeposits(WorldGrid grid)
        {
            var root = new GameObject("Deposits").transform;
            root.SetParent(transform, false);
            void Spawn(ResourceType type, int count, float minD, float maxD, int amountMin, int amountMax, float hiddenChance)
            {
                for (int i = 0; i < count; i++)
                {
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        float a = R(0f, Mathf.PI * 2f), d = R(minD, maxD);
                        var p = Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                        var c = grid.WorldToCell(p);
                        if (!grid.InBounds(c) || grid.IsBlocked(c.x, c.y)) continue;
                        if (!FarFromLakes(p, 4f)) continue;
                        bool near = false;
                        foreach (var dep in Registry.Deposits) if (Vector3.Distance(dep.transform.position, p) < 10f) near = true;
                        if (near) continue;
                        p = grid.CellCenter(c);
                        ResourceDeposit.Create(root, p, type, _rnd.Next(amountMin, amountMax + 1), R() < hiddenChance);
                        break;
                    }
                }
            }
            Spawn(ResourceType.Titanium, 3, 22f, 34f, 60, 90, 0f);
            Spawn(ResourceType.Silicon, 3, 20f, 34f, 70, 110, 0f);
            Spawn(ResourceType.Titanium, 5, 45f, 105f, 80, 160, 0.4f);
            Spawn(ResourceType.Crystals, 6, 50f, 110f, 40, 90, 0.4f);
            Spawn(ResourceType.Silicon, 5, 40f, 100f, 90, 160, 0.3f);
            Spawn(ResourceType.Alloys, 3, 45f, 95f, 120, 200, 0.2f); // обломки старой экспедиции — металлолом
        }

        void BuildCaves(WorldGrid grid)
        {
            var root = new GameObject("Caves").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    float a = R(0f, Mathf.PI * 2f), d = R(80f, 108f);
                    var p = Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                    if (!FarFromLakes(p, 8f)) continue;
                    bool near = false;
                    foreach (var cv in Caves) if (Vector3.Distance(cv.transform.position, p) < 50f) near = true;
                    if (near) continue;
                    p.y = Terrain.SampleHeight(p);
                    var facing = Quaternion.LookRotation(new Vector3(Center.x - p.x, 0, Center.z - p.z));
                    Caves.Add(CaveEntrance.Create(root, p, facing, i));
                    grid.SetBlocked(p - facing * Vector3.forward * 2f, 4.5f, true);
                    break;
                }
            }
        }

        Vector3 FindLandingZone(WorldGrid grid)
        {
            // открытая площадка за пределами стартовых построек, на восток от реактора
            var p = Center + new Vector3(24f, 0, -6f);
            p.y = Terrain.SampleHeight(p);
            return p;
        }
    }
}
