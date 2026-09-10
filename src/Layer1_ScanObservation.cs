// ===========================================================================
//  Layer1_ScanObservation.cs  --  層1 重ね置きチェック: 1 回のスキャンの観測結果
//
//  1 回のスキャンで観測できた事実（IDm の集合と異常フラグ）だけを保持する。
//  判定は Pcsc.Gate（Layer1_Pcsc.cs）で行う。仕組みの詳細は DuplicateGuard.md。
//  層構成は Layer1_Pcsc.cs 先頭のコメントを参照。
// ===========================================================================

using System;
using System.Collections.Generic;

namespace PcscIf
{
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
