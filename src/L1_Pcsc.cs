// ===========================================================================
//  L1_Pcsc.cs  --  層1 公開 API: Pcsc 本体（Connect / Disconnect / Read / Write）
//
//  対象カード : 特定の IC カード（システムコード・IC 種別で判別。値は L2_FelicaCommands.cs の Felica の定数を参照）
//  対象リーダ : CCID Escape / Transparent Session に対応した PC/SC リーダ
//
//  ビルド前提 : .NET Framework 4.5 / C# 5.0 / Visual Studio 2013
//               src/ 配下のファイルでは以下の C# 6 以降の構文を意図的に使用していない。
//                 文字列補間 / nameof / 式形式メンバ / 自動プロパティ初期化子 /
//                 get のみの自動プロパティ / null 条件演算子 / out var /
//                 using 宣言 / 数値区切り / タプル / init / Array.Empty
//               （Array.Empty は .NET Framework 4.6 以降のため 4.5 では使えない）
//
//  ---- 層構成 --------------------------------------------------------------
//  ファイル名の Ln_ が層番号。数字が小さいほど呼び出し側（業務アプリ）に近く、
//  大きいほどハードウェアに近い。関連する型を役割ごとに 1 ファイルにまとめているが、
//  型そのものは分離したまま。
//
//    層1 公開 API          L1_Pcsc.cs            Pcsc
//                          L1_PcscModels.cs      PcscOptions / PcscResult / PcscStatus
//        重ね置きチェック  L1_DuplicateGuard.cs  DuplicateGuard / ScanObservation
//    層2 カードコマンド    L2_FelicaCommands.cs  Felica（Polling） / FelicaLiteS（Read / Write） / FelicaResponseKind
//    層3 リーダ応答 TLV    L3_PcscResponse.cs    PcscResponse / TransceiveResult
//    層4 リーダ制御        L4_PcscTransport.cs   IPcscTransport / RfProtocol / Rcs300Transport（FF 50 の封筒）
//    層5 PC/SC             L5_PcscNative.cs      Interop（winscard.dll の P/Invoke） / PcscException
//    共通                  Common_Hex.cs         Hex
//
//  層番号は役割の分類であり、すべての呼び出しが層1 から層5 へ順番に進むわけではない
//  （例: ListReaders は層5 の Interop を直接呼ぶ）。
//
//  送信時は 層2 のカードフレームを 層4 が FF 50 の封筒に包み、層5 の SCardControl で送る。
//  応答は 層3 が TLV を解釈し、中のカード応答フレームを 層2 が解析する。
//  各層のバイト列の詳細は protocol.md（章立ても同じ層番号）を参照。
//
//  ---- 公開 API -------------------------------------------------------------
//      pcsc.Connect()     リーダに接続する
//      pcsc.Disconnect()  リーダから切断する
//      pcsc.Read()        ユーザデータ領域を読み出す
//      pcsc.Write()       ユーザデータ領域を書き込む
//
//      var pcsc = new Pcsc();
//      pcsc.Connect();                 // (1)
//      PcscResult r = pcsc.Read();     // (3) 重ね置きチェック込み
//      pcsc.Write(payload);            // (4) 重ね置きチェック込み
//      pcsc.Disconnect();              // (2)
//
//  Connect でリーダハンドル（SCardConnect / SHARE_DIRECT）を保持し、
//  Read / Write のたびに Transparent Session を開いて
//    RF OFF → フレーミング切替 → RF ON → Polling 観測 → 判定 → 読み書き
//    → RF OFF → セッション終了
//  を 1 つの RF セッション内で完結させる。
//  「検査してから別セッションで接続する」間にカードが入れ替わる窓を作らないため。
//
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
using System.Threading;

namespace PcscIf
{
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

        /// <summary>配列の一部を複製して返す。</summary>
        private static byte[] Slice(byte[] source, int offset, int length)
        {
            byte[] part = new byte[length];
            Buffer.BlockCopy(source, offset, part, 0, length);
            return part;
        }

        /// <summary>
        /// 読み書きは特定 IDm 宛なので、正常な応答フレームはちょうど 1 個。
        /// 矛盾フラグ・無応答・衝突・複数フレームはすべて失敗として扱う。
        /// </summary>
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
