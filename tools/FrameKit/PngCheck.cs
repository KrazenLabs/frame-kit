using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FrameKit;

public static class PngCheck
{
    public const int SetupIconSize = 256;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();
    private static readonly (int X, int Y, int StepX, int StepY)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
    private static readonly (int X, int Y, int StepX, int StepY)[] Progressive = [(0, 0, 1, 1)];

    public static string? SetupIconProblem(string path)
    {
        if (!File.Exists(path))
        {
            return $"setup.icon {path}: file not found";
        }
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"cannot read setup.icon {path}";
        }
        if (!IsPng(data))
        {
            return $"setup.icon {path} must be a PNG image";
        }
        try
        {
            var (width, height) = Read(data);
            return width == SetupIconSize && height == SetupIconSize ? null : $"setup.icon {path} must be {SetupIconSize} × {SetupIconSize}";
        }
        catch (InvalidDataException)
        {
            return $"setup.icon {path} is corrupted";
        }
    }

    private static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

    public static (int Width, int Height) Read(byte[] data)
    {
        if (!IsPng(data))
        {
            throw new InvalidDataException();
        }
        var position = Signature.Length;
        var header = default((int Width, int Height, int Depth, int Color, bool Interlaced)?);
        var compressed = new MemoryStream();
        var palette = false;
        while (true)
        {
            if (position + 12 > data.Length)
            {
                throw new InvalidDataException();
            }
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            if (length > int.MaxValue || position + 12 + (long)length > data.Length)
            {
                throw new InvalidDataException();
            }
            var type = Encoding.ASCII.GetString(data, position + 4, 4);
            var body = data.AsSpan(position + 8, (int)length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8 + (int)length));
            if (Crc(data.AsSpan(position + 4, 4 + (int)length)) != crc)
            {
                throw new InvalidDataException();
            }
            position += 12 + (int)length;
            if (header is null)
            {
                header = type == "IHDR" && length == 13 ? ReadHeader(body) : throw new InvalidDataException();
                continue;
            }
            switch (type)
            {
                case "PLTE":
                    palette = true;
                    break;
                case "IDAT":
                    compressed.Write(body);
                    break;
                case "IEND":
                    var (width, height, depth, color, interlaced) = header.Value;
                    if (color == 3 && !palette)
                    {
                        throw new InvalidDataException();
                    }
                    CheckImageData(compressed, width, height, BitsPerPixel(depth, color), interlaced);
                    return (width, height);
            }
        }
    }

    private static (int Width, int Height, int Depth, int Color, bool Interlaced) ReadHeader(ReadOnlySpan<byte> body)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(body);
        var height = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
        int depth = body[8], color = body[9];
        var validDepth = color switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => depth is 8 or 16,
            _ => false,
        };
        if (width is 0 or > 16384 || height is 0 or > 16384 || !validDepth || body[10] != 0 || body[11] != 0 || body[12] > 1)
        {
            throw new InvalidDataException();
        }
        return ((int)width, (int)height, depth, color, body[12] == 1);
    }

    private static int BitsPerPixel(int depth, int color) => depth * color switch
    {
        2 => 3,
        4 => 2,
        6 => 4,
        _ => 1,
    };

    private static void CheckImageData(MemoryStream compressed, int width, int height, int bitsPerPixel, bool interlaced)
    {
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[(width * (long)bitsPerPixel + 7) / 8 + 1];
        foreach (var (x, y, stepX, stepY) in interlaced ? Adam7 : Progressive)
        {
            var passWidth = (width - x + stepX - 1) / stepX;
            var passHeight = (height - y + stepY - 1) / stepY;
            if (passWidth == 0 || passHeight == 0)
            {
                continue;
            }
            var rowLength = (int)((passWidth * (long)bitsPerPixel + 7) / 8) + 1;
            for (var line = 0; line < passHeight; line++)
            {
                try
                {
                    inflater.ReadExactly(row, 0, rowLength);
                }
                catch (EndOfStreamException)
                {
                    throw new InvalidDataException();
                }
                if (row[0] > 4)
                {
                    throw new InvalidDataException();
                }
            }
        }
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
