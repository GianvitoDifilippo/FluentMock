﻿using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FluentMock.Generator;

public class FluentMockGeneratorTests
{
  private static readonly MetadataReference[] s_references = AppDomain.CurrentDomain
    .GetAssemblies()
    .Where(sssembly => !sssembly.IsDynamic)
    .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
    .ToArray();

  [Fact]
  public void ShouldGenerateForEmptyTarget()
  {
    // Arrange
    string source = """
      using FluentMock;

      namespace ClassLibrary
      {
        public interface IMyInterface
        {
        }
      }

      namespace Test
      {
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyInterface))]
        class Config { }
      }
      """;


    // Act
    Compilation compilation = CompileWithGenerator(source);

    // Assert
    compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    INamedTypeSymbol type = compilation.GetTypeByMetadataName("ClassLibrary.IMyInterface")!;
    INamedTypeSymbol builderType = compilation.GetTypeByMetadataName("ClassLibrary.FluentMock.MyInterfaceBuilder")!;

    builderType.ShouldMatchBuilderSpecification(type);

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && method.Name != "Build");

    instanceMethods.Should().HaveCount(0);
  }

  [Fact]
  public void ShouldGenerateForSimpleProperty()
  {
    // Arrange
    string source = """
      using FluentMock;

      namespace ClassLibrary
      {
        public interface IMyInterface
        {
          string Name { get; }
        }
      }

      namespace Test
      {
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyInterface))]
        class Config { }
      }
      """;


    // Act
    Compilation compilation = CompileWithGenerator(source);

    // Assert
    compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    INamedTypeSymbol type = compilation.GetTypeByMetadataName("ClassLibrary.IMyInterface")!;
    INamedTypeSymbol builderType = compilation.GetTypeByMetadataName("ClassLibrary.FluentMock.MyInterfaceBuilder")!;

    builderType.ShouldMatchBuilderSpecification(type);

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && method.Name != "Build");

    instanceMethods.Should().HaveCount(1)
      .And.ContainSingle(method =>
        method.Name == "SetName" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "string");
  }

  [Fact]
  public void ShouldGenerateForListProperty()
  {
    // Arrange
    string source = """
      using FluentMock;
      using System.Collections.Generic;

      namespace ClassLibrary
      {
        public interface IMyInterface
        {
          IReadOnlyList<string> Names { get; }
        }
      }

      namespace Test
      {
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyInterface))]
        class Config { }
      }
      """;


    // Act
    Compilation compilation = CompileWithGenerator(source);

    // Assert
    compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    INamedTypeSymbol type = compilation.GetTypeByMetadataName("ClassLibrary.IMyInterface")!;
    INamedTypeSymbol builderType = compilation.GetTypeByMetadataName("ClassLibrary.FluentMock.MyInterfaceBuilder")!;

    builderType.ShouldMatchBuilderSpecification(type);

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && method.Name != "Build");

    instanceMethods.Should().HaveCount(3)
      .And.ContainSingle(method =>
        method.Name == "SetNames" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "System.Collections.Generic.IReadOnlyList<string>")
      .And.ContainSingle(method =>
        method.Name == "SetNames" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].IsParams &&
        method.Parameters[0].Type.ToDisplayString(null) == "string[]")
      .And.ContainSingle(method =>
        method.Name == "SetNames" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "System.Action<FluentMock.ListBuilder<string>>");
  }

  [Fact]
  public void ShouldGenerateForMethod()
  {
    // Arrange
    string source = """
      using FluentMock;

      namespace ClassLibrary
      {
        public interface IMyInterface
        {
          string Method(int arg1, char arg2);
        }
      }

      namespace Test
      {
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyInterface))]
        class Config { }
      }
      """;


    // Act
    Compilation compilation = CompileWithGenerator(source);

    // Assert
    compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    INamedTypeSymbol type = compilation.GetTypeByMetadataName("ClassLibrary.IMyInterface")!;
    INamedTypeSymbol builderType = compilation.GetTypeByMetadataName("ClassLibrary.FluentMock.MyInterfaceBuilder")!;

    builderType.ShouldMatchBuilderSpecification(type);

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && method.Name != "Build");

    instanceMethods.Should().HaveCount(1)
      .And.ContainSingle(method =>
        method.Name == "SetMethod" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "ClassLibrary.FluentMock._MethodDelegate");
  }

  [Fact]
  public void ShouldGenerateForBuilderProperty()
  {
    // Arrange
    string source = """
      using FluentMock;

      namespace ClassLibrary
      {
        public interface IMyInterface
        {
          IMyOtherInterface Other { get; }
        }

        public interface IMyOtherInterface { }
      }

      namespace Test
      {
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyInterface))]
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyOtherInterface))]
        class Config { }
      }
      """;


    // Act
    Compilation compilation = CompileWithGenerator(source);

    // Assert
    compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    INamedTypeSymbol type = compilation.GetTypeByMetadataName("ClassLibrary.IMyInterface")!;
    INamedTypeSymbol builderType = compilation.GetTypeByMetadataName("ClassLibrary.FluentMock.MyInterfaceBuilder")!;

    builderType.ShouldMatchBuilderSpecification(type);

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && method.Name != "Build");

    instanceMethods.Should().HaveCount(2)
      .And.ContainSingle(method =>
        method.Name == "SetOther" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "ClassLibrary.IMyOtherInterface")
      .And.ContainSingle(method =>
        method.Name == "SetOther" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "System.Action<ClassLibrary.FluentMock.MyOtherInterfaceBuilder>");
  }

  [Fact]
  public void ShouldGenerateForBuilderListProperty()
  {
    // Arrange
    string source = """
      using FluentMock;
      using System.Collections.Generic;

      namespace ClassLibrary
      {
        public interface IMyInterface
        {
          IEnumerable<IMyOtherInterface> Others { get; }
        }

        public interface IMyOtherInterface { }
      }

      namespace Test
      {
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyInterface))]
        [GenerateFluentMockFor(typeof(ClassLibrary.IMyOtherInterface))]
        class Config { }
      }
      """;


    // Act
    Compilation compilation = CompileWithGenerator(source);

    // Assert
    compilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    INamedTypeSymbol type = compilation.GetTypeByMetadataName("ClassLibrary.IMyInterface")!;
    INamedTypeSymbol builderType = compilation.GetTypeByMetadataName("ClassLibrary.FluentMock.MyInterfaceBuilder")!;

    builderType.ShouldMatchBuilderSpecification(type);

    IEnumerable<IMethodSymbol> instanceMethods = builderType
      .GetMembers()
      .OfType<IMethodSymbol>()
      .Where(method => method.MethodKind is MethodKind.Ordinary && !method.IsStatic && method.Name != "Build");

    instanceMethods.Should().HaveCount(3)
      .And.ContainSingle(method =>
        method.Name == "SetOthers" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "System.Collections.Generic.IEnumerable<ClassLibrary.IMyOtherInterface>")
      .And.ContainSingle(method =>
        method.Name == "SetOthers" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].IsParams &&
        method.Parameters[0].Type.ToDisplayString(null) == "ClassLibrary.IMyOtherInterface[]")
      .And.ContainSingle(method =>
        method.Name == "SetOthers" &&
        method.ReturnType.Equals(builderType, SymbolEqualityComparer.Default) &&
        method.Parameters.Length == 1 &&
        method.Parameters[0].Type.ToDisplayString(null) == "System.Action<FluentMock.ListBuilder<ClassLibrary.IMyOtherInterface, ClassLibrary.FluentMock.MyOtherInterfaceBuilder>>");
  }

  private static Compilation CompileWithGenerator(string source)
  {
    SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
    Compilation compilation = CSharpCompilation.Create(
      assemblyName: "Test",
      syntaxTrees: new[] { syntaxTree },
      references: s_references,
      options: new(OutputKind.DynamicallyLinkedLibrary));

    GeneratorDriver driver = CSharpGeneratorDriver.Create(new FluentMockGenerator());
    driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);

    return outputCompilation;
  }
}