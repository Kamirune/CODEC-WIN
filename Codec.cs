using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodecWpf
{
    /// <summary>
    /// 인코딩 / 디코딩 로직 모음. UI와 분리되어 있어 테스트나 재사용이 쉽다.
    /// </summary>
    public static class Codec
    {
        // 지원하는 방식 목록 (드롭다운에 그대로 사용)
        public static readonly string[] Methods =
        {
            "base64", "base32", "hex", "url",
            "binary", "ascii", "rot13", "caesar", "morse"
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
                default:
                    throw new ArgumentException($"알 수 없는 방식: {method}");
            }
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
