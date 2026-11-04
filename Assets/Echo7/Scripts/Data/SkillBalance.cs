using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Баланс дерева навыков. Ключ — <see cref="id"/>. Что именно делает навык (какую систему усиливает), задано в коде
    /// (<see cref="SkillTree"/>, блок модификаторов); здесь — сила, цена по уровням, условия и тексты.
    /// </summary>
    [Serializable]
    public class SkillEntry
    {
        [Tooltip("Название в окне навыков.")]
        public string name;
        [Tooltip("Навык — ключ записи. Действие навыка закреплено за ключом в коде.")]
        public SkillId id;
        public SkillBranch branch;
        [Tooltip("Короткое описание под названием.")]
        public string description;

        [Header("Сила")]
        [Tooltip("Эффект за уровень: доля для процентных навыков (0.25 = +25%), число для абсолютных («Выносливость»: +25 здоровья).")]
        public float perLevel = 0.1f;
        [Tooltip("Множитель для текста: 100 — показывать проценты, 1 — как есть.")]
        public float displayScale = 100f;
        [Tooltip("Текст эффекта, {0} — суммарное значение. Например: +{0}% к скорости стройки")]
        public string effectFormat = "+{0}%";

        [Header("Условия")]
        [Min(1)] public int maxLevel = 3;
        public Rank minRank = Rank.Specialist;
        [Tooltip("Требовать изученный навык-предшественник.")]
        public bool requiresSkill;
        public SkillId requires;

        public static SkillEntry From(SkillDef d) => new SkillEntry
        {
            name = d.Name, id = d.Id, branch = d.Branch, description = d.Description,
            perLevel = d.PerLevel, displayScale = d.DisplayScale, effectFormat = d.EffectFormat,
            maxLevel = d.MaxLevel, minRank = d.MinRank, requiresSkill = d.Requires.HasValue, requires = d.Requires ?? default,
        };

        public void ApplyTo(SkillDef d)
        {
            if (!string.IsNullOrEmpty(name)) d.Name = name;
            d.Branch = branch;
            d.Description = description ?? "";
            d.PerLevel = perLevel;
            d.DisplayScale = displayScale;
            d.EffectFormat = string.IsNullOrEmpty(effectFormat) ? "{0}" : effectFormat;
            d.MaxLevel = Mathf.Max(1, maxLevel);
            d.MinRank = minRank;
            d.Requires = requiresSkill && requires != id ? requires : (SkillId?)null;
        }
    }

    [CreateAssetMenu(fileName = "Skills", menuName = "Эхо-7/Баланс/Навыки")]
    public class SkillBalance : BalanceAsset
    {
        public const string File = "Skills";
        public override string FileName => File;

        [Tooltip("Очки навыков на старте партии.")]
        [Min(0)] public int startingPoints = 1;
        public List<SkillEntry> skills = new List<SkillEntry>();

        public override void ResetToCode()
        {
            startingPoints = 1;
            skills.Clear();
            foreach (var d in SkillTree.CodeDefaults()) skills.Add(SkillEntry.From(d));
        }

        public override int AddMissingFromCode()
        {
            int added = 0;
            foreach (var d in SkillTree.CodeDefaults())
                if (!skills.Exists(e => e.id == d.Id)) { skills.Add(SkillEntry.From(d)); added++; }
            return added;
        }

        public override void Validate(List<string> problems)
        {
            BalanceData.CheckDuplicates(skills.ConvertAll(e => e.id), "Навык", problems);
            foreach (var e in skills)
            {
                string who = $"«{e.name}» ({e.id})";
                if (e.requiresSkill && e.requires == e.id) problems.Add($"{who}: навык требует сам себя — условие проигнорировано.");
                if (e.maxLevel < 1) problems.Add($"{who}: максимальный уровень должен быть ≥ 1.");
                try { string.Format(e.effectFormat ?? "", 0); }
                catch (FormatException) { problems.Add($"{who}: ошибка в шаблоне эффекта «{e.effectFormat}» (фигурные скобки)."); }
                if ((e.id == SkillId.PowerEfficiency || e.id == SkillId.Stealth) && e.perLevel * e.maxLevel >= 0.95f)
                    problems.Add($"{who}: на максимуме почти 100% экономии — игра ограничит эффект 95%.");
                if (e.id == SkillId.Fortification && e.perLevel * e.maxLevel > 0.9f)
                    problems.Add($"{who}: снижение урона выше 90% — игра ограничит эффект.");
            }
            // циклы в требованиях
            foreach (var e in skills)
            {
                var seen = new HashSet<SkillId> { e.id };
                var cur = e;
                while (cur != null && cur.requiresSkill && cur.requires != cur.id)
                {
                    if (!seen.Add(cur.requires)) { problems.Add($"«{e.name}»: цикл в требованиях навыков — навык нельзя будет изучить."); break; }
                    var next = cur.requires;
                    cur = skills.Find(s => s.id == next);
                }
            }
        }

        public override void ApplyLive() => SkillTree.Apply(this);
    }
}
