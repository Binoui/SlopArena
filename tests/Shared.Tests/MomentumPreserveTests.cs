using System.Collections.Generic;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Momentum-preserve tests (ADR-0015, issue #115 / #103):
/// - Lunge velocity coasts through the attack and SURVIVES the move end into Idle,
///   where normal friction resumes. Nothing zeroes it mid-move or at EndAbility.
/// (The air-charge momentum-preserve cases rode the now-removed AirRMB.)
/// </summary>
public class MomentumPreserveTests
{
    private static readonly CharacterDefinition MankiDef = TestHelpers.MankiDef;
    private static readonly float MankiGroundPy = TestHelpers.MankiGroundPY;

    // ── Lunge momentum persists through the move and into Idle ──


}
