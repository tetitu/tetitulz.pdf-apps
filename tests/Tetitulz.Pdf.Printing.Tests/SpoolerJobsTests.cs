// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

namespace Tetitulz.Pdf.Printing.Tests;

/// <summary>
/// Which queue the jobs' document names are asked of. A listener started before its printer exists —
/// the installer starts the tray app, which then adds the printer — looks for the queue again at each
/// job until it finds it, then asks that one; a queue found at the start is not looked for again.
/// </summary>
public sealed class SpoolerJobsTests
{
    /// <summary>
    /// No printer at the start, none at the first job, the printer at the second: the queue is looked
    /// for each time until found and then kept. The queue named does not exist, so the job itself is
    /// not read — the spooler answers no job, not a failure.
    /// </summary>
    [Fact]
    public void AQueueNotFoundAtTheStart_IsLookedForAgainAtEachJob_UntilFound()
    {
        var answers = new Queue<string?>(new[] { null, null, "Tetitulz test queue that does not exist" });
        int asked = 0;
        var spooler = new SpoolerJobs(() => { asked++; return answers.Dequeue(); });
        Assert.Null(spooler.QueueName);

        Assert.Null(spooler.CurrentJob());
        Assert.Null(spooler.QueueName);
        Assert.Equal(2, asked);

        Assert.Null(spooler.CurrentJob());
        Assert.Equal("Tetitulz test queue that does not exist", spooler.QueueName);
        Assert.Equal(3, asked);

        Assert.Null(spooler.CurrentJob());
        Assert.Equal(3, asked);
    }

    /// <summary>
    /// Jobs arriving together while no printer is found yet look for it one at a time: the search is
    /// never run by two threads at once, so it is not repeated side by side nor its finding said twice.
    /// </summary>
    [Fact]
    public void JobsArrivingTogether_LookForTheQueueOneAtATime()
    {
        int inside = 0, mostInside = 0, asked = 0;
        string? Find()
        {
            int now = Interlocked.Increment(ref inside);
            int seen;
            while ((seen = Volatile.Read(ref mostInside)) < now && Interlocked.CompareExchange(ref mostInside, now, seen) != seen) { }
            Interlocked.Increment(ref asked);
            Thread.Sleep(20);
            Interlocked.Decrement(ref inside);
            return null;
        }
        var spooler = new SpoolerJobs(Find);
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() => spooler.CurrentJob())).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join(TimeSpan.FromSeconds(30)));

        Assert.Equal(9, asked);                 // the start's search and each job's
        Assert.Equal(1, mostInside);            // never two at once
    }

    /// <summary>A queue found at the start is the one asked; it is not looked for again.</summary>
    [Fact]
    public void AQueueFoundAtTheStart_IsNotLookedForAgain()
    {
        int asked = 0;
        var spooler = new SpoolerJobs(() => { asked++; return "Tetitulz test queue that does not exist"; });
        spooler.CurrentJob();
        spooler.CurrentJob();
        Assert.Equal("Tetitulz test queue that does not exist", spooler.QueueName);
        Assert.Equal(1, asked);
    }
}
