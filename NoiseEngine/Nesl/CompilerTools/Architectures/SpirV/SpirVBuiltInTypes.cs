﻿using NoiseEngine.Collections;
using NoiseEngine.Nesl.CompilerTools.Architectures.SpirV.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NoiseEngine.Nesl.CompilerTools.Architectures.SpirV;

internal class SpirVBuiltInTypes {

    private readonly ConcurrentDictionary<object[], Lazy<SpirVType>> types =
        new ConcurrentDictionary<object[], Lazy<SpirVType>>(new ReadOnlyListEqualityComparer<object>());

    public SpirVCompiler Compiler { get; }

    public SpirVBuiltInTypes(SpirVCompiler compiler) {
        Compiler = compiler;
    }

    public bool TryGetTypeByName(NeslType neslType, string name, [NotNullWhen(true)] out SpirVType? type) {
        string[] args = name.Split('`');

        switch (args[0]) {
            case nameof(SpirVOpCode.OpTypeVoid):
                type = GetOpTypeVoid();
                return true;
            case nameof(SpirVOpCode.OpTypeFloat):
                type = GetOpTypeFloat(ulong.Parse(args[1]));
                return true;
            case nameof(SpirVOpCode.OpTypeVector):
                type = GetOpTypeVector(neslType.Assembly.GetType(args[1])!, uint.Parse(args[2]));
                return true;
            default:
                type = null;
                return false;
        }
    }

    public SpirVType GetOpTypeVoid() {
        return types.GetOrAdd(new object[] { SpirVOpCode.OpTypeVoid }, _ => new Lazy<SpirVType>(() => {
            lock (Compiler.TypesAndVariables) {
                SpirVId id = Compiler.GetNextId();
                Compiler.TypesAndVariables.Emit(SpirVOpCode.OpTypeVoid, id);
                return new SpirVType(Compiler, id);
            }
        })).Value;
    }

    public SpirVType GetOpTypeInt(ulong size, bool signed) {
        return types.GetOrAdd(new object[] { SpirVOpCode.OpTypeInt, size, signed }, _ => new Lazy<SpirVType>(() => {
            lock (Compiler.TypesAndVariables) {
                SpirVId id = Compiler.GetNextId();
                Compiler.TypesAndVariables.Emit(
                    SpirVOpCode.OpTypeInt, id, ((uint)size).ToSpirVLiteral(), (signed ? 1u : 0u).ToSpirVLiteral()
                );
                return new SpirVType(Compiler, id);
            }
        })).Value;
    }

    public SpirVType GetOpTypeFloat(ulong size) {
        return types.GetOrAdd(new object[] { SpirVOpCode.OpTypeFloat, size }, _ => new Lazy<SpirVType>(() => {
            lock (Compiler.TypesAndVariables) {
                SpirVId id = Compiler.GetNextId();
                Compiler.TypesAndVariables.Emit(SpirVOpCode.OpTypeFloat, id, ((uint)size).ToSpirVLiteral());
                return new SpirVType(Compiler, id);
            }
        })).Value;
    }

    public SpirVType GetOpTypeVector(NeslType neslType, uint size) {
        return types.GetOrAdd(new object[] { SpirVOpCode.OpTypeVector, neslType, size },
            _ => new Lazy<SpirVType>(() => {
                lock (Compiler.TypesAndVariables) {
                    SpirVId id = Compiler.GetNextId();

                    Compiler.TypesAndVariables.Emit(
                        SpirVOpCode.OpTypeVector, id, Compiler.GetSpirVType(neslType).Id,
                        size.ToSpirVLiteral()
                    );

                    return new SpirVType(Compiler, id);
                }
            })).Value;
    }

    public SpirVType GetOpTypePointer(StorageClass storageClass, SpirVType type) {
        return types.GetOrAdd(new object[] { storageClass, type }, _ => new Lazy<SpirVType>(() => {
            lock (Compiler.TypesAndVariables) {
                SpirVId id = Compiler.GetNextId();
                Compiler.TypesAndVariables.Emit(SpirVOpCode.OpTypePointer, id, (uint)storageClass, type.Id);
                return new SpirVType(Compiler, id);
            }
        })).Value;
    }

    public SpirVType GetOpTypeFunction(SpirVType returnType, params SpirVType[] parameters) {
        object[] objects = new object[2 + parameters.Length];

        objects[0] = SpirVOpCode.OpTypeFunction;
        objects[1] = returnType;

        for (int i = 0; i < parameters.Length; i++)
            objects[2 + i] = parameters[i];

        return types.GetOrAdd(objects, _ => new Lazy<SpirVType>(() => {
            lock (Compiler.TypesAndVariables) {
                Span<SpirVId> parameterIds = stackalloc SpirVId[parameters.Length];
                for (int i = 0; i < parameterIds.Length; i++)
                    parameterIds[i] = parameters[i].Id;

                SpirVId id = Compiler.GetNextId();
                Compiler.TypesAndVariables.Emit(SpirVOpCode.OpTypeFunction, id, returnType.Id, parameterIds);
                return new SpirVType(Compiler, id);
            }
        })).Value;
    }

}
