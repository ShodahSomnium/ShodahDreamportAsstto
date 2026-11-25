using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto.Editor
{
    /// <summary>
    /// Opens Assets/Assetto Scene.unity, marks it dirty and saves it, so Fusion's scene baker runs again (after a Fusion upgrade the scene's
    /// NetworkObjects must be re-baked: 2.1 bakes the flags as V2). Batch mode:
    ///   Unity -batchmode -quit -projectPath ... -executeMethod SomniumSpace.Worlds.SomniumAssetto.Editor.AcSceneResave.Run -logFile resave.log
    /// </summary>
    public static class AcSceneResave
    {
        public const string ScenePath = "Assets/Assetto Scene.unity";

        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorSceneManager.MarkSceneDirty(scene);
            bool ok = EditorSceneManager.SaveScene(scene);
            Debug.Log("[AcSceneResave] " + ScenePath + (ok ? " saved (Fusion re-baked its network objects)" : " NOT saved"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
