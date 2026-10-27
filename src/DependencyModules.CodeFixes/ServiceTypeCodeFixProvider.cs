using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace DependencyModules.CodeFixes;

/// <summary>
/// Fixes DM0026 by writing <c>As = typeof(...)</c> with the type the generator infers today.
/// </summary>
/// <remarks>
/// <para>
/// The type comes from the diagnostic's properties, where <c>ServiceTypeAnalyzer</c> put what
/// <c>ServiceTypeInference</c> returned. This assembly does not compute it, so the fix cannot
/// drift from what the generator registers. It also does not reference the generator assembly,
/// so the IDE loads it with nothing beside it to resolve. A diagnostic without the property has no
/// fix.
/// </para>
/// <para>
/// Only the attribute's argument list changes. Other arguments and their trivia stay, and an
/// <c>As</c> that is not a <c>typeof(...)</c> has its expression replaced in place.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ServiceTypeCodeFixProvider)), Shared]
public sealed class ServiceTypeCodeFixProvider : CodeFixProvider
{
    // These match DependencyModuleDiagnostics.ServiceTypeNotNamed and the ServiceTypeAnalyzer
    // property names in the generator assembly.
    private const string DiagnosticId = "DM0026";
    private const string ServiceTypeProperty = "ServiceType";
    private const string ServiceTypeDisplayProperty = "ServiceTypeDisplay";

    private const string EquivalenceKey = "DM0026.MakeServiceTypeExplicit";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context
            .Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root == null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (
                !diagnostic.Properties.TryGetValue(ServiceTypeProperty, out var serviceType)
                || serviceType == null
                || root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    ?.FirstAncestorOrSelf<AttributeSyntax>()
                    is not { } attribute
            )
            {
                continue;
            }

            diagnostic.Properties.TryGetValue(ServiceTypeDisplayProperty, out var display);

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Make service type explicit: {display ?? serviceType}",
                    cancellationToken =>
                        FixAsync(context.Document, attribute, serviceType, cancellationToken),
                    EquivalenceKey
                ),
                diagnostic
            );
        }
    }

    private static async Task<Document> FixAsync(
        Document document,
        AttributeSyntax attribute,
        string serviceType,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        var typeOf = SyntaxFactory.TypeOfExpression(
            SyntaxFactory
                .ParseTypeName(serviceType)
                .WithAdditionalAnnotations(Simplifier.Annotation)
        );

        return document.WithSyntaxRoot(root!.ReplaceNode(attribute, WithAs(attribute, typeOf)));
    }

    private static AttributeSyntax WithAs(AttributeSyntax attribute, TypeOfExpressionSyntax typeOf)
    {
        var argumentList = attribute.ArgumentList;

        var existing = argumentList?.Arguments.FirstOrDefault(argument =>
            argument.NameEquals?.Name.ToString() == "As"
        );

        if (existing != null)
        {
            return attribute.ReplaceNode(
                existing.Expression,
                typeOf.WithTriviaFrom(existing.Expression)
            );
        }

        var asArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.NameEquals(
                SyntaxFactory.IdentifierName("As"),
                SyntaxFactory.Token(
                    SyntaxFactory.TriviaList(SyntaxFactory.Space),
                    SyntaxKind.EqualsToken,
                    SyntaxFactory.TriviaList(SyntaxFactory.Space)
                )
            ),
            null,
            typeOf
        );

        if (argumentList == null)
        {
            // Trivia after the name, such as a comment, moves past the new argument list.
            return attribute
                .WithName(attribute.Name.WithoutTrailingTrivia())
                .WithArgumentList(
                    SyntaxFactory
                        .AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(asArgument))
                        .WithTrailingTrivia(attribute.Name.GetTrailingTrivia())
                );
        }

        if (argumentList.Arguments.Count == 0)
        {
            return attribute.WithArgumentList(
                argumentList.WithArguments(SyntaxFactory.SingletonSeparatedList(asArgument))
            );
        }

        // The last argument's trailing trivia, such as a comment or the line break before the
        // closing parenthesis, moves after the new argument.
        var last = argumentList.Arguments.Last();

        var arguments = argumentList
            .Arguments.Take(argumentList.Arguments.Count - 1)
            .Append(last.WithoutTrailingTrivia())
            .Append(asArgument.WithTrailingTrivia(last.GetTrailingTrivia()));

        var separators = argumentList
            .Arguments.GetSeparators()
            .Append(
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space)
            );

        return attribute.WithArgumentList(
            argumentList.WithArguments(SyntaxFactory.SeparatedList(arguments, separators))
        );
    }
}
