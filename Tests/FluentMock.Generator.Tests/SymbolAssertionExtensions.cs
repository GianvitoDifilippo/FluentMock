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

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && (!method.Name.StartsWith("Set") || method.Name == "Setup"));

    constructors.Should().HaveCount(2)
      .And.ContainSingle(ctor =>
        ctor.Parameters.Length == 0)
      .And.ContainSingle(ctor =>
        ctor.Parameters.Length == 1 &&
        ctor.Parameters[0].Type.ToDisplayString(null) == "Moq.MockBehavior");

    staticMethods.Should().HaveCount(2)
      .And.ContainSingle(method =>
        method.Name == "Build" &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == $"System.Action<{builderType.ToDisplayString(null)}>" &&
        method.ReturnType.ToDisplayString(null) == type.ToDisplayString(null))
      .And.ContainSingle(method =>
        method.Name == "Build" &&
        method.Parameters.Length == 2 &&
        method.Parameters[0].Type.ToDisplayString(null) == "Moq.MockBehavior" &&
        method.Parameters[1].Type.ToDisplayString(null) == $"System.Action<{builderType.ToDisplayString(null)}>" &&
        method.ReturnType.ToDisplayString(null) == type.ToDisplayString(null));

    instanceMethods.Should().HaveCount(2)
      .And.ContainSingle(method =>
        method.Name == "Build" &&
        method.Parameters.Length == 0 &&
        method.ReturnType.ToDisplayString(null) == type.ToDisplayString(null))
      .And.ContainSingle(method =>
        method.Name == "Setup" &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == $"System.Action<Moq.Mock<{type.ToDisplayString(null)}>>" &&
        method.ReturnType.ToDisplayString(null) == builderType.ToDisplayString(null));
  }
}
