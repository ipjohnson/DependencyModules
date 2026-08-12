using System.Collections.Immutable;
using CSharpAuthor;
using System.Text.RegularExpressions;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class EntryModelUtil {
    /// <summary>
    /// The declared module an auto-generated <c>ApplicationModule</c> should defer to, or null when
    /// it has to carry its own registrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A module with no realm restriction registers every service in the compilation that is not
    /// aimed at some other realm. The auto-generated module is one of those, so when the project
    /// also declares one the two produce byte-identical registration bodies — measured at 5,413
    /// bytes of IL each in a 200 service project, where the duplicate was 44% of the assembly and
    /// 21% of the ReadyToRun image. It is dead code in every application that does not name
    /// <c>ApplicationModule</c>, and code the AOT compiler still has to compile in the ones that do.
    /// </para>
    /// <para>
    /// Deferring rather than dropping keeps <c>AddModule&lt;ApplicationModule&gt;()</c> registering
    /// what it always did: the auto module returns the declared one from
    /// <c>InternalGetModules</c>, and the runtime loads it. The two register the same set, so what
    /// reaches the collection is unchanged.
    /// </para>
    /// <para>
    /// Only a module with no realm restriction is a valid target. An <c>OnlyRealm</c> module takes
    /// just the registrations aimed at it, so deferring to one would silently drop everything else.
    /// Where several qualify, any of them registers the same set; the name orders them so the
    /// choice does not move between builds.
    /// </para>
    /// </remarks>
    public static ITypeDefinition? DelegateTargetFor(
        ModuleEntryPointModel entryPointModel, IEnumerable<ModuleEntryPointModel> allEntryPoints) {

        if (!entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule)) {
            return null;
        }

        ModuleEntryPointModel? target = null;

        foreach (var candidate in allEntryPoints) {
            if (candidate.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule) ||
                candidate.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.OnlyRealm) ||
                candidate.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.NotPartial)) {
                continue;
            }

            // A module the caller has to supply arguments for cannot be constructed by the auto
            // module, which has nothing to pass.
            if (candidate.Parameters.Count > 0) {
                continue;
            }

            if (target == null || string.Compare(FullName(candidate), FullName(target), StringComparison.Ordinal) < 0) {
                target = candidate;
            }
        }

        return target?.EntryPointType;
    }

    /// <summary>
    /// The entry points that should carry the registrations for a compilation.
    /// </summary>
    /// <remarks>
    /// Every writer that emits registrations, decorations or interceptions into a module filters
    /// through this, so an auto-generated module that defers to a declared one is skipped by all of
    /// them rather than by whichever ones remembered to.
    /// </remarks>
    public static IList<ModuleEntryPointModel> RegistrationTargets(IList<ModuleEntryPointModel> entryPoints) {
        List<ModuleEntryPointModel>? filtered = null;

        for (var i = 0; i < entryPoints.Count; i++) {
            if (DelegateTargetFor(entryPoints[i], entryPoints) == null) {
                filtered?.Add(entryPoints[i]);
                continue;
            }

            filtered ??= new List<ModuleEntryPointModel>(entryPoints.Take(i));
        }

        return filtered ?? entryPoints;
    }

    private static string FullName(ModuleEntryPointModel model) =>
        model.EntryPointType.Namespace + "." + model.EntryPointType.Name;

    /// <summary>
    /// Rewrites a generated partial declaration when the module is a record.
    /// </summary>
    /// <remarks>
    /// CSharpAuthor has no record class, so every writer contributing to a module's partial has to
    /// do this, and a writer that forgets breaks the build with CS0261 — but only in a compilation
    /// that has both a record module and whatever makes that writer emit. Two writers had their own
    /// copy and two did not; the decorator and interceptor files were each found by adding the first
    /// [Decorator] and the first [Intercept] to a project that happened to contain a record module.
    /// Shared so the next writer cannot get it wrong.
    /// </remarks>
    public static string ApplyRecordDeclaration(string output, ModuleEntryPointModel entryPointModel) {
        if (!entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.IsRecord)) {
            return output;
        }

        return Regex.Replace(
            output,
            @"partial class " + Regex.Escape(entryPointModel.EntryPointType.Name) + @"(?!\w)",
            $"partial record class {entryPointModel.EntryPointType.Name}");
    }

    public static string GenerateFileName(ModuleEntryPointModel entryPointModel, string uniquePortion) {
        var namespaceName = entryPointModel.EntryPointType.Namespace;
        if (string.IsNullOrEmpty(entryPointModel.EntryPointType.Namespace)) {
            namespaceName = "blank-namespace";
        }
        
        return $"{namespaceName}.{entryPointModel.EntryPointType.GetShortName()}.{uniquePortion}.g.cs";
    }
    
    public static ModuleEntryPointModel EnsureNamespace(ModuleEntryPointModel entryPointModel, DependencyModuleConfigurationModel configurationModel) {
        
        if (entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule) &&
            string.IsNullOrEmpty(entryPointModel.EntryPointType.Namespace)) {
            entryPointModel = entryPointModel with {
                EntryPointType = TypeDefinition.Get(
                    configurationModel.RootNamespace, 
                    entryPointModel.EntryPointType.Name)
            };
        }
        return entryPointModel;
    }
    
    public static (IList<ModuleEntryPointModel> uniqueEntryPoints, DependencyModuleConfigurationModel configurationModel) ConsolidateEntryPointModels(
        ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)> entryPointList) {
        var uniqueEntryPoints = new List<ModuleEntryPointModel>();
        var configurationModel = entryPointList.First().Right;

        var entryPointModels = entryPointList.Select(m => m.Left);
        if (!configurationModel.AutoGenerateEntry) {
            entryPointModels = entryPointModels.Where(m => !m.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule));
        }
        
        var groupingEnumerable = 
            entryPointModels.GroupBy(m => m.EntryPointType.Namespace + "." + m.EntryPointType.GetShortName());

        foreach (var grouping in groupingEnumerable) {
            if (grouping.Count() > 1) {
                uniqueEntryPoints.Add(
                    ConsolidateEntryPointModelGrouping(grouping, configurationModel));
            } else {
                var entryPointModel = grouping.First();

                if (entryPointModel.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule)) {
                    var path = Path.Combine(configurationModel.ProjectDir, "Program.cs");

                    if (entryPointModel.FileLocation == path) {
                        uniqueEntryPoints.Add(grouping.First());
                    }
                }
                else {
                    uniqueEntryPoints.Add(grouping.First());
                }
            }
        }
        
        return (uniqueEntryPoints, configurationModel);
    }

    private static ModuleEntryPointModel ConsolidateEntryPointModelGrouping(IGrouping<string,ModuleEntryPointModel> grouping, DependencyModuleConfigurationModel configurationModel) {
        var firstNonAuto = grouping.FirstOrDefault(
            m => m.ModuleFeatures.HasFlag(ModuleEntryPointFeatures.AutoGenerateModule) == false);
        
        if (firstNonAuto != null) {
            return firstNonAuto;
        }
        
        return grouping.First();
    }
}