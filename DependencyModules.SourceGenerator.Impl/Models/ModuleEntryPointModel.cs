using System.Reflection;
using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Models;

public record ModuleEntryPointModel(
    ITypeDefinition EntryPointType,
    bool OnlyRealm,
    List<ParameterInfoModel> Parameters,
    bool ImplementsEquals,
    IReadOnlyList<PropertyInfoModel> PropertyInfoModels,
    IReadOnlyList<AttributeModel> AttributeModels);


public class ModuleEntryPointModelComparer : IEqualityComparer<ModuleEntryPointModel> {
    
public bool Equals(ModuleEntryPointModel? x, ModuleEntryPointModel? y)
    {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;
        
        if (x.Parameters.Count != y.Parameters.Count) return false;

        for (var i = 0; i < x.Parameters.Count; i++) {
            if (!x.Parameters[i].Equals(y.Parameters[i])) return false;
        }
        
        return x.EntryPointType.Equals(y.EntryPointType) &&
               x.OnlyRealm == y.OnlyRealm &&
               x.Parameters.SequenceEqual(y.Parameters) &&
               x.Parameters.SequenceEqual(y.Parameters) &&
               x.AttributeModels.SequenceEqual(y.AttributeModels);
    }

    public int GetHashCode(ModuleEntryPointModel obj)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + obj.EntryPointType.GetHashCode();
            hash = hash * 31 + obj.OnlyRealm.GetHashCode();
            foreach (var attribute in obj.AttributeModels)
            {
                hash = hash * 31 + attribute.GetHashCode();
            }
            return hash;
        }
    }
}