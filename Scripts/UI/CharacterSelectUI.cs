#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Character selection screen — pick your fighter before the match.
/// Scene shell in character_select.tscn, character cards created dynamically in code.
/// Open the .tscn to edit the background, title, buttons layout.
/// </summary>
public partial class CharacterSelectUI : Control
{
    public event Action<CharacterClass>? OnCharacterConfirmed;
    public event Action? OnBack;

    private CharacterClass _selectedClass = CharacterClass.Manki;
    private Label _descLabel = null!;
    private Label _statsLabel = null!;
    private readonly List<(CharacterClass cls, string name, string desc, string stats)> _characters = new()
    {
        (CharacterClass.Manki, "Manki",
         "A pyromaniac monkey who rushes down with melee combos and bombards with explosives. Lobs round bombs, rocket jumps with dynamite, and dive bombs from above.",
         "HP: Medium  |  Speed: Fast  |  Range: Melee + Projectile  |  Difficulty: Medium"),
        (CharacterClass.Bunny, "Bunny",
         "A white rabbit kung-fu assassin who kicks first and asks questions later. Punishes with the Whirling Carrot mark into Dragon's Kick, then flips away.",
         "HP: Low  |  Speed: Very Fast  |  Range: Melee  |  Difficulty: Hard"),
    };

    public override void _Ready()
    {
        // Get scene nodes
        _descLabel = GetNode<Label>("DescLabel");
        _statsLabel = GetNode<Label>("StatsLabel");

        // Style static shell
        UITheme.StyleTitle(GetNode<Label>("Title"));
        UITheme.StyleBody(_descLabel, UITheme.White, 15);
        UITheme.StyleBody(_statsLabel, UITheme.DimWhite, 14);
        UITheme.StyleButton(GetNode<Button>("ConfirmBtn"), UITheme.Success, 24);
        UITheme.StyleButton(GetNode<Button>("BackBtn"), UITheme.DimWhite, 16);

        // Wire buttons
        GetNode<Button>("ConfirmBtn").Pressed += () => OnCharacterConfirmed?.Invoke(_selectedClass);
        GetNode<Button>("BackBtn").Pressed += () => OnBack?.Invoke();

        // Create character cards dynamically
        BuildCharacterCards();
    }

    private void BuildCharacterCards()
    {
        var viewport = GetViewportRect().Size;
        float cardWidth = 280f;
        float cardHeight = 340f;
        float totalWidth = _characters.Count * cardWidth + (_characters.Count - 1) * 30f;
        float startX = (viewport.X - totalWidth) / 2f;

        for (int i = 0; i < _characters.Count; i++)
        {
            var (cls, name, _, _) = _characters[i];
            bool selected = cls == _selectedClass;

            var card = new Panel
            {
                Position = new Vector2(startX + i * (cardWidth + 30f), 120f),
                CustomMinimumSize = new Vector2(cardWidth, cardHeight),
            };
            UITheme.StyleCard(card, selected);
            card.SetMeta("character_class", (int)cls);
            AddChild(card);

            // Card content
            var nameLabel = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
            UITheme.StyleHeading(nameLabel);
            nameLabel.AddThemeFontSizeOverride("font_size", 22);
            nameLabel.Position = new Vector2(0f, 15f);
            nameLabel.Size = new Vector2(cardWidth, 40f);
            card.AddChild(nameLabel);

            // Portrait placeholder
            var portrait = new ColorRect
            {
                Color = cls == CharacterClass.Manki
                    ? new Color(0.5f, 0.25f, 0.1f, 0.6f)
                    : new Color(0.9f, 0.9f, 0.9f, 0.2f),
                Position = new Vector2(30f, 65f),
                Size = new Vector2(220f, 200f),
            };
            card.AddChild(portrait);

            // Class type label
            var clsLabel = new Label { Text = cls == CharacterClass.Manki ? "Bomber" : "Assassin", HorizontalAlignment = HorizontalAlignment.Center };
            UITheme.StyleBody(clsLabel, UITheme.DimWhite, 14);
            clsLabel.Position = new Vector2(0f, 155f);
            clsLabel.Size = new Vector2(cardWidth, 30f);
            card.AddChild(clsLabel);

            // Selected indicator
            var selLabel = new Label { Text = "SELECTED", HorizontalAlignment = HorizontalAlignment.Center, Visible = selected };
            selLabel.AddThemeFontSizeOverride("font_size", 14);
            selLabel.Modulate = UITheme.Success;
            selLabel.Position = new Vector2(0f, 290f);
            selLabel.Size = new Vector2(cardWidth, 25f);
            selLabel.Name = "SelectedLabel";
            card.AddChild(selLabel);

            // Click
            int captured = i;
            card.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    SelectCharacter(_characters[captured].cls);
            };
        }
    }

    private void SelectCharacter(CharacterClass cls)
    {
        if (_selectedClass == cls) return;
        _selectedClass = cls;

        var entry = _characters.Find(c => c.cls == cls);
        _descLabel.Text = entry.desc;
        _descLabel.Modulate = UITheme.White;
        _statsLabel.Text = entry.stats;

        // Update card visuals
        foreach (var child in GetChildren())
        {
            if (child is Panel card && card.HasMeta("character_class"))
            {
                int cardCls = (int)card.GetMeta("character_class");
                bool selected = (CharacterClass)cardCls == cls;
                UITheme.StyleCard(card, selected);

                foreach (var sub in card.GetChildren())
                {
                    if (sub is Label lbl && lbl.Name == "SelectedLabel")
                        lbl.Visible = selected;
                }
            }
        }
    }
}
