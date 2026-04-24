using System.Numerics;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.NeutralFormats;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Models;
using OmegaAssetStudio.WinUI.Modules.UpkMigration.Upk148Reader;
using UpkManager.Models.UpkFile.Engine.Anim;
using UpkManager.Models.UpkFile.Tables;

namespace OmegaAssetStudio.WinUI.Modules.UpkMigration.Converters;

public sealed class AnimationConverter148ToNeutral
{
    public IReadOnlyList<NeutralAnimation> Convert(Upk148ExportTableEntry entry, Action<string>? log = null)
    {
        if (string.Equals(entry.ClassName, "AnimSequence", StringComparison.OrdinalIgnoreCase) &&
            UpkExportHydrator.TryHydrate(entry, out UAnimSequence? animSequence, log, false) &&
            animSequence is not null)
            return [ConvertSequence(entry, animSequence, null, log)];

        if (string.Equals(entry.ClassName, "AnimSet", StringComparison.OrdinalIgnoreCase) &&
            UpkExportHydrator.TryHydrate(entry, out UAnimSet? animSet, log, false) &&
            animSet is not null)
        {
            var sequences = animSet.Sequences ?? [];
            var trackBoneNames = animSet.TrackBoneNames ?? [];
            List<NeutralAnimation> animations = [];
            int count = Math.Min(sequences.Count, trackBoneNames.Count);
            for (int index = 0; index < count; index++)
            {
                UAnimSequence? sequence = sequences[index]?.LoadObject<UAnimSequence>();
                if (sequence is null)
                    continue;

                animations.Add(ConvertSequence(entry, sequence, animSet, log));
            }

            if (animations.Count == 0)
            {
                NeutralAnimation empty = new()
                {
                    Name = string.IsNullOrWhiteSpace(entry.ObjectName) ? entry.PathName : entry.ObjectName,
                    FrameCount = 0,
                    LengthSeconds = 0
                };
                log?.Invoke($"Converted animation set {entry.PathName} without extractable sequences.");
                return [empty];
            }

            return animations;
        }

        return [];
    }

    private static NeutralAnimation ConvertSequence(Upk148ExportTableEntry entry, UAnimSequence sequence, UAnimSet? ownerSet, Action<string>? log)
    {
        string sequenceName = sequence.SequenceName?.Name ?? string.Empty;
        NeutralAnimation neutral = new()
        {
            Name = string.IsNullOrWhiteSpace(sequenceName) ? entry.ObjectName : sequenceName,
            LengthSeconds = sequence.SequenceLength,
            FrameCount = sequence.NumFrames
        };

        List<string> boneNames = ownerSet?.TrackBoneNames is null
            ? []
            : ownerSet.TrackBoneNames.Select(name => name?.Name ?? string.Empty).ToList();

        var rawAnimationData = sequence.RawAnimationData ?? [];
        int trackCount = rawAnimationData.Count;
        for (int i = 0; i < trackCount; i++)
        {
            string boneName = i < boneNames.Count ? boneNames[i] : $"Bone_{i}";
            NeutralAnimationTrack track = new() { BoneName = boneName };
            RawAnimSequenceTrack rawTrack = rawAnimationData[i];
            var posKeys = rawTrack.PosKeys ?? [];
            var rotKeys = rawTrack.RotKeys ?? [];
            int frameCount = Math.Max(posKeys.Count, rotKeys.Count);
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                Vector3 position = frameIndex < posKeys.Count
                    ? posKeys[frameIndex].ToVector3()
                    : Vector3.Zero;
                Quaternion rotation = frameIndex < rotKeys.Count
                    ? rotKeys[frameIndex].ToQuaternion()
                    : Quaternion.Identity;
                float timeSeconds = neutral.FrameCount <= 1 ? 0.0f : (neutral.LengthSeconds / Math.Max(1, neutral.FrameCount - 1)) * frameIndex;
                track.Frames.Add(new NeutralAnimationFrame(timeSeconds, position, rotation));
            }
            neutral.Tracks.Add(track);
        }

        log?.Invoke($"Converted animation {entry.PathName} with {neutral.Tracks.Count} track(s).");
        return neutral;
    }
}

