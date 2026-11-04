using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Echo7.EditorTools
{
    /// <summary>
    /// Меню «Эхо-7 → Баланс»: создание ассетов баланса из значений кода, проверка, сброс.
    /// Ассеты лежат в Assets/Echo7/Resources/Echo7Balance. Игра читает их через Resources, а если какого-то
    /// ассета нет — работает на значениях из кода.
    /// </summary>
    public static class Echo7BalanceMenu
    {
        const string Root = "Эхо-7/Баланс/";

        [MenuItem(Root + "Создать или дополнить ассеты", priority = 30)]
        public static void CreateOrUpdate()
        {
            var report = EnsureAssets(true);
            Debug.Log("Эхо-7 · баланс: " + report);
            SelectFolder();
        }

        [MenuItem(Root + "Проверить", priority = 31)]
        public static void ValidateAll()
        {
            int found = 0, total = 0;
            foreach (var asset in LoadAll())
            {
                found++;
                var problems = new List<string>();
                asset.Validate(problems);
                total += problems.Count;
                foreach (var p in problems) Debug.LogWarning($"Эхо-7 · баланс «{asset.name}»: {p}", asset);
            }
            if (found == 0) { Debug.Log("Эхо-7 · баланс: ассетов нет — игра работает на значениях из кода. «Эхо-7 → Баланс → Создать или дополнить ассеты»."); return; }
            Debug.Log(total == 0 ? $"Эхо-7 · баланс: проверено ассетов — {found}, замечаний нет." : $"Эхо-7 · баланс: проверено ассетов — {found}, замечаний — {total} (см. предупреждения выше).");
        }

        [MenuItem(Root + "Сбросить всё к значениям из кода…", priority = 32)]
        public static void ResetAll()
        {
            var assets = LoadAll();
            if (assets.Count == 0) { CreateOrUpdate(); return; }
            if (!EditorUtility.DisplayDialog("Эхо-7 · баланс",
                    "Перезаписать ВСЕ ассеты баланса значениями из кода?\n\nПравки геймдизайнера будут потеряны (если ассеты под контролем версий — их можно вернуть из git).",
                    "Сбросить", "Отмена")) return;
            foreach (var a in assets)
            {
                Undo.RecordObject(a, "Сбросить баланс");
                a.ResetToCode();
                EditorUtility.SetDirty(a);
                if (Application.isPlaying) a.ApplyLive();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Эхо-7 · баланс: сброшено ассетов — {assets.Count}.");
        }

        [MenuItem(Root + "Показать папку баланса", priority = 33)]
        public static void SelectFolder()
        {
            var folder = AssetDatabase.LoadAssetAtPath<Object>(BalanceData.AssetFolder);
            if (folder == null) { Debug.Log("Эхо-7 · баланс: папки ещё нет. «Эхо-7 → Баланс → Создать или дополнить ассеты»."); return; }
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        /// <summary>
        /// Создать отсутствующие ассеты (значения из кода); у существующих — по желанию дописать записи,
        /// которые появились в коде (новые постройки, существа, навыки). Существующие значения не трогаются.
        /// </summary>
        public static string EnsureAssets(bool addMissing)
        {
            EnsureFolder(BalanceData.AssetFolder);
            int created = 0, updated = 0, added = 0;
            var sb = new StringBuilder();
            foreach (var type in BalanceData.AssetTypes)
            {
                var fresh = (BalanceAsset)ScriptableObject.CreateInstance(type);
                string path = $"{BalanceData.AssetFolder}/{fresh.FileName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<BalanceAsset>(path);
                if (existing == null)
                {
                    fresh.ResetToCode();
                    AssetDatabase.CreateAsset(fresh, path);
                    created++;
                    sb.Append($"\n  + {path}");
                    continue;
                }
                Object.DestroyImmediate(fresh);
                if (!addMissing) continue;
                Undo.RecordObject(existing, "Дополнить баланс");
                int n = existing.AddMissingFromCode();
                if (n > 0)
                {
                    EditorUtility.SetDirty(existing);
                    updated++;
                    added += n;
                    sb.Append($"\n  ~ {path}: добавлено записей {n}");
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (created == 0 && updated == 0) return "все ассеты на месте, новых записей в коде нет.";
            return $"создано ассетов — {created}, дополнено — {updated} (записей: {added}).{sb}";
        }

        static List<BalanceAsset> LoadAll()
        {
            var list = new List<BalanceAsset>();
            foreach (var type in BalanceData.AssetTypes)
                foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name, new[] { "Assets" }))
                {
                    var a = AssetDatabase.LoadAssetAtPath<BalanceAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (a != null && !list.Contains(a)) list.Add(a);
                }
            return list;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }

    /// <summary>
    /// Один раз на машину создаёт отсутствующие ассеты баланса, чтобы геймдизайнер сразу нашёл их в проекте.
    /// Удалённый вручную ассет повторно не создаётся (игра тогда берёт значения из кода).
    /// </summary>
    [InitializeOnLoad]
    static class Echo7BalanceFirstRun
    {
        const string Key = "Echo7_BalanceAssetsCreated";

        static Echo7BalanceFirstRun()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(Key, false)) return;
                EditorPrefs.SetBool(Key, true);
                Debug.Log("Эхо-7 · баланс: " + Echo7BalanceMenu.EnsureAssets(false) +
                          "\nНастраивайте значения в Assets/Echo7/Resources/Echo7Balance. Меню — «Эхо-7 → Баланс».");
            };
        }
    }
}
