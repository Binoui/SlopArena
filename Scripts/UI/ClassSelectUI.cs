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

    private CharacterClass _selectedClass = CharacterClass.Manki;
    private bool _confirmed = false;

    /// <summary>
    /// UI nodes
    /// </summary>
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
        _classButtons.Position = new Vector2((viewportSize.X / 2f) - 250f, 160f);
        _classButtons.Size = new Vector2(500f, 350f);
        _classButtons.AddThemeConstantOverride("separation", 10);
        AddChild(_classButtons);

        AddClassButton(CharacterClass.Manki, "Manki");// only Manki for now
        AddClassButton(CharacterClass.Bunny, "Bunny");

        // Selected class description
        _description = new Label();
        _description.Text = GetClassDescription(CharacterClass.Manki);
        _description.HorizontalAlignment = HorizontalAlignment.Center;
        _description.Position = new Vector2(200f, 540f);
        _description.Size = new Vector2(viewportSize.X - 400f, 60f);
        _description.AddThemeFontSizeOverride("font_size", 18);
        _description.Modulate = new Color(0.8f, 0.8f, 0.8f);
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_description);

        // Stats display
        _statsLabel = new Label();
        _statsLabel.Text = GetClassStats(CharacterClass.Manki);
        _statsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statsLabel.Position = new Vector2(200f, 600f);
        _statsLabel.Size = new Vector2(viewportSize.X - 400f, 100f);
        _statsLabel.AddThemeFontSizeOverride("font_size", 16);
        _statsLabel.Modulate = new Color(0.6f, 0.6f, 0.6f);
        AddChild(_statsLabel);

        // Ready button
        _readyBtn = new Button();
        _readyBtn.Text = "FIGHT!";
        _readyBtn.Position = new Vector2((viewportSize.X / 2f) - 120f, 720f);
        _readyBtn.CustomMinimumSize = new Vector2(240f, 60f);
        _readyBtn.AddThemeFontSizeOverride("font_size", 28);
        _readyBtn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _readyBtn.Modulate = new Color(0.2f, 0.8f, 0.2f);
        _readyBtn.Pressed += OnReadyPressed;
        AddChild(_readyBtn);

        // Highlight default selection
        HighlightSelected();
    }

    private void AddClassButton(CharacterClass cls, string name)
    {
        var btn = new Button();
        btn.Text = name;
        btn.CustomMinimumSize = new Vector2(500f, 50f);
        btn.AddThemeFontSizeOverride("font_size", 22);
        btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        btn.Pressed += () => SelectClass(cls);
        _classButtons!.AddChild(btn);
    }

    private void SelectClass(CharacterClass cls)
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
                btn.Modulate = btn.Text == _selectedClass.ToString()
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
        CharacterClass.Manki => "A pyromaniac monkey who rushes down with melee combos and bombards with explosives. Lobs round bombs, rocket jumps with dynamite, and dive bombs from above for a massive Big Boom finale.",
        CharacterClass.Bunny => "A white rabbit kung-fu assassin who kicks first and asks questions later. Punishes with the Whirling Carrot mark into Dragon's Kick, then flips away before you can react.",
        _ => ""
    };

    private static string GetClassStats(CharacterClass cls) => cls switch
    {
        CharacterClass.Manki => "HP: Medium  |  Speed: Fast  |  Range: Melee + Projectile  |  Difficulty: Medium",
        CharacterClass.Bunny => "HP: Low  |  Speed: Very Fast  |  Range: Melee  |  Difficulty: Hard",
        _ => ""
    };
}
