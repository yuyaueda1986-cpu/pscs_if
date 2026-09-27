// ===========================================================================
//  L1_DuplicateGuard.cs  --  層1 重ね置きチェック: Polling 観測ループと観測結果
//
//  pcsc_dup_guard の CardDuplicateGuard 相当。
//    DuplicateGuard   Polling 観測ループ
//    ScanObservation  1 回のスキャンの観測結果
//
//  ここでは観測だけを行い、判定は Pcsc.Gate（L1_Pcsc.cs）で行う。
//  Polling フレームは層2 の Felica（L2_FelicaCommands.cs）で組み立て、
//  層4 の IPcscTransport（L4_PcscTransport.cs）で送る。
//  仕組みの詳細は docs/DuplicateGuard.md。層構成は L1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
    // =======================================================================
    //  DuplicateGuard  --  Polling 観測ループ
    // =======================================================================

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
    //  ScanObservation  --  1 回のスキャンの観測結果
    //
    //  1 回のスキャンで観測できた事実（IDm の集合と異常フラグ）だけを保持する。
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
}
