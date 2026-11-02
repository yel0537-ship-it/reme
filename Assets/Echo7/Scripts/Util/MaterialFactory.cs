using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Echo7
{
    /// <summary>
    /// Создание материалов из кода, независимо от пайплайна: Built-in (Standard) или URP (Universal Render Pipeline/Lit).
    /// Когда появятся настоящие ассеты, достаточно заменить вызовы Palette.* на ссылки на материалы.
    /// </summary>
    public static class MaterialFactory
    {
        static Shader _lit, _sprite, _terrain;
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static bool IsSrp => GraphicsSettings.currentRenderPipeline != null;

        public static Shader LitShader
        {
            get
            {
                if (_lit != null) return _lit;
                if (IsSrp) _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                if (_lit == null) _lit = Shader.Find("Diffuse");
                return _lit;
            }
        }

        /// <summary>Простой неосвещённый шейдер с альфой. Работает и в Built-in, и в URP.</summary>
        public static Shader SpriteShader
        {
            get
            {
                if (_sprite != null) return _sprite;
                _sprite = Shader.Find("Sprites/Default");
                if (_sprite == null && IsSrp) _sprite = Shader.Find("Universal Render Pipeline/Unlit");
                if (_sprite == null) _sprite = Shader.Find("Unlit/Color");
                return _sprite;
            }
        }

        public static Shader TerrainShader
        {
            get
            {
                if (_terrain != null) return _terrain;
                if (IsSrp) _terrain = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (_terrain == null) _terrain = Shader.Find("Nature/Terrain/Standard");
                return _terrain;
            }
        }

        public static Material Lit(Color color, float metallic = 0.1f, float smoothness = 0.35f)
        {
            var m = new Material(LitShader) { enableInstancing = true };
            SetColor(m, color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            return m;
        }

        public static Material Emissive(Color color, Color emission, float intensity = 2f)
        {
            var m = Lit(color, 0f, 0.6f);
            SetEmission(m, emission * intensity);
            return m;
        }

        /// <summary>Полупрозрачный освещённый материал (вода, голограммы).</summary>
        public static Material Transparent(Color color, float smoothness = 0.8f, Color? emission = null)
        {
            var m = Lit(color, 0f, smoothness);
            MakeTransparent(m);
            if (emission.HasValue) SetEmission(m, emission.Value);
            return m;
        }

        /// <summary>Неосвещённый материал: лучи, трассеры, частицы, свечение.</summary>
        public static Material Unlit(Color color, Texture tex = null)
        {
            var m = new Material(SpriteShader);
            if (tex != null) m.mainTexture = tex;
            SetColor(m, color);
            m.renderQueue = 3100;
            return m;
        }

        /// <summary>Кэшированный материал по ключу — чтобы стены/деревья делили один материал (инстансинг/батчинг).</summary>
        public static Material Cached(string key, System.Func<Material> create)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = create();
            m.name = key;
            Cache[key] = m;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static Color GetColor(Material m)
        {
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        public static void SetEmission(Material m, Color e)
        {
            if (!m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        public static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface"))
            {
                // URP Lit
                m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.SetShaderPassEnabled("ShadowCaster", false);
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            else if (m.HasProperty("_Mode"))
            {
                // Built-in Standard, режим Fade
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    /// <summary>
    /// Палитра проекта по референсам: светло-серые модульные корпуса, тёмные вставки,
    /// оранжевые световые полосы, голубое голо-свечение, саванна.
    /// </summary>
    public static class Palette
    {
        public static readonly Color HullLightColor = new Color(0.74f, 0.73f, 0.70f);
        public static readonly Color HullMidColor = new Color(0.55f, 0.55f, 0.54f);
        public static readonly Color HullDarkColor = new Color(0.20f, 0.21f, 0.23f);
        public static readonly Color OrangeGlow = new Color(1f, 0.52f, 0.16f);
        public static readonly Color BlueGlow = new Color(0.30f, 0.72f, 1f);
        public static readonly Color RedGlow = new Color(1f, 0.15f, 0.1f);
        public static readonly Color HoloOk = new Color(0.25f, 0.85f, 1f, 0.35f);
        public static readonly Color HoloBad = new Color(1f, 0.25f, 0.2f, 0.35f);

        public static Material HullLight => MaterialFactory.Cached("hull_light", () => MaterialFactory.Lit(HullLightColor, 0.35f, 0.45f));
        public static Material HullMid => MaterialFactory.Cached("hull_mid", () => MaterialFactory.Lit(HullMidColor, 0.4f, 0.4f));
        public static Material HullDark => MaterialFactory.Cached("hull_dark", () => MaterialFactory.Lit(HullDarkColor, 0.5f, 0.35f));
        public static Material Concrete => MaterialFactory.Cached("concrete", () => MaterialFactory.Lit(new Color(0.62f, 0.60f, 0.56f), 0f, 0.15f));
        public static Material Metal => MaterialFactory.Cached("metal", () => MaterialFactory.Lit(new Color(0.42f, 0.43f, 0.45f), 0.8f, 0.55f));
        public static Material Rubber => MaterialFactory.Cached("rubber", () => MaterialFactory.Lit(new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f));
        public static Material Glass => MaterialFactory.Cached("glass", () => MaterialFactory.Lit(new Color(0.08f, 0.1f, 0.13f), 0.2f, 0.92f));
        public static Material Orange => MaterialFactory.Cached("glow_orange", () => MaterialFactory.Emissive(OrangeGlow, OrangeGlow, 2.6f));
        public static Material Blue => MaterialFactory.Cached("glow_blue", () => MaterialFactory.Emissive(BlueGlow, BlueGlow, 3f));
        public static Material Red => MaterialFactory.Cached("glow_red", () => MaterialFactory.Emissive(RedGlow, RedGlow, 3f));
        public static Material Hazard => MaterialFactory.Cached("hazard", () => MaterialFactory.Lit(new Color(0.85f, 0.65f, 0.12f), 0.1f, 0.3f));
        public static Material Wood => MaterialFactory.Cached("wood", () => MaterialFactory.Lit(new Color(0.30f, 0.21f, 0.14f), 0f, 0.1f));
        public static Material Rock => MaterialFactory.Cached("rock", () => MaterialFactory.Lit(new Color(0.42f, 0.38f, 0.34f), 0f, 0.12f));
        public static Material RockDark => MaterialFactory.Cached("rock_dark", () => MaterialFactory.Lit(new Color(0.25f, 0.23f, 0.22f), 0f, 0.1f));
        public static Material FlagWhite => MaterialFactory.Cached("flag_w", () => MaterialFactory.Lit(new Color(0.95f, 0.95f, 0.95f), 0f, 0.2f));
        public static Material FlagBlue => MaterialFactory.Cached("flag_b", () => MaterialFactory.Lit(new Color(0.05f, 0.25f, 0.7f), 0f, 0.2f));
        public static Material FlagRed => MaterialFactory.Cached("flag_r", () => MaterialFactory.Lit(new Color(0.8f, 0.08f, 0.08f), 0f, 0.2f));
        public static Material HoloValid => MaterialFactory.Cached("holo_ok", () => MaterialFactory.Transparent(HoloOk, 0.9f, BlueGlow * 0.8f));
        public static Material HoloInvalid => MaterialFactory.Cached("holo_bad", () => MaterialFactory.Transparent(HoloBad, 0.9f, RedGlow * 0.8f));
        public static Material HoloBuild => MaterialFactory.Cached("holo_build", () => MaterialFactory.Transparent(new Color(0.3f, 0.8f, 1f, 0.22f), 0.9f, BlueGlow * 0.5f));
        public static Material Water => MaterialFactory.Cached("water", () => MaterialFactory.Transparent(new Color(0.16f, 0.36f, 0.42f, 0.78f), 0.95f));
    }
}
