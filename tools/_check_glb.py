#!/usr/bin/env python3
import struct, json, sys

with open('assets/characters/bunny/bunny.glb', 'rb') as f:
    magic = f.read(4)
    assert magic == b'glTF', 'Not a GLB'
    f.read(8)
    chunk_len = struct.unpack('<I', f.read(4))[0]
    chunk_type = f.read(4)
    data = json.loads(f.read(chunk_len).decode('utf-8'))

print('=== ANIMATIONS IN BUNNY.GLB ===')
for anim in data.get('animations', []):
    print(f'  "{anim["name"]}"  ({len(anim["channels"])} channels)')

print()
print('=== NODES (first 30) ===')
for n in data['nodes'][:30]:
    print(f'  "{n["name"]}"')
