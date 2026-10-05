using System.Text;
using LedMatrixOS.Core;
using LedMatrixOS.Graphics.Text;
using LedMatrixOS.Graphics.UI;
using Xunit;

namespace LedMatrixOS.Tests;

/// <summary>
/// The encoder is checked without a QR decoder: published constants (format and version words, a worked Reed-Solomon example, capacities),
/// structural patterns, and a small independent reader that unmasks the symbol, walks the zigzag, checks every Reed-Solomon block with its own
/// GF(256) tables (syndromes must all be zero) and parses the byte-mode payload back out.
/// </summary>
public class QrMatrixTests
{
    // ---- published constants -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(QrEcc.M, 0, 0x5412)]
    [InlineData(QrEcc.M, 1, 0x5125)]
    [InlineData(QrEcc.M, 2, 0x5E7C)]
    [InlineData(QrEcc.M, 7, 0x4AA0)]
    [InlineData(QrEcc.L, 0, 0x77C4)]
    [InlineData(QrEcc.L, 1, 0x72F3)]
    [InlineData(QrEcc.L, 7, 0x6976)]
    public void FormatBits_MatchTheStandardTable(QrEcc ecc, int mask, int expected) =>
        Assert.Equal(expected, QrMatrix.FormatBits(ecc, mask));

    [Theory]
    [InlineData(7, 0x07C94)]
    [InlineData(8, 0x085BC)]
    [InlineData(10, 0x0A4D3)]
    public void VersionBits_MatchTheStandardTable(int version, int expected) =>
        Assert.Equal(expected, QrMatrix.VersionBits(version));

    [Fact]
    public void ReedSolomon_MatchesTheWorkedHelloWorldExample()
    {
        // The widely published "HELLO WORLD" 1-M example: 16 data codewords, 10 error correction codewords.
        byte[] data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];
        byte[] expected = [196, 35, 39, 119, 235, 215, 231, 226, 93, 23];
        Assert.Equal(expected, ReedSolomon.Remainder(data, ReedSolomon.Divisor(10)));
    }

    [Theory]
    [InlineData(1, QrEcc.L, 17)]
    [InlineData(1, QrEcc.M, 14)]
    [InlineData(2, QrEcc.L, 32)]
    [InlineData(2, QrEcc.M, 26)]
    [InlineData(5, QrEcc.M, 84)]
    [InlineData(10, QrEcc.L, 271)]
    [InlineData(10, QrEcc.M, 213)]
    public void ByteCapacity_MatchesTheStandard(int version, QrEcc ecc, int capacity) =>
        Assert.Equal(capacity, QrMatrix.ByteCapacity(version, ecc));

    [Fact]
    public void VersionGrowsWithThePayload()
    {
        Assert.Equal(1, QrMatrix.Encode(new string('a', 14), QrEcc.M).Version);
        Assert.Equal(2, QrMatrix.Encode(new string('a', 15), QrEcc.M).Version);
        Assert.Equal(1, QrMatrix.Encode(new string('a', 17), QrEcc.L).Version);
        Assert.Equal(10, QrMatrix.Encode(new string('a', 213), QrEcc.M).Version);
        Assert.Equal(21, QrMatrix.Encode("hi").Size);
        Assert.Equal(57, QrMatrix.Encode(new string('a', 213), QrEcc.M).Size);
    }

    [Fact]
    public void TooLong_ThrowsOrReportsFailure()
    {
        var text = new string('a', 214);
        Assert.Throws<ArgumentException>(() => QrMatrix.Encode(text, QrEcc.M));
        Assert.False(QrMatrix.TryEncode(text, QrEcc.M, out var m));
        Assert.Null(m);
        Assert.True(QrMatrix.TryEncode(text, QrEcc.L, out _));   // fits at L
    }

    // ---- structure ---------------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ecc in new[] { QrEcc.L, QrEcc.M })
            foreach (var text in new[]
            {
                "A", "hello", "https://example.com", "WIFI:T:WPA;S:Home;P:secret;;",
                "https://example.com/some/longer/path?with=query&and=more#fragment-1234567890",
                "Unicode: café ☃ \U0001F389", new string('x', 120), new string('7', 200),
            })
                yield return [text, ecc];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Structure_FinderTimingAndDarkModule(string text, QrEcc ecc)
    {
        var m = QrMatrix.Encode(text, ecc);
        int n = m.Size;

        foreach (var (ox, oy) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 7; x++)
                {
                    bool ring = x is 0 or 6 || y is 0 or 6;
                    bool core = x is >= 2 and <= 4 && y is >= 2 and <= 4;
                    Assert.Equal(ring || core, m[ox + x, oy + y]);
                }

        // separators around the finders are light
        for (int i = 0; i < 8; i++)
        {
            Assert.False(m[7, i]); Assert.False(m[i, 7]);
            Assert.False(m[n - 8, i]); Assert.False(m[n - 1 - i, 7]);
            Assert.False(m[7, n - 1 - i]); Assert.False(m[i, n - 8]);
        }

        for (int i = 8; i < n - 8; i++)
        {
            Assert.Equal(i % 2 == 0, m[i, 6]);
            Assert.Equal(i % 2 == 0, m[6, i]);
        }

        Assert.True(m[8, n - 8]);   // the always-dark module
        Assert.False(m[-1, 0]);     // quiet zone reads light
        Assert.False(m[n, n]);
    }

    [Fact]
    public void Alignment_PatternSitsAtTheStandardPositions()
    {
        var m = QrMatrix.Encode(new string('a', 20), QrEcc.M);   // version 2: alignment centre (18, 18)
        Assert.Equal(2, m.Version);
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Assert.Equal(Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1, m[18 + dx, 18 + dy]);

        var big = QrMatrix.Encode(new string('a', 170), QrEcc.L);   // version 8: centres 6, 24, 42 (but not at the three finder corners)
        Assert.Equal(8, big.Version);
        Assert.True(big[24, 24]); Assert.True(big[42, 24]); Assert.True(big[24, 42]); Assert.True(big[42, 42]);
        Assert.False(big[25, 24]);
        Assert.True(big[26, 24]);   // ring
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FormatInfo_BothCopiesAgreeAndMatchTheMask(string text, QrEcc ecc)
    {
        var m = QrMatrix.Encode(text, ecc);
        int n = m.Size;
        int expected = QrMatrix.FormatBits(ecc, m.Mask);

        int a = 0, b = 0;
        int[] ax = [8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0];
        int[] ay = [0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8];
        for (int i = 0; i < 15; i++) if (m[ax[i], ay[i]]) a |= 1 << i;
        for (int i = 0; i < 8; i++) if (m[n - 1 - i, 8]) b |= 1 << i;
        for (int i = 8; i < 15; i++) if (m[8, n - 15 + i]) b |= 1 << i;

        Assert.Equal(expected, a);
        Assert.Equal(expected, b);
    }

    [Fact]
    public void VersionInfo_IsWrittenInBothBlocksFromVersion7()
    {
        var m = QrMatrix.Encode(new string('a', 170), QrEcc.L);   // version 8
        int n = m.Size, bits = QrMatrix.VersionBits(m.Version);
        for (int i = 0; i < 18; i++)
        {
            bool bit = ((bits >> i) & 1) != 0;
            Assert.Equal(bit, m[n - 11 + i % 3, i / 3]);
            Assert.Equal(bit, m[i / 3, n - 11 + i % 3]);
        }
    }

    // ---- independent reader ------------------------------------------------------------------------------------------

    private static readonly int[] Exp = new int[512];
    private static readonly int[] Log = new int[256];

    static QrMatrixTests()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = x;
            Log[x] = i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static int GfMul(int a, int b) => a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]];

    private static bool MaskBit(int mask, int x, int y) => mask switch
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

    // Version 1..10, [L, M]: (blocks, error correction codewords per block)
    private static (int Blocks, int Ecc) Layout(int version, QrEcc ecc)
    {
        int[][] blocks = [[1, 1, 1, 1, 1, 2, 2, 2, 2, 4], [1, 1, 1, 2, 2, 4, 4, 4, 5, 5]];
        int[][] per = [[7, 10, 15, 20, 26, 18, 20, 24, 30, 18], [10, 16, 26, 18, 24, 16, 18, 22, 22, 26]];
        return (blocks[(int)ecc][version - 1], per[(int)ecc][version - 1]);
    }

    // Total codewords per version, from the standard's table (not computed with the encoder's formula).
    private static readonly int[] TotalCodewords = [26, 44, 70, 100, 134, 172, 196, 242, 292, 346];

    private static string Decode(QrMatrix m)
    {
        int n = m.Size;
        var bytes = new List<byte>();
        int acc = 0, count = 0;
        for (int right = n - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < n; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? n - 1 - vert : vert;
                    if (m.IsFunction(x, y)) continue;
                    bool bit = m[x, y] ^ MaskBit(m.Mask, x, y);
                    acc = (acc << 1) | (bit ? 1 : 0);
                    if (++count == 8) { bytes.Add((byte)acc); acc = 0; count = 0; }
                }
        }

        int total = TotalCodewords[m.Version - 1];
        Assert.Equal(total, bytes.Count);   // (leftover remainder bits are not whole codewords)

        var (blocks, eccLen) = Layout(m.Version, m.Ecc);
        int shortBlocks = blocks - total % blocks;
        int shortLen = total / blocks;
        var block = new List<byte>[blocks];
        for (int i = 0; i < blocks; i++) block[i] = [];

        // De-interleave: data columns first (short blocks run out one column early), then the error correction columns.
        int p = 0;
        for (int col = 0; col < shortLen - eccLen + 1; col++)
            for (int b = 0; b < blocks; b++)
            {
                if (col == shortLen - eccLen && b < shortBlocks) continue;
                block[b].Add(bytes[p++]);
            }
        for (int col = 0; col < eccLen; col++)
            for (int b = 0; b < blocks; b++) block[b].Add(bytes[p++]);
        Assert.Equal(total, p);

        // Every block must be a codeword: it evaluates to zero at alpha^0 .. alpha^(ecc-1).
        foreach (var cw in block)
            for (int root = 0; root < eccLen; root++)
            {
                int sum = 0;
                foreach (byte c in cw) sum = GfMul(sum, Exp[root]) ^ c;
                Assert.Equal(0, sum);
            }

        var data = new List<byte>();
        for (int b = 0; b < blocks; b++) data.AddRange(block[b].Take(block[b].Count - eccLen));

        int pos = 0;
        int Read(int bits)
        {
            int v = 0;
            for (int i = 0; i < bits; i++, pos++) v = (v << 1) | ((data[pos >> 3] >> (7 - (pos & 7))) & 1);
            return v;
        }

        Assert.Equal(0b0100, Read(4));
        int length = Read(m.Version <= 9 ? 8 : 16);
        var text = new byte[length];
        for (int i = 0; i < length; i++) text[i] = (byte)Read(8);
        Assert.Equal(0, Read(Math.Min(4, data.Count * 8 - pos)));   // terminator
        return Encoding.UTF8.GetString(text);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTrip_ReaderRecoversTheTextAndEveryBlockIsAValidCodeword(string text, QrEcc ecc) =>
        Assert.Equal(text, Decode(QrMatrix.Encode(text, ecc)));

    [Fact]
    public void RoundTrip_EveryVersionAtBothLevels()
    {
        for (int version = 1; version <= 10; version++)
            foreach (var ecc in new[] { QrEcc.L, QrEcc.M })
            {
                int length = QrMatrix.ByteCapacity(version, ecc);
                var text = new string(Enumerable.Range(0, length).Select(i => (char)('!' + i % 90)).ToArray());
                var m = QrMatrix.Encode(text, ecc);
                Assert.Equal(version, m.Version);
                Assert.Equal(text, Decode(m));
            }
    }

    [Fact]
    public void Encoding_IsDeterministic()
    {
        var a = QrMatrix.Encode("https://example.com", QrEcc.M);
        var b = QrMatrix.Encode("https://example.com", QrEcc.M);
        Assert.Equal(a.Mask, b.Mask);
        for (int y = 0; y < a.Size; y++)
            for (int x = 0; x < a.Size; x++) Assert.Equal(a[x, y], b[x, y]);
    }

    // ---- known matrix ------------------------------------------------------------------------------------------------

    [Fact]
    public void ShortString_MatrixIsStable()
    {
        // A pinned matrix for "A" at level L (version 1, 21x21). Together with the independent reader above this is a regression guard (the reader above is what proves it is a valid symbol) for the mask
        // choice and codeword placement against accidental change. Rows are '#' for dark.
        var m = QrMatrix.Encode("A", QrEcc.L);
        var rows = Enumerable.Range(0, m.Size).Select(y => new string(Enumerable.Range(0, m.Size).Select(x => m[x, y] ? '#' : '.').ToArray())).ToArray();
        Assert.Equal(21, rows.Length);
        Assert.Equal("#######" + ".", rows[0][..8]);
        Assert.Equal(PinnedA, string.Join("/", rows));
    }

    private const string PinnedA = "#######..#.##.#######/#.....#..###..#.....#/#.###.#.##.##.#.###.#/#.###.#..#.#..#.###.#/#.###.#...#.#.#.###.#/#.....#.....#.#.....#/#######.#.#.#.#######/........##.##......../###.########.##...#../#.##....#.....#...##./.#.####..##.#...#...#/.#.##...##....#...#../..##.##.#...#.#.#.#.#/........#..#.#.#.#.#./#######.#.##.###.####/#.....#.######.###.../#.###.#.##.#.###.##.#/#.###.#..##...#...##./#.###.#.##..#...#...#/#.....#.#.....#...##./#######.###.#.#.#.###";

    // ---- node --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Node_DrawsTheMatrixAtAWholeScaleWithAQuietZone()
    {
        Fonts.Load();
        var node = new QrCode("hello") { On = new Pixel(0, 0, 0), Off = new Pixel(255, 255, 255), QuietZone = 2, Width = 64, Height = 64 };
        var m = node.Matrix!;
        Assert.Equal(21, m.Size);   // version 1 at M holds 14 bytes
        Assert.Equal(25, node.TotalModules);
        Assert.Equal(2, node.ScaleFor(64));

        var frame = new FrameBuffer(64, 64);
        frame.Clear(new Pixel(9, 9, 9));
        node.Measure(64, 64);
        node.Arrange(new SixLabors.ImageSharp.Rectangle(0, 0, 64, 64));
        node.Paint(frame, 0, 0);

        // 25 modules * 2 px = 50 px square, centred in 64: origin 7
        const int origin = 7;
        Assert.Equal(new Pixel(9, 9, 9), frame.GetPixel(origin - 1, origin));
        Assert.Equal(new Pixel(255, 255, 255), frame.GetPixel(origin, origin));           // quiet zone
        for (int y = 0; y < m.Size; y++)
            for (int x = 0; x < m.Size; x++)
            {
                var expected = m[x, y] ? new Pixel(0, 0, 0) : new Pixel(255, 255, 255);
                int px = origin + (x + 2) * 2, py = origin + (y + 2) * 2;
                Assert.Equal(expected, frame.GetPixel(px, py));
                Assert.Equal(expected, frame.GetPixel(px + 1, py + 1));
            }
    }

    [Fact]
    public void Node_ReEncodesOnlyWhenTheTextChanges()
    {
        var node = new QrCode("one");
        var first = node.Matrix;
        node.Text = "one";
        Assert.Same(first, node.Matrix);
        node.Text = "two";
        Assert.NotSame(first, node.Matrix);
        node.Text = "";
        Assert.Null(node.Matrix);
        Assert.Equal(0, node.TotalModules);
    }
}
