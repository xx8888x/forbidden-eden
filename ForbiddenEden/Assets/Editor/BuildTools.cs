using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ForbiddenEden.EditorTools
{
    /// <summary>
    /// Batch-mode entry points:
    ///   Unity.exe -batchmode -quit -projectPath … -executeMethod ForbiddenEden.EditorTools.BuildTools.SetupAndBuild
    /// Creates the (empty) boot scene, applies player settings and builds Build/ForbiddenEden.exe.
    /// The whole game is created from code at runtime by GameApp.
    /// </summary>
    public static class BuildTools
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("超禁忌學園/建立場景與設定")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // A Standard material in Resources keeps the shader in the player build.
            Directory.CreateDirectory("Assets/Resources");
            const string matPath = "Assets/Resources/EdenLit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")), matPath);

            // Fog is only turned on from code, so automatic stripping would remove its variants.
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            SetInt(graphics, "m_FogStripping", 1);
            SetInt(graphics, "m_FogKeepLinear", 1);
            SetInt(graphics, "m_FogKeepExp", 1);
            SetInt(graphics, "m_FogKeepExp2", 1);
            graphics.ApplyModifiedProperties();

            PlayerSettings.companyName = "AISG";
            PlayerSettings.productName = "超禁忌學園 EDEN";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            // Gamma: the UI is mostly translucent dark glass, and in linear space its alpha
            // blends far lighter than the values picked for it.
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SplashScreen.show = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[BuildTools] Setup done.");
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null)
                p.intValue = value;
            else
                Debug.LogWarning("[BuildTools] GraphicsSettings has no " + name);
        }

        [MenuItem("超禁忌學園/建置 Windows 版")]
        public static void Build()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build"));
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(outDir, "ForbiddenEden.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[BuildTools] Build " + report.summary.result + " → " + options.locationPathName +
                      " (" + report.summary.totalErrors + " errors, " + report.summary.totalSize / (1024 * 1024) + " MB)");
            if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        public static void SetupAndBuild()
        {
            try
            {
                Setup();
                Build();
            }
            catch (Exception ex)
            {
                Debug.LogError("[BuildTools] " + ex);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }
    }
}
