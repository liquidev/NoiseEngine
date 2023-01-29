﻿using NoiseEngine.Collections.Concurrent;
using NoiseEngine.Jobs;
using NoiseEngine.Primitives;
using NoiseEngine.Rendering;
using NoiseEngine.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoiseEngine;

public class ApplicationScene : IDisposable {

    private readonly ConcurrentList<Camera> cameras = new ConcurrentList<Camera>();

    private AtomicBool isDisposed;
    private GraphicsDevice? graphicsDevice;

    public EntityWorld EntityWorld { get; } = new EntityWorld();
    public PrimitiveCreator Primitive { get; }

    public GraphicsDevice GraphicsDevice {
        get {
            if (graphicsDevice is null)
                graphicsDevice = Application.GraphicsInstance.Devices.First();
            return graphicsDevice!;
        }
        init => graphicsDevice = value;
    }

    public bool IsDisposed => isDisposed;
    public IEnumerable<Camera> Cameras => cameras;

    internal ConcurrentList<EntitySystemBase> FrameDependentSystems { get; } = new ConcurrentList<EntitySystemBase>();

    public ApplicationScene() {
        Primitive = new PrimitiveCreator(this);
        Application.AddSceneToLoaded(this);
    }

    /// <summary>
    /// Initializes and adds <paramref name="system"/> to systems witch will be executed on each render frame.
    /// </summary>
    /// <param name="system"><see cref="EntitySystemBase"/> system witch will be
    /// executed on each render frame.</param>
    public void AddFrameDependentSystem(EntitySystemBase system) {
        system.Initialize(EntityWorld, Application.EntitySchedule);
        FrameDependentSystems.Add(system);
    }

    /// <summary>
    /// Checks if this <see cref="ApplicationScene"/> has frame dependent system of a T type.
    /// </summary>
    /// <typeparam name="T">Type of <see cref="EntitySystemBase"/>.</typeparam>
    /// <returns><see langword="true"/> when this <see cref="ApplicationScene"/> has frame dependent system
    /// of a T type; otherwise <see langword="false"/>.</returns>
    public bool HasFrameDependentSystem<T>() where T : EntitySystemBase {
        Type type = typeof(T);
        return FrameDependentSystems.Any(x => x.GetType() == type);
    }

    /// <summary>
    /// Disposes this <see cref="ApplicationScene"/>.
    /// </summary>
    public void Dispose() {
        if (isDisposed.Exchange(true))
            return;

        Application.RemoveSceneFromLoaded(this);

        OnDispose();

        //foreach (Camera camera in Cameras)
        //    camera.Dispose();

        FrameDependentSystems.Clear();
        EntityWorld.Dispose();

        GC.SuppressFinalize(this);
    }

    internal void RemoveRenderCameraFromScene(Camera renderCamera) {
        cameras.Remove(renderCamera);
    }

    /// <summary>
    /// This method is executed when <see cref="Dispose"/> is called.
    /// </summary>
    protected virtual void OnDispose() {
    }

}
