using System.Buffers;
using System.Text;
using System.Text.Unicode;
using Microsoft.Extensions.Options;

namespace Login38.Core.Text;

/// <summary>
/// Default <see cref="ILegacyTextCodec"/>: UTF-8 first, then code page 950 / 936.
/// </summary>
public sealed class LegacyTextCodec : ILegacyTextCodec
{
    private const int Big5CodePage = 950;
    private const int GbkCodePage = 936;

    /// <summary>U+FFFD, used as the decoder fallback so failures are detectable by scan.</summary>
    private const char ReplacementChar = '�';

    private static readonly Encoding Big5;
    private static readonly Encoding Gbk;

    static LegacyTextCodec()
    {
        // .NET dropped the legacy code pages from the box; without this,
        // GetEncoding(950) throws NotSupportedException.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Fall back to U+FFFD rather than the default '?', so a decode failure can be
        // detected by scanning the result instead of by catching an exception. Item
        // names are decoded inside a polling loop; exceptions there would be costly.
        Big5 = Encoding.GetEncoding(
            Big5CodePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("�"));
        Gbk = Encoding.GetEncoding(
            GbkCodePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("�"));
    }

    /// <summary>A shared guessing codec, for call sites with no access to configuration.</summary>
    public static LegacyTextCodec Auto { get; } = new(TextEncodingMode.Auto);

    public LegacyTextCodec(TextEncodingMode mode) => Mode = mode;

    public LegacyTextCodec(IOptions<LegacyTextOptions> options)
        : this(options.Value.Mode)
    {
    }

    /// <inheritdoc/>
    public TextEncodingMode Mode { get; }

    /// <inheritdoc/>
    public LegacyString Decode(ReadOnlySpan<byte> bytes) => Decode(bytes, Mode);

    /// <inheritdoc/>
    public LegacyString Decode(ReadOnlySpan<byte> bytes, TextEncodingMode mode)
    {
        bytes = StripUtf8Bom(bytes);

        // Pure ASCII is valid UTF-8, so this also short-circuits the common case of an
        // English item name without touching the code page tables.
        if (Utf8.IsValid(bytes))
        {
            return new LegacyString(Encoding.UTF8.GetString(bytes), LegacyEncoding.Utf8);
        }

        switch (mode)
        {
            case TextEncodingMode.Big5:
                return new LegacyString(Big5.GetString(bytes), LegacyEncoding.Big5);
            case TextEncodingMode.Gbk:
                return new LegacyString(Gbk.GetString(bytes), LegacyEncoding.Gbk);
        }

        var (big5Text, big5Failed) = DecodeLossy(bytes, Big5);
        var (gbkText, gbkFailed) = DecodeLossy(bytes, Gbk);

        // If exactly one code page consumed every byte, that is the answer and no
        // heuristic is needed.
        if (big5Failed != gbkFailed)
        {
            return big5Failed
                ? new LegacyString(gbkText, LegacyEncoding.Gbk)
                : new LegacyString(big5Text, LegacyEncoding.Big5);
        }

        var big5Score = PlausibilityScore(big5Text, big5Failed)
                        + VocabularyScore(big5Text, preferSimplified: false);
        var gbkScore = PlausibilityScore(gbkText, gbkFailed)
                       + VocabularyScore(gbkText, preferSimplified: true);

        // Ties go to Big5: the Taiwanese client is the primary target.
        return gbkScore > big5Score
            ? new LegacyString(gbkText, LegacyEncoding.Gbk)
            : new LegacyString(big5Text, LegacyEncoding.Big5);
    }

    /// <inheritdoc/>
    public string DecodeNullTerminated(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end >= 0)
        {
            bytes = bytes[..end];
        }

        return Decode(bytes).Text;
    }

    /// <inheritdoc/>
    public byte[] Encode(string text, LegacyEncoding encoding) => encoding switch
    {
        LegacyEncoding.Big5 => Big5.GetBytes(text),
        LegacyEncoding.Gbk => Gbk.GetBytes(text),
        _ => Encoding.UTF8.GetBytes(text),
    };

    /// <inheritdoc/>
    public string ReadTextFile(string path) => Decode(File.ReadAllBytes(path)).Text;

    /// <summary>Compiles to a direct reference into the assembly's data section.</summary>
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> StripUtf8Bom(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(Utf8Bom) ? bytes[Utf8Bom.Length..] : bytes;

    /// <summary>
    /// Decodes with the U+FFFD fallback and reports whether anything was replaced.
    /// Safe because neither code page can represent U+FFFD, so its presence in the
    /// output can only mean a byte sequence failed to map.
    /// </summary>
    private static (string Text, bool Failed) DecodeLossy(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        var text = encoding.GetString(bytes);
        return (text, text.Contains(ReplacementChar, StringComparison.Ordinal));
    }

    /// <summary>
    /// Scores how much a decode result looks like real text rather than noise.
    /// CJK ideographs are strong evidence; replacement and control characters are
    /// strong evidence against.
    /// </summary>
    private static int PlausibilityScore(string text, bool failed)
    {
        var score = failed ? -200 : 0;

        foreach (var rune in text.EnumerateRunes())
        {
            // Checked first, so that tabs and newlines count as damage rather than as
            // neutral characters — a stray control byte usually means a bad decode.
            if (rune.Value == ReplacementChar || Rune.IsControl(rune))
            {
                score -= 80;
            }
            else if (rune.Value is >= 0x4E00 and <= 0x9FFF)
            {
                score += 4;
            }
            else if (IsPlausiblePunctuation(rune))
            {
                score += 1;
            }
            else
            {
                score -= 2;
            }
        }

        return score;
    }

    private static bool IsPlausiblePunctuation(Rune rune)
    {
        if (rune.IsAscii)
        {
            var c = (char)rune.Value;
            return char.IsAsciiLetterOrDigit(c)
                   || c is ' ' or '\t' or '\n' or '\f' or '\r'
                   || NeutralAscii.Contains(c);
        }

        return rune.Value is (>= 0x3000 and <= 0x303F)   // CJK punctuation
                          or (>= 0xFF00 and <= 0xFFEF);  // fullwidth forms
    }

    /// <summary>
    /// Breaks ties between two clean decodes by looking for words that only exist in
    /// one script. Both readings of a short item name are usually valid characters;
    /// only the vocabulary tells them apart.
    /// </summary>
    private static int VocabularyScore(string text, bool preferSimplified)
    {
        var preferred = preferSimplified ? SimplifiedHints : TraditionalHints;
        var opposite = preferSimplified ? TraditionalHints : SimplifiedHints;

        var score = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!rune.IsBmp)
            {
                continue;
            }

            var c = (char)rune.Value;
            if (preferred.Contains(c))
            {
                score += 12;
            }
            else if (opposite.Contains(c))
            {
                score -= 6;
            }
        }

        return score;
    }

    private static readonly SearchValues<char> NeutralAscii =
        SearchValues.Create("()[]+-,.:_/#=<>\"';");

    private static readonly SearchValues<char> SimplifiedHints = SearchValues.Create(
        "药银剑剂卷轴龙鸟马岛宝矿护强变术书双枪挥坏锅饭绿蓝红黑简体服务器帐号密码金币死亡骑士斗篷头盔手套长靴盔甲烈炎高级皮革勇敢浓缩终极体力恢复传送村庄指定面包精灵饼干蜡烛");

    private static readonly SearchValues<char> TraditionalHints = SearchValues.Create(
        "藥銀劍劑卷軸龍鳥馬島寶礦護強變術書雙槍揮壞鍋飯綠藍紅黑繁體伺服器帳號密碼金幣死亡騎士斗篷頭盔手套長靴盔甲烈炎高級皮革勇敢濃縮終極體力恢復傳送村莊指定麵包精靈餅乾蠟燭");
}
