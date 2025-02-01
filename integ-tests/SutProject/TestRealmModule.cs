using DependencyModules.Runtime.Attributes;

namespace SutProject;

[DependencyModule(OnlyRealm = true)]
public partial class TestRealmModule { }

public interface ITestRealmService {
    
}

[SingletonService(ServiceType = typeof(ITestRealmService), Realm = typeof(TestRealmModule))]
public class TestRealmService : ITestRealmService { }