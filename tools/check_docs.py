#!/usr/bin/env python3
"""Check doc consistency with source code.
Run: python3 tools/check_docs.py
Exits with 1 if any mismatch found."""
import re, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent

errors = []

# 1. CharacterStatePacket size
cs_packet = ROOT / "Shared/CharacterStatePacket.cs"
text = cs_packet.read_text()
m = re.search(r'public const int Size\s*=\s*([^;]+)', text)
if m:
    size_val = eval(m.group(1).replace("+", "+"))
    for doc in ["docs/netcode-architecture.md", "CLAUDE.md"]:
        doc_text = (ROOT / doc).read_text()
        for n in re.findall(rf'CharacterStatePacket[^)]*?(\d+)\s*byte', doc_text):
            if int(n) != size_val:
                errors.append(f"{doc}: CharacterStatePacket size is {n}, code says {size_val}")

# 2. InputState size
is_packet = ROOT / "Shared/InputState.cs"
text = is_packet.read_text()
m = re.search(r'public const int Size\s*=\s*([^;]+)', text)
if m:
    size_val = eval(m.group(1).replace("+", "+"))
    for doc in ["CLAUDE.md"]:
        doc_text = (ROOT / doc).read_text()
        for n in re.findall(rf'ClientInputPacket[^)]*?(\d+)\s*byte', doc_text):
            if int(n) != size_val:
                errors.append(f"{doc}: ClientInputPacket size is {n}, code says {size_val}")

# 3. Baked data bone count
bake_tool = ROOT / "Scripts/Tools/BakeSkeletonTool.cs"
text = bake_tool.read_text()
m = re.search(r'private static readonly string\[\] BoneNames = new\[\]\s*\{(.*?)\};', text, re.DOTALL)
if m:
    bones = re.findall(r'"([^"]+)"', m.group(1))
    doc_bone_refs = []
    for doc in ["docs/adding-a-new-character.md", "docs/hitbox-system.md"]:
        doc_text = (ROOT / doc).read_text()
        for name in bones[:3]:
            if name.replace("mixamorig_","") in doc_text:
                doc_bone_refs.append(name)
    if len(doc_bone_refs) < 3:
        errors.append("Bone names not found in docs (maybe outdated)")

if errors:
    print("❌ Doc consistency issues found:")
    for e in errors:
        print(f"  • {e}")
    sys.exit(1)
else:
    print("✅ Docs are consistent with source code")
