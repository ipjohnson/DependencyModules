using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// One collection item of a module: the class and one of its <c>Into*</c> attributes.
/// </summary>
public record GlueItemOutput(GlueClassModel Class, GlueItemModel Item);

/// <summary>
/// Writes the part of a module that adds its collection items and its <c>Func</c> registrations.
/// </summary>
/// <remarks>
/// The file also implements <c>IDependencyModule.InternalApplyGlue</c>. The module writer does not,
/// so a module with no items and no <c>Func</c> registrations has the same code as before, and
/// compiles against a runtime package that has no <c>GlueRegistry</c>.
/// </remarks>
public class GlueFileWriter
{
    private const string MethodName = "ModuleGlue";

    private const string HelpersNamespace = "global::DependencyModules.Runtime.Helpers";

    public string Write(
        ModuleEntryPointModel entryPointModel,
        DependencyModuleConfigurationModel configurationModel,
        IReadOnlyList<GlueItemOutput> items,
        IReadOnlyList<GlueFuncModel> funcs
    )
    {
        var csharpFile = new CSharpFileDefinition(entryPointModel.EntryPointType.Namespace);

        var classDefinition = csharpFile.AddClass(entryPointModel.EntryPointType.Name);
        classDefinition.Modifiers |= ComponentModifier.Partial;
        classDefinition.EnableNullable();

        // GetRequiredService and GetService are extension methods.
        classDefinition.AddUsingNamespace("Microsoft.Extensions.DependencyInjection");

        var registryType = new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            KnownTypes.DependencyModules.Helpers.Namespace,
            "DependencyRegistry",
            new[] { entryPointModel.EntryPointType }
        );

        WriteField(classDefinition, registryType);
        WriteApplyMethod(classDefinition, registryType);
        WriteGlueMethod(classDefinition, items, funcs);

        GeneratedCodeCoverage.ExcludeMembers(csharpFile, configurationModel);

        var outputContext = new OutputContext(
            new OutputContextOptions
            {
                TypeOutputMode = TypeOutputMode.Global,
                BraceStyle = configurationModel.GeneratedCodeStyle,
            }
        );

        csharpFile.WriteOutput(outputContext);

        return EntryModelUtil.ApplyRecordDeclaration(outputContext.Output(), entryPointModel);
    }

    private static void WriteField(ClassDefinition classDefinition, ITypeDefinition registryType)
    {
        var field = classDefinition.AddField(typeof(int), "moduleGlueField");
        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        field.AddAttribute(
            TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"),
            $"nameof({MethodName})"
        );

        field.InitializeValue = new StaticInvokeStatement(
            registryType,
            "AddGlue",
            new List<IOutputComponent> { CodeOutputComponent.Get(MethodName) }
        )
        {
            Indented = false,
        };
    }

    private static void WriteApplyMethod(
        ClassDefinition classDefinition,
        ITypeDefinition registryType
    )
    {
        var method = classDefinition.AddMethod("InternalApplyGlue");

        method.AddAttribute(
            TypeDefinition.Get("System.ComponentModel", "EditorBrowsable"),
            "global::System.ComponentModel.EditorBrowsableState.Never"
        );
        method.InterfaceImplementation = KnownTypes.DependencyModules.Interfaces.IDependencyModule;
        method.SetReturnType(typeof(bool));

        var services = method.AddParameter(
            KnownTypes.Microsoft.DependencyInjection.IServiceCollection,
            "services"
        );
        var environment = method.AddParameter(
            KnownTypes.DependencyModules.Interfaces.IModuleEnvironment,
            "environment"
        );

        method.Return(
            new StaticInvokeStatement(
                registryType,
                "ApplyGlue",
                new List<IOutputComponent> { services, environment }
            )
            {
                Indented = false,
            }
        );
    }

    private static void WriteGlueMethod(
        ClassDefinition classDefinition,
        IReadOnlyList<GlueItemOutput> items,
        IReadOnlyList<GlueFuncModel> funcs
    )
    {
        var method = classDefinition.AddMethod(MethodName);
        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;

        method.AddParameter(KnownTypes.DependencyModules.Helpers.GlueRegistry, "glue");

        var environment = method.AddParameter(
            KnownTypes.DependencyModules.Interfaces.IModuleEnvironment,
            "environment"
        );

        foreach (var output in items)
        {
            var statement = CodeOutputComponent.Get(ItemStatement(output));

            if (output.Class.Conditions is { Count: > 0 } conditions)
            {
                method
                    .If(
                        CodeOutputComponent.Get(
                            EnvironmentConditionWriter.BuildCondition(conditions, environment.Name)
                        )
                    )
                    .AddIndentedStatement(statement);
            }
            else
            {
                method.AddIndentedStatement(statement);
            }
        }

        foreach (var func in funcs)
        {
            method.AddIndentedStatement(
                CodeOutputComponent.Get($"glue.AddFunc<{func.FuncType}>({func.Factory})")
            );
        }
    }

    private static string ItemStatement(GlueItemOutput output)
    {
        var item = output.Item;
        var implementation = $"typeof({output.Class.TypeName})";
        var factory = $"provider => {output.Class.Construction}";

        switch (item.Kind)
        {
            case GlueItemKind.Map:
                return $"glue.AddMapItem<{item.KeyType}, {item.ItemType}>("
                    + $"{item.Value}, {implementation}, {factory})";
            case GlueItemKind.Set:
                return $"glue.AddSetItem<{item.ItemType}>({implementation}, {factory})";
            case GlueItemKind.TypeList:
                return $"glue.AddListItem<{item.ItemType}>("
                    + $"{implementation}, {ArrayOf("global::System.Type", item.DependsOn)}, {factory})";
            default:
                return $"glue.AddListItem<{item.ItemType}, {item.KeyType}>("
                    + $"{item.Value}, {ArrayOf(item.KeyType!, item.DependsOn)}, {implementation}, {factory})";
        }
    }

    private static string ArrayOf(string elementType, IReadOnlyList<string> elements) =>
        elements.Count == 0
            ? $"global::System.Array.Empty<{elementType}>()"
            : $"new {elementType}[] {{ {string.Join(", ", elements)} }}";
}
