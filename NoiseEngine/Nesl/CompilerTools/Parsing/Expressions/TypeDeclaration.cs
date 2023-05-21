﻿using NoiseEngine.Nesl.CompilerTools.Parsing.Tokens;
using NoiseEngine.Nesl.Emit;
using NoiseEngine.Nesl.Emit.Attributes;
using System.Diagnostics;

namespace NoiseEngine.Nesl.CompilerTools.Parsing.Expressions;

internal class TypeDeclaration : ParserExpressionContainer {

    public TypeDeclaration(Parser parser) : base(parser) {
    }

    [ParserExpression(ParserStep.TopLevel | ParserStep.Type)]
    [ParserExpressionParameter(ParserTokenType.Attributes)]
    [ParserExpressionParameter(ParserTokenType.AccessModifiers)]
    [ParserExpressionParameter(ParserTokenType.Modifiers)]
    [ParserExpressionParameter(ParserTokenType.TypeKind)]
    [ParserExpressionParameter(ParserTokenType.Name)]
    [ParserExpressionParameter(ParserTokenType.GenericDefine)]
    [ParserExpressionParameter(ParserTokenType.CurlyBrackets)]
    public void Define(
        AttributesToken attributes, AccessModifiersToken accessModifiers, ModifiersToken modifiers,
        TypeKindToken typeKind, NameToken name, GenericDefineToken genericParameters, CurlyBracketsToken codeBlock
    ) {
        string fullName = $"{Parser.GetNamespaceFromFilePath(name.Pointer.Path)}.{name.Name}";
        bool successful = true;
        if (!Assembly.TryDefineType(fullName, out NeslTypeBuilder? typeBuilder)) {
            Parser.Throw(new CompilationError(name.Pointer, CompilationErrorType.TypeAlreadyExists, fullName));
            successful = false;
        }

        if (!successful)
            return;
        if (typeBuilder is null)
            throw new UnreachableException();

        foreach (NeslAttribute attribute in attributes.Compile(Parser, AttributeTargets.Type))
            typeBuilder.AddAttribute(attribute);
        foreach (string genericParameterName in genericParameters.GenericParameters)
            typeBuilder.DefineGenericTypeParameter(genericParameterName);

        if (typeKind.TypeKind == NeslTypeKind.Struct)
            typeBuilder.AddAttribute(ValueTypeAttribute.Create());

        Parser.DefineType(typeBuilder!, codeBlock.Buffer);
    }

}
