﻿using NoiseEngine.Nesl.CompilerTools.Parsing.Tokens;
using NoiseEngine.Nesl.Emit;
using NoiseEngine.Nesl.Emit.Attributes;
using System.Diagnostics;

namespace NoiseEngine.Nesl.CompilerTools.Parsing.Expressions;

internal class TypeDeclaration : ParserExpressionContainer {

    public TypeDeclaration(Parser parser) : base(parser) {
    }

    [ParserExpression(ParserStep.TopLevel | ParserStep.Type)]
    [ParserExpressionParameter(ParserTokenType.Modifiers)]
    [ParserExpressionParameter(ParserTokenType.TypeKind)]
    [ParserExpressionParameter(ParserTokenType.Name)]
    [ParserExpressionParameter(ParserTokenType.CurlyBrackets)]
    public void Define(
        ModifiersToken modifiers, TypeKindToken typeKind, NameToken name, CurlyBracketsToken codeBlock
    ) {
        bool successful = true;
        if (!Assembly.TryDefineType(name.Name, out NeslTypeBuilder? typeBuilder)) {
            Parser.Throw(new CompilationError(name.Pointer, CompilationErrorType.TypeAlreadyExists, name.Name));
            successful = false;
        }

        if (!successful)
            return;
        if (typeBuilder is null)
            throw new UnreachableException();

        if (typeKind.TypeKind == NeslTypeKind.Struct)
            typeBuilder.AddAttribute(ValueTypeAttribute.Create());

        Parser.DefineType(typeBuilder!, codeBlock.Buffer);
    }

}