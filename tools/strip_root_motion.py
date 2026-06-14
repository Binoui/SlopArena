import bpy

# Strip Hips root motion from all animations in the GLB.
# Open bunny.glb in Blender, run this script, then re-export.

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

fixed_count = 0

# Strip from actions first
for action in bpy.data.actions:
    if not action or not action.fcurves:
        continue
    modified = False
    fcurves_to_remove = []
    for fcurve in action.fcurves:
        data_path = fcurve.data_path
        if 'mixamorig:Hips' in data_path and 'location' in data_path:
            fcurves_to_remove.append(fcurve)
    for fcurve in fcurves_to_remove:
        action.fcurves.remove(fcurve)
        modified = True
    if modified:
        print(f"Fixed action: {action.name}")
        fixed_count += 1

# Also strip from NLA tracks (GLB import puts anims here)
if armature.animation_data and armature.animation_data.nla_tracks:
    for track in armature.animation_data.nla_tracks:
        for strip in track.strips:
            if strip.action:
                action = strip.action
                modified = False
                fcurves_to_remove = []
                for fcurve in action.fcurves:
                    data_path = fcurve.data_path
                    if 'mixamorig:Hips' in data_path and 'location' in data_path:
                        fcurves_to_remove.append(fcurve)
                for fcurve in fcurves_to_remove:
                    action.fcurves.remove(fcurve)
                    modified = True
                if modified:
                    print(f"Fixed NLA strip: {strip.name}")
                    fixed_count += 1

print(f"Done! Fixed {fixed_count} animation groups.")
print("Now export: File → Export → glTF 2.0 (.glb)")
print("  ✅ Include → Actions")
print("  ✅ Include → NLA Tracks (as stripped actions) + NLA Tracks as individual animations")
