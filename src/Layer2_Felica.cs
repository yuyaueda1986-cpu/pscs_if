// ===========================================================================
//  Layer2_Felica.cs  --  層2 カードコマンド: Polling（重ね置きチェックの観測手段）
//
//  対象カードを識別する定数（システムコード・IC 種別）と、Polling フレームの
//  組み立て・応答からの IDm 抽出を持つ。
//  層構成は Layer1_Pcsc.cs 先頭のコメント、バイト列は protocol.md §4 を参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
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
}
