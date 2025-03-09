using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl;

// ReSharper disable InconsistentNaming
public static class KnownTypes {
    public static class Microsoft {
        public static class DependencyInjection {
            
            public const string Namespace = "Microsoft.Extensions.DependencyInjection";

            public static readonly ITypeDefinition IServiceCollection =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IServiceCollection");
            
            public static readonly ITypeDefinition IServiceProvider =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IServiceProvider");
            
            public static readonly ITypeDefinition ServiceDescriptor = 
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "ServiceDescriptor");
        }
        
        public static class TextJson {
            public const string Namespace = "System.Text.Json.Serialization";
            
            public static readonly ITypeDefinition JsonSourceGenerationOptionsAttribute =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "JsonSourceGenerationOptionsAttribute");
            
            public static readonly ITypeDefinition IJsonTypeInfoResolver =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace + ".Metadata", "IJsonTypeInfoResolver");
        }
    }

    public static class DependencyModules {
        public static class Attributes {
            public const string Namespace = "DependencyModules.Runtime.Attributes";

            public static readonly ITypeDefinition DependencyModuleAttribute =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "DependencyModuleAttribute");

            public static readonly ITypeDefinition TransientServiceAttribute =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "TransientServiceAttribute");

            public static readonly ITypeDefinition ScopedServiceAttribute =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "ScopedServiceAttribute");

            public static readonly ITypeDefinition SingletonServiceAttribute =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "SingletonServiceAttribute");
            
            public static readonly ITypeDefinition CrossWireServiceAttribute =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "CrossWireServiceAttribute");

        }

        public static class Interfaces {
            public const string Namespace = "DependencyModules.Runtime.Interfaces";

            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IDependencyModule =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IDependencyModule");

            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IDependencyModuleProvider =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IDependencyModuleProvider");
        }

        public static class Features {
            
            public const string Namespace = "DependencyModules.Runtime.Features";

            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IFeatureApplicator =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IFeatureApplicator");

            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IDependencyModuleApplicatorProvider =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IDependencyModuleApplicatorProvider");
            
            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition FeatureApplicator =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "FeatureApplicator");

        }
        

        public static class Helpers {
            public const string Namespace = "DependencyModules.Runtime.Helpers";
        }
    }
}