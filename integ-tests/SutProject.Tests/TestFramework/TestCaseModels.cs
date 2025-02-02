using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.TestFramework;


[SingletonService(Realm = typeof(AssemblyLevelModule))]
public class AssemblyTestCaseService : ITestRealmService {
    
}

[SingletonService(Realm = typeof(ClassLevelModule))]
public class ClassTestCaseService : ITestRealmService {
    
}

[SingletonService(Realm = typeof(MethodLevelModule))]
public class MethodTestCaseService : ITestRealmService {
    
}
