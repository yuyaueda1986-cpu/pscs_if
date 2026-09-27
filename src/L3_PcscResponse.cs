// ===========================================================================
//  L3_PcscResponse.cs  --  層3 リーダ応答 TLV: 応答の解析と通信結果への変換
//
//    PcscResponse      応答バイト列の TLV 解析
//    TransceiveResult  Transceive 1 回分の観測結果
//
//  層構成は L1_Pcsc.cs 先頭のコメント、バイト列は docs/protocol.md §3 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
    // =======================================================================
    //  PcscResponse  --  応答バイト列の解析
    //
    //  PC/SC Part 3 supplement v2.02 の応答 TLV 解析。
    //  C0 = 汎用ステータス、92 = 受信ビット数、96 = RF エラー、97 = 受信データ。
    //  未知タグ・途中で切れた TLV は決して無視せず、すべて失敗にする（fail-closed）。
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

    // =======================================================================
    //  TransceiveResult  --  Transceive 1 回分の観測結果
    //
    //  PcscResponse の解析結果を Ok / NoResponse / Collision / Unknown の 4 フラグに落とす。
    //  IPcscTransport.Transceive の戻り値として層4 から上位層へ渡る。
    // =======================================================================

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
}
