using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    public class CharSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        // Off-screen camera + model for 3D preview
        [SerializeField] private Camera _previewCamera;
        [SerializeField] private RenderTexture _previewRenderTexture;
        [SerializeField] private Transform _previewModelRoot;

        private CharacterClass _selected = CharacterClass.Manki;
        private readonly List<Button> _gridButtons = new();
        private GameObject _currentModel;

        private static readonly CharacterClass[] Classes =
            (CharacterClass[])System.Enum.GetValues(typeof(CharacterClass));
        // Slot index → key label: Q=2, E=3, R=4, F=5 (matches GetSlotAbility)
        private static readonly string[] AbilitySlots =
            { "ability-q", "ability-e", "ability-r", "ability-f" };

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            var grid = root.Q<VisualElement>("char-grid");

            // Build portrait buttons for each known character class
            foreach (var cls in Classes)
            {
                var capturedCls = cls;
                var btn = new Button(() => SelectCharacter(capturedCls, root))
                {
                    text = cls.ToString().ToUpper(),
                    name = $"char-{cls}"
                };
                btn.AddToClassList("char-card");
                grid.Add(btn);
                _gridButtons.Add(btn);
            }

            root.Q<Button>("btn-select").clicked += () =>
            {
                MatchConfig.PlayerClass = _selected;
                SceneManager.LoadScene("StageSelect");
            };

            root.Q<Button>("btn-back").clicked += () =>
            {
                string prev = MatchConfig.Mode == GameMode.Training ? "MainMenu" : "Lobby";
                SceneManager.LoadScene(prev);
            };

            // Wire preview camera render texture to model-image element
            if (_previewRenderTexture != null)
            {
                var modelImage = root.Q<VisualElement>("model-image");
                modelImage.style.backgroundImage = Background.FromRenderTexture(_previewRenderTexture);
            }

            SelectCharacter(_selected, root);
        }

        private void SelectCharacter(CharacterClass cls, VisualElement root)
        {
            _selected = cls;

            // Highlight selected grid button
            foreach (var btn in _gridButtons)
            {
                btn.RemoveFromClassList("char-card--selected");
                if (btn.name == $"char-{cls}")
                    btn.AddToClassList("char-card--selected");
            }

            // Update name
            root.Q<Label>("char-name").text = cls.ToString().ToUpper();

            // Load ability data: Q=slot2, E=slot3, R=slot4, F=slot5
            var def = CharacterRegistry.Get(cls);
            for (int i = 0; i < AbilitySlots.Length; i++)
            {
                var spec = def.GetSlotAbility(i + 2, airborne: false);
                var card = root.Q<VisualElement>(AbilitySlots[i]);
                if (card == null) continue;
                card.Q<Label>($"{AbilitySlots[i]}-name").text = spec?.Name ?? "—";
                card.Q<Label>($"{AbilitySlots[i]}-desc").text = spec?.Description ?? "";
            }

            SwapPreviewModel(cls);
        }

        private void SwapPreviewModel(CharacterClass cls)
        {
            if (_previewModelRoot == null) return;
            if (_currentModel != null) Destroy(_currentModel);

            // Try to load model prefab from Resources; fall back to capsule placeholder
            var prefab = Resources.Load<GameObject>($"Characters/{cls}/Model");
            if (prefab != null)
            {
                _currentModel = Instantiate(prefab, _previewModelRoot);
                _currentModel.transform.localPosition = Vector3.zero;
                _currentModel.transform.localRotation = Quaternion.identity;
            }
            else
            {
                _currentModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _currentModel.transform.SetParent(_previewModelRoot, false);
                _currentModel.transform.localPosition = Vector3.zero;
            }
        }

        private void OnDisable()
        {
            if (_currentModel != null) Destroy(_currentModel);
        }
    }
}
