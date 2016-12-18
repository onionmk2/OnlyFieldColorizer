using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using CSharp = Microsoft.CodeAnalysis.CSharp;
using VB = Microsoft.CodeAnalysis.VisualBasic;

namespace SemanticColorizer
{

    [Export(typeof(ITaggerProvider))]
    [ContentType("CSharp")]
    [ContentType("Basic")]
    [TagType(typeof(IClassificationTag))]
    internal class SemanticColorizerProvider : ITaggerProvider
    {
#pragma warning disable CS0649
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry; // Set via MEF
#pragma warning restore CS0649

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag {
            return (ITagger<T>)new SemanticColorizer(buffer, ClassificationRegistry);
        }
    }

    class SemanticColorizer : ITagger<IClassificationTag>
    {
        private readonly ITextBuffer _theBuffer;
        private readonly IClassificationType _fieldType;
        private readonly IClassificationType _enumFieldType;
        private readonly IClassificationType _extensionMethodType;
        private readonly IClassificationType _staticMethodType;
        private readonly IClassificationType _normalMethodType;
        private IClassificationType _constructorType;
        private readonly IClassificationType _typeParameterType;
        private readonly IClassificationType _parameterType;
        private readonly IClassificationType _namespaceType;
        private readonly IClassificationType _propertyType;
        private readonly IClassificationType _localType;
        private readonly IClassificationType _typeSpecialType;
        private readonly IClassificationType _typeNormalType;
        private Cache _cache;
#pragma warning disable CS0067
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
#pragma warning restore CS0067

        internal SemanticColorizer(ITextBuffer buffer, IClassificationTypeRegistryService registry) {
            _theBuffer = buffer;
            _fieldType = registry.GetClassificationType(Constants.FieldFormat);
            _enumFieldType = registry.GetClassificationType(Constants.EnumFieldFormat);
            _extensionMethodType = registry.GetClassificationType(Constants.ExtensionMethodFormat);
            _staticMethodType = registry.GetClassificationType(Constants.StaticMethodFormat);
            _normalMethodType = registry.GetClassificationType(Constants.NormalMethodFormat);
            _constructorType = registry.GetClassificationType(Constants.ConstructorFormat);
            _typeParameterType = registry.GetClassificationType(Constants.TypeParameterFormat);
            _parameterType = registry.GetClassificationType(Constants.ParameterFormat);
            _namespaceType = registry.GetClassificationType(Constants.NamespaceFormat);
            _propertyType = registry.GetClassificationType(Constants.PropertyFormat);
            _localType = registry.GetClassificationType(Constants.LocalFormat);
            _typeSpecialType = registry.GetClassificationType(Constants.TypeSpecialFormat);
            _typeNormalType = registry.GetClassificationType(Constants.TypeNormalFormat);
        }

        public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans) {
            if (spans.Count == 0) {
                return Enumerable.Empty<ITagSpan<IClassificationTag>>();
            }
            if (_cache == null || _cache.Snapshot != spans[0].Snapshot) {
                // this makes me feel dirty, but otherwise it will not
                // work reliably, as TryGetSemanticModel() often will return false
                // should make this into a completely async process somehow
                var task = Cache.Resolve(_theBuffer, spans[0].Snapshot);
                try
                {
                    task.Wait();
                }
                catch (Exception)
                {
                    // TODO: report this to someone.
                    return Enumerable.Empty<ITagSpan<IClassificationTag>>();
                }
                _cache = task.Result;
                if( _cache == null)
                {
                    // TODO: report this to someone.
                    return Enumerable.Empty<ITagSpan<IClassificationTag>>();
                }
            }
            return GetTagsImpl(_cache, spans);
        }

        private IEnumerable<ITagSpan<IClassificationTag>> GetTagsImpl(
              Cache doc,
              NormalizedSnapshotSpanCollection spans) {
            var snapshot = spans[0].Snapshot;

            IEnumerable<ClassifiedSpan> identifiers =
              GetIdentifiersInSpans(doc.Workspace, doc.SemanticModel, spans);

            foreach (var id in identifiers) {
                var node = GetExpression(doc.SyntaxRoot.FindNode(id.TextSpan));
                var symbol = doc.SemanticModel.GetSymbolInfo(node).Symbol;
                if (symbol == null) symbol = doc.SemanticModel.GetDeclaredSymbol(node);
                if (symbol == null) {
                    continue;
                }
                switch (symbol.Kind) {
                    case SymbolKind.Field:
                        if (symbol.ContainingType.TypeKind != TypeKind.Enum) {
                            yield return id.TextSpan.ToTagSpan(snapshot, _fieldType);
                        }
                        else {
                            yield return id.TextSpan.ToTagSpan(snapshot, _enumFieldType);
                        }
                        break;
                    case SymbolKind.Method:
                        if (IsExtensionMethod(symbol)) {
                            yield return id.TextSpan.ToTagSpan(snapshot, _extensionMethodType);
                        }
                        else if (symbol.IsStatic) {
                            yield return id.TextSpan.ToTagSpan(snapshot, _staticMethodType);
                        }
                        else {
                            yield return id.TextSpan.ToTagSpan(snapshot, _normalMethodType);
                        }
                        break;
                    case SymbolKind.TypeParameter:
                        yield return id.TextSpan.ToTagSpan(snapshot, _typeParameterType);
                        break;
                    case SymbolKind.Parameter:
                        yield return id.TextSpan.ToTagSpan(snapshot, _parameterType);
                        break;
                    case SymbolKind.Namespace:
                        yield return id.TextSpan.ToTagSpan(snapshot, _namespaceType);
                        break;
                    case SymbolKind.Property:
                        yield return id.TextSpan.ToTagSpan(snapshot, _propertyType);
                        break;
                    case SymbolKind.Local:
                        yield return id.TextSpan.ToTagSpan(snapshot, _localType);
                        break;
                    case SymbolKind.NamedType:
                        if (IsSpecialType(symbol)) {
                            yield return id.TextSpan.ToTagSpan(snapshot, _typeSpecialType);
                        }
                        else {
                            yield return id.TextSpan.ToTagSpan(snapshot, _typeNormalType);
                        }
                        break;
                }
            }
        }

        private bool IsSpecialType(ISymbol symbol) {
            var type = (INamedTypeSymbol)symbol;
            return type.SpecialType != SpecialType.None;
        }

        private bool IsExtensionMethod(ISymbol symbol) {
            var method = (IMethodSymbol)symbol;
            return method.IsExtensionMethod;
        }

        private SyntaxNode GetExpression(SyntaxNode node) {
            if (node.CSharpKind() == CSharp.SyntaxKind.Argument) {
                return ((CSharp.Syntax.ArgumentSyntax)node).Expression;
            }
            else if (node.CSharpKind() == CSharp.SyntaxKind.AttributeArgument) {
                return ((CSharp.Syntax.AttributeArgumentSyntax)node).Expression;
            }
            else if (node.VbKind() == VB.SyntaxKind.SimpleArgument) {
                return ((VB.Syntax.SimpleArgumentSyntax)node).Expression;
            }
            return node;
        }

        private IEnumerable<ClassifiedSpan> GetIdentifiersInSpans(
              Workspace workspace, SemanticModel model,
              NormalizedSnapshotSpanCollection spans) {
            var comparer = StringComparer.InvariantCultureIgnoreCase;
            var classifiedSpans =
              spans.SelectMany(span => {
                  var textSpan = TextSpan.FromBounds(span.Start, span.End);
                  return Classifier.GetClassifiedSpans(model, textSpan, workspace);
              });

            return from cs in classifiedSpans
                   where comparer.Compare(cs.ClassificationType, "identifier") == 0
                   select cs;
        }

        private class Cache
        {
            public Workspace Workspace { get; private set; }
            public Document Document { get; private set; }
            public SemanticModel SemanticModel { get; private set; }
            public SyntaxNode SyntaxRoot { get; private set; }
            public ITextSnapshot Snapshot { get; private set; }

            private Cache() {}

            public static async Task<Cache> Resolve(ITextBuffer buffer, ITextSnapshot snapshot) {
                var workspace = buffer.GetWorkspace();
                var document = snapshot.GetOpenDocumentInCurrentContextWithChanges();
                if (document == null)
                {
                    // Razor cshtml returns a null document for some reason.
                    return null;
                }

                // the ConfigureAwait() calls are important,
                // otherwise we'll deadlock VS
                var semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);
                var syntaxRoot = await document.GetSyntaxRootAsync().ConfigureAwait(false);
                return new Cache {
                    Workspace = workspace,
                    Document = document,
                    SemanticModel = semanticModel,
                    SyntaxRoot = syntaxRoot,
                    Snapshot = snapshot
                };
            }
        }
    }
}
