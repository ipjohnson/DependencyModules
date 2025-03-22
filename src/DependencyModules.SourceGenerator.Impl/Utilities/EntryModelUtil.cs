using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public class EntryModelUtil {
    public static string GenerateFileName(ModuleEntryPointModel entryPointModel, string uniquePortion) {
        return $"{entryPointModel.EntryPointType.Namespace}.{entryPointModel.EntryPointType.GetShortName()}.{uniquePortion}.g.cs";
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
                uniqueEntryPoints.Add(grouping.First());
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