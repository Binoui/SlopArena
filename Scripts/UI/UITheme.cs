#nullable enable
using Godot;

/// <summary>
/// Static UI theme — brand colors, fonts, and styling methods.
/// Two modes: (a) create-and-style factories, (b) apply-style-to-existing-node.
/// Use (b) for scene-based screens where the .tscn defines the layout.
/// </summary>
public static class UITheme
{
	// ── Brand Palette ──
	public static readonly Color Orange = new(0.91f, 0.53f, 0.16f);
	public static readonly Color OrangeBright = new(1f, 0.7f, 0.25f);
	public static readonly Color Charcoal = new(0.1f, 0.1f, 0.18f);
	public static readonly Color CharcoalLight = new(0.16f, 0.16f, 0.26f);
	public static readonly Color Teal = new(0.18f, 0.62f, 0.62f);
	public static readonly Color TealBright = new(0.25f, 0.75f, 0.75f);
	public static readonly Color White = new(0.92f, 0.92f, 0.95f);
	public static readonly Color DimWhite = new(0.6f, 0.6f, 0.7f);
	public static readonly Color Danger = new(1f, 0.3f, 0.3f);
	public static readonly Color Success = new(0.2f, 0.8f, 0.2f);

	private static Font? _kenneyFont;
	private static bool _fontLoaded;

	private static Font GetFont()
	{
		if (!_fontLoaded)
		{
			_kenneyFont = GD.Load<Font>("res://assets/ui/font/Kenney Future.ttf");
			_fontLoaded = true;
		}
		return _kenneyFont!;
	}

	// ═══ APPLY-STYLE METHODS (for scene-based screens) ═══

	/// <summary>Apply title style to an existing Label node.</summary>
	public static void StyleTitle(Label label)
	{
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.Modulate = OrangeBright;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		var font = GetFont();
		if (font != null) label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", 48);
	}

	/// <summary>Apply heading style to an existing Label.</summary>
	public static void StyleHeading(Label label)
	{
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.Modulate = White;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		var font = GetFont();
		if (font != null) label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", 28);
	}

	/// <summary>Apply body text style to an existing Label.</summary>
	public static void StyleBody(Label label, Color? color = null, int fontSize = 18)
	{
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.Modulate = color ?? DimWhite;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.AddThemeFontSizeOverride("font_size", fontSize);
	}

	/// <summary>Apply brand button style to an existing Button.</summary>
	public static void StyleButton(Button btn, Color? tint = null, int fontSize = 20)
	{
		Color bg = tint ?? CharcoalLight;
		Color border = tint.HasValue
			? new Color(tint.Value.R * 0.8f, tint.Value.G * 0.8f, tint.Value.B * 0.8f, 1f)
			: new Color(0.35f, 0.35f, 0.5f, 1f);
		Color hoverBg = tint.HasValue
			? new Color(tint.Value.R * 0.35f, tint.Value.G * 0.35f, tint.Value.B * 0.35f, 1f)
			: CharcoalLight;

		btn.AddThemeStyleboxOverride("normal", MakeButtonStyle(bg, border));
		btn.AddThemeStyleboxOverride("hover", MakeButtonStyle(hoverBg, tint.HasValue
			? new Color(tint.Value.R * 0.9f, tint.Value.G * 0.9f, tint.Value.B * 0.9f, 1f)
			: new Color(0.5f, 0.5f, 0.7f, 1f)));
		btn.AddThemeStyleboxOverride("pressed", MakeButtonStyle(new Color(0.08f, 0.08f, 0.14f, 1f), Orange));
		btn.AddThemeStyleboxOverride("disabled", MakeButtonStyle(new Color(0.08f, 0.08f, 0.12f, 1f), new Color(0.2f, 0.2f, 0.3f, 1f)));

		var font = GetFont();
		if (font != null) btn.AddThemeFontOverride("font", font);
		btn.AddThemeColorOverride("font_color", White);
		btn.AddThemeColorOverride("font_hover_color", OrangeBright);
		btn.AddThemeColorOverride("font_disabled_color", new Color(0.3f, 0.3f, 0.4f, 1f));
		btn.AddThemeFontSizeOverride("font_size", fontSize);
	}

	private static StyleBoxFlat MakeButtonStyle(Color bg, Color border)
	{
		return new StyleBoxFlat
		{
			BgColor = bg,
			CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
			CornerRadiusBottomRight = 8, CornerRadiusBottomLeft = 8,
			BorderWidthLeft = 2, BorderWidthRight = 2,
			BorderWidthTop = 2, BorderWidthBottom = 2,
			BorderColor = border,
		};
	}

	/// <summary>Style an existing Panel as a character/map card.</summary>
	public static void StyleCard(Panel panel, bool selected = false)
	{
		var style = new StyleBoxFlat
		{
			BgColor = selected
				? new Color(0.25f, 0.35f, 0.25f, 0.9f)
				: new Color(0.12f, 0.12f, 0.2f, 0.9f),
			CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
			CornerRadiusBottomRight = 12, CornerRadiusBottomLeft = 12,
			BorderWidthLeft = selected ? 3 : 1,
			BorderWidthRight = selected ? 3 : 1,
			BorderWidthTop = selected ? 3 : 1,
			BorderWidthBottom = selected ? 3 : 1,
			BorderColor = selected ? Success : new Color(0.25f, 0.25f, 0.35f, 1f),
		};
		panel.AddThemeStyleboxOverride("panel", style);
	}

	/// <summary>Style an existing LineEdit for IP input etc.</summary>
	public static void StyleInput(LineEdit input)
	{
		input.Alignment = HorizontalAlignment.Center;

		var normalStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.08f, 0.15f, 1f),
			CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
			CornerRadiusBottomRight = 6, CornerRadiusBottomLeft = 6,
			BorderWidthLeft = 2, BorderWidthRight = 2,
			BorderWidthTop = 2, BorderWidthBottom = 2,
			BorderColor = new Color(0.3f, 0.3f, 0.45f, 1f),
		};
		input.AddThemeStyleboxOverride("normal", normalStyle);

		var focusStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.08f, 0.15f, 1f),
			CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
			CornerRadiusBottomRight = 6, CornerRadiusBottomLeft = 6,
			BorderWidthLeft = 2, BorderWidthRight = 2,
			BorderWidthTop = 2, BorderWidthBottom = 2,
			BorderColor = Orange,
		};
		input.AddThemeStyleboxOverride("focus", focusStyle);

		input.AddThemeFontSizeOverride("font_size", 18);
		input.AddThemeColorOverride("font_color", White);
		input.AddThemeColorOverride("placeholder_color", new Color(0.4f, 0.4f, 0.5f, 1f));

		var font = GetFont();
		if (font != null) input.AddThemeFontOverride("font", font);
	}

	// ═══ FACTORY METHODS (for code-based screens) ═══

	public static ColorRect CreateBackground()
	{
		return new ColorRect
		{
			Color = new Color(0.05f, 0.05f, 0.12f, 0.92f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
	}

	public static Label CreateTitle(string text)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Modulate = OrangeBright, MouseFilter = Control.MouseFilterEnum.Ignore };
		var font = GetFont(); if (font != null) label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", 48);
		return label;
	}

	public static Label CreateHeading(string text)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Modulate = White, MouseFilter = Control.MouseFilterEnum.Ignore };
		var font = GetFont(); if (font != null) label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", 28);
		return label;
	}

	public static Label CreateBody(string text, Color? color = null)
	{
		var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Modulate = color ?? DimWhite, MouseFilter = Control.MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", 18);
		return label;
	}

	public static Button CreateButton(string text, Color? tint = null, int fontSize = 20)
	{
		var btn = new Button { Text = text, CustomMinimumSize = new Vector2(360f, 56f), Size = new Vector2(360f, 56f) };
		StyleButton(btn, tint, fontSize);
		return btn;
	}

	public static Panel CreateCard(bool selected = false)
	{
		var panel = new Panel { CustomMinimumSize = new Vector2(280f, 320f), Size = new Vector2(280f, 320f) };
		StyleCard(panel, selected);
		return panel;
	}

	public static LineEdit CreateInputField(string placeholder, int width = 300)
	{
		var input = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(width, 40f), Size = new Vector2(width, 40f) };
		StyleInput(input);
		return input;
	}

	/// <summary>Create a styled info panel for lobby screens, etc.</summary>
	public static Panel CreateInfoPanel(float width, float height)
	{
		var panel = new Panel
		{
			CustomMinimumSize = new Vector2(width, height),
			Size = new Vector2(width, height),
		};
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.08f, 0.15f, 0.9f),
			CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
			CornerRadiusBottomRight = 12, CornerRadiusBottomLeft = 12,
			BorderWidthLeft = 2, BorderWidthRight = 2,
			BorderWidthTop = 2, BorderWidthBottom = 2,
			BorderColor = new Color(0.2f, 0.2f, 0.3f, 1f),
		};
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}
}
