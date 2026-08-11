using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.Conventions.Utilities;

/// <summary>
/// Identifies the state of everything in a compilation that can change what a name binds to.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a semantic result can be cached. Roslyn re-runs a <c>CreateSyntaxProvider</c>
/// transform for every node it selected whenever any tree changes — measured, 2,001 calls of which
/// one was for the edited tree, over syntax nodes that were the same objects as the previous run. So
/// the transform is where the per-keystroke cost lives, and caching it is the whole optimisation.
/// </para>
/// <para>
/// The node alone is not a valid key, and the failure is reachable rather than theoretical: moving a
/// <c>global using</c> in one file changes what an untouched declaration in another file implements,
/// while its tree, its node instance and its text all stay identical. A node-keyed cache serves the
/// old interface and registers the wrong service, with a green build.
/// </para>
/// <para>
/// So the key is the node <i>and</i> this stamp. Method bodies are deliberately excluded — nothing
/// inside one can change another file's binding — which is what makes the common keystroke a cache
/// hit. Editing a base list, a using, a namespace or a member signature changes the stamp and
/// invalidates everything, which is correct and rare.
/// </para>
/// <para>
/// <b>Anything omitted here that can affect binding is a silent defect</b>, not a slow path: it
/// produces a stale model and a wrong registration that nothing reports. Add to it freely; removing
/// from it needs an argument.
/// </para>
/// </remarks>
public static class DeclarationStamp {

    private static readonly ConditionalWeakTable<SyntaxTree, StampBox> PerTree = new();
    private static readonly ConditionalWeakTable<Compilation, StampBox> PerCompilation = new();

    private sealed class StampBox {
        public StampBox(long value) => Value = value;

        public long Value { get; }
    }

    /// <summary>
    /// The stamp for a whole compilation. Memoised on the compilation, and on each tree beneath it,
    /// so an edit re-hashes one tree and re-combines the rest.
    /// </summary>
    public static long Of(Compilation compilation) {
        if (PerCompilation.TryGetValue(compilation, out var cached)) {
            return cached.Value;
        }

        // 64-bit, and mixed rather than accumulated with a small multiplier. A collision here means
        // a stale semantic model is served, so the width is a correctness property.
        var hash = 14695981039346656037UL;

        foreach (var tree in compilation.SyntaxTrees) {
            hash = Mix(hash, (ulong)TreeStamp(tree));
        }

        foreach (var reference in compilation.References) {
            hash = Mix(hash, (ulong)(reference.Display?.GetHashCode() ?? 0));
        }

        var value = (long)hash;

        PerCompilation.Add(compilation, new StampBox(value));

        return value;
    }

    /// <summary>
    /// One tree's contribution. Cached on the tree, which is immutable, so only an edited tree is
    /// ever re-hashed.
    /// </summary>
    private static long TreeStamp(SyntaxTree tree) {
        if (PerTree.TryGetValue(tree, out var cached)) {
            return cached.Value;
        }

        var hash = 14695981039346656037UL;

        // Descends into containers only. A method body is not a container of anything that can
        // change a binding, and skipping them is what makes the common keystroke free.
        foreach (var node in tree.GetRoot().DescendantNodes(descendIntoChildren: n =>
                     n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)) {

            switch (node) {
                case UsingDirectiveSyntax usingDirective:
                    hash = Mix(hash, Hash(usingDirective.ToString()));
                    break;

                case ExternAliasDirectiveSyntax externAlias:
                    hash = Mix(hash, Hash(externAlias.ToString()));
                    break;

                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    hash = Mix(hash, Hash(namespaceDeclaration.Name.ToString()));
                    break;

                case TypeDeclarationSyntax type:
                    hash = Mix(hash, Hash(type.Identifier.Text));
                    hash = Mix(hash, Hash(type.Modifiers.ToString()));
                    hash = Mix(hash, Hash(type.BaseList?.ToString()));
                    hash = Mix(hash, Hash(type.TypeParameterList?.ToString()));
                    hash = Mix(hash, Hash(type.ConstraintClauses.ToString()));
                    hash = Mix(hash, Hash(type.ParameterList?.ToString()));
                    hash = Mix(hash, Hash(type.AttributeLists.ToString()));

                    // Signatures, not bodies. A constructor added to one part of a partial changes
                    // what another part's symbol reports about itself.
                    foreach (var member in type.Members) {
                        hash = Mix(hash, MemberSignature(member));
                    }

                    break;
            }
        }

        var value = (long)hash;

        PerTree.Add(tree, new StampBox(value));

        return value;
    }

    private static ulong MemberSignature(MemberDeclarationSyntax member) =>
        member switch {
            ConstructorDeclarationSyntax constructor =>
                Hash(constructor.Modifiers + constructor.ParameterList.ToString()),
            MethodDeclarationSyntax method =>
                Hash(method.Modifiers + method.ReturnType.ToString() + method.Identifier.Text +
                     method.TypeParameterList + method.ParameterList),
            PropertyDeclarationSyntax property =>
                Hash(property.Modifiers + property.Type.ToString() + property.Identifier.Text),
            FieldDeclarationSyntax field =>
                Hash(field.Modifiers + field.Declaration.Type.ToString() +
                     string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.Text))),
            EventDeclarationSyntax @event =>
                Hash(@event.Modifiers + @event.Type.ToString() + @event.Identifier.Text),
            // Nested types are reached by the walk above, so they need nothing here.
            _ => Hash(member.Kind().ToString())
        };

    private static ulong Hash(string? text) {
        if (text == null) {
            return 0;
        }

        var hash = 14695981039346656037UL;

        foreach (var c in text) {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value) => (hash ^ value) * 1099511628211UL;
}
