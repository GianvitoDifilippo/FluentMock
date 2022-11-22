using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace FluentMock;

internal static class SymbolAssertionExtensions
{
  public static void ShouldMatchBuilderSpecification(this INamedTypeSymbol builderType, INamedTypeSymbol type)
  {
    type.Should().NotBeNull();
    builderType.Should().NotBeNull();

    IEnumerable<IMethodSymbol> constructors = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Constructor);

    IEnumerable<IMethodSymbol> staticMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && method.IsStatic);

    IMethodSymbol buildMethod = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .First(method => !method.IsStatic && method.Name == "Build");

    constructors.Should().HaveCount(1)
      .And.ContainSingle(ctor =>
        ctor.Parameters.Length == 1 &&
        ctor.Parameters[0].Type.ToDisplayString(null) == "Moq.MockBehavior" &&
        ctor.Parameters[0].HasExplicitDefaultValue);

    staticMethods.Should().HaveCount(2)
      .And.ContainSingle(method =>
        method.Name == "Build" &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == $"System.Action<{builderType.ToDisplayString(null)}>")
      .And.ContainSingle(method =>
        method.Name == "Build" &&
        method.Parameters.Length == 2 &&
        method.Parameters[0].Type.ToDisplayString(null) == "Moq.MockBehavior" &&
        method.Parameters[1].Type.ToDisplayString(null) == $"System.Action<{builderType.ToDisplayString(null)}>");

    buildMethod.ReturnType.Should().BeEquivalentTo(type, options => options.Using(SymbolEqualityComparer.Default));
  }
}
