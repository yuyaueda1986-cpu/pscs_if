// ===========================================================================
//  Common_Hex.cs  --  共通: 16 進文字列との相互変換
//
//  特定の層に属さない補助。ログ出力・IDm の文字列化・テストの入力解析などで
//  全層から使う。層構成は L1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;
using System.Text;

namespace PcscIf
{
    /// <summary>16 進文字列との相互変換。</summary>
    public static class Hex
    {
        public static string ToString(byte[] data)
        {
            return ToString(data, data == null ? 0 : data.Length, true);
        }

        public static string ToString(byte[] data, int length, bool separated)
        {
            if (data == null) return "(null)";
            if (length < 0 || length > data.Length) length = data.Length;
            StringBuilder sb = new StringBuilder(length * 3);
            for (int i = 0; i < length; i++)
            {
                if (separated && i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>空白・ハイフン・コロン・0x 接頭辞を許容する。</summary>
        public static byte[] Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            string s = text.Replace(" ", "").Replace("-", "").Replace(":", "");
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length % 2 != 0)
                throw new ArgumentException("16 進文字列は偶数桁でなければなりません。", "text");
            byte[] bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value;
                if (!byte.TryParse(s.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new ArgumentException(
                        "16 進として解釈できない箇所があります: " + s.Substring(i * 2, 2), "text");
                bytes[i] = value;
            }
            return bytes;
        }

        internal static bool SequenceEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
