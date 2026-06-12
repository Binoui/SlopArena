#!/usr/bin/env python3
"""Fix GLB: 180° around Z (Blender) = 180° around Y (glTF).
Reads/writes raw GLB directly (no pygltflib accessor issues)."""

import sys, json, struct, copy
import numpy as np

DTYPE = {5126:'f4', 5123:'<u2', 5122:'<i2', 5125:'<u4', 5120:'i1', 5121:'u1'}
TYPESZ = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}

def ry180(v): return np.array([-v[0], v[1], -v[2]], dtype='f4')
def qy180(q):
    w,x,y,z = q
    return np.array([-y, -z, w, x], dtype='f4')
def m4y180(m):
    m16 = np.array(m, dtype='f4').reshape(4,4,order='F')
    m16[0,:], m16[2,:] = -m16[0,:].copy(), -m16[2,:].copy()
    return m16.reshape(16, order='F')

# Read GLB
with open(sys.argv[1], 'rb') as f:
    raw = bytearray(f.read())

magic, ver, size = struct.unpack_from('<III', raw, 0)
p = 12
while p < len(raw):
    cl = struct.unpack_from('<I', raw, p)[0]
    ct = struct.unpack_from('<I', raw, p+4)[0]
    cd = raw[p+8:p+8+cl]
    if ct == 0x4E4F534A:
        scene = json.loads(cd.decode('utf-8'))
        buf_off = p + 8 + cl
        if buf_off % 4 != 0: buf_off += 4 - (buf_off % 4)
        buf = bytearray(raw[buf_off:buf_off + scene['buffers'][0]['byteLength']])
        break
    p += 8 + cl

def read_acc(idx):
    a = scene['accessors'][idx]
    bv = scene['bufferViews'][a['bufferView']]
    off = bv.get('byteOffset',0) + a.get('byteOffset',0)
    stride = bv.get('byteStride', 0)
    cols = TYPESZ[a['type']]
    dt = np.dtype(DTYPE[a['componentType']])
    isz = dt.itemsize
    rstride = stride if stride > 0 else cols * isz
    arr = np.frombuffer(buf, dt, a['count'] * cols, off)
    if stride > cols * isz:
        # Interleaved: extract every stride/isz elements
        result = np.zeros((a['count'], cols), dt)
        for k in range(a['count']):
            for c in range(cols):
                result[k,c] = arr[k * (rstride // isz) + c]
        return result
    return arr.copy().reshape(a['count'], cols)

def write_acc(idx, data):
    a = scene['accessors'][idx]
    bv = scene['bufferViews'][a['bufferView']]
    off = bv.get('byteOffset',0) + a.get('byteOffset',0)
    raw = data.astype(np.dtype(DTYPE[a['componentType']])).tobytes()
    buf[off:off+len(raw)] = raw

arm_idx = next(i for i,n in enumerate(scene['nodes']) if n.get('name','').lower().startswith('armature'))
print(f"Armature node: [{arm_idx}]")

# Collect skeleton nodes
skel = set()
def walk(i):
    skel.add(i)
    for c in scene['nodes'][i].get('children',[]): walk(c)
walk(arm_idx)
print(f"Skeleton tree: {len(skel)} nodes")

# 1. Anims
for anim in scene.get('animations',[]):
    for ch in anim.get('channels',[]):
        if ch['target']['node'] not in skel: continue
        s = anim['samplers'][ch['sampler']]
        d = read_acc(s['output'])
        if d.size == 0: continue
        p = ch['target']['path']
        if p == 'translation':
            for k in range(len(d)): d[k] = ry180(d[k])
        elif p == 'rotation':
            for k in range(len(d)): d[k] = qy180(d[k])
        write_acc(s['output'], d)
    print(f"  ✓ {anim.get('name','?')}")

# 2. Node rest poses
for i in skel:
    if i == arm_idx: continue
    n = scene['nodes'][i]
    if 'translation' in n and any(abs(v)>.001 for v in n['translation']):
        n['translation'] = ry180(np.array(n['translation'])).tolist()
    if 'rotation' in n and any(abs(v)>.001 for v in n['rotation']):
        n['rotation'] = qy180(np.array(n['rotation'])).tolist()

# 3. IBM
for skin in scene.get('skins',[]):
    ibm_idx = skin.get('inverseBindMatrices')
    if ibm_idx is not None:
        ibm = read_acc(ibm_idx)
        for k in range(len(ibm)): ibm[k] = m4y180(ibm[k])
        write_acc(ibm_idx, ibm)

# 4. Armature identity
an = scene['nodes'][arm_idx]
an['rotation'] = [0,0,0,1]
an['translation'] = [0,0,0]
an['scale'] = [1,1,1]

# 5. Reparent children to scene root
kids = list(an.get('children',[]))
for si, s in enumerate(scene.get('scenes',[])):
    if s.get('nodes',[]):
        for c in kids:
            if c not in s['nodes']: s['nodes'].append(c)
        break
an['children'] = []

# Update binary blob size if needed and write
old_buf_len = scene['buffers'][0]['byteLength']
new_buf_len = len(buf)
if new_buf_len != old_buf_len:
    scene['buffers'][0]['byteLength'] = new_buf_len

# Rebuild JSON chunk
json_bytes = json.dumps(scene, separators=(',',':')).encode()
# Pad to 4 bytes
while len(json_bytes) % 4 != 0: json_bytes += b' '

# Rebuild binary blob
# Pad to 4 bytes
while len(buf) % 4 != 0: buf += b'\x00'

# Write output
outpath = sys.argv[2] if len(sys.argv)>2 else sys.argv[1]
with open(outpath, 'wb') as f:
    f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(json_bytes) + 8 + len(buf)))
    f.write(struct.pack('<II', len(json_bytes), 0x4E4F534A))
    f.write(json_bytes)
    f.write(struct.pack('<II', len(buf), 0x004E4249))
    f.write(buf)

print(f"\nSaved {outpath}")
