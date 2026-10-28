using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyModules.SourceGenerator;

/// <summary>
/// DM0026: a <c>[SingletonService]</c>, <c>[ScopedService]</c> or <c>[TransientService]</c> that
/// does not name its service type with <c>As = typeof(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The attribute is matched by symbol, so an alias and a qualified name are found. <c>As</c> is
/// matched by its written argument name, the way the generator reads it, so the two agree on which
/// attributes have one. An <c>As</c> that is not a <c>typeof(...)</c> is reported, because the
/// generator ignores it and infers the type.
/// </para>
/// <para>
/// The type the generator infers is computed with <see cref="ServiceTypeInference"/> and passed to
/// the code fix in <see cref="ServiceTypeProperty"/>. It is passed only when <c>typeof</c> of it,
/// bound at the attribute, reads back as the same type through the generator's <c>As</c> path. A
/// type that cannot be written there, such as <c>IMap&lt;,int&gt;</c>, gets no fix.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ServiceTypeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The inferred type, written with <c>global::</c>, when the code fix can write it.
    /// </summary>
    public const string ServiceTypeProperty = "ServiceType";

    /// <summary>
    /// The inferred type as the code fix title shows it.
    /// </summary>
    public const string ServiceTypeDisplayProperty = "ServiceTypeDisplay";

    private static readonly ITypeDefinition[] _attributeTypes =
    {
        KnownTypes.DependencyModules.Attributes.TransientServiceAttribute,
        KnownTypes.DependencyModules.Attributes.ScopedServiceAttribute,
        KnownTypes.DependencyModules.Attributes.SingletonServiceAttribute,
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DependencyModuleDiagnostics.ServiceTypeNotNamed);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var attributeTypes = _attributeTypes
                .Select(type =>
                    start.Compilation.GetTypeByMetadataName($"{type.Namespace}.{type.Name}")
                )
                .Where(type => type != null)
                .ToImmutableArray();

            if (attributeTypes.IsEmpty)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                nodeContext => Analyze(nodeContext, attributeTypes!),
                SyntaxKind.ClassDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.MethodDeclaration
            );
        });
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<INamedTypeSymbol> attributeTypes
    )
    {
        var member = (MemberDeclarationSyntax)context.Node;

        foreach (var attribute in member.AttributeLists.SelectMany(list => list.Attributes))
        {
            var attributeType = context
                .SemanticModel.GetSymbolInfo(attribute, context.CancellationToken)
                .Symbol?.ContainingType;

            if (
                attributeType == null
                || !attributeTypes.Contains(attributeType, SymbolEqualityComparer.Default)
            )
            {
                continue;
            }

            var asArgument = attribute.ArgumentList?.Arguments.FirstOrDefault(argument =>
                argument.NameEquals?.Name.ToString() == "As"
            );

            if (asArgument?.Expression is TypeOfExpressionSyntax)
            {
                continue;
            }

            Report(context, member, attribute, asArgument != null);
        }
    }

    private static void Report(
        SyntaxNodeAnalysisContext context,
        MemberDeclarationSyntax member,
        AttributeSyntax attribute,
        bool hasInvalidAs
    )
    {
        var serviceType = InferredServiceType(member, context.SemanticModel);
        var properties = ImmutableDictionary<string, string?>.Empty;
        var detail = hasInvalidAs
            ? "The generator ignores an As that is not typeof(...), so the service type is inferred. "
            : "";

        if (serviceType == null)
        {
            detail += "The service type cannot be determined";
        }
        else
        {
            var display = serviceType.GetShortName();
            detail += $"It is registered as '{display}'";

            if (WritableServiceType(attribute, serviceType, context.SemanticModel) is { } written)
            {
                properties = properties
                    .Add(ServiceTypeProperty, written)
                    .Add(ServiceTypeDisplayProperty, display);
            }
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                DependencyModuleDiagnostics.ServiceTypeNotNamed,
                attribute.GetLocation(),
                properties,
                attribute.Name.ToString(),
                MemberName(member),
                detail
            )
        );
    }

    /// <summary>
    /// What the generator registers for this declaration when the attribute has no <c>As</c>.
    /// </summary>
    private static ITypeDefinition? InferredServiceType(
        MemberDeclarationSyntax member,
        SemanticModel semanticModel
    )
    {
        switch (member)
        {
            case TypeDeclarationSyntax type:
                return ServiceTypeInference.GetServiceType(
                    ServiceModelUtility.GetTypeDeclarationDefinition(type),
                    ServiceTypeInference.BaseListSymbols(type, semanticModel)
                );

            case MethodDeclarationSyntax method:
                var returnType = method.ReturnType.GetTypeDefinition(
                    new SyntaxTransformContext(method, semanticModel)
                );

                return returnType == null
                    ? null
                    : ServiceTypeInference.GetServiceType(
                        returnType,
                        Enumerable.Empty<INamedTypeSymbol>()
                    );

            default:
                return null;
        }
    }

    /// <summary>
    /// <paramref name="serviceType"/> written with <c>global::</c>, when <c>typeof</c> of that text
    /// at the attribute gives the generator the same service type. Otherwise null.
    /// </summary>
    private static string? WritableServiceType(
        AttributeSyntax attribute,
        ITypeDefinition serviceType,
        SemanticModel semanticModel
    )
    {
        // A type parameter of the class binds at the attribute but is CS0416 inside typeof there.
        if (ContainsTypeParameter(serviceType))
        {
            return null;
        }

        var builder = new StringBuilder();
        serviceType.WriteTypeName(builder, TypeOutputMode.Global);
        var written = builder.ToString();

        var typeSyntax = SyntaxFactory.ParseTypeName(written);

        if (typeSyntax.ContainsDiagnostics)
        {
            return null;
        }

        var speculative = SyntaxFactory.Attribute(
            attribute.Name,
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.NameEquals("As"),
                        null,
                        SyntaxFactory.TypeOfExpression(typeSyntax)
                    )
                )
            )
        );

        if (
            !semanticModel.TryGetSpeculativeSemanticModel(
                attribute.SpanStart,
                speculative,
                out var speculativeModel
            )
        )
        {
            return null;
        }

        var typeOf = (TypeOfExpressionSyntax)speculative.ArgumentList!.Arguments[0].Expression;
        var readBack = typeOf.Type.GetTypeDefinition(
            new SyntaxTransformContext(speculative, speculativeModel)
        );

        if (readBack is GenericTypeDefinition)
        {
            readBack = ServiceTypeInference.ReplaceGenericParametersForRegistration(readBack);
        }

        return serviceType.Equals(readBack) ? written : null;
    }

    private static bool ContainsTypeParameter(ITypeDefinition type) =>
        type is TypeParameterDefinition || type.TypeArguments.Any(ContainsTypeParameter);

    private static string MemberName(MemberDeclarationSyntax member) =>
        member switch
        {
            TypeDeclarationSyntax type => type.Identifier.Text,
            MethodDeclarationSyntax method => method.Identifier.Text,
            _ => member.ToString(),
        };
}
