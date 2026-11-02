using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Echo7.EditorTools
{
    /// <summary>Меню «Эхо-7» в редакторе: создание сцены, подготовка к сборке, сохранения.</summary>
    public static class Echo7Menu
    {
        public const string SceneDir = "Assets/Echo7/Scenes";
        public const string ScenePath = SceneDir + "/Echo7_Main.unity";

        static readonly string[] RequiredShaders =
        {
            "Standard", "Sprites/Default", "Skybox/Procedural", "Nature/Terrain/Standard", "Unlit/Color",
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Terrain/Lit"
        };

        [MenuItem("Эхо-7/Создать сцену игры", priority = 1)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Echo7");
            go.AddComponent<Echo7Entry>();
            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("Эхо-7: сцена создана — " + ScenePath + ". Нажмите Play.");
        }

        [MenuItem("Эхо-7/Открыть сцену игры", priority = 2)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) { CreateScene(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Эхо-7/Подготовить к сборке (шейдеры)", priority = 20)]
        public static void AddShadersToBuild()
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) { Debug.LogWarning("Эхо-7: GraphicsSettings.asset не найден."); return; }
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            int added = 0;
            foreach (var name in RequiredShaders)
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { present = true; break; }
                if (present) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                added++;
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            if (File.Exists(ScenePath)) AddToBuildSettings(ScenePath);
            Debug.Log($"Эхо-7: добавлено шейдеров в Always Included: {added}. Материалы создаются кодом, поэтому без этого шага в сборке они будут розовыми.");
        }

        [MenuItem("Эхо-7/Сохранения/Открыть папку", priority = 40)]
        public static void RevealSaves() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        [MenuItem("Эхо-7/Сохранения/Удалить сохранение", priority = 41)]
        public static void DeleteSave()
        {
            var p = Path.Combine(Application.persistentDataPath, "echo7_save.json");
            if (File.Exists(p) && EditorUtility.DisplayDialog("Эхо-7", "Удалить сохранение?", "Удалить", "Отмена"))
            {
                File.Delete(p);
                Debug.Log("Эхо-7: сохранение удалено.");
            }
        }

        static void AddToBuildSettings(string path)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }

    /// <summary>При первом открытии проекта предлагает создать сцену игры.</summary>
    [InitializeOnLoad]
    static class Echo7FirstRun
    {
        const string Key = "Echo7_FirstRunAsked";

        static Echo7FirstRun()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(Key, false) || File.Exists(Echo7Menu.ScenePath) || Application.isBatchMode) return;
                EditorPrefs.SetBool(Key, true);
                if (EditorUtility.DisplayDialog("Эхо-7",
                        "Создать сцену игры (Assets/Echo7/Scenes/Echo7_Main.unity)?\n\nМир, база и интерфейс собираются кодом — после создания просто нажмите Play.",
                        "Создать", "Позже"))
                    Echo7Menu.CreateScene();
            };
        }
    }
}
