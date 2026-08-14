using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Reads a type parameter's constraints into parts.
/// </summary>
/// <remarks>
/// One reader for both places a wrapper repeats constraints — the class it is declared as, and each
/// generic method it forwards. The rules are subtle enough that two copies would drift: only one
/// primary constraint is legal, <c>unmanaged</c> has to be tested before <c>struct</c> because it
/// implies it, and Roslyn reports a constructor constraint for a <c>struct</c>-constrained parameter
/// even though repeating <c>new()</c> alongside it is CS0451.
/// </remarks>
public static class TypeParameterReader {

    public static TypeParameterModel Read(ITypeParameterSymbol parameter) {
        string? primary = null;

        if (parameter.HasUnmanagedTypeConstraint) {
            primary = "unmanaged";
        } else if (parameter.HasValueTypeConstraint) {
            primary = "struct";
        } else if (parameter.HasReferenceTypeConstraint) {
            primary = parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                ? "class?"
                : "class";
        } else if (parameter.HasNotNullConstraint) {
            primary = "notnull";
        }

        var constraintTypes = new ITypeDefinition[parameter.ConstraintTypes.Length];

        for (var i = 0; i < constraintTypes.Length; i++) {
            constraintTypes[i] = parameter.ConstraintTypes[i].GetTypeDefinition();
        }

        var defaultConstructor = parameter.HasConstructorConstraint &&
                                 primary is not ("struct" or "unmanaged");

        return new TypeParameterModel(parameter.Name, primary, constraintTypes, defaultConstructor);
    }
}
