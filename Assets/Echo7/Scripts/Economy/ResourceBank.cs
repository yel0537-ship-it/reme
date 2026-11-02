using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum ResourceType
    {
        Alloys,     // стандартные сплавы (Центр)
        Rations,    // пайки (Центр, охота)
        Ammo,       // патроны (Центр)
        Water,      // вода (конденсаторы, озёра) — испаряется в пекло
        Titanium,   // титан — только местный
        Crystals,   // кристаллы — только местный, для турелей
        Silicon,    // кремний — дилемма: жильё или конденсаторы
        Medkits,    // аптечки
        Credits     // кредиты станции
    }

    /// <summary>Набор ресурсов (стоимость постройки, содержимое поставки и т.п.).</summary>
    [Serializable]
    public class Cost
    {
        public List<ResourceType> Types = new List<ResourceType>();
        public List<int> Amounts = new List<int>();

        public Cost() { }

        public Cost(params (ResourceType t, int a)[] items)
        {
            foreach (var it in items) Add(it.t, it.a);
        }

        public Cost Add(ResourceType t, int a)
        {
            int i = Types.IndexOf(t);
            if (i >= 0) Amounts[i] += a;
            else { Types.Add(t); Amounts.Add(a); }
            return this;
        }

        public int Get(ResourceType t) { int i = Types.IndexOf(t); return i >= 0 ? Amounts[i] : 0; }
        public bool IsEmpty { get { foreach (var a in Amounts) if (a > 0) return false; return true; } }

        public Cost Scaled(float k)
        {
            var c = new Cost();
            for (int i = 0; i < Types.Count; i++) c.Add(Types[i], Mathf.Max(0, Mathf.RoundToInt(Amounts[i] * k)));
            return c;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            for (int i = 0; i < Types.Count; i++) if (Amounts[i] > 0) parts.Add($"{ResourceBank.Short(Types[i])} {Amounts[i]}");
            return parts.Count > 0 ? string.Join("  ", parts) : "бесплатно";
        }
    }

    /// <summary>Склад аванпоста. Вместимость растёт со складами.</summary>
    public class ResourceBank
    {
        readonly float[] _amount = new float[Enum.GetValues(typeof(ResourceType)).Length];
        public int BaseCapacity = 400;
        public int ExtraCapacity;
        public int Capacity => BaseCapacity + ExtraCapacity;

        public static readonly ResourceType[] All = (ResourceType[])Enum.GetValues(typeof(ResourceType));

        public int this[ResourceType t] => Mathf.FloorToInt(_amount[(int)t]);
        public float Exact(ResourceType t) => _amount[(int)t];

        public static string Name(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Alloys: return "Сплавы";
                case ResourceType.Rations: return "Пайки";
                case ResourceType.Ammo: return "Патроны";
                case ResourceType.Water: return "Вода";
                case ResourceType.Titanium: return "Титан";
                case ResourceType.Crystals: return "Кристаллы";
                case ResourceType.Silicon: return "Кремний";
                case ResourceType.Medkits: return "Аптечки";
                case ResourceType.Credits: return "Кредиты";
            }
            return t.ToString();
        }

        public static string Short(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Alloys: return "Спл";
                case ResourceType.Rations: return "Пай";
                case ResourceType.Ammo: return "Пат";
                case ResourceType.Water: return "Вод";
                case ResourceType.Titanium: return "Тит";
                case ResourceType.Crystals: return "Крс";
                case ResourceType.Silicon: return "Крм";
                case ResourceType.Medkits: return "Апт";
                case ResourceType.Credits: return "Кр";
            }
            return t.ToString();
        }

        public static Color ColorOf(ResourceType t)
        {
            switch (t)
            {
                case ResourceType.Alloys: return new Color(0.78f, 0.78f, 0.8f);
                case ResourceType.Rations: return new Color(0.9f, 0.75f, 0.45f);
                case ResourceType.Ammo: return new Color(1f, 0.6f, 0.25f);
                case ResourceType.Water: return new Color(0.4f, 0.75f, 1f);
                case ResourceType.Titanium: return new Color(0.6f, 0.7f, 0.85f);
                case ResourceType.Crystals: return new Color(0.75f, 0.45f, 1f);
                case ResourceType.Silicon: return new Color(0.9f, 0.88f, 0.7f);
                case ResourceType.Medkits: return new Color(1f, 0.4f, 0.4f);
                case ResourceType.Credits: return new Color(1f, 0.85f, 0.3f);
            }
            return Color.white;
        }

        /// <summary>Лимит хранения (кредиты и аптечки не ограничены складом).</summary>
        public int CapOf(ResourceType t) => t == ResourceType.Credits ? 999999 : t == ResourceType.Medkits ? 99 : Capacity;

        public bool Has(ResourceType t, int a) => _amount[(int)t] + 0.0001f >= a;

        public bool CanAfford(Cost c)
        {
            if (c == null) return true;
            for (int i = 0; i < c.Types.Count; i++) if (!Has(c.Types[i], c.Amounts[i])) return false;
            return true;
        }

        public string MissingText(Cost c)
        {
            var parts = new List<string>();
            for (int i = 0; i < c.Types.Count; i++)
            {
                int lack = c.Amounts[i] - this[c.Types[i]];
                if (lack > 0) parts.Add($"{Name(c.Types[i])}: не хватает {lack}");
            }
            return string.Join(", ", parts);
        }

        public bool Spend(Cost c)
        {
            if (!CanAfford(c)) return false;
            for (int i = 0; i < c.Types.Count; i++) _amount[(int)c.Types[i]] -= c.Amounts[i];
            return true;
        }

        public bool Spend(ResourceType t, float a)
        {
            if (_amount[(int)t] + 0.0001f < a) return false;
            _amount[(int)t] -= a;
            return true;
        }

        /// <summary>Списывает сколько есть; возвращает долю удовлетворения 0..1.</summary>
        public float Consume(ResourceType t, float a)
        {
            if (a <= 0f) return 1f;
            float have = _amount[(int)t];
            if (have >= a) { _amount[(int)t] -= a; return 1f; }
            _amount[(int)t] = 0f;
            return have / a;
        }

        public int Add(ResourceType t, float a, bool announce = false)
        {
            if (a <= 0f) return 0;
            float before = _amount[(int)t];
            _amount[(int)t] = Mathf.Min(CapOf(t), before + a);
            int gained = Mathf.FloorToInt(_amount[(int)t]) - Mathf.FloorToInt(before);
            if (gained > 0) GameEvents.Gained(t, gained);
            if (announce && gained > 0) Game.Log?.Info($"+{gained} {Name(t)}");
            return gained;
        }

        public void Add(Cost c)
        {
            if (c == null) return;
            for (int i = 0; i < c.Types.Count; i++) Add(c.Types[i], c.Amounts[i]);
        }

        public void Set(ResourceType t, float a) => _amount[(int)t] = Mathf.Max(0f, a);

        public void Evaporate(ResourceType t, float fraction)
        {
            _amount[(int)t] *= Mathf.Clamp01(1f - fraction);
        }
    }
}
