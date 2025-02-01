using DependencyModules.Runtime.Attributes;

namespace SecondarySutProject.CircularReferenceModules;

[TransientService(Realm = typeof(ModuleA))]
public class ServiceA { }