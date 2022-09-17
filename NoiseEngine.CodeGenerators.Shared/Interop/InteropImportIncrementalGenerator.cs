﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace NoiseEngine.CodeGenerators.Shared.Interop;

[Generator]
public class InteropImportIncrementalGenerator : IIncrementalGenerator {

    private const string DllName = "NoiseEngine.Native";
    private const string AttributeFullName = "NoiseEngine.Interop.InteropImportAttribute";

    private static readonly Dictionary<string, InteropMarshal> marshalls = new Dictionary<string, InteropMarshal>();

    static InteropImportIncrementalGenerator() {
        foreach (
            Type type in typeof(InteropImportIncrementalGenerator).Assembly.GetTypes()
            .Where(x => typeof(InteropMarshal).IsAssignableFrom(x) && !x.IsAbstract)
        ) {
            InteropMarshal marshal = (InteropMarshal)Activator.CreateInstance(type)
                ?? throw new NullReferenceException();
            marshalls.Add(marshal.MarshallingType, marshal);
        }
    }

    private static string SplitWithGenerics(string fullName, out string genericRawString) {
        int index = fullName.IndexOf('<');

        if (index == -1) {
            genericRawString = string.Empty;
            return fullName;
        }

        genericRawString = fullName.Substring(index + 1, fullName.Length - index - 2);
        return fullName.Substring(0, index);
    }

    private static string CombineWithGenerics(string name, string genericRawString) {
        return $"{name}<{genericRawString}>";
    }

    private static void AddModifiers(StringBuilder builder, SyntaxTokenList modifiers, string? additional) {
        const string Partial = "partial";

        bool has = false;
        foreach (SyntaxToken modifier in modifiers) {
            if (modifier.ValueText == Partial && !has) {
                if (additional is not null)
                    builder.Append(additional).Append(' ');
                has = true;
            } else {
                has = has || modifier.ValueText == additional;
            }

            builder.Append(modifier.ValueText).Append(' ');
        }

        if (!has && additional is not null)
            builder.Append(additional).Append(' ');
    }

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValuesProvider<(MethodDeclarationSyntax, AttributeSyntax)> methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (x, _) => x is MethodDeclarationSyntax m && m.AttributeLists.Any(),
                static (context, _) => {
                    MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
                    foreach (AttributeSyntax attribute in method.AttributeLists.SelectMany(x => x.Attributes)) {
                        if (
                            context.SemanticModel.GetSymbolInfo(attribute).Symbol
                            is not IMethodSymbol attributeSymbol
                        ) {
                            continue;
                        }

                        if (attributeSymbol.ContainingType.ToDisplayString() == AttributeFullName)
                            return (method, attribute);
                    }

                    return (method, null!);
                }
            ).Where(static x => x.attribute is not null);

        IncrementalValueProvider<(Compilation, ImmutableArray<(MethodDeclarationSyntax, AttributeSyntax)>)>
            compilationAndMethods = context.CompilationProvider.Combine(methods.Collect());

        context.RegisterSourceOutput(compilationAndMethods, (ctx, source) => {
            if (source.Item2.IsDefaultOrEmpty)
                return;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("// <auto-generated />");
            builder.AppendLine();

            foreach ((MethodDeclarationSyntax method, AttributeSyntax attribute) in source.Item2)
                GenerateExtension(builder, source.Item1, method, attribute);

            ctx.AddSource("InteropImport.generated.cs", builder.ToString());
        });
    }

    private void GenerateExtension(
        StringBuilder builder, Compilation compilation, MethodDeclarationSyntax method, AttributeSyntax attribute
    ) {
        // Create method body.
        StringBuilder body = new StringBuilder();
        StringBuilder advancedBody = new StringBuilder(InteropMarshal.MarshalContinuation);
        StringBuilder outputBody = new StringBuilder();
        List<MarshalParameter> parameters = new List<MarshalParameter>();
        List<MarshalOutput> outputs = new List<MarshalOutput>();

        foreach (ParameterSyntax parameter in method.ParameterList.Parameters) {
            string typeFullName = parameter.Type!.GetSymbol<INamedTypeSymbol>(compilation).ToDisplayString();
            string typeName = SplitWithGenerics(typeFullName, out string genericRawString);

            if (!marshalls.TryGetValue(typeName, out InteropMarshal? marshal)) {
                parameters.Add(new MarshalParameter(parameter.Identifier.ValueText, typeFullName));
                continue;
            }

            marshal.SetGenericRawString(genericRawString);
            string a = marshal.Marshall(parameter.Identifier.ValueText, out string marshalledParameterName);
            marshal.SetGenericRawString(string.Empty);

            parameters.Add(new MarshalParameter(
                marshalledParameterName, CombineWithGenerics(marshal.UnmarshallingType, genericRawString)
            ));

            if (marshal.IsAdvanced)
                advancedBody.Replace(InteropMarshal.MarshalContinuation, a);
            else
                body.AppendLine(a);
        }

        // TODO: add out values.
        foreach (TypeSyntax typeSyntax in new TypeSyntax[] { method.ReturnType }) {
            string typeFullName = typeSyntax.GetSymbol<INamedTypeSymbol>(compilation).ToDisplayString();
            string typeName = SplitWithGenerics(typeFullName, out string genericRawString);

            string b = InteropMarshal.CreateUniqueVariableName();
            if (!marshalls.TryGetValue(typeName, out InteropMarshal? marshal)) {
                outputs.Add(new MarshalOutput(b, b, typeFullName));
                continue;
            }

            marshal.SetGenericRawString(genericRawString);
            outputBody.AppendLine(marshal.Unmarshall(b, out string unmarshaledParamterName));
            marshal.SetGenericRawString(string.Empty);

            outputs.Add(new MarshalOutput(
                unmarshaledParamterName, b, CombineWithGenerics(marshal.UnmarshallingType, genericRawString)
            ));
        }

        bool hasBody = body.Length > 0 || advancedBody.ToString() != InteropMarshal.MarshalContinuation;

        // Namespace declaration.
        builder.Append("namespace ").Append(method.ParentNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>().First().Name.GetText()).AppendLine(" {");

        // Type declaration.
        TypeDeclarationSyntax type = method.ParentNodes().OfType<TypeDeclarationSyntax>().First();

        builder.AppendIndentation();
        AddModifiers(builder, type.Modifiers, "unsafe");

        if (type is ClassDeclarationSyntax)
            builder.Append("class ");
        else if (type is StructDeclarationSyntax)
            builder.Append("struct ");
        else if (type is InterfaceDeclarationSyntax)
            builder.Append("interface ");
        else if (type is RecordDeclarationSyntax)
            builder.Append("record ");
        else
            builder.Append("record struct ");

        builder.Append(type.Identifier.Text).AppendLine(" {");

        // Method declaration.
        int attributeIndex = builder.Length - 1;

        builder.AppendIndentation(2);
        AddModifiers(builder, method.Modifiers, hasBody ? null : "extern");

        string returnTypeName = method.ReturnType.GetSymbol<INamedTypeSymbol>(compilation).ToDisplayString();
        builder.Append(returnTypeName).Append(' ');
        builder.Append(method.Identifier.ValueText);
        builder.Append('(');

        foreach (ParameterSyntax parameter in method.ParameterList.Parameters) {
            builder.Append(parameter.Type!.GetSymbol<INamedTypeSymbol>(compilation).ToDisplayString()).Append(' ');
            builder.Append(parameter.Identifier.ValueText);
            builder.Append(", ");
        }

        if (method.ParameterList.Parameters.Count > 0)
            builder.Remove(builder.Length - 2, 2);

        builder.Append(')');

        // Create DllImportAttribute.
        StringBuilder dllImport = new StringBuilder("[System.Runtime.InteropServices.DllImportAttribute(");
        dllImport.Append(attribute.ArgumentList!.Arguments.Count == 2 ?
            attribute.ArgumentList.Arguments[1] : $"\"{DllName}\"");
        dllImport.Append(", EntryPoint = ");
        dllImport.Append(attribute.ArgumentList.Arguments[0]);
        dllImport.Append(", ExactSpelling = true)]");

        // Construct final method body.
        if (hasBody) {
            builder.AppendLine(" {");

            // Create __PInvoke method.
            builder.AppendIndentation(3).Append(dllImport).AppendLine();
            builder.AppendIndentation(3).Append("static extern unsafe ");
            builder.Append(outputs[0].UnmarshalledType);
            builder.Append(" __PInvoke(");

            int i = 0;
            foreach (MarshalParameter parameter in parameters) {
                builder.Append(parameter.MarshalledType);
                builder.Append(" v");
                builder.Append(i++);
                builder.Append(", ");
            }

            if (parameters.Count > 0)
                builder.Remove(builder.Length - 2, 2);

            builder.AppendLine(");");

            // Append body.
            builder.Append(body).AppendLine();

            // Declare output variables.
            body.Clear();

            bool returnTypeIsNotVoid = returnTypeName != "void";
            if (returnTypeIsNotVoid) {
                MarshalOutput returnInfo = outputs[0];

                body.AppendIndentation(3).Append(returnInfo.UnmarshalledType).Append(' ');
                body.Append(returnInfo.MarshalledParameterName).Append(';').AppendLine();
            }

            builder.Append(body);

            // Add PInvoke execution.
            body.Clear();

            if (returnTypeIsNotVoid) {
                MarshalOutput returnInfo = outputs[0];
                body.Append(returnInfo.MarshalledParameterName).Append(" = ");
            }

            body.Append("__PInvoke(");

            foreach (MarshalParameter parameter in parameters) {
                body.Append(parameter.MarshalledParameterName);
                body.Append(", ");
            }

            if (parameters.Count > 0)
                body.Remove(body.Length - 2, 2);

            body.Append(");");

            advancedBody.Replace(InteropMarshal.MarshalContinuation, body.ToString());
            builder.Append(advancedBody).AppendLine();
            builder.Append(outputBody);

            // Collect output.
            for (i = 1; i < outputs.Count; i++) {
                MarshalOutput returnInfo = outputs[i];

                builder.Append(returnInfo.UnmarshalledParameterName);
                builder.Append(" = ");
                builder.Append(returnInfo.MarshalledParameterName).Append(';').AppendLine();
            }

            if (returnTypeIsNotVoid) {
                MarshalOutput returnInfo = outputs[0];

                builder.AppendIndentation(3).Append("return ");
                builder.Append(returnInfo.UnmarshalledParameterName).Append(';').AppendLine();
            }

            builder.AppendIndentation(2).Append('}').AppendLine();
        } else {
            builder.Insert(attributeIndex, GeneratorConstants.Indentation + GeneratorConstants.Indentation + dllImport);
            builder.Append(';').AppendLine();
        }

        builder.AppendIndentation().Append('}').AppendLine();
        builder.AppendLine("}");
    }

}
