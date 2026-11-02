using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum UiWindow { None, Build, Skills, Tasks, Garrison, GlobalMap, Orbital, Help }

    /// <summary>
    /// Весь интерфейс: HUD в духе Diablo (шары здоровья/патронов, панель способностей), окна (строительство,
    /// навыки, задачи, гарнизон, глобальная карта), диалоги, инспектор построек, оверлеи над объектами.
    /// Доступ к окнам «запирается» рангом через PlayerRankManager (см. RequiredFeature).
    /// </summary>
    public partial class UiManager : MonoBehaviour
    {
        public UiWindow Window { get; private set; }
        public Structure Inspected { get; private set; }
        public bool Paused { get; private set; }
        public bool ModalOpen => (Game.Dialogue != null && Game.Dialogue.IsOpen) || Paused || Game.State == GameState.GameOver;
        public bool WantsScroll { get; private set; }

        float _scale = 1f;
        float W => Screen.width / _scale;
        float H => Screen.height / _scale;
        readonly List<Rect> _blockers = new List<Rect>();
        readonly List<Rect> _blockersPrev = new List<Rect>();
        Vector2 _mouseGui;
        int _buildTab;
        Vector2 _scroll, _scroll2;
        string _selectedRegion = "home";
        float _demolishConfirmUntil;
        float _speed = 1f;

        struct FloatingText { public Vector3 Pos; public string Text; public Color Color; public float Born; }
        readonly List<FloatingText> _floats = new List<FloatingText>();

        public void Init() { }

        /// <summary>Требуемая возможность для окна (rank gating).</summary>
        public static Feature? RequiredFeature(UiWindow w)
        {
            switch (w)
            {
                case UiWindow.GlobalMap: return Feature.GlobalMap;
                case UiWindow.Orbital: return Feature.OrbitalDelivery;
            }
            return null;
        }

        public void Open(UiWindow w)
        {
            var f = RequiredFeature(w);
            if (f.HasValue && !Game.Rank.IsUnlocked(f.Value))
            {
                Game.Log?.Warn($"{PlayerRankManager.FeatureName(f.Value)}: {Game.Rank.LockReason(f.Value)}");
                Game.Sound?.Play2D(Sfx.UiError, 0.6f);
                return;
            }
            Window = Window == w ? UiWindow.None : w;
            _scroll = Vector2.zero;
            if (Window == UiWindow.Build) Inspected = null;
            Game.Sound?.Play2D(Sfx.UiClick, 0.5f);
        }

        public void Close() => Window = UiWindow.None;

        public void Inspect(Structure s)
        {
            Inspected = s;
            _demolishConfirmUntil = 0f;
        }

        public void OpenCenterComms() => Game.Dialogue.Start(DialogueLibrary.CenterComms());
        public void OpenGlobalMap() => Open(UiWindow.GlobalMap);

        public void FloatText(Vector3 world, string text, Color color)
        {
            _floats.Add(new FloatingText { Pos = world, Text = text, Color = color, Born = Time.unscaledTime });
            if (_floats.Count > 30) _floats.RemoveAt(0);
        }

        public bool IsPointerOverUi(Vector2 screen)
        {
            var p = new Vector2(screen.x / _scale, (Screen.height - screen.y) / _scale);
            foreach (var r in _blockersPrev) if (r.Contains(p)) return true;
            return false;
        }

        void Block(Rect r) => _blockers.Add(r);

        public void TogglePause()
        {
            if (Game.State == GameState.GameOver) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : _speed;
            Game.Sound?.Play2D(Sfx.UiClick, 0.5f);
        }

        public void SetSpeed(float s)
        {
            _speed = s;
            if (!Paused && !(Game.Dialogue != null && Game.Dialogue.IsOpen)) Time.timeScale = s;
        }

        void Update()
        {
            if (Game.State != GameState.Playing) return;
            var dlg = Game.Dialogue;
            if (dlg != null && dlg.IsOpen)
            {
                for (int i = 0; i < 4; i++) if (InputBridge.KeyDown(GameKey.Alpha1 + i)) dlg.Choose(i);
                if (InputBridge.KeyDown(GameKey.Escape)) dlg.Close();
                return;
            }
            if (InputBridge.KeyDown(GameKey.Escape))
            {
                if (Game.Build.IsPlacing || Game.Build.CancelFrame == Time.frameCount) { /* BuildSystem сам отменит */ }
                else if (Window != UiWindow.None) Close();
                else if (Inspected != null) Inspected = null;
                else TogglePause();
                return;
            }
            if (Paused) return;
            if (InputBridge.KeyDown(GameKey.B)) Open(UiWindow.Build);
            if (InputBridge.KeyDown(GameKey.K)) Open(UiWindow.Skills);
            if (InputBridge.KeyDown(GameKey.J)) Open(UiWindow.Tasks);
            if (InputBridge.KeyDown(GameKey.G)) Open(UiWindow.Garrison);
            if (InputBridge.KeyDown(GameKey.M)) Open(UiWindow.GlobalMap);
            if (InputBridge.KeyDown(GameKey.F1) || InputBridge.KeyDown(GameKey.H)) Open(UiWindow.Help);
            if (InputBridge.KeyDown(GameKey.Delete) && Inspected != null) { Inspected.Demolish(); Inspected = null; }
            if (Inspected != null && (Inspected.IsDestroyed || Inspected == null)) Inspected = null;
        }

        // ------------------------------------------------------------ OnGUI

        void OnGUI()
        {
            if (Game.State == GameState.Booting) return;
            UiSkin.Ensure();
            _scale = Mathf.Max(0.5f, Screen.height / 1080f * (Game.Config != null ? Game.Config.uiScale : 1f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1f));
            var e = Event.current;
            _mouseGui = e.mousePosition;
            if (e.type == EventType.Layout)
            {
                _blockersPrev.Clear();
                _blockersPrev.AddRange(_blockers);
                _blockers.Clear();
                WantsScroll = false;
            }

            DrawWorldOverlays();
            DrawHud();
            DrawInspector();
            switch (Window)
            {
                case UiWindow.Build: DrawBuildWindow(); break;
                case UiWindow.Skills: DrawSkillsWindow(); break;
                case UiWindow.Tasks: DrawTasksWindow(); break;
                case UiWindow.Garrison: DrawGarrisonWindow(); break;
                case UiWindow.GlobalMap: DrawGlobalMapWindow(); break;
                case UiWindow.Orbital: DrawOrbitalWindow(); break;
                case UiWindow.Help: DrawHelpWindow(); break;
            }
            DrawToast();
            if (Game.Dialogue != null && Game.Dialogue.IsOpen) DrawDialogue();
            if (Paused) DrawPause();
            if (Game.State == GameState.GameOver) DrawGameOver();
            DrawHoverLabel();
        }

        // ------------------------------------------------------------ оверлеи над объектами

        bool WorldToGui(Vector3 world, out Vector2 gui)
        {
            gui = Vector2.zero;
            var cam = Game.CameraRig != null ? Game.CameraRig.Cam : null;
            if (cam == null) return false;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;
            gui = new Vector2(sp.x / _scale, (Screen.height - sp.y) / _scale);
            return gui.x > -50 && gui.y > -50 && gui.x < W + 50 && gui.y < H + 50;
        }

        void DrawWorldOverlays()
        {
            // полоски здоровья раненых врагов
            foreach (var en in Registry.Enemies)
            {
                if (en == null || en.IsDead) continue;
                bool tank = en.Archetype.Kind == EnemyKind.Tank;
                if (en.Health.Fraction >= 0.999f && !tank) continue;
                if (!WorldToGui(en.transform.position + Vector3.up * (en.Archetype.Size * 1.5f + 0.5f), out var g)) continue;
                float w = tank ? 90f : 40f;
                UiSkin.Bar(new Rect(g.x - w / 2, g.y, w, tank ? 7f : 4f), en.Health.Fraction, en.IsHostile ? UiSkin.Bad : new Color(0.9f, 0.8f, 0.3f));
                if (tank) GUI.Label(new Rect(g.x - 60, g.y - 18, 120, 18), UiSkin.Col("ТАНК", UiSkin.Bad), UiSkin.Center);
            }
            // постройки: стройка/повреждения
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsDestroyed) continue;
                bool show = !s.IsBuilt || s.Health.Fraction < 0.98f || s.CableCut || (s is Turret t && t.Jammed);
                if (!show) continue;
                if (!WorldToGui(s.transform.position + Vector3.up * (s.Def.Height + 0.8f), out var g)) continue;
                if (!s.IsBuilt) UiSkin.Bar(new Rect(g.x - 30, g.y, 60, 5), s.BuildProgress, UiSkin.Cyan);
                else UiSkin.Bar(new Rect(g.x - 30, g.y, 60, 5), s.Health.Fraction, s.Health.Fraction > 0.5f ? UiSkin.Good : UiSkin.Bad);
                if (s.CableCut) GUI.Label(new Rect(g.x - 60, g.y - 20, 120, 20), UiSkin.Col("⚡ кабель", UiSkin.Accent), UiSkin.Center);
                else if (s is Turret tt && tt.Jammed) GUI.Label(new Rect(g.x - 60, g.y - 20, 120, 20), UiSkin.Col("заклинило", UiSkin.Accent), UiSkin.Center);
            }
            // гарнизон: имя и мораль
            foreach (var n in Registry.Npcs)
            {
                if (n == null || n.Health.IsDead || n.IsAway) continue;
                if (!WorldToGui(n.transform.position + Vector3.up * 2.3f, out var g)) continue;
                var c = n.Morale >= 50 ? UiSkin.Good : n.Morale >= 25 ? UiSkin.Accent : UiSkin.Bad;
                GUI.Label(new Rect(g.x - 70, g.y - 18, 140, 18), UiSkin.Col(n.Name, c) + (n.Sickness > 0f ? UiSkin.Col(" ✚", UiSkin.Bad) : ""), new GUIStyle(UiSkin.Tiny) { alignment = TextAnchor.MiddleCenter, richText = true });
                if (n.Health.Fraction < 0.99f) UiSkin.Bar(new Rect(g.x - 18, g.y, 36, 3), n.Health.Fraction, UiSkin.Good);
            }
            // VIP
            if (Game.Vip?.VipActor != null && Game.Vip.Current != null && WorldToGui(Game.Vip.VipActor.transform.position + Vector3.up * 2.6f, out var vg))
                GUI.Label(new Rect(vg.x - 15, vg.y - 30, 30, 30), UiSkin.Col("!", UiSkin.Accent), new GUIStyle(UiSkin.Big) { fontSize = 28 });
            // добыча игрока
            var pl = Game.Player;
            if (pl != null && pl.IsMining && WorldToGui(pl.transform.position + Vector3.up * 2.4f, out var mg))
                UiSkin.Bar(new Rect(mg.x - 30, mg.y, 60, 6), pl.MiningProgress, UiSkin.Accent);
            // всплывающие надписи
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                float age = Time.unscaledTime - f.Born;
                if (age > 1.8f) { _floats.RemoveAt(i); continue; }
                if (!WorldToGui(f.Pos + Vector3.up * age * 1.2f, out var g)) continue;
                var c = f.Color; c.a = 1f - age / 1.8f;
                var st = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
                st.normal.textColor = c;
                GUI.Label(new Rect(g.x - 120, g.y - 12, 240, 24), f.Text, st);
            }
        }

        void DrawHoverLabel()
        {
            var pl = Game.Player;
            if (pl == null || string.IsNullOrEmpty(pl.HoverLabel) || ModalOpen) return;
            var r = new Rect(_mouseGui.x + 18, _mouseGui.y + 10, 320, 26);
            var st = new GUIStyle(UiSkin.Small) { wordWrap = false };
            var size = st.CalcSize(new GUIContent(pl.HoverLabel));
            r.width = size.x + 16;
            GUI.Box(r, GUIContent.none, UiSkin.PanelLight);
            GUI.Label(new Rect(r.x + 8, r.y + 3, r.width, 22), UiSkin.Col(pl.HoverLabel, UiSkin.Text), st);
        }

        void DrawToast()
        {
            var log = Game.Log;
            if (log?.Toast == null || Time.unscaledTime > log.ToastUntil) return;
            var t = log.Toast.Value;
            float a = Mathf.Clamp01((log.ToastUntil - Time.unscaledTime) * 2f);
            var r = new Rect(W / 2 - 380, 92, 760, 46);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
            GUI.Box(r, GUIContent.none, UiSkin.Panel);
            var st = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            st.normal.textColor = MessageLog.ColorOf(t.Kind);
            GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, r.height - 8), t.Text, st);
            GUI.color = old;
        }
    }
}
