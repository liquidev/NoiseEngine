﻿using NoiseEngine.Nesl.CompilerTools;
using NoiseEngine.Nesl.Emit.Attributes;
using System.Collections.Generic;
using System.Linq;

namespace NoiseEngine.Nesl;

public abstract class NeslMethod {

    public abstract IEnumerable<NeslAttribute> Attributes { get; }

    protected abstract IlContainer IlContainer { get; }

    public NeslType Type { get; }
    public string Name { get; }
    public NeslType? ReturnType { get; }
    public IReadOnlyList<NeslType> ParameterTypes { get; }

    public bool IsStatic => Attributes.HasAnyAttribute(nameof(StaticAttribute));

    protected NeslMethod(NeslType type, string name, NeslType? returnType, NeslType[] parameterTypes) {
        Type = type;
        Name = name;
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }

    internal IEnumerable<Instruction> GetInstructions() {
        return IlContainer.GetInstructions();
    }

}
