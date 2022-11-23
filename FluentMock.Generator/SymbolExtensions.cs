using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace FluentMock.Generator;

internal static class SymbolExtensions
{
  public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol type)
  {
    foreach (ISymbol member in type.GetMembers())
    {
      yield return member;
    }
    foreach (INamedTypeSymbol @interface in type.AllInterfaces)
    {
      foreach (ISymbol member in @interface.GetMembers())
      {
        yield return member;
      }
    }
  }
}
