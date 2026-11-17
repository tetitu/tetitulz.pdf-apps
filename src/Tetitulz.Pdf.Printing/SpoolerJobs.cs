// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Printing;
using Serilog;

namespace Tetitulz.Pdf.Printing;

/// <summary>What the spooler says of the job it is printing: the document's name and the account that submitted it.</summary>
public sealed record SpooledJob(string? Name, string? Submitter)
{
    /// <summary>
    /// Whether the job was submitted by an account other than <paramref name="account"/>. The
    /// printer is the machine's, so any account can print to it, and the spooler writes every job
    /// into the one pipe: a listener that kept them all would save another person's document into
    /// this account's folder. A job whose submitter the spooler did not say is not another's.
    /// </summary>
    public bool IsFromAnotherAccountThan(string account)
    {
        if (string.IsNullOrWhiteSpace(Submitter)) return false;
        string name = Submitter.Trim();
        int domain = name.LastIndexOf('\\');
        if (domain >= 0) name = name[(domain + 1)..];
        return !string.Equals(name, account, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// What the spooler knows about the job it is writing into the pipe. The driver's PostScript does not
/// name the document — the Microsoft PS Class Driver writes <c>%%Title: MSxpsPS</c> for every job —
/// but the job the program submitted does: while the spooler writes to the port, its job is the one
/// printing on the queue whose port is the pipe.
///
/// <para>The queue is looked for when the listener starts, which also loads System.Printing: its first
/// use in a process takes longer than a small job stays in the queue. A listener can start before its
/// printer exists — the installer starts the tray app, which then adds the printer — so a queue not
/// found then is looked for again at each job until it is; a spooler that cannot be read says so
/// once, not at every job. Its objects belong to the thread that made them, so each question opens
/// the queue afresh on its own thread.</para>
/// </summary>
public sealed class SpoolerJobs
{
    private readonly Func<string?> _findQueue;

    // Jobs are received concurrently, each asking CurrentJob on its own thread: the queue is looked
    // for, and the spooler's unreadability said, under this lock, so two jobs arriving together
    // neither search twice nor both say it.
    private readonly object _gate = new();
    private string? _queueName;
    private bool _unreadableSaid;

    public SpoolerJobs(string portName)
    {
        _findQueue = () => QueueOnPort(portName);
        _queueName = _findQueue();
        if (_queueName != null) Log.Information("Serving the printer \"{Printer}\"", _queueName);
        else Log.Warning("No printer uses the port {Port} yet; it is looked for again at each job", portName);
    }

    /// <param name="findQueue">The name of the queue whose port is the pipe, or null when there is none (yet).</param>
    internal SpoolerJobs(Func<string?> findQueue)
    {
        _findQueue = findQueue;
        _queueName = findQueue();
        if (_queueName != null) Log.Information("Serving the printer \"{Printer}\"", _queueName);
    }

    /// <summary>The queue the jobs are asked of, or null while no printer uses the port.</summary>
    internal string? QueueName
    {
        get { lock (_gate) return _queueName; }
    }

    private string? QueueOnPort(string portName)
    {
        try
        {
            using var server = new LocalPrintServer();
            string? found = null;
            foreach (PrintQueue queue in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local }))
            {
                using (queue)
                    if (found == null && string.Equals(queue.QueuePort?.Name, portName, StringComparison.OrdinalIgnoreCase))
                        found = queue.Name;
            }
            return found;
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException)
        {
            if (!_unreadableSaid)
            {
                _unreadableSaid = true;
                Log.Warning(ex, "The spooler could not be read; PDFs are named from the job's own title until it can be (tried again at each job)");
            }
            return null;
        }
    }

    /// <summary>
    /// The job printing on the pipe's queue — its document name and the account that submitted it —
    /// or null when none can be read.
    /// </summary>
    public SpooledJob? CurrentJob()
    {
        string? queueName;
        lock (_gate)
        {
            if (_queueName == null)
            {
                _queueName = _findQueue();
                if (_queueName != null) Log.Information("Serving the printer \"{Printer}\"", _queueName);
            }
            queueName = _queueName;
        }
        if (queueName == null) return null;
        try
        {
            using var server = new LocalPrintServer();
            using var queue = new PrintQueue(server, queueName);
            SpooledJob? printing = null;
            foreach (PrintSystemJobInfo job in queue.GetPrintJobInfoCollection())
            {
                using (job)
                {
                    // The document's name is not logged here: the queue is the machine's, and
                    // this may be another account's job. The listener names the ones it keeps.
                    Log.Debug("Spooler job {Id} from {Submitter}: {Status}",
                        job.JobIdentifier, job.Submitter, job.JobStatus);
                    if (printing == null && job.IsPrinting) printing = new SpooledJob(job.Name, job.Submitter);
                }
            }
            return printing;
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException)
        {
            Log.Debug(ex, "The spooler's job could not be read");
            return null;
        }
    }
}
