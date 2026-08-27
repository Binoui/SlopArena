using Unity.Pipeline.Commands;

public static class SlopArenaCharacterCommands
{
    [CliCommand(
        "sloparena.character.inspect",
        "Inspect a Character Package source, bindings, cooked status, hashes, and canonical slots.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageInspectionResult Inspect(
        [CliArg("target", "Package ID or package root inside Assets/CharacterPackages.", Required = true)] string target)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(target);

    [CliCommand(
        "sloparena.character.cook",
        "Validate and cook a Character Package through the canonical Unity asset pipeline.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageCookResult Cook(
        [CliArg("target", "Package ID or package root inside Assets/CharacterPackages.", Required = true)] string target)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(target);
}
