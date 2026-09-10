// ===========================================================================
//  Layer1_PcscStatus.cs  --  層1 公開 API: 4 つの API が返すステータス
//
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

namespace PcscIf
{
    /// <summary>4 つの API が返すステータス。Ok 以外はすべて失敗として扱うこと。</summary>
    public enum PcscStatus
    {
        /// <summary>成功。応答の検証まで完了している。</summary>
        Ok = 0,
        /// <summary>引数が不正。I/O は行っていない。</summary>
        InvalidArgument = 1,
        /// <summary>Connect していない状態で Read / Write を呼んだ。</summary>
        NotConnected = 2,
        /// <summary>すでに Connect 済みの状態で Connect を呼んだ。</summary>
        AlreadyConnected = 3,
        /// <summary>リーダが見つからない、または winscard の呼び出しが失敗した。</summary>
        ReaderError = 4,
        /// <summary>対象カードの応答を観測しなかった。物理的な不存在の証明ではない。</summary>
        NoCard = 5,
        /// <summary>異なる IDm を 2 種類以上観測した（重ね置き）。読み書きは行っていない。</summary>
        MultipleCards = 6,
        /// <summary>通信異常・RF 衝突・応答途切れ・読み戻し不一致。結果を保証しない。</summary>
        Indeterminate = 7,
        /// <summary>カードがステータスフラグでエラーを返した（領域外・書込禁止など）。</summary>
        CardRejected = 8
    }
}
