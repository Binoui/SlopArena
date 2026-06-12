@tool
extends SceneTree

## Headless bake: loads manki.glb directly, bakes bone positions per frame.
## Run: godot --headless --script tools/headless_bake.gd --path .

const BONE_NAMES := [
    "mixamorig_Head",
    "mixamorig_Spine2",
    "mixamorig_Hips",
    "mixamorig_RightHand",
    "mixamorig_LeftHand",
    "mixamorig_RightFoot",
    "mixamorig_LeftFoot",
]

const FPS := 60.0
const OUTPUT_PATH := "res://data/manki_skeleton.bin"
const GLB_PATH := "res://assets/characters/manki/manki.glb"

func _init():
    # Need a frame to initialize skeleton transforms
    await process_frame

    print("Headless BakeSkeleton v3: Loading GLB...")

    # Load GLB directly
    var glb := load(GLB_PATH) as PackedScene
    if glb == null:
        printerr("Cannot load: ", GLB_PATH)
        quit(1)
        return

    var instance := glb.instantiate() as Node3D
    root.add_child(instance)

    # Wait another frame for the skeleton to fully initialize
    await process_frame

    # Find Skeleton3D + AnimationPlayer
    var skel := find_child_of_type(instance, "Skeleton3D") as Skeleton3D
    var anim_player := find_child_of_type(instance, "AnimationPlayer") as AnimationPlayer

    if skel == null:
        printerr("No Skeleton3D found")
        quit(1)
        return
    if anim_player == null:
        printerr("No AnimationPlayer found")
        quit(1)
        return

    print("Found Skeleton3D (%d bones) + AnimationPlayer" % skel.get_bone_count())
    print("Bones: ", skel.get_bone_count())

    # Build bone index map
    var bone_indices: Array[int] = []
    for name in BONE_NAMES:
        var idx := skel.find_bone(name)
        if idx >= 0:
            bone_indices.append(idx)
        else:
            printerr("Bone not found: ", name)

    if bone_indices.size() == 0:
        printerr("No bones matched")
        quit(1)
        return

    var bone_count := bone_indices.size()
    var anim_list := anim_player.get_animation_list()
    print("%d bones, %d animations" % [bone_count, anim_list.size()])

    # Quick test: sample frame 0 of "idle" to see if bone positions change with animation
    anim_player.stop()
    anim_player.play("idle")
    anim_player.seek(0.0, true)
    await process_frame
    var test_head := skel.get_bone_global_pose(bone_indices[0])
    var test_hips := skel.get_bone_global_pose(skel.find_bone("mixamorig_Hips"))
    print("Test idle frame 0: Head=(%.4f, %.4f, %.4f) Hips=(%.4f, %.4f, %.4f)" % [
        test_head.origin.x, test_head.origin.y, test_head.origin.z,
        test_hips.origin.x, test_hips.origin.y, test_hips.origin.z
    ])

    anim_player.play("backflip")
    anim_player.seek(0.5, true)
    await process_frame
    test_head = skel.get_bone_global_pose(bone_indices[0])
    test_hips = skel.get_bone_global_pose(skel.find_bone("mixamorig_Hips"))
    print("Test backflip frame 0.5s: Head=(%.4f, %.4f, %.4f) Hips=(%.4f, %.4f, %.4f)" % [
        test_head.origin.x, test_head.origin.y, test_head.origin.z,
        test_hips.origin.x, test_hips.origin.y, test_hips.origin.z
    ])

    # Open file
    var f := FileAccess.open(OUTPUT_PATH, FileAccess.WRITE)
    if f == null:
        printerr("Cannot open output: ", OUTPUT_PATH)
        quit(1)
        return

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

    # Each animation
    for anim_name in anim_list:
        var anim: Animation = anim_player.get_animation(anim_name)
        var duration := anim.length
        var frame_count := ceili(duration * FPS) + 1

        print("  Anim '%s'" % anim_name)

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

            # Transform bones into Hips' local coordinate system using AffineInverse.
            # This gives correct positions in character-local space:
            #   X = local right, Y = local up, Z = local forward
            # so the runtime rotation formula (FacingYaw) works correctly.
            var hips_inv := Transform3D.IDENTITY
            if hips_idx >= 0:
                hips_inv = skel.get_bone_global_pose(hips_idx).affine_inverse()

            for bone_idx in bone_indices:
                var pose := skel.get_bone_global_pose(bone_idx)
                var local_pos := hips_inv * pose.origin
                f.store_float(local_pos.x)
                f.store_float(local_pos.y)
                f.store_float(local_pos.z)

    f.close()
    print("Done! Written to ", OUTPUT_PATH)
    quit(0)


func find_child_of_type(node, type_name: String) -> Node:
    """Recursively find a child node of the given type."""
    if node.get_class() == type_name:
        return node
    for child in node.get_children():
        var result = find_child_of_type(child, type_name)
        if result != null:
            return result
    return null
