using System;

namespace FluentMock;

[AttributeUsage(AttributeTargets.Assembly)]
public class GenerateFluentMockForAttribute : Attribute
{
  public GenerateFluentMockForAttribute(Type type)
  {
    Type = type;
  }

  public Type Type { get; }
}
