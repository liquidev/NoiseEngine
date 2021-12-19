﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NoiseEngine.Jobs {
    public class EntityWorld {

        private static uint nextId = 0;

        private readonly ConcurrentList<EntitySystemBase> systems = new ConcurrentList<EntitySystemBase>();
        private readonly ConcurrentList<EntityQueryBase> queries = new ConcurrentList<EntityQueryBase>();
        private readonly ConcurrentDictionary<Type, IList> typeToSystems = new ConcurrentDictionary<Type, IList>();
        private readonly List<EntityGroup> groups = new List<EntityGroup>();
        private readonly Dictionary<int, EntityGroup> idToGroup = new Dictionary<int, EntityGroup>();
        private readonly ConcurrentDictionary<Entity, EntityGroup> entityToGroup = new ConcurrentDictionary<Entity, EntityGroup>();

        private ulong nextEntityId = 1;

        internal static EntityWorld Empty { get; } = new EntityWorld();

        internal ComponentsStorage<Entity> ComponentsStorage { get; } = new ComponentsStorage<Entity>();

        public uint Id { get; }

        public EntityWorld() {
            Id = Interlocked.Increment(ref nextId);
        }

        /// <summary>
        /// Creates new entity in this entity world
        /// </summary>
        /// <returns><see cref="Entity"/></returns>
        public Entity NewEntity() {
            Entity entity = NewEntityWorker();
            AddNewEntityToGroup(entity, new List<Type>());
            return entity;
        }

        /// <summary>
        /// Creates new entity in this entity world
        /// </summary>
        /// <typeparam name="T">Struct inheriting from <see cref="IEntityComponent"/></typeparam>
        /// <param name="component">Component being added</param>
        /// <returns><see cref="Entity"/></returns>
        public Entity NewEntity<T>(T component) where T : struct, IEntityComponent {
            Entity entity = NewEntityWorker();

            ComponentsStorage.AddComponent(entity, component);

            AddNewEntityToGroup(entity, new List<Type>() {
                typeof(T)
            });

            return entity;
        }

        /// <summary>
        /// Creates new entity in this entity world
        /// </summary>
        /// <typeparam name="T1">Struct inheriting from <see cref="IEntityComponent"/></typeparam>
        /// <typeparam name="T2">Struct inheriting from <see cref="IEntityComponent"/></typeparam>
        /// <param name="component1">Component being added</param>
        /// <param name="component2">Component being added</param>
        /// <returns><see cref="Entity"/></returns>
        public Entity NewEntity<T1, T2>(T1 component1, T2 component2)
            where T1 : struct, IEntityComponent
            where T2 : struct, IEntityComponent
        {
            Entity entity = NewEntityWorker();

            ComponentsStorage.AddComponent(entity, component1);
            ComponentsStorage.AddComponent(entity, component2);

            AddNewEntityToGroup(entity, new List<Type>() {
                typeof(T1), typeof(T2)
            });

            return entity;
        }

        /// <summary>
        /// Adds T system to this world
        /// </summary>
        /// <typeparam name="T">Entity system type</typeparam>
        /// <param name="system">Entity system object</param>
        /// <param name="cycleTime">Duration in miliseconds of the system execution cycle by schedule. When null, the schedule is not used.</param>
        /// <param name="schedule"><see cref="EntitySchedule"/> managing this system. When null is used <see cref="EntitySchedule.Instance"/>.</param>
        /// <exception cref="InvalidOperationException">Entity world already contains this <see cref="EntitySystemBase"/></exception>
        public void AddSystem<T>(T system, double? cycleTime = null, EntitySchedule? schedule = null) where T : EntitySystemBase {
            if (HasSystem(system))
                throw new InvalidOperationException($"Entity world already contains this {nameof(T)}.");

            systems.Add(system);
            typeToSystems.GetOrAdd(typeof(T), (Type type) => {
                return new ConcurrentList<T>();
            }).Add(system);

            system.InternalInitialize(this, schedule ?? EntitySchedule.Instance!);
            system.InternalStart();
            system.CycleTime = cycleTime;
        }

        /// <summary>
        /// Removes T system from this world
        /// </summary>
        /// <typeparam name="T">Entity system</typeparam>
        public void RemoveSystem<T>(T system) where T : EntitySystemBase {
            if (!HasSystem(system))
                return;

            systems.Remove(system);
            if (typeToSystems.TryGetValue(typeof(T), out IList? list))
                list.Remove(system);
        }

        /// <summary>
        /// Checks if this entity world has T system
        /// </summary>
        /// <typeparam name="T">Entity system</typeparam>
        /// <returns>True when this entity world contains T system or false when not</returns>
        public bool HasSystem<T>(T system) where T : EntitySystemBase {
            return systems.Contains(system);
        }

        /// <summary>
        /// Checks if this entity world has any T type system
        /// </summary>
        /// <typeparam name="T">Entity system</typeparam>
        /// <returns>True when this entity world contains T system or false when not</returns>
        public bool HasAnySystem<T>() where T : EntitySystemBase {
            return typeToSystems.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Returns first assigned T entity system
        /// </summary>
        /// <typeparam name="T">Entity system</typeparam>
        /// <returns><see cref="EntitySystemBase"/></returns>
        public T GetSystem<T>() where T : EntitySystemBase {
            return (T)typeToSystems[typeof(T)][0]!;
        }

        /// <summary>
        /// Returns T entity systems
        /// </summary>
        /// <typeparam name="T"><see cref="EntitySystemBase"/> type</typeparam>
        /// <returns>Array of <see cref="EntitySystemBase"/></returns>
        public T[] GetSystems<T>() where T : EntitySystemBase {
            return ((ConcurrentList<T>)typeToSystems[typeof(T)]).ToArray();
        }

        internal void AddQuery(EntityQueryBase query) {
            queries.Add(query);
        }

        internal void RemoveQuery(EntityQueryBase query) {
            queries.Add(query);
        }

        internal void RegisterGroupsToQuery(EntityQueryBase query) {
            query.groups.WriteWork(() => {
                query.groups.Clear();
                lock (groups) {
                    for (int i = 0; i < groups.Count; i++)
                        query.RegisterGroup(groups[i]);
                }
            });
        }

        internal EntityGroup GetGroupFromComponents(List<Type> components) {
            int hashCode = 0;
            components = components.OrderBy(t => t.GetHashCode()).ToList();
            for (int i = 0; i < components.Count; i++)
                hashCode ^= components[i].GetHashCode();

            EntityGroup? group;
            while (idToGroup.TryGetValue(hashCode, out group) && !group.CompareSortedComponents(components))
                hashCode++;

            if (group == null) {
                lock (idToGroup) {
                    group = new EntityGroup(hashCode, this, components);
                    idToGroup.Add(hashCode, group);
                }
                lock (groups)
                    groups.Add(group);

                foreach (EntityQueryBase query in queries)
                    query.RegisterGroup(group);
            }

            return group;
        }

        internal EntityGroup GetEntityGroup(Entity entity) {
            try {
                return entityToGroup[entity];
            } catch (KeyNotFoundException) {
                throw new InvalidOperationException($"{entity} was destroyed.");
            }
        }

        internal void SetEntityGroup(Entity entity, EntityGroup group) {
            entityToGroup[entity] = group;
        }

        internal bool IsEntityDestroyed(Entity entity) {
            return !entityToGroup.ContainsKey(entity);
        }

        internal void DestroyEntity(Entity entity) {
            if (entityToGroup.TryRemove(entity, out EntityGroup? group))
                group.RemoveEntity(entity);
        }
      
        private Entity NewEntityWorker() {
            return new Entity(Interlocked.Increment(ref nextEntityId));
        }

        private void AddNewEntityToGroup(Entity entity, List<Type> componentTypes) {
            EntityGroup group = GetGroupFromComponents(componentTypes);
            entityToGroup.GetOrAdd(entity, group);
            group.AddEntity(entity);
        }

    }
}
