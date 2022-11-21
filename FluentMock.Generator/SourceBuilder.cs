using System.Diagnostics;
using System.Linq;
using System.Text;

namespace FluentMock.Generator;

internal struct SourceBuilder
{
	private static readonly string[] s_indentations = Enumerable
		.Range(0, 8)
		.Select(tabs => new string(' ', tabs * 2))
		.ToArray();

  private readonly StringBuilder _sb;
	private int _tabs;

	public SourceBuilder(int capacity)
	{
		_sb = new(capacity);
	}

  public readonly string Source => _sb.ToString();

  public void AddTabs(int tabs)
  {
    Debug.Assert(tabs >= 0);
    _tabs += tabs;
  }

  public void RemoveTabs(int tabs)
  {
    Debug.Assert(tabs >= 0);
    _tabs -= tabs;
  }

  public void OpenScope(string text, int tabs = 1)
  {
    Debug.Assert(tabs >= 0);

    AppendLine(text);

    _tabs += tabs;
    AppendLine("{");
  }

  public void CloseScope(string text, int tabs = 1)
  {
    Debug.Assert(tabs >= 0);

    _tabs -= tabs;
    AppendLine(text);

    AppendLine("}");
  }

  public void OpenScope(int tabs = 1)
  {
    Debug.Assert(tabs >= 0);

    AppendLine();

    _tabs += tabs;
    AppendLine("{");
  }

  public void CloseScope(int tabs = 1)
  {
    Debug.Assert(tabs >= 0);

    _tabs -= tabs;

    AppendLine("}");
  }

  public void Append(string text)
  {
    _sb.Append(text);
  }

  public void Append(char c)
  {
    _sb.Append(c);
  }

  public void AppendLine()
  {
    _sb.AppendLine();
    _sb.Append(s_indentations[_tabs]);
  }

  public void AppendLine(string text)
  {
    _sb.AppendLine(text);
    _sb.Append(s_indentations[_tabs]);
  }
}
