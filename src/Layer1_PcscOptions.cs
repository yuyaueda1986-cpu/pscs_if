// ===========================================================================
//  Layer1_PcscOptions.cs  --  層1 公開 API: Read / Write の動作パラメータ
//
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;

namespace PcscIf
{
    /// <summary>Read / Write の動作パラメータ。コンストラクタでスナップショットされる。</summary>
    public sealed class PcscOptions
    {
        /// <summary>Transceive のタイムアウト（マイクロ秒）。100000..1000000。</summary>
        public int TransceiveTimeoutUs { get; set; }

        /// <summary>
        /// 重ね置きチェックの Polling ラウンド数。2..64。
        /// 対象リーダは 1 回の Transceive で 1 枚分の応答しか返さないため、
        /// 複数ラウンドの IDm 和集合が唯一の検出手段である。減らすと検出が成立しない。
        /// </summary>
        public int DuplicateCheckRounds { get; set; }

        /// <summary>RF 起動待ち（ミリ秒）。0..1000。実機で要確認の保守的な値。</summary>
        public int RfSettleDelayMs { get; set; }

        /// <summary>Write 後に読み戻して一致を確認する。既定で有効。</summary>
        public bool VerifyAfterWrite { get; set; }

        /// <summary>診断ログ。null 可。例外を投げても判定は迂回されない。</summary>
        public Action<string> Log { get; set; }

        public PcscOptions()
        {
            TransceiveTimeoutUs = 300000;   // 書き込みは読み出しより時間がかかる
            DuplicateCheckRounds = 8;
            RfSettleDelayMs = 20;
            VerifyAfterWrite = true;
            Log = null;
        }

        internal PcscOptions Snapshot()
        {
            PcscOptions o = new PcscOptions();
            o.TransceiveTimeoutUs = TransceiveTimeoutUs;
            o.DuplicateCheckRounds = DuplicateCheckRounds;
            o.RfSettleDelayMs = RfSettleDelayMs;
            o.VerifyAfterWrite = VerifyAfterWrite;
            o.Log = Log;
            return o;
        }

        internal void Validate()
        {
            if (TransceiveTimeoutUs < 100000 || TransceiveTimeoutUs > 1000000)
                throw new ArgumentOutOfRangeException("TransceiveTimeoutUs",
                    "TransceiveTimeoutUs は 100000..1000000 の範囲で指定してください。");
            if (DuplicateCheckRounds < 2 || DuplicateCheckRounds > 64)
                throw new ArgumentOutOfRangeException("DuplicateCheckRounds",
                    "DuplicateCheckRounds は 2..64 の範囲で指定してください。1 では重ね置きを検出できません。");
            if (RfSettleDelayMs < 0 || RfSettleDelayMs > 1000)
                throw new ArgumentOutOfRangeException("RfSettleDelayMs",
                    "RfSettleDelayMs は 0..1000 の範囲で指定してください。");
        }
    }
}
