@tool
extends Node

## Bake Skeleton Tool
## 
## Select a Skeleton3D + AnimationPlayer node, run this script to export
## bone positions per frame as a .bin file for the server simulation.
##
## Usage: Attach to any node, set SkeletonPath + AnimPlayerPath, call bake().

@export var skeleton_path: NodePath
@export var anim_player_path: NodePath
@export var bone_names: PackedStringArray = []
@export var output_path: String = "res://data/manki_skeleton.bin"

func bake() -> void:
	var skel: Skeleton3D = get_node(skeleton_path)
	var anim_player: AnimationPlayer = get_node(anim_player_path)
	
	if skel == null or anim_player == null:
		push_error("BakeSkeleton: Skeleton or AnimationPlayer not found")
		return
	
	if bone_names.size() == 0:
		# Default: use all bones in the skeleton (might be too many)
		for i in skel.get_bone_count():
			bone_names.append(skel.get_bone_name(i))
	
	# Build bone index map
	var bone_indices: Array[int] = []
	for name in bone_names:
		var idx := skel.find_bone(name)
		if idx >= 0:
			bone_indices.append(idx)
		else:
			push_warning("BakeSkeleton: Bone not found: ", name)
	
	if bone_indices.size() == 0:
		push_error("BakeSkeleton: No bones found")
		return
	
	print("BakeSkeleton: ", bone_indices.size(), " bones, ", anim_player.get_animation_list().size(), " animations")
	
	# Open file for writing
	var f := FileAccess.open(output_path, FileAccess.WRITE)
	if f == null:
		push_error("BakeSkeleton: Cannot open output file: ", output_path)
		return
	
	var bone_count := bone_indices.size()
	var anim_list := anim_player.get_animation_list()
	
	# Header: magic + version + boneCount + animCount
	f.store_32(0x4C454B53)  # "SKEL"
	f.store_32(1)           # version
	f.store_32(bone_count)
	f.store_32(anim_list.size())
	
	# Bone names
	for name in bone_names:
		var bytes := name.to_utf8_buffer()
		f.store_32(bytes.size())
		f.store_buffer(bytes)
	
	# Animation data
	const FPS := 60.0
	
	for anim_name in anim_list:
		var anim: Animation = anim_player.get_animation(anim_name)
		var duration := anim.length
		var frame_count := ceili(duration * FPS) + 1
		
		print("BakeSkeleton: Anim '", anim_name, "' duration=", duration, "s frames=", frame_count)
		
		# Write animation header
		var name_bytes := anim_name.to_utf8_buffer()
		f.store_32(name_bytes.size())
		f.store_buffer(name_bytes)
		f.store_32(frame_count)
		
		# Sample each frame
		for frame in range(frame_count):
			var time := frame / FPS
			if time > duration:
				time = duration
			
			anim_player.seek(time, true)
			skel.force_update_all_dirty_transforms()
			
			for bi in range(bone_indices.size()):
				var bone_idx := bone_indices[bi]
				var pose := skel.get_bone_global_pose(bone_idx)
				f.store_float(pose.origin.x)
				f.store_float(pose.origin.y)
				f.store_float(pose.origin.z)
			
		# Progress
		print("BakeSkeleton:   frame ", frame_count, " written")
	
	f.close()
	print("BakeSkeleton: Done -> ", output_path, " (", FileAccess.get_open_error(), ")")
	print("BakeSkeleton: Check the output file for errors")
