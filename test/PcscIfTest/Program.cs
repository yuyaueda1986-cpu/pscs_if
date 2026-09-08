// ===========================================================================
//  PcscIfTest -- PcscIf のテスト用処理（.NET 10 コンソールアプリ）
//
//  ../../src/PcscIf.cs をリンク参照してビルドする。ライブラリ本体は
//  .NET Framework 4.5 / C# 5.0 の範囲で書かれているため、そのまま .NET 10 でも通る。
//
//  使い方
//    PcscIfTest                          selftest と同じ（リーダ不要）
//    PcscIfTest selftest                 リーダ不要の自己テスト。CI 用。終了コード = 失敗数
//    PcscIfTest list                     接続中のリーダを列挙する
//    PcscIfTest read  [reader]           実機で Connect -> Read -> Disconnect
//    PcscIfTest read  [reader] <blk> <n> 実機で任意ブロックを読む
//    PcscIfTest write [reader] <hex> --yes   実機で Connect -> Write -> Disconnect（破壊的）
//    PcscIfTest loop  [reader] [count]   Read を繰り返し、重ね置き検出率を見る
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using PcscIf;

namespace PcscIfTest
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PcscIfTest: " + ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            string command = args.Length > 0 ? args[0] : "selftest";
            switch (command)
            {
                case "selftest": return SelfTest.Run();
                case "list": return ListReaders();
                case "read": return LiveRead(args);
                case "write": return LiveWrite(args);
                case "loop": return LiveLoop(args);
                default:
                    Console.WriteLine("usage: PcscIfTest <selftest|list|read|write|loop> [reader] ...");
                    return 2;
            }
        }

        // -------------------------------------------------------------------
        //  実機コマンド
        // -------------------------------------------------------------------

        private static int ListReaders()
        {
            List<string> readers = Pcsc.ListReaders();
            if (readers.Count == 0)
            {
                Console.WriteLine("リーダが見つかりません。");
                return 1;
            }
            for (int i = 0; i < readers.Count; i++)
                Console.WriteLine("[" + i + "] " + readers[i]);
            return 0;
        }

        private static PcscOptions LiveOptions()
        {
            PcscOptions o = new PcscOptions();
            o.Log = Console.WriteLine;
            return o;
        }

        private static int LiveRead(string[] args)
        {
            string reader = args.Length > 1 ? args[1] : null;
            byte startBlock = args.Length > 2 ? byte.Parse(args[2]) : FelicaLiteS.FirstUserBlock;
            int blockCount = args.Length > 3 ? int.Parse(args[3]) : FelicaLiteS.DefaultAccessBlocks;

            using (Pcsc pcsc = new Pcsc(LiveOptions()))
            {
                PcscResult connect = pcsc.Connect(reader);
                Console.WriteLine("Connect  => " + connect);
                if (!connect.Ok) return 1;

                PcscResult read = pcsc.Read(startBlock, blockCount);
                Console.WriteLine("Read     => " + read);
                if (read.Ok) Console.WriteLine("data     : " + Hex.ToString(read.Data));

                Console.WriteLine("Disconnect => " + pcsc.Disconnect());
                return read.Ok ? 0 : 1;
            }
        }

        private static int LiveWrite(string[] args)
        {
            // 破壊的操作なので --yes を必須にし、事故で書き込まないようにする。
            if (args.Length < 3)
                throw new ArgumentException("usage: write \"<reader>\" <hex> --yes [startBlock]");
            bool confirmed = false;
            for (int i = 0; i < args.Length; i++) if (args[i] == "--yes") confirmed = true;
            if (!confirmed)
                throw new ArgumentException("write はユーザ領域を上書きします。--yes を付けてください。");

            string reader = args[1];
            byte[] payload = Hex.Parse(args[2]);
            byte startBlock = FelicaLiteS.FirstUserBlock;
            if (args.Length > 4 && args[4] != "--yes") startBlock = byte.Parse(args[4]);

            using (Pcsc pcsc = new Pcsc(LiveOptions()))
            {
                PcscResult connect = pcsc.Connect(reader);
                Console.WriteLine("Connect  => " + connect);
                if (!connect.Ok) return 1;

                PcscResult write = pcsc.Write(startBlock, payload);
                Console.WriteLine("Write    => " + write);

                Console.WriteLine("Disconnect => " + pcsc.Disconnect());
                return write.Ok ? 0 : 1;
            }
        }

        private static int LiveLoop(string[] args)
        {
            string reader = args.Length > 1 ? args[1] : null;
            int count = args.Length > 2 ? int.Parse(args[2]) : 20;

            using (Pcsc pcsc = new Pcsc(LiveOptions()))
            {
                PcscResult connect = pcsc.Connect(reader);
                Console.WriteLine("Connect  => " + connect);
                if (!connect.Ok) return 1;

                Dictionary<PcscStatus, int> tally = new Dictionary<PcscStatus, int>();
                List<long> elapsed = new List<long>();
                for (int i = 1; i <= count; i++)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    PcscResult r = pcsc.Read();
                    elapsed.Add(sw.ElapsedMilliseconds);
                    if (!tally.ContainsKey(r.Status)) tally[r.Status] = 0;
                    tally[r.Status]++;
                    Console.WriteLine(i.ToString("D4") + " " + r);
                }
                Console.WriteLine("---- 集計 ----");
                foreach (KeyValuePair<PcscStatus, int> pair in tally)
                    Console.WriteLine("  " + pair.Key + " = " + pair.Value);
                elapsed.Sort();
                Console.WriteLine("  median=" + elapsed[elapsed.Count / 2]
                    + "ms max=" + elapsed[elapsed.Count - 1] + "ms");
                Console.WriteLine("Disconnect => " + pcsc.Disconnect());
                return 0;
            }
        }
    }
}
