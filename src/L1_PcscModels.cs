// ===========================================================================
//  L1_PcscModels.cs  --  層1 公開 API: 設定・戻り値・ステータス
//
//  業務アプリが扱う設定・戻り値・ステータスをまとめる。型はそれぞれ独立している。
//    PcscOptions  Read / Write の動作パラメータ
//    PcscResult   4 つの API 共通の戻り値
//    PcscStatus   4 つの API が返すステータス
//
//  層構成は L1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;
using System.Text;

namespace PcscIf
{
    // =======================================================================
    //  PcscOptions  --  Read / Write の動作パラメータ
    // =======================================================================

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

    // =======================================================================
    //  PcscResult  --  4 つの API 共通の戻り値
    // =======================================================================

    /// <summary>4 つの API 共通の戻り値。</summary>
    public sealed class PcscResult
    {
        private readonly PcscStatus _status;
        private readonly byte[] _data;
        private readonly string _idm;
        private readonly byte _sf1;
        private readonly byte _sf2;
        private readonly string _message;
        private readonly long _elapsedMs;
        private readonly Exception _error;

        internal PcscResult(PcscStatus status, string message, long elapsedMs,
            byte[] data, string idm, byte sf1, byte sf2, Exception error)
        {
            _status = status;
            _message = message == null ? "" : message;
            _elapsedMs = elapsedMs;
            _data = data == null ? new byte[0] : data;
            _idm = idm == null ? "" : idm;
            _sf1 = sf1;
            _sf2 = sf2;
            _error = error;
        }

        public PcscStatus Status { get { return _status; } }

        /// <summary>Ok のときだけ true。未知の値も false になる（fail-closed）。</summary>
        public bool Ok { get { return _status == PcscStatus.Ok; } }

        /// <summary>Read 成功時の読み出しデータ。それ以外は長さ 0。</summary>
        public byte[] Data { get { return _data; } }

        /// <summary>アクセス対象として確定した IDm（16 進大文字）。確定できなければ空。</summary>
        public string Idm { get { return _idm; } }

        /// <summary>カードのステータスフラグ 1。カードまで到達していない場合は 0xFF。</summary>
        public byte StatusFlag1 { get { return _sf1; } }

        /// <summary>カードのステータスフラグ 2。カードまで到達していない場合は 0xFF。</summary>
        public byte StatusFlag2 { get { return _sf2; } }

        public string Message { get { return _message; } }
        public long ElapsedMs { get { return _elapsedMs; } }

        /// <summary>例外に起因する失敗のときだけ非 null。</summary>
        public Exception Error { get { return _error; } }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(_status.ToString());
            if (_idm.Length > 0) { sb.Append(" idm="); sb.Append(_idm); }
            if (!(_sf1 == 0xFF && _sf2 == 0xFF))
                sb.Append(" SF=").Append(_sf1.ToString("X2")).Append(_sf2.ToString("X2"));
            if (_data.Length > 0) { sb.Append(" len="); sb.Append(_data.Length); }
            sb.Append(" (").Append(_message).Append(") ").Append(_elapsedMs).Append("ms");
            return sb.ToString();
        }

        // ---- 内部生成ヘルパ --------------------------------------------------
        internal static PcscResult Create(PcscStatus status, string message, long elapsedMs)
        {
            return new PcscResult(status, message, elapsedMs, null, null, 0xFF, 0xFF, null);
        }
        internal static PcscResult Create(PcscStatus status, string message, long elapsedMs, string idm)
        {
            return new PcscResult(status, message, elapsedMs, null, idm, 0xFF, 0xFF, null);
        }
        internal static PcscResult Failure(PcscStatus status, string message, long elapsedMs, Exception error)
        {
            return new PcscResult(status, message, elapsedMs, null, null, 0xFF, 0xFF, error);
        }
    }

    // =======================================================================
    //  PcscStatus  --  4 つの API が返すステータス
    // =======================================================================

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
