namespace FluentMock.Generator;

internal class BuilderInfo
{
  public BuilderInfo(string targetNamespace, string targetFullName, string builderName)
  {
    TargetNamespace = targetNamespace;
    TargetFullName = targetFullName;
    BuilderName = builderName;
    BuilderFullName = $"{TargetNamespace}.FluentMock.{BuilderName}";
  }

  public string TargetNamespace { get; }
  public string TargetFullName { get; }
  public string BuilderName { get; }
  public string BuilderFullName { get; }
}
