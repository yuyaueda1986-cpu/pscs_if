// ===========================================================================
//  Layer3_PcscResponse.cs  --  層3 リーダ応答 TLV: 応答バイト列の解析
//
//  PC/SC Part 3 supplement v2.02 の応答 TLV 解析。
//  C0 = 汎用ステータス、92 = 受信ビット数、96 = RF エラー、97 = 受信データ。
//  未知タグ・途中で切れた TLV は決して無視せず、すべて失敗にする（fail-closed）。
//
//  層構成は Layer1_Pcsc.cs 先頭のコメント、バイト列は protocol.md §3 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
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
}
