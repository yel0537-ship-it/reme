using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Стили IMGUI в духе sci-fi HUD: тёмные полупрозрачные панели, оранжевый акцент, голубые подсветки.
    /// IMGUI выбран для прототипа как самый быстрый способ собрать весь интерфейс кодом; финальный UI
    /// стоит перенести на UI Toolkit (структура окон уже разделена по методам).
    /// </summary>
    public static class UiSkin
    {
        public static readonly Color Accent = new Color(1f, 0.55f, 0.18f);
        public static readonly Color Cyan = new Color(0.4f, 0.8f, 1f);
        public static readonly Color Text = new Color(0.88f, 0.9f, 0.92f);
        public static readonly Color Dim = new Color(0.6f, 0.64f, 0.7f);
        public static readonly Color Good = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color Bad = new Color(1f, 0.4f, 0.32f);

        public static GUIStyle Panel, PanelLight, Header, Title, Label, Small, Tiny, Button, ButtonAccent, ButtonDisabled, Tab, TabActive, Center, Big, Rich;
        public static Texture2D White, Dot, OrbRed, OrbOrange, OrbBack, RingTex;
        static bool _ready;

        public static void Ensure()
        {
            if (_ready && Panel != null && Panel.normal.background != null) return;
            _ready = true;
            White = ProceduralTextures.Solid(Color.white);
            Dot = ProceduralTextures.SoftCircle(16, 0.6f);
            OrbRed = ProceduralTextures.Orb(128, new Color(0.85f, 0.12f, 0.1f), new Color(0.35f, 0.02f, 0.02f));
            OrbOrange = ProceduralTextures.Orb(128, new Color(1f, 0.6f, 0.15f), new Color(0.45f, 0.18f, 0.02f));
            OrbBack = ProceduralTextures.Orb(128, new Color(0.08f, 0.08f, 0.1f), new Color(0.02f, 0.02f, 0.03f));
            RingTex = ProceduralTextures.Ring(128, new Color(1f, 0.55f, 0.2f, 0.9f), 0.06f);

            var panelTex = ProceduralTextures.Panel(new Color(0.05f, 0.06f, 0.075f, 0.88f), new Color(0.25f, 0.27f, 0.3f, 0.9f), new Color(1f, 0.55f, 0.18f, 0.9f));
            var panelLightTex = ProceduralTextures.Panel(new Color(0.11f, 0.12f, 0.14f, 0.92f), new Color(0.3f, 0.32f, 0.36f, 0.9f), new Color(0, 0, 0, 0));
            var btn = ProceduralTextures.Panel(new Color(0.14f, 0.15f, 0.18f, 0.95f), new Color(0.32f, 0.34f, 0.38f, 1f), new Color(0, 0, 0, 0));
            var btnHover = ProceduralTextures.Panel(new Color(0.2f, 0.18f, 0.15f, 0.95f), new Color(1f, 0.55f, 0.18f, 1f), new Color(0, 0, 0, 0));
            var btnActive = ProceduralTextures.Panel(new Color(0.35f, 0.2f, 0.08f, 0.95f), new Color(1f, 0.6f, 0.2f, 1f), new Color(0, 0, 0, 0));
            var btnAccent = ProceduralTextures.Panel(new Color(0.45f, 0.24f, 0.07f, 0.95f), new Color(1f, 0.6f, 0.2f, 1f), new Color(0, 0, 0, 0));
            var btnDisabled = ProceduralTextures.Panel(new Color(0.09f, 0.09f, 0.1f, 0.85f), new Color(0.2f, 0.2f, 0.22f, 1f), new Color(0, 0, 0, 0));

            Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10), border = new RectOffset(3, 3, 3, 3) };
            Panel.normal.background = panelTex;
            PanelLight = new GUIStyle(Panel);
            PanelLight.normal.background = panelLightTex;
            PanelLight.padding = new RectOffset(8, 8, 6, 6);

            Label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            Label.normal.textColor = Text;
            Rich = new GUIStyle(Label);
            Small = new GUIStyle(Label) { fontSize = 13 };
            Small.normal.textColor = Dim;
            Tiny = new GUIStyle(Label) { fontSize = 11 };
            Tiny.normal.textColor = Dim;
            Header = new GUIStyle(Label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = false };
            Header.normal.textColor = Accent;
            Title = new GUIStyle(Label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = false };
            Title.normal.textColor = Text;
            Big = new GUIStyle(Label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Big.normal.textColor = Text;
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };

            Button = new GUIStyle(GUI.skin.button) { fontSize = 14, padding = new RectOffset(10, 10, 6, 6), border = new RectOffset(3, 3, 3, 3), richText = true, wordWrap = true };
            Button.normal.background = btn; Button.hover.background = btnHover; Button.active.background = btnActive;
            Button.normal.textColor = Text; Button.hover.textColor = Color.white; Button.active.textColor = Color.white;
            ButtonAccent = new GUIStyle(Button);
            ButtonAccent.normal.background = btnAccent;
            ButtonDisabled = new GUIStyle(Button);
            ButtonDisabled.normal.background = btnDisabled; ButtonDisabled.hover.background = btnDisabled; ButtonDisabled.active.background = btnDisabled;
            ButtonDisabled.normal.textColor = Dim; ButtonDisabled.hover.textColor = Dim;
            Tab = new GUIStyle(Button) { fontSize = 13, padding = new RectOffset(8, 8, 5, 5) };
            TabActive = new GUIStyle(Tab);
            TabActive.normal.background = btnAccent;
        }

        public static void Bar(Rect r, float fraction, Color fill, Color? back = null)
        {
            var old = GUI.color;
            GUI.color = back ?? new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, White);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), White);
            GUI.color = old;
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        /// <summary>Шар как в Diablo: заполнение снизу вверх.</summary>
        public static void Orb(Rect r, float fraction, Texture2D fill)
        {
            GUI.DrawTexture(r, OrbBack);
            fraction = Mathf.Clamp01(fraction);
            if (fraction > 0f)
            {
                var clip = new Rect(r.x, r.y + r.height * (1f - fraction), r.width, r.height * fraction);
                GUI.BeginGroup(clip);
                GUI.DrawTexture(new Rect(0, -r.height * (1f - fraction), r.width, r.height), fill);
                GUI.EndGroup();
            }
            GUI.DrawTexture(r, RingTex);
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        public static string Col(string text, Color c) => $"<color=#{Hex(c)}>{text}</color>";
    }
}
