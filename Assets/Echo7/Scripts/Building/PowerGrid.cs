using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Энергосеть: реактор и энергоузлы образуют граф, постройки в их радиусе подключены кабелями.
    /// Баланс «выработка/потребление» определяет эффективность всех потребителей.
    /// Мелкие твари перегрызают кабели (Structure.CutCable) и узлы.
    /// </summary>
    public class PowerGrid : MonoBehaviour
    {
        public float Production { get; private set; }
        public float Demand { get; private set; }
        public float Satisfaction { get; private set; } = 1f;
        public float Balance => Production - Demand;

        bool _dirty = true;
        float _balanceTimer;
        readonly List<Structure> _nodes = new List<Structure>();
        readonly List<Structure> _connected = new List<Structure>();
        readonly List<LineRenderer> _cables = new List<LineRenderer>();
        int _cableUsed;
        Material _cableMat, _cutMat;

        public void Init()
        {
            _cableMat = MaterialFactory.Unlit(new Color(0.08f, 0.08f, 0.1f, 1f));
            _cutMat = MaterialFactory.Unlit(new Color(1f, 0.3f, 0.1f, 1f));
            GameEvents.StructureCompleted += _ => MarkDirty();
            GameEvents.StructureDestroyed += _ => MarkDirty();
        }

        public void MarkDirty() => _dirty = true;

        public bool IsNode(Structure s) => s.Def.PowerRadius > 0f && s.IsBuilt && !s.IsDestroyed && !(s.CableCut && s.Def.Kind == StructureKind.Pylon);

        void Update()
        {
            if (!Game.IsPlaying) return;
            if (_dirty) { _dirty = false; Rebuild(); }
            _balanceTimer -= Time.deltaTime;
            if (_balanceTimer <= 0f) { _balanceTimer = 0.25f; RecalcBalance(); }
        }

        void Rebuild()
        {
            _nodes.Clear();
            _connected.Clear();
            _cableUsed = 0;
            var reactor = Game.Reactor;
            if (reactor == null || reactor.IsDestroyed)
            {
                foreach (var s in Registry.Structures) if (s != null) s.Powered = false;
                HideUnusedCables();
                return;
            }
            // BFS по узлам
            var all = new List<Structure>();
            foreach (var s in Registry.Structures) if (s != null && IsNode(s)) all.Add(s);
            var queue = new Queue<Structure>();
            var visited = new HashSet<Structure> { reactor };
            queue.Enqueue(reactor);
            while (queue.Count > 0)
            {
                var a = queue.Dequeue();
                _nodes.Add(a);
                foreach (var b in all)
                {
                    if (visited.Contains(b)) continue;
                    float r = Mathf.Max(a.Def.PowerRadius, b.Def.PowerRadius);
                    if (Flat(a.Center, b.Center) <= r)
                    {
                        visited.Add(b);
                        queue.Enqueue(b);
                        Cable(a.Center + Vector3.up * NodeHeight(a), b.Center + Vector3.up * NodeHeight(b), false);
                    }
                }
            }
            // подключение построек к ближайшему узлу
            foreach (var s in Registry.Structures)
            {
                if (s == null || s.IsDestroyed) continue;
                if (_nodes.Contains(s)) { s.Powered = true; _connected.Add(s); continue; }
                bool consumer = s.Def.NeedsPower || s.Def.EnergyProduce > 0f;
                if (!consumer) { s.Powered = false; continue; }
                Structure best = null; float bestD = float.MaxValue;
                foreach (var n in _nodes)
                {
                    float d = s.DistanceTo(n.Center);
                    if (d <= n.Def.PowerRadius && d < bestD) { bestD = d; best = n; }
                }
                s.Powered = best != null && s.IsBuilt;
                if (best != null)
                {
                    if (s.Powered && !s.CableCut) _connected.Add(s);
                    Cable(best.Center + Vector3.up * NodeHeight(best), s.Center + Vector3.up * 0.4f, s.CableCut);
                }
            }
            HideUnusedCables();
            RecalcBalance();
            GameEvents.Power();
        }

        static float NodeHeight(Structure s) => s.Def.Kind == StructureKind.Reactor ? 2.6f : s.Def.Kind == StructureKind.Pylon ? 3.3f : 1f;
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        void RecalcBalance()
        {
            float prod = 0f, dem = 0f;
            float eff = Game.Skills != null ? Game.Skills.EnergyUseMult : 1f;
            foreach (var s in _connected)
            {
                if (s == null || s.IsDestroyed || !s.IsBuilt) continue;
                prod += s.CurrentEnergyProduce;
                if (s.Def.NeedsPower) dem += s.CurrentEnergyUse * eff;
            }
            Production = prod;
            Demand = dem;
            Satisfaction = dem <= 0.01f ? 1f : Mathf.Clamp01(prod / dem);
            foreach (var s in Registry.Structures)
                if (s != null) s.Efficiency = s.Powered && !s.CableCut ? Satisfaction : 0f;
        }

        void Cable(Vector3 a, Vector3 b, bool cut)
        {
            LineRenderer lr;
            if (_cableUsed < _cables.Count) lr = _cables[_cableUsed];
            else
            {
                var go = new GameObject("cable");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.05f;
                lr.positionCount = 9;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                _cables.Add(lr);
            }
            _cableUsed++;
            lr.enabled = true;
            lr.sharedMaterial = cut ? _cutMat : _cableMat;
            float sag = Mathf.Min(1.2f, Vector3.Distance(a, b) * 0.06f);
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                var p = Vector3.Lerp(a, b, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * sag;
                if (cut && t > 0.42f && t < 0.58f) p.y -= 0.8f; // обрыв
                lr.SetPosition(i, p);
            }
        }

        void HideUnusedCables()
        {
            for (int i = _cableUsed; i < _cables.Count; i++) _cables[i].enabled = false;
        }

        /// <summary>Есть ли покрытие сети в точке (для подсказки при строительстве).</summary>
        public bool Covers(Vector3 p)
        {
            foreach (var n in _nodes)
                if (n != null && Flat(n.Center, p) <= n.Def.PowerRadius) return true;
            return false;
        }

        public IReadOnlyList<Structure> Nodes => _nodes;
    }
}
