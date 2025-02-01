using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.TestFramework;


[SingletonService(ServiceType = typeof(ITestRealmService), 
    Realm = typeof(AssemblyLevelModule))]
public class AssemblyTestCaseService : ITestRealmService {
    
}

[SingletonService(ServiceType = typeof(ITestRealmService), Realm = typeof(ClassLevelModule))]
public class ClassTestCaseService : ITestRealmService {
    
}

[SingletonService(ServiceType = typeof(ITestRealmService), Realm = typeof(MethodLevelModule))]
public class MethodTestCaseService : ITestRealmService {
    
}
