using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// DM0021: a <c>[Mock]</c> parameter and a <c>[TestExport]</c> on the same method, both naming one
/// service.
/// </summary>
/// <remarks>
/// <para>
/// The generator has nothing to emit for a test method. This is here because the question is
/// decidable at compile time and the answer is otherwise a surprise at run time: the parameter wins,
/// so the <c>[TestExport]</c> beside it does nothing, and a test written that way reads as though it
/// should get the real implementation.
/// </para>
/// <para>
/// Only the same method. A <c>[TestExport]</c> on the class or the assembly is the default for
/// everything under it, and one test overriding that for one argument is what having both scopes is
/// for — reporting that would be reporting the feature.
/// </para>
/// </remarks>
internal static class TestAttributeDiagnostics {

    internal static void Setup(IncrementalGeneratorInitializationContext context) {
        var methods = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) =>
                    node is MethodDeclarationSyntax { AttributeLists.Count: > 0, ParameterList.Parameters.Count: > 0 },
                Read)
            .Where(static finding => finding != null)
            .Collect();

        // With the compilation, so the location carries its syntax tree and .editorconfig and
        // #pragma can reach this like any other code. Emits nothing, so re-running it per keystroke
        // costs a walk over findings that are almost always none.
        context.RegisterSourceOutput(methods.Combine(context.CompilationProvider), Report);
    }

    /// <summary>
    /// A method that carries both, and the service they disagree about.
    /// </summary>
    /// <remarks>
    /// The location is rendered to primitives like every other model's, so this can be cached
    /// without pinning a syntax tree. The finding is only produced for a method that already
    /// carries both attributes, which is rare enough that the common case allocates nothing.
    /// </remarks>
    private record Finding(string MethodName, string ServiceName, LocationModel Location);

    private static Finding? Read(GeneratorSyntaxContext syntaxContext, System.Threading.CancellationToken cancellationToken) {
        var context = (SyntaxTransformContext)syntaxContext;
        var method = (MethodDeclarationSyntax)context.Node;

        var exported = ExportedServices(method, context, cancellationToken);

        if (exported.Count == 0) {
            return null;
        }

        foreach (var parameter in method.ParameterList.Parameters) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CarriesMock(parameter, context, cancellationToken)) {
                continue;
            }

            var parameterType = parameter.Type?.GetTypeDefinition(context);

            if (parameterType == null) {
                continue;
            }

            foreach (var service in exported) {
                if (service.Equals(parameterType)) {
                    return new Finding(
                        method.Identifier.ToString(),
                        service.Name,
                        LocationModel.From(parameter));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The services this method's own <c>[TestExport]</c> attributes name.
    /// </summary>
    private static List<ITypeDefinition> ExportedServices(
        MethodDeclarationSyntax method,
        SyntaxTransformContext context,
        System.Threading.CancellationToken cancellationToken) {

        var services = new List<ITypeDefinition>();

        foreach (var attributeList in method.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                if (!AttributeTypeMatcher.Matches(
                        context.SemanticModel,
                        attribute,
                        KnownTypes.DependencyModules.Testing.TestExportAttribute,
                        cancellationToken)) {
                    continue;
                }

                // The service is the first positional argument: [TestExport(typeof(IFoo), ...)].
                var first = attribute.ArgumentList?.Arguments.FirstOrDefault(
                    argument => argument.NameEquals == null);

                if (first?.Expression is TypeOfExpressionSyntax typeOf &&
                    typeOf.Type.GetTypeDefinition(context) is { } service) {
                    services.Add(service);
                }
            }
        }

        return services;
    }

    private static bool CarriesMock(
        ParameterSyntax parameter,
        SyntaxTransformContext context,
        System.Threading.CancellationToken cancellationToken) {

        foreach (var attributeList in parameter.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                if (AttributeTypeMatcher.Matches(
                        context.SemanticModel,
                        attribute,
                        KnownTypes.DependencyModules.Testing.MockAttribute,
                        cancellationToken)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static void Report(
        SourceProductionContext context,
        (System.Collections.Immutable.ImmutableArray<Finding?> Findings, Compilation Compilation) data) {

        var lookup = new SyntaxTreeLookup(data.Compilation);

        foreach (var finding in data.Findings) {
            if (finding == null) {
                continue;
            }

            context.CancellationToken.ThrowIfCancellationRequested();

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DependencyModuleDiagnostics.MockAndTestExportOnOneMethod,
                    finding.Location.ToLocationOrNone(lookup),
                    finding.MethodName,
                    finding.ServiceName));
        }
    }
}
