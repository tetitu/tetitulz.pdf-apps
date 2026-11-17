// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;

namespace Tetitulz.Pdf.Printing;

/// <summary>A job the spooler wrote: its PostScript spooled to a file, when it came, and the document name the spooler gave it.</summary>
public sealed record ReceivedJob(string PostScriptPath, DateTime Received, string? DocumentName, long Bytes);

/// <summary>The printer's pipe already exists: another process serves it, and this listener does not share it.</summary>
public sealed class PipeAlreadyServedException(string pipeName, Exception inner)
    : Core.PdfException($@"The pipe \\.\pipe\{pipeName} already exists: another process is serving the printer's port. " +
                  "This listener takes the pipe only when it is the first to make it, so that no other process receives print jobs beside it.", inner);

/// <summary>
/// The printer's port: a named pipe the spooler opens for each job, writes the driver's PostScript
/// into, and closes. Each connection is one job, spooled to a file as it arrives and handed on once it
/// has ended; a new pipe instance is listening before the job is read, so a second job is not refused
/// while the first is read, and jobs are handed on one at a time — which bounds the memory their
/// conversions hold, and lets a handler that asks the user (the tray app's save dialog) ask about one
/// job at a time.
///
/// <para>The pipe admits the spooler (LocalSystem) and the user running the listener, and no one
/// else: another account's process can neither write a job into this user's folder nor read one.
/// The listener makes the pipe's first instance or does not start (<see cref="PipeAlreadyServedException"/>),
/// and holds an instance from then on, so no process that made the name first receives jobs beside
/// it. The pipe's name is machine-wide, so one listener serves the printer at a time.</para>
///
/// <para>The printer is the machine's too: any account can print to it, and the spooler writes
/// every job into this pipe. A job the spooler says another account submitted is dropped, not saved
/// (<see cref="SpooledJob.IsFromAnotherAccountThan"/>); one whose submitter the spooler did not say in
/// time is kept, so on a machine several people use at once a job can still reach the listener of
/// whoever is running it.</para>
/// </summary>
public sealed class JobListener
{
    /// <summary>A job past this many bytes is not kept: the rest is read and dropped, and the job reported.</summary>
    internal const long MaxJobBytes = 1L << 30;

    /// <summary>
    /// How long the job's document name is waited for before the job is read. It is asked first —
    /// once the pipe is read to its end the spooler finishes the job and it leaves the queue — but not
    /// waited on past this: the pipe holds no buffer, and a spooler that answered only after its write
    /// would otherwise never be read.
    /// </summary>
    private static readonly TimeSpan NamingTimeout = TimeSpan.FromSeconds(2);

    private readonly string _pipeName;
    private readonly string _spoolDirectory;
    private readonly Func<SpooledJob?> _currentJob;
    private readonly Func<ReceivedJob, Task> _handle;
    private readonly string _account;
    private readonly SemaphoreSlim _oneJob = new(1, 1);

    /// <param name="pipeName">The pipe the printer's port is (<see cref="PrinterSetup.PipeName"/>).</param>
    /// <param name="spoolDirectory">Where each job is spooled while it arrives.</param>
    /// <param name="spooler">What the spooler says of the job it is writing: its document name and submitter.</param>
    /// <param name="handle">What becomes of each job: it owns the spooled file from then on.</param>
    public JobListener(string pipeName, string spoolDirectory, SpoolerJobs spooler, Func<ReceivedJob, Task> handle)
        : this(pipeName, spoolDirectory, spooler.CurrentJob, handle, Environment.UserName)
    {
    }

    /// <param name="pipeName">The pipe the printer's port is.</param>
    /// <param name="spoolDirectory">Where each job is spooled while it arrives.</param>
    /// <param name="currentJob">What the spooler says of the job it is writing, or null.</param>
    /// <param name="handle">What becomes of each job: it owns the spooled file from then on.</param>
    /// <param name="account">The account whose jobs are kept.</param>
    internal JobListener(string pipeName, string spoolDirectory, Func<SpooledJob?> currentJob,
        Func<ReceivedJob, Task> handle, string account)
    {
        _pipeName = pipeName;
        _spoolDirectory = spoolDirectory;
        _currentJob = currentJob;
        _handle = handle;
        _account = account;
        Directory.CreateDirectory(_spoolDirectory);
        // A spooled job is removed when it has been handled; one a stopped process left behind is
        // a printed document with no one to hand it to.
        JobConverter.RemoveOlderThan(_spoolDirectory, TimeSpan.FromDays(1), DateTime.UtcNow);
    }

    /// <exception cref="PipeAlreadyServedException">The pipe exists already; nothing was served.</exception>
    public async Task RunAsync(CancellationToken cancel)
    {
        var jobs = new List<Task>();
        NamedPipeServerStream waiting = CreatePipe(first: true);
        while (true)
        {
            try
            {
                await waiting.WaitForConnectionAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                await waiting.DisposeAsync();
                break;
            }

            // The next instance is made before this one is handed on, and so before it can be
            // closed: from the first instance to the last there is always one this listener holds,
            // and the name is never free for another process to make first.
            NamedPipeServerStream connected = waiting;
            try
            {
                waiting = CreatePipe(first: false);
            }
            catch
            {
                await connected.DisposeAsync();
                throw;
            }
            jobs.RemoveAll(job => job.IsCompleted);
            jobs.Add(Task.Run(() => ReceiveAsync(connected, cancel), CancellationToken.None));
        }
        await Task.WhenAll(jobs);
    }

    private NamedPipeServerStream CreatePipe(bool first)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        using (var user = WindowsIdentity.GetCurrent())
            security.AddAccessRule(new PipeAccessRule(user.User!, PipeAccessRights.FullControl, AccessControlType.Allow));

        // FirstPipeInstance refuses a name that exists. Without it a process that made the pipe
        // before this one — under a security descriptor of its choosing — would go on receiving a
        // share of the spooler's connections, each a document someone printed.
        PipeOptions options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        try
        {
            return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options,
                inBufferSize: 0, outBufferSize: 0, security);
        }
        catch (Exception ex) when (first && ex is UnauthorizedAccessException or IOException)
        {
            throw new PipeAlreadyServedException(_pipeName, ex);
        }
    }

    private async Task ReceiveAsync(NamedPipeServerStream pipe, CancellationToken cancel)
    {
        DateTime received = DateTime.Now;
        Task<SpooledJob?> naming = Task.Run(_currentJob);
        if (await Task.WhenAny(naming, Task.Delay(NamingTimeout)) != naming)
            Log.Debug("The spooler did not name the job within {Timeout}; reading it meanwhile", NamingTimeout);
        string spooled = Path.Combine(_spoolDirectory, $"job-{received:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.ps");
        long length;
        bool tooLarge;
        try
        {
            await using (pipe)
            await using (var file = File.Create(spooled))
                (length, tooLarge) = await CopyCappedAsync(pipe, file, cancel);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            Log.Error(ex, "A job was not received whole");
            TryDelete(spooled);
            return;
        }

        if (tooLarge)
        {
            Log.Error("A job past {Max} bytes was dropped", MaxJobBytes);
            TryDelete(spooled);
            return;
        }
        if (length == 0)
        {
            TryDelete(spooled);
            return;
        }

        SpooledJob? job = await Task.WhenAny(naming, Task.Delay(NamingTimeout)) == naming ? await naming : null;
        if (job != null && job.IsFromAnotherAccountThan(_account))
        {
            // Not this account's document: neither saved nor named in the log.
            Log.Warning("A job of {Bytes:N0} bytes submitted by another account ({Submitter}) was dropped", length, job.Submitter);
            TryDelete(spooled);
            return;
        }

        string? documentName = job?.Name;
        Log.Information("Job received: {Document}, {Bytes:N0} bytes", documentName ?? "(no document name)", length);
        await _oneJob.WaitAsync(CancellationToken.None);
        try
        {
            await _handle(new ReceivedJob(spooled, received, documentName, length));
        }
        catch (Exception ex)
        {
            // One job's failure is that job's: the listener goes on serving the printer.
            Log.Error(ex, "The job {Job} could not be handled", spooled);
        }
        finally
        {
            _oneJob.Release();
        }
    }

    /// <summary>Copies until the writer closes the pipe; past <see cref="MaxJobBytes"/> the rest is read and dropped.</summary>
    internal static async Task<(long Length, bool TooLarge)> CopyCappedAsync(Stream from, Stream to, CancellationToken cancel)
    {
        byte[] buffer = new byte[81920];
        long length = 0;
        bool tooLarge = false;
        int read;
        while ((read = await from.ReadAsync(buffer, cancel)) > 0)
        {
            length += read;
            if (length > MaxJobBytes) tooLarge = true;
            if (!tooLarge) await to.WriteAsync(buffer.AsMemory(0, read), cancel);
        }
        return (length, tooLarge);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException ex) { Log.Warning(ex, "{Path} could not be deleted", path); }
    }
}
