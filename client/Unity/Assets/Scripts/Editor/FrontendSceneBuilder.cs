using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using SlopArena.Client.UI;

/// <summary>
/// One-shot builder for the single frontend scene (ADR-0032; issues #210,
/// #212, #219). Creates Assets/Scenes/Frontend.unity with the stable
/// FrontendShell document and the six page hosts. Migrated pages (Home,
/// Fighter Select) carry only their controller: the shell clones their
/// fragment source into its hosts on every activation. Unmigrated pages keep
/// their per-page UIDocument until they migrate (issues #221/#222) — a
/// temporary coexistence, not a compatibility router. Run through the Unity
/// CLI Pipeline, e.g.
/// `unity command --project-path client/Unity eval 'FrontendSceneBuilder.Build();'`.
/// </summary>
public static class FrontendSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Frontend.unity";
    private const string ShellUxmlPath = "Assets/UI/FrontendShell.uxml";
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
        // The stable shell document first, so its visual tree exists when the
        // controller binds and activates the pending page in Start (issue #219).
        var shellDocument = shellObject.AddComponent<UIDocument>();
        var panelPath = AssetDatabase.GUIDToAssetPath(PanelSettingsGuid);
        shellDocument.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
        shellDocument.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ShellUxmlPath);

        var shellView = shellObject.AddComponent<FrontendShellView>();
        var shellViewSerialized = new SerializedObject(shellView);
        shellViewSerialized.FindProperty("_uiDocument").objectReferenceValue = shellDocument;
        shellViewSerialized.ApplyModifiedPropertiesWithoutUndo();

        // Shell-owned identity surface and focus router (issue #220). The
        // frontend controller also creates them at runtime when missing, so
        // scenes built before Pass 2 keep working.
        shellObject.AddComponent<FrontendShellIdentityView>();
        shellObject.AddComponent<FrontendFocusRouter>();

        var shell = shellObject.AddComponent<FrontendController>();

        var pageObjects = new GameObject[Pages.Length];
        for (int i = 0; i < Pages.Length; i++)
        {
            var (name, uxml, controllerType, active) = Pages[i];
            var page = new GameObject(name);
            page.transform.SetParent(shellObject.transform, false);

            if (FrontendController.IsMigratedPage((FrontendPage)i))
            {
                // Fragment-mounted pages own no document (issue #219): the
                // shell clones the fragment source into its hosts on every
                // activation and injects the per-activation page context.
                page.AddComponent(controllerType);
            }
            else
            {
                // Unmigrated pages keep their per-page document until they
                // migrate; the controller's OnEnable still binds it.
                var document = page.AddComponent<UIDocument>();
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);

                var controller = page.AddComponent(controllerType);
                var serialized = new SerializedObject(controller);
                var documentProperty = serialized.FindProperty("_uiDocument");
                if (documentProperty == null)
                    throw new System.InvalidOperationException($"{controllerType.Name} has no _uiDocument field.");
                documentProperty.objectReferenceValue = document;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

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
        shellSerialized.FindProperty("_shell").objectReferenceValue = shellView;
        shellSerialized.FindProperty("_homeFragment").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Pages[0].Uxml);
        shellSerialized.FindProperty("_fighterSelectFragment").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Pages[1].Uxml);
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

        Debug.Log($"[FrontendSceneBuilder] Built {ScenePath} with the stable shell document, identity surface and focus router (#220), "
            + $"and {Pages.Length} pages "
            + "(Home + Fighter Select fragment-mounted; unmigrated pages keep per-page documents).");
    }
}
