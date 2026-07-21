using System.Buffers.Binary;

namespace Clawbot.Agents.Core.Content;

// Phase 2.11: deterministic evenly-spaced GIF sampling without an external image library.
// Non-GIF images pass through as a single review part. Animated GIFs are split into single-frame
// GIF payloads with stable part IDs (`{assetId}#frame-{n}`).

public static class GifFrameSampler
{
    public const int DefaultMaxFrames = 4;
    public const int MaxDetectedFrames = 64;
    public const int MaxInputBytes = 5 * 1024 * 1024;

    public static IReadOnlyList<int> SelectFrameIndexes(int totalFrames, int maxFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);
        if (totalFrames <= maxFrames)
            return Enumerable.Range(0, totalFrames).ToArray();
        if (maxFrames == 1)
            return [0];

        var selected = new int[maxFrames];
        for (var i = 0; i < maxFrames; i++)
        {
            // Evenly spaced inclusive endpoints: 0 .. total-1
            selected[i] = (int)Math.Round(i * (totalFrames - 1) / (double)(maxFrames - 1));
        }

        // Dedup while preserving order (rounding collisions on small totals).
        var ordered = new List<int>(maxFrames);
        var seen = new HashSet<int>();
        foreach (var idx in selected)
        {
            if (seen.Add(idx))
                ordered.Add(idx);
        }

        // If collisions reduced count, fill remaining earliest unused indexes deterministically.
        for (var i = 0; ordered.Count < maxFrames && i < totalFrames; i++)
        {
            if (seen.Add(i))
                ordered.Add(i);
        }

        ordered.Sort();
        return ordered;
    }

    public static IReadOnlyList<ReviewPromptPart> SampleToReviewParts(
        string assetId,
        string mediaType,
        byte[] bytes,
        int maxFrames = DefaultMaxFrames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException("content_asset_empty", nameof(bytes));
        if (bytes.Length > MaxInputBytes)
            throw new InvalidOperationException("content_gif_too_large");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);

        var type = (mediaType ?? string.Empty).Trim().ToLowerInvariant();
        if (type is not "image/gif")
        {
            return
            [
                ReviewPromptPart.UntrustedImageBytes(assetId.Trim(), string.IsNullOrWhiteSpace(type) ? "image/png" : type, bytes)
            ];
        }

        try
        {
            var frames = SplitGifFrames(bytes);
            if (frames.FrameCount == 0)
                throw new InvalidOperationException("content_gif_decode_failed");
            if (frames.FrameCount > MaxDetectedFrames)
                throw new InvalidOperationException("content_gif_frame_count_exceeded");

            var indexes = SelectFrameIndexes(frames.FrameCount, Math.Min(maxFrames, frames.FrameCount));
            var parts = new List<ReviewPromptPart>(indexes.Count);
            foreach (var index in indexes)
            {
                var frameBytes = ComposeSingleFrameGif(frames, index);
                var partId = frames.FrameCount == 1
                    ? assetId.Trim()
                    : $"{assetId.Trim()}#frame-{index}";
                parts.Add(ReviewPromptPart.UntrustedImageBytes(partId, "image/gif", frameBytes));
            }

            return parts;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("content_gif_decode_failed", ex);
        }
    }

    private sealed record GifFrames(
        byte[] HeaderAndScreen,
        byte[] GlobalColorTable,
        byte[] ApplicationExtensions,
        IReadOnlyList<byte[]> FrameBlocks,
        int FrameCount);

    private static GifFrames SplitGifFrames(byte[] bytes)
    {
        if (bytes.Length < 13
            || bytes[0] != (byte)'G'
            || bytes[1] != (byte)'I'
            || bytes[2] != (byte)'F')
        {
            throw new InvalidOperationException("content_gif_decode_failed");
        }

        var offset = 6; // skip signature/version
        if (offset + 7 > bytes.Length)
            throw new InvalidOperationException("content_gif_decode_failed");

        var packed = bytes[offset + 4];
        var gctFlag = (packed & 0x80) != 0;
        var gctSize = gctFlag ? 3 * (1 << ((packed & 0x07) + 1)) : 0;
        offset += 7;
        if (offset + gctSize > bytes.Length)
            throw new InvalidOperationException("content_gif_decode_failed");

        var headerAndScreen = bytes.AsSpan(0, offset).ToArray();
        var gct = gctSize == 0 ? Array.Empty<byte>() : bytes.AsSpan(offset, gctSize).ToArray();
        offset += gctSize;

        var appExts = new List<byte>();
        var frames = new List<byte[]>();
        byte[]? pendingGce = null;

        while (offset < bytes.Length)
        {
            var b = bytes[offset];
            if (b == 0x3B) // trailer
                break;

            if (b == 0x21) // extension
            {
                if (offset + 2 >= bytes.Length)
                    throw new InvalidOperationException("content_gif_decode_failed");
                var label = bytes[offset + 1];
                var extStart = offset;
                offset += 2;
                offset = SkipDataSubBlocks(bytes, offset);
                var ext = bytes.AsSpan(extStart, offset - extStart).ToArray();
                if (label == 0xF9) // graphic control — binds to next image
                    pendingGce = ext;
                else if (label == 0xFF) // application (loop)
                    appExts.AddRange(ext);
                // ignore comments/plain text for sampling
                continue;
            }

            if (b == 0x2C) // image descriptor
            {
                var frameStart = offset;
                if (pendingGce is not null)
                    frameStart = offset - pendingGce.Length; // not reliable if gaps; rebuild instead

                // Parse image descriptor properly and capture GCE + image as one block.
                var block = new List<byte>();
                if (pendingGce is not null)
                {
                    block.AddRange(pendingGce);
                    pendingGce = null;
                }

                if (offset + 10 > bytes.Length)
                    throw new InvalidOperationException("content_gif_decode_failed");
                block.AddRange(bytes.AsSpan(offset, 10).ToArray());
                var localPacked = bytes[offset + 9];
                var lctFlag = (localPacked & 0x80) != 0;
                var lctSize = lctFlag ? 3 * (1 << ((localPacked & 0x07) + 1)) : 0;
                offset += 10;
                if (offset + lctSize > bytes.Length)
                    throw new InvalidOperationException("content_gif_decode_failed");
                if (lctSize > 0)
                {
                    block.AddRange(bytes.AsSpan(offset, lctSize).ToArray());
                    offset += lctSize;
                }

                if (offset >= bytes.Length)
                    throw new InvalidOperationException("content_gif_decode_failed");
                // LZW min code size
                block.Add(bytes[offset]);
                offset += 1;
                var dataStart = offset;
                offset = SkipDataSubBlocks(bytes, offset);
                block.AddRange(bytes.AsSpan(dataStart, offset - dataStart).ToArray());
                frames.Add(block.ToArray());
                _ = frameStart;
                continue;
            }

            throw new InvalidOperationException("content_gif_decode_failed");
        }

        return new GifFrames(
            headerAndScreen,
            gct,
            appExts.ToArray(),
            frames,
            frames.Count);
    }

    private static int SkipDataSubBlocks(byte[] bytes, int offset)
    {
        while (offset < bytes.Length)
        {
            var size = bytes[offset];
            offset += 1;
            if (size == 0)
                return offset;
            offset += size;
            if (offset > bytes.Length)
                throw new InvalidOperationException("content_gif_decode_failed");
        }

        throw new InvalidOperationException("content_gif_decode_failed");
    }

    private static byte[] ComposeSingleFrameGif(GifFrames source, int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= source.FrameBlocks.Count)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        using var ms = new MemoryStream(
            source.HeaderAndScreen.Length
            + source.GlobalColorTable.Length
            + source.ApplicationExtensions.Length
            + source.FrameBlocks[frameIndex].Length
            + 1);
        ms.Write(source.HeaderAndScreen);
        if (source.GlobalColorTable.Length > 0)
            ms.Write(source.GlobalColorTable);
        if (source.ApplicationExtensions.Length > 0)
            ms.Write(source.ApplicationExtensions);
        ms.Write(source.FrameBlocks[frameIndex]);
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }
}
