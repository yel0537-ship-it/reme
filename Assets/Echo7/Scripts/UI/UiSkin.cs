using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Хайтек-стиль интерфейса: голографические панели со скошенными углами, циановые грани и сканлайны,
    /// сегментные шкалы, шестигранные слоты, техничные шрифты ОС (Bahnschrift/Consolas с запасными вариантами).
    /// Всё генерируется кодом; для релиза стиль переносится в UI Toolkit (USS) без изменения раскладки.
    /// </summary>
    public static class UiSkin
    {
        // палитра
        public static readonly Color Cyan = new Color(0.35f, 0.88f, 1f);
        public static readonly Color CyanDim = new Color(0.16f, 0.42f, 0.55f);
        public static readonly Color Accent = new Color(1f, 0.68f, 0.22f);   // янтарный — предупреждения и акценты
        public static readonly Color Text = new Color(0.86f, 0.95f, 1f);
        public static readonly Color Dim = new Color(0.5f, 0.66f, 0.76f);
        public static readonly Color Good = new Color(0.4f, 1f, 0.72f);
        public static readonly Color Bad = new Color(1f, 0.34f, 0.3f);
        public static readonly Color PanelFill = new Color(0.012f, 0.045f, 0.07f, 0.84f);

        public static GUIStyle Panel, PanelLight, Header, Title, Label, Small, Tiny, Mono, MonoBig, MonoSmall,
            Button, ButtonAccent, ButtonDisabled, Tab, TabActive, Center, Big, Rich;
        public static Texture2D White, Dot, Scan, HexEdge, HexFill, Arrow, RadarBack, Sweep, Diamond;
        public static Font TechFont, MonoFont;
        static bool _ready;

        static Font TryOsFont(string[] names, int size)
        {
            try { return Font.CreateDynamicFontFromOSFont(names, size); }
            catch { return null; }
        }

        public static void Ensure()
        {
            if (_ready && Panel != null && Panel.normal.background != null) return;
            _ready = true;

            TechFont = TryOsFont(new[] { "Bahnschrift", "Segoe UI", "Roboto", "Helvetica Neue", "Arial" }, 16);
            MonoFont = TryOsFont(new[] { "Consolas", "Cascadia Mono", "Menlo", "DejaVu Sans Mono", "Courier New" }, 16);

            White = ProceduralTextures.Solid(Color.white);
            Dot = ProceduralTextures.SoftCircle(16, 0.6f);
            Scan = MakeScanlines();
            HexEdge = MakeHex(64, false);
            HexFill = MakeHex(64, true);
            Arrow = MakeArrow(32);
            RadarBack = MakeRadar(160);
            Sweep = MakeSweep(160);
            Diamond = MakeDiamond(16);

            var panelTex = Chamfer(32, 8, PanelFill, new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f), true, false, false, true);
            var panelLightTex = Chamfer(32, 6, new Color(0.03f, 0.09f, 0.13f, 0.9f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f), true, false, false, true);
            var btn = Chamfer(24, 6, new Color(0.03f, 0.11f, 0.15f, 0.92f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f), true, false, false, true);
            var btnHover = Chamfer(24, 6, new Color(0.05f, 0.2f, 0.27f, 0.95f), Cyan, true, false, false, true);
            var btnActive = Chamfer(24, 6, new Color(0.1f, 0.35f, 0.45f, 0.95f), Color.white, true, false, false, true);
            var btnAccent = Chamfer(24, 6, new Color(0.06f, 0.24f, 0.32f, 0.95f), Cyan, true, false, false, true);
            var btnDisabled = Chamfer(24, 6, new Color(0.04f, 0.05f, 0.06f, 0.85f), new Color(0.3f, 0.35f, 0.4f, 0.6f), true, false, false, true);

            Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10), border = new RectOffset(9, 9, 9, 9) };
            Panel.normal.background = panelTex;
            PanelLight = new GUIStyle(Panel) { border = new RectOffset(7, 7, 7, 7), padding = new RectOffset(8, 8, 6, 6) };
            PanelLight.normal.background = panelLightTex;

            Label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true, font = TechFont };
            Label.normal.textColor = Text;
            Rich = new GUIStyle(Label);
            Small = new GUIStyle(Label) { fontSize = 13 };
            Small.normal.textColor = Dim;
            Tiny = new GUIStyle(Label) { fontSize = 11 };
            Tiny.normal.textColor = Dim;
            Header = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            Header.normal.textColor = Cyan;
            Title = new GUIStyle(Label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = false };
            Title.normal.textColor = Text;
            Big = new GUIStyle(Label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Big.normal.textColor = Text;
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            Mono = new GUIStyle(Label) { font = MonoFont, fontSize = 14, wordWrap = false };
            Mono.normal.textColor = Text;
            MonoSmall = new GUIStyle(Mono) { fontSize = 12 };
            MonoSmall.normal.textColor = Dim;
            MonoBig = new GUIStyle(Mono) { fontSize = 34, fontStyle = FontStyle.Bold };
            MonoBig.normal.textColor = Cyan;

            Button = new GUIStyle(GUI.skin.button) { fontSize = 14, padding = new RectOffset(10, 10, 6, 6), border = new RectOffset(7, 7, 7, 7), richText = true, wordWrap = true, font = TechFont };
            Button.normal.background = btn; Button.hover.background = btnHover; Button.active.background = btnActive;
            Button.normal.textColor = Text; Button.hover.textColor = Color.white; Button.active.textColor = Color.white;
            ButtonAccent = new GUIStyle(Button);
            ButtonAccent.normal.background = btnAccent;
            ButtonAccent.normal.textColor = Cyan;
            ButtonDisabled = new GUIStyle(Button);
            ButtonDisabled.normal.background = btnDisabled; ButtonDisabled.hover.background = btnDisabled; ButtonDisabled.active.background = btnDisabled;
            ButtonDisabled.normal.textColor = Dim; ButtonDisabled.hover.textColor = Dim;
            Tab = new GUIStyle(Button) { fontSize = 12, padding = new RectOffset(8, 8, 4, 4) };
            TabActive = new GUIStyle(Tab);
            TabActive.normal.background = btnActive;
            TabActive.normal.textColor = Color.white;
        }

        // ------------------------------------------------------------ текстуры

        /// <summary>Панель со скошенными углами и тонкой гранью (9-slice).</summary>
        static Texture2D Chamfer(int size, int cut, Color fill, Color edge, bool tl, bool tr, bool bl, bool br)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            float s2 = 1.41421f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float left = x, right = size - 1 - x, bottom = y, top = size - 1 - y;
                float d = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
                bool outside = false;
                void Corner(bool on, float a, float b)
                {
                    if (!on) return;
                    float k = (a + b - cut) / s2;
                    if (k < 0f) outside = true;
                    else d = Mathf.Min(d, k);
                }
                Corner(tl, left, top);
                Corner(tr, right, top);
                Corner(bl, left, bottom);
                Corner(br, right, bottom);
                Color c;
                if (outside) c = Color.clear;
                else if (d < 1.2f) c = edge;
                else if (d < 3f) c = Color.Lerp(edge, fill, 0.75f) * new Color(1, 1, 1, 1);
                else c = fill;
                if (!outside && d >= 1.2f && d < 3f) c.a = Mathf.Max(fill.a, edge.a * 0.4f);
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeScanlines()
        {
            var t = new Texture2D(2, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 2; x++)
                t.SetPixel(x, y, new Color(Cyan.r, Cyan.g, Cyan.b, y == 0 ? 0.05f : 0f));
            t.Apply();
            return t;
        }

        static Texture2D MakeHex(int size, bool filled)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            float r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // шестиугольник с плоскими боками (заострён сверху и снизу)
                float qx = Mathf.Abs(x + 0.5f - size * 0.5f), qy = Mathf.Abs(y + 0.5f - size * 0.5f);
                float hexD = Mathf.Max(qx, qx * 0.5f + qy * 0.866f); // расстояние до грани (pointy-top)
                float edge = r * 0.866f - hexD;
                Color c;
                if (edge < 0f) c = Color.clear;
                else if (edge < 1.6f) c = new Color(1, 1, 1, 1f);
                else c = filled ? new Color(1, 1, 1, 0.92f) : new Color(1, 1, 1, 0.08f + 0.1f * Mathf.Clamp01(1f - edge / (r * 0.6f)));
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeArrow(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size - 0.5f;
                // треугольник остриём вправо
                float half = 0.42f * (1f - u);
                bool inside = u > 0.1f && Mathf.Abs(v) < half;
                float a = inside ? Mathf.Clamp01((half - Mathf.Abs(v)) * 30f) : 0f;
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeDiamond(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Abs(x + 0.5f - size * 0.5f) + Mathf.Abs(y + 0.5f - size * 0.5f);
                px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01((size * 0.5f - d) * 0.8f));
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeRadar(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                if (d > 1f) { px[y * size + x] = Color.clear; continue; }
                float a = 0.55f;
                var c = new Color(0.01f, 0.06f, 0.09f, a);
                float ring = Mathf.Min(Mathf.Abs(d - 1f), Mathf.Min(Mathf.Abs(d - 0.66f), Mathf.Abs(d - 0.33f))) * r;
                if (ring < 0.8f) c = new Color(Cyan.r, Cyan.g, Cyan.b, d > 0.97f ? 0.9f : 0.35f);
                if (Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f) c = new Color(Cyan.r, Cyan.g, Cyan.b, 0.2f);
                px[y * size + x] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D MakeSweep(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                if (d > 1f) { px[y * size + x] = Color.clear; continue; }
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // 0 — вправо
                float a = ang >= 0f && ang < 50f ? (1f - ang / 50f) * 0.35f : 0f;
                px[y * size + x] = new Color(Cyan.r, Cyan.g, Cyan.b, a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------ примитивы рисования

        public static float Pulse(float speed = 4f) => 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        public static void Tex(Rect r, Texture2D tex, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex);
            GUI.color = old;
        }

        /// <summary>Уголки-скобки вокруг прямоугольника (фирменная деталь sci-fi HUD).</summary>
        public static void Brackets(Rect r, Color c, float len = 12f, float th = 2f)
        {
            Fill(new Rect(r.x, r.y, len, th), c); Fill(new Rect(r.x, r.y, th, len), c);
            Fill(new Rect(r.xMax - len, r.y, len, th), c); Fill(new Rect(r.xMax - th, r.y, th, len), c);
            Fill(new Rect(r.x, r.yMax - th, len, th), c); Fill(new Rect(r.x, r.yMax - len, th, len), c);
            Fill(new Rect(r.xMax - len, r.yMax - th, len, th), c); Fill(new Rect(r.xMax - th, r.yMax - len, th, len), c);
        }

        public static void Scanlines(Rect r)
        {
            GUI.DrawTextureWithTexCoords(r, Scan, new Rect(0, Time.unscaledTime * 0.5f, r.width / 2f, r.height / 4f));
        }

        /// <summary>Голографическая панель: фон, сканлайны, грань.</summary>
        public static void Panel2(Rect r, bool light = false)
        {
            GUI.Box(r, GUIContent.none, light ? PanelLight : Panel);
            if (!light) Scanlines(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6));
        }

        /// <summary>Окно: панель + полоса заголовка + скобки. Возвращает область содержимого.</summary>
        public static Rect Window(Rect r, string title, string tag = null)
        {
            Panel2(r);
            Fill(new Rect(r.x + 10, r.y + 40, r.width - 20, 1), new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f));
            Fill(new Rect(r.x + 10, r.y + 38, 60, 3), Cyan);
            GUI.Label(new Rect(r.x + 16, r.y + 8, r.width - 80, 30), title.ToUpperInvariant(), Title);
            if (!string.IsNullOrEmpty(tag)) GUI.Label(new Rect(r.xMax - 260, r.y + 14, 210, 20), tag, new GUIStyle(MonoSmall) { alignment = TextAnchor.UpperRight });
            Brackets(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f), 14f, 2f);
            return new Rect(r.x + 16, r.y + 50, r.width - 32, r.height - 60);
        }

        /// <summary>Сегментная шкала.</summary>
        public static void SegBar(Rect r, float fraction, int segments, Color c, float gap = 2f)
        {
            fraction = Mathf.Clamp01(fraction);
            float w = (r.width - gap * (segments - 1)) / segments;
            int lit = Mathf.CeilToInt(fraction * segments - 0.001f);
            for (int i = 0; i < segments; i++)
            {
                var sr = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                Fill(sr, i < lit ? c : new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, 0.55f));
            }
        }

        /// <summary>Тонкая шкала с подложкой (оставлена для совместимости).</summary>
        public static void Bar(Rect r, float fraction, Color fill, Color? back = null)
        {
            Fill(r, back ?? new Color(0f, 0.05f, 0.08f, 0.7f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), fill);
            // риски через 10% — «приборная» шкала
            if (r.width >= 60f)
                for (int i = 1; i < 10; i++)
                    Fill(new Rect(r.x + r.width * i / 10f, r.y, 1f, r.height), new Color(0f, 0.02f, 0.04f, 0.55f));
        }

        /// <summary>Поворот последующего рисования вокруг точки (корректно при масштабе GUI.matrix).
        /// Возвращает прежнюю матрицу — её нужно вернуть в GUI.matrix.</summary>
        public static Matrix4x4 Rotate(float degrees, Vector2 pivot)
        {
            var m = GUI.matrix;
            GUI.matrix = m * Matrix4x4.TRS(pivot, Quaternion.Euler(0f, 0f, degrees), Vector3.one) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
            return m;
        }

        public static void Hex(Rect r, Color c, bool filled)
        {
            Tex(r, filled ? HexFill : HexEdge, c);
        }

        /// <summary>Пульсирующая рамка «сюда смотри» (подсветка обучения).</summary>
        public static void Highlight(Rect r)
        {
            float p = Pulse(6f);
            var c = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f + 0.5f * p);
            float g = 3f + 4f * p;
            Brackets(new Rect(r.x - g, r.y - g, r.width + g * 2, r.height + g * 2), c, 16f, 3f);
        }

        /// <summary>Линия из точки a в b.</summary>
        public static void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = Rotate(ang, a);
            Fill(new Rect(a.x, a.y - width / 2f, len, width), c);
            GUI.matrix = m;
        }

        public static Color HealthColor(float f) => f > 0.6f ? Good : f > 0.3f ? Accent : Bad;

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        public static string Col(string text, Color c) => $"<color=#{Hex(c)}>{text}</color>";
    }
}
