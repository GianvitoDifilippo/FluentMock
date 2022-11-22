using System;

namespace FluentMock;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true)]
public class GenerateFluentMockForAttribute : Attribute
{
  public GenerateFluentMockForAttribute(Type type)
  {
    Type = type;
  }

  public Type Type { get; }
}
