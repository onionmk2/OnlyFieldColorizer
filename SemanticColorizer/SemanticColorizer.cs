using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using ArgumentSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax;
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
        [Import] internal IClassificationTypeRegistryService ClassificationRegistry; // Set via MEF
#pragma warning restore CS0649

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            return (ITagger<T>) new SemanticColorizer(buffer, ClassificationRegistry);
        }
    }

    internal class SemanticColorizer : ITagger<IClassificationTag>
    {
        private readonly IClassificationType _fieldType;
        private readonly ITextBuffer _theBuffer;
        private Cache _cache;

        internal SemanticColorizer(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            _theBuffer = buffer;
            _fieldType = registry.GetClassificationType(Constants.FieldFormat);
        }
#pragma warning disable CS0067
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
#pragma warning restore CS0067

        public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
            {
                return Enumerable.Empty<ITagSpan<IClassificationTag>>();
            }
            if (_cache == null || _cache.Snapshot != spans[0].Snapshot)
            {
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
                if (_cache == null)
                {
                    // TODO: report this to someone.
                    return Enumerable.Empty<ITagSpan<IClassificationTag>>();
                }
            }
            return GetTagsImpl(_cache, spans);
        }

        private IEnumerable<ITagSpan<IClassificationTag>> GetTagsImpl(
            Cache doc,
            NormalizedSnapshotSpanCollection spans)
        {
            var snapshot = spans[0].Snapshot;

            var identifiers =
                        GetIdentifiersInSpans(doc.Workspace, doc.SemanticModel, spans);

            foreach (var id in identifiers)
            {
                var node = GetExpression(doc.SyntaxRoot.FindNode(id.TextSpan));
                var symbol = doc.SemanticModel.GetSymbolInfo(node).Symbol;
                if (symbol == null)
                {
                    symbol = doc.SemanticModel.GetDeclaredSymbol(node);
                }
                if (symbol == null)
                {
                    continue;
                }
                if (symbol.Kind == SymbolKind.Field && symbol.ContainingType.TypeKind == TypeKind.Class)
                {
                    yield return id.TextSpan.ToTagSpan(snapshot, _fieldType);
                }
            }
        }

        private SyntaxNode GetExpression(SyntaxNode node)
        {
            if (node.CSharpKind() == SyntaxKind.Argument)
            {
                return ((ArgumentSyntax) node).Expression;
            }
            if (node.CSharpKind() == SyntaxKind.AttributeArgument)
            {
                return ((AttributeArgumentSyntax) node).Expression;
            }
            if (node.VbKind() == VB.SyntaxKind.SimpleArgument)
            {
                return ((SimpleArgumentSyntax) node).Expression;
            }
            return node;
        }

        private IEnumerable<ClassifiedSpan> GetIdentifiersInSpans(
            Workspace workspace, SemanticModel model,
            NormalizedSnapshotSpanCollection spans)
        {
            var comparer = StringComparer.InvariantCultureIgnoreCase;
            var classifiedSpans =
                        spans.SelectMany(span =>
                        {
                            var textSpan = TextSpan.FromBounds(span.Start, span.End);
                            return Classifier.GetClassifiedSpans(model, textSpan, workspace);
                        });

            return from cs in classifiedSpans
                   where comparer.Compare(cs.ClassificationType, "identifier") == 0
                   select cs;
        }

        private class Cache
        {
            private Cache()
            {
            }

            public Workspace Workspace { get; private set; }
            public Document Document { get; private set; }
            public SemanticModel SemanticModel { get; private set; }
            public SyntaxNode SyntaxRoot { get; private set; }
            public ITextSnapshot Snapshot { get; private set; }

            public static async Task<Cache> Resolve(ITextBuffer buffer, ITextSnapshot snapshot)
            {
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
                return new Cache
                {
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