using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace CodecWpf
{
    /// <summary>
    /// 도구 창(진법 / IP / 타임스탬프 / 파일↔Base64) 계산 로직. UI와 분리되어 있다.
    /// </summary>
    public static class Tools
    {
        // ── 진법 변환 ───────────────────────────────────
        // 임의 길이 정수(BigInteger)라서 64비트를 넘는 큰 수도 처리한다.
        // 입력에 섞인 공백 · _ · , 와 0x / 0b / 0o 접두사는 무시한다.
        private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

        public static BigInteger ParseRadix(string input, int radix)
        {
            string s = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != ',').ToArray())
                .ToLowerInvariant();
            bool negative = s.StartsWith("-");
            if (negative) s = s.Substring(1);

            if (radix == 16 && (s.StartsWith("0x") || s.StartsWith("&h"))) s = s.Substring(2);
            else if (radix == 2 && s.StartsWith("0b")) s = s.Substring(2);
            else if (radix == 8 && s.StartsWith("0o")) s = s.Substring(2);

            if (s.Length == 0) throw new FormatException("숫자를 입력하세요");

            BigInteger value = BigInteger.Zero;
            foreach (char c in s)
            {
                int d = Digits.IndexOf(c);
                if (d < 0 || d >= radix)
                    throw new FormatException($"{radix}진수에 쓸 수 없는 글자: '{c}'");
                value = value * radix + d;
            }
            return negative ? -value : value;
        }

        public static string ToRadix(BigInteger value, int radix)
        {
            if (value.IsZero) return "0";
            bool negative = value.Sign < 0;
            var v = BigInteger.Abs(value);
            var sb = new StringBuilder();
            while (!v.IsZero)
            {
                sb.Insert(0, Digits[(int)(v % radix)]);
                v /= radix;
            }
            return (negative ? "-" : "") + sb.ToString().ToUpperInvariant();
        }

        // ── IPv4 변환 ───────────────────────────────────
        // 입력 형식 자동 판별: 192.168.0.1 / 3232235521 / 0xC0A80001 / C0A80001
        public static string IpReport(string input)
        {
            string s = input.Trim();
            uint ip;
            string detected;

            if (s.Contains('.'))
            {
                var parts = s.Split('.');
                if (parts.Length != 4)
                    throw new FormatException("IPv4는 점으로 나뉜 4칸이어야 합니다 (예: 192.168.0.1)");
                ip = 0;
                foreach (var p in parts)
                {
                    if (!int.TryParse(p.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int octet)
                        || octet > 255)
                        throw new FormatException($"각 칸은 0~255 사이 숫자여야 합니다: '{p}'");
                    ip = (ip << 8) | (uint)octet;
                }
                detected = "IPv4 주소";
            }
            else
            {
                string h = s.ToLowerInvariant();
                bool isHex = h.StartsWith("0x") || h.Any(c => c >= 'a' && c <= 'f');
                if (h.StartsWith("0x")) h = h.Substring(2);

                BigInteger v;
                try { v = ParseRadix(h, isHex ? 16 : 10); }
                catch (FormatException) { throw new FormatException("IPv4 주소, 10진수, 16진수 중 하나를 입력하세요"); }
                if (v.Sign < 0 || v > uint.MaxValue)
                    throw new FormatException("IPv4 범위(0 ~ 4294967295)를 벗어났습니다");
                ip = (uint)v;
                detected = isHex ? "16진수" : "10진수";
            }

            byte[] o = { (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip };
            var sb = new StringBuilder();
            sb.AppendLine($"입력 형식   {detected}");
            sb.AppendLine();
            sb.AppendLine($"IPv4        {o[0]}.{o[1]}.{o[2]}.{o[3]}");
            sb.AppendLine($"10진수      {ip}");
            sb.AppendLine($"16진수      0x{ip:X8}");
            sb.AppendLine($"2진수       {string.Join(".", o.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')))}");
            sb.AppendLine($"종류        {IpKind(o)}");
            return sb.ToString().TrimEnd();
        }

        private static string IpKind(byte[] o)
        {
            if (o[0] == 10) return "사설망 (10.0.0.0/8)";
            if (o[0] == 172 && o[1] >= 16 && o[1] <= 31) return "사설망 (172.16.0.0/12)";
            if (o[0] == 192 && o[1] == 168) return "사설망 (192.168.0.0/16)";
            if (o[0] == 127) return "루프백 (127.0.0.0/8)";
            if (o[0] == 169 && o[1] == 254) return "링크 로컬 (169.254.0.0/16)";
            if (o[0] == 100 && o[1] >= 64 && o[1] <= 127) return "CGNAT (100.64.0.0/10)";
            if (o[0] == 0) return "\"이 네트워크\" (0.0.0.0/8)";
            if (o[0] >= 224 && o[0] <= 239) return "멀티캐스트 (224.0.0.0/4)";
            if (o.All(b => b == 255)) return "브로드캐스트";
            if (o[0] >= 240) return "예약 (240.0.0.0/4)";
            return "공인 IP";
        }

        // ── Unix 타임스탬프 ─────────────────────────────
        // 숫자면 타임스탬프(자릿수로 초/ms/µs/ns 자동 판별), 아니면 날짜로 해석해 타임스탬프로 변환.
        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/MM/dd",
            "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd",
            "yyyyMMddHHmmss", "yyyyMMdd"
        };

        public static string TimestampReport(string input, TimeZoneInfo local)
        {
            string s = input.Trim();
            DateTimeOffset utc;
            string detected;

            if (Regex.IsMatch(s, @"^-?\d+(\.\d+)?$") && !(s.Length == 8 && s.StartsWith("20") && !s.Contains('.')))
            {
                decimal n = decimal.Parse(s, CultureInfo.InvariantCulture);
                decimal abs = Math.Abs(n);
                decimal seconds;
                if (abs < 1e11m)      { seconds = n;              detected = "Unix 타임스탬프 (초)"; }
                else if (abs < 1e14m) { seconds = n / 1000m;      detected = "Unix 타임스탬프 (밀리초)"; }
                else if (abs < 1e17m) { seconds = n / 1000000m;   detected = "Unix 타임스탬프 (마이크로초)"; }
                else                  { seconds = n / 1000000000m; detected = "Unix 타임스탬프 (나노초)"; }

                try
                {
                    long whole = (long)Math.Floor(seconds);
                    long ticks = (long)((seconds - whole) * TimeSpan.TicksPerSecond);
                    utc = DateTimeOffset.FromUnixTimeSeconds(whole).AddTicks(ticks);
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new FormatException("표현할 수 있는 날짜 범위를 벗어났습니다");
                }
            }
            else
            {
                // 시간대가 적혀 있으면(Z, +09:00 등) 그걸 쓰고, 없으면 로컬 시간으로 본다.
                if (DateTimeOffset.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsedExact))
                {
                    var dt = parsedExact.DateTime;
                    utc = new DateTimeOffset(dt, local.GetUtcOffset(dt)).ToUniversalTime();
                    detected = $"날짜 (로컬 시간 {FormatOffset(local.GetUtcOffset(dt))}로 해석)";
                }
                else if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                             DateTimeStyles.AssumeUniversal, out var parsed)
                         && Regex.IsMatch(s, @"(Z|[+-]\d{2}:?\d{2})$"))
                {
                    utc = parsed.ToUniversalTime();
                    detected = "날짜 (시간대 포함)";
                }
                else
                {
                    throw new FormatException(
                        "타임스탬프 숫자나 날짜를 입력하세요 (예: 1700000000, 2026-10-16 03:28:00)");
                }
            }

            var loc = TimeZoneInfo.ConvertTime(utc, local);
            var sb = new StringBuilder();
            sb.AppendLine($"입력 형식   {detected}");
            sb.AppendLine();
            sb.AppendLine($"Unix (초)   {utc.ToUnixTimeSeconds()}");
            sb.AppendLine($"Unix (ms)   {utc.ToUnixTimeMilliseconds()}");
            sb.AppendLine($"UTC         {utc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"로컬        {loc:yyyy-MM-dd HH:mm:ss} ({FormatOffset(loc.Offset)})");
            sb.AppendLine($"ISO 8601    {utc:yyyy-MM-ddTHH:mm:ss}Z");
            sb.AppendLine($"요일        {loc.ToString("dddd", new CultureInfo("ko-KR"))}");
            return sb.ToString().TrimEnd();
        }

        private static string FormatOffset(TimeSpan o) =>
            $"UTC{(o < TimeSpan.Zero ? "-" : "+")}{o.Duration():hh\\:mm}";

        // ── 파일 ↔ Base64 ───────────────────────────────
        // data URI(data:image/png;base64,...)면 접두사를 떼고, 내용으로 확장자를 추측한다.
        private static readonly Regex DataUri = new Regex(@"^data:([\w.+-]+/[\w.+-]+)?(;[^,]*)?;base64,",
                                                          RegexOptions.IgnoreCase);

        public static byte[] ParseBase64Payload(string text, out string suggestedExt)
        {
            string s = text.Trim();
            string mime = null;
            var m = DataUri.Match(s);
            if (m.Success)
            {
                mime = m.Groups[1].Value.ToLowerInvariant();
                s = s.Substring(m.Length);
            }

            s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
            // URL-safe base64(- _)와 패딩 누락도 받아준다
            s = s.Replace('-', '+').Replace('_', '/');
            if (s.Length % 4 != 0) s = s.PadRight(s.Length + (4 - s.Length % 4), '=');

            byte[] data;
            try { data = Convert.FromBase64String(s); }
            catch (FormatException) { throw new FormatException("올바른 Base64가 아닙니다"); }

            suggestedExt = ExtFromMime(mime) ?? ExtFromMagic(data) ?? ".bin";
            return data;
        }

        private static string ExtFromMime(string mime) => mime switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "application/pdf" => ".pdf",
            "application/zip" => ".zip",
            "text/plain" => ".txt",
            "application/json" => ".json",
            _ => null
        };

        // 파일 앞부분의 시그니처(매직 넘버)로 형식 추측
        private static string ExtFromMagic(byte[] d)
        {
            bool Starts(params byte[] sig) => d.Length >= sig.Length && sig.Select((b, i) => d[i] == b).All(x => x);
            if (Starts(0x89, 0x50, 0x4E, 0x47)) return ".png";
            if (Starts(0xFF, 0xD8, 0xFF)) return ".jpg";
            if (Starts(0x47, 0x49, 0x46, 0x38)) return ".gif";
            if (Starts(0x25, 0x50, 0x44, 0x46)) return ".pdf";
            if (Starts(0x50, 0x4B, 0x03, 0x04)) return ".zip";
            if (Starts(0x4D, 0x5A)) return ".exe";
            if (Starts(0x52, 0x49, 0x46, 0x46) && d.Length > 11 && d[8] == 0x57 && d[9] == 0x45) return ".webp";
            if (Starts(0x1F, 0x8B)) return ".gz";
            if (Starts(0x7B) || Starts(0x5B)) return ".json";
            return null;
        }

        // ── 찾기 / 바꾸기 ───────────────────────────────
        public static List<int> FindAll(string text, string query, bool caseSensitive)
        {
            var hits = new List<int>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return hits;
            var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int i = 0;
            while ((i = text.IndexOf(query, i, cmp)) >= 0)
            {
                hits.Add(i);
                i += query.Length;   // 겹치는 일치는 세지 않음 (바꾸기와 개수가 같도록)
            }
            return hits;
        }

        public static string ReplaceAll(string text, string query, string replacement,
                                        bool caseSensitive, out int count)
        {
            var hits = FindAll(text, query, caseSensitive);
            count = hits.Count;
            if (count == 0) return text;
            var sb = new StringBuilder();
            int last = 0;
            foreach (int h in hits)
            {
                sb.Append(text, last, h - last).Append(replacement);
                last = h + query.Length;
            }
            sb.Append(text, last, text.Length - last);
            return sb.ToString();
        }

        // ── 줄 · 글자 수 ────────────────────────────────
        // 줄 번호는 실제 줄바꿈 기준 (화면에서 자동 줄바꿈된 건 한 줄로 침)
        public static (int line, int col) LineCol(string text, int index)
        {
            index = Math.Max(0, Math.Min(index, text.Length));
            int line = 1, lineStart = 0;
            for (int i = 0; i < index; i++)
                if (text[i] == '\n') { line++; lineStart = i + 1; }
            return (line, index - lineStart + 1);
        }

        public static int LineCount(string text) =>
            string.IsNullOrEmpty(text) ? 0 : text.Count(c => c == '\n') + 1;

        // 글자 수는 사람이 보는 글자 단위 (이모지·조합 문자도 1자, \r\n은 1자)
        public static int CharCount(string text) =>
            string.IsNullOrEmpty(text) ? 0
                : new StringInfo(text.Replace("\r\n", "\n")).LengthInTextElements;

        public static string HumanSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? $"{bytes} B" : $"{v:0.##} {units[u]}";
        }
    }
}
