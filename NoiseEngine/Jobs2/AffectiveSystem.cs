﻿using NoiseEngine.Collections.Concurrent;
using NoiseEngine.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NoiseEngine.Jobs2;

public abstract class AffectiveSystem : IDisposable {

    private readonly Dictionary<int, EntitySystem> systems = new Dictionary<int, EntitySystem>();
    private readonly ConcurrentHashSet<Archetype> archetypes = new ConcurrentHashSet<Archetype>();

    private EntityWorld? world;
    private AtomicBool isDisposed;

    public IEnumerable<EntitySystem> Systems => systems.Values;
    public bool IsDisposed => isDisposed;
    public EntityWorld World {
        get {
            EntityWorld? world = this.world;
            if (world is not null)
                return world;

            AssertIsNotDisposed();
            throw new InvalidOperationException("This affective system is not initialized.");
        }
    }

    private protected abstract Type[] AffectiveComponents { get; }

    private protected abstract EntitySystem CreateFromComponents(Dictionary<Type, object> components);

    /// <summary>
    /// Disposes this <see cref="AffectiveSystem"/> and their <see cref="EntitySystem"/>s.
    /// </summary>
    public void Dispose() {
        if (isDisposed.Exchange(true))
            return;

        foreach (EntitySystem system in systems.Values)
            system.Dispose();
        systems.Clear();
    }

    internal void InternalInitialize(EntityWorld world) {
        AssertIsNotDisposed();
        if (Interlocked.CompareExchange(ref this.world, world, null) is not null)
            throw new InvalidOperationException("Affective system is already initialized.");
    }

    internal void RegisterArchetypes(Archetype archetype) {
        if (isDisposed || archetypes.Contains(archetype))
            return;

        int hashCode = unchecked((int)2166136261u);
        foreach (Type type in AffectiveComponents) {
            if (!archetype.Offsets.ContainsKey(type))
                return;

            hashCode *= 16777619;
            hashCode ^= archetype.ComponentTypes.First(x => x.type == type).affectiveHashCode;
        }

        if (!archetypes.Add(archetype))
            return;

        if (!systems.TryGetValue(hashCode, out EntitySystem? system)) {
            lock (systems) {
                if (!systems.TryGetValue(hashCode, out system)) {

                }
            }
        }
    }

    internal EntitySystem? InternalCreateFromComponents(Dictionary<Type, object> components) {
        if (isDisposed)
            return null;

        EntitySystem newSystem = CreateFromComponents(components);
        newSystem.InternalInitialize(World);

        if (isDisposed) {
            newSystem.Dispose();
            return null;
        }
        return newSystem;
    }

    private void AssertIsNotDisposed() {
        if (isDisposed)
            throw new ObjectDisposedException(ToString(), "Affective system is disposed.");
    }

}
