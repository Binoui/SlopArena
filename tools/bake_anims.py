import bpy

# Bake all actions to new rest pose
# Run this AFTER rotating armature + mesh and applying rest pose

armature = None
for obj in bpy.data.objects:
    if obj.type == 'ARMATURE':
        armature = obj
        break

if not armature:
    print("No armature found")
    exit()

bpy.context.view_layer.objects.active = armature
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT')

# Get all actions that belong to this armature
actions = set()
for track in armature.animation_data.nla_tracks if armature.animation_data else []:
    for strip in track.strips:
        if strip.action:
            actions.add(strip.action)

# Also check directly assigned action
if armature.animation_data and armature.animation_data.action:
    actions.add(armature.animation_data.action)

# Bake each action
for action in actions:
    if not action:
        continue
    bpy.context.object.animation_data.action = action
    # Bake to new action with visual keying (bakes transform into bone space)
    bpy.ops.nla.bake(
        frame_start=int(action.frame_range[0]),
        frame_end=int(action.frame_range[1]),
        step=1,
        only_selected=False,
        visual_keying=True,
        clear_constraints=False,
        clear_parents=False,
        use_current_action=True,
        bake_types={'POSE'}
    )
    print(f"Baked: {action.name}")

print("All actions baked! Export GLB with ✅ Include -> Actions")
