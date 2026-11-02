using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    public enum Rank { Specialist = 0, Lieutenant = 1, Captain = 2, Major = 3, Colonel = 4 }

    /// <summary>
    /// Возможности, которые открываются рангами. UI-окна и пункты меню строительства спрашивают
    /// PlayerRankManager.IsUnlocked(feature) — единая точка «запирания» интерфейса.
    /// </summary>
    public enum Feature
    {
        BasicConstruction,   // базовые постройки, малый отряд
        HeavyTurrets,        // Лейтенант
        LaserTurrets,        // Лейтенант
        GeoSurvey,           // Лейтенант: геологоразведка
        TitaniumWalls,       // Лейтенант
        CenterVoice,         // Капитан: право голоса при запросах Центра
        SecondOutpost,       // Капитан: аванпост-рудник
        OrbitalDelivery,     // Капитан: орбитальная доставка тяжёлых модулей
        Spaceport,           // Майор: космопорт
        GlobalMap,           // Майор: глобальная карта и перелёты
        GarrisonCommand,     // Майор: командование гарнизоном (пополнение)
    }

    /// <summary>
    /// Ранги вместо опыта. Очки ранга (ОР) дают задачи Центра, отражённые волны, удачные визиты чинов.
    /// Трибунал понижает ранг.
    /// </summary>
    public class PlayerRankManager
    {
        public Rank Current { get; private set; } = Rank.Specialist;
        public int Points { get; private set; }
        public int Reputation { get; private set; } = 50; // 0..100, влияет на поставки
        public string PlayerName = "Ковальски";

        static readonly int[] Thresholds = { 0, 120, 350, 800, 1600 };

        static readonly Dictionary<Feature, Rank> FeatureRanks = new Dictionary<Feature, Rank>
        {
            { Feature.BasicConstruction, Rank.Specialist },
            { Feature.HeavyTurrets, Rank.Lieutenant },
            { Feature.LaserTurrets, Rank.Lieutenant },
            { Feature.GeoSurvey, Rank.Lieutenant },
            { Feature.TitaniumWalls, Rank.Lieutenant },
            { Feature.CenterVoice, Rank.Captain },
            { Feature.SecondOutpost, Rank.Captain },
            { Feature.OrbitalDelivery, Rank.Captain },
            { Feature.Spaceport, Rank.Major },
            { Feature.GlobalMap, Rank.Major },
            { Feature.GarrisonCommand, Rank.Major },
        };

        public static string RankName(Rank r)
        {
            switch (r)
            {
                case Rank.Specialist: return "Специалист";
                case Rank.Lieutenant: return "Лейтенант";
                case Rank.Captain: return "Капитан";
                case Rank.Major: return "Майор";
                case Rank.Colonel: return "Полковник";
            }
            return r.ToString();
        }

        public static string FeatureName(Feature f)
        {
            switch (f)
            {
                case Feature.HeavyTurrets: return "Тяжёлые турели";
                case Feature.LaserTurrets: return "Лазерные излучатели";
                case Feature.GeoSurvey: return "Геологоразведка";
                case Feature.TitaniumWalls: return "Титановые стены";
                case Feature.CenterVoice: return "Право голоса в Центре";
                case Feature.SecondOutpost: return "Второй аванпост (рудник)";
                case Feature.OrbitalDelivery: return "Орбитальная доставка";
                case Feature.Spaceport: return "Космопорт";
                case Feature.GlobalMap: return "Глобальная карта и перелёты";
                case Feature.GarrisonCommand: return "Командование гарнизоном";
            }
            return "Базовое строительство";
        }

        public static Rank RequiredRank(Feature f) => FeatureRanks.TryGetValue(f, out var r) ? r : Rank.Specialist;
        public bool IsUnlocked(Feature f) => Current >= RequiredRank(f);
        public bool IsAtLeast(Rank r) => Current >= r;

        public string LockReason(Feature f) => IsUnlocked(f) ? null : $"Требуется ранг: {RankName(RequiredRank(f))}";

        public int PointsForNext => (int)Current + 1 < Thresholds.Length ? Thresholds[(int)Current + 1] : -1;
        public int PointsForCurrent => Thresholds[(int)Current];
        public float ProgressToNext
        {
            get
            {
                int next = PointsForNext;
                if (next < 0) return 1f;
                return Mathf.Clamp01((float)(Points - PointsForCurrent) / (next - PointsForCurrent));
            }
        }

        /// <summary>Радиус застройки вокруг реактора растёт с рангом.</summary>
        public float BuildRadius => 34f + (int)Current * 7f;
        /// <summary>Сколько строительных дронов выделено аванпосту.</summary>
        public int BuilderDrones => 2 + Mathf.Min(2, (int)Current / 2);

        public void AddPoints(int amount, string reason)
        {
            if (amount == 0) return;
            Points = Mathf.Max(0, Points + amount);
            if (amount > 0) Game.Log?.Info($"+{amount} ОР — {reason}");
            while (PointsForNext > 0 && Points >= PointsForNext) Promote();
        }

        void Promote()
        {
            var from = Current;
            Current = (Rank)((int)Current + 1);
            Game.Skills?.AddPoints(3);
            Game.Log?.Center($"Приказ Центра: присвоено звание «{RankName(Current)}». Получено 3 очка навыков.");
            foreach (var kv in FeatureRanks)
                if (kv.Value == Current) Game.Log?.Info($"Открыто: {FeatureName(kv.Key)}");
            Game.Sound?.Play2D(Sfx.RankUp, 0.9f);
            GameEvents.RankUpdate(from, Current);
        }

        /// <summary>Военный трибунал: понижение на ранг, очки сбрасываются к порогу нового ранга.</summary>
        public void Demote(string reason)
        {
            if (Current == Rank.Specialist)
            {
                Points = 0;
                AddReputation(-10, reason);
                Game.Log?.Alert($"Трибунал: {reason}. Понижать уже некуда — объявлен выговор.");
                return;
            }
            var from = Current;
            Current = (Rank)((int)Current - 1);
            Points = Thresholds[(int)Current];
            Game.Log?.Alert($"ВОЕННЫЙ ТРИБУНАЛ: {reason}. Вы понижены до звания «{RankName(Current)}».");
            Game.Sound?.Play2D(Sfx.UiError, 1f);
            GameEvents.RankUpdate(from, Current);
        }

        public void AddReputation(int delta, string reason)
        {
            int before = Reputation;
            Reputation = Mathf.Clamp(Reputation + delta, 0, 100);
            if (Reputation != before)
                Game.Log?.Info($"Репутация в Центре {(delta > 0 ? "+" : "")}{delta}: {reason}");
        }

        /// <summary>Для загрузки сохранения.</summary>
        public void Restore(Rank rank, int points, int reputation)
        {
            Current = rank; Points = points; Reputation = Mathf.Clamp(reputation, 0, 100);
        }

        public string Insignia
        {
            get
            {
                switch (Current)
                {
                    case Rank.Specialist: return "▪";
                    case Rank.Lieutenant: return "▪▪";
                    case Rank.Captain: return "▪▪▪";
                    case Rank.Major: return "★";
                    default: return "★★";
                }
            }
        }
    }
}
