// ===========================================================================
//  SelfTest.cs -- リーダ不要の自己テスト。CI に組み込むこと。
//
//  既知ベクタ（コマンド組み立て）、応答パーサの異常系、そして
//  IPcscTransport を差し替えた模擬セッションでの Connect / Disconnect /
//  Read / Write と重ね置き検出を検証する。
//  加えて、重ね置きの観測ループ単体、並行呼び出し、16 進変換、
//  実機トランスポートのうちリーダを使わずに確認できる部分を検証する。
//  終了コードは失敗数。
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using PcscIf;

namespace PcscIfTest
{
    internal static class SelfTest
    {
        private static int _failed;
        private static int _passed;

        public static int Run()
        {
            _failed = 0;
            _passed = 0;

            Section("1. コマンド組み立ての既知ベクタ");
            CommandVectors();
            CommandLengthLimits();

            Section("2. PC/SC 応答パーサ（fail-closed）");
            ResponseParsing();
            ResponseParsingDetails();

            Section("3. 対象カードの Polling 応答の抽出");
            PollingExtraction();

            Section("4. ユーザ領域の境界チェック");
            BlockBoundaries();

            Section("5. 読み書き応答の解析");
            AccessResponseParsing();

            Section("6. Connect / Disconnect");
            ConnectLifecycle();
            ConnectFailurePaths();

            Section("7. Read と重ね置きチェック");
            ReadTests();
            ReadFailurePaths();

            Section("8. Write と重ね置きチェック");
            WriteTests();
            WriteFailurePaths();

            Section("9. オプション検証とログ");
            OptionTests();
            OptionBoundariesAndResult();

            Section("10. 重ね置きの観測（DuplicateGuard / ScanObservation）");
            ObservationTests();

            Section("11. 並行呼び出し");
            ConcurrencyTests();

            Section("12. 16 進変換（Hex）");
            HexTests();

            Section("13. 実機トランスポートのリーダ不要な検査");
            OfflineTransportTests();

            Console.WriteLine();
            Console.WriteLine(_failed == 0
                ? "SelfTest: ALL PASS (" + _passed + " checks)"
                : "SelfTest: " + _failed + " FAILED / " + (_passed + _failed) + " checks");
            return _failed;
        }

        // ===================================================================
        //  1. コマンド組み立て
        // ===================================================================

        private static void CommandVectors()
        {
            Expect("Polling は 88B4 / RC=01 / 16 スロット",
                Felica.BuildPolling(),
                new byte[] { 0x06, 0x00, 0x88, 0xB4, 0x01, 0x0F });
            Expect("Polling TSN=1",
                Felica.BuildPolling(0xFFFF, 0x01, 0),
                new byte[] { 0x06, 0x00, 0xFF, 0xFF, 0x01, 0x00 });
            Check("不正なタイムスロットは拒否",
                Throws(delegate { Felica.BuildPolling(0xFFFF, 1, 5); }));
            Check("不正なリクエストコードは拒否",
                Throws(delegate { Felica.BuildPolling(0xFFFF, 3, 15); }));

            Expect("プロトコル切替はフレーミング指定（アクティベーションではない）",
                Rcs300Transport.BuildFelicaSwitchApdu(),
                new byte[] { 0xFF, 0x50, 0x00, 0x02, 0x04, 0x8F, 0x02, 0x03, 0x00, 0x00 });

            Expect("Transceive APDU（Polling, 100ms）",
                Rcs300Transport.BuildTransceiveApdu(
                    new byte[] { 0x06, 0x00, 0xFF, 0xFF, 0x01, 0x00 }, 100000),
                new byte[] {
                    0xFF, 0x50, 0x00, 0x01, 0x00, 0x00, 0x11,
                    0x5F, 0x46, 0x04, 0xA0, 0x86, 0x01, 0x00,
                    0x95, 0x82, 0x00, 0x06,
                    0x06, 0x00, 0xFF, 0xFF, 0x01, 0x00,
                    0x00, 0x00, 0x00 });
            Check("空フレームの Transceive は拒否",
                Throws(delegate { Rcs300Transport.BuildTransceiveApdu(new byte[0], 100000); }));
            Check("タイムアウト 0 の Transceive は拒否",
                Throws(delegate { Rcs300Transport.BuildTransceiveApdu(new byte[] { 6 }, 0); }));

            byte[] idm = Idm(1);
            Expect("Read コマンド（S_PAD0+S_PAD1, サービス 000B）",
                FelicaLiteS.BuildRead(idm, FelicaLiteS.BlockRange(0, 2)),
                new byte[] { 0x12, 0x06, 1, 0, 0, 0, 0, 0, 0, 1,
                             0x01, 0x0B, 0x00, 0x02, 0x80, 0x00, 0x80, 0x01 });
            Expect("Write コマンド（S_PAD0, サービス 0009）",
                FelicaLiteS.BuildWrite(idm, FelicaLiteS.BlockRange(0, 1), Payload(0x10, 16)),
                Concat(new byte[] { 0x20, 0x08, 1, 0, 0, 0, 0, 0, 0, 1,
                                    0x01, 0x09, 0x00, 0x01, 0x80, 0x00 }, Payload(0x10, 16)));
            Check("既定アクセスは 16 バイト = 1 ブロック",
                FelicaLiteS.DefaultAccessLength == 16 && FelicaLiteS.DefaultAccessBlocks == 1
                && FelicaLiteS.BlockSize == 16);
        }

        // ===================================================================
        //  2. 応答パーサ
        // ===================================================================

        private static void ResponseParsing()
        {
            byte[] good = Response(Card(1));
            Check("正常な C0/92/96/97 応答", Parse(good).Ok);

            byte[] swError = (byte[])good.Clone();
            swError[swError.Length - 2] = 0x6F;
            Check("エラー SW は成功にならない", Parse(swError).Unknown);

            byte[] genericError = (byte[])good.Clone();
            genericError[3] = 0x6A; genericError[4] = 0x81;
            Check("C0 のエラーは成功にならない", Parse(genericError).Unknown);

            byte[] collision = (byte[])good.Clone(); collision[10] = 2;
            Check("DO96 の衝突はデータがあっても衝突扱い",
                Parse(collision).Collision && !Parse(collision).Ok);

            byte[] crc = (byte[])good.Clone(); crc[10] = 1;
            Check("DO96 の CRC 異常はデータを無効化", Parse(crc).Unknown);
            byte[] parity = (byte[])good.Clone(); parity[10] = 4;
            Check("DO96 のパリティ異常はデータを無効化", Parse(parity).Unknown);
            byte[] framing = (byte[])good.Clone(); framing[10] = 8;
            Check("DO96 のフレーミング異常はデータを無効化", Parse(framing).Unknown);
            byte[] bits = (byte[])good.Clone(); bits[7] = 7;
            Check("端数ビットのある応答は不正", Parse(bits).Unknown);

            Check("C0 6401 だけが「応答なし」",
                Parse(new byte[] { 0xC0, 3, 2, 0x64, 1, 0x90, 0 }).NoResponse);
            ushort[] failures = new ushort[] { 0x6400, 0x6700, 0x6A80, 0x6A81, 0x6F00 };
            for (int i = 0; i < failures.Length; i++)
                Check("C0 " + failures[i].ToString("X4") + " は空スロットではない",
                    Parse(new byte[] { 0xC0, 3, 2, (byte)(failures[i] >> 8), (byte)failures[i], 0x90, 0 }).Unknown);

            byte[] contradictory = (byte[])good.Clone();
            contradictory[2] = 2; contradictory[3] = 0x64; contradictory[4] = 1;
            Check("「応答なし」とデータの同居は拒否", Parse(contradictory).Unknown);
            Check("裸の 9000 は空スロットではない", Parse(new byte[] { 0x90, 0 }).Unknown);
            Check("DO92 の不正値は拒否", Parse(new byte[] { 0x92, 1, 0x64, 0x90, 0 }).Unknown);
            Check("SW 欠落は拒否", Parse(Take(good, good.Length - 2)).Unknown);
            bool allTruncatedRejected = true;
            for (int n = 0; n < good.Length; n++)
                if (Parse(Take(good, n)).Ok) allTruncatedRejected = false;
            Check("あらゆる途中切れは成功にならない", allTruncatedRejected);
            Check("末尾の壊れた TLV は拒否",
                Parse(Concat(Take(good, good.Length - 2), new byte[] { 0x97, 3, 1, 0x90, 0 })).Unknown);
            Check("不定長 BER は拒否", Parse(new byte[] { 0x97, 0x80, 0x90, 0 }).Unknown);
            Check("C0 の重複は拒否",
                Parse(new byte[] { 0xC0, 3, 0, 0x90, 0, 0xC0, 3, 0, 0x90, 0, 0x90, 0 }).Unknown);
            Check("未知タグは fail-closed", Parse(new byte[] { 0xAA, 0, 0x90, 0 }).Unknown);
            Check("複数の DO97 はすべて保持",
                Parse(Response(Card(1), Card(2))).Frames.Count == 2);

            Check("管理コマンドは検証済みの成功だけ受理",
                PcscResponse.Parse(new byte[] { 0xC0, 3, 0, 0x90, 0, 0x90, 0 }).CommandSucceeded);
            Check("管理コマンドは C0 の失敗を拒否",
                Throws(delegate { Rcs300Transport.ValidateManagementResponse(genericError, "test"); }));
            Check("管理コマンドはエラー SW を拒否",
                Throws(delegate { Rcs300Transport.ValidateManagementResponse(new byte[] { 0x6F, 0 }, "test"); }));
            Check("管理コマンドは空応答を拒否",
                Throws(delegate { Rcs300Transport.ValidateManagementResponse(new byte[0], "test"); }));
        }

        // ===================================================================
        //  3. Polling 応答の抽出
        // ===================================================================

        private static void PollingExtraction()
        {
            List<byte[]> ids;
            Check("正常な対象カードのフレームを解析",
                Felica.TryExtractLiteSIdm(Card(1), out ids) && ids.Count == 1);
            Check("連結された 2 フレームを解析",
                Felica.TryExtractLiteSIdm(Concat(Card(1), Card(2)), out ids) && ids.Count == 2);
            Check("末尾のゴミは拒否",
                !Felica.TryExtractLiteSIdm(Concat(Card(1), new byte[] { 0 }), out ids));
            Check("途中切れは拒否",
                !Felica.TryExtractLiteSIdm(Take(Card(1), 19), out ids));

            byte[] wrongType = Card(1); wrongType[11] = 0xF0;
            Check("対象カード以外の IC 種別は拒否", !Felica.TryExtractLiteSIdm(wrongType, out ids));
            byte[] wrongSystem = Card(1); wrongSystem[18] = 0x12; wrongSystem[19] = 0xFC;
            Check("システムコード違いは拒否", !Felica.TryExtractLiteSIdm(wrongSystem, out ids));

            Check("IDm 文字列は 16 桁の大文字",
                Felica.IdmToString(Idm(0xAB)) == "01000000000000AB");
            Check("8 バイト以外の IDm は拒否",
                Throws(delegate { Felica.IdmToString(new byte[7]); }));
        }

        // ===================================================================
        //  4. ユーザ領域の境界
        // ===================================================================

        private static void BlockBoundaries()
        {
            byte[] idm = Idm(1);
            Check("システムブロック MC(0x88) は読み出し不可",
                Throws(delegate { FelicaLiteS.BuildRead(idm, new byte[] { 0x88 }); }));
            Check("システムブロック MC(0x88) は書き込み不可",
                Throws(delegate { FelicaLiteS.BuildWrite(idm, new byte[] { 0x88 }, Payload(0, 16)); }));
            Check("S_PAD13 を超えるブロックは拒否",
                Throws(delegate { FelicaLiteS.BuildRead(idm, new byte[] { 0x0E }); }));
            Check("S_PAD13 は許可", FelicaLiteS.BuildRead(idm, new byte[] { 0x0D }).Length == 16);
            Check("ブロック重複は拒否",
                Throws(delegate { FelicaLiteS.BuildWrite(idm, new byte[] { 0, 0 }, Payload(0, 32)); }));
            Check("空のブロックリストは拒否",
                Throws(delegate { FelicaLiteS.BuildRead(idm, new byte[0]); }));
            Check("IDm 長の誤りは拒否",
                Throws(delegate { FelicaLiteS.BuildRead(new byte[7], FelicaLiteS.BlockRange(0, 2)); }));
            Check("書き込みデータ長はブロック数と一致必須",
                Throws(delegate { FelicaLiteS.BuildWrite(idm, FelicaLiteS.BlockRange(0, 2), Payload(0, 16)); }));
            Check("BlockRange はユーザ領域を超えない",
                Throws(delegate { FelicaLiteS.BlockRange(0, FelicaLiteS.UserBlockCount + 1); }));
            Check("BlockRange は全 14 ブロックを許可",
                FelicaLiteS.BlockRange(0, FelicaLiteS.UserBlockCount).Length == 14);
            // 実機の対象カードは 5 ブロック以上の Read を SF1=FF SF2=A2 で拒否する。
            Check("Read は 4 ブロックまで許可",
                FelicaLiteS.BuildRead(idm, FelicaLiteS.BlockRange(0, FelicaLiteS.MaxBlocksPerRead)).Length == 22);
            Check("Read の 5 ブロック以上は組み立て時点で拒否",
                Throws(delegate {
                    FelicaLiteS.BuildRead(idm, FelicaLiteS.BlockRange(0, FelicaLiteS.MaxBlocksPerRead + 1)); }));

            // BlockRange の範囲検査。startBlock + blockCount で判定するとオーバーフローし、
            // 判定をすり抜けて巨大な配列を確保しに行く。
            // 引数エラーであることまで見る。判定をすり抜けて new byte[blockCount] に
            // 到達すると OutOfMemoryException になり、Throws() だけでは区別できない。
            Check("blockCount の桁あふれは引数エラー（巨大確保に到達しない・開始 2）",
                ThrowsArgument(delegate { FelicaLiteS.BlockRange(2, int.MaxValue); }));
            Check("blockCount の桁あふれは引数エラー（開始 0）",
                ThrowsArgument(delegate { FelicaLiteS.BlockRange(0, int.MaxValue); }));
            Check("開始ブロックがユーザ領域外なら拒否",
                Throws(delegate { FelicaLiteS.BlockRange(0x0E, 1); }));
            Check("境界: (13,1) は許可",
                FelicaLiteS.BlockRange(FelicaLiteS.LastUserBlock, 1).Length == 1);
            Check("境界: (13,2) は拒否",
                Throws(delegate { FelicaLiteS.BlockRange(FelicaLiteS.LastUserBlock, 2); }));
        }

        // ===================================================================
        //  5. 読み書き応答の解析
        // ===================================================================

        private static void AccessResponseParsing()
        {
            byte[] idm = Idm(1);
            byte[] payload = Payload(0x10, 32);
            byte[] data;
            byte sf1, sf2;

            Check("正常な読み出し応答を解析",
                FelicaLiteS.TryParseReadResponse(ReadFrame(idm, payload), idm, 2, out data, out sf1, out sf2)
                && SeqEqual(data, payload) && sf1 == 0 && sf2 == 0);
            Check("別 IDm の読み出し応答は拒否",
                !FelicaLiteS.TryParseReadResponse(ReadFrame(Idm(2), payload), idm, 2, out data, out sf1, out sf2));
            Check("読み出しのステータスフラグ異常は拒否し、値は返す",
                !FelicaLiteS.TryParseReadResponse(StatusFrame(0x07, idm, 0xFF, 0xA1), idm, 2, out data, out sf1, out sf2)
                && sf1 == 0xFF && sf2 == 0xA1);
            Check("ブロック数不一致は拒否",
                !FelicaLiteS.TryParseReadResponse(ReadFrame(idm, Payload(0x10, 16)), idm, 2, out data, out sf1, out sf2));
            Check("応答コード違いは拒否",
                !FelicaLiteS.TryParseReadResponse(Corrupt(ReadFrame(idm, payload), 1, 0x09), idm, 2, out data, out sf1, out sf2));
            Check("宣言長の不一致は拒否",
                !FelicaLiteS.TryParseReadResponse(Corrupt(ReadFrame(idm, payload), 0, 44), idm, 2, out data, out sf1, out sf2));
            bool allTruncatedRejected = true;
            byte[] full = ReadFrame(idm, payload);
            for (int n = 0; n < full.Length; n++)
            {
                byte[] d; byte a, b;
                if (FelicaLiteS.TryParseReadResponse(Take(full, n), idm, 2, out d, out a, out b))
                    allTruncatedRejected = false;
            }
            Check("読み出し応答のあらゆる途中切れを拒否", allTruncatedRejected);

            Check("正常な書き込み応答を解析",
                FelicaLiteS.TryParseWriteResponse(StatusFrame(0x09, idm, 0, 0), idm, out sf1, out sf2)
                && sf1 == 0 && sf2 == 0);
            Check("書き込みのステータスフラグ異常は拒否し、値は返す",
                !FelicaLiteS.TryParseWriteResponse(StatusFrame(0x09, idm, 0xFF, 0xA2), idm, out sf1, out sf2)
                && sf1 == 0xFF && sf2 == 0xA2);
            Check("別 IDm の書き込み応答は拒否",
                !FelicaLiteS.TryParseWriteResponse(StatusFrame(0x09, Idm(2), 0, 0), idm, out sf1, out sf2));
            Check("読み出し応答コードは書き込み応答として拒否",
                !FelicaLiteS.TryParseWriteResponse(StatusFrame(0x07, idm, 0, 0), idm, out sf1, out sf2));

            // ---- CardError（カードが拒否）と Malformed（応答が信頼できない）の区別 ----
            // 両者は呼び出し側の復旧判断が違うので、同じ失敗として潰してはならない。
            Check("Read 正常 -> Ok",
                FelicaLiteS.ParseReadResponse(ReadFrame(idm, payload), idm, 2, out data, out sf1, out sf2)
                    == FelicaResponseKind.Ok && sf1 == 0 && sf2 == 0);
            Check("Read の SF 非ゼロ -> CardError（SF は実値）",
                FelicaLiteS.ParseReadResponse(StatusFrame(0x07, idm, 0xFF, 0xA1), idm, 2, out data, out sf1, out sf2)
                    == FelicaResponseKind.CardError && sf1 == 0xFF && sf2 == 0xA1);
            Check("Read の別 IDm -> Malformed（SF は返さない）",
                FelicaLiteS.ParseReadResponse(ReadFrame(Idm(2), payload), idm, 2, out data, out sf1, out sf2)
                    == FelicaResponseKind.Malformed && sf1 == 0xFF && sf2 == 0xFF);
            Check("Read の応答コード違い -> Malformed",
                FelicaLiteS.ParseReadResponse(Corrupt(ReadFrame(idm, payload), 1, 0x09), idm, 2, out data, out sf1, out sf2)
                    == FelicaResponseKind.Malformed);
            Check("Read の SF は正常だがブロック数不一致 -> Malformed",
                FelicaLiteS.ParseReadResponse(ReadFrame(idm, Payload(0x10, 16)), idm, 2, out data, out sf1, out sf2)
                    == FelicaResponseKind.Malformed && sf1 == 0xFF && sf2 == 0xFF);
            Check("Read の途中切れ -> Malformed",
                FelicaLiteS.ParseReadResponse(Take(ReadFrame(idm, payload), 11), idm, 2, out data, out sf1, out sf2)
                    == FelicaResponseKind.Malformed);

            Check("Write 正常 -> Ok",
                FelicaLiteS.ParseWriteResponse(StatusFrame(0x09, idm, 0, 0), idm, out sf1, out sf2)
                    == FelicaResponseKind.Ok);
            Check("Write の SF 非ゼロ -> CardError（SF は実値）",
                FelicaLiteS.ParseWriteResponse(StatusFrame(0x09, idm, 0xFF, 0xA2), idm, out sf1, out sf2)
                    == FelicaResponseKind.CardError && sf1 == 0xFF && sf2 == 0xA2);
            Check("Write の長さ不足 -> Malformed（SF は返さない）",
                FelicaLiteS.ParseWriteResponse(Take(StatusFrame(0x09, idm, 0, 0), 11), idm, out sf1, out sf2)
                    == FelicaResponseKind.Malformed && sf1 == 0xFF && sf2 == 0xFF);
            Check("Write の応答コード違い -> Malformed",
                FelicaLiteS.ParseWriteResponse(StatusFrame(0x07, idm, 0, 0), idm, out sf1, out sf2)
                    == FelicaResponseKind.Malformed);
            Check("Write の別 IDm -> Malformed",
                FelicaLiteS.ParseWriteResponse(StatusFrame(0x09, Idm(2), 0, 0), idm, out sf1, out sf2)
                    == FelicaResponseKind.Malformed);
        }

        // ===================================================================
        //  6. Connect / Disconnect
        // ===================================================================

        private static void ConnectLifecycle()
        {
            FakeTransport fake = new FakeTransport(1);
            Pcsc pcsc = New(fake);

            Check("未接続では Read が NotConnected", pcsc.Read().Status == PcscStatus.NotConnected);
            Check("未接続では Write が NotConnected",
                pcsc.Write(Payload(0x20, 32)).Status == PcscStatus.NotConnected);
            Check("未接続の IsConnected は false", !pcsc.IsConnected);

            Check("Connect が成功する", pcsc.Connect("fake reader").Ok);
            Check("Connect 後の IsConnected は true", pcsc.IsConnected);
            Check("ReaderName を保持する", pcsc.ReaderName == "fake reader");
            Check("Connect 時点では RF を起動しない",
                fake.OnCalls == 0 && fake.StartCalls == 0);
            Check("二重 Connect は AlreadyConnected",
                pcsc.Connect("fake reader").Status == PcscStatus.AlreadyConnected);

            Check("Disconnect が成功する", pcsc.Disconnect().Ok);
            Check("Disconnect でトランスポートを破棄する", fake.Disposed);
            Check("Disconnect は冪等", pcsc.Disconnect().Ok);
            Check("Disconnect 後は NotConnected", pcsc.Read().Status == PcscStatus.NotConnected);

            FakeTransport broken = new FakeTransport(1) { FailOpen = true };
            PcscResult failed = New(broken).Connect("fake reader");
            Check("Open 失敗は ReaderError", failed.Status == PcscStatus.ReaderError);
            Check("Open 失敗時はトランスポートを破棄する", broken.Disposed);
            Check("Open 失敗の原因例外を保持する", failed.Error is PcscException);
        }

        // ===================================================================
        //  7. Read
        // ===================================================================

        private static void ReadTests()
        {
            byte[] stored = Payload(0x10, 32);

            FakeTransport one = new FakeTransport(1);
            one.Store(0, stored);
            PcscResult read = Connected(one).Read();
            Check("引数なし Read は先頭 16 バイト（1 ブロック）",
                read.Ok && read.Data.Length == 16 && SeqEqual(read.Data, Take(stored, 16)));
            Check("対象 IDm を返す", read.Idm == "0100000000000001");
            Check("指定ラウンド分の Polling を実行", one.Polls == 2);
            Check("RF とセッションの手順が正しい",
                one.StartCalls == 1 && one.SwitchCalls == 1 && one.OnCalls == 1
                && one.OffCalls == 2 && one.EndCalls == 1);
            Check("Read コマンドは 1 回だけ", one.Reads == 1);

            FakeTransport range = new FakeTransport(1);
            range.Store(0, Payload(0, 224));
            PcscResult ranged = range.Wrap().Read(3, 4);
            Check("任意ブロック範囲を読み出す",
                ranged.Ok && ranged.Data.Length == 64 && ranged.Data[0] == 48);

            // ---- 重ね置き ----
            FakeTransport two = new FakeTransport(1) { SecondCard = 2 };
            PcscResult multi = Connected(two).Read();
            Check("2 枚重なっているときはエラー（MultipleCards）",
                multi.Status == PcscStatus.MultipleCards);
            Check("重ね置き検出時は Read コマンドを送出しない", two.Reads == 0);
            Check("重ね置きの検出枚数をメッセージに含む", multi.Message.Contains("2 種類"));

            FakeTransport swapped = new FakeTransport(1);
            swapped.Script(Ok(1), Ok(2));
            Check("ラウンドをまたいで別 IDm を観測したらエラー",
                Connected(swapped).Read().Status == PcscStatus.MultipleCards);

            FakeTransport interleaved = new FakeTransport(1);
            interleaved.Script(Parse(Response(Card(1), Card(2))));
            Check("1 応答に 2 枚分含まれてもエラー",
                Connected(interleaved).Read().Status == PcscStatus.MultipleCards);

            // ---- 異常系はすべて fail-closed ----
            FakeTransport none = new FakeTransport(1) { NoCard = true };
            PcscResult noCard = Connected(none).Read();
            Check("無応答は NoCard で、コマンドを送出しない",
                noCard.Status == PcscStatus.NoCard && none.Reads == 0);

            FakeTransport collision = new FakeTransport(1);
            collision.Script(Parse(Collision()));
            Check("RF 衝突は Indeterminate（枚数を主張しない）",
                Connected(collision).Read().Status == PcscStatus.Indeterminate);

            FakeTransport malformed = new FakeTransport(1);
            TransceiveResult garbage = new TransceiveResult();
            garbage.Ok = true;
            garbage.Data = new byte[] { 1, 2, 3 };
            malformed.Script(garbage);
            Check("不正応答は 0 枚ではなく Indeterminate",
                Connected(malformed).Read().Status == PcscStatus.Indeterminate);

            FakeTransport contradictory = new FakeTransport(1);
            TransceiveResult both = new TransceiveResult();
            both.Ok = true; both.NoResponse = true; both.Data = Card(1);
            contradictory.Script(both);
            Check("矛盾したフラグは fail-closed",
                Connected(contradictory).Read().Status == PcscStatus.Indeterminate);

            FakeTransport vanished = new FakeTransport(1);
            vanished.Script(Ok(1), Empty());
            Check("検査中に応答が途切れたら Indeterminate",
                Connected(vanished).Read().Status == PcscStatus.Indeterminate);

            FakeTransport splitFrame = new FakeTransport(1);
            splitFrame.Script(Parse(Response(Take(Card(1), 10), Skip(Card(1), 10))));
            Check("途切れた DO を次の DO で補修しない",
                Connected(splitFrame).Read().Status == PcscStatus.Indeterminate);

            FakeTransport failEnd = new FakeTransport(1) { FailEnd = true };
            failEnd.Store(0, stored);
            Check("セッション終了の失敗は成功した読み出しも無効にする",
                Connected(failEnd).Read().Status == PcscStatus.Indeterminate);

            FakeTransport failSwitch = new FakeTransport(1) { FailSwitch = true };
            Check("プロトコル切替の失敗で検査を中止",
                Connected(failSwitch).Read().Status == PcscStatus.Indeterminate);

            FakeTransport rejectRead = new FakeTransport(1) { RejectRead = true };
            PcscResult rejected = Connected(rejectRead).Read();
            Check("カードの読み出し拒否は CardRejected",
                rejected.Status == PcscStatus.CardRejected
                && rejected.StatusFlag1 == 0xFF && rejected.StatusFlag2 == 0xA1);

            // ---- 不正な応答は CardRejected ではなく Indeterminate ----
            // カードが拒否したのか応答が壊れたのかで、呼び出し側の復旧手順が変わる。
            FakeTransport otherIdm = new FakeTransport(1) { ReadResponseIdm = 2 };
            PcscResult otherIdmResult = Connected(otherIdm).Read();
            Check("別 IDm の読み出し応答は Indeterminate",
                otherIdmResult.Status == PcscStatus.Indeterminate);
            Check("不正応答では SF を報告しない",
                otherIdmResult.StatusFlag1 == 0xFF && otherIdmResult.StatusFlag2 == 0xFF);
            Check("不正応答でも対象 IDm は報告する",
                otherIdmResult.Idm == "0100000000000001");

            Check("応答コード違いの読み出しは Indeterminate",
                Connected(new FakeTransport(1) { WrongReadResponseCode = true })
                    .Read().Status == PcscStatus.Indeterminate);

            // ---- 引数検証 ----
            Pcsc connected = Connected(new FakeTransport(1));
            Check("システムブロックの読み出しは InvalidArgument",
                connected.Read(0x88, 1).Status == PcscStatus.InvalidArgument);
            Check("ユーザ領域を超える読み出しは InvalidArgument",
                connected.Read(0, FelicaLiteS.UserBlockCount + 1).Status == PcscStatus.InvalidArgument);
            Check("ブロック数 0 は InvalidArgument",
                connected.Read(0, 0).Status == PcscStatus.InvalidArgument);
            Check("5 ブロック以上の Read は I/O 前に InvalidArgument",
                connected.Read(0, FelicaLiteS.MaxBlocksPerRead + 1).Status == PcscStatus.InvalidArgument);
            Check("4 ブロックの Read は許可", range.Wrap().Read(0, FelicaLiteS.MaxBlocksPerRead).Ok);

            // 巨大配列の確保に到達せず、I/O も行わないこと。
            // 確保に到達していると Error が OutOfMemoryException になる。
            FakeTransport overflow = new FakeTransport(1);
            PcscResult overflowResult = Connected(overflow).Read(2, int.MaxValue);
            Check("Read(2, int.MaxValue) は引数エラー（巨大確保に到達しない）",
                overflowResult.Status == PcscStatus.InvalidArgument
                && overflowResult.Error is ArgumentException);
            Check("桁あふれ入力では I/O を行わない", overflow.Polls == 0 && overflow.Reads == 0);
        }

        // ===================================================================
        //  8. Write
        // ===================================================================

        private static void WriteTests()
        {
            byte[] newData = Payload(0x20, 32);

            // 原則の 1 ブロック（16 バイト）書き込み。
            byte[] oneBlock = Payload(0x30, 16);
            FakeTransport single = new FakeTransport(1);
            single.Store(0, Payload(0x10, 32));
            PcscResult singleWrite = Connected(single).Write(oneBlock);
            Check("16 バイト書き込みが成功し、読み戻しで一致を確認",
                singleWrite.Ok && SeqEqual(single.Read(0, 16), oneBlock));
            Check("16 バイト書き込みは 1 コマンド・読み戻し 1 回",
                single.Writes == 1 && single.Reads == 1);
            Check("16 バイト書き込みは次のブロックを壊さない",
                SeqEqual(single.Read(16, 16), Payload(0x20, 16)));

            // 明示的に複数ブロックを渡した場合（原子的ではない）。
            FakeTransport written = new FakeTransport(1);
            written.Store(0, Payload(0x10, 32));
            PcscResult write = Connected(written).Write(newData);
            Check("32 バイト書き込みも成功し、読み戻しで一致を確認",
                write.Ok && SeqEqual(written.Read(0, 32), newData));
            // 対象カードは 1 コマンド 1 ブロックしか受け付けない（実機確認済み）。
            Check("1 ブロックにつき 1 コマンド送出", written.Writes == 2);
            Check("読み戻しは 1 回", written.Reads == 1);

            FakeTransport twoCards = new FakeTransport(1) { SecondCard = 2 };
            PcscResult blocked = Connected(twoCards).Write(newData);
            Check("2 枚重なっているときは書き込まない（MultipleCards）",
                blocked.Status == PcscStatus.MultipleCards);
            Check("重ね置き検出時は Write コマンドを送出しない", twoCards.Writes == 0);

            FakeTransport noCard = new FakeTransport(1) { NoCard = true };
            Check("無応答では書き込まない",
                Connected(noCard).Write(newData).Status == PcscStatus.NoCard && noCard.Writes == 0);

            FakeTransport rejected = new FakeTransport(1) { RejectWrite = true };
            PcscResult reject = Connected(rejected).Write(newData);
            Check("カードの書き込み拒否は CardRejected",
                reject.Status == PcscStatus.CardRejected && reject.StatusFlag2 == 0xA2);

            // 応答が壊れている場合は「書き込まれたか不明」なので CardRejected にしない。
            FakeTransport truncated = new FakeTransport(1) { TruncateWriteResponse = true };
            PcscResult truncatedResult = Connected(truncated).Write(oneBlock);
            Check("11 バイトに欠けた書き込み応答は Indeterminate",
                truncatedResult.Status == PcscStatus.Indeterminate);
            Check("不正な書き込み応答では SF を報告しない",
                truncatedResult.StatusFlag1 == 0xFF && truncatedResult.StatusFlag2 == 0xFF);
            Check("不正な書き込み応答は「書き込まれたか不明」と伝える",
                truncatedResult.Message.Contains("書き込まれたか不明"));

            FakeTransport dropped = new FakeTransport(1) { SilentlyDropWrite = true };
            dropped.Store(0, Payload(0x10, 32));
            Check("成功応答でも読み戻しが違えば Indeterminate",
                Connected(dropped).Write(newData).Status == PcscStatus.Indeterminate);

            // ライブラリは 1 ブロックずつ送る。まとめて送った場合の実機挙動を模擬側で確認する。
            FakeTransport multiBlockCmd = new FakeTransport(1);
            byte[] multiFrame = FelicaLiteS.BuildWrite(Idm(1), FelicaLiteS.BlockRange(0, 2), Payload(0, 32));
            TransceiveResult multiResult = multiBlockCmd.Transceive(multiFrame, 300000);
            byte msf1, msf2;
            bool multiRejected = multiResult.Ok
                && !FelicaLiteS.TryParseWriteResponse(multiResult.Frames[0], Idm(1), out msf1, out msf2)
                && msf1 == 0x02 && msf2 == 0xA8;
            Check("複数ブロックをまとめて送ると実機同様に拒否される（SF=02A8）", multiRejected);

            FakeTransport unverified = new FakeTransport(1);
            PcscOptions noVerify = FakeOptions();
            noVerify.VerifyAfterWrite = false;
            Pcsc pcsc = new Pcsc(noVerify, unverified.Factory());
            pcsc.Connect("fake reader");
            Check("VerifyAfterWrite=false なら読み戻さない",
                pcsc.Write(newData).Ok && unverified.Reads == 0);

            // ---- 引数検証 ----
            Pcsc connected = Connected(new FakeTransport(1));
            Check("null データは InvalidArgument",
                connected.Write(null).Status == PcscStatus.InvalidArgument);
            Check("空データは InvalidArgument",
                connected.Write(new byte[0]).Status == PcscStatus.InvalidArgument);
            Check("16 の倍数でないデータは InvalidArgument",
                connected.Write(Payload(0, 20)).Status == PcscStatus.InvalidArgument);
            Check("ユーザ領域を超える書き込みは InvalidArgument",
                connected.Write(0x0D, Payload(0, 32)).Status == PcscStatus.InvalidArgument);
            Check("システムブロックへの書き込みは InvalidArgument",
                connected.Write(0x88, Payload(0, 16)).Status == PcscStatus.InvalidArgument);

            Check("処理中に呼び出し側が配列を書き換えても送出内容は変わらない",
                CallerMutationIsIgnored());

            // ---- 読み戻しの分割（Read は 1 コマンド 4 ブロックまで）----
            // 分割しないと、書き込みは全件成功しているのに BuildRead が例外になり
            // Indeterminate で返る（カードは書き換わったまま）。
            FakeTransport five = new FakeTransport(1);
            byte[] fiveBlocks = Payload(0x40, 5 * FelicaLiteS.BlockSize);
            PcscResult write5 = Connected(five).Write(fiveBlocks);
            Check("5 ブロック書き込みが成功する", write5.Ok);
            Check("5 ブロックは 5 コマンド・読み戻し 2 回（4+1）",
                five.Writes == 5 && five.Reads == 2);
            Check("5 ブロックがカードに反映される",
                SeqEqual(five.Read(0, fiveBlocks.Length), fiveBlocks));

            FakeTransport whole = new FakeTransport(1);
            byte[] allBlocks = Payload(0x60, FelicaLiteS.UserBlockCount * FelicaLiteS.BlockSize);
            PcscResult write14 = Connected(whole).Write(allBlocks);
            Check("14 ブロック（ユーザ領域全体）書き込みが成功する", write14.Ok);
            Check("14 ブロックは 14 コマンド・読み戻し 4 回（4+4+4+2）",
                whole.Writes == 14 && whole.Reads == 4);
            Check("14 ブロックがカードに反映される",
                SeqEqual(whole.Read(0, allBlocks.Length), allBlocks));

            byte[] initial = Payload(0xF0, FelicaLiteS.UserBlockCount * FelicaLiteS.BlockSize);
            FakeTransport shifted = new FakeTransport(1);
            shifted.Store(0, initial);
            byte[] fiveAt3 = Payload(0x50, 5 * FelicaLiteS.BlockSize);
            PcscResult writeAt3 = Connected(shifted).Write(3, fiveAt3);
            Check("開始ブロックをずらした 5 ブロック書き込みが成功する",
                writeAt3.Ok && SeqEqual(shifted.Read(3 * FelicaLiteS.BlockSize, fiveAt3.Length), fiveAt3));
            Check("書き込み範囲の前後を壊さない",
                SeqEqual(shifted.Read(0, 3 * FelicaLiteS.BlockSize), Take(initial, 3 * FelicaLiteS.BlockSize))
                && SeqEqual(shifted.Read(8 * FelicaLiteS.BlockSize, 6 * FelicaLiteS.BlockSize),
                            Skip(initial, 8 * FelicaLiteS.BlockSize)));

            // 分割した読み戻しが 2 チャンク目まで比較されていることの確認。
            FakeTransport dropFirstChunk = new FakeTransport(1) { DropWriteForBlock = 1 };
            Check("1 チャンク目のブロックが書けていなければ Indeterminate",
                Connected(dropFirstChunk).Write(Payload(0x40, 5 * FelicaLiteS.BlockSize)).Status
                    == PcscStatus.Indeterminate);
            FakeTransport dropSecondChunk = new FakeTransport(1) { DropWriteForBlock = 4 };
            Check("2 チャンク目のブロックが書けていなければ Indeterminate",
                Connected(dropSecondChunk).Write(Payload(0x40, 5 * FelicaLiteS.BlockSize)).Status
                    == PcscStatus.Indeterminate);
        }

        /// <summary>
        /// Polling の最中（Write コマンド組み立て前）に呼び出し側の配列を書き換える。
        /// Write 完了後に書き換えるだけでは Clone を外しても成功してしまい、試験にならない。
        /// </summary>
        private static bool CallerMutationIsIgnored()
        {
            byte[] caller = Payload(0x20, FelicaLiteS.DefaultAccessLength);
            FakeTransport fake = new FakeTransport(1);
            fake.MutateOnPoll = caller;
            PcscResult result = Connected(fake).Write(caller);
            // caller[0] が実際に書き換わっていなければ、この試験は何も検証できていない。
            return result.Ok && caller[0] == 0xFF && fake.Read(0, 16)[0] == 0x20;
        }

        // ===================================================================
        //  9. オプションとログ
        // ===================================================================

        private static void OptionTests()
        {
            Check("ラウンド数 1 は拒否（重ね置きを検出できないため）",
                Throws(delegate { new Pcsc(Options(1, 300000, 0)); }));
            Check("ラウンド数 65 は拒否",
                Throws(delegate { new Pcsc(Options(65, 300000, 0)); }));
            Check("タイムアウト下限未満は拒否",
                Throws(delegate { new Pcsc(Options(8, 99999, 0)); }));
            Check("タイムアウト上限超過は拒否",
                Throws(delegate { new Pcsc(Options(8, 1000001, 0)); }));
            Check("RF 待ち時間の上限超過は拒否",
                Throws(delegate { new Pcsc(Options(8, 300000, 1001)); }));

            PcscOptions mutable = FakeOptions();
            FakeTransport fake = new FakeTransport(1);
            Pcsc pcsc = new Pcsc(mutable, fake.Factory());
            mutable.DuplicateCheckRounds = 64;   // 生成後の書き換えは効かない
            pcsc.Connect("fake reader");
            pcsc.Read();
            Check("オプションは生成時にスナップショットされる", fake.Polls == 2);

            PcscOptions throwing = FakeOptions();
            throwing.Log = delegate(string s) { throw new Exception("logging failure"); };
            FakeTransport logged = new FakeTransport(1) { NoCard = true };
            Pcsc logging = new Pcsc(throwing, logged.Factory());
            logging.Connect("fake reader");
            Check("ログの失敗は判定を迂回しない",
                logging.Read().Status == PcscStatus.NoCard);

            int lines = 0;
            PcscOptions counting = FakeOptions();
            counting.Log = delegate(string s) { lines++; };
            FakeTransport counted = new FakeTransport(1);
            Pcsc c = new Pcsc(counting, counted.Factory());
            c.Connect("fake reader");
            c.Read();
            c.Disconnect();
            Check("Connect / Read / Disconnect がそれぞれログされる", lines == 3);
        }

        // ===================================================================
        //  1. 追加: フレーム長の上限
        // ===================================================================

        private static void CommandLengthLimits()
        {
            // doLen = 11 + frameLen が 2 バイト（最大 FFFF）に収まる最大のフレーム長が 65524。
            byte[] maxApdu = Rcs300Transport.BuildTransceiveApdu(new byte[65524], 100000);
            Check("Transceive は最大長 65524 バイトのフレームを許可（データ長 FFFF）",
                maxApdu.Length == 18 + 65524 + 3
                && maxApdu[5] == 0xFF && maxApdu[6] == 0xFF
                && maxApdu[16] == 0xFF && maxApdu[17] == 0xF4);
            Check("65525 バイトのフレームは拒否（データ長が 2 バイトに収まらない）",
                ThrowsArgument(delegate { Rcs300Transport.BuildTransceiveApdu(new byte[65525], 100000); }));
        }

        // ===================================================================
        //  2. 追加: TLV の長さ表現と各 DO の細部
        // ===================================================================

        private static void ResponseParsingDetails()
        {
            byte[] card = Card(1);
            byte[] good = Response(card);
            byte[] sw = new byte[] { 0x90, 0x00 };
            byte[] c0Ok = new byte[] { 0xC0, 3, 0, 0x90, 0 };

            TransceiveResult long81 = Parse(Concat(Concat(new byte[] { 0x97, 0x81, 0x14 }, card), sw));
            Check("長さ 81 形式の DO97 を受理",
                long81.Ok && long81.Frames.Count == 1 && SeqEqual(long81.Frames[0], card));
            TransceiveResult long82 = Parse(Concat(Concat(new byte[] { 0x97, 0x82, 0x00, 0x14 }, card), sw));
            Check("長さ 82 形式の DO97 を受理",
                long82.Ok && long82.Frames.Count == 1 && SeqEqual(long82.Frames[0], card));
            Check("長さバイトが欠けた 81 形式は拒否",
                Parse(new byte[] { 0x97, 0x81, 0x90, 0 }).Unknown);
            Check("長さバイトが欠けた 82 形式は拒否",
                Parse(new byte[] { 0x97, 0x82, 0x00, 0x90, 0 }).Unknown);
            Check("81 形式でも宣言長が残りより長ければ拒否",
                Parse(Concat(Concat(new byte[] { 0x97, 0x81, 0x15 }, card), sw)).Unknown);
            Check("長さ 83 以上の長形式は拒否",
                Parse(Concat(Concat(new byte[] { 0x97, 0x83, 0x00, 0x00, 0x14 }, card), sw)).Unknown);

            TransceiveResult rf1 = Parse(Concat(Concat(Concat(c0Ok, new byte[] { 0x96, 1, 0, 0x97, 0x14 }), card), sw));
            Check("DO96 の 1 バイト形式（エラーなし）を受理", rf1.Ok);
            TransceiveResult rf1Error = Parse(Concat(Concat(Concat(c0Ok, new byte[] { 0x96, 1, 2, 0x97, 0x14 }), card), sw));
            Check("DO96 の 1 バイト形式でエラー値は不正（衝突にもしない）",
                rf1Error.Unknown && !rf1Error.Collision);
            // 上位ビットだけを立てると、検査が無くても RfErrors != 0 で Unknown になり区別できない。
            // 衝突ビットと同時に立て、「衝突」ではなく「不正」になることで検査の存在を確かめる。
            byte[] upperNibble = (byte[])good.Clone(); upperNibble[10] = 0x12;
            TransceiveResult reserved = Parse(upperNibble);
            Check("DO96 の上位 4 ビットが立っていたら衝突ビットがあっても不正",
                reserved.Unknown && !reserved.Collision);
            Check("DO96 の長さ 3 は拒否", Parse(new byte[] { 0x96, 3, 0, 0, 0, 0x90, 0 }).Unknown);

            byte[] c0Nonzero = (byte[])good.Clone(); c0Nonzero[2] = 1;
            Check("C0 が 9000 なのに先頭バイトが 0 以外なら拒否", Parse(c0Nonzero).Unknown);
            Check("C0 の長さ 3 以外は拒否", Parse(new byte[] { 0xC0, 2, 0x90, 0, 0x90, 0 }).Unknown);
            Check("長さ 0 の DO97 は拒否", Parse(new byte[] { 0x97, 0, 0x90, 0 }).Unknown);
            Check("DO92 の長さ 1 以外は拒否", Parse(new byte[] { 0x92, 2, 0, 0, 0x90, 0 }).Unknown);

            Check("管理コマンドは受信データ付きの応答を拒否",
                Throws(delegate { Rcs300Transport.ValidateManagementResponse(good, "test"); }));
            Check("管理コマンドは RF エラー付きの応答を拒否",
                Throws(delegate {
                    Rcs300Transport.ValidateManagementResponse(
                        new byte[] { 0xC0, 3, 0, 0x90, 0, 0x96, 2, 2, 0, 0x90, 0 }, "test"); }));
            Check("管理コマンドは端数ビット付きの応答を拒否",
                Throws(delegate {
                    Rcs300Transport.ValidateManagementResponse(
                        new byte[] { 0xC0, 3, 0, 0x90, 0, 0x92, 1, 1, 0x90, 0 }, "test"); }));
        }

        // ===================================================================
        //  6. 追加: 接続・切断の失敗経路と既定コンストラクタ
        // ===================================================================

        private static void ConnectFailurePaths()
        {
            Pcsc noTransport = new Pcsc(FakeOptions(), delegate { return (IPcscTransport)null; });
            PcscResult nullFactory = noTransport.Connect("fake reader");
            Check("トランスポートを生成できなければ ReaderError",
                nullFactory.Status == PcscStatus.ReaderError && nullFactory.Error is InvalidOperationException);
            Check("トランスポート生成失敗後は未接続のまま",
                !noTransport.IsConnected && noTransport.ReaderName == "");

            FakeTransport failDispose = new FakeTransport(1) { FailDispose = true };
            Pcsc pcsc = Connected(failDispose);
            PcscResult disconnect = pcsc.Disconnect();
            Check("Dispose が例外を投げたら Disconnect は ReaderError",
                disconnect.Status == PcscStatus.ReaderError && disconnect.Error is InvalidOperationException);
            Check("Disconnect の失敗後もハンドルは手放し、未接続になる",
                failDispose.Disposed && !pcsc.IsConnected && pcsc.ReaderName == "");
            Check("Disconnect の失敗後に再接続できる",
                pcsc.Connect("fake reader").Ok && pcsc.IsConnected);

            Pcsc defaults = new Pcsc();
            Check("new Pcsc() はリーダに触れずに生成でき、未接続",
                !defaults.IsConnected && defaults.ReaderName == ""
                && defaults.Read().Status == PcscStatus.NotConnected);
            Check("new Pcsc(null) は既定オプションで生成できる",
                !Throws(delegate { new Pcsc((PcscOptions)null); }));
            Check("未接続での Dispose は例外を投げない",
                !Throws(delegate { defaults.Dispose(); }));
        }

        // ===================================================================
        //  7. 追加: Read の応答形式の異常と、例外時の後始末
        // ===================================================================

        private static void ReadFailurePaths()
        {
            // ---- Read コマンドへの応答がフレーム 1 個の Ok でない（TrySingleFrame が false）----
            FakeTransport noResp = new FakeTransport(1) { ReadResponseOverride = Empty() };
            PcscResult noRespResult = Connected(noResp).Read();
            Check("Read コマンドへの無応答は Indeterminate（解析できない）",
                noRespResult.Status == PcscStatus.Indeterminate
                && noRespResult.Message.Contains("解析できない")
                && noRespResult.Idm == "0100000000000001");

            byte[] frame = ReadFrame(Idm(1), Payload(0x10, 16));
            FakeTransport twoFrames = new FakeTransport(1) { ReadResponseOverride = Parse(Response(frame, frame)) };
            Check("Read 応答のフレームが 2 個なら Indeterminate",
                Connected(twoFrames).Read().Status == PcscStatus.Indeterminate);

            TransceiveResult both = new TransceiveResult();
            both.Ok = true; both.NoResponse = true;
            both.Frames = new List<byte[]> { frame };
            FakeTransport contradictory = new FakeTransport(1) { ReadResponseOverride = both };
            Check("Read 応答のフラグが矛盾していたら Indeterminate",
                Connected(contradictory).Read().Status == PcscStatus.Indeterminate);

            // ---- 例外で中断したときの後始末 ----
            FakeTransport failSwitch = new FakeTransport(1) { FailSwitch = true };
            PcscResult switchResult = Connected(failSwitch).Read();
            Check("切替失敗時も RF OFF とセッション終了を試み、RF ON と Polling はしない",
                failSwitch.StartCalls == 1 && failSwitch.OffCalls == 2 && failSwitch.EndCalls == 1
                && failSwitch.OnCalls == 0 && failSwitch.Polls == 0);
            Check("例外で中断した結果は原因例外と操作名を保持する",
                switchResult.Error is NotSupportedException
                && switchResult.Message.StartsWith("read: ", StringComparison.Ordinal));

            FakeTransport failEnd = new FakeTransport(1) { FailEnd = true };
            Connected(failEnd).Read();
            Check("セッション終了の失敗時は RF OFF とセッション終了をもう一度試みる",
                failEnd.Reads == 1 && failEnd.OffCalls == 3 && failEnd.EndCalls == 2);
        }

        // ===================================================================
        //  8. 追加: 2 ブロック目以降の失敗、読み戻しの失敗、応答形式の異常
        // ===================================================================

        private static void WriteFailurePaths()
        {
            byte[] initial = Payload(0xA0, 32);
            byte[] newData = Payload(0x20, 32);

            // ---- 2 ブロック目でカードが拒否 ----
            FakeTransport rejectSecond = new FakeTransport(1) { RejectWriteForBlock = 1 };
            rejectSecond.Store(0, initial);
            PcscResult rejected = Connected(rejectSecond).Write(newData);
            Check("2 ブロック目の拒否は CardRejected（SF は実値）",
                rejected.Status == PcscStatus.CardRejected
                && rejected.StatusFlag1 == 0xFF && rejected.StatusFlag2 == 0xA2);
            Check("2 ブロック目の拒否は「先行ブロックは書き換わっている」と伝える",
                rejected.Message.Contains("先行ブロックは書き換わっている"));
            Check("2 ブロック目の拒否時、1 ブロック目は書き換わり 2 ブロック目は元のまま",
                SeqEqual(rejectSecond.Read(0, 16), Take(newData, 16))
                && SeqEqual(rejectSecond.Read(16, 16), Skip(initial, 16)));
            Check("書き込みの拒否後は読み戻しを行わない",
                rejectSecond.Writes == 2 && rejectSecond.Reads == 0);

            FakeTransport rejectFirst = new FakeTransport(1) { RejectWriteForBlock = 0 };
            PcscResult rejectedFirst = Connected(rejectFirst).Write(newData);
            Check("1 ブロック目の拒否では「先行ブロック」に触れず、後続ブロックを送らない",
                rejectedFirst.Status == PcscStatus.CardRejected
                && !rejectedFirst.Message.Contains("先行ブロック") && rejectFirst.Writes == 1);

            // ---- 2 ブロック目の応答が壊れている ----
            FakeTransport truncSecond = new FakeTransport(1) { TruncateWriteForBlock = 1 };
            PcscResult truncated = Connected(truncSecond).Write(newData);
            Check("2 ブロック目の応答不正は Indeterminate（SF は報告しない）",
                truncated.Status == PcscStatus.Indeterminate
                && truncated.StatusFlag1 == 0xFF && truncated.StatusFlag2 == 0xFF);
            Check("2 ブロック目の応答不正は「書き込まれたか不明」「先行ブロックは書き換わっている」と伝える",
                truncated.Message.Contains("書き込まれたか不明")
                && truncated.Message.Contains("先行ブロックは書き換わっている"));

            // ---- 読み戻しの失敗と不一致の区別 ----
            FakeTransport verifyRejected = new FakeTransport(1) { RejectRead = true };
            PcscResult verifyFail = Connected(verifyRejected).Write(Payload(0x30, 16));
            Check("読み戻しの Read が拒否されたら Indeterminate",
                verifyFail.Status == PcscStatus.Indeterminate
                && verifyRejected.Writes == 1 && verifyRejected.Reads == 1);
            Check("読み戻し失敗は「読み戻しに失敗」と伝え、読み戻しの SF を報告する",
                verifyFail.Message.Contains("読み戻しに失敗")
                && verifyFail.StatusFlag1 == 0xFF && verifyFail.StatusFlag2 == 0xA1);

            FakeTransport dropped = new FakeTransport(1) { SilentlyDropWrite = true };
            dropped.Store(0, initial);
            PcscResult mismatch = Connected(dropped).Write(newData);
            Check("読み戻し不一致は「一致しない」と伝え、読み戻し失敗と区別する",
                mismatch.Status == PcscStatus.Indeterminate
                && mismatch.Message.Contains("一致しない")
                && !mismatch.Message.Contains("読み戻しに失敗"));

            // ---- Write コマンドへの応答がフレーム 1 個の Ok でない（TrySingleFrame が false）----
            FakeTransport writeNoResp = new FakeTransport(1) { WriteResponseOverride = Empty() };
            PcscResult writeNoRespResult = Connected(writeNoResp).Write(newData);
            Check("Write コマンドへの無応答は Indeterminate（解析できない・書き込まれたか不明）",
                writeNoRespResult.Status == PcscStatus.Indeterminate
                && writeNoRespResult.Message.Contains("解析できない")
                && writeNoRespResult.Message.Contains("書き込まれたか不明"));
            Check("Write 応答を解析できなければ後続ブロックも読み戻しも送らない",
                writeNoResp.Writes == 1 && writeNoResp.Reads == 0);

            // 読み戻しを有効にしたままだと、フレーム数の検査が無くても読み戻し不一致で
            // Indeterminate になり、この経路を検証できない。読み戻しを切って確認する。
            byte[] okFrame = StatusFrame(0x09, Idm(1), 0, 0);
            FakeTransport writeTwoFrames = new FakeTransport(1) { WriteResponseOverride = Parse(Response(okFrame, okFrame)) };
            PcscOptions noVerify = FakeOptions();
            noVerify.VerifyAfterWrite = false;
            Pcsc twoFramePcsc = new Pcsc(noVerify, writeTwoFrames.Factory());
            twoFramePcsc.Connect("fake reader");
            Check("Write 応答のフレームが 2 個なら Indeterminate（読み戻しに頼らず判定）",
                twoFramePcsc.Write(Payload(0x30, 16)).Status == PcscStatus.Indeterminate);
        }

        // ===================================================================
        //  9. 追加: オプションの境界と既定値、RF 待ち、結果の書式
        // ===================================================================

        private static void OptionBoundariesAndResult()
        {
            PcscOptions defaults = new PcscOptions();
            Check("既定値はラウンド 8 / 300000us / RF 待ち 20ms / 読み戻し有効 / ログなし",
                defaults.DuplicateCheckRounds == 8 && defaults.TransceiveTimeoutUs == 300000
                && defaults.RfSettleDelayMs == 20 && defaults.VerifyAfterWrite && defaults.Log == null);

            Check("ラウンド数 2（下限）は許可", !Throws(delegate { new Pcsc(Options(2, 300000, 0)); }));
            Check("ラウンド数 64（上限）は許可", !Throws(delegate { new Pcsc(Options(64, 300000, 0)); }));
            Check("タイムアウト 100000（下限）は許可", !Throws(delegate { new Pcsc(Options(8, 100000, 0)); }));
            Check("タイムアウト 1000000（上限）は許可", !Throws(delegate { new Pcsc(Options(8, 1000000, 0)); }));
            Check("RF 待ち 0（下限）は許可", !Throws(delegate { new Pcsc(Options(8, 300000, 0)); }));
            Check("RF 待ち 1000（上限）は許可", !Throws(delegate { new Pcsc(Options(8, 300000, 1000)); }));
            Check("RF 待ち -1 は拒否",
                Throws<ArgumentOutOfRangeException>(delegate { new Pcsc(Options(8, 300000, -1)); }));
            Check("範囲外の例外は ArgumentOutOfRangeException",
                Throws<ArgumentOutOfRangeException>(delegate { new Pcsc(Options(1, 300000, 0)); }));

            FakeTransport defaultRounds = new FakeTransport(1);
            Pcsc withNull = new Pcsc(null, defaultRounds.Factory());
            withNull.Connect("fake reader");
            Check("options に null を渡すと既定のラウンド数 8 で Polling する",
                withNull.Read().Ok && defaultRounds.Polls == 8);

            // ---- RF 待ちと所要時間 ----
            FakeTransport settle = new FakeTransport(1);
            Pcsc settling = new Pcsc(Options(2, 300000, 50), settle.Factory());
            settling.Connect("fake reader");
            PcscResult settled = settling.Read();
            Check("RF 待ちを指定すると待ってから処理する（ElapsedMs >= 40）",
                settled.Ok && settled.ElapsedMs >= 40);
            Check("I/O 前の引数エラーの ElapsedMs は 0",
                Connected(new FakeTransport(1)).Read(0, 0).ElapsedMs == 0);

            // ---- PcscResult.ToString（ログの 1 行の書式）----
            FakeTransport shown = new FakeTransport(1);
            shown.Store(0, Payload(0x10, 16));
            PcscResult okRead = Connected(shown).Read();
            Check("ToString（Ok）はステータス・IDm・SF・データ長・メッセージ・所要時間の順",
                okRead.ToString() == "Ok idm=0100000000000001 SF=0000 len=16 ("
                    + okRead.Message + ") " + okRead.ElapsedMs + "ms");
            PcscResult notConnected = New(new FakeTransport(1)).Read();
            Check("ToString は IDm・SF（FFFF）・データが無ければ省略する",
                notConnected.ToString() == "NotConnected ("
                    + notConnected.Message + ") " + notConnected.ElapsedMs + "ms");
            PcscResult rejectedRead = Connected(new FakeTransport(1) { RejectRead = true }).Read();
            Check("ToString は SF の片方でも FF 以外なら表示する",
                rejectedRead.ToString() == "CardRejected idm=0100000000000001 SF=FFA1 ("
                    + rejectedRead.Message + ") " + rejectedRead.ElapsedMs + "ms");
            Check("失敗時の Data は null ではなく長さ 0、IDm 未確定なら空文字",
                notConnected.Data != null && notConnected.Data.Length == 0 && notConnected.Idm == "");
        }

        // ===================================================================
        //  10. 重ね置きの観測（DuplicateGuard / ScanObservation）
        //
        //  Pcsc を通さずに観測ループを直接呼び、打ち切り条件とフラグを確認する。
        // ===================================================================

        private static void ObservationTests()
        {
            const int rounds = 8;
            const int timeout = 300000;

            Check("Collect(null) は ArgumentNullException",
                Throws<ArgumentNullException>(delegate { DuplicateGuard.Collect(null, rounds, timeout); }));

            FakeTransport single = new FakeTransport(1);
            ScanObservation one = DuplicateGuard.Collect(single, rounds, timeout);
            Check("1 枚なら全ラウンド Polling し、同じ IDm は 1 種類として数える",
                single.Polls == rounds && one.Count == 1
                && !one.Unknown && !one.Collision && !one.NoResponse);
            Check("SingleIdm は観測した IDm を返す", SeqEqual(one.SingleIdm, Idm(1)));

            FakeTransport two = new FakeTransport(1) { SecondCard = 2 };
            ScanObservation stacked = DuplicateGuard.Collect(two, rounds, timeout);
            Check("1 応答で 2 種類を観測したら 1 ラウンドで打ち切る",
                two.Polls == 1 && stacked.Count == 2);

            FakeTransport swapped = new FakeTransport(1);
            swapped.Script(Ok(3), Ok(3), Ok(1), Ok(2));
            ScanObservation later = DuplicateGuard.Collect(swapped, rounds, timeout);
            Check("ラウンドをまたいで 2 種類になった時点で打ち切る",
                swapped.Polls == 3 && later.Count == 2);
            Check("IdList は観測順ではなく昇順",
                later.IdList.Count == 2
                && later.IdList[0] == "0100000000000001" && later.IdList[1] == "0100000000000003");

            FakeTransport collide = new FakeTransport(1);
            collide.Script(Ok(1), Parse(Collision()));
            ScanObservation collided = DuplicateGuard.Collect(collide, rounds, timeout);
            Check("RF 衝突を観測したらそのラウンドで打ち切る",
                collide.Polls == 2 && collided.Collision && !collided.Unknown && collided.Count == 1);

            FakeTransport garbage = new FakeTransport(1);
            garbage.Script(Ok(1), Parse(Response(new byte[] { 1, 2, 3 })));
            ScanObservation malformed = DuplicateGuard.Collect(garbage, rounds, timeout);
            Check("対象外の応答を観測したら Unknown で打ち切る（観測済みの IDm は残る）",
                garbage.Polls == 2 && malformed.Unknown && malformed.Count == 1);

            FakeTransport nullResult = new FakeTransport(1);
            nullResult.Script(new TransceiveResult[] { null });
            ScanObservation nulled = DuplicateGuard.Collect(nullResult, rounds, timeout);
            Check("Transceive が null を返したら Unknown で打ち切る",
                nullResult.Polls == 1 && nulled.Unknown && nulled.Count == 0);

            FakeTransport none = new FakeTransport(1) { NoCard = true };
            ScanObservation empty = DuplicateGuard.Collect(none, rounds, timeout);
            Check("無応答では打ち切らず全ラウンド続ける",
                none.Polls == rounds && empty.NoResponse && !empty.Unknown && empty.Count == 0);

            FakeTransport late = new FakeTransport(1);
            late.Script(Empty(), Ok(1));
            ScanObservation lateCard = DuplicateGuard.Collect(late, 4, timeout);
            Check("無応答の後に応答したカードも数える（NoResponse も残る）",
                late.Polls == 4 && lateCard.Count == 1 && lateCard.NoResponse);

            Check("SingleIdm は 0 種類なら InvalidOperationException",
                Throws<InvalidOperationException>(delegate { GC.KeepAlive(new ScanObservation().SingleIdm); }));
            Check("SingleIdm は 2 種類なら InvalidOperationException",
                Throws<InvalidOperationException>(delegate { GC.KeepAlive(stacked.SingleIdm); }));
        }

        // ===================================================================
        //  11. 並行呼び出し
        //
        //  Pcsc は内部で lock しているので、同じインスタンスを複数スレッドから呼んでも
        //  セッション（重ね置きチェック → 読み書き → 後始末）が重ならないはず。
        //  FakeTransport はセッション開始と Transceive でスレッドを譲り、重なりを起こしやすくする。
        // ===================================================================

        private static void ConcurrencyTests()
        {
            const int threads = 4;
            const int perThread = 25;
            const int total = threads * perThread;

            FakeTransport shared = new FakeTransport(1) { YieldInSession = true };
            Pcsc pcsc = Connected(shared);
            int readOk = 0;
            Thread[] readers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                readers[t] = new Thread(delegate()
                {
                    for (int i = 0; i < perThread; i++)
                        if (pcsc.Read().Ok) Interlocked.Increment(ref readOk);
                });
            }
            for (int t = 0; t < threads; t++) readers[t].Start();
            for (int t = 0; t < threads; t++) readers[t].Join();

            Check("複数スレッドから同時に Read してもセッションは重ならない", !shared.Overlap);
            Check("同時 Read はすべて Ok", readOk == total);
            Check("同時 Read でもコマンドの取りこぼしがない",
                shared.StartCalls == total && shared.EndCalls == total
                && shared.Reads == total && shared.Polls == 2 * total);

            // 書き込みと読み戻しの間に別スレッドの書き込みが割り込むと、読み戻しが一致しなくなる。
            FakeTransport mixed = new FakeTransport(1) { YieldInSession = true };
            Pcsc mixedPcsc = Connected(mixed);
            int mixedOk = 0;
            Thread[] workers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                byte fill = (byte)(0x10 * (t + 1));
                bool writer = t % 2 == 0;
                workers[t] = new Thread(delegate()
                {
                    byte[] data = Payload(fill, 2 * FelicaLiteS.BlockSize);
                    for (int i = 0; i < perThread; i++)
                    {
                        PcscResult r = writer ? mixedPcsc.Write(data) : mixedPcsc.Read(0, 2);
                        if (r.Ok) Interlocked.Increment(ref mixedOk);
                    }
                });
            }
            for (int t = 0; t < threads; t++) workers[t].Start();
            for (int t = 0; t < threads; t++) workers[t].Join();

            Check("Read と Write を同時に呼んでもセッションは重ならない", !mixed.Overlap);
            Check("Read と Write を同時に呼んでも、読み戻しを含めてすべて Ok", mixedOk == total);
        }

        // ===================================================================
        //  12. 16 進変換（Hex）
        // ===================================================================

        private static void HexTests()
        {
            byte[] sample = new byte[] { 0x0A, 0xFF, 0x00 };

            Check("ToString は大文字 2 桁を空白区切りで出力", Hex.ToString(sample) == "0A FF 00");
            Check("空配列の ToString は空文字", Hex.ToString(new byte[0]) == "");
            Check("ToString(null) は \"(null)\"",
                Hex.ToString(null) == "(null)" && Hex.ToString(null, 3, false) == "(null)");
            Check("長さ指定・区切りなしの ToString", Hex.ToString(sample, 2, false) == "0AFF");
            Check("範囲外の長さ指定は全体を出力",
                Hex.ToString(sample, 5, true) == "0A FF 00" && Hex.ToString(sample, -1, true) == "0A FF 00");

            Check("Parse は大文字・小文字と空白を受け付ける", SeqEqual(Hex.Parse("0A ff 00"), sample));
            Check("Parse はハイフン・コロン区切りを受け付ける", SeqEqual(Hex.Parse("0A-FF:00"), sample));
            Check("Parse は先頭の 0x / 0X を受け付ける",
                SeqEqual(Hex.Parse("0x0AFF00"), sample) && SeqEqual(Hex.Parse("0X0aff00"), sample));
            Check("Parse(\"\") は長さ 0", Hex.Parse("").Length == 0);
            Check("奇数桁は ArgumentException", ThrowsArgument(delegate { Hex.Parse("ABC"); }));
            Check("16 進以外の文字は ArgumentException", ThrowsArgument(delegate { Hex.Parse("0G"); }));
            Check("Parse(null) は ArgumentNullException",
                Throws<ArgumentNullException>(delegate { Hex.Parse(null); }));
            byte[] all = Payload(0, 256);
            Check("全 256 値で ToString と Parse が往復一致", SeqEqual(Hex.Parse(Hex.ToString(all)), all));

            Check("SequenceEqual は内容が同じなら true",
                Hex.SequenceEqual(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
            Check("SequenceEqual は長さや内容が違えば false",
                !Hex.SequenceEqual(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 })
                && !Hex.SequenceEqual(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
            Check("SequenceEqual は null を含めば null 同士でも false（fail-closed）",
                !Hex.SequenceEqual(null, null) && !Hex.SequenceEqual(null, new byte[0]));
        }

        // ===================================================================
        //  13. 実機トランスポートのリーダ不要な検査
        //
        //  Rcs300Transport のうち、winscard.dll を呼ぶ前に決まる振る舞いだけを確認する。
        //  最後の 1 件だけは OS の PC/SC 層を呼ぶが、存在しないリーダ名なので必ず失敗する。
        // ===================================================================

        private static void OfflineTransportTests()
        {
            Check("Open(null) は ArgumentException（winscard を呼ばない）",
                ThrowsArgument(delegate { new Rcs300Transport().Open(null); }));
            Check("Open(空白のみ) は ArgumentException",
                ThrowsArgument(delegate { new Rcs300Transport().Open("   "); }));
            Check("対象外プロトコルへの切替は NotSupportedException（送信前に拒否）",
                Throws<NotSupportedException>(delegate { new Rcs300Transport().SwitchProtocol(RfProtocol.TypeA); }));
            Check("未オープンでのセッション開始は InvalidOperationException",
                Throws<InvalidOperationException>(delegate { new Rcs300Transport().StartTransparentSession(); }));
            Check("未オープンでの RF ON / OFF は InvalidOperationException",
                Throws<InvalidOperationException>(delegate { new Rcs300Transport().TurnOnRf(); })
                && Throws<InvalidOperationException>(delegate { new Rcs300Transport().TurnOffRf(); }));
            Check("未オープンでの Transceive は InvalidOperationException",
                Throws<InvalidOperationException>(delegate {
                    new Rcs300Transport().Transceive(Felica.BuildPolling(), 300000); }));
            Check("未オープンの Dispose は例外を投げない",
                !Throws(delegate { new Rcs300Transport().Dispose(); }));
            Check("送信失敗の後の Dispose も例外を投げず、2 回呼んでもよい",
                !Throws(delegate {
                    Rcs300Transport t = new Rcs300Transport();
                    try { t.TurnOnRf(); } catch (InvalidOperationException) { }
                    try { t.StartTransparentSession(); } catch (InvalidOperationException) { }
                    t.Dispose();
                    t.Dispose();
                }));

            Check("Escape 制御コードの算出（SCARD_CTL_CODE）",
                Interop.ScardCtlCode(3500) == 0x003136B0
                && Interop.IOCTL_CCID_ESCAPE_FALLBACK == 0x003136B0
                && Interop.CM_IOCTL_GET_FEATURE_REQUEST == 0x00313520);
            PcscException error = new PcscException("SCardConnect", unchecked((int)0x8010000C));
            Check("PcscException は戻り値を ReturnCode と 16 進のメッセージで保持",
                error.ReturnCode == unchecked((int)0x8010000C)
                && error.Message == "SCardConnect (0x8010000C)");

            // ここだけ OS の PC/SC 層を呼ぶ。リーダの有無や winscard.dll の有無に関係なく失敗する。
            Pcsc real = new Pcsc(FakeOptions());
            PcscResult bogus = real.Connect("PcscIfTest: no such reader");
            Check("既定トランスポートで存在しないリーダ名への Connect は ReaderError",
                bogus.Status == PcscStatus.ReaderError && bogus.Error != null && !real.IsConnected);
        }

        // ===================================================================
        //  模擬トランスポート
        // ===================================================================

        /// <summary>
        /// Polling / Read / Write に応答する模擬トランスポート。
        /// 実機の癖（1 コマンド 1 ブロック書き込み）も再現する。
        /// </summary>
        private sealed class FakeTransport : IPcscTransport
        {
            private readonly byte _id;
            private readonly byte[] _memory = new byte[FelicaLiteS.UserBlockCount * FelicaLiteS.BlockSize];
            private readonly Queue<TransceiveResult> _script = new Queue<TransceiveResult>();
            private TransceiveResult _lastScripted;

            internal byte SecondCard;
            internal bool NoCard, RejectRead, RejectWrite, SilentlyDropWrite;
            internal bool FailOpen, FailEnd, FailSwitch;
            /// <summary>0 以外なら、その ID の IDm で読み出し応答を返す（別 IDm の応答を模擬）。</summary>
            internal byte ReadResponseIdm;
            /// <summary>読み出し応答の応答コードを書き込み用（0x09）にして返す。</summary>
            internal bool WrongReadResponseCode;
            /// <summary>書き込み応答を 11 バイトに切り詰めて返す。</summary>
            internal bool TruncateWriteResponse;
            /// <summary>このブロックの書き込みだけ、成功応答を返しつつ実際には書かない。-1 で無効。</summary>
            internal int DropWriteForBlock = -1;
            /// <summary>Polling のタイミングで、この配列の先頭を 0xFF に書き換える。</summary>
            internal byte[] MutateOnPoll;
            /// <summary>このブロックの書き込みだけ SF1=FF SF2=A2 で拒否する（メモリには書かない）。-1 で無効。</summary>
            internal int RejectWriteForBlock = -1;
            /// <summary>このブロックの書き込みだけ、メモリには書いたうえで応答を 11 バイトに切り詰める。-1 で無効。</summary>
            internal int TruncateWriteForBlock = -1;
            /// <summary>null 以外なら、Read コマンドにこの結果をそのまま返す。</summary>
            internal TransceiveResult ReadResponseOverride;
            /// <summary>null 以外なら、Write コマンドにこの結果をそのまま返す（メモリには書かない）。</summary>
            internal TransceiveResult WriteResponseOverride;
            /// <summary>Dispose で例外を投げる（Disposed は立てたうえで投げる）。</summary>
            internal bool FailDispose;
            /// <summary>セッション開始と Transceive でスレッドを譲り、並行呼び出しの重なりを起こしやすくする。</summary>
            internal bool YieldInSession;
            /// <summary>2 つ以上のセッションが同時に開いていたら true。</summary>
            internal bool Overlap;
            private readonly object _sessionLock = new object();
            private int _activeSessions;
            internal int Polls, Reads, Writes;
            internal int StartCalls, EndCalls, OffCalls, OnCalls, SwitchCalls;
            internal bool Disposed;

            internal FakeTransport(byte id) { _id = id; }

            internal void Store(int offset, byte[] data)
            {
                Buffer.BlockCopy(data, 0, _memory, offset, data.Length);
            }
            internal byte[] Read(int offset, int length)
            {
                byte[] data = new byte[length];
                Buffer.BlockCopy(_memory, offset, data, 0, length);
                return data;
            }
            /// <summary>Polling の応答列を固定する。使い切ったら最後の応答を返し続ける。</summary>
            internal void Script(params TransceiveResult[] responses)
            {
                for (int i = 0; i < responses.Length; i++) _script.Enqueue(responses[i]);
                _lastScripted = responses[responses.Length - 1];
            }
            internal Func<IPcscTransport> Factory()
            {
                FakeTransport self = this;
                return delegate { return (IPcscTransport)self; };
            }
            internal Pcsc Wrap()
            {
                Pcsc p = new Pcsc(FakeOptions(), Factory());
                p.Connect("fake reader");
                return p;
            }

            public void Open(string reader)
            {
                if (FailOpen) throw new PcscException("SCardConnect", unchecked((int)0x8010000C));
            }
            public void StartTransparentSession()
            {
                lock (_sessionLock)
                {
                    StartCalls++;
                    if (_activeSessions > 0) Overlap = true;
                    _activeSessions++;
                }
                if (YieldInSession) Thread.Sleep(0);
            }
            public void EndTransparentSession()
            {
                lock (_sessionLock)
                {
                    EndCalls++;
                    if (_activeSessions > 0) _activeSessions--;
                }
                if (FailEnd) throw new InvalidOperationException("end failure");
            }
            public void TurnOffRf() { OffCalls++; }
            public void TurnOnRf() { OnCalls++; }
            public void SwitchProtocol(RfProtocol protocol)
            {
                SwitchCalls++;
                if (protocol != RfProtocol.Felica || FailSwitch)
                    throw new NotSupportedException("switch failure");
            }
            public void Dispose()
            {
                Disposed = true;
                if (FailDispose) throw new InvalidOperationException("dispose failure");
            }

            public TransceiveResult Transceive(byte[] frame, int timeoutUs)
            {
                if (YieldInSession) Thread.Sleep(0);
                if (SeqEqual(frame, Felica.BuildPolling()))
                {
                    Polls++;
                    // 呼び出し側の配列を「処理中」に書き換える。Clone されていなければ
                    // 後続の Write コマンドに 0xFF が乗る。
                    if (MutateOnPoll != null) { MutateOnPoll[0] = 0xFF; MutateOnPoll = null; }
                    if (_script.Count > 0) return _script.Dequeue();
                    if (_lastScripted != null) return _lastScripted;
                    if (NoCard) return Empty();
                    return SecondCard != 0
                        ? Parse(Response(Card(_id), Card(SecondCard)))
                        : Parse(Response(Card(_id)));
                }

                byte[] idm = Idm(_id);
                if (frame[1] == FelicaLiteS.CommandRead)
                {
                    Reads++;
                    if (ReadResponseOverride != null) return ReadResponseOverride;
                    if (RejectRead) return Parse(Response(StatusFrame(0x07, idm, 0xFF, 0xA1)));
                    int blockCount = frame[13];
                    byte[] data = new byte[blockCount * FelicaLiteS.BlockSize];
                    for (int i = 0; i < blockCount; i++)
                        Buffer.BlockCopy(_memory, frame[15 + i * 2] * FelicaLiteS.BlockSize,
                            data, i * FelicaLiteS.BlockSize, FelicaLiteS.BlockSize);
                    byte[] readFrame = ReadFrame(ReadResponseIdm != 0 ? Idm(ReadResponseIdm) : idm, data);
                    if (WrongReadResponseCode) readFrame[1] = FelicaLiteS.ResponseWrite;
                    return Parse(Response(readFrame));
                }

                if (frame[1] == FelicaLiteS.CommandWrite)
                {
                    Writes++;
                    if (WriteResponseOverride != null) return WriteResponseOverride;
                    if (RejectWrite) return Parse(Response(StatusFrame(0x09, idm, 0xFF, 0xA2)));
                    int blockCount = frame[13];
                    // 実機の対象カードは 1 コマンド 1 ブロックしか受け付けず、
                    // 2 ブロック目で SF1=02 SF2=A8 を返す。その挙動を再現する。
                    if (blockCount != 1) return Parse(Response(StatusFrame(0x09, idm, 0x02, 0xA8)));
                    byte blockNumber = frame[15];
                    if (blockNumber == RejectWriteForBlock)
                        return Parse(Response(StatusFrame(0x09, idm, 0xFF, 0xA2)));
                    if (!SilentlyDropWrite && blockNumber != DropWriteForBlock)
                        Buffer.BlockCopy(frame, 16, _memory,
                            blockNumber * FelicaLiteS.BlockSize, FelicaLiteS.BlockSize);
                    byte[] writeFrame = StatusFrame(0x09, idm, 0, 0);
                    if (TruncateWriteResponse || blockNumber == TruncateWriteForBlock)
                        writeFrame = Take(writeFrame, 11);
                    return Parse(Response(writeFrame));
                }
                throw new InvalidOperationException("unexpected command");
            }
        }

        // ===================================================================
        //  ヘルパ
        // ===================================================================

        private static PcscOptions Options(int rounds, int timeoutUs, int settleMs)
        {
            PcscOptions o = new PcscOptions();
            o.DuplicateCheckRounds = rounds;
            o.TransceiveTimeoutUs = timeoutUs;
            o.RfSettleDelayMs = settleMs;
            return o;
        }
        private static PcscOptions FakeOptions() { return Options(2, 300000, 0); }

        private static Pcsc New(FakeTransport fake) { return new Pcsc(FakeOptions(), fake.Factory()); }

        private static Pcsc Connected(FakeTransport fake)
        {
            Pcsc pcsc = New(fake);
            pcsc.Connect("fake reader");
            return pcsc;
        }

        private static TransceiveResult Parse(byte[] bytes) { return TransceiveResult.Parse(bytes); }
        private static TransceiveResult Ok(byte id) { return Parse(Response(Card(id))); }
        private static TransceiveResult Empty() { return Parse(new byte[] { 0xC0, 3, 2, 0x64, 1, 0x90, 0 }); }
        private static byte[] Collision()
        {
            byte[] response = Response(Card(1));
            response[10] = 2;
            return response;
        }

        /// <summary>対象カードの Polling 応答（LEN=20, CMD=01, PMm[1]=F1, RD=88B4）。</summary>
        private static byte[] Card(byte id)
        {
            byte[] data = new byte[20];
            data[0] = 20; data[1] = 1; data[2] = 1; data[9] = id;
            data[11] = 0xF1; data[18] = 0x88; data[19] = 0xB4;
            return data;
        }

        /// <summary>C0 / 92 / 96 / 97 * n / SW の応答を組み立てる。</summary>
        private static byte[] Response(params byte[][] frames)
        {
            List<byte> bytes = new List<byte>(new byte[] { 0xC0, 3, 0, 0x90, 0, 0x92, 1, 0, 0x96, 2, 0, 0 });
            for (int i = 0; i < frames.Length; i++)
            {
                bytes.Add(0x97);
                bytes.Add((byte)frames[i].Length);
                bytes.AddRange(frames[i]);
            }
            bytes.AddRange(new byte[] { 0x90, 0 });
            return bytes.ToArray();
        }

        private static byte[] Idm(byte id)
        {
            byte[] idm = new byte[8];
            idm[0] = 1;
            idm[7] = id;
            return idm;
        }

        private static byte[] Payload(byte start, int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)(start + i);
            return data;
        }

        /// <summary>LEN, 07, IDm, SF1=0, SF2=0, ブロック数, ブロックデータ。</summary>
        private static byte[] ReadFrame(byte[] idm, byte[] data)
        {
            byte[] frame = new byte[13 + data.Length];
            frame[0] = (byte)frame.Length;
            frame[1] = 0x07;
            Buffer.BlockCopy(idm, 0, frame, 2, 8);
            frame[12] = (byte)(data.Length / FelicaLiteS.BlockSize);
            Buffer.BlockCopy(data, 0, frame, 13, data.Length);
            return frame;
        }

        /// <summary>LEN(12), 応答コード, IDm, SF1, SF2。</summary>
        private static byte[] StatusFrame(byte responseCode, byte[] idm, byte sf1, byte sf2)
        {
            byte[] frame = new byte[12];
            frame[0] = 12;
            frame[1] = responseCode;
            Buffer.BlockCopy(idm, 0, frame, 2, 8);
            frame[10] = sf1;
            frame[11] = sf2;
            return frame;
        }

        private static byte[] Corrupt(byte[] frame, int index, byte value)
        {
            byte[] copy = (byte[])frame.Clone();
            copy[index] = value;
            return copy;
        }

        private static byte[] Take(byte[] source, int count)
        {
            byte[] r = new byte[count];
            Buffer.BlockCopy(source, 0, r, 0, count);
            return r;
        }
        private static byte[] Skip(byte[] source, int count)
        {
            byte[] r = new byte[source.Length - count];
            Buffer.BlockCopy(source, count, r, 0, r.Length);
            return r;
        }
        private static byte[] Concat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
        private static bool SeqEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static bool Throws(Action action)
        {
            try { action(); return false; }
            catch { return true; }
        }

        /// <summary>
        /// 引数エラーとして弾かれたことまで確認する。
        /// OutOfMemoryException などを「拒否できた」と誤認しないため。
        /// </summary>
        private static bool ThrowsArgument(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }

        /// <summary>指定した型（またはその派生）の例外が発生したときだけ true。</summary>
        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); return false; }
            catch (T) { return true; }
            catch { return false; }
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title + " ==");
        }

        private static void Check(string name, bool pass)
        {
            Console.WriteLine("  [" + (pass ? "OK" : "NG") + "] " + name);
            if (pass) _passed++; else _failed++;
        }

        private static void Expect(string name, byte[] actual, byte[] expected)
        {
            bool ok = SeqEqual(actual, expected);
            Check(name, ok);
            if (!ok)
            {
                Console.WriteLine("        expected: " + Hex.ToString(expected));
                Console.WriteLine("        actual  : " + Hex.ToString(actual));
            }
        }
    }
}
