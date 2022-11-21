using System;
using System.Collections.Immutable;
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

  public void Append(string text)
  {
    _sb.Append(text);
  }

  public void Append(char c)
  {
    _sb.Append(c);
  }

  public void AppendLine(int tabs = 0)
  {
    _tabs += tabs;
    Debug.Assert(_tabs >= 0);

    _sb.AppendLine();
    _sb.Append(s_indentations[_tabs]);
  }

  public void AppendLine(string text, int tabs = 0)
  {
    _tabs += tabs;
    Debug.Assert(_tabs >= 0);

    _sb.AppendLine(text);
    _sb.Append(s_indentations[_tabs]);
  }

  public void AppendJoin<T>(string separator, ImmutableArray<T> array, Action<StringBuilder, T> append)
  {
    ImmutableArray<T>.Enumerator enumerator = array.GetEnumerator();

    if (!enumerator.MoveNext())
      return;

    append(_sb, enumerator.Current);

    while (enumerator.MoveNext())
    {
      _sb.Append(separator);
      append(_sb, enumerator.Current);
    }
  }
}
