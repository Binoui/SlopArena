#nullable enable
using Godot;

/// <summary>
/// Animation controller — lightweight wrapper for model setup and node discovery.
/// The custom FSM (StateMachine.cs) now handles all animation state transitions.
/// Only retains model-scaffolding helpers (Setup, FindSkeleton, FindAnimationPlayer).
/// </summary>
public partial class AnimationController : Node
{
    private AnimationPlayer? _animPlayer;
    private Skeleton3D? _skeleton;
    private AnimationTree? _animTree;
    private static bool _tracksFixed = false;

    public void Setup(AnimationPlayer animPlayer, Skeleton3D? skeleton)
    {
        _animPlayer = animPlayer;
        _skeleton = skeleton;
        if (_animPlayer != null)
            _animPlayer.PlaybackDefaultBlendTime = 0.15f;
        if (_animPlayer != null && _skeleton != null)
            FixAnimationTracks();
    }

    public void SetupAnimationTree(AnimationTree animTree)
    {
        _animTree = animTree;
    }

    // ── INTERNAL ──

    private void FixAnimationTracks()
    {
        if (_animPlayer == null || _skeleton == null) return;

        _animPlayer.RootNode = _skeleton.GetPath();
        if (_tracksFixed) return;

        int fixedCount = 0, totalTracks = 0;

        foreach (var animName in _animPlayer.GetAnimationList())
        {
            var anim = _animPlayer.GetAnimation(animName);
            if (anim == null) continue;

            for (int i = 0; i < anim.GetTrackCount(); i++)
            {
                totalTracks++;
                var oldPath = anim.TrackGetPath(i);
                string pathStr = oldPath.ToString();
                int colonIdx = pathStr.LastIndexOf(':');
                if (colonIdx < 0) continue;
                string boneSubname = pathStr.Substring(colonIdx);
                var newPath = new NodePath(boneSubname);
                if (newPath != oldPath)
                {
                    anim.TrackSetPath(i, newPath);
                    fixedCount++;
                }
            }
        }

        _tracksFixed = true;
        GD.Print($"[AnimController] Fixed {fixedCount}/{totalTracks} tracks");
    }

    // ── SCENE HELPERS ──

    public Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D sk) return sk;
        foreach (var c in node.GetChildren())
        {
            var r = FindSkeleton(c);
            if (r != null) return r;
        }
        return null;
    }

    public AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var c in node.GetChildren())
        {
            var r = FindAnimationPlayer(c);
            if (r != null) return r;
        }
        return null;
    }
}
