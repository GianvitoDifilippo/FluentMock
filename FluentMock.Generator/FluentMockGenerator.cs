using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Threading;

namespace FluentMock.Generator;

[Generator]
internal class FluentMockGenerator : IIncrementalGenerator
{
  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var values = context.SyntaxProvider
      .CreateSyntaxProvider(OfMarkerAttributes, SelectTargetType)
      .Where(static symbol => symbol is not null)
      .Collect();

    context.RegisterSourceOutput(values, Execute!);
  }

  private void Execute(SourceProductionContext context, ImmutableArray<ITypeSymbol> types)
  {
    context.AddSource("IBuilder", SourceGenerator.Instance.GenerateIBuilder());
    context.AddSource("ListBuilder", SourceGenerator.Instance.GenerateListBuilder());

    foreach (ITypeSymbol type in types)
    {
      string hint = type.ToDisplayString();
      string source = SourceGenerator.Instance.GenerateObjectBuilder(in types, type);
      context.AddSource(hint, source);
    }
  }

  private static bool OfMarkerAttributes(SyntaxNode node, CancellationToken cancellationToken)
  {
    return node is AttributeSyntax
    {
      Name: IdentifierNameSyntax
      {
        Identifier.Text: "GenerateFluentMockFor"
      },
      ArgumentList.Arguments.Count: 1
    };
  }

  private static ITypeSymbol? SelectTargetType(GeneratorSyntaxContext context, CancellationToken cancellationToken)
  {
    AttributeSyntax attributeSyntax = (AttributeSyntax)context.Node;
    if (attributeSyntax.ArgumentList!.Arguments[0].Expression is not TypeOfExpressionSyntax typeOfExpressionSyntax)
      return null;

    SymbolInfo info = context.SemanticModel.GetSymbolInfo(typeOfExpressionSyntax.Type, cancellationToken);
    if (info.Symbol is not INamedTypeSymbol typeSymbol || typeSymbol.TypeKind is not TypeKind.Interface)
      return null;

    return typeSymbol;
  }
}
