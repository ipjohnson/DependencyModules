using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record ModuleEntryPointModel(
    ITypeDefinition EntryPointType,
    bool OnlyRealm,
    bool? UseTry,
    bool? GenerateAttribute,
    List<ParameterInfoModel> Parameters,
    bool ImplementsEquals,
    List<PropertyInfoModel> PropertyInfoModels,
    List<AttributeModel> AttributeModels);

public class ModuleEntryPointModelComparer : IEqualityComparer<ModuleEntryPointModel> {

    public bool Equals(ModuleEntryPointModel? x, ModuleEntryPointModel? y) {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;

        return x.EntryPointType.Equals(y.EntryPointType) &&
               x.OnlyRealm == y.OnlyRealm &&
               x.UseTry == y.UseTry &&
               x.GenerateAttribute == y.GenerateAttribute &&
               x.Parameters.SequenceEqual(y.Parameters) &&
               x.PropertyInfoModels.SequenceEqual(y.PropertyInfoModels) &&
               x.AttributeModels.SequenceEqual(y.AttributeModels);
    }

    public int GetHashCode(ModuleEntryPointModel obj) {
        unchecked {
            var hash = 17;
            hash = hash * 31 + obj.EntryPointType.GetHashCode();
            if (obj.UseTry.HasValue) {
                hash = hash * 31 + obj.UseTry.Value.GetHashCode();
            }
            if (obj.GenerateAttribute.HasValue) {
                hash = hash * 31 + obj.GenerateAttribute.Value.GetHashCode();
            }
            hash = hash * 31 + obj.OnlyRealm.GetHashCode();
            hash = GetListHashCode(obj.Parameters, hash);
            hash = GetListHashCode(obj.PropertyInfoModels, hash);
            hash = GetListHashCode(obj.AttributeModels, hash);
            
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