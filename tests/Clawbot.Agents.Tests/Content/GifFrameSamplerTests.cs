using System.Buffers.Binary;
using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

// Phase 2.11: deterministic evenly-spaced GIF frame sampling with named caps.
public sealed class GifFrameSamplerTests
{
    [Theory]
    [InlineData(1, 3, new[] { 0 })]
    [InlineData(3, 3, new[] { 0, 1, 2 })]
    [InlineData(5, 3, new[] { 0, 2, 4 })]
    [InlineData(10, 4, new[] { 0, 3, 6, 9 })]
    [InlineData(7, 1, new[] { 0 })]
    public void SelectFrameIndexes_is_evenly_spaced_and_deterministic(int total, int max, int[] expected)
    {
        GifFrameSampler.SelectFrameIndexes(total, max).Should().Equal(expected);
        GifFrameSampler.SelectFrameIndexes(total, max).Should().Equal(expected);
    }

    [Fact]
    public void SelectFrameIndexes_rejects_invalid_bounds()
    {
        var actZero = () => GifFrameSampler.SelectFrameIndexes(0, 3);
        actZero.Should().Throw<ArgumentOutOfRangeException>();
        var actNegMax = () => GifFrameSampler.SelectFrameIndexes(5, 0);
        actNegMax.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Sample_static_png_returns_single_frame_part()
    {
        var png = MinimalPng();
        var parts = GifFrameSampler.SampleToReviewParts("asset-1", "image/png", png, maxFrames: 3);

        parts.Should().ContainSingle();
        parts[0].PartId.Should().Be("asset-1");
        parts[0].MediaType.Should().Be("image/png");
        parts[0].Bytes.Should().Equal(png);
    }

    [Fact]
    public void Sample_animated_gif_returns_evenly_spaced_frame_parts_with_stable_ids()
    {
        // Synthetic multi-frame GIF built by the sampler test helper (not ImageSharp dependency in tests).
        var gif = BuildMultiFrameGif(frameCount: 5);
        var parts = GifFrameSampler.SampleToReviewParts("gif-asset", "image/gif", gif, maxFrames: 3);

        parts.Should().HaveCount(3);
        parts.Select(p => p.PartId).Should().Equal("gif-asset#frame-0", "gif-asset#frame-2", "gif-asset#frame-4");
        parts.Should().OnlyContain(p => p.MediaType == "image/gif" || p.MediaType == "image/png");
        parts.Should().OnlyContain(p => p.Bytes != null && p.Bytes.Count > 0);
    }

    [Fact]
    public void Sample_rejects_oversized_or_too_many_frames()
    {
        var huge = new byte[GifFrameSampler.MaxInputBytes + 1];
        huge[0] = (byte)'G'; huge[1] = (byte)'I'; huge[2] = (byte)'F';
        var act = () => GifFrameSampler.SampleToReviewParts("a", "image/gif", huge, maxFrames: 3);
        act.Should().Throw<InvalidOperationException>().WithMessage("content_gif_too_large");

        var gif = BuildMultiFrameGif(frameCount: GifFrameSampler.MaxDetectedFrames + 1);
        var act2 = () => GifFrameSampler.SampleToReviewParts("a", "image/gif", gif, maxFrames: 3);
        act2.Should().Throw<InvalidOperationException>().WithMessage("content_gif_frame_count_exceeded");
    }

    [Fact]
    public void Sample_corrupt_gif_fails_closed()
    {
        var act = () => GifFrameSampler.SampleToReviewParts("a", "image/gif", "not-a-gif"u8.ToArray(), 3);
        act.Should().Throw<InvalidOperationException>().WithMessage("content_gif_decode_failed");
    }

    private static byte[] MinimalPng()
    {
        var bytes = new byte[32];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        return bytes;
    }

    // Minimal GIF89a with N identical 1x1 frames (graphic control + image descriptor + tiny LZW).
    private static byte[] BuildMultiFrameGif(int frameCount)
    {
        using var ms = new MemoryStream();
        // Header + Logical Screen Descriptor (1x1, GCT flag off)
        ms.Write("GIF89a"u8);
        BinaryPrimitives.WriteUInt16LittleEndian(stackalloc byte[2] { 1, 0 }, 1);
        // rewrite properly
        ms.Position = 6;
        Span<byte> lsd = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(lsd, 1); // width
        BinaryPrimitives.WriteUInt16LittleEndian(lsd[2..], 1); // height
        lsd[4] = 0x00; // no GCT
        lsd[5] = 0x00; // bg
        lsd[6] = 0x00; // aspect
        ms.Write(lsd);

        // Netscape loop extension (optional)
        ms.Write(new byte[] { 0x21, 0xFF, 0x0B });
        ms.Write("NETSCAPE2.0"u8);
        ms.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

        for (var i = 0; i < frameCount; i++)
        {
            // Graphic Control Extension
            ms.Write(new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00 });
            // Image Descriptor 1x1 at 0,0
            ms.Write(new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 });
            // Minimal LZW image data: min code size 2 + one sub-block
            ms.Write(new byte[] { 0x02, 0x02, 0x44, 0x01, 0x00 });
        }

        ms.WriteByte(0x3B); // trailer
        return ms.ToArray();
    }
}
