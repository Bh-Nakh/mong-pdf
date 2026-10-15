using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace PdfViewer
{
    // 몽PDF를 여러 번 실행해도(예: 탐색기에서 PDF 여러 개를 한꺼번에 열기) 창은 하나만 두고,
    // 나중에 실행된 쪽은 파일 경로만 먼저 떠 있는 창에 넘기고 끝낸다. 넘겨받은 파일은 탭으로 열린다.
    internal static class SingleInstance
    {
        private static Mutex mutex;
        private static readonly object sync = new object();
        private static readonly List<string[]> backlog = new List<string[]>();
        private static Action<string[]> handler;

        private static string Suffix
        {
            get { return Environment.UserName; }
        }

        private static string PipeName
        {
            get { return "MongPDF-Pipe-" + Suffix; }
        }

        // 내가 첫 번째 실행이면 true. 이미 다른 창이 떠 있으면 false.
        public static bool TryBecomePrimary()
        {
            bool created;
            mutex = new Mutex(true, @"Local\MongPDF-Single-" + Suffix, out created);
            if (!created) return false;
            var thread = new Thread(ServerLoop) { IsBackground = true, Name = "MongPDF-Pipe" };
            thread.Start();
            return true;
        }

        // 떠 있는 창에 파일 경로를 넘긴다. 넘겼으면 true.
        public static bool Forward(IList<string> files)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(8000);
                    using (var w = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        foreach (string f in files) w.WriteLine(f);
                        w.Flush();
                    }
                }
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // 창이 준비되면 호출한다. 그 전에 받아 둔 파일도 이때 한꺼번에 넘겨 준다. handler는 아무 스레드에서나 불린다.
        public static void SetHandler(Action<string[]> h)
        {
            List<string[]> pending;
            lock (sync)
            {
                handler = h;
                pending = new List<string[]>(backlog);
                backlog.Clear();
            }
            foreach (string[] files in pending) h(files);
        }

        private static void Deliver(string[] files)
        {
            Action<string[]> h;
            lock (sync)
            {
                h = handler;
                if (h == null) backlog.Add(files);
            }
            if (h != null) h(files);
        }

        private static void ServerLoop()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 8, PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        server.WaitForConnection();
                        var files = new List<string>();
                        using (var r = new StreamReader(server, new UTF8Encoding(false)))
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                                if (line.Length > 0) files.Add(line);
                        }
                        Deliver(files.ToArray());
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
    }
}
