// ===========================================================================
//  Layer1_DuplicateGuard.cs  --  層1 重ね置きチェック: Polling 観測ループ
//
//  pcsc_dup_guard の CardDuplicateGuard 相当。
//  Polling フレームは層2 の Felica で組み立て、層4 の IPcscTransport で送る。
//  仕組みの詳細は DuplicateGuard.md。層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
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
}
