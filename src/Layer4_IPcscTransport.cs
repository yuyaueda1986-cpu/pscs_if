// ===========================================================================
//  Layer4_IPcscTransport.cs  --  層4 リーダ制御: RF トランスポートの抽象
//
//  実機用の実装は Rcs300Transport（Layer4_Rcs300Transport.cs）。
//  テストではこのインタフェースを差し替えて、リーダ無しで上位層を検証する。
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;

namespace PcscIf
{
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
}
