﻿using NoiseEngine.Nesl.Emit;
using System;
using System.Buffers.Binary;

namespace NoiseEngine.Nesl.Runtime;

internal readonly struct Instruction {

    private readonly IlContainer container;
    private readonly uint tailIndex;

    public OpCode OpCode { get; }

    internal Instruction(OpCode opCode, uint tailIndex, IlContainer container) {
        OpCode = opCode;
        this.tailIndex = tailIndex;
        this.container = container;
    }

    public ulong ReadUInt64() {
        return BinaryPrimitives.ReadUInt64BigEndian(GetTail());
    }

    private ReadOnlySpan<byte> GetTail() {
        return container.GetTail((int)tailIndex);
    }

}
