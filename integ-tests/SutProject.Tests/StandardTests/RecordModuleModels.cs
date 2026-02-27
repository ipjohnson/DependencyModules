using DependencyModules.Runtime.Attributes;

namespace SutProject.Tests.StandardTests;

public interface IRecordModuleService {
    string Value { get; }
}

[SingletonService(Realm = typeof(RecordModule))]
public class RecordModuleService : IRecordModuleService {
    public string Value => "FromRecordModule";
}

[DependencyModule]
public partial record RecordModule;
