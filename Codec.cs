using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodecWpf
{
    /// <summary>
    /// 인코딩 / 디코딩 로직 모음. UI와 분리되어 있어 테스트나 재사용이 쉽다.
    /// </summary>
    public static class Codec
    {
        // 자동 다중 디코딩 방식 이름 (디코딩 전용)
        public const string AutoMethod = "auto";

        // 지원하는 방식 목록 (드롭다운에 그대로 사용)
        public static readonly string[] Methods =
        {
            "base64", "base32", "hex", "url",
            "binary", "ascii", "rot13", "caesar", "morse",
            AutoMethod
        };

        // ── 모스 부호 테이블 ─────────────────────────────
        private static readonly Dictionary<char, string> Morse = new Dictionary<char, string>
        {
            {'A',".-"},{'B',"-..."},{'C',"-.-."},{'D',"-.."},{'E',"."},{'F',"..-."},
            {'G',"--."},{'H',"...."},{'I',".."},{'J',".---"},{'K',"-.-"},{'L',".-.."},
            {'M',"--"},{'N',"-."},{'O',"---"},{'P',".--."},{'Q',"--.-"},{'R',".-."},
            {'S',"..."},{'T',"-"},{'U',"..-"},{'V',"...-"},{'W',".--"},{'X',"-..-"},
            {'Y',"-.--"},{'Z',"--.."},{'0',"-----"},{'1',".----"},{'2',"..---"},
            {'3',"...--"},{'4',"....-"},{'5',"....."},{'6',"-...."},{'7',"--..."},
            {'8',"---.."},{'9',"----."},{'.',".-.-.-"},{',',"--..--"},{'?',"..--.."},
            {'\'',".----."},{'!',"-.-.--"},{'/',"-..-."},{'(',"-.--."},{')',"-.--.-"},
            {'&',".-..."},{':',"---..."},{';',"-.-.-."},{'=',"-...-"},{'+',".-.-."},
            {'-',"-....-"},{'_',"..--.-"},{'"',".-..-."},{'$',"...-..-"},{'@',".--.-."},
            {' ',"/"}
        };

        private static readonly Dictionary<string, char> MorseRev =
            Morse.ToDictionary(kv => kv.Value, kv => kv.Key);

        // ── 시저 / ROT 계열 공통 처리 ────────────────────
        private static string CaesarShift(string text, int shift)
        {
            var sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (ch >= 'A' && ch <= 'Z')
                    sb.Append((char)((ch - 'A' + shift % 26 + 26) % 26 + 'A'));
                else if (ch >= 'a' && ch <= 'z')
                    sb.Append((char)((ch - 'a' + shift % 26 + 26) % 26 + 'a'));
                else
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        // ── 인코딩 ──────────────────────────────────────
        public static string Encode(string method, string text, int shift = 3)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            switch (method)
            {
                case "base64":
                    return Convert.ToBase64String(data);
                case "base32":
                    return Base32Encode(data);
                case "hex":
                    return Convert.ToHexString(data).ToLowerInvariant();
                case "url":
                    return Uri.EscapeDataString(text);
                case "binary":
                    return string.Join(" ", data.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
                case "ascii":
                    return string.Join(" ", data.Select(b => b.ToString()));
                case "rot13":
                    return CaesarShift(text, 13);
                case "caesar":
                    return CaesarShift(text, shift);
                case "morse":
                    return string.Join(" ", text.Select(ch =>
                        Morse.TryGetValue(char.ToUpper(ch), out var code) ? code : "?"));
                case AutoMethod:
                    throw new ArgumentException("auto는 디코딩 전용입니다. DECODE 모드로 바꾸세요");
                default:
                    throw new ArgumentException($"알 수 없는 방식: {method}");
            }
        }

        // ── 디코딩 ──────────────────────────────────────
        public static string Decode(string method, string text, int shift = 3)
        {
            switch (method)
            {
                case "base64":
                    return Encoding.UTF8.GetString(Convert.FromBase64String(text));
                case "base32":
                    return Encoding.UTF8.GetString(Base32Decode(text));
                case "hex":
                    return Encoding.UTF8.GetString(Convert.FromHexString(text.Replace(" ", "")));
                case "url":
                    return Uri.UnescapeDataString(text);
                case "binary":
                    return Encoding.UTF8.GetString(
                        text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(b => Convert.ToByte(b, 2)).ToArray());
                case "ascii":
                    return Encoding.UTF8.GetString(
                        text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(n => byte.Parse(n)).ToArray());
                case "rot13":
                    return CaesarShift(text, -13);
                case "caesar":
                    return CaesarShift(text, -shift);
                case "morse":
                    return string.Concat(
                        text.Split(' ').Select(code =>
                            MorseRev.TryGetValue(code, out var ch) ? ch : '?'));
                case AutoMethod:
                    return AutoDecode(text, out _);
                default:
                    throw new ArgumentException($"알 수 없는 방식: {method}");
            }
        }

        // ── 자동 다중 디코딩 ────────────────────────────
        // 결과가 또 인코딩된 형태로 보이면 계속 벗겨낸다.
        // 판별은 "형식이 맞는가" + "디코딩 결과가 정상 텍스트인가" 두 단계로 해서
        // 평범한 단어(예: "test")를 base64로 오인하는 걸 막는다.
        // rot13/caesar는 결과만 봐서는 판별이 불가능해서 제외.
        private static readonly string[] AutoOrder =
        {
            "url", "binary", "ascii", "hex", "base32", "base64", "morse"
        };

        public const int MaxAutoLayers = 10;

        public static string AutoDecode(string text, out List<string> steps)
        {
            steps = new List<string>();
            string current = text.Trim();

            for (int layer = 0; layer < MaxAutoLayers; layer++)
            {
                string next = null, used = null;
                foreach (var m in AutoOrder)
                {
                    if (TryDecodeLayer(m, current, out var result))
                    {
                        next = result;
                        used = m;
                        break;
                    }
                }
                if (next == null) break;   // 더 벗길 게 없음
                steps.Add(used);
                current = next.Trim();
            }

            if (steps.Count == 0)
                throw new FormatException("인식할 수 있는 인코딩이 없습니다");
            return current;
        }

        private static readonly Regex UrlPattern = new Regex("%[0-9A-Fa-f]{2}");
        private static readonly Regex BinaryPattern = new Regex(@"^[01]{8}( [01]{8})*$");
        private static readonly Regex AsciiPattern = new Regex(@"^\d{1,3}( \d{1,3})+$");
        private static readonly Regex HexPattern = new Regex("^(?:[0-9A-Fa-f]{2})+$");
        private static readonly Regex Base32Pattern = new Regex("^[A-Z2-7]+=*$");
        private static readonly Regex Base64Pattern = new Regex("^[A-Za-z0-9+/]+={0,2}$");
        private static readonly Regex MorsePattern = new Regex(@"^[.\-/ ]+$");

        // 잘못된 UTF-8이면 예외를 던지는 디코더 (깨진 결과를 통과시키지 않기 위해)
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static bool TryDecodeLayer(string method, string text, out string result)
        {
            result = null;
            try
            {
                switch (method)
                {
                    case "url":
                        if (!UrlPattern.IsMatch(text)) return false;
                        result = Uri.UnescapeDataString(text);
                        break;
                    case "binary":
                        if (!BinaryPattern.IsMatch(text)) return false;
                        result = StrictUtf8.GetString(text.Split(' ')
                            .Select(b => Convert.ToByte(b, 2)).ToArray());
                        break;
                    case "ascii":
                        if (!AsciiPattern.IsMatch(text)) return false;
                        var nums = text.Split(' ').Select(int.Parse).ToArray();
                        if (nums.Any(n => n > 255)) return false;
                        result = StrictUtf8.GetString(nums.Select(n => (byte)n).ToArray());
                        break;
                    case "hex":
                        string hex = text.Replace(" ", "");
                        if (hex.Length < 4 || !HexPattern.IsMatch(hex)) return false;
                        result = StrictUtf8.GetString(Convert.FromHexString(hex));
                        break;
                    case "base32":
                        if (text.Length < 8 || text.Length % 8 != 0 || !Base32Pattern.IsMatch(text))
                            return false;
                        result = StrictUtf8.GetString(Base32Decode(text));
                        break;
                    case "base64":
                        string b64 = Regex.Replace(text, @"\s", "");
                        if (b64.Length < 4 || b64.Length % 4 != 0 || !Base64Pattern.IsMatch(b64))
                            return false;
                        result = StrictUtf8.GetString(Convert.FromBase64String(b64));
                        break;
                    case "morse":
                        if (!MorsePattern.IsMatch(text) || !text.Any(c => c == '.' || c == '-'))
                            return false;
                        result = Decode("morse", text);
                        if (result.Contains('?')) return false;
                        break;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;   // 형식은 맞아 보였지만 실제로는 디코딩 불가
            }

            return IsReadableText(result) && result != text;
        }

        // 제어문자(줄바꿈·탭 제외)가 섞였으면 "정상 텍스트가 아님"으로 본다.
        private static bool IsReadableText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (c == '\r' || c == '\n' || c == '\t') continue;
                if (char.IsControl(c) || c == '�') return false;
            }
            return true;
        }

        // ── Base32 (.NET에 기본 API가 없어 직접 구현) ────
        private const string B32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private static string Base32Encode(byte[] data)
        {
            if (data.Length == 0) return "";
            var sb = new StringBuilder();
            int buffer = data[0], bitsLeft = 8, next = 1;
            while (bitsLeft > 0 || next < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (next < data.Length)
                    {
                        buffer = (buffer << 8) | (data[next++] & 0xFF);
                        bitsLeft += 8;
                    }
                    else
                    {
                        int pad = 5 - bitsLeft;
                        buffer <<= pad;
                        bitsLeft += pad;
                    }
                }
                int index = 0x1F & (buffer >> (bitsLeft - 5));
                bitsLeft -= 5;
                sb.Append(B32Alphabet[index]);
            }
            while (sb.Length % 8 != 0) sb.Append('=');
            return sb.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            input = input.TrimEnd('=').ToUpperInvariant();
            var bytes = new List<byte>();
            int buffer = 0, bitsLeft = 0;
            foreach (char c in input)
            {
                int val = B32Alphabet.IndexOf(c);
                if (val < 0) continue;
                buffer = (buffer << 5) | val;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bytes.Add((byte)(buffer >> (bitsLeft - 8)));
                    bitsLeft -= 8;
                }
            }
            return bytes.ToArray();
        }
    }
}
