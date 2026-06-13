using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SlopArena.Shared
{
    /// <summary>
    /// A single GLB node (bone) in the skeleton.
    /// </summary>
    public struct GlbNode
    {
        public int Index;
        public string Name;
        public int Parent;
        public int[] Children;
        /// <summary>
        /// 3 floats: tx, ty, tz
        /// </summary>
        public float[] RestPos;
        /// <summary>
        /// 4 floats: qx, qy, qz, qw (glTF convention: xyz then w)
        /// </summary>
        public float[] RestRot;
        /// <summary>
        /// 3 floats
        /// </summary>
        public float[] RestScale;
        public bool IsSkinRoot;
    }

    /// <summary>
    /// Represents one animated property track for a bone.
    /// </summary>
    public struct AnimTrack
    {
        public int BoneIndex;
        /// <summary>
        /// &quot;translation&quot;, &quot;rotation&quot;, &quot;scale&quot;
        /// </summary>
        public string Path;
        public float[] Times;
        /// <summary>
        /// VEC3 for translation/scale, VEC4 for rotation
        /// </summary>
        public float[] Values;
    }

    /// <summary>
    /// A single animation (name + tracks per bone).
    /// </summary>
    public struct AnimationData
    {
        public string Name;
        /// <summary>
        /// in seconds
        /// </summary>
        public float Duration;
        public AnimTrack[] Tracks;
    }

    /// <summary>
    /// Pure C# skeleton loaded from a GLB file.
    /// Can compute bone world transforms at any animation frame.
    /// No Godot dependency — usable by client AND server.
    /// </summary>
    public class ServerSkeleton
    {
        public GlbNode[] Nodes { get; private set; }
        public AnimationData[] Animations { get; private set; }
        public int RootBone { get; private set; }

        /// <summary>
        /// Cached per-frame (set by SampleAnimation)
        /// </summary>
        private readonly float[][] _localPos;   // per bone: 3 floats
        /// <summary>
        /// per bone: 4 floats (qx, qy, qz, qw)
        /// </summary>
        private readonly float[][] _localRot;
        /// <summary>
        /// per bone: 3 floats
        /// </summary>
        private readonly float[][] _localScale;
        /// <summary>
        /// per bone: 3 floats (computed)
        /// </summary>
        private readonly float[][] _worldPos;
        /// <summary>
        /// per bone: 4 floats (computed)
        /// </summary>
        private readonly float[][] _worldRot;

        public ServerSkeleton(GlbNode[] nodes, AnimationData[] anims, int rootBone)
        {
            Nodes = nodes;
            Animations = anims;
            RootBone = rootBone;
            int n = nodes.Length;
            _localPos = new float[n][];
            _localRot = new float[n][];
            _localScale = new float[n][];
            _worldPos = new float[n][];
            _worldRot = new float[n][];
            for (int i = 0; i < n; i++)
            {
                _localPos[i] = (float[])nodes[i].RestPos?.Clone() ?? new float[] { 0, 0, 0 };
                _localRot[i] = (float[])nodes[i].RestRot?.Clone() ?? new float[] { 0, 0, 0, 1 };
                _localScale[i] = (float[])nodes[i].RestScale?.Clone() ?? new float[] { 1, 1, 1 };
                _worldPos[i] = new float[3];
                _worldRot[i] = new float[4];
            }
        }

        /// <summary>
        /// Sample an animation at a given time, setting local transforms for each bone.
        /// </summary>
        public void SampleAnimation(int animIndex, float timeSec)
        {
            // Reset to rest pose
            for (int i = 0; i < Nodes.Length; i++)
            {
                _localPos[i][0] = Nodes[i].RestPos?[0] ?? 0;
                _localPos[i][1] = Nodes[i].RestPos?[1] ?? 0;
                _localPos[i][2] = Nodes[i].RestPos?[2] ?? 0;
                _localRot[i][0] = Nodes[i].RestRot?[0] ?? 0;
                _localRot[i][1] = Nodes[i].RestRot?[1] ?? 0;
                _localRot[i][2] = Nodes[i].RestRot?[2] ?? 0;
                _localRot[i][3] = Nodes[i].RestRot?[3] ?? 1;
                _localScale[i][0] = Nodes[i].RestScale?[0] ?? 1;
                _localScale[i][1] = Nodes[i].RestScale?[1] ?? 1;
                _localScale[i][2] = Nodes[i].RestScale?[2] ?? 1;
            }

            if (animIndex < 0 || animIndex >= Animations.Length) return;

            var anim = Animations[animIndex];
            float clampedT = Math.Clamp(timeSec, 0f, anim.Duration > 0 ? anim.Duration : 1f);

            foreach (var track in anim.Tracks)
            {
                int bi = track.BoneIndex;
                if (bi < 0 || bi >= Nodes.Length) continue;

                if (track.Times.Length == 0) continue;

                // Find the two keyframes to interpolate between
                int k = 0;
                while (k < track.Times.Length - 1 && track.Times[k + 1] < clampedT)
                    k++;

                if (k >= track.Times.Length) k = track.Times.Length - 1;

                int nextK = Math.Min(k + 1, track.Times.Length - 1);
                float t0 = track.Times[k];
                float t1 = track.Times[nextK];
                float frac = (t1 - t0) > 0.0001f
                    ? Math.Clamp((clampedT - t0) / (t1 - t0), 0f, 1f)
                    : 0f;

                if (track.Path == "translation")
                {
                    const int stride = 3;
                    int base0 = k * stride;
                    int base1 = nextK * stride;
                    _localPos[bi][0] = Lerp(track.Values[base0], track.Values[base1], frac);
                    _localPos[bi][1] = Lerp(track.Values[base0 + 1], track.Values[base1 + 1], frac);
                    _localPos[bi][2] = Lerp(track.Values[base0 + 2], track.Values[base1 + 2], frac);
                }
                else if (track.Path == "rotation")
                {
                    const int stride = 4;
                    int base0 = k * stride;
                    int base1 = nextK * stride;
                    // SLERP between quaternions
                    float qax = track.Values[base0], qay = track.Values[base0 + 1], qaz = track.Values[base0 + 2], qaw = track.Values[base0 + 3];
                    float qbx = track.Values[base1], qby = track.Values[base1 + 1], qbz = track.Values[base1 + 2], qbw = track.Values[base1 + 3];
                    float dot = (qax * qbx) + (qay * qby) + (qaz * qbz) + (qaw * qbw);
                    if (dot < 0) { qbx = -qbx; qby = -qby; qbz = -qbz; qbw = -qbw; dot = -dot; }
                    if (dot > 0.9999f) { /* near-linear */ }
                    float theta = MathF.Acos(Math.Clamp(dot, -1f, 1f));
                    float sinTheta = MathF.Sin(theta);
                    if (sinTheta > 0.0001f)
                    {
                        float wa = MathF.Sin((1 - frac) * theta) / sinTheta;
                        float wb = MathF.Sin(frac * theta) / sinTheta;
                        _localRot[bi][0] = (wa * qax) + (wb * qbx);
                        _localRot[bi][1] = (wa * qay) + (wb * qby);
                        _localRot[bi][2] = (wa * qaz) + (wb * qbz);
                        _localRot[bi][3] = (wa * qaw) + (wb * qbw);
                    }
                    else
                    {
                        _localRot[bi][0] = qax; _localRot[bi][1] = qay;
                        _localRot[bi][2] = qaz; _localRot[bi][3] = qaw;
                    }
                }
                else if (track.Path == "scale")
                {
                    const int stride = 3;
                    int base0 = k * stride;
                    int base1 = nextK * stride;
                    _localScale[bi][0] = Lerp(track.Values[base0], track.Values[base1], frac);
                    _localScale[bi][1] = Lerp(track.Values[base0 + 1], track.Values[base1 + 1], frac);
                    _localScale[bi][2] = Lerp(track.Values[base0 + 2], track.Values[base1 + 2], frac);
                }
            }
        }

        /// <summary>
        /// Compute world transforms for all bones (top-down from root).
        /// Call after SampleAnimation().
        /// </summary>
        public void ComputeWorldTransforms()
        {
            for (int i = 0; i < Nodes.Length; i++)
                ComputeWorldForNode(i);
        }

        private void ComputeWorldForNode(int idx)
        {
            var node = Nodes[idx];
            if (node.Parent < 0)
            {
                // Root: world = local
                _worldPos[idx][0] = _localPos[idx][0];
                _worldPos[idx][1] = _localPos[idx][1];
                _worldPos[idx][2] = _localPos[idx][2];
                _worldRot[idx][0] = _localRot[idx][0];
                _worldRot[idx][1] = _localRot[idx][1];
                _worldRot[idx][2] = _localRot[idx][2];
                _worldRot[idx][3] = _localRot[idx][3];
            }
            else
            {
                int p = node.Parent;
                // Rotate local position by parent's world rotation, then add parent world position
                float lx = _localPos[idx][0], ly = _localPos[idx][1], lz = _localPos[idx][2];
                float pqw = _worldRot[p][3], pqx = _worldRot[p][0], pqy = _worldRot[p][1], pqz = _worldRot[p][2];
                // Rotate vector
                RotateVector3(ref lx, ref ly, ref lz, pqx, pqy, pqz, pqw);
                _worldPos[idx][0] = _worldPos[p][0] + lx;
                _worldPos[idx][1] = _worldPos[p][1] + ly;
                _worldPos[idx][2] = _worldPos[p][2] + lz;

                // Combine quaternions: q_world_child = q_world_parent * q_local_child
                float lqw = _localRot[idx][3], lqx = _localRot[idx][0], lqy = _localRot[idx][1], lqz = _localRot[idx][2];
                float w = (pqw * lqw) - (pqx * lqx) - (pqy * lqy) - (pqz * lqz);
                float x = (pqw * lqx) + (pqx * lqw) + (pqy * lqz) - (pqz * lqy);
                float y = (pqw * lqy) - (pqx * lqz) + (pqy * lqw) + (pqz * lqx);
                float z = (pqw * lqz) + (pqx * lqy) - (pqy * lqx) + (pqz * lqw);

                _worldRot[idx][0] = x; _worldRot[idx][1] = y;
                _worldRot[idx][2] = z; _worldRot[idx][3] = w;
            }
        }

        /// <summary>
        /// Get the world position of a bone by name (after ComputeWorldTransforms).
        /// Returns true if found.
        /// </summary>
        public bool GetBoneWorldPosition(string name, out float px, out float py, out float pz)
        {
            // Try exact match, then strip common prefixes to match Godot/Mixamo naming
            for (int i = 0; i < Nodes.Length; i++)
            {
                if (Nodes[i].Name == name || Nodes[i].Name.EndsWith(":" + name))
                {
                    px = _worldPos[i][0];
                    py = _worldPos[i][1];
                    pz = _worldPos[i][2];
                    return true;
                }
            }
            // Try stripping "mixamorig_" prefix: "mixamorig_Head" → "Head"
            for (int i = 0; i < Nodes.Length; i++)
            {
                string stripped = name;
                int colonIdx = stripped.IndexOf(':');
                if (colonIdx >= 0) stripped = stripped.Substring(colonIdx + 1);
                int uscoreIdx = stripped.IndexOf('_');
                if (uscoreIdx >= 0) stripped = stripped.Substring(uscoreIdx + 1);
                if (Nodes[i].Name == stripped || Nodes[i].Name == "mixamorig_" + stripped)
                {
                    px = _worldPos[i][0];
                    py = _worldPos[i][1];
                    pz = _worldPos[i][2];
                    return true;
                }
            }
            px = py = pz = 0;
            return false;
        }

        // ── Helpers ──

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static void RotateVector3(ref float x, ref float y, ref float z,
            float qx, float qy, float qz, float qw)
        {
            // v' = 2*(qw*qv + q × qv) cross part + qv*(qw² - |q|²) scalar part
            float ux = qx, uy = qy, uz = qz;
            float crossX = (uy * z) - (uz * y);
            float crossY = (uz * x) - (ux * z);
            float crossZ = (ux * y) - (uy * x);
            float dot = (ux * x) + (uy * y) + (uz * z);
            float s = (qw * qw) - ((ux * ux) + (uy * uy) + (uz * uz));
            x = (2f * ((qw * crossX) + (dot * ux))) + (s * x);
            y = (2f * ((qw * crossY) + (dot * uy))) + (s * y);
            z = (2f * ((qw * crossZ) + (dot * uz))) + (s * z);
        }

        // ── GLB PARSER ──

        public static ServerSkeleton LoadFromGlb(byte[] glbData)
        {
            // Parse GLB JSON chunk
            int pos = 12; // skip header
            uint magic = BitConverter.ToUInt32(glbData, 0);
            if (magic != 0x46546C67) throw new Exception("Not a valid GLB file");

            byte[] binBuf = null;
            JsonDocument jsonDoc = null;

            while (pos < glbData.Length)
            {
                uint chunkLen = BitConverter.ToUInt32(glbData, pos);
                uint chunkType = BitConverter.ToUInt32(glbData, pos + 4);
                if (chunkLen > 100_000_000)
                {
                    Console.WriteLine($"[Skeleton] Invalid chunkLen={chunkLen} at pos={pos}, fileLen={glbData.Length}");
                    break;
                }
                byte[] chunkData = new byte[chunkLen];
                Array.Copy(glbData, pos + 8, chunkData, 0, chunkLen);
                pos += 8 + (int)Align4(chunkLen + 8);

                if (chunkType == 0x4E4F534A) // JSON
                    jsonDoc = JsonDocument.Parse(chunkData);
                else if (chunkType == 0x004E4249) // BIN
                    binBuf = chunkData;
            }

            if (jsonDoc == null) throw new Exception("No JSON chunk in GLB");

            var root = jsonDoc.RootElement;

            // Parse nodes
            var nodes = new List<GlbNode>();
            var nodeNameToIdx = new Dictionary<string, int>();

            if (root.TryGetProperty("nodes", out var nodesEl))
            {
                for (int i = 0; i < nodesEl.GetArrayLength(); i++)
                {
                    var n = nodesEl[i];
                    string name = n.TryGetProperty("name", out var nameEl) ? nameEl.GetString()! : $"node_{i}";
                    nodeNameToIdx[name] = i;
                    nodes.Add(new GlbNode
                    {
                        Index = i,
                        Name = name,
                        Parent = -1,
                        Children = n.TryGetProperty("children", out var cEl)
                            ? JsonSerializer.Deserialize<int[]>(cEl.GetRawText()) ?? Array.Empty<int>()
                            : Array.Empty<int>(),
                        RestPos = n.TryGetProperty("translation", out var tEl)
                            ? JsonSerializer.Deserialize<float[]>(tEl.GetRawText()) : null,
                        RestRot = n.TryGetProperty("rotation", out var rEl)
                            ? JsonSerializer.Deserialize<float[]>(rEl.GetRawText()) : null,
                        RestScale = n.TryGetProperty("scale", out var sEl)
                            ? JsonSerializer.Deserialize<float[]>(sEl.GetRawText()) : null,
                    });
                }
            }

            // Set parent indices from children arrays
            for (int i = 0; i < nodes.Count; i++)
            {
                foreach (int c in nodes[i].Children)
                {
                    var child = nodes[c];
                    child.Parent = i;
                    nodes[c] = child;
                }
            }

            // Find root bone: first node with a skin reference
            int rootBone = 0;
            if (root.TryGetProperty("skins", out var skinsEl) && skinsEl.GetArrayLength() > 0)
            {
                var skin = skinsEl[0];
                if (skin.TryGetProperty("skeleton", out var skelEl))
                    rootBone = skelEl.GetInt32();
                else if (skin.TryGetProperty("joints", out var jointsEl) && jointsEl.GetArrayLength() > 0)
                    rootBone = jointsEl[0].GetInt32();
            }

            // Parse animations
            var anims = new List<AnimationData>();
            if (root.TryGetProperty("animations", out var animsEl))
            {
                for (int ai = 0; ai < animsEl.GetArrayLength(); ai++)
                {
                    var a = animsEl[ai];
                    string animName = a.TryGetProperty("name", out var anEl) ? anEl.GetString()! : $"anim_{ai}";

                    // Get all accessors for reading keyframes
                    var accessors = root.GetProperty("accessors");
                    var bufferViews = root.GetProperty("bufferViews");

                    // Parse samplers + channels
                    var samplers = a.GetProperty("samplers");
                    var channels = a.GetProperty("channels");

                    // Group channels by bone + path
                    var trackMap = new Dictionary<(int boneIdx, string path), (List<float> times, List<float> values)>();
                    float maxTime = 0;

                    for (int ci = 0; ci < channels.GetArrayLength(); ci++)
                    {
                        var ch = channels[ci];
                        int samplerIdx = ch.GetProperty("sampler").GetInt32();
                        var target = ch.GetProperty("target");
                        int nodeIdx = target.GetProperty("node").GetInt32();
                        string path = target.GetProperty("path").GetString()!;
                        var samp = samplers[samplerIdx];

                        // Read input (time) accessor
                        int inputAccIdx = samp.GetProperty("input").GetInt32();
                        var inputAcc = accessors[inputAccIdx];
                        int inputBV = inputAcc.GetProperty("bufferView").GetInt32();
                        var inputAccBV = bufferViews[inputBV];
                        int inputByteOffset = inputAccBV.GetProperty("byteOffset").GetInt32()
                            + (inputAcc.TryGetProperty("byteOffset", out var iboEl) ? iboEl.GetInt32() : 0);
                        int inputCount = inputAcc.GetProperty("count").GetInt32();
                        float[] times = new float[inputCount];
                        if (binBuf != null)
                            for (int t = 0; t < inputCount; t++)
                                times[t] = BitConverter.ToSingle(binBuf, inputByteOffset + (t * 4));

                        // Read output (value) accessor
                        int outAccIdx = samp.GetProperty("output").GetInt32();
                        var outAcc = accessors[outAccIdx];
                        int outBV = outAcc.GetProperty("bufferView").GetInt32();
                        var outAccBV = bufferViews[outBV];
                        int stride = outAccBV.TryGetProperty("byteStride", out var strEl) ? strEl.GetInt32() : 0;
                        int outByteOffset = outAccBV.GetProperty("byteOffset").GetInt32()
                            + (outAcc.TryGetProperty("byteOffset", out var oboEl) ? oboEl.GetInt32() : 0);
                        string outType = outAcc.GetProperty("type").GetString()!;
                        int outCompType = outAcc.GetProperty("componentType").GetInt32();
                        int compSize = outCompType switch { 5126 => 4, 5123 => 2, 5122 => 2, _ => 4 };
                        int comps = outType switch { "VEC3" => 3, "VEC4" => 4, "SCALAR" => 1, "VEC2" => 2, _ => 3 };
                        int outCount = outAcc.GetProperty("count").GetInt32();
                        int realStride = stride > 0 ? stride : comps * compSize;

                        float[] values = new float[outCount * comps];
                        if (binBuf != null)
                            for (int t = 0; t < outCount; t++)
                                for (int c = 0; c < comps; c++)
                                    values[(t * comps) + c] = BitConverter.ToSingle(binBuf, outByteOffset + (t * realStride) + (c * 4));

                        var key = (nodeIdx, path);
                        if (!trackMap.ContainsKey(key))
                            trackMap[key] = (new List<float>(), new List<float>());
                        trackMap[key].times.AddRange(times);
                        trackMap[key].values.AddRange(values);

                        if (times.Length > 0 && times[times.Length - 1] > maxTime)
                            maxTime = times[times.Length - 1];
                    }

                    // Build tracks
                    var tracks = new List<AnimTrack>();
                    foreach (var kvp in trackMap)
                    {
                        tracks.Add(new AnimTrack
                        {
                            BoneIndex = kvp.Key.boneIdx,
                            Path = kvp.Key.path,
                            Times = kvp.Value.times.ToArray(),
                            Values = kvp.Value.values.ToArray(),
                        });
                    }

                    anims.Add(new AnimationData
                    {
                        Name = animName,
                        Duration = maxTime,
                        Tracks = tracks.ToArray(),
                    });
                }
            }

            return new ServerSkeleton(nodes.ToArray(), anims.ToArray(), rootBone);
        }

        private static uint Align4(uint val) => (val + 3) & ~3u;
    }
}
