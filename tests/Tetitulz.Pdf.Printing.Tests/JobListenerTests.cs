// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.IO.Pipes;

namespace Tetitulz.Pdf.Printing.Tests;

/// <summary>
/// The printer's port: each connection to the pipe is one job, spooled to a file and handed on once
/// the writer closes; an empty connection is no job; past a gigabyte the rest is read and dropped.
/// </summary>
public sealed class JobListenerTests : IDisposable
{
    private readonly string _spool = Path.Combine(Path.GetTempPath(), "vp-spool-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_spool)) Directory.Delete(_spool, recursive: true);
    }

    /// <summary>
    /// A job written into the pipe reaches the handler as a spooled file holding its bytes; a
    /// connection closed with nothing written reaches it not at all. A private pipe name and a port no
    /// printer uses keep the machine's printer out of it.
    /// </summary>
    [Fact]
    public async Task AJobWrittenIntoThePipe_ReachesTheHandler_AndAnEmptyOneDoesNot()
    {
        string pipeName = "TetitulzPdfPrinterTest-" + Guid.NewGuid().ToString("N");
        var handled = new List<(byte[] Bytes, ReceivedJob Job)>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new JobListener(pipeName, _spool, new SpoolerJobs(@"\\.\pipe\" + pipeName), job =>
        {
            lock (handled) handled.Add((File.ReadAllBytes(job.PostScriptPath), job));
            first.TrySetResult();
            return Task.CompletedTask;
        });
        using var stop = new CancellationTokenSource();
        Task running = listener.RunAsync(stop.Token);

        await Send(pipeName, Array.Empty<byte>());
        byte[] job = "%!PS\n(job) pop showpage\n"u8.ToArray();
        await Send(pipeName, job);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(30));

        stop.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        var only = Assert.Single(handled);
        Assert.Equal(job, only.Bytes);
        Assert.Equal(job.Length, only.Job.Bytes);
        Assert.Null(only.Job.DocumentName);
        Assert.StartsWith(_spool, only.Job.PostScriptPath);
    }

    /// <summary>
    /// A pipe that exists before the listener starts is someone else's: the listener refuses to
    /// start beside it, where joining it would leave that process receiving a share of the jobs.
    /// </summary>
    [Fact]
    public async Task APipeAnotherProcessMadeFirst_StopsTheListener()
    {
        string pipeName = "TetitulzPdfPrinterTest-" + Guid.NewGuid().ToString("N");
        await using var squatter = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var listener = new JobListener(pipeName, _spool, () => null, _ => Task.CompletedTask, Environment.UserName);

        var refusal = await Assert.ThrowsAsync<PipeAlreadyServedException>(() => listener.RunAsync(CancellationToken.None));

        Assert.Contains(pipeName, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The printer is the machine's: a job the spooler says another account submitted is dropped —
    /// not handed on, and its spooled file removed — and the next one, this account's, is kept.
    /// </summary>
    [Fact]
    public async Task AJobAnotherAccountSubmitted_IsDropped_AndThisAccountsIsKept()
    {
        string pipeName = "TetitulzPdfPrinterTest-" + Guid.NewGuid().ToString("N");
        var answers = new Queue<SpooledJob?>(new SpooledJob?[]
        {
            new("Payroll.xlsx", @"OFFICE\someone-else"),
            new("Mine.docx", @"OFFICE\me"),
        });
        var handled = new List<ReceivedJob>();
        var kept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new JobListener(pipeName, _spool, () => { lock (answers) return answers.Dequeue(); }, job =>
        {
            lock (handled) handled.Add(job);
            kept.TrySetResult();
            return Task.CompletedTask;
        }, account: "ME");
        using var stop = new CancellationTokenSource();
        Task running = listener.RunAsync(stop.Token);

        await Send(pipeName, "%!PS\n(theirs) pop showpage\n"u8.ToArray());
        await WaitUntil(() => Directory.GetFiles(_spool).Length == 0 && answers.Count == 1);
        await Send(pipeName, "%!PS\n(mine) pop showpage\n"u8.ToArray());
        await kept.Task.WaitAsync(TimeSpan.FromSeconds(30));

        stop.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        var only = Assert.Single(handled);
        Assert.Equal("Mine.docx", only.DocumentName);
        Assert.Equal(only.PostScriptPath, Assert.Single(Directory.GetFiles(_spool)));
    }

    /// <summary>Whose job it is: the account's own name with or without a domain, in any case; a job with no submitter is not another's.</summary>
    [Theory]
    [InlineData("me", false)]
    [InlineData(@"OFFICE\Me", false)]
    [InlineData(" ME ", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("someone-else", true)]
    [InlineData(@"me\someone-else", true)]
    [InlineData("me2", true)]
    public void AJobIsAnothers_WhenItsSubmitterIsNamedAndIsNotThisAccount(string? submitter, bool anothers)
        => Assert.Equal(anothers, new SpooledJob("Document", submitter).IsFromAnotherAccountThan("me"));

    /// <summary>Waits for the listener's own work — a file removed, an answer taken — without a fixed sleep.</summary>
    private static async Task WaitUntil(Func<bool> reached)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!reached())
        {
            Assert.True(DateTime.UtcNow < deadline, "The listener did not finish the first job.");
            await Task.Delay(20);
        }
    }

    private static async Task Send(string pipeName, byte[] bytes)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        await client.ConnectAsync(30_000);
        await client.WriteAsync(bytes);
    }

    /// <summary>A job past the cap is counted to its end and reported too large, with nothing past the cap written.</summary>
    [Fact]
    public async Task AJobPastTheCap_IsReadToItsEnd_AndNotWrittenPastIt()
    {
        var sink = new CountingSink();
        var (length, tooLarge) = await JobListener.CopyCappedAsync(new Zeros(JobListener.MaxJobBytes + 1), sink, CancellationToken.None);

        Assert.Equal(JobListener.MaxJobBytes + 1, length);
        Assert.True(tooLarge);
        Assert.InRange(sink.Written, JobListener.MaxJobBytes - 81920, JobListener.MaxJobBytes);
    }

    /// <summary>The control: a job of exactly the cap is kept whole.</summary>
    [Fact]
    public async Task AJobOfExactlyTheCap_IsKeptWhole()
    {
        var sink = new CountingSink();
        var (length, tooLarge) = await JobListener.CopyCappedAsync(new Zeros(JobListener.MaxJobBytes), sink, CancellationToken.None);

        Assert.Equal(JobListener.MaxJobBytes, length);
        Assert.False(tooLarge);
        Assert.Equal(JobListener.MaxJobBytes, sink.Written);
    }

    /// <summary>A stream of <c>length</c> bytes that reads without filling the buffer: a gigabyte at no cost.</summary>
    private sealed class Zeros(long length) : Stream
    {
        private long _left = length;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = (int)Math.Min(buffer.Length, _left);
            _left -= n;
            return ValueTask.FromResult(n);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream that counts what is written to it and keeps none of it.</summary>
    private sealed class CountingSink : Stream
    {
        public long Written { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written += buffer.Length;
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count) => Written += count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
