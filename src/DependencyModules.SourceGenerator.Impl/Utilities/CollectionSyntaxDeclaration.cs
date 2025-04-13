using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl;

public class CollectionSyntaxDeclaration : BaseOutputComponent {
    private List<object> _items = new();

    public void Add(object item) {
        _items.Add(item);
    }

    protected override void WriteComponentOutput(IOutputContext outputContext) {
        outputContext.Write("[");
        var first = true;

        foreach (var value in _items) {            
            if (first == false) {
                outputContext.Write(", ");
            }
            else {
                first = false;
            }
            
            if (value is IOutputComponent component) {
                component.WriteOutput(outputContext);
            }
            else if (value is string str) {
                outputContext.Write(SyntaxHelpers.QuoteString(str));
            }
            else {
                outputContext.Write(value.ToString());
            }
        }

        outputContext.Write("]");
    }

    public override bool Equals(object? obj) {
        if (obj is CollectionSyntaxDeclaration other) {
            if (other._items.Count != _items.Count) {
                return false;
            }

            for (var i = 0; i < _items.Count; i++) {
                if (other._items[i].Equals(_items[i]) == false) {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    public override int GetHashCode() {
        return base.GetHashCode();
    }
}