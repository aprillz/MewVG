// Copyright (c) 2024 .NET Port
// MIT License

using System.Runtime.InteropServices;

namespace Aprillz.MewVG.Interop;

/// <summary>
/// Grand Central Dispatch (libdispatch) interop for macOS.
/// </summary>
public static unsafe partial class Dispatch
{
    private const string LibDispatch = "/usr/lib/libSystem.B.dylib";

    [LibraryImport(LibDispatch, EntryPoint = "dispatch_semaphore_create")]
    public static partial nint SemaphoreCreate(nint value);

    [LibraryImport(LibDispatch, EntryPoint = "dispatch_semaphore_wait")]
    public static partial nint SemaphoreWait(nint semaphore, ulong timeout);

    [LibraryImport(LibDispatch, EntryPoint = "dispatch_semaphore_signal")]
    public static partial nint SemaphoreSignal(nint semaphore);

    [LibraryImport(LibDispatch, EntryPoint = "dispatch_release")]
    public static partial void Release(nint obj);

    [LibraryImport(LibDispatch, EntryPoint = "dispatch_data_create")]
    public static partial nint DataCreate(void* buffer, nuint size, nint queue, nint destructor);

    public const ulong TimeForever = ulong.MaxValue;
    public const ulong TimeNow = 0;

    public static readonly nint DataDestructorDefault = nint.Zero;
    public static readonly nint MainQueue = GetMainQueue();

    [LibraryImport(LibDispatch, EntryPoint = "dispatch_get_main_queue")]
    private static partial nint GetMainQueue();
}

/// <summary>
/// Helper wrapper for dispatch semaphore.
/// </summary>
public sealed class DispatchSemaphore : IDisposable
{
    private nint _handle;
    private bool _disposed;

    public DispatchSemaphore(int value)
    {
        _handle = Dispatch.SemaphoreCreate(value);
    }

    public nint Handle => _handle;

    public bool Wait(ulong timeout = Dispatch.TimeForever)
    {
        return Dispatch.SemaphoreWait(_handle, timeout) == 0;
    }

    public void Signal()
    {
        Dispatch.SemaphoreSignal(_handle);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != nint.Zero)
            {
                Dispatch.Release(_handle);
                _handle = nint.Zero;
            }
            _disposed = true;
        }
    }
}
