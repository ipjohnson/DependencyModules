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
    /// Raised when an interceptor has no way to be placed around one of the members it was asked to
    /// intercept. A sync interceptor holds no way to await, so it cannot serve a member returning a
    /// task; wrapping the member anyway would report completion when the task was handed back.
    /// Generating the wrapper without that member would leave the call unintercepted silently.
    /// </summary>
    public static readonly DiagnosticDescriptor InterceptorCannotServeMember = new(
        id: "DM0009",
        title: "Interceptor cannot intercept a member",
        messageFormat: "No interception was generated for this service: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
