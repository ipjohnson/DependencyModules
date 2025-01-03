using DependencyModules.Runtime.Attributes;

namespace SecondarySutProject.CircularReferenceModules;

[TransientService(Realm = typeof(ModuleB))]
public class ServiceB {
    
}