using DependencyModules.Runtime.Attributes;
using SecondarySutProject;

namespace SutProject.Tests.RealmTests;

[DependencyModule(OnlyRealm = true)]
public partial class FirstRealmModule {
    
}