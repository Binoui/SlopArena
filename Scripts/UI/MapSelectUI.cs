#nullable enable
using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// Map selection screen — pick arena before the match.
/// Scene shell in map_select.tscn, map cards created dynamically.
/// </summary>
public partial class MapSelectUI : Control
{
	public event Action<string>? OnMapConfirmed;
	public event Action? OnBack;

	private string _selectedArena = "split";
	private Label _descLabel = null!;

	private static readonly (string id, string name, string desc)[] Maps =
	{
		("pit",    "The Pit",      "A small, enclosed arena with low walls. Close quarters combat."),
		("split",  "Split",        "Two platforms separated by a gap. Keep your footing."),
		("sanctum","Sanctum",      "A wide-open arena with elevated platforms. Room to breathe."),
		("cross",  "Cross",        "Four platforms in a cross pattern. Vertical and horizontal play."),
	};

	public override void _Ready()
	{
		_descLabel = GetNode<Label>("DescLabel");

		UITheme.StyleTitle(GetNode<Label>("Title"));
		UITheme.StyleBody(_descLabel, UITheme.White, 16);
		UITheme.StyleButton(GetNode<Button>("FightBtn"), UITheme.Success, 28);
		UITheme.StyleButton(GetNode<Button>("BackBtn"), UITheme.DimWhite, 16);

		GetNode<Button>("FightBtn").Pressed += () => OnMapConfirmed?.Invoke(_selectedArena);
		GetNode<Button>("BackBtn").Pressed += () => OnBack?.Invoke();

		BuildMapCards();
	}

	private void BuildMapCards()
	{
		var viewport = GetViewportRect().Size;
		float cardWidth = 220f;
		float cardHeight = 180f;
		int cols = Math.Min(Maps.Length, 4);
		float totalWidth = cols * cardWidth + (cols - 1) * 20f;
		float startX = (viewport.X - totalWidth) / 2f;

		for (int i = 0; i < Maps.Length; i++)
		{
			var (id, name, desc) = Maps[i];
			bool selected = id == _selectedArena;

			var card = new Panel
			{
				Position = new Vector2(startX + i * (cardWidth + 20f), 130f),
				CustomMinimumSize = new Vector2(cardWidth, cardHeight),
			};
			card.SetMeta("arena_id", id);
			AddChild(card);

			// Use StyleCard on this specific panel
			var style = new StyleBoxFlat
			{
				BgColor = selected ? new Color(0.25f, 0.35f, 0.25f, 0.9f) : new Color(0.12f, 0.12f, 0.2f, 0.9f),
				CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
				CornerRadiusBottomRight = 12, CornerRadiusBottomLeft = 12,
				BorderWidthLeft = selected ? 3 : 1,
				BorderWidthRight = selected ? 3 : 1,
				BorderWidthTop = selected ? 3 : 1,
				BorderWidthBottom = selected ? 3 : 1,
				BorderColor = selected ? UITheme.Success : new Color(0.25f, 0.25f, 0.35f, 1f),
			};
			card.AddThemeStyleboxOverride("panel", style);

			// Map name
			var nameLabel = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center };
			UITheme.StyleHeading(nameLabel);
			nameLabel.AddThemeFontSizeOverride("font_size", 18);
			nameLabel.Size = new Vector2(cardWidth, 30f);
			nameLabel.Position = new Vector2(0f, 15f);
			card.AddChild(nameLabel);

			// Preview placeholder
			var preview = new ColorRect
			{
				Color = GetMapColor(id),
				Position = new Vector2(20f, 55f),
				Size = new Vector2(180f, 90f),
			};
			card.AddChild(preview);

			// Selection label
			var selLabel = new Label { Text = "SELECTED", HorizontalAlignment = HorizontalAlignment.Center, Visible = selected };
			selLabel.AddThemeFontSizeOverride("font_size", 13);
			selLabel.Modulate = UITheme.Success;
			selLabel.Position = new Vector2(0f, 150f);
			selLabel.Size = new Vector2(cardWidth, 25f);
			selLabel.Name = "SelectedLabel";
			card.AddChild(selLabel);

			int captured = i;
			card.GuiInput += (@event) =>
			{
				if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
					SelectMap(Maps[captured].id);
			};
		}
	}

	private void SelectMap(string id)
	{
		if (_selectedArena == id) return;
		_selectedArena = id;
		_descLabel.Text = GetDescription(id);

		foreach (var child in GetChildren())
		{
			if (child is Panel card && card.HasMeta("arena_id"))
			{
				string cardId = (string)card.GetMeta("arena_id");
				bool selected = cardId == id;

				var style = new StyleBoxFlat
				{
					BgColor = selected ? new Color(0.25f, 0.35f, 0.25f, 0.9f) : new Color(0.12f, 0.12f, 0.2f, 0.9f),
					CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
					CornerRadiusBottomRight = 12, CornerRadiusBottomLeft = 12,
					BorderWidthLeft = selected ? 3 : 1,
					BorderWidthRight = selected ? 3 : 1,
					BorderWidthTop = selected ? 3 : 1,
					BorderWidthBottom = selected ? 3 : 1,
					BorderColor = selected ? UITheme.Success : new Color(0.25f, 0.25f, 0.35f, 1f),
				};
				card.AddThemeStyleboxOverride("panel", style);

				foreach (var sub in card.GetChildren())
				{
					if (sub is Label lbl && lbl.Name == "SelectedLabel")
						lbl.Visible = selected;
				}
			}
		}
	}

	private static string GetDescription(string id)
	{
		foreach (var (i, _, desc) in Maps)
			if (i == id) return desc;
		return "";
	}

	private static Color GetMapColor(string id) => id switch
	{
		"pit" => new Color(0.3f, 0.15f, 0.1f, 0.7f),
		"split" => new Color(0.1f, 0.3f, 0.3f, 0.7f),
		"sanctum" => new Color(0.2f, 0.15f, 0.3f, 0.7f),
		"cross" => new Color(0.3f, 0.2f, 0.1f, 0.7f),
		_ => new Color(0.2f, 0.2f, 0.2f, 0.7f),
	};
}
