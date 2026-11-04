using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum SupplyPriority { Balanced, Ammo, Construction, Provisions }

    /// <summary>Заказ орбитальной доставки тяжёлого модуля (с ранга Капитан).</summary>
    public class OrbitalOrder
    {
        public string Name;
        public string Description;
        public int ReputationCost;
        public int CreditCost;
        public float EtaHours;
        public System.Action OnDeliver;
    }

    /// <summary>
    /// Цепочка снабжения. Центр присылает базовые ресурсы (сплавы, пайки, патроны) по расписанию.
    /// Объём и задержка зависят от репутации и выполнения KPI; капитан может выбирать приоритет поставок,
    /// с посадочной площадкой — заказывать тяжёлые модули орбитальной доставкой.
    /// Расходы гарнизона (пайки, вода) списываются здесь же.
    /// </summary>
    public class ResourceSupplyChain : MonoBehaviour
    {
        public SupplyPriority Priority = SupplyPriority.Balanced;
        public float NextDeliveryHour { get; private set; }   // абсолютные игровые часы
        public int SkipNext;                                    // наказание (отказ VIP) — пропуск поставки
        public readonly List<(OrbitalOrder order, float eta)> Pending = new List<(OrbitalOrder, float)>();
        float _consumeAcc;

        public void Init()
        {
            NextDeliveryHour = ScheduleNext(Game.Clock.TotalHours);
            GameEvents.HourPassed += OnHour;
        }

        void OnDestroy() { GameEvents.HourPassed -= OnHour; }

        float ScheduleNext(float now)
        {
            // каждый день в 08:00 + задержка при низкой репутации
            float dayStart = Mathf.Floor(now / 24f) * 24f;
            float t = dayStart + 8f;
            if (t <= now + 0.5f) t += 24f;
            int rep = Game.Rank.Reputation;
            if (rep < 35) t += (35 - rep) * 0.3f;
            return t;
        }

        public float HoursToDelivery => NextDeliveryHour - Game.Clock.TotalHours;

        /// <summary>Содержимое регулярной поставки.</summary>
        public Cost PlannedDelivery()
        {
            float rep = Game.Rank.Reputation;
            float k = Mathf.Lerp(0.55f, 1.35f, rep / 100f);
            k *= 1f + Mathf.Clamp(Game.Tasks.RecentKpi, -3, 3) * 0.12f;
            k *= 1f + (int)Game.Rank.Current * 0.15f;
            float alloys = 40, rations = 18, ammo = 110, med = 1;
            switch (Priority)
            {
                case SupplyPriority.Ammo: ammo *= 1.7f; alloys *= 0.6f; break;
                case SupplyPriority.Construction: alloys *= 1.7f; ammo *= 0.6f; break;
                case SupplyPriority.Provisions: rations *= 1.8f; med += 2; ammo *= 0.7f; break;
            }
            return new Cost((ResourceType.Alloys, Mathf.RoundToInt(alloys * k)), (ResourceType.Rations, Mathf.RoundToInt(rations * k)),
                (ResourceType.Ammo, Mathf.RoundToInt(ammo * k)), (ResourceType.Medkits, Mathf.RoundToInt(med)));
        }

        void OnHour(int hour)
        {
            float now = Game.Clock.TotalHours;
            if (now >= NextDeliveryHour)
            {
                if (SkipNext > 0)
                {
                    SkipNext--;
                    Game.Log?.Center("Центр: плановая поставка отменена «в связи с перераспределением ресурсов».");
                }
                else SendPod(PlannedDelivery(), "Плановая поставка из Центра", false);
                NextDeliveryHour = ScheduleNext(now);
            }
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (now < Pending[i].eta) continue;
                var o = Pending[i].order;
                Pending.RemoveAt(i);
                SendPod(new Cost(), $"Орбитальная доставка: {o.Name}", true, o.OnDeliver);
            }
        }

        void Update()
        {
            if (!Game.IsPlaying) return;
            // Потребление гарнизона: 3 пайка и 2 воды на человека в сутки
            float hours = Game.Clock.GameHoursPerSecond * Time.deltaTime;
            _consumeAcc += hours;
            if (_consumeAcc < 1f) return;
            _consumeAcc -= 1f;
            int people = (Game.Garrison != null ? Game.Garrison.PresentCount : 0) + 1;
            float heat = Game.Weather != null && Game.Weather.IsHot ? 1.6f : 1f;
            float food = Game.Bank.Consume(ResourceType.Rations, people * 3f / 24f);
            float water = Game.Bank.Consume(ResourceType.Water, people * 2f / 24f * heat);
            Game.Garrison?.ReportSupply(food, water);
        }

        public void SendBonusPod(Cost contents, string reason) => SendPod(contents, reason, false);

        /// <summary>Капсула со своим обработчиком посадки (сценарий, обучение). Возвращает точку посадки.</summary>
        public Vector3 SendCustomPod(Cost contents, string label, System.Action onLand) => SendPod(contents, label, false, onLand);

        Vector3 SendPod(Cost contents, string label, bool heavy, System.Action onLand = null)
        {
            var pad = StructureLookup.FirstOf(StructureKind.LandingPad) as LandingPad;
            Vector3 p;
            if (pad != null && pad.IsBuilt) p = pad.PadPoint + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
            else
            {
                var c = Game.Reactor != null ? Game.Reactor.transform.position : Game.World.Center;
                p = FindDropPoint(c);
            }
            DropPod.Launch(p, contents, label, heavy, onLand);
            return p;
        }

        static Vector3 FindDropPoint(Vector3 center)
        {
            for (int i = 0; i < 40; i++)
            {
                var off = Random.insideUnitCircle * 14f;
                var p = center + new Vector3(off.x, 0, off.y);
                var c = Game.Grid.WorldToCell(p);
                if (Game.Grid.InBounds(c) && Game.Grid.StructureAt(c.x, c.y) == null && !Game.Grid.IsBlocked(c.x, c.y))
                {
                    p.y = Game.Grid.SampleHeight(p);
                    return p;
                }
            }
            return Game.World.LandingZone;
        }

        // ------------------------------------------------------------ орбитальная доставка

        public List<OrbitalOrder> Catalog()
        {
            return new List<OrbitalOrder>
            {
                new OrbitalOrder { Name = "Комплект тяжёлой турели", Description = "Бесплатная постройка одной тяжёлой турели.", ReputationCost = 8, CreditCost = 60, EtaHours = 6,
                    OnDeliver = () => { Game.Entry.SetFlag("free_" + StructureKind.HeavyTurret); Game.Log?.Center("Доставлен комплект тяжёлой турели — следующая постройка бесплатна."); } },
                new OrbitalOrder { Name = "Модуль «Реактор+»", Description = "+20 к выработке центрального реактора.", ReputationCost = 12, CreditCost = 120, EtaHours = 10,
                    OnDeliver = () => { if (Game.Reactor != null) { Game.Reactor.BonusOutput += 20f; Game.Power.MarkDirty(); } Game.Log?.Center("Реактор модернизирован: +20 энергии."); } },
                new OrbitalOrder { Name = "Пополнение гарнизона", Description = "Два бойца с базовой экипировкой.", ReputationCost = 10, CreditCost = 40, EtaHours = 8,
                    OnDeliver = () => { Game.Garrison.Recruit(2); } },
                new OrbitalOrder { Name = "Медицинский контейнер", Description = "8 аптечек.", ReputationCost = 4, CreditCost = 30, EtaHours = 4,
                    OnDeliver = () => Game.Bank.Add(ResourceType.Medkits, 8, true) },
                new OrbitalOrder { Name = "Кристаллы с орбиты", Description = "25 кристаллов (дорого, но без шума).", ReputationCost = 6, CreditCost = 90, EtaHours = 6,
                    OnDeliver = () => Game.Bank.Add(ResourceType.Crystals, 25, true) },
            };
        }

        public string CanOrder(OrbitalOrder o)
        {
            if (!Game.Rank.IsUnlocked(Feature.OrbitalDelivery)) return Game.Rank.LockReason(Feature.OrbitalDelivery);
            var pad = StructureLookup.FirstOf(StructureKind.LandingPad);
            if (pad == null || !pad.IsBuilt) return "Нужна посадочная площадка";
            if (Game.Rank.Reputation < o.ReputationCost + 10) return "Недостаточно репутации";
            if (!Game.Bank.Has(ResourceType.Credits, o.CreditCost)) return $"Нужно {o.CreditCost} кредитов";
            return null;
        }

        public bool Order(OrbitalOrder o)
        {
            if (CanOrder(o) != null) return false;
            Game.Bank.Spend(ResourceType.Credits, o.CreditCost);
            Game.Rank.AddReputation(-o.ReputationCost, $"запрос «{o.Name}»");
            Pending.Add((o, Game.Clock.TotalHours + o.EtaHours));
            Game.Log?.Center($"Заказ принят: {o.Name}. Доставка через ~{o.EtaHours:0} ч.");
            return true;
        }

        public void Restore(float nextHour, SupplyPriority prio, int skip)
        {
            NextDeliveryHour = nextHour; Priority = prio; SkipNext = skip;
        }
    }

    /// <summary>Капсула снабжения: падает с неба с огненным следом, при посадке выгружает груз.</summary>
    public class DropPod : MonoBehaviour
    {
        Vector3 _target;
        float _t;
        Cost _contents;
        string _label;
        System.Action _onLand;
        ParticleSystem _trail;
        bool _landed;
        float _life;

        public static void Launch(Vector3 target, Cost contents, string label, bool heavy, System.Action onLand)
        {
            var go = new GameObject("DropPod");
            go.transform.SetParent(Game.Root, false);
            var pod = go.AddComponent<DropPod>();
            pod._target = target;
            pod._contents = contents;
            pod._label = label;
            pod._onLand = onLand;
            go.transform.position = target + new Vector3(30f, 120f, 20f);
            var mb = new ModelBuilder(go.transform, "m");
            float s = heavy ? 1.4f : 1f;
            mb.Root.localScale = Vector3.one * s;
            mb.Frustum(new Vector3(0, 1.2f, 0), 1.1f, 0.6f, 2.4f, Palette.HullLight, null, null, 12);
            mb.Frustum(new Vector3(0, 2.7f, 0), 0.6f, 0.15f, 0.7f, Palette.HullDark, null, null, 12);
            mb.Cyl(new Vector3(0, 0.15f, 0), 1.15f, 0.3f, Palette.HullDark, null, null, 12);
            mb.Strip(new Vector3(0, 1.5f, 0.95f), new Vector3(0.5f, 0.12f, 0.05f), Palette.Orange);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f;
                var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                mb.Box(d * 1.0f + Vector3.up * 0.6f, new Vector3(0.1f, 1.2f, 0.6f), Palette.HullMid, 0.02f, null, new Vector3(0, a, 0));
            }
            pod._trail = Game.Fx.CreateLooping("trail", go.transform, new Color(1f, 0.55f, 0.2f, 0.8f), 80f, 0.6f, 1f, 1.2f, ParticleSystemShapeType.Sphere, Vector3.one * 0.5f);
            Game.Sound.PlayAt(Sfx.Pod, target, 1f);
            Game.Log?.Info($"{label}: капсула входит в атмосферу.");
        }

        void Update()
        {
            if (_landed)
            {
                _life += Time.deltaTime;
                if (_life > 40f) Destroy(gameObject);
                return;
            }
            _t += Time.deltaTime / 2.2f;
            var start = _target + new Vector3(30f, 120f, 20f);
            transform.position = Vector3.Lerp(start, _target, _t * _t);
            if (_t >= 1f) Land();
        }

        void Land()
        {
            _landed = true;
            transform.position = _target;
            var em = _trail.emission;
            em.rateOverTime = 0f;
            Game.Fx.Emit(FxKind.Dust, _target + Vector3.up * 0.3f, 30, Vector3.up, 1f, null, 1.6f);
            Game.Fx.Flash(_target + Vector3.up * 2f, new Color(1f, 0.7f, 0.4f), 14f, 4f, 0.4f);
            Game.Noise?.Emit(_target, 10f, NoiseSource.Event);
            if (Game.CameraRig != null && Game.Player != null && Vector3.Distance(_target, Game.Player.transform.position) < 40f) Game.CameraRig.Shake(0.3f, 0.4f);
            if (_contents != null && !_contents.IsEmpty)
            {
                Game.Bank.Add(_contents);
                Game.Log?.Center($"{_label}: {_contents}");
            }
            _onLand?.Invoke();
        }
    }
}
