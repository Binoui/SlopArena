import bpy

armature = bpy.context.active_object
if armature and armature.type == 'ARMATURE':
    # Rename bones
    renamed = 0
    for bone in armature.data.bones:
        if "mixamorig:" in bone.name:
            old = bone.name
            bone.name = bone.name.replace("mixamorig:", "")
            print(f"  Bone: {old} -> {bone.name}")
            renamed += 1
    print(f"Renamed {renamed} bones")

    # Search ALL fcurves in ALL animations for mixamorig: references
    replaced = 0
    for action in bpy.data.actions:
        fcurves = getattr(action, 'fcurves', None)
        if fcurves is None:
            continue
        for fcu in fcurves:
            if "mixamorig:" in fcu.data_path:
                fcu.data_path = fcu.data_path.replace("mixamorig:", "")
                replaced += 1

    print(f"Updated {replaced} fcurves in all actions")
    print("Done! Export GLB now.")
else:
    print("Select an armature first")
