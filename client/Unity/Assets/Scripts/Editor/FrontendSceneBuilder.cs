using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.UI;

/// <summary>
/// One-shot builder for the single frontend scene (ADR-0032, issues #210, #212).
/// Creates Assets/Scenes/Frontend.unity with the shared shell and the six
/// page hosts, cuts over the build scene list, and deletes the migrated
/// scene-per-page scenes. Run through the Unity CLI Pipeline, e.g.
/// `unity command --project-path client/Unity eval 'FrontendSceneBuilder.Build();'`.
/// </summary>
public static class FrontendSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Frontend.unity";
    private const string PanelSettingsGuid = "375219e0f9be73791af68905fab77544";

    private static readonly (string Name, string Uxml, System.Type Controller, bool Active)[] Pages =
    {
        // All pages start deactivated: the shell activates the pending page in
        // Start, so re-entering Home (and its preparation reset) happens only
        // on an explicit Home activation — never on frontend recreation for a
        // gameplay return (issue #210 early-exit retention).
        ("HomePage", "Assets/UI/MainMenu.uxml", typeof(MainMenuController), false),
        ("FighterSelectPage", "Assets/UI/CharSelect.uxml", typeof(CharSelectController), false),
        ("StageSelectPage", "Assets/UI/StageSelect.uxml", typeof(StageSelectController), false),
        ("ResultsPage", "Assets/UI/Results.uxml", typeof(ResultsUI), false),
        ("ServerBrowserPage", "Assets/UI/ServerBrowser.uxml", typeof(ServerBrowserUI), false),
        ("LobbyRoomPage", "Assets/UI/LobbyRoom.uxml", typeof(LobbyRoomUI), false),
    };

    private static readonly string[] RemovedScenes =
    {
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/CharSelect.unity",
        "Assets/Scenes/StageSelect.unity",
        "Assets/Scenes/Results.unity",
        "Assets/Scenes/ServerBrowser.unity",
        "Assets/Scenes/LobbyRoom.unity",
    };

    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var shellObject = new GameObject("Frontend");
        var shell = shellObject.AddComponent<FrontendController>();

        var pageObjects = new GameObject[Pages.Length];
        for (int i = 0; i < Pages.Length; i++)
        {
            var (name, uxml, controllerType, active) = Pages[i];
            var page = new GameObject(name);
            page.transform.SetParent(shellObject.transform, false);

            // UIDocument first so its visual tree exists when the page
            // controller's OnEnable runs during activation.
            var document = page.AddComponent<UIDocument>();
            var panelPath = AssetDatabase.GUIDToAssetPath(PanelSettingsGuid);
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);

            var controller = page.AddComponent(controllerType);
            var serialized = new SerializedObject(controller);
            var documentProperty = serialized.FindProperty("_uiDocument");
            if (documentProperty == null)
                throw new System.InvalidOperationException($"{controllerType.Name} has no _uiDocument field.");
            documentProperty.objectReferenceValue = document;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            page.SetActive(active);
            pageObjects[i] = page;
        }

        var shellSerialized = new SerializedObject(shell);
        shellSerialized.FindProperty("_homePage").objectReferenceValue = pageObjects[0];
        shellSerialized.FindProperty("_fighterSelectPage").objectReferenceValue = pageObjects[1];
        shellSerialized.FindProperty("_stageSelectPage").objectReferenceValue = pageObjects[2];
        shellSerialized.FindProperty("_resultsPage").objectReferenceValue = pageObjects[3];
        shellSerialized.FindProperty("_serverBrowserPage").objectReferenceValue = pageObjects[4];
        shellSerialized.FindProperty("_lobbyRoomPage").objectReferenceValue = pageObjects[5];
        shellSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);

        var buildScenes = EditorBuildSettings.scenes
            .Where(s => System.Array.IndexOf(RemovedScenes, s.path) < 0 && s.path != ScenePath)
            .ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();

        foreach (var removed in RemovedScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(removed) != null)
                AssetDatabase.DeleteAsset(removed);
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"[FrontendSceneBuilder] Built {ScenePath} with {Pages.Length} pages; "
            + $"removed migrated scene-per-page scenes: {string.Join(", ", RemovedScenes)}.");
    }
}
