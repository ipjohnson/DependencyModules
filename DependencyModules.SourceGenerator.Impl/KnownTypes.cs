using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl;

public static class KnownTypes {
    public static class Microsoft {
        public static class DependencyInjection {
            
            public const string Namespace = "Microsoft.Extensions.DependencyInjection";
            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IServiceCollection =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace, "IServiceCollection");
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
        }

        public static class Interfaces {
            public const string Namespace = "DependencyModules.Runtime.Interfaces";

            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IDependencyModule =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "IDependencyModule");

            // ReSharper disable once InconsistentNaming
            public static readonly ITypeDefinition IDependencyModuleProvider =
                TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition, Namespace, "IDependencyModuleProvider");
        }

        public static class Helpers {
            public const string Namespace = "DependencyModules.Runtime.Helpers";
        }
    }
}