using UnityEngine;

namespace Echo7
{
    public struct HumanoidStyle
    {
        public Material Plates;
        public Material Suit;
        public Material Accent;
        public Material Visor;
        public bool Backpack;
        public bool FlagPatch;
        public bool Cape;
        public bool MedicCross;
        public float Scale;

        public static HumanoidStyle Soldier => new HumanoidStyle
        {
            Plates = Palette.HullLight, Suit = Palette.HullDark, Accent = Palette.Orange, Visor = Palette.Blue,
            Backpack = true, FlagPatch = true, Scale = 1f
        };
    }

    /// <summary>
    /// Процедурный гуманоид-заглушка (бронекостюм как на референсах) с кодовой анимацией:
    /// ходьба, бег, прицеливание, перекат, смерть. Позже заменяется на модель с Animator.
    /// </summary>
    public class HumanoidRig : MonoBehaviour
    {
        public float Speed;          // м/с — задаёт контроллер
        public bool Aiming;
        public bool Working;         // жест «чинит/добывает»
        public float RollT = -1f;    // 0..1 — перекат
        public bool Dead;

        Transform _hips, _torso, _head, _shL, _shR, _elL, _elR, _hipL, _hipR, _knL, _knR, _weapon;
        float _phase, _deadT;
        Vector3 _hipsBase;

        public static HumanoidRig Build(Transform parent, HumanoidStyle st)
        {
            var root = new GameObject("Body").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * (st.Scale <= 0f ? 1f : st.Scale);
            var rig = root.gameObject.AddComponent<HumanoidRig>();
            var mb = new ModelBuilder(root, true);

            rig._hips = mb.Group("hips", new Vector3(0, 0.98f, 0));
            rig._hipsBase = rig._hips.localPosition;
            mb.Box(new Vector3(0, 0, 0), new Vector3(0.42f, 0.2f, 0.26f), st.Suit, 0.05f, rig._hips);

            rig._torso = mb.Group("torso", new Vector3(0, 0.08f, 0), rig._hips);
            mb.Box(new Vector3(0, 0.33f, 0), new Vector3(0.5f, 0.56f, 0.3f), st.Suit, 0.08f, rig._torso);
            mb.Box(new Vector3(0, 0.38f, 0.07f), new Vector3(0.46f, 0.42f, 0.22f), st.Plates, 0.07f, rig._torso);
            mb.Strip(new Vector3(0, 0.27f, 0.185f), new Vector3(0.2f, 0.04f, 0.02f), st.Accent, rig._torso);
            if (st.Backpack)
            {
                mb.Box(new Vector3(0, 0.36f, -0.24f), new Vector3(0.4f, 0.46f, 0.2f), st.Plates, 0.06f, rig._torso);
                mb.Strip(new Vector3(0, 0.5f, -0.345f), new Vector3(0.22f, 0.04f, 0.02f), st.Accent, rig._torso);
            }
            if (st.Cape) mb.Box(new Vector3(0, 0.15f, -0.2f), new Vector3(0.5f, 0.85f, 0.03f), st.Accent, 0f, rig._torso, new Vector3(-8f, 0, 0));

            rig._head = mb.Group("head", new Vector3(0, 0.7f, 0), rig._torso);
            mb.Box(new Vector3(0, 0.14f, 0), new Vector3(0.28f, 0.3f, 0.3f), st.Plates, 0.09f, rig._head);
            mb.Box(new Vector3(0, 0.15f, 0.12f), new Vector3(0.22f, 0.09f, 0.08f), st.Visor, 0.02f, rig._head);
            mb.Box(new Vector3(0, 0.03f, 0.02f), new Vector3(0.2f, 0.08f, 0.24f), st.Suit, 0.02f, rig._head);

            for (int side = -1; side <= 1; side += 2)
            {
                var sh = mb.Group(side < 0 ? "shL" : "shR", new Vector3(side * 0.32f, 0.56f, 0), rig._torso);
                mb.Box(new Vector3(side * 0.05f, 0.04f, 0), new Vector3(0.24f, 0.16f, 0.28f), st.Plates, 0.05f, sh);
                if (st.FlagPatch && side < 0)
                {
                    mb.Box(new Vector3(-0.18f, -0.04f, 0.05f), new Vector3(0.01f, 0.03f, 0.1f), Palette.FlagWhite, 0f, sh);
                    mb.Box(new Vector3(-0.18f, -0.07f, 0.05f), new Vector3(0.01f, 0.03f, 0.1f), Palette.FlagBlue, 0f, sh);
                    mb.Box(new Vector3(-0.18f, -0.10f, 0.05f), new Vector3(0.01f, 0.03f, 0.1f), Palette.FlagRed, 0f, sh);
                }
                if (st.MedicCross)
                {
                    mb.Box(new Vector3(side * 0.18f, -0.06f, 0.05f), new Vector3(0.01f, 0.1f, 0.03f), Palette.Red, 0f, sh);
                    mb.Box(new Vector3(side * 0.18f, -0.06f, 0.05f), new Vector3(0.01f, 0.03f, 0.1f), Palette.Red, 0f, sh);
                }
                mb.Box(new Vector3(0, -0.18f, 0), new Vector3(0.13f, 0.34f, 0.14f), st.Suit, 0.03f, sh);
                var el = mb.Group("el", new Vector3(0, -0.36f, 0), sh);
                mb.Box(new Vector3(0, -0.15f, 0), new Vector3(0.14f, 0.3f, 0.15f), st.Plates, 0.04f, el);
                if (side < 0) { rig._shL = sh; rig._elL = el; } else { rig._shR = sh; rig._elR = el; }

                var hip = mb.Group(side < 0 ? "hipL" : "hipR", new Vector3(side * 0.13f, -0.06f, 0), rig._hips);
                mb.Box(new Vector3(0, -0.22f, 0), new Vector3(0.18f, 0.44f, 0.2f), st.Suit, 0.04f, hip);
                mb.Box(new Vector3(0, -0.2f, 0.07f), new Vector3(0.16f, 0.3f, 0.08f), st.Plates, 0.03f, hip);
                var kn = mb.Group("kn", new Vector3(0, -0.44f, 0), hip);
                mb.Box(new Vector3(0, -0.22f, 0.01f), new Vector3(0.16f, 0.44f, 0.18f), st.Plates, 0.04f, kn);
                mb.Box(new Vector3(0, -0.47f, 0.05f), new Vector3(0.17f, 0.1f, 0.28f), st.Suit, 0.03f, kn);
                if (side < 0) { rig._hipL = hip; rig._knL = kn; } else { rig._hipR = hip; rig._knR = kn; }
            }

            // винтовка в руках
            rig._weapon = mb.Group("weapon", new Vector3(0.1f, 0.38f, 0.32f), rig._torso);
            mb.Box(new Vector3(0, 0, 0.1f), new Vector3(0.08f, 0.12f, 0.75f), Palette.HullDark, 0.02f, rig._weapon);
            mb.Box(new Vector3(0, 0.07f, 0.0f), new Vector3(0.06f, 0.05f, 0.25f), Palette.HullMid, 0.01f, rig._weapon);
            mb.Strip(new Vector3(0.045f, 0.0f, 0.2f), new Vector3(0.01f, 0.03f, 0.3f), st.Visor == Palette.Blue ? Palette.Blue : st.Accent, rig._weapon);
            mb.Group("muzzle", new Vector3(0, 0, 0.5f), rig._weapon);
            return rig;
        }

        public Transform Muzzle => _weapon != null ? _weapon.Find("muzzle") : transform;

        void LateUpdate()
        {
            if (_hips == null) return;
            if (Dead)
            {
                _deadT = Mathf.Min(1f, _deadT + Time.deltaTime * 2f);
                transform.localRotation = Quaternion.Euler(-88f * Mathf.SmoothStep(0, 1, _deadT), 0, 0);
                transform.localPosition = new Vector3(0, 0.15f * _deadT, 0);
                return;
            }
            if (_deadT > 0f) { _deadT = 0f; transform.localRotation = Quaternion.identity; transform.localPosition = Vector3.zero; }

            float k = Mathf.Clamp01(Speed / 6f);
            _phase += Time.deltaTime * (2.2f + Speed * 1.4f);
            float s = Mathf.Sin(_phase);
            float swing = 38f * k;
            _hipL.localRotation = Quaternion.Euler(s * swing, 0, 0);
            _hipR.localRotation = Quaternion.Euler(-s * swing, 0, 0);
            _knL.localRotation = Quaternion.Euler(Mathf.Max(0, -s) * 55f * k + 4f, 0, 0);
            _knR.localRotation = Quaternion.Euler(Mathf.Max(0, s) * 55f * k + 4f, 0, 0);
            _hips.localPosition = _hipsBase + Vector3.up * (Mathf.Abs(Mathf.Cos(_phase)) * 0.05f * k - 0.03f * k);
            _torso.localRotation = Quaternion.Euler(6f * k, 0, 0);

            if (Aiming)
            {
                _shR.localRotation = Quaternion.Euler(-72f, -8f, 0);
                _elR.localRotation = Quaternion.Euler(-18f, 0, 0);
                _shL.localRotation = Quaternion.Euler(-70f, 28f, 0);
                _elL.localRotation = Quaternion.Euler(-35f, 0, 0);
                _weapon.localRotation = Quaternion.identity;
                _weapon.localPosition = new Vector3(0.12f, 0.45f, 0.38f);
            }
            else if (Working)
            {
                float w = Mathf.Sin(Time.time * 9f) * 20f;
                _shR.localRotation = Quaternion.Euler(-60f + w, 0, 0);
                _shL.localRotation = Quaternion.Euler(-55f - w, 0, 0);
                _elR.localRotation = Quaternion.Euler(-30f, 0, 0);
                _elL.localRotation = Quaternion.Euler(-30f, 0, 0);
                _weapon.localRotation = Quaternion.Euler(70f, 0, 0);
                _weapon.localPosition = new Vector3(0.22f, 0.3f, -0.05f);
            }
            else
            {
                _shL.localRotation = Quaternion.Euler(-s * swing * 0.7f - 10f, 0, 0);
                _elL.localRotation = Quaternion.Euler(-15f - 10f * k, 0, 0);
                _shR.localRotation = Quaternion.Euler(-35f, -10f, 0);
                _elR.localRotation = Quaternion.Euler(-40f, 0, 0);
                _weapon.localRotation = Quaternion.Euler(25f, 0, 0);
                _weapon.localPosition = new Vector3(0.12f, 0.3f, 0.28f);
            }

            if (RollT >= 0f)
            {
                float r = Mathf.Clamp01(RollT);
                transform.localRotation = Quaternion.Euler(360f * r, 0, 0);
                transform.localPosition = Vector3.up * Mathf.Sin(r * Mathf.PI) * 0.5f;
            }
            else
            {
                transform.localRotation = Quaternion.identity;
                transform.localPosition = Vector3.zero;
            }
        }
    }
}
