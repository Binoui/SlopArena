// Copyright SlopArena Contributors. MIT License.

using UnrealBuildTool;
using System.Collections.Generic;

public class SlopArenaEditorTarget : TargetRules
{
	public SlopArenaEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.V6;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("SlopArena");
	}
}
