using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace FluentMock.Generator;

internal class SourceGenerator
{
  private static readonly SymbolDisplayFormat s_namespaceDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining);
  private static readonly SymbolDisplayFormat s_typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat
    .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)
    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

  private readonly Dictionary<ITypeSymbol, BuilderInfo> _infoCache;

  public SourceGenerator()
  {
    _infoCache = new(SymbolEqualityComparer.Default);
  }

  public string GenerateMoqSettings(string namespacePrefix)
  {
    return $$"""
      namespace {{namespacePrefix}}FluentMock
      {
        public static class MoqSettings
        {
          public static global::Moq.MockBehavior DefaultMockBehavior = global::Moq.MockBehavior.Strict;
        }
      }
      """;
  }

  public string GenerateIBuilder(string namespacePrefix)
  {
    return $$"""
      namespace {{namespacePrefix}}FluentMock
      {
        public interface IBuilder<out T>
        {
          T Build();
        }
      }
      """;
  }

  public string GenerateISubstitute(string namespacePrefix)
  {
    return $$"""
      namespace {{namespacePrefix}}FluentMock
      {
        public interface ISubstitute
        {
          global::Moq.Mock ObjectMock { get; }
          global::Moq.Mock SubstituteMock { get; }
        }
      }
      """;
  }

  public string GenerateMockHelper(string namespacePrefix)
  {
    return $$"""
      namespace {{namespacePrefix}}FluentMock
      {
        public static class MockHelper
        {
          public static bool IsSetUp<T, TProperty>(T obj, global::System.Linq.Expressions.Expression<global::System.Func<T, TProperty>> propertyExpression)
            where T : class
          {
            if (propertyExpression is not global::System.Linq.Expressions.MemberExpression memberExpr || memberExpr.Member is not global::System.Reflection.PropertyInfo property)
              return false;
      
            string propertyName = property.Name;

            if (obj is ISubstitute substitute)
            {
              return IsSetUp(substitute.ObjectMock, propertyName) || IsSetUp(substitute.SubstituteMock, propertyName);
            }

            var mock = global::Moq.Mock.Get(obj);
            return IsSetUp(mock, propertyName);
          }
          
          public static bool IsSetUp<T, TProperty>(global::Moq.Mock<T> mock, global::System.Linq.Expressions.Expression<global::System.Func<T, TProperty>> propertyExpression)
            where T : class
          {
            if (propertyExpression is not global::System.Linq.Expressions.MemberExpression memberExpr || memberExpr.Member is not global::System.Reflection.PropertyInfo property)
              return false;

            return IsSetUp(mock, property.Name);
          }
          
          public static bool IsSetUp(global::Moq.Mock mock, string propertyName)
          {
            return global::System.Linq.Enumerable.Any(mock.Setups, setup =>
              setup is global::System.Linq.Expressions.MemberExpression memberExpr &&
              memberExpr.Member is global::System.Reflection.PropertyInfo property &&
              property.Name == propertyName);
          }
        }
      }
      """;
  }

  public string GenerateListBuilder(string namespacePrefix)
  {
    return $$"""
      namespace {{namespacePrefix}}FluentMock
      {
        public abstract class __ListBuilderBase<T, TListBuilder>
          where TListBuilder : __ListBuilderBase<T, TListBuilder>, new()
        {
          private readonly global::System.Collections.Generic.List<T> _list;

          protected __ListBuilderBase()
          {
            _list = new();
          }

          protected abstract TListBuilder This { get; }

          public global::System.Collections.Generic.IReadOnlyList<T> Build() => _list;

          public TListBuilder Add(T item)
          {
            _list.Add(item);
            return This;
          }

          public static global::System.Collections.Generic.IReadOnlyList<T> Build(global::System.Action<TListBuilder> buildAction)
          {
            TListBuilder builder = new TListBuilder();
            buildAction(builder);
            return builder.Build();
          }
        }

        public abstract class __ListBuilderBase<T, TBuilder, TListBuilder> : __ListBuilderBase<T, TListBuilder>
          where TBuilder : IBuilder<T>
          where TListBuilder : __ListBuilderBase<T, TBuilder, TListBuilder>, new()
        {
          protected __ListBuilderBase()
          {
          }

          public TListBuilder Add(global::Moq.MockBehavior behavior, global::System.Action<TBuilder> buildAction)
          {
            TBuilder builder = (TBuilder)global::System.Activator.CreateInstance(typeof(TBuilder), new object[] { behavior })!;
            buildAction(builder);
            return Add(builder.Build());
          }

          public TListBuilder Add<TDerivedBuilder>(global::Moq.MockBehavior behavior, global::System.Action<TDerivedBuilder> buildAction)
            where TDerivedBuilder : IBuilder<T>
          {
            TDerivedBuilder builder = (TDerivedBuilder)global::System.Activator.CreateInstance(typeof(TDerivedBuilder), new object[] { behavior })!;
            buildAction(builder);
            return Add(builder.Build());
          }

          public TListBuilder Add(global::System.Action<TBuilder> buildAction)
          {
            return Add(MoqSettings.DefaultMockBehavior, buildAction);
          }

          public TListBuilder Add<TDerivedBuilder>(global::System.Action<TDerivedBuilder> buildAction)
            where TDerivedBuilder : IBuilder<T>
          {
            return Add<TDerivedBuilder>(MoqSettings.DefaultMockBehavior, buildAction);
          }
        }

        public sealed class ListBuilder<T> : __ListBuilderBase<T, ListBuilder<T>>
        {
          public ListBuilder()
          {
          }

          protected override ListBuilder<T> This => this;
        }

        public sealed class ListBuilder<T, TBuilder> : __ListBuilderBase<T, TBuilder, ListBuilder<T, TBuilder>>
          where TBuilder : IBuilder<T>
        {
          public ListBuilder()
          {
          }

          protected override ListBuilder<T, TBuilder> This => this;
        }
      }
      """;
  }

  public string GenerateObjectBuilder(ImmutableArray<TargetInfo> infos, TargetInfo targetInfo, string namespacePrefix)
  {
    (INamedTypeSymbol type, HashSet<string> toIgnore) = targetInfo;
    BuilderInfo info = GetInfo(type);
    ImmutableArray<IPropertySymbol> allProperties = type.GetAllProperties(toIgnore);
    bool hasSpans = HasSpans(allProperties, out ImmutableArray<IPropertySymbol> spanProperties);

    SourceBuilder sourceBuilder = new(256); // TODO: Estimate capacity

    sourceBuilder.Append("namespace ");
    sourceBuilder.Append(info.TargetNamespace);
    sourceBuilder.AppendLine(".FluentMock");
    sourceBuilder.AppendLine("{", 1);

    // GenerateDelegates(ref sourceBuilder, allMembers);

    sourceBuilder.Append("public class ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append($" : global::{namespacePrefix}FluentMock.IBuilder<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(">");
    sourceBuilder.AppendLine("{", 1);
    GenerateFields(ref sourceBuilder, info, hasSpans);
    sourceBuilder.AppendLine();
    GenerateConstructors(ref sourceBuilder, info, namespacePrefix, hasSpans);
    sourceBuilder.AppendLine();
    GenerateMockProperty(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateInstanceBuildMethod(ref sourceBuilder, info, hasSpans);
    sourceBuilder.AppendLine();
    GenerateSetupMethod(ref sourceBuilder, info);
    sourceBuilder.AppendLine();

    foreach (IPropertySymbol property in allProperties)
    {
      if (spanProperties.Contains(property))
      {
        GenerateSpanPropertySetter(ref sourceBuilder, property, info);
      }
      else
      {
        GeneratePropertySetters(ref sourceBuilder, infos, property, info, namespacePrefix);
      }
      sourceBuilder.AppendLine();
    }

    GenerateStaticBuildMethods(ref sourceBuilder, info, namespacePrefix, hasSpans);

    if (hasSpans)
    {
      GenerateSubstitute(ref sourceBuilder, allProperties, spanProperties, info, targetInfo, namespacePrefix);
    }

    sourceBuilder.AppendLine("}", -1);

    sourceBuilder.AppendLine("}");

    return sourceBuilder.Source;
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

  private static void GenerateFields(ref SourceBuilder sourceBuilder, BuilderInfo info, bool hasSpans)
  {
    sourceBuilder.AppendLine("private readonly global::Moq.MockBehavior _behavior;");

    if (hasSpans)
    {
      sourceBuilder.AppendLine("private readonly global::Moq.Mock<__ISubstitute> _substituteMock;");
    }

    sourceBuilder.Append("private readonly global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine("> _mock;");
  }

  private static void GenerateConstructors(ref SourceBuilder sourceBuilder, BuilderInfo info, string namespacePrefix, bool hasSpans)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("(global::Moq.MockBehavior behavior)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("_behavior = behavior;");
    if (hasSpans)
    {
      sourceBuilder.AppendLine("_substituteMock = new global::Moq.Mock<__ISubstitute>(behavior);");
    }
    sourceBuilder.Append("_mock = new global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(">(behavior);", -1);
    sourceBuilder.AppendLine("}");
    sourceBuilder.AppendLine();
    
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine($"() : this(global::{namespacePrefix}FluentMock.MoqSettings.DefaultMockBehavior)");
    sourceBuilder.AppendLine("{");
    sourceBuilder.AppendLine("}");
  }

  private static void GenerateMockProperty(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.Append("> Mock => _mock;");
  }

  private static void GenerateInstanceBuildMethod(ref SourceBuilder sourceBuilder, BuilderInfo info, bool hasSpan)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.Append(" Build() => ");

    if (hasSpan)
    {
      sourceBuilder.AppendLine("new __Substitute(_substituteMock, _mock);");
    }
    else
    {
      sourceBuilder.AppendLine("_mock.Object;");
    }
  }

  private static void GenerateSetupMethod(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderFullName);
    sourceBuilder.Append(" Setup(global::System.Action<global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(">> setup)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("setup(_mock);");
    sourceBuilder.AppendLine("return this;", -1);
    sourceBuilder.AppendLine("}");
  }

  private void GeneratePropertySetters(ref SourceBuilder sourceBuilder, ImmutableArray<TargetInfo> infos, IPropertySymbol property, BuilderInfo info, string namespacePrefix)
  {
    ITypeSymbol propertyType = property.Type;
    string propertyName = property.Name;
    string propertyTypeFullName = propertyType.ToDisplayString(s_typeDisplayFormat);

    bool isCollection = !propertyType.IsDefinition && propertyType.OriginalDefinition.SpecialType is
      SpecialType.System_Collections_Generic_IEnumerable_T or
      SpecialType.System_Collections_Generic_IReadOnlyCollection_T or
      SpecialType.System_Collections_Generic_IReadOnlyList_T;

    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append('(');
    sourceBuilder.Append(propertyTypeFullName);
    sourceBuilder.AppendLine(" value)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("_mock.Setup(x => x.");
    sourceBuilder.Append(propertyName);
    sourceBuilder.AppendLine(").Returns(value);");
    sourceBuilder.AppendLine("return this;", -1);
    sourceBuilder.AppendLine("}");

    if (!isCollection && propertyType.Kind is SymbolKind.NamedType)
    {
      foreach (TargetInfo targetInfo in infos)
      {
        (INamedTypeSymbol type, _) = targetInfo;
        bool isSameType = SymbolEqualityComparer.Default.Equals(type, propertyType);
        if (!isSameType && !type.AllInterfaces.Contains((INamedTypeSymbol)propertyType))
          continue;

        BuilderInfo propertyBuilderInfo = GetInfo(type);
        
        sourceBuilder.AppendLine();
        sourceBuilder.Append("public ");
        sourceBuilder.Append(info.BuilderName);
        sourceBuilder.Append(" Set");
        sourceBuilder.Append(property.Name);
        if (!isSameType)
        {
          sourceBuilder.Append("<T>");
        }
        sourceBuilder.Append("(global::System.Action<global::");
        sourceBuilder.Append(propertyBuilderInfo.BuilderFullName);
        sourceBuilder.Append("> buildAction)");
        if (!isSameType)
        {
          sourceBuilder.Append(" where T : class, ");
          sourceBuilder.AppendLine(propertyBuilderInfo.TargetFullName);
        }
        else
        {
          sourceBuilder.AppendLine();
        }
        sourceBuilder.AppendLine("{", 1);
        sourceBuilder.Append("return Set");
        sourceBuilder.Append(property.Name);
        sourceBuilder.Append("(global::");
        sourceBuilder.Append(propertyBuilderInfo.BuilderFullName);
        sourceBuilder.AppendLine(".Build(buildAction));", -1);
        sourceBuilder.AppendLine("}");

        sourceBuilder.AppendLine();
        sourceBuilder.Append("public ");
        sourceBuilder.Append(info.BuilderName);
        sourceBuilder.Append(" Set");
        sourceBuilder.Append(property.Name);
        if (!isSameType)
        {
          sourceBuilder.Append("<T>");
        }
        sourceBuilder.Append("(global::Moq.MockBehavior behavior, global::System.Action<global::");
        sourceBuilder.Append(propertyBuilderInfo.BuilderFullName);
        sourceBuilder.Append("> buildAction)");
        if (!isSameType)
        {
          sourceBuilder.Append(" where T : class, ");
          sourceBuilder.AppendLine(propertyBuilderInfo.TargetFullName);
        }
        else
        {
          sourceBuilder.AppendLine();
        }
        sourceBuilder.AppendLine("{", 1);
        sourceBuilder.Append("return Set");
        sourceBuilder.Append(property.Name);
        sourceBuilder.Append("(global::");
        sourceBuilder.Append(propertyBuilderInfo.BuilderFullName);
        sourceBuilder.AppendLine(".Build(behavior, buildAction));", -1);
        sourceBuilder.AppendLine("}");
      }

      return;
    }

    if (!isCollection)
      return;

    ITypeSymbol elementType = ((INamedTypeSymbol)propertyType).TypeArguments[0];
    string elementTypeFullName = elementType.ToDisplayString(s_typeDisplayFormat);
    bool hasBuilderInfo = TryGetBuilderInfo(elementType, infos, out BuilderInfo? elementBuilderInfo);

    sourceBuilder.AppendLine();
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append("(params ");
    sourceBuilder.Append(elementTypeFullName);
    sourceBuilder.AppendLine("[] values)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("return Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append("(values as global::System.Collections.Generic.IReadOnlyList<");
    sourceBuilder.Append(elementTypeFullName);
    sourceBuilder.AppendLine(">);", -1);
    sourceBuilder.AppendLine("}");

    sourceBuilder.AppendLine();
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append($"(global::System.Action<global::{namespacePrefix}FluentMock.ListBuilder<");
    sourceBuilder.Append(elementTypeFullName);
    if (hasBuilderInfo)
    {
      sourceBuilder.Append(", ");
      sourceBuilder.Append(elementBuilderInfo!.BuilderFullName);
    }
    sourceBuilder.AppendLine(">> buildAction)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("return Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append($"(global::{namespacePrefix}FluentMock.ListBuilder<");
    sourceBuilder.Append(elementTypeFullName);
    if (hasBuilderInfo)
    {
      sourceBuilder.Append(", ");
      sourceBuilder.Append(elementBuilderInfo!.BuilderFullName);
    }
    sourceBuilder.AppendLine(">.Build(buildAction));", -1);
    sourceBuilder.AppendLine("}");
  }

  private void GenerateSpanPropertySetter(ref SourceBuilder sourceBuilder, IPropertySymbol property, BuilderInfo info)
  {
    INamedTypeSymbol propertyType = (INamedTypeSymbol)property.Type;
    string propertyName = property.Name;
    ITypeSymbol argumentType = propertyType.TypeArguments[0];
    string argumentTypeFullName = argumentType.ToDisplayString(s_typeDisplayFormat);

    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append('(');
    sourceBuilder.Append(argumentTypeFullName);
    sourceBuilder.AppendLine("[] value)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("_substituteMock.Setup(x => x.");
    sourceBuilder.Append(propertyName);
    sourceBuilder.AppendLine(").Returns(value);");
    sourceBuilder.AppendLine("return this;", -1);
    sourceBuilder.AppendLine("}");

    if (argumentType.Name is "Char" && argumentType.ContainingNamespace.Name is "System")
    {
      sourceBuilder.AppendLine();
      sourceBuilder.Append("public ");
      sourceBuilder.Append(info.BuilderName);
      sourceBuilder.Append(" Set");
      sourceBuilder.Append(propertyName);
      sourceBuilder.Append('(');
      sourceBuilder.AppendLine("string value)");
      sourceBuilder.AppendLine("{", 1);
      sourceBuilder.Append("_substituteMock.Setup(x => x.");
      sourceBuilder.Append(propertyName);
      sourceBuilder.AppendLine(").Returns(value.ToCharArray());");
      sourceBuilder.AppendLine("return this;", -1);
      sourceBuilder.AppendLine("}");
    }
  }

  private static void GenerateStaticBuildMethods(ref SourceBuilder sourceBuilder, BuilderInfo info, string namespacePrefix, bool hasSpans)
  {
    sourceBuilder.Append("public static ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.Append(" Build(global::Moq.MockBehavior behavior, global::System.Action<");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("> buildAction)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("var builder = new ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("(behavior);");
    sourceBuilder.AppendLine("buildAction(builder);");
    sourceBuilder.AppendLine("return builder.Build();", -1);
    sourceBuilder.AppendLine("}");
    sourceBuilder.AppendLine();

    sourceBuilder.Append("public static ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.Append(" Build(global::System.Action<");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("> buildAction)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine($"return Build(global::{namespacePrefix}FluentMock.MoqSettings.DefaultMockBehavior, buildAction);", -1);

    if (hasSpans)
    {
      sourceBuilder.AppendLine("}");
    }
    else
    {
      sourceBuilder.AppendLine("}", -1);
    }
  }

  private static void GenerateSubstitute(ref SourceBuilder sourceBuilder, ImmutableArray<IPropertySymbol> allProperties, ImmutableArray<IPropertySymbol> spanProperties, BuilderInfo info, TargetInfo target, string namespacePrefix)
  {
    sourceBuilder.AppendLine();
    sourceBuilder.AppendLine($"public interface __ISubstitute");
    sourceBuilder.AppendLine("{", 1);

    for (int i = 0; i < spanProperties.Length; i++)
    {
      IPropertySymbol property = spanProperties[i];
      INamedTypeSymbol propertyType = (INamedTypeSymbol)property.Type;
      string propertyName = property.Name;
      string substituteTypeFullName = propertyType.TypeArguments[0].ToDisplayString(s_typeDisplayFormat);

      sourceBuilder.Append(substituteTypeFullName);
      sourceBuilder.Append("[] ");
      sourceBuilder.Append(propertyName);
      
      if (i == spanProperties.Length - 1)
      {
        sourceBuilder.AppendLine(" { get; }", -1);
      }
      else
      {
        sourceBuilder.AppendLine(" { get; }");
      }
    }
    sourceBuilder.AppendLine("}");

    sourceBuilder.AppendLine();
    sourceBuilder.Append("private class __Substitute : ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine($", global::{namespacePrefix}FluentMock.ISubstitute");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("private readonly global::Moq.Mock<__ISubstitute> _substituteMock;");
    sourceBuilder.Append("private readonly global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine("> _objectMock;");
    sourceBuilder.Append("public __Substitute(global::Moq.Mock<__ISubstitute> substituteMock, global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine("> objectMock)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("_substituteMock = substituteMock;");
    sourceBuilder.AppendLine("_objectMock = objectMock;", -1);
    sourceBuilder.AppendLine("}");
    sourceBuilder.AppendLine();
    sourceBuilder.AppendLine("public global::Moq.Mock SubstituteMock => _substituteMock;");
    sourceBuilder.AppendLine("public global::Moq.Mock ObjectMock => _objectMock;");

    foreach (IPropertySymbol property in allProperties)
    {
      sourceBuilder.AppendLine();
      sourceBuilder.Append("public ");
      sourceBuilder.Append(property.Type.ToDisplayString(s_typeDisplayFormat));
      sourceBuilder.Append(' ');
      sourceBuilder.Append(property.Name);
      sourceBuilder.Append(" => ");

      if (spanProperties.Contains(property))
      {
        sourceBuilder.Append("_substituteMock.Object.");
      }
      else
      {
        sourceBuilder.Append("_objectMock.Object.");
      }

      sourceBuilder.Append(property.Name);
      sourceBuilder.AppendLine(";");
    }

    foreach (IMethodSymbol method in target.Symbol.GetAllMethods(target.ToIgnore))
    {
      sourceBuilder.AppendLine();
      sourceBuilder.Append("public ");
      sourceBuilder.Append(method.ReturnType.ToDisplayString(s_typeDisplayFormat));
      sourceBuilder.Append(' ');
      sourceBuilder.Append(method.Name);
      sourceBuilder.Append('(');
      sourceBuilder.AppendJoin(", ", method.Parameters, static (sb, parameter) =>
      {
        switch (parameter.RefKind)
        {
          case RefKind.Ref:
            sb.Append("ref ");
            break;
          case RefKind.Out:
            sb.Append("out ");
            break;
          case RefKind.In:
            sb.Append("in ");
            break;
        }
        sb.Append(parameter.Type.ToDisplayString(s_typeDisplayFormat));
        sb.Append(' ');
        sb.Append(parameter.Name);
      });
      sourceBuilder.Append(')');
      sourceBuilder.Append(" => _objectMock.Object.");
      sourceBuilder.Append(method.Name);
      sourceBuilder.Append('(');
      sourceBuilder.AppendJoin(", ", method.Parameters, static (sb, parameter) => sb.Append(parameter.Name));
      sourceBuilder.AppendLine(");");
    }

    sourceBuilder.AppendLine(-1);

    sourceBuilder.AppendLine("}", -1);
  }

  private bool TryGetBuilderInfo(ITypeSymbol type, ImmutableArray<TargetInfo> infos, out BuilderInfo? builderInfo)
  {
    if (type.Kind is not SymbolKind.NamedType)
    {
      builderInfo = null;
      return false;
    }

    foreach (var info in infos)
    {
      if (SymbolEqualityComparer.Default.Equals(info.Symbol, type))
      {
        builderInfo = GetInfo(type);
        return true;
      }
    }

    builderInfo = null;
    return false;
  }

  private static bool HasSpans(ImmutableArray<IPropertySymbol> allProperties, out ImmutableArray<IPropertySymbol> spanProperties)
  {
    var builder = ImmutableArray.CreateBuilder<IPropertySymbol>();
    foreach (IPropertySymbol property in allProperties)
    {
      ITypeSymbol propertyType = property.Type;
      if (propertyType.IsRefLikeType && propertyType.Name is "Span" or "ReadOnlySpan" && propertyType.ContainingNamespace.Name is "System")
      {
        builder.Add(property);
      }
    }

    spanProperties = builder.ToImmutable();
    return builder.Count != 0;
  }
}
