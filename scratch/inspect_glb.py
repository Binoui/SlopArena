import json
import struct
import sys

def inspect_glb(filepath):
    print(f"Opening GLB: {filepath}")
    with open(filepath, 'rb') as f:
        # Read header
        magic = f.read(4)
        if magic != b'glTF':
            print("Not a valid GLB file.")
            return
        version, = struct.unpack('<I', f.read(4))
        length, = struct.unpack('<I', f.read(4))
        print(f"GLTF Version: {version}, Length: {length} bytes")

        # Read chunk 0 (JSON)
        chunk_length, = struct.unpack('<I', f.read(4))
        chunk_type = f.read(4)
        if chunk_type != b'JSON':
            print("First chunk is not JSON.")
            return
        
        json_data = f.read(chunk_length).decode('utf-8')
        gltf = json.loads(json_data)
        
        nodes = gltf.get('nodes', [])
        meshes = gltf.get('meshes', [])
        
        print(f"Found {len(nodes)} nodes and {len(meshes)} meshes.")
        
        # Display main nodes
        for i, node in enumerate(nodes):
            name = node.get('name', f"Node_{i}")
            mesh_idx = node.get('mesh')
            mesh_name = ""
            if mesh_idx is not None and mesh_idx < len(meshes):
                mesh_name = meshes[mesh_idx].get('name', f"Mesh_{mesh_idx}")
            
            translation = node.get('translation', [0, 0, 0])
            rotation = node.get('rotation', [0, 0, 0, 1])
            scale = node.get('scale', [1, 1, 1])
            
            # Print nodes that have mesh or interesting names
            if mesh_idx is not None or any(kw in name.lower() for kw in ['ramp', 'pipe', 'wall', 'floor', 'box', 'rail']):
                print(f"Node '{name}': Mesh='{mesh_name}', Pos={translation}, Scale={scale}")

if __name__ == '__main__':
    inspect_glb('/home/binoui/Documents/projects/MoveBox/thps_warehouse.glb')
