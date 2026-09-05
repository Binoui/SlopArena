using Unity.Pipeline.Commands;

public static class SlopArenaStageCommands
{
    [CliCommand(
        "sloparena.stage.bake",
        "Validate and bake a fixed authoritative stage source scene.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/stage" })]
    public static SlopArenaStageBakeResult Bake(
        [CliArg("stage", "Immutable lowercase snake_case stage key.", Required = true)] string stage)
        => new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bake(stage);

    [CliCommand(
        "sloparena.stage.inspect",
        "Inspect the authoritative stage source, baked arena, and cosmetic prefab.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/stage" })]
    public static SlopArenaStageInspectionResult Inspect(
        [CliArg("stage", "Immutable lowercase snake_case stage key.", Required = true)] string stage,
        [CliArg("output", "Repository-relative inspection JSON path under .stage-authoring-cache.", Required = true)] string output)
        => new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(stage, output);

    [CliCommand(
        "sloparena.stage.design-capture",
        "Render stage design views (four yaw quarter-turns plus top) of the current cosmetic prefab with kill-plane overlay, in edit mode.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/stage" })]
    public static SlopArenaStageDesignCaptureResult DesignCapture(
        [CliArg("stage", "Immutable lowercase snake_case stage key.", Required = true)] string stage,
        [CliArg("output", "Repository-relative output directory. Defaults to .stage-authoring-cache/<stage>/design.", Required = false)] string output = null)
        => new SlopArenaStageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).DesignCapture(stage, output);
}
