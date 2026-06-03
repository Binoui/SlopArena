#nullable enable
using Godot;
using SlopArena.Shared;
using System;

/// <summary>
/// Pre-match class selection screen.
/// Player picks a class before the match starts. Choice is locked once match begins.
/// Shows class name, description, and stats for each available character.
/// </summary>
public partial class ClassSelectUI : Control
{
    public event Action<CharacterClass>? OnClassConfirmed;

    private CharacterClass _selectedClass = CharacterClass.Vanguard;
    private bool _confirmed = false;

    // UI nodes
    private ColorRect? _bg;
    private Label? _title;
    private Label? _description;
    private VBoxContainer? _classButtons;
    private Button? _readyBtn;
    private Label? _statsLabel;

    public override void _Ready()
    {
        var viewportSize = GetViewportRect().Size;
        Position = Vector2.Zero;
        Size = viewportSize;
        MouseFilter = MouseFilterEnum.Stop;

        // Background
        _bg = new ColorRect();
        _bg.Color = new Color(0f, 0f, 0f, 0.85f);
        _bg.Position = Vector2.Zero;
        _bg.Size = viewportSize;
        AddChild(_bg);

        // Title
        _title = new Label();
        _title.Text = "SELECT CLASS";
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.AddThemeFontSizeOverride("font_size", 52);
        _title.Position = new Vector2(0f, 60f);
        _title.Size = new Vector2(viewportSize.X, 70f);
        _title.Modulate = new Color(1f, 0.8f, 0.2f);
        AddChild(_title);

        // Class buttons (horizontal or vertical)
        _classButtons = new VBoxContainer();
        _classButtons.Position = new Vector2(viewportSize.X / 2f - 250f, 160f);
        _classButtons.Size = new Vector2(500f, 350f);
        _classButtons.AddThemeConstantOverride("separation", 10);
        AddChild(_classButtons);

        AddClassButton(CharacterClass.Vanguard, "Vanguard",   "Heavy brawler. Slow but tanky, shield bash and AoE crowd control.");
        AddClassButton(CharacterClass.Wraith,   "Wraith",     "Fast assassin. Hit-and-run, poison, shadow step, ranged pressure.");
        AddClassButton(CharacterClass.Channeler,"Channeler",  "Ranged mage. Frost, fire, beams and zone control.");
        AddClassButton(CharacterClass.Knight,   "Knight",     "Balanced swordfighter. Stun, gap closer, parry and sword combos.");

        // Selected class description
        _description = new Label();
        _description.Text = GetClassDescription(CharacterClass.Vanguard);
        _description.HorizontalAlignment = HorizontalAlignment.Center;
        _description.Position = new Vector2(200f, 540f);
        _description.Size = new Vector2(viewportSize.X - 400f, 60f);
        _description.AddThemeFontSizeOverride("font_size", 18);
        _description.Modulate = new Color(0.8f, 0.8f, 0.8f);
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_description);

        // Stats display
        _statsLabel = new Label();
        _statsLabel.Text = GetClassStats(CharacterClass.Vanguard);
        _statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statsLabel.Position = new Vector2(200f, 600f);
        _statsLabel.Size = new Vector2(viewportSize.X - 400f, 100f);
        _statsLabel.AddThemeFontSizeOverride("font_size", 16);
        _statsLabel.Modulate = new Color(0.6f, 0.6f, 0.6f);
        AddChild(_statsLabel);

        // Ready button
        _readyBtn = new Button();
        _readyBtn.Text = "FIGHT!";
        _readyBtn.Position = new Vector2(viewportSize.X / 2f - 120f, 720f);
        _readyBtn.CustomMinimumSize = new Vector2(240f, 60f);
        _readyBtn.AddThemeFontSizeOverride("font_size", 28);
        _readyBtn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _readyBtn.Modulate = new Color(0.2f, 0.8f, 0.2f);
        _readyBtn.Pressed += OnReadyPressed;
        AddChild(_readyBtn);

        // Highlight default selection
        HighlightSelected();
    }

    private void AddClassButton(CharacterClass cls, string name, string desc)
    {
        var btn = new Button();
        btn.Text = name;
        btn.CustomMinimumSize = new Vector2(500f, 50f);
        btn.AddThemeFontSizeOverride("font_size", 22);
        btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        btn.Pressed += () => SelectClass(cls, btn);
        _classButtons!.AddChild(btn);
    }

    private void SelectClass(CharacterClass cls, Button source)
    {
        if (_confirmed) return;
        _selectedClass = cls;
        _description!.Text = GetClassDescription(cls);
        _statsLabel!.Text = GetClassStats(cls);
        HighlightSelected();
    }

    private void HighlightSelected()
    {
        if (_classButtons == null) return;
        foreach (var child in _classButtons.GetChildren())
        {
            if (child is Button btn)
            {
                CharacterClass btnClass = btn.Text switch
                {
                    "Vanguard" => CharacterClass.Vanguard,
                    "Wraith" => CharacterClass.Wraith,
                    "Channeler" => CharacterClass.Channeler,
                    "Knight" => CharacterClass.Knight,
                    _ => CharacterClass.Vanguard
                };
                btn.Modulate = btnClass == _selectedClass
                    ? new Color(0.3f, 0.9f, 0.3f)
                    : new Color(1f, 1f, 1f);
            }
        }
    }

    private void OnReadyPressed()
    {
        if (_confirmed) return;
        _confirmed = true;
        _readyBtn!.Text = "READY!";
        _readyBtn.Modulate = new Color(0.3f, 0.3f, 0.3f);
        _readyBtn.Disabled = true;

        // Brief delay then fire event
        var timer = GetTree().CreateTimer(0.3f);
        timer.Timeout += () =>
        {
            OnClassConfirmed?.Invoke(_selectedClass);
            QueueFree();
        };
    }

    private static string GetClassDescription(CharacterClass cls) => cls switch
    {
        CharacterClass.Vanguard => "A heavy front-line brawler with shield bashes, war cries and thunderous slams. Slow but durable.",
        CharacterClass.Wraith => "A lightning-fast assassin who strikes from the shadows with poison blades, rapid fire and shadow step.",
        CharacterClass.Channeler => "A ranged magic user who controls the battlefield with frost, fire and beam attacks from a distance.",
        CharacterClass.Knight => "A balanced sword-and-shield fighter with a stunning light, a gap-closing lunge, a parry and a devastating Excalibur slam.",
        _ => ""
    };

    private static string GetClassStats(CharacterClass cls) => cls switch
    {
        CharacterClass.Vanguard => "HP: High  |  Speed: Slow  |  Range: Melee  |  Difficulty: Easy",
        CharacterClass.Wraith => "HP: Low  |  Speed: Fast  |  Range: Mixed  |  Difficulty: Medium",
        CharacterClass.Channeler => "HP: Medium  |  Speed: Medium  |  Range: Ranged  |  Difficulty: Medium",
        CharacterClass.Knight => "HP: Medium  |  Speed: Medium  |  Range: Melee  |  Difficulty: Easy",
        _ => ""
    };
}
