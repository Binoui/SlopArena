using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.MovementReport;

/// <summary>
/// Movement data sheet (issue #150): measures run / dash / jump / double jump / air drift /
/// fall / fast fall / stop for every character from the REAL ServerSimulation
/// (MovementProbe) and renders a side-by-side comparison table + per-character curves.
/// Lossless per-character JSON (full tick-by-tick sample curves) + self-contained HTML +
/// markdown, consistent with the move-data / self-play report pattern. No new physics —
/// every number comes out of the sim.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true, // MovementStats is a field-based struct
    };

    private static int Main(string[] args)
    {
        string charName = ParseArg(args, "--char") ?? "all";
        string? jsonDir = ParseArg(args, "--json");
        string? htmlPath = ParseArg(args, "--html");
        string? outPath = ParseArg(args, "--out");

        var defs = new List<CharacterDefinition>();
        if (charName == "all")
        {
            foreach (CharacterClass c in Enum.GetValues(typeof(CharacterClass)))
                if (c != CharacterClass.None) defs.Add(CharacterRegistry.Get(c));
        }
        else
        {
            defs.Add(ResolveCharacter(charName));
        }

        var arena = FlatArena();
        var measured = new List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)>();
        foreach (var def in defs) measured.Add((def, MovementProbe.Measure(def, arena)));

        if (jsonDir != null)
        {
            Directory.CreateDirectory(jsonDir);
            foreach (var (def, m) in measured)
                File.WriteAllText(Path.Combine(jsonDir, $"movement-{def.Class.ToString().ToLowerInvariant()}.json"),
                    JsonSerializer.Serialize(m, JsonOpts));
        }

        if (htmlPath != null)
            File.WriteAllText(htmlPath, BuildHtml(measured));

        string md = BuildMarkdown(measured);
        if (outPath != null) File.WriteAllText(outPath, md);
        else Console.WriteLine(md);
        return 0;
    }

    // ── CLI ───────────────────────────────────────────────────────────────────

    private static string? ParseArg(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == key) return args[i + 1];
        return null;
    }

    private static CharacterDefinition ResolveCharacter(string which)
    {
        var cls = which.ToLowerInvariant() switch
        {
            "fightguy" => CharacterClass.FightGuy,
            "manki" => CharacterClass.Manki,
            "kistu" => CharacterClass.Kistu,
            "nilus" => CharacterClass.Nilus,
            _ => throw new ArgumentException($"unknown character: {which} (expected one of: fightguy, manki, kistu, nilus, all)"),
        };
        return CharacterRegistry.Get(cls);
    }

    private static ArenaDefinition FlatArena()
    {
        const int w = 200, h = 200;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "movement-probe",
            DisplayName = "Movement Probe Arena",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f },
        };
    }

    // ── Markdown ──────────────────────────────────────────────────────────────

    private static string BuildMarkdown(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all)
    {
        var sb = new StringBuilder();
        string generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        sb.AppendLine("# Movement data sheet — measured from the real sim (issue #150)");
        sb.AppendLine();
        sb.AppendLine($"> Generated {generated} · scripted inputs on the real ServerSimulation (60 Hz tick). ");
        sb.AppendLine("> Run: hold right from standstill. Dash: one dash press. Jump: full jump (held past the short-hop ");
        sb.AppendLine("> window). Double jump: jump edge at first apex. Drift: stick held through the full hop. ");
        sb.AppendLine("> Fall: spawned airborne at 50 m (float window skipped) — natural drop vs hold-Down fast fall. ");
        sb.AppendLine("> Stop: release at cruise. ");
        sb.AppendLine("> Values are measured (effective behavior, incl. rush kick-off, float windows, caps), not authored constants.");
        sb.AppendLine();

        // Side-by-side comparison
        sb.AppendLine("## Comparison");
        sb.AppendLine();
        sb.AppendLine("| metric | " + string.Join(" | ", all.ConvertAll(x => x.Def.DisplayName)) + " |");
        sb.AppendLine("|" + string.Concat(all.ConvertAll(_ => "---|")));
        foreach (var row in Rows(all))
        {
            sb.Append("| ").Append(row.Label).Append(" | ");
            sb.Append(string.Join(" | ", row.Values));
            sb.AppendLine(" |");
        }
        sb.AppendLine();

        foreach (var (def, m) in all)
        {
            sb.AppendLine($"## {def.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"- **Run**: {m.Run.MaxSpeed:F1} m/s (authored {m.Authored.RunSpeed:F0})"
                + (m.Run.Note.Length > 0 ? $" — {m.Run.Note}" : $"; time-to-max {m.Run.TimeToMaxTicks + 1} ticks, {m.Run.DistanceToMax:F2} m"));
            sb.AppendLine($"- **Dash**: {m.Dash.DurationTicks} ticks, {m.Dash.TotalDistance:F2} m, actionable on tick {m.Dash.ActionableTick} "
                + $"(hard stop; authored {m.Authored.DashSpeed:F0} m/s for {m.Authored.DashDurationTicks} ticks)");
            sb.AppendLine($"- **Jump**: apex {m.Jump.ApexHeight:F2} m at {m.Jump.TimeToApexTicks} ticks, airtime {m.Jump.AirtimeTicks / 60f:F2} s, "
                + $"full-hop drift {m.Jump.HorizontalDistance:F2} m; running jump carries {m.RunningJump.HorizontalDistance:F2} m");
            sb.AppendLine($"- **Double jump**: second apex {m.DoubleJump.ApexHeight:F2} m, total airtime {m.DoubleJump.AirtimeTicks / 60f:F2} s");
            sb.AppendLine($"- **Air drift**: speed cap {m.Jump.DriftSpeedMax:F1} m/s (authored {m.Authored.AirSpeedMax:F1})");
            sb.AppendLine($"- **Fall** (50 m drop): max {m.Fall.MaxFallSpeed:F0} m/s (authored {m.Authored.MaxFallSpeed:F0}), reached "
                + $"{m.Fall.TimeToMaxFallTicks / 60f:F2} s into the drop, descent {m.Fall.DescentTicks / 60f:F2} s; "
                + $"fast fall {m.Fall.FastFallSpeed:F0} m/s (authored {m.Authored.FastFallSpeed:F0}), 50 m descent {m.Fall.FastFallDescentTicks / 60f:F2} s");
            sb.AppendLine($"- **Stop** (cruise → standstill): {m.Stop.StopTicks / 60f:F2} s, {m.Stop.StopDistance:F2} m");
            sb.AppendLine();
        }
        // The comparison rows are shared with the HTML renderer; strip the styling spans.
        return sb.ToString().Replace("<span class=\"note\">(", "(").Replace(")</span>", ")");
    }

    // ── HTML ──────────────────────────────────────────────────────────────────

    private static string BuildHtml(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all)
    {
        var sb = new StringBuilder();
        string generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Movement data sheet</title><style>");
        sb.AppendLine("body{background:#0d1117;color:#c9d1d9;font-family:ui-monospace,Menlo,Consolas,monospace;margin:2rem auto;max-width:1100px;padding:0 1rem}");
        sb.AppendLine("h1{color:#f0f6fc}h2{margin-top:2.5rem;color:#58a6ff;border-bottom:1px solid #30363d;padding-bottom:.3rem}");
        sb.AppendLine("h3{color:#f0f6fc;font-size:.85rem;margin:0 0 .4rem}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;margin:1rem 0;font-size:.85rem}");
        sb.AppendLine("th,td{border:1px solid #30363d;padding:.4rem .6rem;text-align:right}th{background:#161b22;color:#f0f6fc}");
        sb.AppendLine("td:first-child{text-align:left;color:#f0f6fc;white-space:nowrap}");
        sb.AppendLine(".meta{color:#8b949e;font-size:.85rem;line-height:1.5}.note{color:#f0883e}");
        sb.AppendLine(".curves{display:flex;flex-wrap:wrap;gap:1rem}.curve{background:#161b22;border:1px solid #30363d;padding:.6rem}");
        sb.AppendLine("svg text{fill:#8b949e;font-size:9px}svg line{stroke:#30363d}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>Movement data sheet</h1>");
        sb.AppendLine($"<div class=\"meta\">Generated {generated} &middot; scripted inputs on the real ServerSimulation (60 Hz). ");
        sb.AppendLine("Run: hold right from standstill &middot; Dash: one press &middot; Jump: full jump &middot; Double jump: edge at first apex &middot; ");
        sb.AppendLine("Fall: natural / fast-fall (hold Down) from apex &middot; Drift: stick held through the full hop &middot; Stop: release at cruise.<br>");
        sb.AppendLine("Measured <em>effective</em> behavior (rush kick-off, float windows, caps) &mdash; not authored constants. Authored values in <span class=\"note\">orange</span>.</div>");

        // Side-by-side comparison
        sb.AppendLine("<h2>Comparison</h2><table><tr><th>metric</th>");
        foreach (var (def, _) in all) sb.Append($"<th>{Escape(def.DisplayName)}</th>");
        sb.AppendLine("</tr>");
        foreach (var row in Rows(all))
        {
            sb.Append($"<tr><td>{Escape(row.Label)}</td>");
            foreach (var v in row.Values) sb.Append($"<td>{v}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");

        foreach (var (def, m) in all)
        {
            float groundY = def.CapsuleHeight * 0.5f;
            sb.AppendLine($"<h2>{Escape(def.DisplayName)}</h2>");
            sb.AppendLine("<table><tr><th>metric</th><th>value</th></tr>");
            Add(sb, "Run max speed", $"{m.Run.MaxSpeed:F1} m/s <span class=\"note\">(authored {m.Authored.RunSpeed:F0})</span>");
            Add(sb, "Run time-to-max", m.Run.Note.Length > 0 ? "instant — tick 1 <span class=\"note\">(rush kick-off, no ramp)</span>"
                : $"{m.Run.TimeToMaxTicks + 1} ticks, {m.Run.DistanceToMax:F2} m");
            Add(sb, "Dash", $"{m.Dash.DurationTicks} ticks &middot; {m.Dash.TotalDistance:F2} m &middot; actionable tick {m.Dash.ActionableTick} "
                + $"<span class=\"note\">(authored {m.Authored.DashSpeed:F0} m/s &times; {m.Authored.DashDurationTicks} ticks)</span>");
            Add(sb, "Jump", $"apex {m.Jump.ApexHeight:F2} m at {m.Jump.TimeToApexTicks} ticks &middot; airtime {m.Jump.AirtimeTicks / 60f:F2} s &middot; "
                + $"full-hop drift {m.Jump.HorizontalDistance:F2} m");
            Add(sb, "Running jump", $"full hop from cruise: {m.RunningJump.HorizontalDistance:F2} m, apex {m.RunningJump.ApexHeight:F2} m");
            Add(sb, "Double jump", $"second apex {m.DoubleJump.ApexHeight:F2} m &middot; total airtime {m.DoubleJump.AirtimeTicks / 60f:F2} s");
            Add(sb, "Air drift cap", $"{m.Jump.DriftSpeedMax:F1} m/s <span class=\"note\">(authored {m.Authored.AirSpeedMax:F1})</span>");
            Add(sb, "Fall (50 m drop)", $"max {m.Fall.MaxFallSpeed:F0} m/s <span class=\"note\">(authored {m.Authored.MaxFallSpeed:F0})</span> &middot; "
                + $"time-to-max {m.Fall.TimeToMaxFallTicks / 60f:F2} s &middot; descent {m.Fall.DescentTicks / 60f:F2} s");
            Add(sb, "Fast fall (50 m drop)", $"{m.Fall.FastFallSpeed:F0} m/s <span class=\"note\">(authored {m.Authored.FastFallSpeed:F0})</span> &middot; "
                + $"descent {m.Fall.FastFallDescentTicks / 60f:F2} s &middot; reach {m.Fall.FastFallReachTicks} ticks");
            Add(sb, "Stop", $"{m.Stop.StopTicks / 60f:F2} s &middot; {m.Stop.StopDistance:F2} m (GroundStopFriction 36 m/s&sup2;)");
            sb.AppendLine("</table>");

            sb.AppendLine("<div class=\"curves\">");
            Chart(sb, "Run — speed vs tick", ToPts(m.Run.Curve, c => c.Speed), null);
            Chart(sb, "Dash — distance vs tick", ToPts(m.Dash.Curve, c => c.PosX), null);
            Chart(sb, "Jump — height vs tick", ToPts(m.Jump.Curve, c => c.PosY - groundY), null);
            Chart(sb, "Double jump — height vs tick", ToPts(m.DoubleJump.Curve, c => c.PosY - groundY), null);
            Chart(sb, "Fall — fall speed vs tick (natural / fast fall)", ToPts(m.Fall.NaturalCurve, c => c.Vy), ToPts(m.Fall.FastFallCurve, c => c.Vy));
            Chart(sb, "Stop — speed vs tick", ToPts(m.Stop.Curve, c => c.Speed), null);
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void Add(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><td>{Escape(label)}</td><td>{value}</td></tr>");

    private static (int Tick, float V)[] ToPts(MovementProbe.MovementSample[] curve, Func<MovementProbe.MovementSample, float> v)
    {
        var pts = new (int, float)[curve.Length];
        for (int i = 0; i < curve.Length; i++) pts[i] = (curve[i].Tick, v(curve[i]));
        return pts;
    }

    private static void Chart(StringBuilder sb, string title, (int Tick, float V)[] a, (int Tick, float V)[]? b)
    {
        const int w = 300, h = 130;
        int xMax = 1; float yMin = 0f, yMax = 1f;
        void Fold((int Tick, float V)[] pts)
        {
            foreach (var p in pts)
            {
                if (p.Tick > xMax) xMax = p.Tick;
                if (p.V < yMin) yMin = p.V;
                if (p.V > yMax) yMax = p.V;
            }
        }
        Fold(a); if (b != null) Fold(b);
        if (yMax - yMin < 0.5f) yMax = yMin + 0.5f;
        float Y(float v) => h - 14f - ((v - yMin) / (yMax - yMin)) * (h - 24f);

        sb.AppendLine($"<div class=\"curve\"><h3>{title}</h3><svg viewBox=\"0 0 {w} {h}\">");
        sb.AppendLine($"<line x1=\"8\" y1=\"{Y(0f)}\" x2=\"{w - 8}\" y2=\"{Y(0f)}\"/><line x1=\"8\" y1=\"12\" x2=\"8\" y2=\"{h - 12}\"/>");
        sb.AppendLine($"<text x=\"6\" y=\"{Y(yMax) - 3}\" text-anchor=\"end\">{yMax:F1}</text>");
        sb.AppendLine($"<text x=\"6\" y=\"{Y(yMin) + 10}\" text-anchor=\"end\">{yMin:F1}</text>");
        sb.AppendLine($"<text x=\"{w - 10}\" y=\"{h - 3}\" text-anchor=\"end\">{xMax} ticks</text>");
        Polyline(sb, a, "#58a6ff");
        if (b != null) Polyline(sb, b, "#f0883e");
        sb.AppendLine("</svg></div>");

        void Polyline(StringBuilder sb, (int Tick, float V)[] pts, string color)
        {
            if (pts.Length == 0) return;
            var path = new StringBuilder();
            for (int i = 0; i < pts.Length; i++)
            {
                float x = 8f + (pts[i].Tick / (float)xMax) * (w - 16f);
                float y = h - 14f - ((pts[i].V - yMin) / (yMax - yMin)) * (h - 24f);
                path.Append(i == 0 ? $"M{x:F1} {y:F1}" : $" L{x:F1} {y:F1}");
            }
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>");
        }
    }

    // ── Comparison rows ───────────────────────────────────────────────────────

    private sealed record Row(string Label, string[] Values);

    private static List<Row> Rows(List<(CharacterDefinition Def, MovementProbe.CharacterMovement M)> all)
    {
        static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("F1", CultureInfo.InvariantCulture);
        static string F0(float v) => v.ToString("F0", CultureInfo.InvariantCulture);
        static string Note(MovementProbe.CharacterMovement m, float authored) =>
            $"<span class=\"note\">({F0(authored)})</span>";

        var rows = new List<Row>();
        void Row_(string label, Func<(CharacterDefinition Def, MovementProbe.CharacterMovement M), string> f) =>
            rows.Add(new Row(label, all.Select(f).ToArray()));

        Row_("Run max speed (m/s)", x => $"{F1(x.M.Run.MaxSpeed)} {Note(x.M, x.M.Authored.RunSpeed)}");
        Row_("Run time-to-max", x => x.M.Run.TimeToMaxTicks <= 2 ? "instant <span class=\"note\">(rush kick-off)</span>"
            : $"{x.M.Run.TimeToMaxTicks + 1} ticks");
        Row_("Dash duration (ticks)", x => $"{x.M.Dash.DurationTicks} {Note(x.M, x.M.Authored.DashDurationTicks)}");
        Row_("Dash distance (m)", x => F(x.M.Dash.TotalDistance));
        Row_("Dash actionable (tick)", x => $"{x.M.Dash.ActionableTick}");
        Row_("Jump apex (m)", x => F(x.M.Jump.ApexHeight));
        Row_("Jump time-to-apex (ticks)", x => $"{x.M.Jump.TimeToApexTicks}");
        Row_("Jump airtime (s)", x => $"{x.M.Jump.AirtimeTicks / 60f:F2}");
        Row_("Full-hop drift (m)", x => F(x.M.Jump.HorizontalDistance));
        Row_("Running jump distance (m)", x => F(x.M.RunningJump.HorizontalDistance));
        Row_("Double-jump apex (m)", x => F(x.M.DoubleJump.ApexHeight));
        Row_("Double-jump airtime (s)", x => $"{x.M.DoubleJump.AirtimeTicks / 60f:F2}");
        Row_("Air drift cap (m/s)", x => $"{F1(x.M.Jump.DriftSpeedMax)} {Note(x.M, x.M.Authored.AirSpeedMax)}");
        Row_("Fall max speed (m/s)", x => $"{F0(x.M.Fall.MaxFallSpeed)} {Note(x.M, x.M.Authored.MaxFallSpeed)}");
        Row_("Fall time-to-max (s)", x => $"{x.M.Fall.TimeToMaxFallTicks / 60f:F2}");
        Row_("Fall 50 m descent (s)", x => $"{x.M.Fall.DescentTicks / 60f:F2}");
        Row_("Fast fall (m/s)", x => $"{F0(x.M.Fall.FastFallSpeed)} {Note(x.M, x.M.Authored.FastFallSpeed)}");
        Row_("Fast-fall 50 m descent (s)", x => $"{x.M.Fall.FastFallDescentTicks / 60f:F2}");
        Row_("Stop time (s)", x => $"{x.M.Stop.StopTicks / 60f:F2}");
        Row_("Stop distance (m)", x => F(x.M.Stop.StopDistance));
        return rows;
    }

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
