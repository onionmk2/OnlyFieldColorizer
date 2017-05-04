using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace SemanticColorizer
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = Constants.FieldFormat)]
    [Name(Constants.FieldFormat)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class SemanticFieldFormat : ClassificationFormatDefinition
    {
        public SemanticFieldFormat()
        {
            DisplayName = "Semantic Field";
            ForegroundColor = Color.FromRgb(202, 60, 110);
        }
    }
}