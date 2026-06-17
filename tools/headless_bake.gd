@tool
extends SceneTree

## Headless bake: loads a character GLB and bakes bone positions per frame.
## Usage: godot --headless --script tools/headless_bake.gd --path . -- <char1> [char2 ...]
##   e.g. godot --headless --script tools/headless_bake.gd --path . -- manki bunny
##   Each character must have assets/characters/<name>/<name>.glb

const BONE_NAMES := [
    "mixamorig_Head",
    "mixamorig_Spine2",
    "mixamorig_Hips",
    "mixamorig_RightHand",
    "mixamorig_LeftHand",
    "mixamorig_RightFoot",
    "mixamorig_LeftFoot",
    "mixamorig_RightToeBase",
    "mixamorig_LeftToeBase",
    "mixamorig_RightToe_End",
    "mixamorig_LeftToe_End",
]

const FPS := 60.0

func _init():
    # Parse character names from command line
    var user_args := OS.get_cmdline_user_args()
    var chars: Array[String] = []
    for a in user_args:
        var trimmed := a.strip_edges()
        if trimmed.length() > 0:
            chars.append(trimmed)

    if chars.is_empty():
        chars = ["manki", "bunny"]

    for char_name in chars:
        var result := await bake_character(char_name)
        if result != OK:
            printerr("FAILED: ", char_name)
            quit(1)

    quit(0)


func bake_character(char_name: String) -> int:
    var glb_path := "res://assets/characters/%s/%s.glb" % [char_name, char_name]
    var output_path := "res://data/%s_skeleton.bin" % [char_name]

    print("\n=== %s: Loading GLB..." % char_name)
    # Need a frame to initialize skeleton transforms
    await process_frame

    # Load GLB directly
    var glb := load(glb_path) as PackedScene
    if glb == null:
        printerr("Cannot load: ", glb_path)
        return ERR_FILE_CANT_OPEN

    var instance := glb.instantiate() as Node3D
    root.add_child(instance)

    # Wait another frame for the skeleton to fully initialize
    await process_frame

    # Find Skeleton3D + AnimationPlayer
    var skel := find_child_of_type(instance, "Skeleton3D") as Skeleton3D
    var anim_player := find_child_of_type(instance, "AnimationPlayer") as AnimationPlayer

    if skel == null:
        printerr("No Skeleton3D found")
        return ERR_UNCONFIGURED
    if anim_player == null:
        printerr("No AnimationPlayer found")
        return ERR_UNCONFIGURED

    print("  Found Skeleton3D (%d bones) + AnimationPlayer" % skel.get_bone_count())

    # Build bone index map
    var bone_indices: Array[int] = []
    for name in BONE_NAMES:
        var idx := skel.find_bone(name)
        if idx >= 0:
            bone_indices.append(idx)
        else:
            printerr("  Bone not found: ", name)

    if bone_indices.size() == 0:
        printerr("  No bones matched")
        return ERR_UNCONFIGURED

    var bone_count := bone_indices.size()
    var anim_list := anim_player.get_animation_list()
    print("  %d bones, %d animations" % [bone_count, anim_list.size()])

    # Open file
    var f := FileAccess.open(output_path, FileAccess.WRITE)
    if f == null:
        printerr("  Cannot open output: ", output_path)
        return ERR_FILE_CANT_WRITE

    # Header
    f.store_32(0x4C454B53)  # "SKEL"
    f.store_32(1)           # version
    f.store_32(bone_count)
    f.store_32(anim_list.size())

    # Bone names (using Godot's underscore format)
    for bi in bone_indices:
        var real_name := skel.get_bone_name(bi)
        var bytes := real_name.to_utf8_buffer()
        f.store_32(bytes.size())
        f.store_buffer(bytes)

    var hips_idx := skel.find_bone("mixamorig_Hips")

    # Step 1: sample the rest pose Hips transform (stopped player).
    # This gives armature-local space: X=right, Y=up, Z=depth — what the server expects.
    anim_player.stop()
    await process_frame
    var rest_hips_inv := Transform3D.IDENTITY
    if hips_idx >= 0:
        rest_hips_inv = skel.get_bone_global_pose(hips_idx).affine_inverse()

    # Step 2: compute the Y normalization offset from idle frame 0.
    # We measure the lowest bone in armature-local space at idle frame 0
    # so the lowest contact point = 0 in baked space for every character.
    # This must be idle (not rest pose) because some characters stand differently
    # in their idle pose than in their rest pose (e.g. digitigrade animals).
    anim_player.play("idle")
    anim_player.seek(0.0, true)
    await process_frame
    var ground_offset_y := INF
    var ground_bone := ""
    for bi in bone_indices:
        var local_y := (rest_hips_inv * skel.get_bone_global_pose(bi).origin).y
        if local_y < ground_offset_y:
            ground_offset_y = local_y
            ground_bone = skel.get_bone_name(bi)
    print("  Ground ref: local_y=%.4f  bone=%s" % [ground_offset_y, ground_bone])

    # Each animation
    for anim_name in anim_list:
        var anim: Animation = anim_player.get_animation(anim_name)
        var duration := anim.length
        var frame_count := ceili(duration * FPS) + 1

        print("  '%s' -> %d frames" % [anim_name, frame_count])

        var name_bytes := anim_name.to_utf8_buffer()
        f.store_32(name_bytes.size())
        f.store_buffer(name_bytes)
        f.store_32(frame_count)

        anim_player.stop()
        anim_player.play(anim_name)

        for frame in range(frame_count):
            var time := mini(frame, int(duration * FPS)) / FPS
            anim_player.seek(time, true)

            # Wait a frame to ensure skeleton updates
            await process_frame

            for bone_idx in bone_indices:
                var bone_world := skel.get_bone_global_pose(bone_idx).origin
                var local_pos := rest_hips_inv * bone_world
                f.store_float(local_pos.x)
                f.store_float(local_pos.y - ground_offset_y)
                f.store_float(local_pos.z)

    f.close()
    print("  ✓ Written to ", output_path)
    instance.queue_free()
    return OK


func find_child_of_type(node, type_name: String) -> Node:
    if node.get_class() == type_name:
        return node
    for child in node.get_children():
        var result = find_child_of_type(child, type_name)
        if result != null:
            return result
    return null
