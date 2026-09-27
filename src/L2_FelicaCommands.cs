// ===========================================================================
//  L2_FelicaCommands.cs  --  層2 カードコマンド: コマンド生成と応答解析
//
//    Felica              Polling（重ね置きチェックの観測手段）
//    FelicaLiteS         ユーザデータ領域の Read / Write
//    FelicaResponseKind  読み書き応答の解析結果の分類
//
//  層構成は L1_Pcsc.cs 先頭のコメント、バイト列は docs/protocol.md §4 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
    // =======================================================================
    //  Felica  --  Polling（重ね置きチェックの観測手段）
    //
    //  対象カードを識別する定数（システムコード・IC 種別）と、Polling フレームの
    //  組み立て・応答からの IDm 抽出を持つ。
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
    //  FelicaLiteS  --  ユーザデータ領域の Read / Write
    //
    //  対象カードのユーザデータ領域のコマンド生成と応答解析。
    //
    //  Read/Write Without Encryption だけを扱う。MAC 付きアクセス・相互認証・
    //  鍵運用は実装していない。平文アクセスなので改ざん検知や機密保持には使えない。
    //
    //  対象はユーザブロック S_PAD0..S_PAD13 のみ。システムブロック（MC/CK/CKV/ID など）
    //  には意図的にアクセスできない。MC を誤って書くとカードが復旧不能になるため。
    // =======================================================================

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
    //  FelicaResponseKind  --  読み書き応答の解析結果の分類
    //
    //  FelicaLiteS.ParseReadResponse / ParseWriteResponse の戻り値。
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
}
