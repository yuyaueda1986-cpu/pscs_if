// ===========================================================================
//  L4_PcscTransport.cs  --  層4 リーダ制御: トランスポートの契約と実機実装
//
//    IPcscTransport   RF トランスポートの抽象（テストで差し替える境界）
//    RfProtocol       RF プロトコルの種別
//    Rcs300Transport  ベンダ拡張 APDU（FF 50）による実機トランスポート
//
//  インタフェースと実装は同じファイルに置くが、別の型として分離したままにしている。
//  層構成は L1_Pcsc.cs 先頭のコメント、バイト列は protocol.md §2 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
    // =======================================================================
    //  IPcscTransport  --  RF トランスポートの抽象
    //
    //  実機用の実装は Rcs300Transport（このファイルの末尾）。
    //  テストではこのインタフェースを差し替えて、リーダ無しで上位層を検証する。
    // =======================================================================

    /// <summary>
    /// RF トランスポート。実機は <see cref="Rcs300Transport"/>。
    /// テストではこのインタフェースを差し替えてリーダ無しで検証する。
    /// </summary>
    public interface IPcscTransport : IDisposable
    {
        void Open(string readerName);
        void StartTransparentSession();
        void TurnOffRf();
        void SwitchProtocol(RfProtocol protocol);
        void TurnOnRf();
        TransceiveResult Transceive(byte[] frame, int timeoutUs);
        void EndTransparentSession();
    }

    // =======================================================================
    //  RfProtocol  --  RF プロトコルの種別
    //
    //  IPcscTransport.SwitchProtocol の引数。
    // =======================================================================

    public enum RfProtocol { TypeA = 0, TypeB = 1, Iso15693 = 2, Felica = 3 }

    // =======================================================================
    //  Rcs300Transport  --  ベンダ拡張 APDU（FF 50）による実機トランスポート
    //
    //  層2 のカードフレームを FF 50 の封筒に包み、層5 の SCardControl（Escape）で送る。
    //  応答は層3 の PcscResponse / TransceiveResult で解釈する。
    // =======================================================================

    /// <summary>
    /// 対象リーダの Transparent Session プロファイル。対象カード専用。
    /// CRC はリーダ側が付与するため、ソフトウェア CRC は扱わない。
    /// 不明な応答はすべて失敗として扱う（fail-closed）。
    /// </summary>
    public sealed class Rcs300Transport : IPcscTransport
    {
        private IntPtr _context;
        private IntPtr _card;
        private uint _escapeCode;
        private bool _sessionStarted;
        private bool _rfOn;

        public void Open(string readerName)
        {
            if (string.IsNullOrEmpty(readerName) || readerName.Trim().Length == 0)
                throw new ArgumentException("リーダ名が必要です。", "readerName");
            if (_context != IntPtr.Zero)
                throw new InvalidOperationException("すでに開いています。");
            try
            {
                Check(Interop.SCardEstablishContext(Interop.SCARD_SCOPE_USER,
                    IntPtr.Zero, IntPtr.Zero, out _context), "SCardEstablishContext");
                uint activeProtocol;
                Check(Interop.SCardConnect(_context, readerName, Interop.SCARD_SHARE_DIRECT,
                    Interop.SCARD_PROTOCOL_UNDEFINED, out _card, out activeProtocol), "SCardConnect");
                uint? code = Interop.TryGetEscapeControlCode(_card);
                _escapeCode = code.HasValue ? code.Value : Interop.IOCTL_CCID_ESCAPE_FALLBACK;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static byte[] Manage(byte tag)
        {
            return new byte[] { 0xFF, 0x50, 0, 0, 2, tag, 0, 0 };
        }

        public void StartTransparentSession()
        {
            // 応答が不確かでも復旧を試みられるよう、送出前にフラグを立てる。
            _sessionStarted = true;
            SendApdu(Manage(0x81), "StartTransparentSession");
        }

        public void EndTransparentSession()
        {
            SendApdu(Manage(0x82), "EndTransparentSession");
            _sessionStarted = false;
        }

        public void TurnOffRf()
        {
            SendApdu(Manage(0x83), "TurnOffRF");
            _rfOn = false;
        }

        public void TurnOnRf()
        {
            _rfOn = true;
            SendApdu(Manage(0x84), "TurnOnRF");
        }

        /// <summary>Table 45: 03 = 対象プロトコル、00 = フレーミング指定（ビットレートではない）。</summary>
        public static byte[] BuildFelicaSwitchApdu()
        {
            return new byte[] { 0xFF, 0x50, 0, 2, 4, 0x8F, 2, 3, 0, 0 };
        }

        public void SwitchProtocol(RfProtocol protocol)
        {
            if (protocol != RfProtocol.Felica)
                throw new NotSupportedException("対象カード以外はサポートしません。");
            SendApdu(BuildFelicaSwitchApdu(), "SwitchProtocol(framing)");
        }

        public TransceiveResult Transceive(byte[] frame, int timeoutUs)
        {
            int rc;
            byte[] response = SendApduRaw(BuildTransceiveApdu(frame, timeoutUs), out rc);
            if (rc != Interop.SCARD_S_SUCCESS)
            {
                TransceiveResult failed = new TransceiveResult();
                failed.Unknown = true;
                failed.RawResponse = response;
                return failed;
            }
            return TransceiveResult.Parse(response);
        }

        public static byte[] BuildTransceiveApdu(byte[] frame, int timeoutUs)
        {
            if (frame == null || frame.Length == 0 || frame.Length > 65524)
                throw new ArgumentException("送信フレーム長が不正です。", "frame");
            if (timeoutUs <= 0)
                throw new ArgumentOutOfRangeException("timeoutUs");
            int doLen = 11 + frame.Length;
            List<byte> a = new List<byte>();
            a.AddRange(new byte[] {
                0xFF, 0x50, 0, 1, 0, (byte)(doLen >> 8), (byte)doLen,
                0x5F, 0x46, 4, (byte)timeoutUs, (byte)(timeoutUs >> 8),
                (byte)(timeoutUs >> 16), (byte)(timeoutUs >> 24),
                0x95, 0x82, (byte)(frame.Length >> 8), (byte)frame.Length });
            a.AddRange(frame);
            // 対象リーダの実機で観測した FF50 のエンベロープ。ベンダ独自のフレーミングであり、
            // 「拡張 Le がデータの後ろに 3 バイト」という一般規則ではない。
            a.AddRange(new byte[] { 0, 0, 0 });
            return a.ToArray();
        }

        /// <summary>管理コマンドの応答を検証する。成功と言い切れなければ例外。</summary>
        public static void ValidateManagementResponse(byte[] response, string what)
        {
            if (!PcscResponse.Parse(response).CommandSucceeded)
                throw new InvalidOperationException(
                    what + ": 拒否または不正な応答 [" + Hex.ToString(response) + "]");
        }

        private void SendApdu(byte[] apdu, string what)
        {
            int rc;
            byte[] response = SendApduRaw(apdu, out rc);
            Check(rc, what);
            ValidateManagementResponse(response, what);
        }

        private byte[] SendApduRaw(byte[] apdu, out int rc)
        {
            if (_card == IntPtr.Zero)
                throw new InvalidOperationException("リーダが開かれていません。");
            byte[] output = new byte[4096];
            uint returned;
            rc = Interop.SCardControl(_card, _escapeCode, apdu, (uint)apdu.Length,
                output, (uint)output.Length, out returned);
            if (rc != Interop.SCARD_S_SUCCESS) return new byte[0];
            if (returned > output.Length)
                throw new InvalidOperationException("応答がバッファを超えました。");
            byte[] response = new byte[returned];
            Buffer.BlockCopy(output, 0, response, 0, (int)returned);
            return response;
        }

        private static void Check(int rc, string what)
        {
            if (rc != Interop.SCARD_S_SUCCESS) throw new PcscException(what, rc);
        }

        public void Dispose()
        {
            // 復旧目的のみ。正常系では Pcsc 側が明示的に RF / セッションを閉じている。
            try { if (_rfOn) TurnOffRf(); }
            catch { }
            try { if (_sessionStarted) EndTransparentSession(); }
            catch { }
            // 既知の制限: SCardDisconnect / SCardReleaseContext は失敗を例外ではなく戻り値で
            // 通知するが、ここでは戻り値を確認していない。そのため解放に失敗しても
            // Pcsc.Disconnect は Ok を返す。呼び出し側へ通知する仕組みは未実装。
            if (_card != IntPtr.Zero)
            {
                Interop.SCardDisconnect(_card, Interop.SCARD_LEAVE_CARD);
                _card = IntPtr.Zero;
            }
            if (_context != IntPtr.Zero)
            {
                Interop.SCardReleaseContext(_context);
                _context = IntPtr.Zero;
            }
        }
    }
}
