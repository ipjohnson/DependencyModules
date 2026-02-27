using DependencyModules.Runtime.Attributes;

namespace SutProject;

public interface IRecordService {
    string GetName();
}

[SingletonService]
public record RecordService : IRecordService {
    public string GetName() {
        return nameof(RecordService);
    }
}
