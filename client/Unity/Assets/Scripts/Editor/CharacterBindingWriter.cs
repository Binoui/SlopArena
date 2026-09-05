using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Client.Animation;

internal static class CharacterBindingWriter
{
    internal static byte[] Write(
        CharacterAssetCatalog catalog,
        IReadOnlyList<CharacterCookAnimationDefinition> animations,
        string sourceHash)
    {
        var json = new StringBuilder(1024);
        json.Append('{');
        Property(json, "packageId", catalog.PackageId);
        Property(json, "catalogSchemaVersion", catalog.CatalogSchemaVersion);
        Property(json, "bindingSchemaVersion", UnityCharacterAssetCooker.BindingSchemaVersion);
        Property(json, "poseFormat", "SKEL");
        Property(json, "poseVersion", UnityCharacterAssetCooker.PoseVersion);
        Property(json, "sampleRate", UnityCharacterAssetCooker.SampleRate);
        Property(json, "sourceHash", sourceHash);
        Property(json, "rigGlobalObjectId", UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(catalog.Rig).ToString());
        Property(json, "weaponConfigGlobalObjectId", catalog.WeaponConfig == null ? "" : UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(catalog.WeaponConfig).ToString());
        json.Append("\"animations\":[");
        bool first = true;
        foreach (var animation in animations.OrderBy(x => x.SemanticId, StringComparer.Ordinal))
        {
            if (!first) json.Append(',');
            first = false;
            json.Append('{');
            Property(json, "semanticId", animation.SemanticId);
            Property(json, "poseTrackId", animation.PoseTrackId);
            Property(json, "clipGlobalObjectId", animation.ClipGlobalObjectId);
            Property(json, "poseName", animation.PoseTrackId);
            Property(json, "frameCount", animation.FrameCount);
            Property(json, "clipLengthBits", animation.ClipLengthBits);
            Property(json, "sampleRate", animation.SampleRate);
            Property(json, "extrapolation", (int)animation.Extrapolation, final: true);
            json.Append('}');
        }
        json.Append(']');
        json.Append(",\"presentations\":[");
        first = true;
        foreach (var presentation in (catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>()).OrderBy(x => x.SemanticId, StringComparer.Ordinal))
        {
            if (!first) json.Append(',');
            first = false;
            json.Append('{');
            Property(json, "semanticId", presentation.SemanticId);
            Property(json, "prefabGlobalObjectId",
                UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(presentation.Prefab).ToString(), final: true);
            json.Append('}');
        }
        json.Append(']');
        json.Append('}');
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static void Property(StringBuilder json, string name, string value, bool final = false)
    {
        json.Append(JsonSerializer.Serialize(name));
        json.Append(':');
        json.Append(JsonSerializer.Serialize(value));
        if (!final) json.Append(',');
    }

    private static void Property(StringBuilder json, string name, int value, bool final = false)
    {
        json.Append(JsonSerializer.Serialize(name));
        json.Append(':');
        json.Append(value.ToString(CultureInfo.InvariantCulture));
        if (!final) json.Append(',');
    }
}
