﻿using NoiseEngine.Interop;
using NoiseEngine.Interop.Rendering.Presentation;
using NoiseEngine.Mathematics;
using NoiseEngine.Rendering;
using NoiseEngine.Threading;
using System;

namespace NoiseEngine;

public class Window : IDisposable, ICameraRenderTarget {

    private AtomicBool isDisposed;

    public bool IsDisposed => isDisposed;

    internal InteropHandle<Window> Handle { get; }

    TextureUsage ICameraRenderTarget.Usage => TextureUsage.ColorAttachment;
    Vector3<uint> ICameraRenderTarget.Extent => new Vector3<uint>(1280, 720, 0);
    uint ICameraRenderTarget.SampleCount => 1;
    TextureFormat ICameraRenderTarget.Format => throw new NotImplementedException();

    public Window(string? title = null, uint width = 1280, uint height = 720) {
        title ??= Application.Name;

        if (!WindowInterop.Create(title, width, height).TryGetValue(
            out InteropHandle<Window> handle, out ResultError error
        )) {
            error.ThrowAndDispose();
        }

        Handle = handle;
    }

    ~Window() {
        WindowInterop.Destroy(Handle);
    }

    /// <summary>
    /// Disposes this <see cref="Window"/>.
    /// </summary>
    public void Dispose() {
        if (isDisposed.Exchange(true))
            return;

        WindowInterop.Destroy(Handle);
        GC.SuppressFinalize(this);
    }

}
