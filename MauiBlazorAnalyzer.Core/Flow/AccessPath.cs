using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Flow;
public sealed record AccessPath(ISymbol Base, ImmutableArray<IFieldSymbol> Fields);
