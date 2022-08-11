﻿using NoiseEngine.Nesl.CompilerTools.Architectures.SpirV.Types;
using NoiseEngine.Nesl.Emit.Attributes;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoiseEngine.Nesl.CompilerTools.Architectures.SpirV;

internal class SpirVCompiler {

    private readonly ConcurrentDictionary<NeslType, Lazy<SpirVType>> types =
        new ConcurrentDictionary<NeslType, Lazy<SpirVType>>();
    private readonly ConcurrentDictionary<NeslField, Lazy<SpirVVariable>> variables =
        new ConcurrentDictionary<NeslField, Lazy<SpirVVariable>>();
    private readonly ConcurrentDictionary<NeslMethod, Lazy<SpirVFunction>> functions =
        new ConcurrentDictionary<NeslMethod, Lazy<SpirVFunction>>();

    private readonly ConcurrentBag<SpirVVariable> allVariables = new ConcurrentBag<SpirVVariable>();

    private uint nextId;

    internal IEnumerable<NeslEntryPoint> EntryPoints { get; }

    internal SpirVCompilationResultBuilder ResultBuilder { get; }
    internal SpirVJit Jit { get; }
    internal SpirVBuiltInTypes BuiltInTypes { get; }

    internal SpirVGenerator Header { get; }
    internal SpirVGenerator Annotations { get; }
    internal SpirVGenerator TypesAndVariables { get; }

    private SpirVCompiler(IEnumerable<NeslEntryPoint> entryPoints) {
        EntryPoints = entryPoints;

        ResultBuilder = new SpirVCompilationResultBuilder();
        Jit = new SpirVJit(this);
        BuiltInTypes = new SpirVBuiltInTypes(this);

        Header = new SpirVGenerator(this);
        Annotations = new SpirVGenerator(this);
        TypesAndVariables = new SpirVGenerator(this);
    }

    public static SpirVCompilationResult Compile(IEnumerable<NeslEntryPoint> entryPoints) {
        SpirVCompiler compiler = new SpirVCompiler(entryPoints);
        compiler.CompileWorker();
        return compiler.ResultBuilder.Build();
    }

    internal SpirVId GetNextId() {
        return new SpirVId(Interlocked.Increment(ref nextId));
    }

    internal void AddVariable(SpirVVariable variable) {
        allVariables.Add(variable);
    }

    internal SpirVVariable GetSpirVVariable(NeslField neslField) {
        return variables.GetOrAdd(neslField, _ => new Lazy<SpirVVariable>(() => {
            SpirVVariable variable = new SpirVVariable(this, neslField);
            AddVariable(variable);
            return variable;
        })).Value;
    }

    internal SpirVType GetSpirVType(NeslType? neslType) {
        if (neslType is null)
            return BuiltInTypes.GetOpTypeVoid();

        return types.GetOrAdd(neslType, _ => new Lazy<SpirVType>(() => {
            if (neslType.Attributes.TryCastAnyAttribute(
                out PlatformDependentTypeRepresentationAttribute? platformDependentTypeRepresentation
            )) {
                string? name = platformDependentTypeRepresentation.SpirVTargetName;
                if (name is null)
                    throw new InvalidOperationException();

                if (!BuiltInTypes.TryGetTypeByName(neslType, name, out SpirVType? type))
                    throw new InvalidOperationException();
                return type;
            }

            return new SpirVType(this, neslType);
        })).Value;
    }

    internal SpirVFunction GetSpirVFunction(NeslMethod neslMethod) {
        return functions.GetOrAdd(neslMethod,
            _ => new Lazy<SpirVFunction>(() => new SpirVFunction(this, neslMethod))).Value;
    }

    internal bool TryGetEntryPoint(NeslMethod neslMethod, [NotNullWhen(true)] out NeslEntryPoint entryPoint) {
        entryPoint = EntryPoints.FirstOrDefault(x => x.Method == neslMethod);
        return entryPoint != default;
    }

    private void CompileWorker() {
        Header.Emit(SpirVOpCode.OpCapability, (uint)Capability.Shader);
        Header.Emit(SpirVOpCode.OpMemoryModel, (uint)AddressingModel.Logical, (uint)MemoryModel.Glsl450);

        // Generate entry points.
        Parallel.ForEach(EntryPoints, x => GetSpirVFunction(x.Method));

        // Emit OpEntryPoint.
        foreach (NeslEntryPoint entryPoint in EntryPoints) {
            SpirVFunction function = GetSpirVFunction(entryPoint.Method);

            Header.Emit(
                SpirVOpCode.OpEntryPoint, (uint)entryPoint.ExecutionModel,
                function.Id, entryPoint.Method.Guid.ToString().ToSpirVLiteral(),
                allVariables.OrderBy(x => x.StorageClass).Select(x => x.Id).ToArray()
            );

            // TODO: add support for another execution modes.
            Header.Emit(SpirVOpCode.OpExecutionMode, function.Id, (uint)ExecutionMode.OriginUpperLeft);
        }

        // Emit OpExecutionMode.
        foreach (NeslEntryPoint entryPoint in EntryPoints) {
            SpirVFunction function = GetSpirVFunction(entryPoint.Method);

            Header.Emit(SpirVOpCode.OpExecutionMode, function.Id, (uint)ExecutionMode.OriginUpperLeft);
        }

        // Construct result.
        SpirVGenerator generator = new SpirVGenerator(this);
        FirstWords(generator);

        generator.Writer.WriteBytes(Header.Writer.AsSpan());
        generator.Writer.WriteBytes(Annotations.Writer.AsSpan());
        generator.Writer.WriteBytes(TypesAndVariables.Writer.AsSpan());

        foreach (Lazy<SpirVFunction> lazy in functions.Values)
            lazy.Value.Construct(generator);

        // Set bound.
        BinaryPrimitives.WriteUInt32LittleEndian(generator.Writer.AsSpan(12), GetNextId().RawId);

        ResultBuilder.Code = generator.Writer.ToArray();
    }

    /// <summary>
    /// https://registry.khronos.org/SPIR-V/specs/1.2/SPIRV.html#_a_id_physicallayout_a_physical_layout_of_a_spir_v_module_and_instruction
    /// </summary>
    private void FirstWords(SpirVGenerator generator) {
        generator.Writer.WriteUInt32(0x07230203); // Magic number.
        generator.Writer.WriteUInt32(0x00010300); // Version number.
        generator.Writer.WriteUInt32(0x1ec5712d); // Generator's magic number.
        generator.Writer.WriteUInt32(0x0); // Bound space.
        generator.Writer.WriteUInt32(0x0);
    }

}