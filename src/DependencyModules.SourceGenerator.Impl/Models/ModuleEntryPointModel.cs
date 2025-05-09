using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

[Flags]
public enum ModuleEntryPointFeatures {
    None,
    AutoGenerateModule = 1,
    OnlyRealm = 2,
    ShouldImplementEquals = 4,
}

public record ModuleEntryPointModel(
    ModuleEntryPointFeatures ModuleFeatures,
    string FileLocation,
    ITypeDefinition EntryPointType,
    RegistrationType? RegistrationType,
    bool? GenerateAttribute,
    bool? RegisterJsonSerializers,
    string? UseMethod,
    bool? GenerateFactories,
    IReadOnlyList<ParameterInfoModel> Parameters,
    IReadOnlyList<PropertyInfoModel> PropertyInfoModels,
    IReadOnlyList<AttributeModel> AttributeModels,
    IReadOnlyList<ITypeDefinition> AdditionalModules,
    IReadOnlyList<ITypeDefinition> Features) : IClassModel {
    public ITypeDefinition ClassType => EntryPointType;
}

public class ModuleEntryPointModelComparer : IEqualityComparer<ModuleEntryPointModel> {
    
    public bool Equals(ModuleEntryPointModel? x, ModuleEntryPointModel? y) {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;

        return x.FileLocation == y.FileLocation &&
               x.EntryPointType.Equals(y.EntryPointType) &&
               x.ModuleFeatures == y.ModuleFeatures &&
               x.UseMethod == y.UseMethod &&
               x.RegistrationType == y.RegistrationType &&
               x.GenerateAttribute == y.GenerateAttribute &&
               x.RegisterJsonSerializers == y.RegisterJsonSerializers &&
               x.GenerateFactories == y.GenerateFactories &&
               x.Parameters.SequenceEqual(y.Parameters) &&
               x.PropertyInfoModels.SequenceEqual(y.PropertyInfoModels) &&
               x.Features.SequenceEqual(y.Features) &&
               x.AttributeModels.SequenceEqual(y.AttributeModels) &&
               x.AdditionalModules.SequenceEqual(y.AdditionalModules);
    }

    public int GetHashCode(ModuleEntryPointModel obj) {
        unchecked {
            var hash = 17;
            hash = hash * 31 + obj.EntryPointType.GetHashCode();
            if (obj.RegistrationType.HasValue) {
                hash = hash * 31 + obj.RegistrationType.Value.GetHashCode();
            }
            if (obj.GenerateAttribute.HasValue) {
                hash = hash * 31 + obj.GenerateAttribute.Value.GetHashCode();
            }
            hash = hash * 31 + obj.FileLocation.GetHashCode();
            hash = hash * 31 + obj.UseMethod?.GetHashCode() ?? 1;
            hash = hash * 31 + (obj.RegisterJsonSerializers?.GetHashCode() ?? 1);
            hash = hash * 31 + (obj.GenerateFactories?.GetHashCode() ?? 1);
            hash = hash * 31 + (int)obj.ModuleFeatures;
            hash = GetListHashCode(obj.Parameters, hash);
            hash = GetListHashCode(obj.PropertyInfoModels, hash);
            hash = GetListHashCode(obj.AttributeModels, hash);
            hash = GetListHashCode(obj.Features, hash);
            
            return hash;
        }
    }

    private int GetListHashCode<T>(IEnumerable<T> list, int hashSeed) {
        int hash = hashSeed;
        unchecked {
            foreach (var obj in list) {
                hash = hash * 31 + (obj?.GetHashCode() ?? 1);
            }
        }
        return hash;
    }
}

public static class ModuleEntryPointModelExtensions {
    public static string UniqueId(this ModuleEntryPointModel model) {
        var count = 0;

        foreach (var charValue in model.EntryPointType.Namespace + "." + model.EntryPointType.Name) {
            count += charValue;
        }
        
        return count.ToString();
    }
}