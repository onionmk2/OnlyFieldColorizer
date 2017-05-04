using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SemanticColorizer
{
    internal static class ClassificationTypes
    {
#pragma warning disable CS0649
        [Export(typeof(ClassificationTypeDefinition))] [Name(Constants.FieldFormat)] internal static ClassificationTypeDefinition FieldType;

#pragma warning restore CS0649
    }
}