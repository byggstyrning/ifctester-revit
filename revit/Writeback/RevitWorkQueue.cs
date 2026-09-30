using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace IfcTesterRevit.Writeback;

/// <summary>
/// Thrown when Revit did not get to a queued job in time. The job is withdrawn, so it will not
/// run later behind the caller's back.
/// </summary>
public sealed class RevitBusyException : Exception
{
    public RevitBusyException()
        : base("Revit is busy. Finish the current command or close the open dialog in Revit, then try again.")
    {
    }
}

/// <summary>
/// Runs work on the Revit thread through one external event and a queue. Every request gets its
/// own job and its own completion source, so overlapping HTTP requests cannot overwrite each
/// other's arguments or results.
/// </summary>
public sealed class RevitWorkQueue : IExternalEventHandler
{
    private readonly ConcurrentQueue<Job> _jobs = new();
    private ExternalEvent? _externalEvent;

    /// <summary>Creates the external event. Call from a valid Revit API context.</summary>
    public void Start()
    {
        _externalEvent ??= ExternalEvent.Create(this);
    }

    /// <summary>
    /// Queues the work and waits for its result. <paramref name="startTimeout"/> limits only the
    /// wait for Revit to pick the job up; once it has started it runs to the end, however long a
    /// dialog in Revit keeps it.
    /// </summary>
    public async Task<T> Run<T>(Func<UIApplication, T> work, TimeSpan startTimeout)
    {
        var externalEvent = _externalEvent ?? throw new InvalidOperationException("Revit application not available");

        var job = new Job(app => work(app));
        _jobs.Enqueue(job);

        var deadline = DateTime.UtcNow + startTimeout;
        while (job.IsPending)
        {
            if (DateTime.UtcNow >= deadline)
            {
                if (job.TryCancel()) throw new RevitBusyException();
                break;
            }

            // Raising again while the event is already pending is harmless, and it covers a job
            // queued just as Execute finished draining.
            externalEvent.Raise();
            await Task.WhenAny(job.Completion.Task, Task.Delay(250)).ConfigureAwait(false);
        }

        return (T)(await job.Completion.Task.ConfigureAwait(false))!;
    }

    public void Execute(UIApplication app)
    {
        while (_jobs.TryDequeue(out var job))
        {
            if (!job.TryStart()) continue;

            try
            {
                job.Completion.SetResult(job.Work(app));
            }
            catch (Exception ex)
            {
                job.Completion.SetException(ex);
            }
        }
    }

    public string GetName() => "IfcTester write-back";

    private sealed class Job
    {
        private const int Pending = 0;
        private const int Started = 1;
        private const int Cancelled = 2;

        private int _state = Pending;

        public Job(Func<UIApplication, object?> work)
        {
            Work = work;
        }

        public Func<UIApplication, object?> Work { get; }

        // Continuations must not run inline on the Revit thread.
        public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsPending => Volatile.Read(ref _state) == Pending;

        public bool TryStart() => Interlocked.CompareExchange(ref _state, Started, Pending) == Pending;

        public bool TryCancel() => Interlocked.CompareExchange(ref _state, Cancelled, Pending) == Pending;
    }
}
