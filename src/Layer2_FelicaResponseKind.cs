// ===========================================================================
//  Layer2_FelicaResponseKind.cs  --  層2 カードコマンド: 読み書き応答の解析結果の分類
//
//  FelicaLiteS.ParseReadResponse / ParseWriteResponse の戻り値。
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

namespace PcscIf
{
    /// <summary>
    /// 読み書き応答の解析結果。
    /// 「カードが明示的にエラーを返した」と「応答自体が信頼できない」を区別する。
    /// 前者はカードの状態が確定しているが、後者は確定していない。
    /// 呼び出し側の復旧判断が変わるため、同じ失敗として潰してはならない。
    /// </summary>
    public enum FelicaResponseKind
    {
        /// <summary>応答は妥当で、カードもエラーを返していない。</summary>
        Ok = 0,
        /// <summary>応答は妥当。カードがステータスフラグでエラーを返した。</summary>
        CardError = 1,
        /// <summary>応答が途中切れ・別 IDm・応答コード違い・長さ不整合など。信頼できない。</summary>
        Malformed = 2
    }
}
