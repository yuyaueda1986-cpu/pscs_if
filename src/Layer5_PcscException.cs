// ===========================================================================
//  Layer5_PcscException.cs  --  層5 PC/SC: winscard の戻り値を運ぶ例外
//
//  winscard の関数が SCARD_S_SUCCESS 以外を返したときに、その戻り値を ReturnCode として保持する。
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;

namespace PcscIf
{
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
