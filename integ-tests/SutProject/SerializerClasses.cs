using System.Text.Json.Serialization;
using DependencyModules.Runtime.Attributes;

namespace SutProject;

[DependencyModule(RegisterJsonSerializers = true)]
public partial class SerializerClasses {
    
}

public record SerialA(string A, string B);

public record SerialB(string A, string B);

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SerialA))]
[JsonSerializable(typeof(SerialB))]
public partial class SerializerContext : JsonSerializerContext;


[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SerialA))]
[TransientService(Key = "A", Realm = typeof(SerializerClasses))]
public partial class SerializerContextA : JsonSerializerContext;


[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SerialA))]
[TransientService(Key = "B", Realm = typeof(SerializerClasses))]
public partial class SerializerContextB : JsonSerializerContext;