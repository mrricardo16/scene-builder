using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace SceneBuilder.Blender.Tests;

public sealed class BinaryGlbValidatorStreamTests
{
    [Fact]
    public void Validate_stream_preserves_the_callers_position_and_open_ownership()
    {
        using var stream = new MemoryStream(CreateMinimalGlb());
        stream.Position = 4;

        var result = new BinaryGlbValidator().Validate(stream, leaveOpen: true);

        Assert.True(result.IsValid);
        Assert.Equal(4, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Validate_stream_rejects_invalid_magic_without_mutating_input()
    {
        var bytes = CreateMinimalGlb();
        bytes[0] = 0;
        using var stream = new MemoryStream(bytes);

        var result = new BinaryGlbValidator().Validate(stream, leaveOpen: true);

        Assert.False(result.IsValid);
        Assert.Equal("BLENDER_OUTPUT_INVALID", result.DiagnosticCode);
        Assert.Equal(0, stream.Position);
    }

    private static byte[] CreateMinimalGlb()
    {
        var json = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"nodes\":[{}]}");
        var paddedLength = (json.Length + 3) & ~3;
        var bytes = new byte[20 + paddedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)paddedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4E4F534A);
        json.CopyTo(bytes, 20);
        Array.Fill(bytes, (byte)0x20, 20 + json.Length, paddedLength - json.Length);
        return bytes;
    }
}
