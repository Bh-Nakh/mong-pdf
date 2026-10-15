using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace PdfViewer
{
    // 렌더링을 백그라운드 스레드 하나에서 순서대로 처리한다. 최근 요청부터 처리하고, 본문 페이지(high)를 썸네일(low)보다 먼저 처리한다.
    internal sealed class RenderWorker : IDisposable
    {
        private sealed class Job
        {
            public string Key;
            public Func<bool> StillWanted;
            public Func<object> Work;
            public Action<object> Done;
        }

        private readonly Control owner;
        private readonly object gate = new object();
        private readonly List<Job> high = new List<Job>();
        private readonly List<Job> low = new List<Job>();
        private readonly HashSet<string> queued = new HashSet<string>();
        private readonly HashSet<string> failed = new HashSet<string>();
        private readonly Thread thread;
        private bool stopping;
        private bool busy;

        public RenderWorker(Control owner)
        {
            this.owner = owner;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "PdfRender";
            thread.Start();
        }

        public bool IsIdle
        {
            get
            {
                lock (gate) return !busy && high.Count == 0 && low.Count == 0;
            }
        }

        public void Enqueue(string key, bool highPriority, Func<bool> stillWanted, Func<object> work, Action<object> done)
        {
            lock (gate)
            {
                if (stopping || queued.Contains(key) || failed.Contains(key)) return;
                queued.Add(key);
                var job = new Job { Key = key, StillWanted = stillWanted, Work = work, Done = done };
                (highPriority ? high : low).Add(job);
                Monitor.Pulse(gate);
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                high.Clear();
                low.Clear();
                queued.Clear();
                failed.Clear();
            }
        }

        private void Run()
        {
            while (true)
            {
                Job job;
                lock (gate)
                {
                    while (!stopping && high.Count == 0 && low.Count == 0) Monitor.Wait(gate);
                    if (stopping) return;
                    List<Job> list = high.Count > 0 ? high : low;
                    job = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                    busy = true;
                }

                object result = null;
                bool produced = false;
                try
                {
                    if (job.StillWanted())
                    {
                        result = job.Work();
                        produced = true;
                    }
                }
                catch (Exception)
                {
                    lock (gate) failed.Add(job.Key);
                }

                if (produced) Post(job, result);
                lock (gate)
                {
                    queued.Remove(job.Key);
                    busy = false;
                }
            }
        }

        private void Post(Job job, object result)
        {
            try
            {
                owner.BeginInvoke(new Action(delegate
                {
                    if (owner.IsDisposed) DisposeResult(result);
                    else job.Done(result);
                }));
            }
            catch (InvalidOperationException)
            {
                DisposeResult(result);
            }
        }

        private static void DisposeResult(object result)
        {
            var d = result as IDisposable;
            if (d != null) d.Dispose();
        }

        public void Dispose()
        {
            lock (gate)
            {
                stopping = true;
                high.Clear();
                low.Clear();
                Monitor.PulseAll(gate);
            }
            thread.Join(3000);
        }
    }
}
