using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace AvaloniaVS.Shared.IntelliSense
{
    internal class AddNamespaceSuggestedAction(ITextBuffer buffer, string prefix, string namespaceValue, int insertionPoint) : ISuggestedAction
    {
        public string DisplayText => $"Add xmlns:{prefix}=\"{namespaceValue}\"";
        public string IconAutomationText => null;
        public ImageMoniker IconMoniker => default;
        public string InputGestureText => null;
        public bool HasActionSets => false;
        public bool HasPreview => true;

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            var block = new System.Windows.Controls.TextBlock
            {
                Text = $"xmlns:{prefix}=\"{namespaceValue}\"",
                Padding = new System.Windows.Thickness(5)
            };
            return Task.FromResult<object>(block);
        }

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IEnumerable<SuggestedActionSet>>(null);

        public void Invoke(CancellationToken cancellationToken)
        {
            using var edit = buffer.CreateEdit();
            edit.Insert(insertionPoint, $" xmlns:{prefix}=\"{namespaceValue}\"");
            edit.Apply();
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose() { }
    }
}
