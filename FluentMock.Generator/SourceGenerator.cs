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
          where TListBuilder : __ListBuilderBase<T, TListBuilder>
        {
          private readonly global::Moq.MockBehavior _behavior;
          private readonly global::System.Collections.Generic.List<T> _list;

          protected __ListBuilderBase(global::Moq.MockBehavior behavior)
          {
            _behavior = behavior;
            _list = new();
          }

          protected abstract TListBuilder This { get; }

          public global::System.Collections.Generic.IReadOnlyList<T> Build() => _list;

          public TListBuilder Add(T item)
          {
            _list.Add(item);
            return This;
          }

          public static global::System.Collections.Generic.IReadOnlyList<T> Build(global::Moq.MockBehavior behavior, global::System.Action<TListBuilder> buildAction)
          {
            TListBuilder builder = (TListBuilder)global::System.Activator.CreateInstance(typeof(TListBuilder), new object[] { behavior });
            buildAction(builder);
            return builder.Build();
          }

          public static global::System.Collections.Generic.IReadOnlyList<T> Build(global::System.Action<TListBuilder> buildAction)
          {
            return Build(global::Moq.MockBehavior.Loose, buildAction);
          }
        }

        internal abstract class __ListBuilderBase<T, TBuilder, TListBuilder> : __ListBuilderBase<T, TListBuilder>
          where TBuilder : IBuilder<T>
          where TListBuilder : __ListBuilderBase<T, TBuilder, TListBuilder>
        {
          protected __ListBuilderBase(global::Moq.MockBehavior behavior)
            : base(behavior)
          {
          }

          public TListBuilder Add(global::Moq.MockBehavior behavior, global::System.Action<TBuilder> buildAction)
          {
            TBuilder builder = (TBuilder)global::System.Activator.CreateInstance(typeof(TBuilder), new object[] { behavior });
            buildAction(builder);
            return Add(builder.Build());
          }

          public TListBuilder Add(global::System.Action<TBuilder> buildAction)
          {
            return Add(global::Moq.MockBehavior.Loose, buildAction);
          }
        }

        internal sealed class ListBuilder<T> : __ListBuilderBase<T, ListBuilder<T>>
        {
          public ListBuilder()
            : base(global::Moq.MockBehavior.Loose)
          {
          }

          public ListBuilder(global::Moq.MockBehavior behavior)
            : base(behavior)
          {
          }

          protected override ListBuilder<T> This => this;
        }

        internal sealed class ListBuilder<T, TBuilder> : __ListBuilderBase<T, TBuilder, ListBuilder<T, TBuilder>>
          where TBuilder : IBuilder<T>
        {
          public ListBuilder()
            : base(global::Moq.MockBehavior.Loose)
          {
          }

          public ListBuilder(global::Moq.MockBehavior behavior)
            : base(behavior)
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
      if (member is IMethodSymbol method && method.MethodKind is MethodKind.Ordinary && method.RefKind is RefKind.None)
      {
        GenerateDelegate(ref sourceBuilder, (IMethodSymbol)member);
        appendLineForDelegates = true;
      }
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
    GenerateConstructor(ref sourceBuilder, info);
    sourceBuilder.AppendLine();
    GenerateInstanceBuildMethod(ref sourceBuilder, info);
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

  public delegate ref int A();

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

  private static void GenerateConstructor(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.BuilderName);
    sourceBuilder.AppendLine("(global::Moq.MockBehavior behavior = global::Moq.MockBehavior.Loose)");
    sourceBuilder.AppendLine("{", 1);
    sourceBuilder.AppendLine("_behavior = behavior;");
    sourceBuilder.Append("_mock = new global::Moq.Mock<");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(">(behavior);", -1);
    sourceBuilder.AppendLine("}");
  }

  private static void GenerateInstanceBuildMethod(ref SourceBuilder sourceBuilder, BuilderInfo info)
  {
    sourceBuilder.Append("public ");
    sourceBuilder.Append(info.TargetFullName);
    sourceBuilder.AppendLine(" Build() => _mock.Object;");
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

    if (!isCollection && propertyType.Kind is SymbolKind.NamedType && types.Contains(propertyType))
    {
      BuilderInfo propertyBuilderInfo = GetInfo(propertyType);

      sourceBuilder.AppendLine();
      sourceBuilder.Append("public ");
      sourceBuilder.Append(info.BuilderName);
      sourceBuilder.Append(" Set");
      sourceBuilder.Append(property.Name);
      sourceBuilder.Append("(global::System.Action<global::");
      sourceBuilder.Append(propertyBuilderInfo.BuilderFullName);
      sourceBuilder.AppendLine("> buildAction)");
      sourceBuilder.AppendLine("{", 1);
      sourceBuilder.Append("return Set");
      sourceBuilder.Append(property.Name);
      sourceBuilder.Append("(global::");
      sourceBuilder.Append(propertyBuilderInfo.BuilderFullName);
      sourceBuilder.AppendLine(".Build(buildAction));", -1);
      sourceBuilder.AppendLine("}");

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
    sourceBuilder.AppendLine("return Build(global::Moq.MockBehavior.Loose, buildAction);", -1);
    sourceBuilder.AppendLine("}", -1);
  }
}
