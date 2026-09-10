// ===========================================================================
//  Layer1_PcscResult.cs  --  層1 公開 API: 4 つの API 共通の戻り値
//
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;
using System.Text;

namespace PcscIf
{
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
}
