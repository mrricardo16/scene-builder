using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SceneBuilder.Blender;

public sealed class BinaryGlbValidator
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;

    public GlbValidationResult Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_MISSING");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            return Validate(stream, leaveOpen: true);
        }
        catch (IOException)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_MISSING");
        }
        catch (UnauthorizedAccessException)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_MISSING");
        }
    }

    public GlbValidationResult Validate(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
        }

        var initialPosition = stream.Position;
        try
        {
            stream.Position = 0;
            if (stream.Length < 20 || stream.Length > int.MaxValue || stream.Length % 4 != 0)
            {
                return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
            }

            var bytes = new byte[checked((int)stream.Length)];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0)
                {
                    return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
                }

                read += count;
            }

            return ValidateBytes(bytes);
        }
        catch (IOException)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
        }
        finally
        {
            if (leaveOpen)
            {
                stream.Position = initialPosition;
            }
            else
            {
                stream.Dispose();
            }
        }
    }

    private static GlbValidationResult ValidateBytes(byte[] bytes)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != 2 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != bytes.Length)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
        }

        var offset = 12;
        if (!TryReadChunk(bytes, ref offset, out var jsonLength, out var jsonType) || jsonType != JsonChunkType)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
        }

        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 20, checked((int)jsonLength)));
            var root = document.RootElement;
            if (!root.TryGetProperty("asset", out var asset) || !asset.TryGetProperty("version", out var version) || version.GetString() != "2.0" ||
                !root.TryGetProperty("scene", out _) || !root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() == 0)
            {
                return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
            }
        }
        catch (JsonException)
        {
            return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
        }

        while (offset < bytes.Length)
        {
            if (!TryReadChunk(bytes, ref offset, out _, out _))
            {
                return GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
            }
        }

        return offset == bytes.Length ? GlbValidationResult.Succeeded() : GlbValidationResult.Failed("BLENDER_OUTPUT_INVALID");
    }

    private static bool TryReadChunk(byte[] bytes, ref int offset, out uint length, out uint type)
    {
        length = 0;
        type = 0;
        if (offset > bytes.Length - 8)
        {
            return false;
        }

        length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
        if (length % 4 != 0 || length > bytes.Length - offset - 8)
        {
            return false;
        }

        offset += checked(8 + (int)length);
        return true;
    }
}

public sealed record GlbValidationResult(bool IsValid, string? DiagnosticCode)
{
    public static GlbValidationResult Succeeded() => new(true, null);

    public static GlbValidationResult Failed(string code) => new(false, code);
}
