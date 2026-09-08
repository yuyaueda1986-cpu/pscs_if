// ===========================================================================
//  PcscIf 使い方サンプル（C の main() 相当）
//
//  リーダ検索 → open → read → write → close をひととおり行う最小のサンプルです。
//
//  PC に接続されたカードリーダは 1 台という前提で、検索して最初に見つかった
//  1 台を使います。
//
//  読み書きは原則 1 ブロック = 先頭 16 バイト（S_PAD0）です。
//  Read / Write の中で重ね置きチェックが自動的に走ります。カードが 2 枚
//  重なっていると PcscStatus.MultipleCards が返り、コマンドは送出されません。
//
//  実行:
//      Example            リーダ検索 → open → read → close
//      Example --write    上に加えて 16 バイトの write も行う（カードを上書きします）
// ===========================================================================

using System;
using System.Collections.Generic;
using PcscIf;

namespace PcscIfExample
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool doWrite = args.Length > 0 && args[0] == "--write";

            // ---- 1. リーダを検索する（1 台前提。最初に見つかったものを使う）----
            List<string> readers = Pcsc.ListReaders();
            if (readers.Count == 0)
            {
                Console.Error.WriteLine("リーダが見つかりません。");
                return 1;
            }
            string reader = readers[0];
            Console.WriteLine("リーダ : " + reader);

            Pcsc pcsc = new Pcsc();
            try
            {
                // ---- 2. open ----
                PcscResult open = pcsc.Connect(reader);
                if (!open.Ok)
                {
                    Console.Error.WriteLine("接続失敗 : " + open.Message);
                    return 1;
                }

                // ---- 3. read（先頭 16 バイト）----
                PcscResult read = pcsc.Read();
                if (!read.Ok)
                {
                    // MultipleCards ならカードが 2 枚重なっている。
                    Console.Error.WriteLine("読み出し失敗 : " + read.Status + " - " + read.Message);
                    return 1;
                }
                Console.WriteLine("IDm    : " + read.Idm);
                Console.WriteLine("読み出し: " + Hex.ToString(read.Data));

                // ---- 4. write（先頭 16 バイト）----
                if (doWrite)
                {
                    byte[] data = new byte[FelicaLiteS.DefaultAccessLength];
                    for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

                    PcscResult write = pcsc.Write(data);
                    if (!write.Ok)
                    {
                        Console.Error.WriteLine("書き込み失敗 : " + write.Status + " - " + write.Message);
                        return 1;
                    }
                    Console.WriteLine("書き込み: " + Hex.ToString(data));
                }
            }
            finally
            {
                // ---- 5. close ----
                pcsc.Disconnect();
            }
            return 0;
        }
    }
}
