using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record ModuleEntryPointModel(
    ITypeDefinition EntryPointType,
    bool OnlyRealm,
    RegistrationType? RegistrationType,
    bool? GenerateAttribute,
    IReadOnlyList<ParameterInfoModel> Parameters,
    bool ImplementsEquals,
    IReadOnlyList<PropertyInfoModel> PropertyInfoModels,
    IReadOnlyList<AttributeModel> AttributeModels);

public class ModuleEntryPointModelComparer : IEqualityComparer<ModuleEntryPointModel> {
    
    public bool Equals(ModuleEntryPointModel? x, ModuleEntryPointModel? y) {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;

        return x.EntryPointType.Equals(y.EntryPointType) &&
               x.ImplementsEquals == y.ImplementsEquals &&
               x.OnlyRealm == y.OnlyRealm &&
               x.RegistrationType == y.RegistrationType &&
               x.GenerateAttribute == y.GenerateAttribute &&
               x.Parameters.SequenceEqual(y.Parameters) &&
               x.PropertyInfoModels.SequenceEqual(y.PropertyInfoModels) &&
               x.AttributeModels.SequenceEqual(y.AttributeModels);
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
            hash = hash * 31 + obj.OnlyRealm.GetHashCode();
            hash = GetListHashCode(obj.Parameters, hash);
            hash = GetListHashCode(obj.PropertyInfoModels, hash);
            hash = GetListHashCode(obj.AttributeModels, hash);
            hash = hash * 31 + obj.ImplementsEquals.GetHashCode();
            
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