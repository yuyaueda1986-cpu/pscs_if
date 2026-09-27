// ===========================================================================
//  L5_PcscNative.cs  --  層5 PC/SC: winscard.dll との接続
//
//    Interop        winscard.dll の P/Invoke と制御コード
//    PcscException  winscard の戻り値を運ぶ例外
//
//  層構成は L1_Pcsc.cs 先頭のコメント、パラメータの詳細は docs/protocol.md §1 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PcscIf
{
    // =======================================================================
    //  Interop  --  winscard.dll の P/Invoke と制御コード
    // =======================================================================

    internal static class Interop
    {
        public const int SCARD_S_SUCCESS = 0x00000000;

        public const uint SCARD_SCOPE_USER = 0;

        /// <summary>カードが無くてもリーダに直接つなぐ。Escape を送るのに使う。</summary>
        public const uint SCARD_SHARE_DIRECT = 3;

        public const uint SCARD_PROTOCOL_UNDEFINED = 0x00000000;

        public const uint SCARD_LEAVE_CARD = 0;

        /// <summary>SCARD_CTL_CODE(code) = (0x31 &lt;&lt; 16) | (code &lt;&lt; 2)</summary>
        public static uint ScardCtlCode(uint code) { return (0x31u << 16) | (code << 2); }

        /// <summary>リーダの機能一覧を取得する IOCTL（SCARD_CTL_CODE(3400) = 0x00313520）。</summary>
        public static readonly uint CM_IOCTL_GET_FEATURE_REQUEST = ScardCtlCode(3400);

        /// <summary>フォールバック用の Escape 制御コード（SCARD_CTL_CODE(3500) = 0x003136B0）。</summary>
        public static readonly uint IOCTL_CCID_ESCAPE_FALLBACK = ScardCtlCode(3500);

        /// <summary>GET_FEATURE_REQUEST 応答 TLV における Escape コマンドのタグ。</summary>
        public const byte FEATURE_CCID_ESC_COMMAND = 0x13;

        [DllImport("winscard.dll", SetLastError = true)]
        public static extern int SCardEstablishContext(
            uint dwScope, IntPtr pvReserved1, IntPtr pvReserved2, out IntPtr phContext);

        [DllImport("winscard.dll", SetLastError = true)]
        public static extern int SCardReleaseContext(IntPtr hContext);

        [DllImport("winscard.dll", EntryPoint = "SCardConnectW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int SCardConnect(
            IntPtr hContext, string szReader, uint dwShareMode, uint dwPreferredProtocols,
            out IntPtr phCard, out uint pdwActiveProtocol);

        [DllImport("winscard.dll", SetLastError = true)]
        public static extern int SCardDisconnect(IntPtr hCard, uint dwDisposition);

        [DllImport("winscard.dll", SetLastError = true)]
        public static extern int SCardControl(
            IntPtr hCard, uint dwControlCode,
            [In] byte[] lpInBuffer, uint nInBufferSize,
            [Out] byte[] lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned);

        [DllImport("winscard.dll", EntryPoint = "SCardListReadersW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int SCardListReaders(
            IntPtr hContext, string mszGroups, [Out] char[] mszReaders, ref uint pcchReaders);

        /// <summary>接続中のリーダ名を列挙する（\0 区切りのマルチ文字列を分解）。</summary>
        public static List<string> ListReaders(IntPtr hContext)
        {
            List<string> result = new List<string>();
            uint len = 0;
            int rc = SCardListReaders(hContext, null, null, ref len);
            if (rc != SCARD_S_SUCCESS || len == 0) return result;

            char[] buf = new char[len];
            rc = SCardListReaders(hContext, null, buf, ref len);
            if (rc != SCARD_S_SUCCESS) return result;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < len && i < buf.Length; i++)
            {
                if (buf[i] == '\0')
                {
                    if (sb.Length > 0) { result.Add(sb.ToString()); sb.Length = 0; }
                }
                else sb.Append(buf[i]);
            }
            return result;
        }

        /// <summary>
        /// CM_IOCTL_GET_FEATURE_REQUEST から Escape 制御コードを取得する。
        /// 取得できなければ null（呼び出し側でフォールバック値を使う）。
        /// 応答は「tag(1) len(1) value(len, ビッグエンディアン)」の TLV 列。
        /// </summary>
        public static uint? TryGetEscapeControlCode(IntPtr hCard)
        {
            byte[] outBuf = new byte[256];
            uint returned;
            int rc = SCardControl(hCard, CM_IOCTL_GET_FEATURE_REQUEST,
                new byte[0], 0, outBuf, (uint)outBuf.Length, out returned);
            if (rc != SCARD_S_SUCCESS || returned < 3) return null;

            int i = 0;
            while (i + 2 <= returned)
            {
                byte tag = outBuf[i++];
                byte len = outBuf[i++];
                if (i + len > returned) break;
                if (tag == FEATURE_CCID_ESC_COMMAND && len == 4)
                    return (uint)((outBuf[i] << 24) | (outBuf[i + 1] << 16)
                                | (outBuf[i + 2] << 8) | outBuf[i + 3]);
                i += len;
            }
            return null;
        }
    }

    // =======================================================================
    //  PcscException  --  winscard の戻り値を運ぶ例外
    //
    //  winscard の関数が SCARD_S_SUCCESS 以外を返したときに、その戻り値を ReturnCode として保持する。
    // =======================================================================

    public sealed class PcscException : Exception
    {
        private readonly int _returnCode;
        public int ReturnCode { get { return _returnCode; } }
        public PcscException(string message, int rc)
            : base(message + " (0x" + rc.ToString("X8") + ")")
        {
            _returnCode = rc;
        }
    }
}
