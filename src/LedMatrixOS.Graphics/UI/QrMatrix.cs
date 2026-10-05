using System.Text;

namespace LedMatrixOS.Graphics.UI;

/// <summary>Error correction level. Only L and M are supported (they leave the most room for data on a small display).</summary>
public enum QrEcc { L, M }

/// <summary>
/// A QR Code symbol (model 2, byte mode, versions 1 to 10, EC level L or M) encoded into a module matrix.
/// Encoding allocates, so do it once when the text changes and keep the result; reading <see cref="this[int,int]"/> does not.
/// The construction follows the ISO/IEC 18004 procedure: data bits, Reed-Solomon blocks interleaved, function patterns,
/// zigzag codeword placement, the lowest-penalty mask of eight, then format and version information.
/// </summary>
public sealed class QrMatrix
{
    public const int MinVersion = 1;
    public const int MaxVersion = 10;

    // Per version 1..10, index 0 = L, 1 = M.
    private static readonly int[][] EccPerBlock =
    [
        [7, 10, 15, 20, 26, 18, 20, 24, 30, 18],
        [10, 16, 26, 18, 24, 16, 18, 22, 22, 26],
    ];

    private static readonly int[][] BlockCount =
    [
        [1, 1, 1, 1, 1, 2, 2, 2, 2, 4],
        [1, 1, 1, 2, 2, 4, 4, 4, 5, 5],
    ];

    private readonly bool[] _modules;
    private readonly bool[] _function;

    private QrMatrix(int version, QrEcc ecc)
    {
        Version = version;
        Ecc = ecc;
        Size = version * 4 + 17;
        _modules = new bool[Size * Size];
        _function = new bool[Size * Size];
    }

    public int Version { get; }
    public QrEcc Ecc { get; }
    public int Mask { get; private set; }

    /// <summary>Modules per side, without any quiet zone.</summary>
    public int Size { get; }

    /// <summary>True for a dark module. Out of range reads are light (the quiet zone).</summary>
    public bool this[int x, int y] => (uint)x < (uint)Size && (uint)y < (uint)Size && _modules[y * Size + x];

    /// <summary>True for modules that are not data (finder, timing, alignment, format, version); useful for tests.</summary>
    public bool IsFunction(int x, int y) => _function[y * Size + x];

    /// <summary>How many bytes of UTF-8 fit in <paramref name="version"/> at <paramref name="ecc"/>.</summary>
    public static int ByteCapacity(int version, QrEcc ecc)
    {
        int bits = DataCodewords(version, ecc) * 8 - 4 - CountBits(version);
        return Math.Max(0, bits / 8);
    }

    /// <summary>Encodes <paramref name="text"/> as UTF-8 bytes into the smallest version that fits; throws when it exceeds version 10.</summary>
    public static QrMatrix Encode(string text, QrEcc ecc = QrEcc.M)
    {
        var data = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Encode(data, ecc);
    }

    public static bool TryEncode(string text, QrEcc ecc, out QrMatrix? matrix)
    {
        var data = Encoding.UTF8.GetBytes(text ?? string.Empty);
        if (FitVersion(data.Length, ecc) < 0) { matrix = null; return false; }
        matrix = Encode(data, ecc);
        return true;
    }

    public static QrMatrix Encode(byte[] data, QrEcc ecc = QrEcc.M)
    {
        int version = FitVersion(data.Length, ecc);
        if (version < 0) throw new ArgumentException($"Text of {data.Length} bytes does not fit in a version {MaxVersion} QR code at level {ecc}.");

        var qr = new QrMatrix(version, ecc);
        var codewords = qr.Interleave(BuildData(data, version, ecc));
        qr.DrawFunctionPatterns();
        qr.DrawCodewords(codewords);

        int best = 0, bestPenalty = int.MaxValue;
        for (int m = 0; m < 8; m++)
        {
            qr.ApplyMask(m);
            qr.DrawFormatBits(m);
            int penalty = qr.Penalty();
            if (penalty < bestPenalty) { best = m; bestPenalty = penalty; }
            qr.ApplyMask(m);   // XOR again to undo
        }
        qr.ApplyMask(best);
        qr.DrawFormatBits(best);
        qr.Mask = best;
        return qr;
    }

    // ---- sizing ------------------------------------------------------------------------------------------------------

    private static int FitVersion(int byteLength, QrEcc ecc)
    {
        for (int v = MinVersion; v <= MaxVersion; v++)
            if (byteLength <= ByteCapacity(v, ecc)) return v;
        return -1;
    }

    private static int CountBits(int version) => version <= 9 ? 8 : 16;

    private static int RawModules(int version)
    {
        int result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            int align = version / 7 + 2;
            result -= (25 * align - 10) * align - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    private static int DataCodewords(int version, QrEcc ecc) =>
        RawModules(version) / 8 - EccPerBlock[(int)ecc][version - 1] * BlockCount[(int)ecc][version - 1];

    // ---- data and error correction ------------------------------------------------------------------------------------

    private static byte[] BuildData(byte[] data, int version, QrEcc ecc)
    {
        int capacityBits = DataCodewords(version, ecc) * 8;
        var bits = new List<bool>(capacityBits);
        void Append(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }

        Append(0b0100, 4);
        Append(data.Length, CountBits(version));
        foreach (byte b in data) Append(b, 8);
        Append(0, Math.Min(4, capacityBits - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);

        var result = new byte[capacityBits / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
        for (int i = bits.Count / 8, pad = 0; i < result.Length; i++, pad ^= 1)
            result[i] = pad == 0 ? (byte)0xEC : (byte)0x11;
        return result;
    }

    private byte[] Interleave(byte[] data)
    {
        int blocks = BlockCount[(int)Ecc][Version - 1];
        int eccLen = EccPerBlock[(int)Ecc][Version - 1];
        int raw = RawModules(Version) / 8;
        int shortBlocks = blocks - raw % blocks;
        int shortLen = raw / blocks;
        var divisor = ReedSolomon.Divisor(eccLen);

        var all = new byte[blocks][];
        for (int i = 0, k = 0; i < blocks; i++)
        {
            int dataLen = shortLen - eccLen + (i < shortBlocks ? 0 : 1);
            var block = new byte[dataLen + eccLen + (i < shortBlocks ? 1 : 0)];
            Array.Copy(data, k, block, 0, dataLen);
            var ecc = ReedSolomon.Remainder(data.AsSpan(k, dataLen), divisor);
            k += dataLen;
            // Short blocks carry one pad slot where the long ones have their last data byte, so the columns line up.
            Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
            all[i] = block;
        }

        var result = new byte[raw];
        int n = 0;
        for (int i = 0; i < all[0].Length; i++)
            for (int j = 0; j < blocks; j++)
                if (i != shortLen - eccLen || j >= shortBlocks) result[n++] = all[j][i];
        return result;
    }

    // ---- drawing ------------------------------------------------------------------------------------------------------

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y * Size + x] = dark;
        _function[y * Size + x] = true;
    }

    private static int[] AlignmentPositions(int version)
    {
        if (version == 1) return [];
        int count = version / 7 + 2;
        int step = (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, pos = version * 4 + 10; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    private void DrawFunctionPatterns()
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var pos = AlignmentPositions(Version);
        for (int i = 0; i < pos.Length; i++)
            for (int j = 0; j < pos.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)) continue;
                DrawAlignment(pos[i], pos[j]);
            }

        DrawFormatBits(0);
        DrawVersion();
    }

    private void DrawFinder(int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int x = cx + dx, y = cy + dy;
                if ((uint)x < (uint)Size && (uint)y < (uint)Size) SetFunction(x, y, dist != 2 && dist != 4);
            }
    }

    private void DrawAlignment(int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    /// <summary>The 15 format bits (EC level, mask, BCH(15,5) remainder, XOR 0x5412).</summary>
    public static int FormatBits(QrEcc ecc, int mask)
    {
        int data = ((ecc == QrEcc.L ? 1 : 0) << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        return ((data << 10) | rem) ^ 0x5412;
    }

    /// <summary>The 18 version bits (version, BCH(18,6) remainder); only used from version 7.</summary>
    public static int VersionBits(int version)
    {
        int rem = version;
        for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        return (version << 12) | rem;
    }

    private static bool Bit(int value, int i) => ((value >> i) & 1) != 0;

    private void DrawFormatBits(int mask)
    {
        int bits = FormatBits(Ecc, mask);
        for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));

        for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
        for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
        SetFunction(8, Size - 8, true);   // the always-dark module
    }

    private void DrawVersion()
    {
        if (Version < 7) return;
        int bits = VersionBits(Version);
        for (int i = 0; i < 18; i++)
        {
            bool bit = Bit(bits, i);
            int a = Size - 11 + i % 3, b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    private void DrawCodewords(byte[] data)
    {
        int i = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < Size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? Size - 1 - vert : vert;
                    if (_function[y * Size + x] || i >= data.Length * 8) continue;
                    _modules[y * Size + x] = Bit(data[i >> 3], 7 - (i & 7));
                    i++;
                }
        }
    }

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert && !_function[y * Size + x]) _modules[y * Size + x] ^= true;
            }
    }

    // ---- mask penalty ---------------------------------------------------------------------------------------------------

    private int Penalty()
    {
        int result = 0;
        int dark = 0;

        for (int pass = 0; pass < 2; pass++)
        {
            bool Get(int a, int b) => pass == 0 ? _modules[a * Size + b] : _modules[b * Size + a];   // a = line, b = position along it

            for (int a = 0; a < Size; a++)
            {
                int run = 1;
                for (int b = 1; b < Size; b++)
                {
                    if (Get(a, b) == Get(a, b - 1)) { run++; continue; }
                    if (run >= 5) result += 3 + (run - 5);
                    run = 1;
                }
                if (run >= 5) result += 3 + (run - 5);

                // 1:1:3:1:1 finder-like patterns with four light modules on either side.
                for (int b = 0; b + 6 < Size; b++)
                {
                    if (Get(a, b) && !Get(a, b + 1) && Get(a, b + 2) && Get(a, b + 3) && Get(a, b + 4) && !Get(a, b + 5) && Get(a, b + 6))
                    {
                        bool lightBefore = true, lightAfter = true;
                        for (int k = 1; k <= 4; k++)
                        {
                            if (b - k >= 0 && Get(a, b - k)) lightBefore = false;
                            if (b + 6 + k < Size && Get(a, b + 6 + k)) lightAfter = false;
                        }
                        if (lightBefore) result += 40;
                        if (lightAfter) result += 40;
                    }
                }
            }
        }

        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool c = _modules[y * Size + x];
                if (c) dark++;
                if (x + 1 < Size && y + 1 < Size && c == _modules[y * Size + x + 1] && c == _modules[(y + 1) * Size + x] && c == _modules[(y + 1) * Size + x + 1])
                    result += 3;
            }

        int total = Size * Size;
        int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return result + Math.Max(0, k2) * 10;
    }
}

/// <summary>Reed-Solomon over GF(256) with the QR polynomial 0x11D.</summary>
public static class ReedSolomon
{
    public static byte[] Divisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    public static byte[] Remainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < divisor.Length; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    public static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }
}
