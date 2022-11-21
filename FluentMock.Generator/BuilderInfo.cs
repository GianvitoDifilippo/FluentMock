namespace FluentMock.Generator;

internal class BuilderInfo
{
  private string? _builderFullName;

  public BuilderInfo(string targetNamespace, string targetFullName, string builderName)
  {
    TargetNamespace = targetNamespace;
    TargetFullName = targetFullName;
    BuilderName = builderName;
  }

  public string TargetNamespace { get; }
  public string TargetFullName { get; }
  public string BuilderName { get; }
  public string BuilderFullName => _builderFullName ??= $"{TargetNamespace}.FluentMock.{BuilderName}";
}
