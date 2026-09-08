// ===========================================================================
//  PcscIf.cs  --  PC/SC インタフェース API（1 ファイル集約）
//
//  対象カード : 特定の IC カード（システムコード・IC 種別で判別。値は本ファイル内の定数を参照）
//  対象リーダ : CCID Escape / Transparent Session に対応した PC/SC リーダ
//
//  ビルド前提 : .NET Framework 4.5 / C# 5.0 / Visual Studio 2013
//               このファイルでは以下の C# 6 以降の構文を意図的に使用していない。
//                 文字列補間 / nameof / 式形式メンバ / 自動プロパティ初期化子 /
//                 get のみの自動プロパティ / null 条件演算子 / out var /
//                 using 宣言 / 数値区切り / タプル / init / Array.Empty
//               （Array.Empty は .NET Framework 4.6 以降のため 4.5 では使えない）
//
//  公開 API は 4 つ。
//      pcsc.Connect()     リーダに接続する
//      pcsc.Disconnect()  リーダから切断する
//      pcsc.Read()        ユーザデータ領域を読み出す
//      pcsc.Write()       ユーザデータ領域を書き込む
//
//  Read / Write は毎回、実アクセスと同一の RF セッション内で重ね置きチェック
//  （pcsc_dup_guard の CardDuplicateGuard 相当）を行う。
//  異なる IDm を 2 種類以上観測した場合は PcscStatus.MultipleCards を返し、
//  読み書きコマンドを一切送出しない。
//
//  設計上の限界（運用手順で補うこと）
//    - RF 応答を返さない 2 枚目は検出できない。ラウンドを増やしても見逃し 0% にはならない。
//    - NoCard は「明示的な無応答を観測した」であって物理的な不存在の証明ではない。
//    - 検査と読み書きは 1 セッション内で連続実行するが、セッション終了後に
//      カードが追加・移動される可能性は残る。
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PcscIf
{
    // =======================================================================
    //  1. 公開ステータスと結果
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

    /// <summary>16 進文字列との相互変換。</summary>
    public static class Hex
    {
        public static string ToString(byte[] data)
        {
            return ToString(data, data == null ? 0 : data.Length, true);
        }

        public static string ToString(byte[] data, int length, bool separated)
        {
            if (data == null) return "(null)";
            if (length < 0 || length > data.Length) length = data.Length;
            StringBuilder sb = new StringBuilder(length * 3);
            for (int i = 0; i < length; i++)
            {
                if (separated && i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>空白・ハイフン・コロン・0x 接頭辞を許容する。</summary>
        public static byte[] Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            string s = text.Replace(" ", "").Replace("-", "").Replace(":", "");
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length % 2 != 0)
                throw new ArgumentException("16 進文字列は偶数桁でなければなりません。", "text");
            byte[] bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value;
                if (!byte.TryParse(s.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new ArgumentException(
                        "16 進として解釈できない箇所があります: " + s.Substring(i * 2, 2), "text");
                bytes[i] = value;
            }
            return bytes;
        }

        internal static bool SequenceEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    // =======================================================================
    //  2. PC/SC Part 3 supplement v2.02 の応答 TLV 解析
    //     C0 = 汎用ステータス、92 = 受信ビット数、96 = RF エラー、97 = 受信データ。
    //     未知タグ・途中で切れた TLV は決して無視せず、すべて失敗にする（fail-closed）。
    // =======================================================================

    public sealed class PcscResponse
    {
        internal bool Valid;
        internal ushort Sw;
        internal ushort? GenericSw;
        internal byte RfErrors;
        internal bool PartialByte;
        internal readonly List<byte[]> Frames = new List<byte[]>();

        /// <summary>管理コマンド（FF50 系）が成功したと言い切れる場合だけ true。</summary>
        public bool CommandSucceeded
        {
            get
            {
                return Valid && Sw == 0x9000
                    && (!GenericSw.HasValue || GenericSw.Value == 0x9000)
                    && RfErrors == 0 && !PartialByte && Frames.Count == 0;
            }
        }

        public static PcscResponse Parse(byte[] response)
        {
            PcscResponse r = new PcscResponse();
            if (response == null || response.Length < 2) return r;
            int end = response.Length - 2;
            r.Sw = (ushort)((response[end] << 8) | response[end + 1]);
            int i = 0;
            while (i < end)
            {
                if (end - i < 2) return r;
                byte tag = response[i++];
                int len = response[i++];
                if (len == 0x81)
                {
                    if (i >= end) return r;
                    len = response[i++];
                }
                else if (len == 0x82)
                {
                    if (end - i < 2) return r;
                    len = (response[i] << 8) | response[i + 1];
                    i += 2;
                }
                else if (len >= 0x80) return r;   // 不定長 BER は受け付けない
                if (len > end - i) return r;

                switch (tag)
                {
                    case 0xC0:
                        if (len != 3 || r.GenericSw.HasValue) return r;
                        r.GenericSw = (ushort)((response[i + 1] << 8) | response[i + 2]);
                        if (r.GenericSw.Value == 0x9000 && response[i] != 0) return r;
                        break;
                    case 0x92:
                        if (len != 1 || response[i] > 7) return r;
                        if (response[i] != 0) r.PartialByte = true;
                        break;
                    case 0x96:
                        // Table 28 は 2 バイト。仕様の例には 1 バイトのゼロ（エラー無し）もある。
                        if (len != 2 && !(len == 1 && response[i] == 0)) return r;
                        if ((response[i] & 0xF0) != 0) return r;
                        r.RfErrors |= response[i];
                        break;
                    case 0x97:
                        if (len == 0) return r;
                        byte[] frame = new byte[len];
                        Buffer.BlockCopy(response, i, frame, 0, len);
                        r.Frames.Add(frame);      // 最後の 1 個ではなく全部保持する
                        break;
                    default:
                        return r;                 // 未知タグは失敗
                }
                i += len;
            }
            r.Valid = true;
            return r;
        }
    }

    /// <summary>Transceive 1 回分の観測結果。フラグはちょうど 1 つだけ立つのが正常。</summary>
    public sealed class TransceiveResult
    {
        public bool Ok { get; set; }
        /// <summary>明示的な「ICC からの応答なし」。物理的な不存在の証明ではない。</summary>
        public bool NoResponse { get; set; }
        public bool Collision { get; set; }
        public bool Unknown { get; set; }
        public byte[] Data { get; set; }
        public IList<byte[]> Frames { get; set; }
        /// <summary>DO 96 の RF エラーフラグ（DO 92 ではない）。</summary>
        public byte RfErrors { get; set; }
        public ushort StatusWord { get; set; }
        public ushort? GenericStatusWord { get; set; }
        public byte[] RawResponse { get; set; }

        public TransceiveResult()
        {
            Data = new byte[0];
            Frames = new List<byte[]>();
            RawResponse = new byte[0];
        }

        /// <summary>立っているフラグ数。1 以外は矛盾しているので信用してはならない。</summary>
        public int FlagCount
        {
            get
            {
                return (Ok ? 1 : 0) + (NoResponse ? 1 : 0) + (Collision ? 1 : 0) + (Unknown ? 1 : 0);
            }
        }

        public static TransceiveResult Parse(byte[] response)
        {
            PcscResponse r = PcscResponse.Parse(response);
            bool transportSuccess = r.Valid && r.Sw == 0x9000;
            bool commandSuccess = transportSuccess && (!r.GenericSw.HasValue || r.GenericSw.Value == 0x9000);
            bool collision = commandSuccess && (r.RfErrors & 2) != 0;
            bool ok = commandSuccess && r.RfErrors == 0 && !r.PartialByte && r.Frames.Count > 0;
            // 6400 / 6700 / 6Axx は失敗。完全な C0 6401 だけが「応答なし」。
            bool noResponse = transportSuccess && r.GenericSw.HasValue && r.GenericSw.Value == 0x6401
                && r.RfErrors == 0 && !r.PartialByte && r.Frames.Count == 0;

            TransceiveResult t = new TransceiveResult();
            t.Ok = ok;
            t.NoResponse = noResponse;
            t.Collision = collision;
            t.Unknown = !ok && !noResponse && !collision;
            t.RfErrors = r.RfErrors;
            t.StatusWord = r.Sw;
            t.GenericStatusWord = r.GenericSw;
            t.RawResponse = response == null ? new byte[0] : response;
            if (ok)
            {
                t.Frames = r.Frames;
                int total = 0;
                for (int i = 0; i < r.Frames.Count; i++) total += r.Frames[i].Length;
                byte[] flat = new byte[total];
                int offset = 0;
                for (int i = 0; i < r.Frames.Count; i++)
                {
                    Buffer.BlockCopy(r.Frames[i], 0, flat, offset, r.Frames[i].Length);
                    offset += r.Frames[i].Length;
                }
                t.Data = flat;
            }
            return t;
        }

        public override string ToString()
        {
            return "Ok=" + Ok + " NoResp=" + NoResponse + " Collision=" + Collision
                + " Unknown=" + Unknown + " RF=0x" + RfErrors.ToString("X2")
                + " SW=0x" + StatusWord.ToString("X4")
                + " C0=" + (GenericStatusWord.HasValue ? GenericStatusWord.Value.ToString("X4") : "-")
                + " Raw=[" + Hex.ToString(RawResponse) + "]";
        }
    }

    public enum RfProtocol { TypeA = 0, TypeB = 1, Iso15693 = 2, Felica = 3 }

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

    // =======================================================================
    //  3. winscard.dll の P/Invoke
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
    //  4. Escape トランスポート（実機用）
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

    // =======================================================================
    //  5. Polling（重ね置きチェックの観測手段）
    // =======================================================================

    public static class Felica
    {
        /// <summary>対象カードのシステムコード。</summary>
        public const ushort LiteSSystemCode = 0x88B4;

        /// <summary>PMm の IC 種別。対象カードは 0xF1。チップ種別の確認であって認証ではない。</summary>
        public const byte RcS966IcType = 0xF1;

        public const byte TimeSlot16 = 15;

        /// <summary>LEN, 00, システムコード, リクエストコード, タイムスロット。</summary>
        public static byte[] BuildPolling()
        {
            return BuildPolling(LiteSSystemCode, 1, TimeSlot16);
        }

        public static byte[] BuildPolling(ushort systemCode, byte requestCode, byte timeSlot)
        {
            if (requestCode > 2)
                throw new ArgumentOutOfRangeException("requestCode");
            if (timeSlot != 0 && timeSlot != 1 && timeSlot != 3 && timeSlot != 7 && timeSlot != 15)
                throw new ArgumentOutOfRangeException("timeSlot",
                    "タイムスロットは 0 / 1 / 3 / 7 / 15 のいずれかです。");
            return new byte[] { 6, 0, (byte)(systemCode >> 8), (byte)systemCode, requestCode, timeSlot };
        }

        /// <summary>
        /// 対象カードの完全な Polling 応答（RC=01）だけを厳密に受理する。
        /// LEN=20, CMD=01, PMm[1]=F1, RD=88B4。1 バイトでもずれたら再同期せず全体を失敗にする。
        /// </summary>
        public static bool TryExtractLiteSIdm(byte[] buffer, out List<byte[]> ids)
        {
            ids = new List<byte[]>();
            if (buffer == null || buffer.Length == 0) return false;
            for (int i = 0; i < buffer.Length; i += 20)
            {
                if (buffer.Length - i < 20 || buffer[i] != 20 || buffer[i + 1] != 1
                    || buffer[i + 11] != RcS966IcType
                    || buffer[i + 18] != 0x88 || buffer[i + 19] != 0xB4)
                {
                    ids.Clear();
                    return false;
                }
                byte[] id = new byte[8];
                Buffer.BlockCopy(buffer, i + 2, id, 0, 8);
                ids.Add(id);
            }
            return true;
        }

        public static string IdmToString(byte[] idm)
        {
            if (idm == null || idm.Length != 8)
                throw new ArgumentException("IDm は 8 バイトです。", "idm");
            return Hex.ToString(idm, 8, false);
        }
    }

    // =======================================================================
    //  6. 対象カードのユーザデータ領域のコマンド生成と応答解析
    //
    //     Read/Write Without Encryption だけを扱う。MAC 付きアクセス・相互認証・
    //     鍵運用は実装していない。平文アクセスなので改ざん検知や機密保持には使えない。
    //
    //     対象はユーザブロック S_PAD0..S_PAD13 のみ。システムブロック（MC/CK/CKV/ID など）
    //     には意図的にアクセスできない。MC を誤って書くとカードが復旧不能になるため。
    // =======================================================================

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

    public static class FelicaLiteS
    {
        /// <summary>1 ブロックは 16 バイト固定。</summary>
        public const int BlockSize = 16;

        /// <summary>ユーザブロック S_PAD0。</summary>
        public const byte FirstUserBlock = 0x00;

        /// <summary>ユーザブロック S_PAD13。ここまでが読み書きを許可する範囲。</summary>
        public const byte LastUserBlock = 0x0D;

        /// <summary>ユーザブロック数（S_PAD0..S_PAD13）。</summary>
        public const int UserBlockCount = LastUserBlock - FirstUserBlock + 1;

        /// <summary>
        /// 既定アクセスサイズ = 先頭 16 バイト（S_PAD0 の 1 ブロック）。
        /// 読み書きは原則この 1 ブロックで行う。複数ブロックは範囲指定の
        /// オーバーロードを明示的に呼んだときだけ。
        /// </summary>
        public const int DefaultAccessLength = 1 * BlockSize;

        /// <summary>既定アクセスのブロック数。</summary>
        public const int DefaultAccessBlocks = DefaultAccessLength / BlockSize;

        /// <summary>
        /// Read Without Encryption 1 コマンドで読める最大ブロック数。
        /// 実機の対象カードは 5 ブロック以上で SF1=FF SF2=A2（ブロック数エラー）を返す。
        /// 確実に失敗する要求をカードまで送らないよう、組み立て時点で弾く。
        /// </summary>
        public const int MaxBlocksPerRead = 4;

        /// <summary>
        /// Write Without Encryption 1 コマンドで書ける最大ブロック数。
        /// 実機の対象カードは 2 ブロック目で SF1=02 SF2=A8 を返すため 1 固定。
        /// Pcsc.Write は複数ブロックをこの単位に分割して送る。
        /// </summary>
        public const int MaxBlocksPerWrite = 1;

        /// <summary>Random Service / Read Only / 認証不要。</summary>
        public const ushort ReadServiceCode = 0x000B;

        /// <summary>Random Service / Read Write / 認証不要。</summary>
        public const ushort WriteServiceCode = 0x0009;

        public const byte CommandRead = 0x06;
        public const byte ResponseRead = 0x07;
        public const byte CommandWrite = 0x08;
        public const byte ResponseWrite = 0x09;

        /// <summary>2 バイト形式のブロックリスト要素。アクセスモード 000、サービス順 0000。</summary>
        private const byte TwoByteElement = 0x80;

        /// <summary>ステータスフラグだけを返す応答の長さ（LEN, CMD, IDm, SF1, SF2）。</summary>
        private const int StatusOnlyResponseLength = 12;

        /// <summary>startBlock から blockCount 個の連続ブロック番号を作る。</summary>
        public static byte[] BlockRange(byte startBlock, int blockCount)
        {
            if (blockCount <= 0)
                throw new ArgumentOutOfRangeException("blockCount", "ブロック数は 1 以上です。");
            if (startBlock > LastUserBlock)
                throw new ArgumentOutOfRangeException("startBlock",
                    "開始ブロックがユーザ領域（0x00..0x0D）の外です。");
            // ここで startBlock は 0..LastUserBlock に確定しているので、右辺は 1..UserBlockCount。
            // startBlock + blockCount のような加算にすると blockCount = int.MaxValue で
            // オーバーフローして判定をすり抜け、直後の配列確保が巨大になる。
            if (blockCount > UserBlockCount - startBlock)
                throw new ArgumentOutOfRangeException("blockCount",
                    "ユーザ領域（0x00..0x0D）を超えています。");
            byte[] blocks = new byte[blockCount];
            for (int i = 0; i < blockCount; i++) blocks[i] = (byte)(startBlock + i);
            return blocks;
        }

        // ---- Read Without Encryption ---------------------------------------

        /// <summary>LEN, 06, IDm(8), サービス数, サービスコード(LE), ブロック数, ブロックリスト。</summary>
        public static byte[] BuildRead(byte[] idm, byte[] blocks)
        {
            ValidateIdm(idm);
            ValidateBlocks(blocks);
            if (blocks.Length > MaxBlocksPerRead)
                throw new ArgumentOutOfRangeException("blocks",
                    "Read は 1 コマンドあたり " + MaxBlocksPerRead
                    + " ブロックまでです（実機で 5 ブロック以上は SF1=FF SF2=A2 で拒否されることを確認済み）。");
            int length = 1 + 1 + 8 + 1 + 2 + 1 + blocks.Length * 2;
            if (length > 255)
                throw new ArgumentException("Read コマンドがフレーム長の上限を超えます。", "blocks");

            byte[] frame = new byte[length];
            int i = 0;
            frame[i++] = (byte)length;
            frame[i++] = CommandRead;
            Buffer.BlockCopy(idm, 0, frame, i, 8); i += 8;
            frame[i++] = 1;                            // サービス数
            frame[i++] = (byte)ReadServiceCode;        // リトルエンディアン
            frame[i++] = (byte)(ReadServiceCode >> 8);
            frame[i++] = (byte)blocks.Length;
            for (int b = 0; b < blocks.Length; b++)
            {
                frame[i++] = TwoByteElement;
                frame[i++] = blocks[b];
            }
            return frame;
        }

        /// <summary>
        /// LEN, 07, IDm(8), SF1, SF2, ブロック数, ブロックデータ を厳密に検証する。
        ///
        /// 「カードが明示的にエラーを返した」(<see cref="FelicaResponseKind.CardError"/>) と
        /// 「応答自体が信頼できない」(<see cref="FelicaResponseKind.Malformed"/>) を区別する。
        /// 両者は呼び出し側の復旧判断が異なるため、同じ失敗として潰してはならない。
        /// SF1/SF2 は CardError と Ok のときだけ実値を返す。それ以外は 0xFF（未到達）。
        /// </summary>
        public static FelicaResponseKind ParseReadResponse(byte[] response, byte[] expectedIdm,
            int expectedBlocks, out byte[] data, out byte statusFlag1, out byte statusFlag2)
        {
            data = new byte[0];
            statusFlag1 = 0xFF;
            statusFlag2 = 0xFF;
            if (expectedBlocks <= 0) throw new ArgumentOutOfRangeException("expectedBlocks");
            ValidateIdm(expectedIdm);

            // ---- 応答の形が信頼できるか --------------------------------
            if (response == null || response.Length < StatusOnlyResponseLength)
                return FelicaResponseKind.Malformed;
            if (response[0] != response.Length) return FelicaResponseKind.Malformed;
            if (response[1] != ResponseRead) return FelicaResponseKind.Malformed;
            if (!IdmMatches(response, 2, expectedIdm)) return FelicaResponseKind.Malformed;

            // ---- カードが明示的にエラーを返した --------------------------
            if (response[10] != 0x00 || response[11] != 0x00)
            {
                statusFlag1 = response[10];
                statusFlag2 = response[11];
                return FelicaResponseKind.CardError;
            }

            // ---- SF は正常なのにデータ部が整合しない = 応答が信頼できない ----
            if (response.Length < StatusOnlyResponseLength + 1) return FelicaResponseKind.Malformed;
            int blocks = response[12];
            if (blocks != expectedBlocks) return FelicaResponseKind.Malformed;
            if (response.Length != 13 + blocks * BlockSize) return FelicaResponseKind.Malformed;

            statusFlag1 = 0x00;
            statusFlag2 = 0x00;
            data = new byte[blocks * BlockSize];
            Buffer.BlockCopy(response, 13, data, 0, data.Length);
            return FelicaResponseKind.Ok;
        }

        /// <summary>
        /// <see cref="ParseReadResponse"/> の真偽値版。成功したときだけ true。
        /// 失敗理由を区別したい場合は <see cref="ParseReadResponse"/> を使うこと。
        /// </summary>
        public static bool TryParseReadResponse(byte[] response, byte[] expectedIdm, int expectedBlocks,
            out byte[] data, out byte statusFlag1, out byte statusFlag2)
        {
            return ParseReadResponse(response, expectedIdm, expectedBlocks,
                out data, out statusFlag1, out statusFlag2) == FelicaResponseKind.Ok;
        }

        // ---- Write Without Encryption --------------------------------------

        /// <summary>
        /// LEN, 08, IDm(8), サービス数, サービスコード(LE), ブロック数, ブロックリスト, ブロックデータ。
        /// data はブロック数 x 16 バイトちょうどでなければならない。
        /// </summary>
        public static byte[] BuildWrite(byte[] idm, byte[] blocks, byte[] data)
        {
            ValidateIdm(idm);
            ValidateBlocks(blocks);
            if (data == null || data.Length != blocks.Length * BlockSize)
                throw new ArgumentException(
                    "書き込みデータは " + (blocks.Length * BlockSize) + " バイトちょうどにしてください。", "data");
            int length = 1 + 1 + 8 + 1 + 2 + 1 + blocks.Length * 2 + data.Length;
            if (length > 255)
                throw new ArgumentException("Write コマンドがフレーム長の上限を超えます。", "blocks");

            byte[] frame = new byte[length];
            int i = 0;
            frame[i++] = (byte)length;
            frame[i++] = CommandWrite;
            Buffer.BlockCopy(idm, 0, frame, i, 8); i += 8;
            frame[i++] = 1;                             // サービス数
            frame[i++] = (byte)WriteServiceCode;        // リトルエンディアン
            frame[i++] = (byte)(WriteServiceCode >> 8);
            frame[i++] = (byte)blocks.Length;
            for (int b = 0; b < blocks.Length; b++)
            {
                frame[i++] = TwoByteElement;
                frame[i++] = blocks[b];
            }
            Buffer.BlockCopy(data, 0, frame, i, data.Length);
            return frame;
        }

        /// <summary>
        /// LEN(12), 09, IDm(8), SF1, SF2。SF1/SF2 がともに 0 のときだけ成功。
        /// 「応答が来たこと」を成功の根拠にしない。
        /// </summary>
        public static FelicaResponseKind ParseWriteResponse(byte[] response, byte[] expectedIdm,
            out byte statusFlag1, out byte statusFlag2)
        {
            statusFlag1 = 0xFF;
            statusFlag2 = 0xFF;
            ValidateIdm(expectedIdm);

            // ---- 応答の形が信頼できるか --------------------------------
            if (response == null || response.Length != StatusOnlyResponseLength)
                return FelicaResponseKind.Malformed;
            if (response[0] != StatusOnlyResponseLength) return FelicaResponseKind.Malformed;
            if (response[1] != ResponseWrite) return FelicaResponseKind.Malformed;
            if (!IdmMatches(response, 2, expectedIdm)) return FelicaResponseKind.Malformed;

            // ---- ここまで来ればカードの応答として読める ------------------
            statusFlag1 = response[10];
            statusFlag2 = response[11];
            if (statusFlag1 == 0x00 && statusFlag2 == 0x00) return FelicaResponseKind.Ok;
            return FelicaResponseKind.CardError;
        }

        /// <summary>
        /// <see cref="ParseWriteResponse"/> の真偽値版。成功したときだけ true。
        /// 失敗理由を区別したい場合は <see cref="ParseWriteResponse"/> を使うこと。
        /// </summary>
        public static bool TryParseWriteResponse(byte[] response, byte[] expectedIdm,
            out byte statusFlag1, out byte statusFlag2)
        {
            return ParseWriteResponse(response, expectedIdm, out statusFlag1, out statusFlag2)
                == FelicaResponseKind.Ok;
        }

        // ---- 共通 -----------------------------------------------------------

        internal static void ValidateIdm(byte[] idm)
        {
            if (idm == null || idm.Length != 8)
                throw new ArgumentException("IDm は 8 バイトです。", "idm");
        }

        /// <summary>
        /// ユーザブロック以外を拒否する。システムブロック（MC など）の破壊を防ぐ境界。
        /// 重複ブロック指定も拒否する。
        /// </summary>
        internal static void ValidateBlocks(byte[] blocks)
        {
            if (blocks == null || blocks.Length == 0)
                throw new ArgumentException("ブロックを 1 つ以上指定してください。", "blocks");
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] > LastUserBlock)
                    throw new ArgumentOutOfRangeException("blocks",
                        "ブロック 0x" + blocks[i].ToString("X2")
                        + " はユーザ領域（0x00..0x" + LastUserBlock.ToString("X2") + "）の外です。");
                for (int j = 0; j < i; j++)
                    if (blocks[i] == blocks[j])
                        throw new ArgumentException(
                            "ブロック 0x" + blocks[i].ToString("X2") + " が重複しています。", "blocks");
            }
        }

        private static bool IdmMatches(byte[] response, int offset, byte[] expected)
        {
            for (int i = 0; i < 8; i++)
                if (response[offset + i] != expected[i]) return false;
            return true;
        }
    }

    // =======================================================================
    //  7. 重ね置きチェック（CardDuplicateGuard 相当）
    //
    //     1 回のスキャンで観測できた事実だけを保持する。判定は Pcsc 側で行う。
    // =======================================================================

    public sealed class ScanObservation
    {
        /// <summary>IDm 文字列 -> IDm バイト列。異なる IDm の数がそのまま検出数になる。</summary>
        private readonly Dictionary<string, byte[]> _ids =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>通信異常・対象外応答・矛盾したフラグ。</summary>
        public bool Unknown { get; internal set; }

        /// <summary>RF 衝突。枚数は確定しない。</summary>
        public bool Collision { get; internal set; }

        /// <summary>いずれかのラウンドで明示的な無応答を観測した。</summary>
        public bool NoResponse { get; internal set; }

        /// <summary>観測できた異なる IDm の数。</summary>
        public int Count { get { return _ids.Count; } }

        public List<string> IdList
        {
            get
            {
                List<string> list = new List<string>(_ids.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        internal void Add(byte[] idm) { _ids[Felica.IdmToString(idm)] = idm; }

        /// <summary>異なる IDm がちょうど 1 種類のときだけ、その IDm を返す。</summary>
        public byte[] SingleIdm
        {
            get
            {
                if (_ids.Count != 1)
                    throw new InvalidOperationException("単一の IDm を観測していません。");
                foreach (KeyValuePair<string, byte[]> pair in _ids) return pair.Value;
                throw new InvalidOperationException("到達しません。");
            }
        }
    }

    /// <summary>
    /// 対象カードの Polling 観測ループ。
    /// 判定根拠を 1 箇所に集約し、Read 側と Write 側で片方だけ緩むことを防ぐ。
    /// </summary>
    public static class DuplicateGuard
    {
        /// <summary>
        /// 指定ラウンド数だけ Polling し、観測できた IDm の和集合と異常フラグを返す。
        /// 異なる IDm を 2 種類観測した時点で打ち切る（それ以上数えても判定は変わらない）。
        /// </summary>
        public static ScanObservation Collect(IPcscTransport transport, int rounds, int timeoutUs)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            ScanObservation observation = new ScanObservation();
            byte[] polling = Felica.BuildPolling();

            for (int round = 0; round < rounds; round++)
            {
                TransceiveResult t = transport.Transceive(polling, timeoutUs);

                // 矛盾したフラグは信用しない（代替トランスポートやテスト実装も保護する）。
                if (t == null || t.FlagCount != 1 || t.Unknown) { observation.Unknown = true; break; }
                if (t.Collision) { observation.Collision = true; break; }
                if (t.NoResponse) { observation.NoResponse = true; continue; }

                // DO 境界を保持する。途切れたフレームを次の DO と連結して補修しない。
                // 1 つの DO 内で連結された完全なフレームは、連結のまま解析する。
                IList<byte[]> buffers = t.Frames;
                if (buffers == null || buffers.Count == 0)
                {
                    buffers = new List<byte[]>();
                    buffers.Add(t.Data);
                }

                bool malformed = false;
                for (int b = 0; b < buffers.Count; b++)
                {
                    List<byte[]> ids;
                    if (!Felica.TryExtractLiteSIdm(buffers[b], out ids)) { malformed = true; break; }
                    for (int k = 0; k < ids.Count; k++) observation.Add(ids[k]);
                }
                if (malformed) { observation.Unknown = true; break; }
                if (observation.Count >= 2) break;
            }
            return observation;
        }
    }

    // =======================================================================
    //  8. 公開 API 本体
    //
    //     var pcsc = new Pcsc();
    //     pcsc.Connect();                 // (1)
    //     PcscResult r = pcsc.Read();     // (3) 重ね置きチェック込み
    //     pcsc.Write(payload);            // (4) 重ね置きチェック込み
    //     pcsc.Disconnect();              // (2)
    //
    //     Connect でリーダハンドル（SCardConnect / SHARE_DIRECT）を保持し、
    //     Read / Write のたびに Transparent Session を開いて
    //       RF OFF → フレーミング切替 → RF ON → Polling 観測 → 判定 → 読み書き
    //       → RF OFF → セッション終了
    //     を 1 つの RF セッション内で完結させる。
    //     「検査してから別セッションで接続する」間にカードが入れ替わる窓を作らないため。
    // =======================================================================

    public sealed class Pcsc : IDisposable
    {
        private readonly PcscOptions _opt;
        private readonly Func<IPcscTransport> _transportFactory;
        private readonly object _gate = new object();

        private IPcscTransport _transport;
        private string _readerName;

        /// <summary>既定オプション・実機トランスポートで生成する。</summary>
        public Pcsc()
            : this(null, null)
        {
        }

        /// <summary>オプションを指定して生成する。</summary>
        public Pcsc(PcscOptions options)
            : this(options, null)
        {
        }

        /// <summary>
        /// トランスポートを差し替えて生成する。テストでリーダ無しに検証するための入口。
        /// factory が null なら実機用 <see cref="Rcs300Transport"/> を使う。
        /// </summary>
        public Pcsc(PcscOptions options, Func<IPcscTransport> transportFactory)
        {
            PcscOptions source = options == null ? new PcscOptions() : options;
            // 走査中に呼び出し側が書き換えても効かないようスナップショットする。
            _opt = source.Snapshot();
            _opt.Validate();
            _transportFactory = transportFactory == null
                ? new Func<IPcscTransport>(CreateDefaultTransport)
                : transportFactory;
        }

        private static IPcscTransport CreateDefaultTransport()
        {
            return new Rcs300Transport();
        }

        /// <summary>Connect 済みなら true。</summary>
        public bool IsConnected
        {
            get { lock (_gate) { return _transport != null; } }
        }

        /// <summary>接続中のリーダ名。未接続なら空文字。</summary>
        public string ReaderName
        {
            get { lock (_gate) { return _readerName == null ? "" : _readerName; } }
        }

        /// <summary>接続中のリーダ名を列挙する。1 つも無ければ空のリスト。</summary>
        public static List<string> ListReaders()
        {
            IntPtr context = IntPtr.Zero;
            int rc = Interop.SCardEstablishContext(Interop.SCARD_SCOPE_USER,
                IntPtr.Zero, IntPtr.Zero, out context);
            if (rc != Interop.SCARD_S_SUCCESS) return new List<string>();
            try { return Interop.ListReaders(context); }
            finally { Interop.SCardReleaseContext(context); }
        }

        // ===================================================================
        //  (1) Connect
        // ===================================================================

        /// <summary>最初に見つかったリーダに接続する。</summary>
        public PcscResult Connect()
        {
            return Connect(null);
        }

        /// <summary>
        /// リーダに接続する。readerName が null / 空なら最初に見つかったリーダを使う。
        /// この時点では RF は起動しない（Read / Write のときだけ起動する）。
        /// </summary>
        public PcscResult Connect(string readerName)
        {
            lock (_gate)
            {
                Stopwatch sw = Stopwatch.StartNew();
                if (_transport != null)
                    return Log(PcscResult.Create(PcscStatus.AlreadyConnected,
                        "すでに接続しています: " + _readerName, sw.ElapsedMilliseconds));

                string target = readerName;
                try
                {
                    if (target == null || target.Trim().Length == 0)
                    {
                        List<string> readers = ListReaders();
                        if (readers.Count == 0)
                            return Log(PcscResult.Create(PcscStatus.ReaderError,
                                "リーダが見つかりません。接続とドライバを確認してください。",
                                sw.ElapsedMilliseconds));
                        target = readers[0];
                    }

                    IPcscTransport transport = _transportFactory();
                    if (transport == null)
                        throw new InvalidOperationException("トランスポートを生成できませんでした。");
                    try
                    {
                        transport.Open(target);
                    }
                    catch
                    {
                        try { transport.Dispose(); }
                        catch { }
                        throw;
                    }

                    _transport = transport;
                    _readerName = target;
                    return Log(PcscResult.Create(PcscStatus.Ok,
                        "接続しました: " + target, sw.ElapsedMilliseconds));
                }
                catch (Exception ex)
                {
                    return Log(PcscResult.Failure(PcscStatus.ReaderError,
                        "接続に失敗しました: " + ex.Message, sw.ElapsedMilliseconds, ex));
                }
            }
        }

        // ===================================================================
        //  (2) Disconnect
        // ===================================================================

        /// <summary>
        /// リーダから切断する。未接続でも成功として扱う（冪等）。
        /// RF とトランスペアレントセッションが残っていればトランスポート側が落とす。
        /// </summary>
        public PcscResult Disconnect()
        {
            lock (_gate)
            {
                Stopwatch sw = Stopwatch.StartNew();
                if (_transport == null)
                    return Log(PcscResult.Create(PcscStatus.Ok,
                        "接続していません（何もしません）", sw.ElapsedMilliseconds));

                string target = _readerName;
                IPcscTransport transport = _transport;
                _transport = null;
                _readerName = null;
                try
                {
                    transport.Dispose();
                    return Log(PcscResult.Create(PcscStatus.Ok,
                        "切断しました: " + target, sw.ElapsedMilliseconds));
                }
                catch (Exception ex)
                {
                    // ハンドルはすでに手放しているので、再接続は可能。
                    return Log(PcscResult.Failure(PcscStatus.ReaderError,
                        "切断処理で例外が発生しました: " + ex.Message, sw.ElapsedMilliseconds, ex));
                }
            }
        }

        // ===================================================================
        //  (3) Read
        // ===================================================================

        /// <summary>
        /// ユーザデータ領域の先頭 16 バイト（S_PAD0 の 1 ブロック）を読み出す。
        /// 読み書きは原則こちらを使う。
        /// </summary>
        public PcscResult Read()
        {
            return Read(FelicaLiteS.FirstUserBlock, FelicaLiteS.DefaultAccessBlocks);
        }

        /// <summary>
        /// ユーザブロック startBlock から blockCount ブロック（16 バイト単位）を読み出す。
        /// 対象は S_PAD0..S_PAD13 のみ。システムブロックは指定できない。
        /// blockCount は 1..<see cref="FelicaLiteS.MaxBlocksPerRead"/>（実機確認済みの上限）。
        ///
        /// 実行前に重ね置きチェックを行い、異なる IDm を 2 種類以上観測した場合は
        /// MultipleCards を返し、Read コマンドは一切送出しない。
        /// </summary>
        public PcscResult Read(byte startBlock, int blockCount)
        {
            byte[] blocks;
            try
            {
                blocks = FelicaLiteS.BlockRange(startBlock, blockCount);
                // I/O 前にコマンドを組み立てて、実機が拒否する要求をここで弾く。
                FelicaLiteS.BuildRead(new byte[8], blocks);
            }
            catch (Exception ex)
            {
                return Log(PcscResult.Failure(PcscStatus.InvalidArgument, ex.Message, 0, ex));
            }
            return Execute("read", new Func<IPcscTransport, byte[], Stopwatch, PcscResult>(
                delegate(IPcscTransport transport, byte[] idm, Stopwatch sw)
                {
                    return DoRead(transport, idm, blocks, sw);
                }));
        }

        // ===================================================================
        //  (4) Write
        // ===================================================================

        /// <summary>
        /// ユーザデータ領域の先頭に書き込む。読み書きは原則 1 ブロックなので、
        /// data は 16 バイト（S_PAD0 のみ）を想定している。
        /// 16 バイトの倍数を渡せば複数ブロックにも書けるが、その場合は原子的ではない。
        /// </summary>
        public PcscResult Write(byte[] data)
        {
            return Write(FelicaLiteS.FirstUserBlock, data);
        }

        /// <summary>
        /// ユーザブロック startBlock から data を書き込む。data は 16 バイトの倍数。
        /// 対象は S_PAD0..S_PAD13 のみ。システムブロックは指定できない。
        ///
        /// 実行前に重ね置きチェックを行い、異なる IDm を 2 種類以上観測した場合は
        /// MultipleCards を返し、Write コマンドは一切送出しない。
        ///
        /// 注意: 対象カードの Write Without Encryption は 1 コマンドあたり 1 ブロックしか
        /// 受け付けない（実機で SF1=02 SF2=A8 を確認）。したがって複数ブロックの書き込みは
        /// 複数コマンドに分かれ、原子的ではない。途中で失敗するとカードは中途半端な状態で残る。
        /// </summary>
        public PcscResult Write(byte startBlock, byte[] data)
        {
            byte[] blocks;
            byte[] payload;
            try
            {
                if (data == null || data.Length == 0)
                    throw new ArgumentException("書き込みデータが空です。", "data");
                if (data.Length % FelicaLiteS.BlockSize != 0)
                    throw new ArgumentException(
                        "書き込みデータは " + FelicaLiteS.BlockSize + " バイトの倍数にしてください。", "data");
                blocks = FelicaLiteS.BlockRange(startBlock, data.Length / FelicaLiteS.BlockSize);
                // 呼び出し側が後から配列を書き換えても送出内容が変わらないよう複製する。
                payload = (byte[])data.Clone();
            }
            catch (Exception ex)
            {
                return Log(PcscResult.Failure(PcscStatus.InvalidArgument, ex.Message, 0, ex));
            }
            return Execute("write", new Func<IPcscTransport, byte[], Stopwatch, PcscResult>(
                delegate(IPcscTransport transport, byte[] idm, Stopwatch sw)
                {
                    return DoWrite(transport, idm, blocks, payload, sw);
                }));
        }

        // ===================================================================
        //  セッション制御と重ね置き判定
        // ===================================================================

        private PcscResult Execute(string operation,
            Func<IPcscTransport, byte[], Stopwatch, PcscResult> action)
        {
            lock (_gate)
            {
                Stopwatch sw = Stopwatch.StartNew();
                if (_transport == null)
                    return Log(PcscResult.Create(PcscStatus.NotConnected,
                        "Connect() を先に呼んでください。", sw.ElapsedMilliseconds));

                IPcscTransport transport = _transport;
                PcscResult result;
                try
                {
                    transport.StartTransparentSession();
                    transport.TurnOffRf();
                    transport.SwitchProtocol(RfProtocol.Felica);
                    transport.TurnOnRf();
                    if (_opt.RfSettleDelayMs > 0) Thread.Sleep(_opt.RfSettleDelayMs);

                    ScanObservation observation = DuplicateGuard.Collect(
                        transport, _opt.DuplicateCheckRounds, _opt.TransceiveTimeoutUs);

                    PcscResult blocked = Gate(observation, sw);
                    result = blocked != null ? blocked : action(transport, observation.SingleIdm, sw);

                    // 終了処理の失敗は、成功していた読み書きも無効にする（例外は下で捕捉）。
                    transport.TurnOffRf();
                    transport.EndTransparentSession();
                }
                catch (Exception ex)
                {
                    // リーダを既知の状態に戻す。失敗しても結果は Indeterminate のまま。
                    try { transport.TurnOffRf(); }
                    catch { }
                    try { transport.EndTransparentSession(); }
                    catch { }
                    result = PcscResult.Failure(PcscStatus.Indeterminate,
                        operation + ": " + ex.Message, sw.ElapsedMilliseconds, ex);
                }
                return Log(result);
            }
        }

        /// <summary>
        /// 重ね置き判定。単一 IDm を確定できないかぎり読み書きさせない。
        /// 続行可能なら null を返す。
        /// </summary>
        private static PcscResult Gate(ScanObservation o, Stopwatch sw)
        {
            if (o.Count >= 2)
                return PcscResult.Create(PcscStatus.MultipleCards,
                    "異なる対象カードの IDm を " + o.Count + " 種類検出（重ね置き）。読み書きしない: "
                    + string.Join(",", o.IdList.ToArray()), sw.ElapsedMilliseconds);
            if (o.Collision)
                return PcscResult.Create(PcscStatus.Indeterminate,
                    "RF 衝突を検出（枚数未確定）。読み書きしない", sw.ElapsedMilliseconds);
            if (o.Unknown)
                return PcscResult.Create(PcscStatus.Indeterminate,
                    "通信異常または対象外・不正応答。読み書きしない", sw.ElapsedMilliseconds);
            if (o.Count == 0)
                return PcscResult.Create(PcscStatus.NoCard,
                    "対象カードの応答を観測しなかった（物理的な不存在の証明ではない）", sw.ElapsedMilliseconds);
            if (o.NoResponse)
                return PcscResult.Create(PcscStatus.Indeterminate,
                    "検査中に応答が途切れた。置き直して再実行", sw.ElapsedMilliseconds);
            return null;
        }

        // ===================================================================
        //  読み書き本体
        // ===================================================================

        private PcscResult DoRead(IPcscTransport transport, byte[] idm, byte[] blocks, Stopwatch sw)
        {
            string idmText = Felica.IdmToString(idm);

            byte[] payload;
            byte[] frame = FelicaLiteS.BuildRead(idm, blocks);
            if (!TrySingleFrame(transport.Transceive(frame, _opt.TransceiveTimeoutUs), out payload))
                return PcscResult.Create(PcscStatus.Indeterminate,
                    "読み出し応答を解析できない", sw.ElapsedMilliseconds, idmText);

            byte[] data;
            byte sf1, sf2;
            FelicaResponseKind kind = FelicaLiteS.ParseReadResponse(
                payload, idm, blocks.Length, out data, out sf1, out sf2);
            if (kind == FelicaResponseKind.CardError)
                return new PcscResult(PcscStatus.CardRejected,
                    "カードが読み出しを拒否した", sw.ElapsedMilliseconds,
                    null, idmText, sf1, sf2, null);
            if (kind != FelicaResponseKind.Ok)
                // 応答が信頼できない。カードが拒否したのかどうかも確定していない。
                return PcscResult.Create(PcscStatus.Indeterminate,
                    "読み出し応答が不正。カードの状態を確認できない", sw.ElapsedMilliseconds, idmText);

            return new PcscResult(PcscStatus.Ok,
                blocks.Length + " ブロック（" + data.Length + " バイト）を読み出した",
                sw.ElapsedMilliseconds, data, idmText, sf1, sf2, null);
        }

        private PcscResult DoWrite(IPcscTransport transport, byte[] idm, byte[] blocks,
            byte[] data, Stopwatch sw)
        {
            string idmText = Felica.IdmToString(idm);
            byte sf1 = 0xFF, sf2 = 0xFF;

            // 1 コマンド 1 ブロック。まとめて送ると 2 ブロック目で拒否される（実機確認済み）。
            for (int i = 0; i < blocks.Length; i++)
            {
                byte[] block = new byte[FelicaLiteS.BlockSize];
                Buffer.BlockCopy(data, i * FelicaLiteS.BlockSize, block, 0, block.Length);
                byte[] frame = FelicaLiteS.BuildWrite(idm, new byte[] { blocks[i] }, block);

                byte[] payload;
                if (!TrySingleFrame(transport.Transceive(frame, _opt.TransceiveTimeoutUs), out payload))
                    return PcscResult.Create(PcscStatus.Indeterminate,
                        "ブロック 0x" + blocks[i].ToString("X2")
                        + " の書き込み応答を解析できない。書き込まれたか不明",
                        sw.ElapsedMilliseconds, idmText);

                FelicaResponseKind kind = FelicaLiteS.ParseWriteResponse(payload, idm, out sf1, out sf2);
                if (kind == FelicaResponseKind.CardError)
                    return new PcscResult(PcscStatus.CardRejected,
                        "カードがブロック 0x" + blocks[i].ToString("X2") + " の書き込みを拒否した"
                        + (i > 0 ? "。先行ブロックは書き換わっている" : ""),
                        sw.ElapsedMilliseconds, null, idmText, sf1, sf2, null);
                if (kind != FelicaResponseKind.Ok)
                    // 応答が信頼できない。このブロックが書き込まれたかどうかは確定していない。
                    return PcscResult.Create(PcscStatus.Indeterminate,
                        "ブロック 0x" + blocks[i].ToString("X2") + " の書き込み応答が不正。書き込まれたか不明"
                        + (i > 0 ? "。先行ブロックは書き換わっている" : ""),
                        sw.ElapsedMilliseconds, idmText);
            }

            if (!_opt.VerifyAfterWrite)
                return new PcscResult(PcscStatus.Ok,
                    blocks.Length + " ブロックを書き込んだ（読み戻し未実施）",
                    sw.ElapsedMilliseconds, null, idmText, sf1, sf2, null);

            // 成功応答だけを信用せず、同一セッション内で読み戻して一致を確認する。
            //
            // Read は 1 コマンド 4 ブロックまで（MaxBlocksPerRead）なので、書き込んだ範囲が
            // それを超える場合は分割して読み戻す。まとめて渡すと BuildRead が例外になり、
            // カードは書き換わっているのに Indeterminate で返ることになる。
            for (int offset = 0; offset < blocks.Length; offset += FelicaLiteS.MaxBlocksPerRead)
            {
                int count = Math.Min(FelicaLiteS.MaxBlocksPerRead, blocks.Length - offset);
                PcscResult verify = DoRead(transport, idm, Slice(blocks, offset, count), sw);
                if (!verify.Ok)
                    return new PcscResult(PcscStatus.Indeterminate,
                        "書き込み後の読み戻しに失敗。内容を確認できない", sw.ElapsedMilliseconds,
                        null, idmText, verify.StatusFlag1, verify.StatusFlag2, null);
                byte[] expected = Slice(data, offset * FelicaLiteS.BlockSize,
                    count * FelicaLiteS.BlockSize);
                if (!Hex.SequenceEqual(verify.Data, expected))
                    return new PcscResult(PcscStatus.Indeterminate,
                        "書き込み後の読み戻しが一致しない", sw.ElapsedMilliseconds,
                        null, idmText, verify.StatusFlag1, verify.StatusFlag2, null);
            }

            return new PcscResult(PcscStatus.Ok,
                blocks.Length + " ブロックを書き込み、読み戻しで一致を確認",
                sw.ElapsedMilliseconds, null, idmText, sf1, sf2, null);
        }

        /// <summary>
        /// 読み書きは特定 IDm 宛なので、正常な応答フレームはちょうど 1 個。
        /// 矛盾フラグ・無応答・衝突・複数フレームはすべて失敗として扱う。
        /// </summary>
        /// <summary>配列の一部を複製して返す。</summary>
        private static byte[] Slice(byte[] source, int offset, int length)
        {
            byte[] part = new byte[length];
            Buffer.BlockCopy(source, offset, part, 0, length);
            return part;
        }

        private static bool TrySingleFrame(TransceiveResult t, out byte[] payload)
        {
            payload = new byte[0];
            if (t == null || t.FlagCount != 1 || !t.Ok) return false;
            if (t.Frames == null || t.Frames.Count != 1) return false;
            payload = t.Frames[0];
            return true;
        }

        // ===================================================================
        //  ログ / 後始末
        // ===================================================================

        private PcscResult Log(PcscResult result)
        {
            Action<string> log = _opt.Log;
            if (log != null)
            {
                // ログ失敗が結果を迂回してはならない。
                try { log("[pcsc] " + result.ToString()); }
                catch { }
            }
            return result;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
