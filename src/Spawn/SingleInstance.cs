using System.Threading;

namespace Spawn;

/// <summary>
/// Ensures only one Spawn process runs, and that it does not run beside a
/// leftover ProcessLink process that still owns <see cref="LegacyMutexName"/>.
/// While Spawn is running it keeps a handle on that legacy mutex. ProcessLink
/// treats "the mutex already exists" as "already running", so the old exe
/// will not start beside Spawn either.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Global\Spawn_SingleInstance";
    public const string LegacyMutexName = @"Global\ProcessLink_SingleInstance";

    private readonly Mutex _mutex;
    private readonly Mutex? _legacyMutex;
    private readonly bool _ownsLegacyMutex;
    private bool _disposed;

    private SingleInstance(Mutex mutex, Mutex? legacyMutex, bool ownsLegacyMutex)
    {
        _mutex = mutex;
        _legacyMutex = legacyMutex;
        _ownsLegacyMutex = ownsLegacyMutex;
    }

    /// <summary>
    /// Acquires <see cref="MutexName"/>. Returns null when this process should
    /// exit: another Spawn already holds it, or a leftover ProcessLink process
    /// holds <see cref="LegacyMutexName"/>.
    /// </summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        if (!TryHoldLegacyMutex(out var legacy, out var ownsLegacy))
        {
            ReleaseAndDispose(mutex);
            return null;
        }

        return new SingleInstance(mutex, legacy, ownsLegacy);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_legacyMutex != null)
        {
            if (_ownsLegacyMutex)
                ReleaseQuietly(_legacyMutex);
            _legacyMutex.Dispose();
        }

        ReleaseAndDispose(_mutex);
    }

    /// <summary>
    /// Opens the legacy ProcessLink mutex and keeps the handle for this process.
    /// Returns false only when another process currently owns it.
    /// </summary>
    private static bool TryHoldLegacyMutex(out Mutex? legacy, out bool ownsLegacy)
    {
        legacy = null;
        ownsLegacy = false;
        try
        {
            legacy = new Mutex(false, LegacyMutexName, out bool createdNew);
            if (createdNew)
            {
                // Nobody else had created it. The open handle is what ProcessLink
                // checks (createdNew == false). This thread does not own it yet.
                return true;
            }

            try
            {
                if (!legacy.WaitOne(0))
                {
                    legacy.Dispose();
                    legacy = null;
                    return false;
                }

                ownsLegacy = true;
                return true;
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited without releasing. This thread owns it now.
                ownsLegacy = true;
                return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The name exists but cannot be inspected. Do not block Spawn.
            legacy?.Dispose();
            legacy = null;
            ownsLegacy = false;
            return true;
        }
    }

    private static void ReleaseAndDispose(Mutex mutex)
    {
        ReleaseQuietly(mutex);
        mutex.Dispose();
    }

    private static void ReleaseQuietly(Mutex mutex)
    {
        try { mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
    }
}
