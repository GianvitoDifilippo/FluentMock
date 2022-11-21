using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace FluentMock.Generator;

internal class SourceGenerator
{
  private static readonly SymbolDisplayFormat s_namespaceDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining);
  private static readonly SymbolDisplayFormat s_typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included);

  public static readonly SourceGenerator Instance = new();

  private readonly Dictionary<ITypeSymbol, BuilderInfo> _infoCache;

  private SourceGenerator()
  {
    _infoCache = new(SymbolEqualityComparer.Default);
  }

  public string GenerateSource(in ImmutableArray<ITypeSymbol> types, ITypeSymbol type)
  {
    BuilderInfo info = GetInfo(type);

    SourceBuilder sourceBuilder = new(100); // TODO: Estimate capacity

    sourceBuilder.Append("namespace ");
    sourceBuilder.Append(info.TargetNamespace);
    sourceBuilder.OpenScope(".FluentMock");
    sourceBuilder.Append("internal class ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.OpenScope();
    GenerateFields(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateConstructor(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateInstanceBuildMethod(ref sourceBuilder, info);
    sourceBuilder.AppendLine();

    foreach (ISymbol member in type.GetMembers())
    {
      if (member is IPropertySymbol property)
      {
        GeneratePropertySetter(ref sourceBuilder, property, info);
        sourceBuilder.AppendLine();

        ITypeSymbol propertyType = property.Type;
        if (propertyType.Kind is SymbolKind.NamedType && types.Contains(propertyType))
        {
          GeneratePropertyBuilder(ref sourceBuilder, property, info, GetInfo(propertyType));
          sourceBuilder.AppendLine();
        }

        continue;
      }
      if (member is IMethodSymbol method && method.MethodKind is MethodKind.Ordinary)
      {
        GenerateMethodSetter(ref sourceBuilder, (IMethodSymbol)member);
        continue;
      }
    }

    GenerateStaticBuildMethod(ref sourceBuilder, info);

    sourceBuilder.CloseScope();
    sourceBuilder.CloseScope();

    return sourceBuilder.Source;
  }

  private static void GenerateFields(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("private readonly global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine("> _mock;");
    sourceBuilder.AppendLine("private readonly global::Moq.MockBehavior _behavior;");
  }

  private static void GenerateConstructor(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.OpenScope("(global::Moq.MockBehavior behavior = global::Moq.MockBehavior.Loose)");
    sourceBuilder.AppendLine("_behavior = behavior;");
    sourceBuilder.CloseScope("_mock = new global::Moq.Mock(behavior);");
  }

  private static void GenerateInstanceBuildMethod(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(" Build() => _mock.Object;");
  }

  private void GeneratePropertySetter(ref SourceBuilder sourceBuilder, IPropertySymbol property, BuilderInfo info)
  {
    string propertyName = property.Name;

    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append('(');
    sourceBuilder.Append(property.Type.ToDisplayString(s_typeDisplayFormat));
    sourceBuilder.Append(' ');
    sourceBuilder.Append(propertyName);
    sourceBuilder.OpenScope(")");
    sourceBuilder.Append("_mock.Setup(x => x.");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append(").Returns(");
    sourceBuilder.Append(propertyName);
    sourceBuilder.AppendLine(");");
    sourceBuilder.CloseScope("return this;");
  }

  private void GeneratePropertyBuilder(ref SourceBuilder sourceBuilder, IPropertySymbol property, BuilderInfo info, BuilderInfo propertyTypeInfo)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(property.Name);
    sourceBuilder.Append("(global::System.Action<global::");
    sourceBuilder.Append(propertyTypeInfo.TargetNamespace);
    sourceBuilder.Append(".FluentMock.");
    sourceBuilder.Append(propertyTypeInfo.BuilderName);
    sourceBuilder.OpenScope("> buildAction)");
    sourceBuilder.Append("Set");
    sourceBuilder.Append(property.Name);
    sourceBuilder.Append("(global::");
    sourceBuilder.Append(propertyTypeInfo.TargetNamespace);
    sourceBuilder.Append(".FluentMock.");
    sourceBuilder.Append(propertyTypeInfo.BuilderName);
    sourceBuilder.CloseScope(".Build(buildAction));");
  }

  private void GenerateMethodSetter(ref SourceBuilder sourceBuilder, IMethodSymbol method)
  {

  }

  private void GenerateStaticBuildMethod(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public static ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.Append(" Build(global::Moq.MockBehavior behavior, global::System.Action<");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.OpenScope("> buildAction)");
    sourceBuilder.Append("var builder = new ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("(behavior);");
    sourceBuilder.AppendLine("buildAction(builder);");
    sourceBuilder.CloseScope("return builder.Build();");
  }

  private BuilderInfo GetInfo(ITypeSymbol type)
  {
    if (!_infoCache.TryGetValue(type, out BuilderInfo? info))
    {
      string targetNamespace = type.ContainingNamespace.ToDisplayString(s_namespaceDisplayFormat);
      string targetFullName = type.ToDisplayString(s_typeDisplayFormat);
      string builderName = type.Name[0] is 'I'
        ? $"{type.Name[1..]}Builder"
        : $"{type.Name}Builder";

      info = new(targetNamespace, targetFullName, builderName);
      _infoCache.Add(type, info);
    }

    return info;
  }
}
