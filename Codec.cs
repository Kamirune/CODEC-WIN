using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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

        // 해시 방식 (인코딩 전용, 되돌릴 수 없음)
        public static readonly string[] HashMethods = { "md5", "sha1", "sha256", "sha512" };

        public static bool IsHash(string method) => HashMethods.Contains(method);

        // 지원하는 방식 목록 (드롭다운에 그대로 사용)
        public static readonly string[] Methods =
        {
            "base64", "base32", "hex", "url",
            "binary", "ascii", "rot13", "caesar", "morse",
            "html",
            AutoMethod,
            "md5", "sha1", "sha256", "sha512"
        };

        // ── 문자셋 ──────────────────────────────────────
        // 바이트 기반 방식(base64/hex/url/binary/ascii/해시 등)에서 텍스트↔바이트 변환에 쓸 문자셋.
        // EUC-KR은 실제로는 윈도우 확장판인 CP949로 처리한다 (똠, 햏 같은 글자까지 지원).
        public const string Utf8 = "UTF-8";
        public const string EucKr = "EUC-KR";
        public static readonly string[] Charsets = { Utf8, EucKr };

        static Codec()
        {
            // .NET Core/5+ 는 CP949를 기본 등록하지 않으므로 코드페이지 공급자를 등록
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        // 문자셋 영향을 받지 않는 방식 (텍스트 그대로 다룸)
        public static bool UsesCharset(string method) =>
            method != "rot13" && method != "caesar" && method != "morse" && method != "html";

        // strict = 깨진 바이트를 만나면 예외 (auto 판별용). 인코딩 방향은 항상 strict.
        private static Encoding GetCharset(string charset, bool strict = false)
        {
            if (charset == EucKr)
                return Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback,
                    strict ? DecoderFallback.ExceptionFallback : DecoderFallback.ReplacementFallback);
            return strict ? StrictUtf8 : Encoding.UTF8;
        }

        private static byte[] ToBytes(string text, string charset)
        {
            try
            {
                return GetCharset(charset).GetBytes(text);
            }
            catch (EncoderFallbackException ex)
            {
                string ch = ex.CharUnknownHigh != '\0'
                    ? new string(new[] { ex.CharUnknownHigh, ex.CharUnknownLow })   // 이모지 등 서로게이트 쌍
                    : ex.CharUnknown.ToString();
                throw new ArgumentException(
                    $"{charset}로 표현할 수 없는 문자가 있습니다: '{ch}' (이모지 등은 UTF-8을 쓰세요)");
            }
        }

        // 상태 표시용 바이트 수. 해당 문자셋으로 표현할 수 없는 글자가 있으면 -1.
        public static int ByteCount(string text, string charset)
        {
            try { return GetCharset(charset).GetByteCount(text); }
            catch (EncoderFallbackException) { return -1; }
        }

        // URL 퍼센트 인코딩 (UTF-8이 아닌 문자셋용). Uri.EscapeDataString과 같은 규칙.
        private static string PercentEncode(byte[] data)
        {
            var sb = new StringBuilder();
            foreach (byte b in data)
            {
                char c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.' || c == '~')
                    sb.Append(c);
                else
                    sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        // %XX는 바이트로, 나머지 글자는 해당 문자셋 바이트로 바꿔 한 줄의 바이트열로 만든다.
        private static byte[] PercentDecodeBytes(string s, Encoding enc)
        {
            var bytes = new List<byte>();
            var run = new StringBuilder();
            void Flush()
            {
                if (run.Length == 0) return;
                bytes.AddRange(enc.GetBytes(run.ToString()));
                run.Clear();
            }
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%' && i + 2 < s.Length &&
                    Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
                {
                    Flush();
                    bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else run.Append(s[i]);
            }
            Flush();
            return bytes.ToArray();
        }

        // 디코딩된 바이트 → 텍스트.
        // UTF-8로 읽었는데 깨지고, EUC-KR로는 멀쩡한 한글이 되면 문자셋을 바꾸라고 알려준다.
        // (둘 다 아니면 이미지 같은 이진 데이터일 수 있으니 예전처럼 깨진 채로 보여줌)
        private static string BytesToText(byte[] bytes, string charset)
        {
            if (charset != Utf8) return GetCharset(charset).GetString(bytes);
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                if (LooksLikeEucKr(bytes))
                    throw new FormatException(
                        "UTF-8이 아니라 EUC-KR 한글로 보여요. 오른쪽 아래 문자셋을 EUC-KR로 바꿔보세요");
                return Encoding.UTF8.GetString(bytes);
            }
        }

        private static bool LooksLikeEucKr(byte[] bytes)
        {
            try
            {
                string t = GetCharset(EucKr, strict: true).GetString(bytes);
                return IsReadableText(t) && IsPlausibleKorean(t) && t.Any(c => c >= 0xAC00 && c <= 0xD7A3);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        // hex 인코딩 출력 형식 (드롭다운 표시용 예시 = 형식 이름)
        public static readonly string[] HexFormats =
        {
            "4865",          // 0: 붙여서
            "48 65",         // 1: 공백
            "%48%65",        // 2: URL 퍼센트
            @"\x48\x65",     // 3: C/파이썬 이스케이프
            "0x48 0x65"      // 4: 0x 접두사
        };

        private static string FormatHex(byte[] data, int format)
        {
            var parts = data.Select(b => b.ToString("x2"));
            switch (format)
            {
                case 1:  return string.Join(" ", parts);
                case 2:  return string.Concat(parts.Select(p => "%" + p));
                case 3:  return string.Concat(parts.Select(p => "\\x" + p));
                case 4:  return string.Join(" ", parts.Select(p => "0x" + p));
                default: return string.Concat(parts);
            }
        }

        // 디코딩 시엔 어떤 형식이든 받아준다: 0x / \x 접두사, % , : - ; 구분자, 공백류
        private static string NormalizeHex(string s)
        {
            s = s.Replace("0x", "").Replace("0X", "").Replace("\\x", "").Replace("\\X", "");
            return new string(s.Where(c => !char.IsWhiteSpace(c) && "%,:-;".IndexOf(c) < 0).ToArray());
        }

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
        public static string Encode(string method, string text, int shift = 3, int hexFormat = 0,
                                    string charset = Utf8)
        {
            byte[] data = UsesCharset(method) && method != AutoMethod && !IsHash(method)
                ? ToBytes(text, charset) : null;
            switch (method)
            {
                case "base64":
                    return Convert.ToBase64String(data);
                case "base32":
                    return Base32Encode(data);
                case "hex":
                    return FormatHex(data, hexFormat);
                case "html":
                    return WebUtility.HtmlEncode(text);
                case "url":
                    return charset == Utf8 ? Uri.EscapeDataString(text) : PercentEncode(data);
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
                case "md5":
                case "sha1":
                case "sha256":
                case "sha512":
                    return HashText(method, text, charset);
                default:
                    throw new ArgumentException($"알 수 없는 방식: {method}");
            }
        }

        // ── 디코딩 ──────────────────────────────────────
        // 디코딩은 복사·붙여넣기 과정에서 섞이는 공백·탭·줄바꿈을 관대하게 처리한다.
        //  - base64/base32/hex: 공백류를 전부 제거하고 디코딩.
        //    실패하면 줄마다 따로 인코딩된 경우로 보고 한 줄씩 디코딩 (결과 줄바꿈 유지).
        //  - binary/ascii/morse: 공백·탭·줄바꿈 어떤 조합이든 구분자로 취급.
        public static string Decode(string method, string text, int shift = 3, string charset = Utf8)
        {
            switch (method)
            {
                case "base64":
                case "base32":
                case "hex":
                    return DecodeCompact(method, text, charset);
                case "url":
                    return BytesToText(PercentDecodeBytes(text.Trim(), GetCharset(charset)), charset);
                case "html":
                    return WebUtility.HtmlDecode(text);
                case "binary":
                    return BytesToText(ParseBinary(text), charset);
                case "ascii":
                    return BytesToText(
                        SplitTokens(text).Select(n => byte.Parse(n)).ToArray(), charset);
                case "rot13":
                    return CaesarShift(text, -13);
                case "caesar":
                    return CaesarShift(text, -shift);
                case "morse":
                    return DecodeMorse(text);
                case AutoMethod:
                    return AutoDecode(text, out _, charset);
                case "md5":
                case "sha1":
                case "sha256":
                case "sha512":
                    throw new ArgumentException("해시는 단방향이라 디코딩할 수 없습니다. ENCODE 모드로 바꾸세요");
                default:
                    throw new ArgumentException($"알 수 없는 방식: {method}");
            }
        }

        // ── 해시 ────────────────────────────────────────
        // 텍스트는 선택한 문자셋의 바이트로 해시 (BlueBell은 MD5에 ASCII를 써서 한글 해시가 틀렸음)
        private static HashAlgorithmName HashName(string method) => method switch
        {
            "md5"    => HashAlgorithmName.MD5,
            "sha1"   => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException($"알 수 없는 해시: {method}")
        };

        public static string HashText(string method, string text, string charset = Utf8)
        {
            using var h = IncrementalHash.CreateHash(HashName(method));
            h.AppendData(ToBytes(text, charset));
            return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
        }

        // 파일을 한 번만 읽으면서 네 가지 해시를 동시에 계산 (큰 파일도 메모리 적게 사용)
        public static string HashFileReport(string path)
        {
            var hashes = HashMethods.Select(m => IncrementalHash.CreateHash(HashName(m))).ToArray();
            try
            {
                long size;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                               1 << 20, FileOptions.SequentialScan))
                {
                    size = fs.Length;
                    var buf = new byte[1 << 20];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                        foreach (var h in hashes) h.AppendData(buf, 0, n);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"FILE    {Path.GetFileName(path)}  ({size:N0} bytes)");
                for (int i = 0; i < HashMethods.Length; i++)
                    sb.AppendLine($"{HashMethods[i].ToUpperInvariant(),-7} " +
                                  Convert.ToHexString(hashes[i].GetHashAndReset()).ToLowerInvariant());
                return sb.ToString().TrimEnd();
            }
            finally
            {
                foreach (var h in hashes) h.Dispose();
            }
        }

        // ── 공백 처리 도우미 ────────────────────────────
        private static string StripWhitespace(string s) =>
            new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

        private static string[] SplitTokens(string s) =>
            s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);   // null = 모든 공백류

        private static byte[] DecodeCompactBytes(string method, string compact)
        {
            switch (method)
            {
                case "base64": return Convert.FromBase64String(compact);
                case "base32": return Base32Decode(compact);
                default:       return Convert.FromHexString(NormalizeHex(compact));   // hex
            }
        }

        private static string DecodeCompact(string method, string text, string charset)
        {
            try
            {
                return BytesToText(DecodeCompactBytes(method, StripWhitespace(text)), charset);
            }
            catch (FormatException ex) when (ex.Message.Contains("EUC-KR"))
            {
                throw;   // 문자셋 안내는 그대로 보여준다
            }
            catch
            {
                // 여러 줄에 각각 따로 인코딩된 값이 있는 경우 (예: base64 두 줄, 각자 == 패딩)
                var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                if (lines.Count(l => l.Trim().Length > 0) < 2) throw;
                return string.Join(Environment.NewLine, lines.Select(l =>
                    l.Trim().Length == 0 ? "" :
                    BytesToText(DecodeCompactBytes(method, StripWhitespace(l)), charset)));
            }
        }

        // 공백으로 나뉜 8비트 묶음, 또는 공백 없이 붙은 비트열 둘 다 허용
        private static byte[] ParseBinary(string text)
        {
            var tokens = SplitTokens(text);
            if (tokens.Length == 1 && tokens[0].Length > 8)
            {
                string bits = tokens[0];
                if (bits.Length % 8 != 0)
                    throw new FormatException("비트 수가 8의 배수가 아닙니다");
                tokens = Enumerable.Range(0, bits.Length / 8)
                    .Select(i => bits.Substring(i * 8, 8)).ToArray();
            }
            return tokens.Select(b => Convert.ToByte(b, 2)).ToArray();
        }

        // 글자는 공백류로, 단어는 '/'로 구분. 줄바꿈도 단어 구분으로 본다.
        private static string DecodeMorse(string text)
        {
            var words = text.Replace("\r\n", "/").Replace('\n', '/').Replace('\r', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => SplitTokens(w))
                .Where(w => w.Length > 0)
                .Select(w => string.Concat(w.Select(code =>
                    MorseRev.TryGetValue(code, out var ch) ? ch : '?')));
            return string.Join(" ", words);
        }

        // ── 자동 다중 디코딩 ────────────────────────────
        // 결과가 또 인코딩된 형태로 보이면 계속 벗겨낸다.
        // 판별은 "형식이 맞는가" + "디코딩 결과가 정상 텍스트인가" 두 단계로 해서
        // 평범한 단어(예: "test")를 base64로 오인하는 걸 막는다.
        // rot13/caesar는 결과만 봐서는 판별이 불가능해서 제외.
        private static readonly string[] AutoOrder =
        {
            "url", "html", "binary", "ascii", "hex", "base32", "base64", "morse"
        };

        public const int MaxAutoLayers = 10;

        public static string AutoDecode(string text, out List<string> steps, string charset = Utf8)
        {
            steps = new List<string>();
            string current = text.Trim();

            for (int layer = 0; layer < MaxAutoLayers; layer++)
            {
                string next = null, used = null;
                foreach (var m in AutoOrder)
                {
                    if (TryDecodeLayer(m, current, charset, out var result, out var label))
                    {
                        next = result;
                        used = label;
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
        private static readonly Regex HtmlPattern = new Regex("&(?:#[0-9]+|#[xX][0-9A-Fa-f]+|[A-Za-z][A-Za-z0-9]*);");
        private static readonly Regex BinaryPattern = new Regex(@"^[01]{8}(?:\s+[01]{8})+$|^(?:[01]{8}){2,}$");
        private static readonly Regex AsciiPattern = new Regex(@"^\d{1,3}(?:\s+\d{1,3})+$");
        private static readonly Regex HexPattern = new Regex("^(?:[0-9A-Fa-f]{2})+$");
        private static readonly Regex Base32Pattern = new Regex("^[A-Z2-7]+=*$");
        private static readonly Regex Base64Pattern = new Regex("^[A-Za-z0-9+/]+={0,2}$");
        private static readonly Regex MorsePattern = new Regex(@"^[.\-/\s]+$");

        // 잘못된 UTF-8이면 예외를 던지는 디코더 (깨진 결과를 통과시키지 않기 위해)
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static bool TryDecodeLayer(string method, string text, string charset,
                                           out string result, out string label)
        {
            result = null;
            label = method;
            var strict = GetCharset(charset, strict: true);
            // CP949는 짧은 바이트열도 그럴듯한 한글로 읽혀버려서 ("test" → "듬-") 최소 길이를 둔다
            int minCompact = charset == Utf8 ? 4 : 8;
            try
            {
                switch (method)
                {
                    case "url":
                        if (!UrlPattern.IsMatch(text)) return false;
                        var urlBytes = PercentDecodeBytes(text, GetCharset(charset));
                        try
                        {
                            result = strict.GetString(urlBytes);
                        }
                        catch (DecoderFallbackException) when (charset == Utf8)
                        {
                            // %XX는 강한 신호라서, UTF-8로 안 풀리면 EUC-KR도 시도해본다
                            if (!LooksLikeEucKr(urlBytes)) return false;
                            result = GetCharset(EucKr, strict: true).GetString(urlBytes);
                            label = "url(euc-kr)";
                            return true;
                        }
                        break;
                    case "binary":
                        if (!BinaryPattern.IsMatch(text)) return false;
                        result = strict.GetString(ParseBinary(text));
                        break;
                    case "ascii":
                        if (!AsciiPattern.IsMatch(text)) return false;
                        var nums = SplitTokens(text).Select(int.Parse).ToArray();
                        if (nums.Any(n => n > 255)) return false;
                        result = strict.GetString(nums.Select(n => (byte)n).ToArray());
                        break;
                    case "html":
                        if (!HtmlPattern.IsMatch(text)) return false;
                        result = WebUtility.HtmlDecode(text);
                        break;
                    case "hex":
                        string hex = NormalizeHex(text);
                        if (hex.Length < minCompact || !HexPattern.IsMatch(hex)) return false;
                        result = strict.GetString(Convert.FromHexString(hex));
                        break;
                    case "base32":
                        string b32 = StripWhitespace(text);
                        if (b32.Length < 8 || b32.Length % 8 != 0 || !Base32Pattern.IsMatch(b32))
                            return false;
                        result = strict.GetString(Base32Decode(b32));
                        break;
                    case "base64":
                        string b64 = StripWhitespace(text);
                        if (b64.Length < minCompact || b64.Length % 4 != 0 || !Base64Pattern.IsMatch(b64))
                            return false;
                        result = strict.GetString(Convert.FromBase64String(b64));
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

            return IsReadableText(result) && result != text &&
                   (charset == Utf8 || IsPlausibleKorean(result));
        }

        // CP949는 아무 바이트 쌍이나 한자/한글로 읽혀버리는 경우가 많아서,
        // auto 판별 시엔 영문·한글·일반 문장부호로만 이루어진 결과만 인정한다. (한자 제외)
        private static bool IsPlausibleKorean(string s) => s.All(c =>
            c < 0x80 ||
            (c >= 0xAC00 && c <= 0xD7A3) ||   // 한글 음절
            (c >= 0x3130 && c <= 0x318F) ||   // 한글 자모
            (c >= 0x2000 && c <= 0x206F) ||   // 일반 문장부호
            (c >= 0x3000 && c <= 0x303F) ||   // CJK 문장부호
            (c >= 0xFF00 && c <= 0xFFEF));    // 전각 문자

        // 제어문자(줄바꿈·탭 제외)가 섞였으면 "정상 텍스트가 아님"으로 본다.
        private static bool IsReadableText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                if (c == '\r' || c == '\n' || c == '\t') continue;
                if (char.IsControl(c) || c == '\uFFFD') return false;
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
