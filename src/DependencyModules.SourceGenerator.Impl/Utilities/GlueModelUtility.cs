using System.Globalization;
using System.Text;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Reads the <c>Into*</c> attributes and the <c>Func</c> constructor parameters of a class, and
/// writes the expressions that build what they name.
/// </summary>
/// <remarks>
/// <para>
/// The model holds text and not symbols, so it is a cache key that Roslyn can compare. Each
/// expression reads its services from a variable with the name <c>provider</c>.
/// </para>
/// <para>
/// A parameter of an object that the glue builds gets its value from the first rule that applies:
/// a caller argument of a <c>Func</c>; the provider itself; a keyed service; <c>new</c> for a
/// concrete class with no service attribute; the provider.
/// </para>
/// </remarks>
public static class GlueModelUtility
{
    private const string AttributeNamespace = "DependencyModules.Runtime.Attributes";

    private const string RegistryMetadataName = "DependencyModules.Runtime.Helpers.GlueRegistry";

    private const string ProviderName = "provider";

    // A cycle is found before this depth. The limit stops a long chain of generic closings.
    private const int MaxDepth = 24;

    private static readonly SymbolDisplayFormat QualifiedName = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
    );

    /// <summary>
    /// The model of a class or a factory method, or null when the glue has nothing to write for
    /// it.
    /// </summary>
    /// <param name="serviceAttributeNames">
    /// The names of the service attributes of the generator. A class with one of them is a
    /// service, and its <c>Func</c> constructor parameters get registrations.
    /// </param>
    public static GlueClassModel? GetModel(
        GeneratorAttributeSyntaxContext context,
        ISet<string> serviceAttributeNames,
        CancellationToken cancellationToken
    )
    {
        var compilation = context.SemanticModel.Compilation;

        // An older runtime package has no registry for the generated code to call.
        if (compilation.GetTypeByMetadataName(RegistryMetadataName) == null)
        {
            return null;
        }

        var builder = new Builder(compilation, serviceAttributeNames, cancellationToken);

        switch (context.TargetSymbol)
        {
            case IMethodSymbol method:
                return ForFactoryMethod(context, builder, method);
            case INamedTypeSymbol { TypeKind: TypeKind.Class } type:
                return ForClass(context, builder, type);
            default:
                return null;
        }
    }

    private static GlueClassModel? ForFactoryMethod(
        GeneratorAttributeSyntaxContext context,
        Builder builder,
        IMethodSymbol method
    )
    {
        foreach (var parameter in method.Parameters)
        {
            builder.RegisterFunc(parameter.Type);
        }

        if (!builder.HasOutput)
        {
            return null;
        }

        return new GlueClassModel(
            Qualified(method.ContainingType) + "." + method.Name,
            method.ContainingType.Name + "." + method.Name,
            LocationModel.From(context.TargetNode),
            Array.Empty<GlueItemModel>(),
            null,
            null,
            builder.Funcs.Values.ToArray(),
            builder.FuncProblems.ToArray(),
            builder.ParameterProblems.ToArray(),
            ServiceRealms(builder, method),
            null
        );
    }

    private static GlueClassModel? ForClass(
        GeneratorAttributeSyntaxContext context,
        Builder builder,
        INamedTypeSymbol type
    )
    {
        var items = new List<GlueItemModel>();
        var realms = ServiceRealms(builder, type);

        foreach (var attribute in type.GetAttributes())
        {
            if (ReadItem(builder, type, attribute) is { } item)
            {
                items.Add(item);
                realms.Add(item.Realm);
            }
        }

        string? construction = null;
        string? constructionProblem = null;

        if (items.Count > 0)
        {
            construction = builder.BuildNamed(type, null, out constructionProblem);
        }

        // The container selects the constructor of a service, so each one is read.
        if (builder.IsService(type) && !ContainsTypeParameter(type))
        {
            foreach (var constructor in type.InstanceConstructors)
            {
                if (!builder.IsAccessible(constructor))
                {
                    continue;
                }

                foreach (var parameter in constructor.Parameters)
                {
                    builder.RegisterFunc(parameter.Type);
                }
            }
        }

        if (items.Count == 0 && !builder.HasOutput)
        {
            return null;
        }

        return new GlueClassModel(
            Fq(type),
            type.Name,
            LocationModel.From(context.TargetNode),
            items,
            construction,
            constructionProblem,
            builder.Funcs.Values.ToArray(),
            builder.FuncProblems.ToArray(),
            builder.ParameterProblems.ToArray(),
            realms,
            EnvironmentConditionUtility.GetConditions(type)
        );
    }

    private static GlueItemModel? ReadItem(
        Builder builder,
        INamedTypeSymbol type,
        AttributeData attribute
    )
    {
        if (
            attribute.AttributeClass is not { IsGenericType: true } attributeClass
            || NamespaceOf(attributeClass) != AttributeNamespace
        )
        {
            return null;
        }

        var typeArguments = attributeClass.TypeArguments;
        var arguments = attribute.ConstructorArguments;

        GlueItemKind kind;
        ITypeSymbol itemType;
        ITypeSymbol? keyType = null;
        TypedConstant? value = null;
        var dependsOn = Array.Empty<string>();

        switch (attributeClass.Name)
        {
            case "IntoMapAttribute" when typeArguments.Length == 2 && arguments.Length == 1:
                kind = GlueItemKind.Map;
                keyType = typeArguments[0];
                itemType = typeArguments[1];
                value = arguments[0];
                break;
            case "IntoSetAttribute" when typeArguments.Length == 1:
                kind = GlueItemKind.Set;
                itemType = typeArguments[0];
                break;
            case "IntoListAttribute" when typeArguments.Length == 1 && arguments.Length == 1:
                kind = GlueItemKind.TypeList;
                itemType = typeArguments[0];
                dependsOn = Literals(arguments[0]);
                break;
            case "IntoListAttribute" when typeArguments.Length == 2 && arguments.Length == 2:
                kind = GlueItemKind.ValueList;
                itemType = typeArguments[0];
                keyType = typeArguments[1];
                value = arguments[0];
                dependsOn = Literals(arguments[1]);
                break;
            default:
                return null;
        }

        // An argument that does not compile is already an error from the compiler.
        if (value is { Kind: TypedConstantKind.Error } || itemType.TypeKind == TypeKind.Error)
        {
            return null;
        }

        var problem = builder.Compilation.ClassifyConversion(type, itemType).IsImplicit
            ? null
            : $"it is not assignable to '{itemType.ToDisplayString(QualifiedName)}'";

        return new GlueItemModel(
            kind,
            Fq(itemType),
            keyType == null ? null : Fq(keyType),
            value == null ? null : Literal(value.Value),
            value?.ToCSharpString(),
            dependsOn,
            Realm(attribute),
            problem
        );
    }

    private static List<string?> ServiceRealms(Builder builder, ISymbol symbol)
    {
        var realms = new List<string?>();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (builder.IsServiceAttribute(attribute))
            {
                realms.Add(Realm(attribute));
            }
        }

        return realms;
    }

    private static string? Realm(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Realm" && argument.Value.Value is INamedTypeSymbol realm)
            {
                return Qualified(realm);
            }
        }

        return null;
    }

    private static string[] Literals(TypedConstant array)
    {
        if (array.Kind != TypedConstantKind.Array || array.IsNull)
        {
            return Array.Empty<string>();
        }

        return array.Values.Select(Literal).ToArray();
    }

    /// <summary>
    /// An attribute argument as a C# expression that compiles in any namespace.
    /// </summary>
    private static string Literal(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null!";
        }

        switch (constant.Kind)
        {
            case TypedConstantKind.Type when constant.Value is ITypeSymbol type:
                return $"typeof({Fq(type)})";
            case TypedConstantKind.Enum when constant.Type != null:
                return $"({Fq(constant.Type)})({Number(constant.Value!)})";
        }

        return constant.Value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true),
            char character => SymbolDisplay.FormatLiteral(character, true),
            bool flag => flag ? "true" : "false",
            null => "null!",
            int number => Number(number),
            var number when constant.Type != null => $"({Fq(constant.Type)})({Number(number)})",
            var other => Number(other),
        };
    }

    private static string Number(object value) =>
        value switch
        {
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => ((double)number).ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "0",
        };

    private static string Fq(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Qualified(INamedTypeSymbol type) => type.ToDisplayString(QualifiedName);

    private static string NamespaceOf(INamedTypeSymbol symbol) =>
        symbol.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : "";

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return true;
            case IArrayTypeSymbol array:
                return ContainsTypeParameter(array.ElementType);
            case INamedTypeSymbol named:
                if (named.IsUnboundGenericType)
                {
                    return true;
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (ContainsTypeParameter(argument))
                    {
                        return true;
                    }
                }

                return named.ContainingType != null && ContainsTypeParameter(named.ContainingType);
            default:
                return false;
        }
    }

    /// <summary>
    /// The caller arguments of one <c>Func</c>, and which of them a constructor has used.
    /// </summary>
    private sealed class CallerArguments
    {
        private readonly ITypeSymbol[] _types;
        private readonly bool[] _used;

        public CallerArguments(ITypeSymbol[] types)
        {
            _types = types;
            _used = new bool[types.Length];
        }

        public int Count => _types.Length;

        public static string Name(int index) =>
            "arg" + index.ToString(CultureInfo.InvariantCulture);

        public bool TryTake(ITypeSymbol type, out string name)
        {
            for (var i = 0; i < _types.Length; i++)
            {
                if (!_used[i] && SymbolEqualityComparer.Default.Equals(_types[i], type))
                {
                    _used[i] = true;
                    name = Name(i);
                    return true;
                }
            }

            name = "";
            return false;
        }

        public CallerArguments Copy() => new(_types);

        public ITypeSymbol? FirstUnused()
        {
            for (var i = 0; i < _types.Length; i++)
            {
                if (!_used[i])
                {
                    return _types[i];
                }
            }

            return null;
        }

        public ITypeSymbol? FirstDuplicate()
        {
            for (var i = 0; i < _types.Length; i++)
            {
                for (var j = i + 1; j < _types.Length; j++)
                {
                    if (SymbolEqualityComparer.Default.Equals(_types[i], _types[j]))
                    {
                        return _types[i];
                    }
                }
            }

            return null;
        }

        public string ParameterList()
        {
            var text = new StringBuilder("(");

            for (var i = 0; i < _types.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(Name(i));
            }

            return text.Append(')').ToString();
        }
    }

    private enum ParameterSource
    {
        Rules,
        ServiceProvider,
        Constructor,
    }

    private sealed class Builder
    {
        private readonly ISet<string> _serviceAttributeNames;
        private readonly CancellationToken _cancellationToken;
        private readonly HashSet<ITypeSymbol> _building = new(SymbolEqualityComparer.Default);
        private readonly HashSet<string> _funcsSeen = new();
        private readonly HashSet<string> _parameterProblemsSeen = new();

        public Builder(
            Compilation compilation,
            ISet<string> serviceAttributeNames,
            CancellationToken cancellationToken
        )
        {
            Compilation = compilation;
            _serviceAttributeNames = serviceAttributeNames;
            _cancellationToken = cancellationToken;
        }

        public Compilation Compilation { get; }

        public Dictionary<string, GlueFuncModel> Funcs { get; } = new();

        public List<GlueFuncProblemModel> FuncProblems { get; } = new();

        public List<GlueParameterProblemModel> ParameterProblems { get; } = new();

        public bool HasOutput =>
            Funcs.Count > 0 || FuncProblems.Count > 0 || ParameterProblems.Count > 0;

        public bool IsAccessible(ISymbol symbol) =>
            Compilation.IsSymbolAccessibleWithin(symbol, Compilation.Assembly);

        public bool IsServiceAttribute(AttributeData attribute)
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                return false;
            }

            if (_serviceAttributeNames.Contains(attributeClass.Name))
            {
                return true;
            }

            foreach (var implemented in attributeClass.AllInterfaces)
            {
                if (implemented.Name == "IServiceRegistrationAttribute")
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsService(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (IsServiceAttribute(attribute))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The expression that builds a class that the developer named: a collection item, or the
        /// result of a <c>Func</c>. Null, with the reason, when the glue cannot build it.
        /// </summary>
        public string? BuildNamed(
            INamedTypeSymbol type,
            CallerArguments? callerArguments,
            out string? problem
        )
        {
            problem = ShapeProblem(type);

            if (problem != null)
            {
                return null;
            }

            var constructor = ChooseConstructor(type, callerArguments);

            if (constructor == null)
            {
                problem = "it has no constructor that the generated module can call";
                return null;
            }

            var probe = callerArguments?.Copy();

            foreach (var parameter in constructor.Parameters)
            {
                if (
                    SourceOf(parameter) != ParameterSource.Rules
                    || probe?.TryTake(parameter.Type, out _) == true
                    || !IsPlainValue(parameter.Type)
                )
                {
                    continue;
                }

                problem =
                    $"its constructor parameter '{parameter.Name}' has the type "
                    + $"'{parameter.Type.ToDisplayString(QualifiedName)}', which neither a caller "
                    + "argument nor the service provider can give";
                return null;
            }

            var expression = Construct(type, constructor, callerArguments);

            if (expression == null)
            {
                problem = "its constructor parameters make a cycle";
                return null;
            }

            if (callerArguments?.FirstUnused() is { } unused)
            {
                problem =
                    $"its constructor has no parameter of the type "
                    + $"'{unused.ToDisplayString(QualifiedName)}' for the caller argument";
                return null;
            }

            return expression;
        }

        /// <summary>
        /// Makes a registration for a <c>Func</c> type, when the glue can write its closure.
        /// </summary>
        public void RegisterFunc(ITypeSymbol type)
        {
            if (
                type
                    is not INamedTypeSymbol
                    {
                        TypeKind: TypeKind.Delegate,
                        Name: "Func",
                        IsGenericType: true,
                        ContainingNamespace:
                        { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                    } func
                || ContainsTypeParameter(func)
            )
            {
                return;
            }

            var funcType = Fq(func);

            if (!_funcsSeen.Add(funcType))
            {
                return;
            }

            var product = func.TypeArguments[func.TypeArguments.Length - 1];
            var callerArguments = new CallerArguments(
                func.TypeArguments.Take(func.TypeArguments.Length - 1).ToArray()
            );

            var factory =
                callerArguments.Count == 0
                    ? FactoryWithNoArguments(func, product)
                    : FactoryWithArguments(funcType, product, callerArguments);

            if (factory != null)
            {
                Funcs[funcType] = new GlueFuncModel(funcType, factory);
            }
        }

        private string? FactoryWithNoArguments(INamedTypeSymbol func, ITypeSymbol product)
        {
            if (!product.IsReferenceType || product.SpecialType != SpecialType.None)
            {
                return null;
            }

            if (CanBuildUnnamed(product, out var constructor))
            {
                var construction = Construct((INamedTypeSymbol)product, constructor!, null);

                if (construction != null)
                {
                    return $"{ProviderName} => () => {ProviderName}.GetService<{Fq(product)}>() ?? {construction}";
                }
            }

            // Func<Task> and other delegates over System types are callbacks, not requests for a
            // service.
            if (IsInNamespace(product, "System"))
            {
                return null;
            }

            var annotations = func.TypeArgumentNullableAnnotations;

            return annotations[annotations.Length - 1] == NullableAnnotation.Annotated
                ? $"{ProviderName} => () => {ProviderName}.GetService<{Fq(product)}>()!"
                : $"{ProviderName} => () => {ProviderName}.GetRequiredService<{Fq(product)}>()";
        }

        private string? FactoryWithArguments(
            string funcType,
            ITypeSymbol product,
            CallerArguments callerArguments
        )
        {
            // The provider cannot take caller arguments, so only a class that the glue builds has
            // a closure. A delegate over other types is one that the application registers.
            if (
                product is not INamedTypeSymbol { TypeKind: TypeKind.Class } productClass
                || product.SpecialType != SpecialType.None
                || productClass.IsAbstract
                || productClass.IsStatic
                || IsFrameworkType(product)
            )
            {
                return null;
            }

            if (callerArguments.FirstDuplicate() is { } duplicate)
            {
                FuncProblems.Add(
                    new GlueFuncProblemModel(
                        funcType,
                        $"two caller arguments have the type '{duplicate.ToDisplayString(QualifiedName)}'"
                    )
                );
                return null;
            }

            var construction = BuildNamed(productClass, callerArguments, out var problem);

            if (construction == null)
            {
                FuncProblems.Add(
                    new GlueFuncProblemModel(
                        funcType,
                        $"'{productClass.Name}' cannot be built, because {problem}"
                    )
                );
                return null;
            }

            return $"{ProviderName} => {callerArguments.ParameterList()} => {construction}";
        }

        private static string? ShapeProblem(INamedTypeSymbol type)
        {
            if (type.IsStatic)
            {
                return "it is a static class";
            }

            if (type.IsAbstract)
            {
                return "it is abstract";
            }

            return ContainsTypeParameter(type) ? "it is an open generic class" : null;
        }

        /// <summary>
        /// True for a concrete class that no attribute names, which the glue builds with
        /// <c>new</c> when it is a constructor parameter.
        /// </summary>
        private bool CanBuildUnnamed(ITypeSymbol type, out IMethodSymbol? constructor)
        {
            constructor = null;

            if (
                type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named
                || named.SpecialType != SpecialType.None
                || ShapeProblem(named) != null
                || IsFrameworkType(named)
                || IsService(named)
                || !IsAccessible(named)
            )
            {
                return false;
            }

            constructor = ChooseConstructor(named, null);

            if (constructor == null)
            {
                return false;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (SourceOf(parameter) == ParameterSource.Rules && IsPlainValue(parameter.Type))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The constructor with <c>[ActivatorUtilitiesConstructor]</c>, or the one with the most
        /// parameters. With caller arguments, a constructor that takes all of them is first.
        /// </summary>
        private IMethodSymbol? ChooseConstructor(
            INamedTypeSymbol type,
            CallerArguments? callerArguments
        )
        {
            IMethodSymbol? chosen = null;
            var chosenTakesAll = false;

            foreach (var constructor in type.InstanceConstructors)
            {
                if (constructor.IsStatic || !IsAccessible(constructor))
                {
                    continue;
                }

                foreach (var attribute in constructor.GetAttributes())
                {
                    if (attribute.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute")
                    {
                        return constructor;
                    }
                }

                var takesAll = callerArguments == null || TakesAll(constructor, callerArguments);

                if (
                    chosen == null
                    || (takesAll && !chosenTakesAll)
                    || (
                        takesAll == chosenTakesAll
                        && constructor.Parameters.Length > chosen.Parameters.Length
                    )
                )
                {
                    chosen = constructor;
                    chosenTakesAll = takesAll;
                }
            }

            return chosen;
        }

        private static bool TakesAll(IMethodSymbol constructor, CallerArguments callerArguments)
        {
            var probe = callerArguments.Copy();

            foreach (var parameter in constructor.Parameters)
            {
                if (SourceOf(parameter) == ParameterSource.Rules)
                {
                    probe.TryTake(parameter.Type, out _);
                }
            }

            return probe.FirstUnused() == null;
        }

        private string? Construct(
            INamedTypeSymbol type,
            IMethodSymbol constructor,
            CallerArguments? callerArguments
        )
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (_building.Count >= MaxDepth || !_building.Add(type))
            {
                return null;
            }

            var text = new StringBuilder("new ").Append(Fq(type)).Append('(');

            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(Argument(constructor.Parameters[i], callerArguments));
            }

            _building.Remove(type);

            return text.Append(')').ToString();
        }

        private string Argument(IParameterSymbol parameter, CallerArguments? callerArguments)
        {
            var type = parameter.Type;
            var source = SourceOf(parameter);

            if (
                source == ParameterSource.Rules
                && callerArguments != null
                && callerArguments.TryTake(type, out var callerArgument)
            )
            {
                return callerArgument;
            }

            if (
                type is INamedTypeSymbol
                {
                    Name: "IServiceProvider",
                    ContainingNamespace:
                    { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                }
            )
            {
                return ProviderName;
            }

            var optional = parameter.NullableAnnotation == NullableAnnotation.Annotated;
            var name = Fq(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

            foreach (var attribute in parameter.GetAttributes())
            {
                if (
                    attribute.AttributeClass?.Name == "FromKeyedServicesAttribute"
                    && attribute.ConstructorArguments.Length > 0
                )
                {
                    var key = Literal(attribute.ConstructorArguments[0]);

                    return optional
                        ? $"{ProviderName}.GetKeyedService<{name}>({key})"
                        : $"{ProviderName}.GetRequiredKeyedService<{name}>({key})";
                }
            }

            RegisterFunc(type);

            if (source == ParameterSource.Constructor)
            {
                string? problem = null;

                if (
                    type is INamedTypeSymbol { TypeKind: TypeKind.Class } named
                    && named.SpecialType == SpecialType.None
                    && BuildNamed(named, null, out problem) is { } built
                )
                {
                    return built;
                }

                AddParameterProblem(parameter, problem ?? "it is not a class");
            }
            else if (source == ParameterSource.Rules && CanBuildUnnamed(type, out var constructor))
            {
                // Null for a cycle. The provider then gives the error for the type.
                var construction = Construct((INamedTypeSymbol)type, constructor!, null);

                if (construction != null)
                {
                    return $"({ProviderName}.GetService<{name}>() ?? {construction})";
                }
            }

            return optional
                ? $"{ProviderName}.GetService<{name}>()"
                : $"{ProviderName}.GetRequiredService<{name}>()";
        }

        private void AddParameterProblem(IParameterSymbol parameter, string reason)
        {
            var owner = parameter.ContainingType?.Name ?? "";

            if (_parameterProblemsSeen.Add(owner + "|" + parameter.Name))
            {
                ParameterProblems.Add(
                    new GlueParameterProblemModel(
                        owner,
                        parameter.Name,
                        parameter.Type.ToDisplayString(QualifiedName),
                        reason
                    )
                );
            }
        }

        /// <summary>
        /// The source that an attribute on the parameter names, or the rules.
        /// </summary>
        private static ParameterSource SourceOf(IParameterSymbol parameter)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (
                    attribute.AttributeClass is not { } attributeClass
                    || NamespaceOf(attributeClass) != AttributeNamespace
                )
                {
                    continue;
                }

                switch (attributeClass.Name)
                {
                    case "FromServiceProviderAttribute":
                        return ParameterSource.ServiceProvider;
                    case "FromConstructorAttribute":
                        return ParameterSource.Constructor;
                }
            }

            return ParameterSource.Rules;
        }

        /// <summary>
        /// A value type or a string. The provider has no registration for it that a constructor
        /// can mean.
        /// </summary>
        private static bool IsPlainValue(ITypeSymbol type) =>
            type.IsValueType || type.SpecialType == SpecialType.System_String;

        private static bool IsFrameworkType(ITypeSymbol type) =>
            IsInNamespace(type, "System") || IsInNamespace(type, "Microsoft");

        private static bool IsInNamespace(ITypeSymbol type, string root)
        {
            var containing = type.ContainingNamespace;

            while (containing is { IsGlobalNamespace: false })
            {
                if (containing.ContainingNamespace is { IsGlobalNamespace: true })
                {
                    return containing.Name == root;
                }

                containing = containing.ContainingNamespace;
            }

            return false;
        }
    }
}
