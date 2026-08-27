using System.Collections.Immutable;
using System.Linq;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// The diagnostics about a module's own declaration — DM0003, DM0017 and DM0018.
/// </summary>
/// <remarks>
/// <para>
/// Reported from their own source output rather than from the writer that emits the module, so that
/// the compilation can be combined in. A location needs the syntax tree it came from before Roslyn
/// will let <c>.editorconfig</c> or <c>#pragma warning disable</c> touch the diagnostic, and the
/// tree can only be found by asking the compilation for it.
/// </para>
/// <para>
/// The split is what makes that affordable. The compilation changes on every keystroke, so
/// combining it into the writer would re-emit every module every time — measured, and the reason
/// module decorators and the metadata scan resolve the compilation in a Select and pass the result
/// on by value. This output emits nothing. It walks models that are already cached, tests three
/// conditions, and reports; re-running it costs a loop.
/// </para>
/// <para>
/// The conditions live here rather than being written out at both ends. The writer still has to
/// know when to stop — generating for a non-partial or nested module produces errors against the
/// developer's own code instead of the one that names the fix — but it asks the same predicates
/// rather than repeating them.
/// </para>
/// </remarks>
public static class ModuleEntryPointDiagnostics {

    /// <summary>
    /// Generating into a non-partial type produces CS0260 against the developer's own declaration,
    /// which describes the symptom rather than the fix.
    /// </summary>
    public static bool IsNotPartial(ModuleEntryPointModel model) =>
        model.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.NotPartial);

    /// <summary>
    /// Generating for a nested module emits a same-named type at namespace level, which compiles
    /// and registers nothing — the failure DM0017 exists to replace.
    /// </summary>
    public static bool IsNestedInType(ModuleEntryPointModel model) =>
        model.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.NestedInType);

    /// <summary>
    /// A module carrying parameters that leaves equality to the generator. Modules de-duplicate by
    /// type, so two instances holding different values are the same module by that rule and the
    /// second is discarded silently.
    /// </summary>
    /// <remarks>
    /// Only when the generator is supplying equality: a module that declares its own <c>Equals</c>
    /// has already answered the question this asks about.
    /// </remarks>
    public static bool ReliesOnGeneratedEquality(ModuleEntryPointModel model) =>
        model.PropertyInfoModels.Any(p => p.IsModuleParameter) &&
        model.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.ShouldImplementEquals);

    public static void Report(
        SourceProductionContext context,
        (ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> Models,
            Compilation Compilation) input) {

        if (input.Models.Length == 0) {
            return;
        }

        // The same consolidation the writer runs. Two partial declarations of one module are one
        // module, and reporting per declaration would report twice.
        var (entryPointList, _) = EntryModelUtil.ConsolidateEntryPointModels(input.Models);

        var lookup = new SyntaxTreeLookup(input.Compilation);

        foreach (var entryPointModel in entryPointList) {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (IsNotPartial(entryPointModel)) {
                Report(context, DependencyModuleDiagnostics.ModuleMustBePartial, entryPointModel, lookup);
                continue;
            }

            if (IsNestedInType(entryPointModel)) {
                Report(context, DependencyModuleDiagnostics.ModuleCannotBeNested, entryPointModel, lookup);
                continue;
            }

            if (ReliesOnGeneratedEquality(entryPointModel)) {
                Report(context, DependencyModuleDiagnostics.ModuleWithPropertiesShouldImplementEquals,
                    entryPointModel, lookup);
            }
        }
    }

    private static void Report(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        ModuleEntryPointModel entryPointModel,
        SyntaxTreeLookup lookup) =>
        context.ReportDiagnostic(
            Diagnostic.Create(
                descriptor,
                entryPointModel.Location.ToLocationOrNone(lookup),
                entryPointModel.EntryPointType.Name));
}
