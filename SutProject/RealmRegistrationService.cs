using DependencyModules.Runtime.Attributes;

namespace SutProject;

[SingletonService(Realm = typeof(SutRealmModule))]
public class RealmRegistrationService {
    
}