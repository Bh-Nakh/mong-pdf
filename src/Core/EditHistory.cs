using System.Collections.Generic;

namespace PdfCore
{
    // 편집 직전 문서를 통째로 저장한 스냅숏으로 실행 취소/다시 실행을 처리한다.
    // 각 상태에 버전 번호를 붙여, 저장 시점과 비교해 "수정됨" 여부를 판단한다.
    public sealed class EditHistory
    {
        private sealed class Snapshot
        {
            public int Version;
            public byte[] Data;
        }

        private readonly int maxSteps;
        private readonly long maxBytes;
        private readonly LinkedList<Snapshot> undo = new LinkedList<Snapshot>();
        private readonly Stack<Snapshot> redo = new Stack<Snapshot>();
        private long undoBytes;
        private int versionCounter;

        public EditHistory(int maxSteps, long maxBytes)
        {
            this.maxSteps = maxSteps;
            this.maxBytes = maxBytes;
        }

        public int CurrentVersion { get; private set; }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public void Record(byte[] before)
        {
            undo.AddLast(new Snapshot { Version = CurrentVersion, Data = before });
            undoBytes += before.Length;
            redo.Clear();
            CurrentVersion = ++versionCounter;
            while (undo.Count > 1 && (undo.Count > maxSteps || undoBytes > maxBytes))
            {
                undoBytes -= undo.First.Value.Data.Length;
                undo.RemoveFirst();
            }
        }

        public byte[] Undo(byte[] current)
        {
            Snapshot s = undo.Last.Value;
            undo.RemoveLast();
            undoBytes -= s.Data.Length;
            redo.Push(new Snapshot { Version = CurrentVersion, Data = current });
            CurrentVersion = s.Version;
            return s.Data;
        }

        public byte[] Redo(byte[] current)
        {
            Snapshot s = redo.Pop();
            undo.AddLast(new Snapshot { Version = CurrentVersion, Data = current });
            undoBytes += current.Length;
            CurrentVersion = s.Version;
            return s.Data;
        }

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
            undoBytes = 0;
            CurrentVersion = ++versionCounter;
        }
    }
}
