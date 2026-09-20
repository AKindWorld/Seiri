namespace Seiri.Core;

public static class ColorBucket
{
    public static readonly string[] Names =
    [
        "red", "orange", "yellow", "green", "cyan", "blue", "purple", "pink", "brown", "gray", "black", "white"
    ];

    public static string FromBgra(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return "gray";
        }

        long r = 0, g = 0, b = 0, n = 0;
        var step = Math.Max(1, (width * height) / 1024);
        for (var i = 0; i < width * height; i += step)
        {
            var o = i * 4;
            if (o + 2 >= bgra.Length)
            {
                break;
            }

            b += bgra[o];
            g += bgra[o + 1];
            r += bgra[o + 2];
            n++;
        }

        if (n == 0)
        {
            return "gray";
        }

        var rf = r / (255.0 * n);
        var gf = g / (255.0 * n);
        var bf = b / (255.0 * n);
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;
        var v = max;
        var s = max <= 0 ? 0 : delta / max;
        var h = 0.0;
        if (delta > 0)
        {
            if (max == rf)
            {
                h = 60 * (((gf - bf) / delta) % 6);
            }
            else if (max == gf)
            {
                h = 60 * (((bf - rf) / delta) + 2);
            }
            else
            {
                h = 60 * (((rf - gf) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        if (v < 0.12)
        {
            return "black";
        }

        if (s < 0.12)
        {
            return v > 0.85 ? "white" : "gray";
        }

        if (h < 15 || h >= 345)
        {
            return s < 0.45 && v < 0.55 ? "brown" : "red";
        }

        if (h < 45)
        {
            return "orange";
        }

        if (h < 70)
        {
            return "yellow";
        }

        if (h < 160)
        {
            return "green";
        }

        if (h < 200)
        {
            return "cyan";
        }

        if (h < 255)
        {
            return "blue";
        }

        if (h < 290)
        {
            return "purple";
        }

        return "pink";
    }
}
