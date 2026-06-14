@tool
extends Node

## Strip Hips root motion from all animations in the given AnimationPlayer.
## Run: attach to any node, set AnimPlayerPath, set Action, run scene.

@export var anim_player_path: NodePath
@export var do_strip: bool = false:
    set(v):
        if v and has_node(anim_player_path):
            strip_root_motion(get_node(anim_player_path) as AnimationPlayer)
            do_strip = false

func strip_root_motion(ap: AnimationPlayer) -> void:
    var fixed := 0
    for anim_name in ap.get_animation_list():
        var anim: Animation = ap.get_animation(anim_name)
        if anim == null:
            continue
        var modified := false
        for track_idx in range(anim.get_track_count()):
            var track_path: NodePath = anim.track_get_path(track_idx)
            var path_str := track_path.to_string()
            # Match Hips translation tracks (format: ".../Skeleton3D:mixamorig_Hips" with position type)
            if path_str.contains("mixamorig_Hips") and anim.track_get_type(track_idx) == Animation.TrackType.POSITION_3D:
                # Remove all position keyframes from Hips
                anim.track_remove_key_at_time(track_idx, 0.0)
                anim.remove_track(track_idx)
                modified = true
                track_idx -= 1  # re-check this index since tracks shifted
        if modified:
            fixed += 1
            print("  Fixed: ", anim_name)
    print("Stripped root motion from ", fixed, " animations")
