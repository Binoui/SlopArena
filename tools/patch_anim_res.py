#!/usr/bin/env python3
"""
Patch mixamo_com.res bone paths to match Narodin's skeleton.
- Replace "mixamorig_" with "mixamorig:" 
- Remove "Armature/Skeleton3D/" prefix
"""
import os

res_path = os.path.expanduser("~/Documents/projects/SlopArena/animation_source/mixamo_com.res")
out_path = os.path.expanduser("~/Documents/projects/SlopArena/animations/run.res")

with open(res_path, 'rb') as f:
    data = f.read()

# Replace mixamorig_ with mixamorig:
data = data.replace(b"mixamorig_", b"mixamorig:")

# Remove Armature/Skeleton3D/ from paths
data = data.replace(b"Armature/Skeleton3D/", b"")

# Remove Root: prefix (Root is a bone, not the skeleton node)
# After stripping Armature/Skeleton3D, paths are like Root:mixamorig:Hips
# The skeleton node is Armature, so we need Armature:mixamorig:Hips
data = data.replace(b"Root:mixamorig:", b"Armature:mixamorig:")
# Also handle paths where Root is followed by /
data = data.replace(b"Root/mixamorig:", b"Armature/mixamorig:")

with open(out_path, 'wb') as f:
    f.write(data)

print(f"Written to {out_path}")
print(f"Size: {len(data)} bytes")
print("\nDelete mixamo_com.res.import and restart Godot")
