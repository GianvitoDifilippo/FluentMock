using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FluentMock.Generator;

public class FluentMockGeneratorTests
{
  [Fact]
  public void Test1()
  {
    string source1 = """
      namespace Test
      {
        internal interface IMyInterface
        {
          string Name { get; }
          IMyInterface Child { get; }
        }
      }
      """;

    string source2 = """
      using FluentMock;
      using Test;

      [assembly: GenerateFluentMockFor(typeof(IMyInterface))]
      """;

    var syntaxTree1 = CSharpSyntaxTree.ParseText(source1);
    var syntaxTree2 = CSharpSyntaxTree.ParseText(source2);
    var references = AppDomain.CurrentDomain
      .GetAssemblies()
      .Where(assembly => !assembly.IsDynamic)
      .Select(assembly => MetadataReference.CreateFromFile(assembly.Location));

    var compilation = CSharpCompilation.Create("Test", new[] { syntaxTree1, syntaxTree2 }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var diagnostics = compilation.GetDiagnostics();

    var generator = new FluentMockGenerator();

    CSharpGeneratorDriver.Create(generator).RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var outputDiagnostics);
  }


}