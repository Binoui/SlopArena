// Copyright SlopArena Contributors. MIT License.

using UnrealBuildTool;
using System.Collections.Generic;

public class SlopArenaTarget : TargetRules
{
	public SlopArenaTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V6;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("SlopArena");
	}
}
