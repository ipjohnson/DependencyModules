using CSharpAuthor;
using DependencyModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

/// <summary>
/// Renders the arguments for a constructor or factory call: each parameter resolved from the
/// provider on the terms the parameter itself declares.
/// </summary>
/// <remarks>
/// Shared rather than reimplemented per call site. Three things here are easy to write out and easy
/// to get subtly wrong, and each is silent when it is:
/// <list type="bullet">
/// <item>a nullable parameter takes <c>GetService</c>, not <c>GetRequiredService</c>, or an optional
/// dependency starts throwing;</item>
/// <item><c>[FromKeyedServices]</c> takes <c>GetRequiredKeyedService</c> with the key, or a keyed
/// dependency silently resolves the <i>unkeyed</i> registration — the right type, the wrong
/// instance;</item>
/// <item>an <c>IServiceProvider</c> parameter is the provider itself rather than something to
/// resolve.</item>
/// </list>
/// The decorator writer duplicated this and got the second one wrong, which is why it now lives in
/// one place.
/// </remarks>
public static class ConstructorArgumentWriter {

    /// <summary>
    /// Arguments for every parameter.
    /// </summary>
    public static object[] Arguments(
        ParameterDefinition serviceProvider, IReadOnlyList<ParameterInfoModel> parameters) =>
        Arguments(serviceProvider, parameters, -1, null);

    /// <summary>
    /// Arguments for every parameter, with one supplied rather than resolved.
    /// </summary>
    /// <param name="suppliedIndex">
    /// The parameter that is passed in — the instance a decorator wraps. -1 when every parameter is
    /// resolved.
    /// </param>
    /// <param name="supplied">What to write at that position.</param>
    public static object[] Arguments(
        ParameterDefinition serviceProvider,
        IReadOnlyList<ParameterInfoModel> parameters,
        int suppliedIndex,
        object? supplied) {

        var arguments = new List<object>(parameters.Count);

        for (var i = 0; i < parameters.Count; i++) {
            if (i == suppliedIndex && supplied != null) {
                arguments.Add(supplied);

                continue;
            }

            arguments.Add(Argument(serviceProvider, parameters[i]));
        }

        return arguments.ToArray();
    }

    private static object Argument(ParameterDefinition serviceProvider, ParameterInfoModel parameter) {
        if (parameter.ParameterType.Equals(KnownTypes.Microsoft.DependencyInjection.IServiceProvider)) {
            return serviceProvider;
        }

        var keyed = parameter.Attributes.FirstOrDefault(
            attribute => attribute.TypeDefinition.Equals(
                KnownTypes.Microsoft.DependencyInjection.FromKeyedServicesAttribute));

        var name = "Get";
        var arguments = new List<object>();

        if (!parameter.ParameterType.IsNullable) {
            name += "Required";
        }

        if (keyed != null) {
            name += "Keyed";

            var key = keyed.Arguments.First().Value!;

            if (key is string text) {
                key = QuoteString(text);
            }

            arguments.Add(key);
        }

        name += "Service";

        return serviceProvider.InvokeGeneric(
            name,
            new[] { parameter.ParameterType.MakeNullable(false) },
            arguments.ToArray());
    }
}
