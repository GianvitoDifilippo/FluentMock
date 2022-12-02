using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace FluentMock.Generator;

public record TargetInfo(INamedTypeSymbol Symbol, IReadOnlyCollection<string> ToIgnore);