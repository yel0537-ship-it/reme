using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Баланс стрельбы турелей. Ключ — тип постройки. Режим огня (луч, баллистика) задан в коде:
    /// от него зависит, как стреляет турель, а не насколько сильно.
    /// Цена, прочность и энергопотребление турели — в ассете построек.
    /// </summary>
    [Serializable]
    public class TurretEntry
    {
        [Tooltip("Подпись записи (для удобства).")]
        public string name;
        [Tooltip("Тип постройки-турели — ключ записи.")]
        public StructureKind kind;

        [Header("Огонь")]
        [Min(1f)] public float range = 22f;
        [Tooltip("Секунды между выстрелами (у лучевой — между «тиками» урона).")]
        [Min(0.02f)] public float fireInterval = 0.12f;
        [Tooltip("Урон за выстрел / тик до брони цели.")]
        [Min(0f)] public float damage = 7f;
        public DamageType damageType = DamageType.Kinetic;
        [Tooltip("Базовая точность 0..1. Умножается на мораль расчёта.")]
        [Range(0f, 1f)] public float accuracy = 0.75f;
        [Tooltip("Радиус взрыва (0 — без разлёта).")]
        [Min(0f)] public float splash;
        [Tooltip("Градусов в секунду.")]
        [Min(1f)] public float turnSpeed = 240f;
        [Tooltip("Лазеры и тяжёлые орудия предпочитают бронированные цели.")]
        public bool preferHeavy;

        [Header("Расход и износ")]
        [Min(0f)] public float ammoPerShot = 0.5f;
        [Tooltip("Доп. потребление энергии, пока турель стреляет (лазер).")]
        [Min(0f)] public float energyFiring;
        [Tooltip("Шум за выстрел (копит угрозу волны).")]
        [Min(0f)] public float noise = 0.35f;
        [Tooltip("Износ за выстрел (1 = полностью изношена). В пекло — быстрее.")]
        [Min(0f)] public float wearPerShot = 0.0012f;

        [Header("Подача")]
        public Sfx sound = Sfx.MachineGun;
        public Color tracer = new Color(1f, 0.75f, 0.35f, 1f);

        public static TurretEntry From(StructureKind kind, TurretProfile p) => new TurretEntry
        {
            name = BuildingCatalog.Get(kind).Name, kind = kind,
            range = p.Range, fireInterval = p.FireInterval, damage = p.Damage, damageType = p.Type, accuracy = p.Accuracy,
            splash = p.Splash, turnSpeed = p.TurnSpeed, preferHeavy = p.PreferHeavy,
            ammoPerShot = p.AmmoPerShot, energyFiring = p.EnergyFiring, noise = p.Noise, wearPerShot = p.WearPerShot,
            sound = p.Sound, tracer = p.Tracer,
        };

        public void ApplyTo(TurretProfile p)
        {
            p.Range = Mathf.Max(1f, range);
            p.FireInterval = Mathf.Max(0.02f, fireInterval);
            p.Damage = Mathf.Max(0f, damage);
            p.Type = damageType;
            p.Accuracy = Mathf.Clamp01(accuracy);
            p.Splash = Mathf.Max(0f, splash);
            p.TurnSpeed = Mathf.Max(1f, turnSpeed);
            p.PreferHeavy = preferHeavy;
            p.AmmoPerShot = Mathf.Max(0f, ammoPerShot);
            p.EnergyFiring = Mathf.Max(0f, energyFiring);
            p.Noise = Mathf.Max(0f, noise);
            p.WearPerShot = Mathf.Max(0f, wearPerShot);
            p.Sound = sound;
            p.Tracer = tracer;
        }
    }

    [CreateAssetMenu(fileName = "Turrets", menuName = "Эхо-7/Баланс/Турели")]
    public class TurretBalance : BalanceAsset
    {
        public const string File = "Turrets";
        public override string FileName => File;

        [Header("Второй ярус")]
        [Tooltip("Прибавка к дальности турели, стоящей на стене или настиле (0.15 = +15%).")]
        [Range(0f, 1f)] public float elevatedRangeBonus = 0.15f;

        public List<TurretEntry> turrets = new List<TurretEntry>();

        public override void ResetToCode()
        {
            elevatedRangeBonus = 0.15f;
            turrets.Clear();
            foreach (var k in TurretProfile.TurretKinds) turrets.Add(TurretEntry.From(k, TurretProfile.CodeDefault(k)));
        }

        public override int AddMissingFromCode()
        {
            int added = 0;
            foreach (var k in TurretProfile.TurretKinds)
                if (!turrets.Exists(e => e.kind == k)) { turrets.Add(TurretEntry.From(k, TurretProfile.CodeDefault(k))); added++; }
            return added;
        }

        public override void Validate(List<string> problems)
        {
            BalanceData.CheckDuplicates(turrets.ConvertAll(e => e.kind), "Турель", problems);
            if (elevatedRangeBonus > 0.5f) problems.Add($"Бонус дальности на втором ярусе +{elevatedRangeBonus * 100f:0}% — турели на стенах станут сильнее всех остальных.");
            foreach (var e in turrets)
            {
                string who = $"«{e.name}» ({e.kind})";
                if (Array.IndexOf(TurretProfile.TurretKinds, e.kind) < 0) problems.Add($"{who}: эта постройка не турель — запись не используется.");
                if (e.fireInterval <= 0f) problems.Add($"{who}: интервал выстрелов должен быть > 0.");
                if (e.range <= 0f) problems.Add($"{who}: дальность должна быть > 0.");
                if (e.accuracy <= 0.05f) problems.Add($"{who}: точность почти нулевая — турель будет мазать.");
                if (e.damage <= 0f) problems.Add($"{who}: урон 0 — турель бесполезна.");
            }
        }

        public override void ApplyLive() => TurretProfile.Apply(this);
    }
}
