using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// A member declared as a type and a name with two accessor blocks: an event, or an indexer.
/// </summary>
/// <remarks>
/// <see cref="PropertyDefinition"/> covers a plain property and is used for one, but it writes a
/// single index parameter and has no notion of an event. An intercepted interface can declare
/// <c>this[int row, int column]</c> or <c>event EventHandler Changed</c>, and refusing either would
/// be a limit of the writer rather than of interception.
/// </remarks>
public class AccessorMemberDefinition : BaseOutputComponent, INamedComponent {
    private readonly string _keyword;
    private readonly string _firstAccessor;
    private readonly string _secondAccessor;

    private AccessorMemberDefinition(
        ITypeDefinition type, string name, string keyword, string firstAccessor, string secondAccessor) {

        Type = type;
        Name = name;
        _keyword = keyword;
        _firstAccessor = firstAccessor;
        _secondAccessor = secondAccessor;
    }

    /// <summary>
    /// <c>public event THandler Name { add { } remove { } }</c>
    /// </summary>
    public static AccessorMemberDefinition Event(ITypeDefinition handlerType, string name) =>
        new(handlerType, name, "event", "add", "remove");

    /// <summary>
    /// <c>public TValue this[TIndex index] { get { } set { } }</c>
    /// </summary>
    public static AccessorMemberDefinition Indexer(ITypeDefinition valueType) =>
        new(valueType, "this", "", "get", "set");

    public string Name { get; }

    public ITypeDefinition Type { get; }

    /// <summary>
    /// The indices an indexer is written with. Empty for an event.
    /// </summary>
    public List<ParameterDefinition> Parameters { get; } = new();

    /// <summary>
    /// The getter, or the adder.
    /// </summary>
    public PropertyMethodDefinition? First { get; set; }

    /// <summary>
    /// The setter, or the remover. Null when the member declares only the first.
    /// </summary>
    public PropertyMethodDefinition? Second { get; set; }

    protected override void WriteComponentOutput(IOutputContext outputContext) {
        outputContext.AddImportNamespace(Type);

        outputContext.WriteIndent(GetAccessModifier(KeyWords.Public));
        outputContext.WriteSpace();

        if (_keyword.Length > 0) {
            outputContext.Write(_keyword);
            outputContext.WriteSpace();
        }

        outputContext.Write(Type);
        outputContext.Write(" ");
        outputContext.Write(Name);

        if (Parameters.Count > 0) {
            outputContext.Write("[");

            for (var index = 0; index < Parameters.Count; index++) {
                if (index > 0) {
                    outputContext.Write(", ");
                }

                Parameters[index].WriteWithSignature(outputContext);
            }

            outputContext.Write("]");
        }

        outputContext.WriteLine();
        outputContext.OpenScope();

        if (First != null) {
            outputContext.WriteIndent(_firstAccessor);
            First.WriteOutput(outputContext);
        }

        if (Second != null) {
            outputContext.WriteIndent(_secondAccessor);
            Second.WriteOutput(outputContext);
        }

        outputContext.CloseScope();
    }
}
