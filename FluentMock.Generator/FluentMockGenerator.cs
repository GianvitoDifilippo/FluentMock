using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace FluentMock.Generator;

[Generator]
internal class FluentMockGenerator : IIncrementalGenerator
{
  private static readonly HashSet<string> s_emptyHashSet = new();

  private readonly SourceGenerator _generator;

  public FluentMockGenerator()
  {
    _generator = new();
  }

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var assemblyNameProvider = context.CompilationProvider.Select(static (compilation, ct) => compilation.AssemblyName);

    var values = context.SyntaxProvider
      .CreateSyntaxProvider(OfMarkerAttributes, SelectTargetType)
      .Where(static symbol => symbol is not null)
      .Collect()
      .Combine(assemblyNameProvider);

    context.RegisterSourceOutput(values, GenerateBuilders!);
  }

  private void GenerateBuilders(SourceProductionContext context, (ImmutableArray<TargetInfo>, string?) item)
  {
    (ImmutableArray<TargetInfo> infos, string? assemblyName) = item;
    string namespacePrefix = assemblyName is null ? string.Empty : $"{assemblyName}.";

    context.AddSource("IBuilder", _generator.GenerateIBuilder(namespacePrefix));
    context.AddSource("ISubstitute", _generator.GenerateISubstitute(namespacePrefix));
    context.AddSource("MockHelper", _generator.GenerateMockHelper(namespacePrefix));
    context.AddSource("ListBuilder", _generator.GenerateListBuilder(namespacePrefix));
    context.AddSource("MoqSettings", _generator.GenerateMoqSettings(namespacePrefix));

    foreach (TargetInfo info in infos)
    {
      string hint = info.Symbol.ToDisplayString();
      string source = _generator.GenerateObjectBuilder(infos, info, namespacePrefix);
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
      ArgumentList.Arguments.Count: 1 or 2
    };
  }

  private static TargetInfo? SelectTargetType(GeneratorSyntaxContext context, CancellationToken cancellationToken)
  {
    AttributeSyntax attribute = (AttributeSyntax)context.Node;
    AttributeArgumentListSyntax attributeArgumentList = attribute.ArgumentList!;
    if (attributeArgumentList.Arguments[0].Expression is not TypeOfExpressionSyntax typeOfExpression)
      return null;

    HashSet<string> toIgnore = GetMembersToIgnore(attributeArgumentList);

    SymbolInfo info = context.SemanticModel.GetSymbolInfo(typeOfExpression.Type, cancellationToken);
    if (info.Symbol is not INamedTypeSymbol typeSymbol || typeSymbol.TypeKind is not TypeKind.Interface)
      return null;

    return new(typeSymbol, toIgnore);
  }

  private static HashSet<string> GetMembersToIgnore(AttributeArgumentListSyntax attributeArgumentList)
  {
    if (attributeArgumentList.Arguments.Count != 2)
      return s_emptyHashSet;

    if (attributeArgumentList.Arguments[1].Expression is not ArrayCreationExpressionSyntax arrayCreationExpression)
      return s_emptyHashSet;

    if (arrayCreationExpression.Initializer is not { } initializerExpression)
      return s_emptyHashSet;

    HashSet<string> result = new();
    foreach (ExpressionSyntax expression in initializerExpression.Expressions)
    {
      if (expression is not LiteralExpressionSyntax literalExpression)
        continue;

      result.Add(literalExpression.Token.ValueText);
    }

    return result;
  }
}
