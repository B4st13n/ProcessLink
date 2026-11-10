using Xunit;

namespace Spawn.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void TryAcquire_SecondCallFailsUntilFirstIsDisposed()
    {
        var first = SingleInstance.TryAcquire();
        try
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstance.TryAcquire());
        }
        finally
        {
            first?.Dispose();
        }

        using var second = SingleInstance.TryAcquire();
        Assert.NotNull(second);
    }

    [Fact]
    public void TryAcquire_FailsWhileLegacyProcessLinkMutexIsOwned_ThenSucceeds()
    {
        // Ownership is per thread. A same-thread WaitOne would succeed recursively,
        // so the leftover ProcessLink process is simulated by another thread.
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        Exception? error = null;

        var worker = new Thread(() =>
        {
            Mutex? legacy = null;
            try
            {
                legacy = new Mutex(true, SingleInstance.LegacyMutexName, out bool created);
                if (!created)
                    throw new InvalidOperationException("legacy mutex already existed");
                started.Set();
                release.Wait();
                legacy.ReleaseMutex();
            }
            catch (Exception ex)
            {
                error = ex;
                started.Set();
            }
            finally
            {
                legacy?.Dispose();
            }
        });
        worker.IsBackground = true;
        worker.Start();

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        if (error != null)
            throw error;

        try
        {
            Assert.Null(SingleInstance.TryAcquire());
        }
        finally
        {
            release.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        }

        using var acquired = SingleInstance.TryAcquire();
        Assert.NotNull(acquired);
    }

    [Fact]
    public void TryAcquire_KeepsLegacyMutexVisibleToProcessLink()
    {
        using (var gate = SingleInstance.TryAcquire())
        {
            Assert.NotNull(gate);

            // ProcessLink treats an existing mutex (createdNew == false) as already running.
            using var probe = new Mutex(true, SingleInstance.LegacyMutexName, out bool createdNew);
            Assert.False(createdNew);
        }

        using var after = new Mutex(true, SingleInstance.LegacyMutexName, out bool createdAfter);
        Assert.True(createdAfter);
        after.ReleaseMutex();
    }
}
