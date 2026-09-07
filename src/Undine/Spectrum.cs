namespace Undine;

/// <summary>
/// A square complex FFT, radix 2, for the spectral step of <see cref="Surface"/>. Sizes are powers of two; the
/// transform is unnormalized forward and divides by the count on the way back.
/// </summary>
public static class Spectrum
{
    /// <summary>Transforms <paramref name="re"/> and <paramref name="im"/>, both <paramref name="size"/>² row-major, in place along both axes.</summary>
    public static void Transform(Span<float> re, Span<float> im, int size, bool inverse)
    {
        if ((size & (size - 1)) != 0 || size < 2)
        {
            throw new ArgumentException("The size must be a power of two.", nameof(size));
        }

        if (re.Length != size * size || im.Length != size * size)
        {
            throw new ArgumentException("The arrays must hold size² values.");
        }

        Span<float> lineRe = stackalloc float[size];
        Span<float> lineIm = stackalloc float[size];
        for (int y = 0; y < size; y++)
        {
            Line(re.Slice(y * size, size), im.Slice(y * size, size), inverse);
        }

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                lineRe[y] = re[y * size + x];
                lineIm[y] = im[y * size + x];
            }

            Line(lineRe, lineIm, inverse);
            for (int y = 0; y < size; y++)
            {
                re[y * size + x] = lineRe[y];
                im[y * size + x] = lineIm[y];
            }
        }

        if (inverse)
        {
            float scale = 1f / (size * (float)size);
            for (int i = 0; i < re.Length; i++)
            {
                re[i] *= scale;
                im[i] *= scale;
            }
        }
    }

    /// <summary>One line, iterative radix 2 with bit reversal.</summary>
    private static void Line(Span<float> re, Span<float> im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = 2 * Math.PI / length * (inverse ? 1 : -1);
            float wRe = (float)Math.Cos(angle), wIm = (float)Math.Sin(angle);
            for (int start = 0; start < n; start += length)
            {
                float cRe = 1f, cIm = 0f;
                int half = length >> 1;
                for (int k = 0; k < half; k++)
                {
                    int a = start + k, b = a + half;
                    float tRe = re[b] * cRe - im[b] * cIm;
                    float tIm = re[b] * cIm + im[b] * cRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    (cRe, cIm) = (cRe * wRe - cIm * wIm, cRe * wIm + cIm * wRe);
                }
            }
        }
    }
}
