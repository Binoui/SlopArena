#!/usr/bin/env python3
"""Read and verify the baked skeleton .bin file."""
import struct
import sys

def read_bin(path):
    with open(path, 'rb') as f:
        data = f.read()

    print(f'File size: {len(data)} bytes ({len(data)/1024:.0f}KB)\n')

    pos = 0
    magic = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4
    version = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4
    bone_count = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4
    anim_count = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4

    print(f'Magic=0x{magic:08X} ({"SKEL" if magic == 0x4C454B53 else "BAD!"})')
    print(f'Version={version}, Bones={bone_count}, Anims={anim_count}\n')

    # Bone names
    bone_names = []
    for i in range(bone_count):
        name_len = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4
        name = data[pos:pos+name_len].decode('utf-8')
        pos += name_len
        bone_names.append(name)
    print(f'Bones ({bone_count}): {bone_names}\n')

    total_frames = 0
    # Read all animations
    for a in range(anim_count):
        name_len = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4
        anim_name = data[pos:pos+name_len].decode('utf-8')
        pos += name_len
        frame_count = struct.unpack('<I', data[pos:pos+4])[0]; pos += 4
        total_frames += frame_count

        stride = bone_count * 3
        frames_data = []
        for f_idx in range(frame_count):
            frame = []
            for b_idx in range(bone_count):
                fx = struct.unpack('<f', data[pos:pos+4])[0]; pos += 4
                fy = struct.unpack('<f', data[pos:pos+4])[0]; pos += 4
                fz = struct.unpack('<f', data[pos:pos+4])[0]; pos += 4
                frame.append((fx, fy, fz))
            frames_data.append(frame)

        # First frame sample
        head_f0 = frames_data[0][0]  # Head at frame 0
        hips_f0 = frames_data[0][2]      # Hips at frame 0 (bone index 2)
        head_f_last = frames_data[-1][0]

        # Check if Hips is normalized (should be ~0,0,0 after bake tool subtraction)
        hips_mag = (hips_f0[0]**2 + hips_f0[1]**2 + hips_f0[2]**2)**0.5
        hips_ok = hips_mag < 0.01

        # Movement frame 0→1
        if frame_count > 1:
            dx = frames_data[1][0][0] - frames_data[0][0][0]
            dy = frames_data[1][0][1] - frames_data[0][0][1]
            dz = frames_data[1][0][2] - frames_data[0][0][2]
            mov_01 = (dx*dx + dy*dy + dz*dz)**0.5
        else:
            mov_01 = 0

        # Head Y range across all frames
        head_y_vals = [f[0][1] for f in frames_data]

        # Hips position across frames (should stay ~0,0,0)
        hips_x_all = [f[2][0] for f in frames_data]
        hips_z_all = [f[2][2] for f in frames_data]

        hips_norm_str = "OK" if hips_ok else "NOT_ZERO"
        print(f'{anim_name:>20s}: {frame_count:3d} frames  '
              f'Head Y=[{min(head_y_vals):5.2f}, {max(head_y_vals):5.2f}]  '
              f'Hips_norm={hips_norm_str}  '
              f'HeadΔ01={mov_01:.4f}m  '
              f'HipsX=[{min(hips_x_all):.3f},{max(hips_x_all):.3f}]')

    print(f'\nTotal frames across all anims: {total_frames}')
    remaining = len(data) - pos
    print(f'Remaining bytes: {remaining} (should be 0)')
    print('✅ File is valid' if remaining == 0 else '⚠️  Trailing data!')

if __name__ == '__main__':
    path = sys.argv[1] if len(sys.argv) > 1 else 'data/manki_skeleton.bin'
    read_bin(path)
