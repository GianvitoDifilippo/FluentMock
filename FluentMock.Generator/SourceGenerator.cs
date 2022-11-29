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

  public static readonly SourceGenerator Instance = new();

  private readonly Dictionary<ITypeSymbol, BuilderInfo> _infoCache;

  private SourceGenerator()
  {
    _infoCache = new(SymbolEqualityComparer.Default);
  }

  public string GenerateMoqSettings()
  {
    return """
      namespace FluentMock
      {
        public static class MoqSettings
        {
          public static global::Moq.MockBehavior DefaultMockBehavior = global::Moq.MockBehavior.Strict;
        }
      }
      """;
  }

  public string GenerateIBuilder()
  {
    return """
      namespace FluentMock
      {
        public interface IBuilder<out T>
        {
          T Build();
        }
      }
      """;
  }

  public string GenerateListBuilder()
  {
    return """
      namespace FluentMock
      {
        internal abstract class __ListBuilderBase<T, TListBuilder>
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

        internal abstract class __ListBuilderBase<T, TBuilder, TListBuilder> : __ListBuilderBase<T, TListBuilder>
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
            return Add(global::FluentMock.MoqSettings.DefaultMockBehavior, buildAction);
          }

          public TListBuilder Add<TDerivedBuilder>(global::System.Action<TDerivedBuilder> buildAction)
            where TDerivedBuilder : IBuilder<T>
          {
            return Add<TDerivedBuilder>(global::FluentMock.MoqSettings.DefaultMockBehavior, buildAction);
          }
        }

        internal sealed class ListBuilder<T> : __ListBuilderBase<T, ListBuilder<T>>
        {
          public ListBuilder()
          {
          }

          protected override ListBuilder<T> This => this;
        }

        internal sealed class ListBuilder<T, TBuilder> : __ListBuilderBase<T, TBuilder, ListBuilder<T, TBuilder>>
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

  public string GenerateObjectBuilder(in ImmutableArray<ITypeSymbol> types, ITypeSymbol type)
  {
    BuilderInfo info = GetInfo(type);

    SourceBuilder sourceBuilder = new(100); // TODO: Estimate capacity

    sourceBuilder.Append("namespace ");
    sourceBuilder.Append(info.TargetNamespace);
    sourceBuilder.AppendLine(".FluentMock");
    sourceBuilder.AppendLine("{", 1);

    bool appendLineForDelegates = false;
    IEnumerable<ISymbol> allMembers = type.GetAllMembers();

    foreach (ISymbol member in allMembers)
    {
      if (member is not IMethodSymbol method || method.MethodKind is not MethodKind.Ordinary || method.RefKind is not RefKind.None)
        continue;

      bool hasRefStructParams = false;
      foreach (IParameterSymbol parameter in method.Parameters)
      {
        if (parameter.Type.IsRefLikeType)
        {
          hasRefStructParams = true;
          break;
        }
      }

      if (hasRefStructParams)
        continue;

      GenerateDelegate(ref sourceBuilder, (IMethodSymbol)member);
      appendLineForDelegates = true;
    }
    if (appendLineForDelegates)
    {
      sourceBuilder.AppendLine();
    }

    sourceBuilder.Append("internal class ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" : global::FluentMock.IBuilder<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(">");
    sourceBuilder.AppendLine("{", 1);
    GenerateFields(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateConstructors(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateMockProperty(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateInstanceBuildMethod(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateSetupMethod(ref sourceBuilder, info);
    sourceBuilder.AppendLine();

    foreach (ISymbol member in allMembers)
    {
      if (member is IPropertySymbol property)
      {
        GeneratePropertySetters(ref sourceBuilder, in types, property, info);
        sourceBuilder.AppendLine();
      }
      else if (member is IMethodSymbol method && method.MethodKind is MethodKind.Ordinary && method.RefKind is RefKind.None)
      {
        bool hasRefStructParams = false;
        foreach (IParameterSymbol parameter in method.Parameters)
        {
          if (parameter.Type.IsRefLikeType)
          {
            hasRefStructParams = true;
            break;
          }
        }

        if (hasRefStructParams)
          continue;

        GenerateMethodSetter(ref sourceBuilder, (IMethodSymbol)member, info);
        sourceBuilder.AppendLine();
      }
    }

    GenerateStaticBuildMethods(ref sourceBuilder, info);

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

  private static void GenerateDelegate(ref SourceBuilder sourceBuilder, IMethodSymbol method)
  {
    sourceBuilder.Append("internal delegate ");
    sourceBuilder.Append(method.ReturnType.ToDisplayString(s_typeDisplayFormat));
    sourceBuilder.Append(" _");
    sourceBuilder.Append(method.Name);
    sourceBuilder.Append("Delegate(");
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
    sourceBuilder.AppendLine(");");
  }

  private static void GenerateFields(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("private readonly global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine("> _mock;");
    sourceBuilder.AppendLine("private readonly global::Moq.MockBehavior _behavior;");
  }

  private static void GenerateConstructors(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("(global::Moq.MockBehavior behavior)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("_behavior = behavior;");
    sourceBuilder.Append("_mock = new global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(">(behavior);", -1);
    sourceBuilder.AppendLine("}");
    sourceBuilder.AppendLine();
    
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("() : this(global::FluentMock.MoqSettings.DefaultMockBehavior)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("}");
  }

  private static void GenerateMockProperty(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.Append("> Mock => _mock;");
  }

  private static void GenerateInstanceBuildMethod(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(" Build() => _mock.Object;");
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

  private void GeneratePropertySetters(ref SourceBuilder sourceBuilder, in ImmutableArray<ITypeSymbol> types, IPropertySymbol property, BuilderInfo info)
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
      foreach (ITypeSymbol type in types)
      {
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
    BuilderInfo? elementBuilderInfo = elementType.Kind is SymbolKind.NamedType && types.Contains(elementType)
      ? GetInfo(elementType)
      : null;

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
    sourceBuilder.Append("(global::System.Action<global::FluentMock.ListBuilder<");
    sourceBuilder.Append(elementTypeFullName);
    if (elementBuilderInfo is not null)
    {
      sourceBuilder.Append(", ");
      sourceBuilder.Append(elementBuilderInfo.BuilderFullName);
    }
    sourceBuilder.AppendLine(">> buildAction)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("return Set");
    sourceBuilder.Append(propertyName);
    sourceBuilder.Append("(global::FluentMock.ListBuilder<");
    sourceBuilder.Append(elementTypeFullName);
    if (elementBuilderInfo is not null)
    {
      sourceBuilder.Append(", ");
      sourceBuilder.Append(elementBuilderInfo.BuilderFullName);
    }
    sourceBuilder.AppendLine(">.Build(buildAction));", -1);
    sourceBuilder.AppendLine("}");
  }

  private static void GenerateMethodSetter(ref SourceBuilder sourceBuilder, IMethodSymbol method, BuilderInfo info)
  {
    string methodName = method.Name;

    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.Append(" Set");
    sourceBuilder.Append(methodName);
    sourceBuilder.Append("(_");
    sourceBuilder.Append(methodName);
    sourceBuilder.Append("Delegate @delegate");
    sourceBuilder.AppendLine(")");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.Append("_mock.Setup(x => x.");
    sourceBuilder.Append(methodName);
    sourceBuilder.Append('(');
    sourceBuilder.AppendJoin(", ", method.Parameters, static (sb, parameter) =>
    {
      bool isRef = false;
      switch (parameter.RefKind)
      {
        case RefKind.Ref:
          sb.Append("ref ");
          isRef = true;
          break;
        case RefKind.Out:
          sb.Append("out ");
          isRef = true;
          break;
        case RefKind.In:
          sb.Append("in ");
          isRef = true;
          break;
      }

      if (isRef)
      {
        sb.Append("global::Moq.It.Ref<");
        sb.Append(parameter.Type.ToDisplayString(s_typeDisplayFormat));
        sb.Append(">.IsAny");
      }
      else
      {
        sb.Append("global::Moq.It.IsAny<");
        sb.Append(parameter.Type.ToDisplayString(s_typeDisplayFormat));
        sb.Append(">()");
      }
    });
    sourceBuilder.AppendLine(")).Returns(@delegate);");
    sourceBuilder.AppendLine("return this;", -1);
    sourceBuilder.AppendLine("}");
  }

  private static void GenerateStaticBuildMethods(ref SourceBuilder sourceBuilder, BuilderInfo info)
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
    sourceBuilder.AppendLine("return Build(global::FluentMock.MoqSettings.DefaultMockBehavior, buildAction);", -1);
    sourceBuilder.AppendLine("}", -1);
  }
}
