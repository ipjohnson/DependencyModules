using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// Diagnostics reported by the generator.
///
/// A source generator that stays quiet when something is wrong leaves the developer with a build
/// that succeeds and an application that misbehaves at run time. Everything here exists to move a
/// failure from run time to build time, or at minimum to make it visible.
/// </summary>
public static class DependencyModuleDiagnostics {
    private const string Category = "DependencyModules";

    /// <summary>
    /// Raised when the generator itself throws. Previously the exception was caught and, unless a
    /// log directory happened to be configured, discarded — the build succeeded, no registrations
    /// were produced, and nothing said so.
    /// </summary>
    public static readonly DiagnosticDescriptor GeneratorFailure = new(
        id: "DM0001",
        title: "DependencyModules generator failed",
        messageFormat:
        "The DependencyModules generator failed and registrations may be missing or incomplete: {0}. " +
        "Set DependencyModules_LogOutputDirectory to capture a log, and report the issue at " +
        "https://github.com/ipjohnson/DependencyModules/issues with that log attached.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a service the container could never construct. Without this the generator emits a
    /// registration that throws when the provider is built, far from the declaration that caused it.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceCannotBeConstructed = new(
        id: "DM0002",
        title: "Service type cannot be constructed",
        messageFormat:
        "'{0}' is {1} and cannot be instantiated, so it was not registered. " +
        "Apply the service attribute to a concrete class, or register it with a static factory method.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a module that is not partial. The compiler also reports CS0260 once the generated
    /// half arrives, but that message describes the symptom rather than what to do about it.
    /// </summary>
    public static readonly DiagnosticDescriptor ModuleMustBePartial = new(
        id: "DM0003",
        title: "Dependency module must be partial",
        messageFormat:
        "'{0}' is marked with [DependencyModule] but is not declared partial. " +
        "The generator completes the type with a second partial declaration, so add the partial modifier.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised when two conventions in one module both match a type. Picking one silently would
    /// produce a registration nobody can predict from reading the module.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousConventionMatch = new(
        id: "DM0004",
        title: "Convention match is ambiguous",
        messageFormat:
        "'{0}' is matched by more than one convention in '{1}' — as '{2}' and as '{3}'. " +
        "Narrow one of them, or move it to another module.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a convention that matched nothing. A convention naming a service type no type in
    /// the compilation implements is almost always a mistake, and without this it fails silently —
    /// which is the failure mode this codebase has repeatedly had to hunt down.
    /// </summary>
    public static readonly DiagnosticDescriptor ConventionMatchedNothing = new(
        id: "DM0005",
        title: "Convention matched no types",
        messageFormat:
        "The convention registering '{0}' in '{1}' matched no types. " +
        "Conventions match a type that declares the service type, or declares an interface " +
        "extending it; call IncludeBaseClasses() to also match types that reach it through a base class.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a convention match the container could not construct. The abstract and static
    /// cases are excluded before this point, because an abstract base implementing the convention's
    /// interface is the normal shape; a concrete class with no accessible constructor is not.
    /// </summary>
    public static readonly DiagnosticDescriptor ConventionMatchNotConstructable = new(
        id: "DM0006",
        title: "Convention matched a type that cannot be constructed",
        messageFormat:
        "'{0}' matches the convention registering '{1}' in '{2}', but has no accessible constructor, " +
        "so it was not registered",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised when two decorators of one service share an order. Applying them in an arbitrary order
    /// would nest them in a way nobody declared, and the two nestings behave differently.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousDecoratorOrder = new(
        id: "DM0007",
        title: "Decorator order is ambiguous",
        messageFormat:
        "'{0}' and '{1}' both decorate '{2}' with order {3}, so the order they nest in is undefined. " +
        "Give them distinct Order values.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised when a class marked for interception cannot be wrapped. Interception works through an
    /// interface, and a few member shapes cannot be forwarded; saying so beats emitting a wrapper
    /// that does not compile.
    /// </summary>
    public static readonly DiagnosticDescriptor CannotIntercept = new(
        id: "DM0008",
        title: "Service cannot be intercepted",
        messageFormat: "This service cannot be intercepted: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for something in a Conventions method body the generator could not read.
    /// </summary>
    /// <remarks>
    /// That body is configuration rather than code — it is read at compile time and never executed —
    /// so only a closed set of calls can appear in it. An expression outside that set has no
    /// compile-time meaning, and skipping it would drop registrations while the build stayed green.
    /// </remarks>
    public static readonly DiagnosticDescriptor ConventionCannotBeRead = new(
        id: "DM0009",
        title: "Convention declaration cannot be read",
        messageFormat: "This convention declaration could not be read, because {0}: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports, on the class itself, that a convention registered it.
    /// </summary>
    /// <remarks>
    /// A class registered by convention carries no attribute saying so, so nothing at the
    /// declaration explains why it is in the container. This is that explanation, and it names the
    /// interface the match came through when it was not direct — which is what keeps matching
    /// through interface inheritance from looking like luck.
    ///
    /// Informational, so it stays out of the build: measured, a diagnostic at this severity is
    /// invisible at every MSBuild verbosity below detailed. It does appear in the IDE, and in SARIF
    /// when ErrorLog is set. Silence it with dotnet_diagnostic.DM0010.severity = none.
    /// </remarks>
    public static readonly DiagnosticDescriptor ExposedByConvention = new(
        id: "DM0010",
        title: "Service is registered by convention",
        messageFormat: "Exposed as {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
