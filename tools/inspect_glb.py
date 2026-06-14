import struct, json, sys

def read_glb(path):
    with open(path, 'rb') as f:
        magic = f.read(4)
        assert magic == b'glTF', f'{path}: Not a GLB'
        f.read(8)  # version + length
        chunk_len = struct.unpack('<I', f.read(4))[0]
        chunk_type = f.read(4)
        return json.loads(f.read(chunk_len).decode('utf-8'))

data = read_glb('assets/characters/manki/manki.glb')

# ── 1. Scene structure ──
print("=== 1. SCENE ROOT NODES ===")
for idx in data['scenes'][0]['nodes']:
    n = data['nodes'][idx]
    print(f'  [{idx}] "{n["name"]}"  scale={n.get("scale", [1,1,1])}')

print()

# ── 2. Armature transform ──
print("=== 2. ARMATURE NODE ===")
for i, n in enumerate(data['nodes']):
    if n['name'] == 'Armature':
        print(f'  scale = {n.get("scale", [1,1,1])}')
        print(f'  translation = {n.get("translation", [0,0,0])}')
        print(f'  children = {n.get("children", [])}')

print()

# ── 3. Mesh node + bounding box ──
print("=== 3. MESH ===")
for i, n in enumerate(data['nodes']):
    if 'mesh' in n:
        print(f'  Node: "{n["name"]}"')
        print(f'  scale = {n.get("scale", [1,1,1])}')
        print(f'  translation = {n.get("translation", [0,0,0])}')
for m in data['meshes']:
    for prim in m.get('primitives', []):
        acc_idx = prim['attributes']['POSITION']
        acc = data['accessors'][acc_idx]
        print(f'  Vertex range: min={acc["min"]} max={acc["max"]}')
        size = [acc['max'][i] - acc['min'][i] for i in range(3)]
        print(f'  Size: {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f}')

print()

# ── 4. Key bone translations ──
print("=== 4. KEY BONE POSITIONS (relative to parent) ===")
key_bones = ['mixamorig:Hips', 'mixamorig:Spine', 'mixamorig:Spine1', 'mixamorig:Spine2',
             'mixamorig:Neck', 'mixamorig:Head', 'mixamorig:HeadTop_End',
             'mixamorig:LeftFoot', 'mixamorig:RightFoot',
             'mixamorig:LeftHand', 'mixamorig:RightHand']
for i, n in enumerate(data['nodes']):
    name = n['name']
    if name in key_bones:
        t = n.get('translation', [0,0,0])
        print(f'  {name:30s} ({t[0]:10.4f}, {t[1]:10.4f}, {t[2]:10.4f})')

# ── 5. Height estimate based on bones ──
print()
print("=== 5. HEIGHT ESTIMATE ===")
# Find HeadTop_End (highest) and lowest foot
heights = {}
for i, n in enumerate(data['nodes']):
    name = n['name']
    if 'Foot' in name or 'Toe' in name or 'Head' in name or 'Hips' in name:
        t = n.get('translation', [0,0,0])
        heights[name] = t[1]
if heights:
    print(f'  Highest Y: {max(heights.values()):.2f} ({max(heights, key=heights.get)})')
    print(f'  Lowest Y:  {min(heights.values()):.2f} ({min(heights, key=heights.get)})')
    print(f'  Bone height range: {max(heights.values()) - min(heights.values()):.2f}')

# ── 6. Sample animation ──
print()
print("=== 6. IDLE ANIMATION (first 2 channels) ===")
for anim in data['animations']:
    if anim['name'] != 'idle':
        continue
    for ci, ch in enumerate(anim['channels'][:3]):
        node_idx = ch['target']['node']
        path = ch['target']['path']
        sampler = anim['samplers'][ch['sampler']]
        acc = data['accessors'][sampler['output']]
        node_name = data['nodes'][node_idx]['name']
        print(f'  channel {ci}: node[{node_idx}] "{node_name}".{path}')
        print(f'    {acc["count"]} samples, type={acc["type"]}, componentType={acc["componentType"]}')
        # Read first value
        bv = data['bufferViews'][acc['bufferView']]
        buf = data['buffers'][bv['buffer']]
        with open('assets/characters/manki/manki.glb', 'rb') as f:
            f.seek(buf['byteOffset'] + bv['byteOffset'] + acc.get('byteOffset', 0))
            if acc['type'] == 'VEC3':
                vals = [struct.unpack('<fff', f.read(12)) for _ in range(3)]
                print(f'    first 3 frames: {[(round(v[0],4), round(v[1],4), round(v[2],4)) for v in vals]}')
            else:
                vals = [struct.unpack('<ffff', f.read(16)) for _ in range(3)]
                print(f'    first 3 frames: {[(round(v[0],4), round(v[1],4), round(v[2],4), round(v[3],4)) for v in vals]}')

# ── 7. Animation list ──
print()
print("=== 7. ALL ANIMATIONS ===")
for anim in data['animations']:
    print(f'  {anim["name"]} ({len(anim["channels"])} channels)')
