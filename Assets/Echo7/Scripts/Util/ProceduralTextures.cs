using UnityEngine;

namespace Echo7
{
    /// <summary>Шумы и процедурные текстуры (заглушки до появления настоящих текстур).</summary>
    public static class Noise2
    {
        /// <summary>Фрактальный шум Перлина 0..1.</summary>
        public static float Fbm(float x, float y, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq + i * 17.13f, y * freq + i * 31.7f) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        public static float Ridged(float x, float y, int octaves = 3)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq + 5.3f * i, y * freq + 9.1f * i) * 2f - 1f);
                sum += n * n * amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum;
        }
    }

    public static class ProceduralTextures
    {
        public static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Мягкий круг для частиц.</summary>
        public static Texture2D SoftCircle(int size = 64, float hardness = 0.35f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01((1f - d) / (1f - hardness));
                px[y * size + x] = new Color(1, 1, 1, a * a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Тайловая текстура грунта: шум + палитра из двух цветов + крапинки.</summary>
        public static Texture2D Ground(int size, Color a, Color b, float scale, int seed, Color? speck = null, float speckDensity = 0f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color[size * size];
            var rnd = new System.Random(seed);
            float ox = seed * 13.7f, oy = seed * 7.3f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // тайлинг: смешиваем шум по торической развёртке
                float u = (float)x / size, v = (float)y / size;
                float n = TileableFbm(u, v, scale, ox, oy);
                var c = Color.Lerp(a, b, Mathf.SmoothStep(0.25f, 0.75f, n));
                float fine = Mathf.PerlinNoise(x * 0.35f + ox, y * 0.35f + oy);
                c *= 0.9f + fine * 0.2f;
                if (speck.HasValue && rnd.NextDouble() < speckDensity) c = Color.Lerp(c, speck.Value, 0.6f);
                c.a = 1f;
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static float TileableFbm(float u, float v, float scale, float ox, float oy)
        {
            // Тайлинг через смешивание четырёх сдвинутых копий
            float n00 = Noise2.Fbm(u * scale + ox, v * scale + oy);
            float n10 = Noise2.Fbm((u - 1f) * scale + ox, v * scale + oy);
            float n01 = Noise2.Fbm(u * scale + ox, (v - 1f) * scale + oy);
            float n11 = Noise2.Fbm((u - 1f) * scale + ox, (v - 1f) * scale + oy);
            float a = Mathf.Lerp(n00, n10, u);
            float b = Mathf.Lerp(n01, n11, u);
            return Mathf.Lerp(a, b, v);
        }

        /// <summary>Текстура для панелей IMGUI: заливка + рамка + тонкий акцент сверху.</summary>
        public static Texture2D Panel(Color fill, Color border, Color accent, int size = 16)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                bool top = y >= size - 2;
                var c = edge ? border : fill;
                if (top && accent.a > 0f) c = accent;
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Круглая «сфера» для HUD (как шары здоровья в Diablo): радиальный градиент, заполнение снизу.</summary>
        public static Texture2D Orb(int size, Color inner, Color outer)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f - r, y + 0.5f - r);
                float d = p.magnitude / r;
                if (d > 1f) { px[y * size + x] = Color.clear; continue; }
                var hl = new Vector2(-0.3f, 0.35f) * r;
                float spec = Mathf.Clamp01(1f - Vector2.Distance(p, hl) / (r * 0.5f));
                var c = Color.Lerp(inner, outer, d * d);
                c += Color.white * spec * spec * 0.35f;
                c.a = Mathf.Clamp01((1f - d) * size * 0.5f);
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        public static Texture2D Ring(int size, Color c, float thickness)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - (1f - thickness)) / thickness);
                px[y * size + x] = new Color(c.r, c.g, c.b, c.a * a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
