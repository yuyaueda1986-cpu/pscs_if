// ===========================================================================
//  Layer4_RfProtocol.cs  --  層4 リーダ制御: RF プロトコルの種別
//
//  IPcscTransport.SwitchProtocol の引数。
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

namespace PcscIf
{
    public enum RfProtocol { TypeA = 0, TypeB = 1, Iso15693 = 2, Felica = 3 }
}
