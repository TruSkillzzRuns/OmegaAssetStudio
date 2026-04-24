using System.Numerics;
using OmegaAssetStudio.TexturePreview;
using UpkManager.Models.UpkFile.Classes;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;

namespace OmegaAssetStudio.MeshPreview;

public sealed class MeshPreviewGameMaterialResolver
{
    private readonly UpkTextureLoader _upkTextureLoader = new();
    private readonly TextureToMaterialConverter _slotConverter = new();

    public async Task ApplyToSectionsAsync(string upkPath, USkeletalMesh skeletalMesh, MeshPreviewMesh previewMesh, Action<string> log = null)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(previewMesh);

        foreach (MeshPreviewSection section in previewMesh.Sections)
            section.GameMaterial = null;

        if (skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0)
            return;

        FStaticLODModel lod = skeletalMesh.LODModels[0];
        int materialCount = skeletalMesh.Materials?.Count ?? 0;
        log?.Invoke($"GameApprox: SkeletalMesh material slots = {materialCount}.");

        if (lod.Sections is null)
            return;

        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count && sectionIndex < previewMesh.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection sourceSection = lod.Sections[sectionIndex];
            FObject materialObject = sourceSection.MaterialIndex >= 0 && sourceSection.MaterialIndex < materialCount && skeletalMesh.Materials is not null
                ? skeletalMesh.Materials[sourceSection.MaterialIndex]
                : null;
            string materialPath = materialObject?.GetPathName() ?? "<missing>";
            string tableEntryType = materialObject?.TableEntry?.GetType().Name ?? "<null>";
            string className = ResolveClassName(materialObject?.TableEntry);
            object resolvedMaterial = materialObject?.LoadObject<UObject>();
            log?.Invoke($"GameApprox: section {sectionIndex} material index {sourceSection.MaterialIndex}, path {materialPath}, tableEntry {tableEntryType}, class {className}, resolved type {(resolvedMaterial?.GetType().Name ?? "<null>")}.");

            try
            {
                MeshPreviewGameMaterial material = await BuildSectionMaterialAsync(sectionIndex, materialObject, log).ConfigureAwait(true);
                previewMesh.Sections[sectionIndex].GameMaterial = material;
            }
            catch (Exception ex)
            {
                log?.Invoke($"GameApprox: skipped section {sectionIndex} game material build: {ex.Message}");
            }
        }
    }

    public async Task<MeshPreviewGameMaterialResult> BuildMaterialSetAsync(string upkPath, USkeletalMesh skeletalMesh, Action<string> log = null)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);

        Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> targets = ResolveTextureTargets(skeletalMesh, log);
        if (targets.Count == 0)
            log?.Invoke("GameApprox: no previewable textures were resolved from the UE3 material chain or material-resource fallback.");
        else
            log?.Invoke($"GameApprox: resolved {targets.Count} candidate texture slot(s) from the UE3 material chain.");
        if (targets.Count == 0)
            return MeshPreviewGameMaterialResult.Empty;

        TexturePreviewMaterialSet materialSet = new() { Enabled = true };
        List<string> resolvedSources = [];

        foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in targets.OrderBy(static entry => entry.Key))
        {
            if (target.TextureObject == null)
                continue;

            try
            {
                log?.Invoke($"GameApprox: loading {slot} from {target.TexturePath} (Section {target.SectionIndex}, Material {target.MaterialPath}, Param {target.ParameterName}).");
                TexturePreviewTexture texture = await _upkTextureLoader.LoadFromObjectAsync(target.TextureObject, slot, log).ConfigureAwait(true);
                texture.Slot = slot;
                materialSet.SetTexture(slot, texture);
                resolvedSources.Add($"{slot}: {target.TexturePath} (Section {target.SectionIndex})");
            }
            catch (Exception ex)
            {
                log?.Invoke($"GameApprox skipped {slot} from {target.TexturePath}: {ex.Message}");
            }
        }

        if (!materialSet.Textures.Any())
            return MeshPreviewGameMaterialResult.Empty;

        return new MeshPreviewGameMaterialResult
        {
            MaterialSet = materialSet,
            Summary = string.Join(", ", resolvedSources)
        };
    }

    private async Task<MeshPreviewGameMaterial> BuildSectionMaterialAsync(int sectionIndex, FObject materialObject, Action<string> log)
    {
        if (materialObject?.LoadObject<UMaterialInstanceConstant>() is not UMaterialInstanceConstant instanceConstant)
            return null;

        MeshPreviewGameMaterial material = new()
        {
            Enabled = true,
            MaterialPath = materialObject.GetPathName()
        };
        ApplyMaterialParameters(instanceConstant, material);
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        FObject current = materialObject;
        while (current != null)
        {
            string currentPath = current.GetPathName() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPath) && !seenPaths.Add(currentPath))
                break;

            UObject resolved = current.LoadObject<UObject>();
            switch (resolved)
            {
                case UMaterialInstanceConstant currentInstance:
                    await TryAssignResolvedTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Diffuse, currentInstance, log,
                        "Diffuse").ConfigureAwait(true);
                    await TryAssignResolvedTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Normal, currentInstance, log,
                        "Norm").ConfigureAwait(true);
                    await TryAssignResolvedTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Smspsk, currentInstance, log,
                        "specmult_specpow_skinmask").ConfigureAwait(true);
                    await TryAssignResolvedTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Espa, currentInstance, log,
                        "emissivespecpow").ConfigureAwait(true);
                    await TryAssignResolvedTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Smrr, currentInstance, log,
                        "specmultrimmaskrefl").ConfigureAwait(true);
                    await TryAssignResolvedTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.SpecColor, currentInstance, log,
                        "SpecColor").ConfigureAwait(true);
                    current = currentInstance.Parent;
                    continue;

                case UMaterialInstance currentInstanceBase:
                    current = currentInstanceBase.Parent;
                    continue;

                case UMaterial parentMaterial:
                    await TryAssignMaterialResourceTexturesAsync(sectionIndex, material, currentPath, parentMaterial, log).ConfigureAwait(true);
                    current = null;
                    break;

                default:
                    current = null;
                    break;
            }
        }

        if (!material.Textures.Any())
            return null;

        log?.Invoke(
            $"GameApprox: section {sectionIndex} built material BlendMode={material.BlendMode}, TwoSided={material.TwoSided}, " +
            $"Textures=[{string.Join(", ", material.Textures.Select(static kvp => $"{kvp.Key}={kvp.Value?.ExportPath ?? "<null>"}"))}]");
        log?.Invoke(
            $"GameApprox: section {sectionIndex} values " +
            $"Ambient={material.LightingAmbient:F2}, ShadowAmbient={material.ShadowAmbientMult:F2}, " +
            $"NormalStrength={material.NormalStrength:F2}, Reflection={material.ReflectionMult:F2}, " +
            $"Rim={material.RimColorMult:F2}, RimFalloff={material.RimFalloff:F2}, " +
            $"SpecMult={material.SpecMult:F2}, SpecMultLQ={material.SpecMultLq:F2}, " +
            $"SpecPower={material.SpecularPower:F2}, SpecMask={material.SpecularPowerMask:F2}, " +
            $"TwoSidedLighting={material.TwoSidedLighting:F2}, ScreenLightAmount={material.ScreenLightAmount:F2}, " +
            $"ScreenLightMult={material.ScreenLightMult:F2}, ScreenLightPower={material.ScreenLightPower:F2}");
        log?.Invoke(
            $"GameApprox: section {sectionIndex} vectors " +
            $"LambertAmbient={material.LambertAmbient}, Fill={material.FillLightColor}, " +
            $"ShadowAmbientColor={material.ShadowAmbientColor}, SpecularColor={material.SpecularColor}");

        return material;
    }

    private async Task TryAssignTextureAsync(
        int sectionIndex,
        MeshPreviewGameMaterial material,
        MeshPreviewGameTextureSlot slot,
        FObject textureObject,
        string parameterName,
        Action<string> log)
    {
        if (material.HasTexture(slot) || textureObject == null)
            return;

        string texturePath = textureObject.GetPathName() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(texturePath))
            return;

        try
        {
            log?.Invoke($"GameApprox: loading {slot} from {texturePath} (Section {sectionIndex}, Material {material.MaterialPath}, Param {parameterName}).");
            TexturePreviewMaterialSlot previewSlot = ResolvePreviewMaterialSlot(slot);
            TexturePreviewTexture texture = await _upkTextureLoader.LoadFromObjectAsync(textureObject, previewSlot, log).ConfigureAwait(true);
            texture.Slot = previewSlot;
            material.SetTexture(slot, texture);
        }
        catch (Exception ex)
        {
            log?.Invoke($"GameApprox skipped {slot} from {texturePath}: {ex.Message}");
        }
    }

    private async Task TryAssignMaterialResourceTexturesAsync(
        int sectionIndex,
        MeshPreviewGameMaterial material,
        string materialPath,
        UMaterial parentMaterial,
        Action<string> log)
    {
        FMaterialResource resource = parentMaterial.MaterialResource?.FirstOrDefault(static value => value != null);
        if (resource?.UniformExpressionTextures == null)
            return;

        foreach (FObject textureObject in resource.UniformExpressionTextures)
        {
            if (!TryResolveGameTextureSlot(textureObject, out MeshPreviewGameTextureSlot slot))
                continue;

            await TryAssignTextureAsync(
                sectionIndex,
                material,
                slot,
                textureObject,
                $"UniformExpressionTexture ({materialPath})",
                log).ConfigureAwait(true);
        }
    }

    private static void ApplyMaterialParameters(UMaterialInstanceConstant source, MeshPreviewGameMaterial target)
    {
        target.LambertDiffusePower = ResolveFirstScalarParameterValue(source, "lambertdiffusepower") ?? target.LambertDiffusePower;
        target.LightingAmbient = ResolveFirstScalarParameterValue(source, "lightingambient") ?? target.LightingAmbient;
        target.PhongDiffusePower = ResolveFirstScalarParameterValue(source, "phongdiffusepower") ?? target.PhongDiffusePower;
        target.ShadowAmbientMult = ResolveFirstScalarParameterValue(source, "shadowambientmult") ?? target.ShadowAmbientMult;
        target.NormalStrength = ResolveFirstScalarParameterValue(source, "normalstrength") ?? target.NormalStrength;
        target.ReflectionMult = ResolveFirstScalarParameterValue(source, "reflectionmult") ?? target.ReflectionMult;
        target.RimColorMult = ResolveFirstScalarParameterValue(source, "rimcolormult") ?? target.RimColorMult;
        target.RimFalloff = ResolveFirstScalarParameterValue(source, "rimfalloff") ?? target.RimFalloff;
        target.ScreenLightAmount = ResolveFirstScalarParameterValue(source, "screenlight_amount") ?? target.ScreenLightAmount;
        target.ScreenLightMult = ResolveFirstScalarParameterValue(source, "screenlight_mult") ?? target.ScreenLightMult;
        target.ScreenLightPower = ResolveFirstScalarParameterValue(source, "screenlight_power") ?? target.ScreenLightPower;
        target.SpecMult = ResolveFirstScalarParameterValue(source, "specmult") ?? target.SpecMult;
        target.SpecMultLq = ResolveFirstScalarParameterValue(source, "specmult_lq") ?? target.SpecMultLq;
        target.SpecularPower = ResolveFirstScalarParameterValue(source, "specularpower") ?? target.SpecularPower;
        target.SpecularPowerMask = ResolveFirstScalarParameterValue(source, "specularpowermask") ?? target.SpecularPowerMask;
        target.TwoSidedLighting = ResolveFirstScalarParameterValue(source, "twosidedlighting") ?? target.TwoSidedLighting;
        target.ImageReflectionNormalDampening = ResolveFirstScalarParameterValue(source, "imagereflectionnormaldampening") ?? target.ImageReflectionNormalDampening;
        target.SkinScatterStrength = ResolveFirstScalarParameterValue(source, "skinscatterstrength") ?? target.SkinScatterStrength;

        target.LambertAmbient = ResolveFirstVectorParameterValue(source, "lambertambient") ?? target.LambertAmbient;
        target.ShadowAmbientColor = ResolveFirstVectorParameterValue(source, "shadowambientcolor") ?? target.ShadowAmbientColor;
        target.FillLightColor = ResolveFirstVectorParameterValue(source, "filllightcolor") ?? target.FillLightColor;
        target.SpecularColor = ResolveFirstVectorParameterValue(source, "specularcolor") ?? target.SpecularColor;
        target.DiffuseColor = ResolveFirstVectorParameterValue(source, "diffusecolor") ?? target.DiffuseColor;
        target.SubsurfaceInscatteringColor = ResolveFirstVectorParameterValue(source, "subsurfaceinscatteringcolor") ?? target.SubsurfaceInscatteringColor;
        target.SubsurfaceAbsorptionColor = ResolveFirstVectorParameterValue(source, "subsurfaceabsorptioncolor") ?? target.SubsurfaceAbsorptionColor;

        UMaterial parentMaterial = source.Parent?.LoadObject<UMaterial>();
        if (parentMaterial != null)
        {
            target.TwoSided = parentMaterial.TwoSided;
            target.BlendMode = parentMaterial.BlendMode;
        }

        if (source.bHasStaticPermutationResource && source.StaticPermutationResources.Length > 0)
        {
            FMaterialResource resource = source.StaticPermutationResources[0];
            if (resource != null && resource.bIsMaskedOverrideValue && resource.BlendModeOverrideValue != EBlendMode.BLEND_Opaque)
                target.BlendMode = resource.BlendModeOverrideValue;
        }

    }

    private async Task TryAssignResolvedTextureAsync(
        int sectionIndex,
        MeshPreviewGameMaterial material,
        MeshPreviewGameTextureSlot slot,
        UMaterialInstanceConstant source,
        Action<string> log,
        params string[] parameterNames)
    {
        FObject textureObject = ResolveFirstTextureParameterValue(source, out string resolvedParameterName, parameterNames);
        if (textureObject == null)
            return;

        await TryAssignTextureAsync(sectionIndex, material, slot, textureObject, resolvedParameterName, log).ConfigureAwait(true);
    }

    private static FObject ResolveFirstTextureParameterValue(UMaterialInstanceConstant source, out string resolvedParameterName, params string[] parameterNames)
    {
        foreach (string parameterName in parameterNames)
        {
            FObject textureObject = source.GetTextureParameterValue(parameterName);
            if (textureObject != null)
            {
                resolvedParameterName = parameterName;
                return textureObject;
            }
        }

        resolvedParameterName = parameterNames.Length > 0 ? parameterNames[0] : string.Empty;
        return null;
    }

    private static float? ResolveFirstScalarParameterValue(UMaterialInstanceConstant source, params string[] parameterNames)
    {
        foreach (string parameterName in parameterNames)
        {
            float? value = source.GetScalarParameterValue(parameterName);
            if (value.HasValue)
                return value;
        }

        return null;
    }

    private static Vector3? ResolveFirstVectorParameterValue(UMaterialInstanceConstant source, params string[] parameterNames)
    {
        foreach (string parameterName in parameterNames)
        {
            Vector3? value = source.GetVectorParameterValue(parameterName);
            if (value.HasValue)
                return value;
        }

        return null;
    }

    private static bool TryResolveGameTextureSlot(FObject textureObject, out MeshPreviewGameTextureSlot slot)
    {
        string texturePath = textureObject?.GetPathName() ?? string.Empty;
        string path = texturePath.ToLowerInvariant();

        if (path.Contains("specmult_specpow_skinmask") || path.Contains("smspsk"))
        {
            slot = MeshPreviewGameTextureSlot.Smspsk;
            return true;
        }

        if (path.Contains("emissivespecpow") || path.Contains("espa"))
        {
            slot = MeshPreviewGameTextureSlot.Espa;
            return true;
        }

        if (path.Contains("specmultrimmaskrefl") || path.Contains("smrr"))
        {
            slot = MeshPreviewGameTextureSlot.Smrr;
            return true;
        }

        if (path.Contains("speccolor"))
        {
            slot = MeshPreviewGameTextureSlot.SpecColor;
            return true;
        }

        UTexture2D texture = textureObject?.LoadObject<UTexture2D>();
        if (texture != null)
        {
            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap)
            {
                slot = MeshPreviewGameTextureSlot.Normal;
                return true;
            }

            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_CharacterSpecular)
            {
                slot = MeshPreviewGameTextureSlot.SpecColor;
                return true;
            }
        }

        if (path.Contains("_norm") || path.Contains("normal"))
        {
            slot = MeshPreviewGameTextureSlot.Normal;
            return true;
        }

        if (path.Contains("_diff") || path.Contains("diffuse") || path.Contains("albedo"))
        {
            slot = MeshPreviewGameTextureSlot.Diffuse;
            return true;
        }

        if (path.Contains("_spec"))
        {
            slot = MeshPreviewGameTextureSlot.SpecColor;
            return true;
        }

        slot = default;
        return false;
    }

    private Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> ResolveTextureTargets(USkeletalMesh skeletalMesh, Action<string> log)
    {
        Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> resolved = [];
        if (skeletalMesh.LODModels is null || skeletalMesh.LODModels.Count == 0)
            return resolved;

        int materialCount = skeletalMesh.Materials?.Count ?? 0;
        log?.Invoke($"GameApprox: SkeletalMesh material slots = {materialCount}.");
        FStaticLODModel lod = skeletalMesh.LODModels[0];
        if (lod.Sections is null)
            return resolved;

        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            try
            {
                FSkelMeshSection section = lod.Sections[sectionIndex];
                FObject materialObject = section.MaterialIndex >= 0 && section.MaterialIndex < materialCount && skeletalMesh.Materials is not null
                    ? skeletalMesh.Materials[section.MaterialIndex]
                    : null;
                string materialPath = materialObject?.GetPathName() ?? "<missing>";
                string tableEntryType = materialObject?.TableEntry?.GetType().Name ?? "<null>";
                string className = ResolveClassName(materialObject?.TableEntry);
                object resolvedMaterial = materialObject?.LoadObject<UObject>();
                log?.Invoke($"GameApprox: section {sectionIndex} material index {section.MaterialIndex}, path {materialPath}, tableEntry {tableEntryType}, class {className}, resolved type {(resolvedMaterial?.GetType().Name ?? "<null>")}.");

                foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in ResolveSectionTargets(sectionIndex, materialObject))
                {
                    if (!resolved.ContainsKey(slot))
                        resolved[slot] = target;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"GameApprox: skipped section {sectionIndex} during material resolution: {ex.Message}");
            }
        }

        return resolved;
    }

    private IEnumerable<KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>> ResolveSectionTargets(int sectionIndex, FObject materialObject)
    {
        Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> targets = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        FObject current = materialObject;

        while (current != null)
        {
            string currentPath = current.GetPathName() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPath) && !seenPaths.Add(currentPath))
                break;

            object resolvedObject = current.LoadObject<UObject>();
            if (resolvedObject == null)
                break;
            if (resolvedObject is UMaterialInstanceConstant instanceConstant)
            {
                foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in ResolveKnownInstanceParameters(sectionIndex, currentPath, instanceConstant))
                {
                    if (!targets.ContainsKey(slot))
                        targets[slot] = target;
                }

                foreach (FTextureParameterValue parameter in instanceConstant.TextureParameterValues ?? [])
                {
                    FObject textureObject = parameter.ParameterValue;
                    string texturePath = textureObject?.GetPathName() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(texturePath))
                        continue;

                    string parameterName = parameter.ParameterName?.Name ?? string.Empty;
                    TexturePreviewMaterialSlot slot = ResolveTextureSlot(textureObject, parameterName, TexturePreviewMaterialSlot.Diffuse);
                    if (targets.ContainsKey(slot))
                        continue;

                    targets[slot] = new ResolvedTextureTarget
                    {
                        SectionIndex = sectionIndex,
                        MaterialPath = currentPath,
                        ParameterName = parameterName,
                        TextureObject = textureObject,
                        TexturePath = texturePath
                    };
                }

                current = instanceConstant.Parent;
                continue;
            }

            if (resolvedObject is UMaterialInstance instance)
            {
                current = instance.Parent;
                continue;
            }

            if (resolvedObject is UMaterial material)
            {
                foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in ResolveMaterialResourceTextures(sectionIndex, currentPath, material))
                {
                    if (!targets.ContainsKey(slot))
                        targets[slot] = target;
                }

                break;
            }

            break;
        }

        return targets;
    }

    private IEnumerable<KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>> ResolveKnownInstanceParameters(
        int sectionIndex,
        string materialPath,
        UMaterialInstanceConstant material)
    {
        FObject diffuseTexture = ResolveFirstTextureParameterValue(material, out string diffuseParameterName,
            "Diffuse");
        if (diffuseTexture != null)
            yield return CreateResolvedTarget(sectionIndex, materialPath, diffuseParameterName, diffuseTexture,
                ResolveTextureSlot(diffuseTexture, diffuseParameterName, TexturePreviewMaterialSlot.Diffuse));

        FObject normalTexture = ResolveFirstTextureParameterValue(material, out string normalParameterName,
            "Norm");
        if (normalTexture != null)
            yield return CreateResolvedTarget(sectionIndex, materialPath, normalParameterName, normalTexture,
                ResolveTextureSlot(normalTexture, normalParameterName, TexturePreviewMaterialSlot.Normal));

        FObject specTexture = ResolveFirstTextureParameterValue(material, out string specParameterName,
            "SpecColor");
        if (specTexture != null)
            yield return CreateResolvedTarget(sectionIndex, materialPath, specParameterName, specTexture,
                ResolveTextureSlot(specTexture, specParameterName, TexturePreviewMaterialSlot.Specular));

        FObject maskTexture = ResolveFirstTextureParameterValue(material, out string maskParameterName,
            "specmult_specpow_skinmask");
        if (maskTexture != null)
            yield return CreateResolvedTarget(sectionIndex, materialPath, maskParameterName, maskTexture,
                ResolveTextureSlot(maskTexture, maskParameterName, TexturePreviewMaterialSlot.Mask));

        FObject emissiveTexture = ResolveFirstTextureParameterValue(material, out string emissiveParameterName,
            "emissivespecpow");
        if (emissiveTexture != null)
            yield return CreateResolvedTarget(sectionIndex, materialPath, emissiveParameterName, emissiveTexture,
                ResolveTextureSlot(emissiveTexture, emissiveParameterName, TexturePreviewMaterialSlot.Emissive));

        FObject reflectionTexture = ResolveFirstTextureParameterValue(material, out string reflectionParameterName,
            "specmultrimmaskrefl");
        if (reflectionTexture != null)
            yield return CreateResolvedTarget(sectionIndex, materialPath, reflectionParameterName, reflectionTexture,
                ResolveTextureSlot(reflectionTexture, reflectionParameterName, TexturePreviewMaterialSlot.Mask));
    }

    private static KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget> CreateResolvedTarget(
        int sectionIndex,
        string materialPath,
        string parameterName,
        FObject textureObject,
        TexturePreviewMaterialSlot slot)
    {
        string texturePath = textureObject?.GetPathName() ?? string.Empty;
        return new KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>(
            slot,
            new ResolvedTextureTarget
            {
                SectionIndex = sectionIndex,
                MaterialPath = materialPath,
                ParameterName = parameterName,
                TextureObject = textureObject,
                TexturePath = texturePath
            });
    }

    private TexturePreviewMaterialSlot ResolveTextureSlot(FObject textureObject, string parameterName, TexturePreviewMaterialSlot fallbackSlot)
    {
        string texturePath = textureObject?.GetPathName() ?? string.Empty;
        TexturePreviewMaterialSlot slot = _slotConverter.ResolveSlot($"{parameterName} {texturePath}", fallbackSlot);

        if (textureObject?.LoadObject<UTexture2D>() is UTexture2D texture)
        {
            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap)
            {
                return TexturePreviewMaterialSlot.Normal;
            }

            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_CharacterSpecular)
                return TexturePreviewMaterialSlot.Specular;
        }

        return slot;
    }

    private static TexturePreviewMaterialSlot ResolvePreviewMaterialSlot(MeshPreviewGameTextureSlot slot)
    {
        return slot switch
        {
            MeshPreviewGameTextureSlot.Diffuse => TexturePreviewMaterialSlot.Diffuse,
            MeshPreviewGameTextureSlot.Normal => TexturePreviewMaterialSlot.Normal,
            MeshPreviewGameTextureSlot.Smspsk => TexturePreviewMaterialSlot.Mask,
            MeshPreviewGameTextureSlot.Espa => TexturePreviewMaterialSlot.Emissive,
            MeshPreviewGameTextureSlot.Smrr => TexturePreviewMaterialSlot.Mask,
            MeshPreviewGameTextureSlot.SpecColor => TexturePreviewMaterialSlot.Specular,
            _ => TexturePreviewMaterialSlot.Diffuse
        };
    }

    private IEnumerable<KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>> ResolveMaterialResourceTextures(
        int sectionIndex,
        string materialPath,
        UMaterial material)
    {
        FMaterialResource resource = material.MaterialResource?.FirstOrDefault(static value => value != null);
        if (resource?.UniformExpressionTextures == null || resource.UniformExpressionTextures.Count == 0)
            yield break;

        int diffuseFallbackIndex = 0;
        foreach (FObject textureObject in resource.UniformExpressionTextures)
        {
            string texturePath = textureObject?.GetPathName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(texturePath))
                continue;

            TexturePreviewMaterialSlot slot = ResolveSlotFromTextureObject(textureObject, diffuseFallbackIndex == 0);
            if (slot == TexturePreviewMaterialSlot.Diffuse)
                diffuseFallbackIndex++;

            yield return new KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>(
                slot,
                new ResolvedTextureTarget
                {
                    SectionIndex = sectionIndex,
                    MaterialPath = materialPath,
                    ParameterName = "UniformExpressionTexture",
                    TextureObject = textureObject,
                    TexturePath = texturePath
                });
        }
    }

    private TexturePreviewMaterialSlot ResolveSlotFromTextureObject(FObject textureObject, bool preferDiffuseFallback)
    {
        string texturePath = textureObject?.GetPathName() ?? string.Empty;
        UTexture2D texture = textureObject?.LoadObject<UTexture2D>();

        if (texture != null)
        {
            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap)
            {
                return TexturePreviewMaterialSlot.Normal;
            }

            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_CharacterSpecular)
                return TexturePreviewMaterialSlot.Specular;
        }

        TexturePreviewMaterialSlot slot = _slotConverter.ResolveSlot(texturePath, preferDiffuseFallback ? TexturePreviewMaterialSlot.Diffuse : TexturePreviewMaterialSlot.Mask);
        return slot;
    }

    private static string ResolveClassName(UnrealObjectTableEntryBase entry)
    {
        return entry switch
        {
            UnrealExportTableEntry export => export.ClassReferenceNameIndex?.Name ?? "<unknown>",
            UnrealImportTableEntry import => import.ClassNameIndex?.Name ?? "<unknown>",
            _ => "<unknown>"
        };
    }

    private sealed class ResolvedTextureTarget
    {
        public int SectionIndex { get; init; }
        public string MaterialPath { get; init; } = string.Empty;
        public string ParameterName { get; init; } = string.Empty;
        public FObject TextureObject { get; init; }
        public string TexturePath { get; init; } = string.Empty;
    }
}

public sealed class MeshPreviewGameMaterialResult
{
    public static MeshPreviewGameMaterialResult Empty { get; } = new()
    {
        MaterialSet = new TexturePreviewMaterialSet(),
        Summary = string.Empty
    };

    public required TexturePreviewMaterialSet MaterialSet { get; init; }
    public string Summary { get; init; } = string.Empty;
}

