namespace Seiri.Infrastructure;

public static class ImageDimensions
{
    public static (int Width, int Height)? TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[32];
            var n = stream.Read(header);
            if (n < 16)
            {
                return null;
            }

            if (header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
            {
                return (ReadBe32(header, 16), ReadBe32(header, 20));
            }

            if (header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
            {
                return (header[6] | (header[7] << 8), header[8] | (header[9] << 8));
            }

            if (header[0] == (byte)'B' && header[1] == (byte)'M' && n >= 26)
            {
                return (ReadLe32(header, 18), Math.Abs(ReadLe32(header, 22)));
            }

            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                stream.Position = 0;
                return ReadJpeg(stream);
            }

            if (header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                && n >= 30 && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            {
                return ReadWebp(header, stream);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static (int, int)? ReadJpeg(FileStream stream)
    {
        stream.Position = 2;
        while (stream.Position < stream.Length)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return null;
            }

            if (b != 0xFF)
            {
                continue;
            }

            int marker;
            do
            {
                marker = stream.ReadByte();
            }
            while (marker == 0xFF);

            if (marker < 0 || marker == 0xD9 || marker == 0xDA)
            {
                return null;
            }

            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                continue;
            }

            var len = (stream.ReadByte() << 8) | stream.ReadByte();
            if (len < 2)
            {
                return null;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC9 or 0xCA or 0xCB)
            {
                stream.ReadByte();
                var h = (stream.ReadByte() << 8) | stream.ReadByte();
                var w = (stream.ReadByte() << 8) | stream.ReadByte();
                if (w > 0 && h > 0)
                {
                    return (w, h);
                }

                return null;
            }

            stream.Position += len - 2;
        }

        return null;
    }

    private static (int, int)? ReadWebp(ReadOnlySpan<byte> header, FileStream stream)
    {
        var tag = System.Text.Encoding.ASCII.GetString(header.Slice(12, 4));
        if (tag == "VP8X" && header.Length >= 30)
        {
            var w = 1 + header[24] + (header[25] << 8) + (header[26] << 16);
            var h = 1 + header[27] + (header[28] << 8) + (header[29] << 16);
            return (w, h);
        }

        if (tag == "VP8 " && header.Length >= 30)
        {
            stream.Position = 26;
            Span<byte> buf = stackalloc byte[4];
            if (stream.Read(buf) < 4)
            {
                return null;
            }

            var w = buf[0] | ((buf[1] & 0x3F) << 8);
            var h = buf[2] | ((buf[3] & 0x3F) << 8);
            return (w, h);
        }

        return null;
    }

    private static int ReadBe32(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static int ReadLe32(ReadOnlySpan<byte> data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
}
