using System;

namespace SRMS.Desktop.Abstractions
{
    /// <summary>
    /// Platform-agnostic app lifecycle events. Lets SRMS.Core services
    /// pause background work (log flushing, auto-save timers, cache cleanup)
    /// when the app is backgrounded, and resume when it returns.
    ///
    /// Without this, Logger's 5-second flush timer keeps firing on Android
    /// in the background and drains battery.
    /// </summary>
    public interface IAppLifecycle
    {
        event EventHandler Resumed;
        event EventHandler Paused;
        event EventHandler Stopping;
        bool IsForeground { get; }
    }
}