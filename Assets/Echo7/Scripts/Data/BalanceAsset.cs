using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echo7
{
    /// <summary>
    /// Базовый класс ассетов баланса. Ассеты лежат в Assets/Echo7/Resources/Echo7Balance и читаются через Resources.
    /// Если ассета нет, игра работает на значениях из кода. Значит, проект открывается и без них,
    /// а меню «Эхо-7 → Баланс» генерирует ассеты из тех же значений.
    /// </summary>
    public abstract class BalanceAsset : ScriptableObject
    {
        [Tooltip("Заметки геймдизайнера: что и зачем меняли. На игру не влияет.")]
        [TextArea(2, 6)] public string notes;

        /// <summary>Имя файла ассета без расширения (в папке Resources/Echo7Balance).</summary>
        public abstract string FileName { get; }

        /// <summary>Заполнить значениями из кода (полный сброс).</summary>
        public abstract void ResetToCode();

        /// <summary>Добавить записи, которые есть в коде, но отсутствуют в ассете (новые постройки, враги…). Возвращает число добавленных.</summary>
        public abstract int AddMissingFromCode();

        /// <summary>Проверка данных: ошибки и подозрительные значения.</summary>
        public abstract void Validate(List<string> problems);

        /// <summary>Применить изменения к уже запущенной игре (правка в инспекторе во время Play).</summary>
        public abstract void ApplyLive();

        protected virtual void OnValidate()
        {
            if (!Application.isPlaying) return;
            try { ApplyLive(); }
            catch (Exception e) { Debug.LogWarning($"Эхо-7: не удалось применить баланс «{name}» на лету: {e.Message}"); }
        }

        // ---- контекстное меню ассета (шестерёнка в инспекторе)

        [ContextMenu("Проверить")]
        void ContextValidate()
        {
            var problems = new List<string>();
            Validate(problems);
            if (problems.Count == 0) Debug.Log($"Эхо-7 · баланс «{name}»: замечаний нет.", this);
            foreach (var p in problems) Debug.LogWarning($"Эхо-7 · баланс «{name}»: {p}", this);
        }

        [ContextMenu("Добавить недостающие записи из кода")]
        void ContextAddMissing()
        {
            RecordUndo("Добавить записи из кода");
            int n = AddMissingFromCode();
            MarkDirty();
            Debug.Log($"Эхо-7 · баланс «{name}»: добавлено записей — {n}.", this);
        }

        [ContextMenu("Сбросить к значениям из кода")]
        void ContextReset()
        {
            RecordUndo("Сбросить баланс");
            ResetToCode();
            MarkDirty();
            Debug.Log($"Эхо-7 · баланс «{name}» сброшен к значениям из кода (Ctrl+Z — отменить).", this);
        }

        void RecordUndo(string what)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, what);
#endif
        }

        void MarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            if (Application.isPlaying) ApplyLive();
        }
    }

    /// <summary>Ресурс и количество — удобная для инспектора замена <see cref="Cost"/>.</summary>
    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType type;
        [Min(0)] public int amount;

        public ResourceAmount(ResourceType t, int a) { type = t; amount = a; }
    }

    /// <summary>Количество, растущее с рангом командира: amount + perRank × ранг (Специалист = 0).</summary>
    [Serializable]
    public struct RankedAmount
    {
        public ResourceType type;
        [Min(0)] public int amount;
        [Tooltip("Прибавка за каждый ранг выше Специалиста.")]
        public int perRank;

        public RankedAmount(ResourceType t, int a, int perRank = 0) { type = t; amount = a; this.perRank = perRank; }
    }

    /// <summary>Загрузка ассетов баланса и общие преобразования.</summary>
    public static class BalanceData
    {
        public const string ResourcesFolder = "Echo7Balance";
        public const string AssetFolder = "Assets/Echo7/Resources/" + ResourcesFolder;

        /// <summary>Все типы ассетов баланса (для меню редактора).</summary>
        public static readonly Type[] AssetTypes =
        {
            typeof(BuildingBalance), typeof(TurretBalance), typeof(EnemyBalance),
            typeof(SkillBalance), typeof(TaskBalance), typeof(VipBalance)
        };

        static readonly HashSet<string> _reported = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _reported.Clear();

        /// <summary>Загрузить ассет; при первой загрузке за сессию — проверить и вывести предупреждения.</summary>
        public static T Load<T>(string fileName) where T : BalanceAsset
        {
            T asset = null;
            try { asset = Resources.Load<T>(ResourcesFolder + "/" + fileName); }
            catch (Exception e) { Debug.LogWarning($"Эхо-7: ошибка загрузки баланса {fileName}: {e.Message}"); }
            if (asset != null && _reported.Add(fileName))
            {
                var problems = new List<string>();
                asset.Validate(problems);
                foreach (var p in problems) Debug.LogWarning($"Эхо-7 · баланс «{fileName}»: {p}", asset);
            }
            return asset;
        }

        /// <summary>Ассет или (если его нет) временный экземпляр со значениями из кода.</summary>
        public static T LoadOrDefault<T>(string fileName) where T : BalanceAsset
        {
            var a = Load<T>(fileName);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            a.hideFlags = HideFlags.DontSave;
            a.ResetToCode();
            return a;
        }

        public static Cost ToCost(IList<ResourceAmount> list)
        {
            var c = new Cost();
            if (list != null) foreach (var r in list) if (r.amount > 0) c.Add(r.type, r.amount);
            return c;
        }

        public static Cost ToCost(IList<RankedAmount> list, int rank)
        {
            var c = new Cost();
            if (list != null)
                foreach (var r in list)
                {
                    int a = r.amount + r.perRank * Mathf.Max(0, rank);
                    if (a > 0) c.Add(r.type, a);
                }
            return c;
        }

        public static List<ResourceAmount> FromCost(Cost c)
        {
            var list = new List<ResourceAmount>();
            if (c != null) for (int i = 0; i < c.Types.Count; i++) list.Add(new ResourceAmount(c.Types[i], c.Amounts[i]));
            return list;
        }

        public static void CheckCost(IList<ResourceAmount> list, string owner, List<string> problems)
        {
            if (list == null) return;
            var seen = new HashSet<ResourceType>();
            foreach (var r in list)
            {
                if (r.amount < 0) problems.Add($"{owner}: отрицательная цена ({ResourceBank.Name(r.type)}).");
                if (!seen.Add(r.type)) problems.Add($"{owner}: ресурс «{ResourceBank.Name(r.type)}» указан дважды — значения сложатся.");
            }
        }

        public static void CheckDuplicates<TKey>(IEnumerable<TKey> keys, string what, List<string> problems)
        {
            var seen = new HashSet<TKey>();
            foreach (var k in keys) if (!seen.Add(k)) problems.Add($"{what} {k} описан(а) дважды — будет использована последняя запись.");
        }

        /// <summary>Безопасная подстановка числа в шаблон текста ("+{0}% к урону").</summary>
        public static string Format(string template, object value)
        {
            if (string.IsNullOrEmpty(template)) return value?.ToString() ?? "";
            try { return string.Format(template, value); }
            catch (FormatException) { return template; }
        }
    }
}
