using System.Text.Json.Serialization;
using DependencyModules.Runtime.Attributes;

namespace SutProject;


public record SerialA(string A, string B);

public record SerialB(string A, string B);

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SerialA))]
[JsonSerializable(typeof(SerialB))]
public partial class SerializerContext : JsonSerializerContext;


[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SerialA))]
[TransientService(Key = "A")]
public partial class SerializerContextA : JsonSerializerContext;


[JsonSourceGenerationOptions]
[JsonSerializable(typeof(SerialA))]
[TransientService(Key = "B")]
public partial class SerializerContextB : JsonSerializerContext;