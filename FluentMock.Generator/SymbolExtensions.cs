using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace FluentMock.Generator;

internal static class SymbolExtensions
{
  public static ImmutableArray<IPropertySymbol> GetAllProperties(this ITypeSymbol type, HashSet<string> toIgnore)
  {
    ImmutableArray<ISymbol> members = type.GetMembers();
    var builder = ImmutableArray.CreateBuilder<IPropertySymbol>(members.Length);

    foreach (ISymbol member in members)
    {
      if (toIgnore.Contains(member.Name) || member is not IPropertySymbol property)
        continue;

      builder.Add(property);
    }
    foreach (INamedTypeSymbol @interface in type.AllInterfaces)
    {
      foreach (ISymbol member in @interface.GetMembers())
      {
        if (toIgnore.Contains(member.Name) || member is not IPropertySymbol property)
          continue;

        builder.Add(property);
      }
    }

    return builder.ToImmutable();
  }

  public static IEnumerable<IMethodSymbol> GetAllMethods(this ITypeSymbol type, HashSet<string> toIgnore)
  {
    ImmutableArray<ISymbol> members = type.GetMembers();

    foreach (ISymbol member in members)
    {
      if (toIgnore.Contains(member.Name) || member is not IMethodSymbol method || method.MethodKind is not MethodKind.Ordinary)
        continue;

      yield return method;
    }
    foreach (INamedTypeSymbol @interface in type.AllInterfaces)
    {
      foreach (ISymbol member in @interface.GetMembers())
      {
        if (toIgnore.Contains(member.Name) || member is not IMethodSymbol method || method.MethodKind is not MethodKind.Ordinary)
          continue;

        yield return method;
      }
    }
  }
}
