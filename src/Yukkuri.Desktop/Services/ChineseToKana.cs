using Pinyin4net;

namespace Yukkuri.Services;

/// <summary>
/// Converts Chinese text into the katakana-style reading accepted by AquesTalk1.
/// The mapping is intentionally local so QQ drafts never leave the machine.
/// </summary>
public static class ChineseToKana
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Mapping = new(LoadMapping);

    private static readonly IReadOnlyDictionary<char, string> Digits = new Dictionary<char, string>
    {
        ['0'] = "ゼロ",
        ['1'] = "イチ",
        ['2'] = "ニ",
        ['3'] = "サン",
        ['4'] = "ヨン",
        ['5'] = "ゴ",
        ['6'] = "ロク",
        ['7'] = "ナナ",
        ['8'] = "ハチ",
        ['9'] = "キュウ"
    };

    private static readonly IReadOnlyDictionary<char, string> Letters = new Dictionary<char, string>
    {
        ['a'] = "エー", ['b'] = "ビー", ['c'] = "シー", ['d'] = "ディー", ['e'] = "イー",
        ['f'] = "エフ", ['g'] = "ジー", ['h'] = "エイチ", ['i'] = "アイ", ['j'] = "ジェイ",
        ['k'] = "ケー", ['l'] = "エル", ['m'] = "エム", ['n'] = "エヌ", ['o'] = "オー",
        ['p'] = "ピー", ['q'] = "キュー", ['r'] = "アール", ['s'] = "エス", ['t'] = "ティー",
        ['u'] = "ユー", ['v'] = "ブイ", ['w'] = "ダブリュー", ['x'] = "エックス",
        ['y'] = "ワイ", ['z'] = "ゼット"
    };

    public static string Convert(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var result = new StringBuilder(text.Length * 2);
        foreach (var character in text.Trim())
        {
            if (IsHan(character))
            {
                var pinyin = TryGetPinyin(character);
                if (pinyin is not null && Mapping.Value.TryGetValue(pinyin, out var kana))
                {
                    result.Append(kana);
                    continue;
                }

                // AquesTalk cannot pronounce an unknown ideograph. Keep a
                // short pause so the remaining sentence is still usable.
                result.Append(' ');
                continue;
            }

            if (Digits.TryGetValue(character, out var digit))
            {
                result.Append(digit);
                continue;
            }

            if (Letters.TryGetValue(char.ToLowerInvariant(character), out var letter))
            {
                result.Append(letter);
                continue;
            }

            result.Append(NormalizePunctuation(character));
        }

        return result.ToString().Trim();
    }

    public static bool ContainsHan(string text) => text.Any(IsHan);

    private static string? TryGetPinyin(char character)
    {
        try
        {
            var values = PinyinHelper.ToHanyuPinyinStringArray(character);
            if (values is null || values.Length == 0) return null;
            var value = values[0].Trim().ToLowerInvariant();
            value = value.Replace('ü', 'v');
            value = StripTone(value);
            return value;
        }
        catch
        {
            return null;
        }
    }

    private static string StripTone(string value)
    {
        if (value.Length == 0) return value;
        var last = value[^1];
        if (last is >= '1' and <= '5') return value[..^1];

        return value
            .Replace("ā", "a").Replace("á", "a").Replace("ǎ", "a").Replace("à", "a")
            .Replace("ē", "e").Replace("é", "e").Replace("ě", "e").Replace("è", "e")
            .Replace("ī", "i").Replace("í", "i").Replace("ǐ", "i").Replace("ì", "i")
            .Replace("ō", "o").Replace("ó", "o").Replace("ǒ", "o").Replace("ò", "o")
            .Replace("ū", "u").Replace("ú", "u").Replace("ǔ", "u").Replace("ù", "u")
            .Replace("ǖ", "v").Replace("ǘ", "v").Replace("ǚ", "v").Replace("ǜ", "v");
    }

    private static char NormalizePunctuation(char character) => character switch
    {
        '，' => '、',
        '；' => '、',
        '：' => '：',
        '！' => '！',
        '？' => '？',
        '（' => '（',
        '）' => '）',
        '【' => '「',
        '】' => '」',
        '“' => '「',
        '”' => '」',
        '‘' => '「',
        '’' => '」',
        '…' => '、',
        '—' => 'ー',
        _ => character
    };

    private static bool IsHan(char character) =>
        character is >= '\u3400' and <= '\u9fff' ||
        character is >= '\uf900' and <= '\ufaff';

    private static IReadOnlyDictionary<string, string> LoadMapping()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "pinyin-kana.tsv");
        if (File.Exists(path))
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                var parts = line.Split('\t', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                    map[parts[0]] = parts[1];
            }

            if (map.Count > 0) return map;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["man"] = "マン", ["manman"] = "マンマン", ["lai"] = "ライ",
            ["ni"] = "ニー", ["hao"] = "ハオ", ["shi"] = "シー",
            ["de"] = "ドー", ["yi"] = "イー", ["tian"] = "ティエン"
        };
    }
}
