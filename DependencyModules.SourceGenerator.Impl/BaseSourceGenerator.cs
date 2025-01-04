using System.Reflection;
using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using DependencyModules.SourceGenerator.Impl.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyModules.SourceGenerator.Impl;

public abstract class BaseSourceGenerator  : IIncrementalGenerator {

    protected abstract IEnumerable<ISourceGenerator> AttributeSourceGenerators();

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var incrementalValueProvider = CreateSourceValueProvider(context);
        var dependencyConfigurationProvider = CreateConfigurationValueProvider(context);

        var valuesProvider = incrementalValueProvider.Combine(dependencyConfigurationProvider);
        
        foreach (var attributeSourceGenerator in AttributeSourceGenerators()) {
            attributeSourceGenerator.SetupGenerator(context, valuesProvider);
        }
        
        SetupRootGenerator(context, incrementalValueProvider);
    }

    private IncrementalValueProvider<DependencyModuleConfigurationModel> CreateConfigurationValueProvider(IncrementalGeneratorInitializationContext context) {
        return context.AnalyzerConfigOptionsProvider.Select((options, _) => {
            var defaultUseTry = false;

            if (options.GlobalOptions.TryGetValue("build_property.DependencyModules_DefaultUseTry", out var value)) {
                defaultUseTry = value.ToLowerInvariant() == "true";
            }
            
            return new DependencyModuleConfigurationModel(defaultUseTry);
        }).WithComparer(new DependencyModuleConfigurationModelComparer());
    }

    protected virtual void SetupRootGenerator(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ModuleEntryPointModel> incrementalValueProvider) {
        
    }

    private IncrementalValuesProvider<ModuleEntryPointModel> CreateSourceValueProvider(IncrementalGeneratorInitializationContext context) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.DependencyModules.Attributes.DependencyModuleAttribute);

        return context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateEntryPointModel
        ).WithComparer(new ModuleEntryPointModelComparer());
    }

    protected virtual ModuleEntryPointModel GenerateEntryPointModel(GeneratorSyntaxContext context, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();
        
        IReadOnlyList<AttributeModel> attributes = Array.Empty<AttributeModel>();
        
        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            attributes = AttributeModelHelper
                .GetAttributes(context, classDeclarationSyntax.AttributeLists, cancellation)
                .ToList();
        }

        var realmOnly = GetRealmOnlyFlag(context);
        var implementsEqualsFlag = GetEqualsFlag(context);
        var parameters = GetConstructorParameters(context);
        
        return new ModuleEntryPointModel(
            ((ClassDeclarationSyntax)context.Node).GetTypeDefinition(), 
            realmOnly,
            parameters,
            implementsEqualsFlag,
            attributes);
    }

    private List<ParameterInfoModel> GetConstructorParameters(GeneratorSyntaxContext context) {
        var list = new List<ParameterInfoModel>();
        var constructors = 
            context.Node.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToList();
        
        constructors.Sort(
            (a, b) => 
                a.ParameterList.Parameters.Count.CompareTo(b.ParameterList.Parameters.Count));

        if (constructors.Count > 0) {
            foreach (var parameterSyntax in constructors[0].ParameterList.Parameters) {
                list.Add(new ParameterInfoModel(
                    parameterSyntax.Identifier.ToString(),
                    parameterSyntax.Type?.GetTypeDefinition(context) ?? TypeDefinition.Get(typeof(object))
                    ));
            }
        }
        
        return list;
    }

    private bool GetEqualsFlag(GeneratorSyntaxContext context) {
        return context.Node.DescendantNodes().OfType<MethodDeclarationSyntax>().
            Any(m => m.Identifier.ToString().Equals("Equals"));
    }

    private bool GetRealmOnlyFlag(GeneratorSyntaxContext context) {
        if (context.Node is ClassDeclarationSyntax classDeclarationSyntax) {
            var module = classDeclarationSyntax.DescendantNodes().OfType<AttributeSyntax>().
                FirstOrDefault(attr => attr.Name.ToString().StartsWith("DependencyModule"));

            if (module is { ArgumentList: not null }) {
                foreach (var argumentSyntax in module.ArgumentList.Arguments) {
                    if (argumentSyntax.NameEquals?.Name.ToString().StartsWith("OnlyRealm") ?? false) {
                        return argumentSyntax.Expression.ToString() == "true";
                    }
                }
            }
        }
        return false;
    }
}
