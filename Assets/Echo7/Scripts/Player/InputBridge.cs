using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Echo7
{
    public enum GameKey
    {
        W, A, S, D, Q, Space, E, R, X, B, K, J, G, M, H, P, C, Tab, Escape, LeftShift,
        Alpha1, Alpha2, Alpha3, Alpha4, Delete, F1, F5, F6, F7, F8, F9, F10
    }

    /// <summary>
    /// Прослойка ввода: работает и со старым Input Manager, и с новым Input System
    /// (Unity сама выставляет ENABLE_INPUT_SYSTEM / ENABLE_LEGACY_INPUT_MANAGER по настройке Active Input Handling).
    /// </summary>
    public static class InputBridge
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static Key Map(GameKey k)
        {
            switch (k)
            {
                case GameKey.W: return Key.W;
                case GameKey.A: return Key.A;
                case GameKey.S: return Key.S;
                case GameKey.D: return Key.D;
                case GameKey.Q: return Key.Q;
                case GameKey.Space: return Key.Space;
                case GameKey.E: return Key.E;
                case GameKey.R: return Key.R;
                case GameKey.X: return Key.X;
                case GameKey.B: return Key.B;
                case GameKey.K: return Key.K;
                case GameKey.J: return Key.J;
                case GameKey.G: return Key.G;
                case GameKey.M: return Key.M;
                case GameKey.H: return Key.H;
                case GameKey.P: return Key.P;
                case GameKey.C: return Key.C;
                case GameKey.Tab: return Key.Tab;
                case GameKey.Escape: return Key.Escape;
                case GameKey.LeftShift: return Key.LeftShift;
                case GameKey.Alpha1: return Key.Digit1;
                case GameKey.Alpha2: return Key.Digit2;
                case GameKey.Alpha3: return Key.Digit3;
                case GameKey.Alpha4: return Key.Digit4;
                case GameKey.Delete: return Key.Delete;
                case GameKey.F1: return Key.F1;
                case GameKey.F5: return Key.F5;
                case GameKey.F6: return Key.F6;
                case GameKey.F7: return Key.F7;
                case GameKey.F8: return Key.F8;
                case GameKey.F9: return Key.F9;
                case GameKey.F10: return Key.F10;
            }
            return Key.None;
        }

        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static float ScrollDelta => Mouse.current != null ? Mathf.Clamp(Mouse.current.scroll.ReadValue().y / 120f, -3f, 3f) : 0f;

        static UnityEngine.InputSystem.Controls.ButtonControl Btn(int b)
        {
            var m = Mouse.current;
            if (m == null) return null;
            return b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;
        }

        public static bool MouseDown(int b) { var c = Btn(b); return c != null && c.wasPressedThisFrame; }
        public static bool MouseHeld(int b) { var c = Btn(b); return c != null && c.isPressed; }
        public static bool MouseUp(int b) { var c = Btn(b); return c != null && c.wasReleasedThisFrame; }
        public static bool KeyDown(GameKey k) { var kb = Keyboard.current; return kb != null && kb[Map(k)].wasPressedThisFrame; }
        public static bool KeyHeld(GameKey k) { var kb = Keyboard.current; return kb != null && kb[Map(k)].isPressed; }
#else
        static KeyCode Map(GameKey k)
        {
            switch (k)
            {
                case GameKey.W: return KeyCode.W;
                case GameKey.A: return KeyCode.A;
                case GameKey.S: return KeyCode.S;
                case GameKey.D: return KeyCode.D;
                case GameKey.Q: return KeyCode.Q;
                case GameKey.Space: return KeyCode.Space;
                case GameKey.E: return KeyCode.E;
                case GameKey.R: return KeyCode.R;
                case GameKey.X: return KeyCode.X;
                case GameKey.B: return KeyCode.B;
                case GameKey.K: return KeyCode.K;
                case GameKey.J: return KeyCode.J;
                case GameKey.G: return KeyCode.G;
                case GameKey.M: return KeyCode.M;
                case GameKey.H: return KeyCode.H;
                case GameKey.P: return KeyCode.P;
                case GameKey.C: return KeyCode.C;
                case GameKey.Tab: return KeyCode.Tab;
                case GameKey.Escape: return KeyCode.Escape;
                case GameKey.LeftShift: return KeyCode.LeftShift;
                case GameKey.Alpha1: return KeyCode.Alpha1;
                case GameKey.Alpha2: return KeyCode.Alpha2;
                case GameKey.Alpha3: return KeyCode.Alpha3;
                case GameKey.Alpha4: return KeyCode.Alpha4;
                case GameKey.Delete: return KeyCode.Delete;
                case GameKey.F1: return KeyCode.F1;
                case GameKey.F5: return KeyCode.F5;
                case GameKey.F6: return KeyCode.F6;
                case GameKey.F7: return KeyCode.F7;
                case GameKey.F8: return KeyCode.F8;
                case GameKey.F9: return KeyCode.F9;
                case GameKey.F10: return KeyCode.F10;
            }
            return KeyCode.None;
        }

        public static Vector2 MousePosition => Input.mousePosition;
        public static float ScrollDelta => Mathf.Clamp(Input.mouseScrollDelta.y, -3f, 3f);
        public static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
        public static bool MouseHeld(int b) => Input.GetMouseButton(b);
        public static bool MouseUp(int b) => Input.GetMouseButtonUp(b);
        public static bool KeyDown(GameKey k) => Input.GetKeyDown(Map(k));
        public static bool KeyHeld(GameKey k) => Input.GetKey(Map(k));
#endif

        /// <summary>Курсор над окном интерфейса — игровые клики игнорируются.</summary>
        public static bool PointerOverUi => Game.Ui != null && Game.Ui.IsPointerOverUi(MousePosition);
    }
}
