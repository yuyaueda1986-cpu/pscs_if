// ===========================================================================
//  Layer3_TransceiveResult.cs  --  層3 リーダ応答 TLV: Transceive 1 回分の観測結果
//
//  PcscResponse の解析結果を Ok / NoResponse / Collision / Unknown の 4 フラグに落とす。
//  IPcscTransport.Transceive の戻り値として層4 から上位層へ渡る。
//  層構成は Layer1_Pcsc.cs 先頭のコメント、バイト列は protocol.md §3 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
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
