using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DiskLens.Core.Models;

namespace DiskLens.Core.Services;

/// <summary>
/// Scans a drive (or any folder) into a <see cref="FolderNode"/> tree using several worker threads.
/// Junctions and symbolic links are not followed, so nothing is counted twice. OneDrive files that
/// only live in the cloud count as 0 bytes, because they take no space on this PC.
/// </summary>
public sealed class Scanner
{
    public const int DefaultTopFileCount = 500;
    private const int MaxUnreadableSamples = 200;

    private const FileAttributes CloudOnlyAttributes =
        FileAttributes.Offline | (FileAttributes)0x00400000 /* RECALL_ON_DATA_ACCESS */ | (FileAttributes)0x00040000 /* RECALL_ON_OPEN */;

    private long _files;
    private long _bytes;
    private long _folders;
    private int _unreadable;
    private volatile string _current = "";

    public int TopFileCount { get; init; } = DefaultTopFileCount;

    /// <summary>Folders are listed by this many threads at once (more didn't help in measurements).</summary>
    public int WorkerCount { get; init; } = Math.Clamp(Environment.ProcessorCount, 8, 16);

    /// <summary>Directory-listing buffer per request.</summary>
    public int BufferSize { get; init; } = 32 * 1024;

    public ScanProgress Progress =>
        new(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), Interlocked.Read(ref _folders), Volatile.Read(ref _unreadable), _current);

    public ScanResult Scan(string rootPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        rootPath = Path.GetFullPath(rootPath);
        if (Path.GetPathRoot(rootPath) != rootPath) rootPath = rootPath.TrimEnd('\\');

        var root = new FolderNode(rootPath, null);
        var result = new ScanResult(rootPath, root);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,          // include hidden and system files (pagefile.sys, ...)
            ReturnSpecialDirectories = false,
            BufferSize = BufferSize,
        };

        var work = new ConcurrentStack<(FolderNode Node, string Path)>();
        work.Push((root, rootPath));
        long pending = 1;
        var unreadableSamples = new ConcurrentQueue<string>();

        int workerCount = WorkerCount;
        var tops = new TopFiles[workerCount];
        var workers = new Task[workerCount];

        for (int w = 0; w < workerCount; w++)
        {
            var top = tops[w] = new TopFiles(TopFileCount);
            workers[w] = Task.Factory.StartNew(() =>
            {
                var spin = new SpinWait();
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!work.TryPop(out var item))
                    {
                        if (Interlocked.Read(ref pending) == 0) return;
                        spin.SpinOnce(sleep1Threshold: 20);
                        continue;
                    }

                    spin.Reset();
                    ProcessFolder(item.Node, item.Path, options, top, item.Node.IsRoot ? result.RootFiles : null,
                        work, ref pending, unreadableSamples);

                    if (Interlocked.Decrement(ref pending) == 0) return;
                }
            }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        try
        {
            Task.WaitAll(workers);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
        }

        cancellationToken.ThrowIfCancellationRequested();

        ComputeTotals(root);

        result.LargestFiles.AddRange(TopFiles.Merge(tops, TopFileCount));
        result.FileCount = root.FileCount;
        result.FolderCount = Interlocked.Read(ref _folders);
        result.UnreadableCount = Volatile.Read(ref _unreadable);
        result.UnreadableSamples.AddRange(unreadableSamples.Take(MaxUnreadableSamples));
        result.Duration = stopwatch.Elapsed;
        result.FinishedAt = DateTime.Now;
        return result;
    }

    private void ProcessFolder(
        FolderNode node, string path, EnumerationOptions options, TopFiles top, List<FileEntry>? captureFiles,
        ConcurrentStack<(FolderNode, string)> work, ref long pending, ConcurrentQueue<string> unreadableSamples)
    {
        _current = path;
        List<FolderNode>? children = null;
        List<(FolderNode, string)>? childWork = null;

        using (var enumerator = new FolderEnumerator(path, options, top, captureFiles))
        {
            try
            {
                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current;
                    string childPath = Path.Join(path, entry.Name);
                    if (entry.IsReparsePoint && !ShouldFollowReparsePoint(childPath)) continue;

                    var child = new FolderNode(entry.Name, node);
                    (children ??= new List<FolderNode>()).Add(child);
                    (childWork ??= new List<(FolderNode, string)>()).Add((child, childPath));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                enumerator.Error = enumerator.Error == 0 ? 5 : enumerator.Error;
            }

            node.OwnFilesSize = enumerator.FilesSize;
            node.OwnFileCount = enumerator.FileCount;
            Interlocked.Add(ref _bytes, enumerator.FilesSize);
            Interlocked.Add(ref _files, enumerator.FileCount);

            if (enumerator.Error != 0)
            {
                node.Unreadable = true;
                Interlocked.Increment(ref _unreadable);
                if (unreadableSamples.Count < MaxUnreadableSamples) unreadableSamples.Enqueue(path);
            }
        }

        Interlocked.Increment(ref _folders);
        node.ChildList = children;

        if (childWork is not null)
        {
            Interlocked.Add(ref pending, childWork.Count);
            work.PushRange(childWork.ToArray());
        }
    }

    /// <summary>Adds up sizes bottom-up and sorts every folder's children biggest first.</summary>
    internal static void ComputeTotals(FolderNode root)
    {
        var stack = new Stack<(FolderNode Node, bool Visited)>();
        stack.Push((root, false));
        while (stack.Count > 0)
        {
            var (node, visited) = stack.Pop();
            if (!visited)
            {
                stack.Push((node, true));
                foreach (var child in node.Children) stack.Push((child, false));
                continue;
            }

            long size = node.OwnFilesSize, count = node.OwnFileCount;
            if (node.ChildList is { } children)
            {
                foreach (var child in children)
                {
                    size += child.Size;
                    count += child.FileCount;
                }

                children.Sort((a, b) => b.Size.CompareTo(a.Size));
            }

            node.Size = size;
            node.FileCount = count;
        }
    }

    /// <summary>
    /// Junctions, mount points and symbolic links ("name surrogates") point somewhere else that is
    /// scanned on its own, so they're skipped. Other reparse points (OneDrive folders, containers) are followed.
    /// </summary>
    internal static bool ShouldFollowReparsePoint(string path)
    {
        uint tag = NativeMethods.GetReparseTag(path);
        const uint NameSurrogateBit = 0x20000000;
        return tag != 0 && (tag & NameSurrogateBit) == 0;
    }

    internal readonly record struct FolderItem(string Name, bool IsReparsePoint);

    /// <summary>Lists one folder: adds up its files on the fly and only returns its subfolders.</summary>
    private sealed class FolderEnumerator : FileSystemEnumerator<FolderItem>
    {
        private readonly TopFiles _top;
        private readonly List<FileEntry>? _capture;

        public FolderEnumerator(string directory, EnumerationOptions options, TopFiles top, List<FileEntry>? capture)
            : base(directory, options)
        {
            _top = top;
            _capture = capture;
        }

        public long FilesSize { get; private set; }
        public long FileCount { get; private set; }
        public int Error { get; set; }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            if (entry.IsDirectory) return true;

            long length = (entry.Attributes & CloudOnlyAttributes) != 0 ? 0 : entry.Length;
            FilesSize += length;
            FileCount++;

            bool wanted = _top.Accepts(length);
            if (wanted || _capture is not null)
            {
                var file = new FileEntry(entry.ToFullPath(), length, entry.LastWriteTimeUtc.UtcDateTime);
                if (wanted) _top.Add(file);
                _capture?.Add(file);
            }

            return false;
        }

        protected override FolderItem TransformEntry(ref FileSystemEntry entry) =>
            new(entry.FileName.ToString(), (entry.Attributes & FileAttributes.ReparsePoint) != 0);

        protected override bool ContinueOnError(int error)
        {
            Error = error;
            return true;
        }
    }

    /// <summary>Keeps the N largest files seen, using a min-heap.</summary>
    private sealed class TopFiles
    {
        private readonly int _capacity;
        private readonly PriorityQueue<FileEntry, long> _heap = new();

        public TopFiles(int capacity) => _capacity = capacity;

        public bool Accepts(long size) =>
            size > 0 && (_heap.Count < _capacity || (_heap.TryPeek(out _, out long smallest) && size > smallest));

        public void Add(FileEntry file)
        {
            if (_heap.Count < _capacity) _heap.Enqueue(file, file.Size);
            else _heap.EnqueueDequeue(file, file.Size);
        }

        public static IEnumerable<FileEntry> Merge(IEnumerable<TopFiles> lists, int count) =>
            lists.SelectMany(l => l._heap.UnorderedItems.Select(i => i.Element))
                 .OrderByDescending(f => f.Size)
                 .Take(count);
    }

    private static class NativeMethods
    {
        private static readonly IntPtr InvalidHandle = new(-1);

        public static uint GetReparseTag(string path)
        {
            string query = path.Length >= 248 && !path.StartsWith(@"\\?\") ? @"\\?\" + path : path;
            IntPtr handle = FindFirstFileW(query, out var data);
            if (handle == InvalidHandle) return 0;
            FindClose(handle);
            return (data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.dwReserved0 : 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr hFindFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }
    }
}
