using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed class AbilityLabInspectorWindow : EditorWindow
{
    [SerializeField] private AbilityLabWindow? _owner;
    private VisualElement? _host;
    private Label? _context;
    private Label? _availability;
    private Button? _returnInline;

    internal void BindOwner(AbilityLabWindow owner)
    {
        if (_owner != null && _owner != owner)
            _owner.ReturnInspectorInline(this);
        _owner = owner;
        CreateGUI();
        if (!owner.HasDetachedFields(this))
            owner.AttachInspectorFields(this, _host!);
    }

    public void CreateGUI()
    {
        string focusedName = _owner?.CaptureFieldsFocus() ?? "";
        bool wasAttached = _owner != null && _owner.HasDetachedFields(this);
        var root = rootVisualElement;
        root.focusable = true;
        root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        root.Clear();
        root.AddToClassList("ability-lab-fields-root");
        var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/AbilityLab/Editor/AbilityLabWindow.uss");
        if (stylesheet != null && !root.styleSheets.Contains(stylesheet))
            root.styleSheets.Add(stylesheet);
        _context = new Label { name = "fields-context" };
        _context.AddToClassList("fields-context");
        _returnInline = new Button(() => _owner?.ReturnInspectorInline(this))
        {
            name = "fields-return-inline", text = "Return fields inline",
        };
        _availability = new Label { name = "fields-availability" };
        _availability.AddToClassList("fields-context");
        _host = new VisualElement { name = "detached-fields-host" };
        _host.AddToClassList("fields-host");
        root.Add(_context);
        root.Add(_returnInline);
        root.Add(_availability);
        root.Add(_host);
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        if (wasAttached)
            _owner!.AttachInspectorFields(this, _host);
        RefreshAvailability();
        _owner?.RestoreFocus(focusedName);
    }

    internal void RefreshAvailability()
    {
        bool attached = _owner != null && _owner.HasDetachedFields(this);
        bool visible = attached && _owner!.MovesPageActive;
        if (_context != null)
            _context.text = _owner != null ? _owner.InspectorContext : "Ability Lab fields";
        if (_host != null)
            _host.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (_availability != null)
        {
            _availability.text = _owner == null
                ? "Open Ability Lab and use Detach fields to attach this pane."
                : !attached ? "Fields are inline in Ability Lab."
                : !visible ? "Select Moves in Ability Lab to inspect move fields." : "";
            _availability.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
        }
        _returnInline?.SetEnabled(attached);
    }

    internal void OwnerUnavailable(AbilityLabWindow owner)
    {
        if (_owner != owner) return;
        _owner = null;
        _host?.Clear();
        RefreshAvailability();
    }

    private void OnKeyDown(KeyDownEvent evt) => _owner?.HandleFieldsKeyDown(evt);

    private void OnDisable()
    {
        _owner?.ReturnInspectorInline(this);
        _owner = null;
    }
}
