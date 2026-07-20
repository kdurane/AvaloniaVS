using System.ComponentModel.Composition;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using IAsyncServiceProvider = Microsoft.VisualStudio.Shell.IAsyncServiceProvider;
using Task = System.Threading.Tasks.Task;

namespace AvaloniaVS.ProjectSystem;

[Export(ExportContractNames.Scopes.UnconfiguredProject, typeof(IProjectDynamicLoadComponent))]
[AppliesTo(Constants.AvaloniaCapability)]
internal class AvaloniaProject : IProjectDynamicLoadComponent
{
    private IAsyncServiceProvider _asyncServiceProvider;

    public async Task LoadAsync()
    {
        if (_asyncServiceProvider is null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (ServiceProvider.GlobalProvider.GetService(typeof(IVsShell)) is IVsShell shell)
            {
                if (shell.IsPackageLoaded(Constants.PackageGuid, out var vsPackage)
                   != Microsoft.VisualStudio.VSConstants.S_OK)
                {
                    shell.LoadPackage(Constants.PackageGuid, out vsPackage);
                }
                _asyncServiceProvider = (IAsyncServiceProvider)vsPackage;
            }
        }
    }

    public async Task UnloadAsync()
    {
        // Unload the feature
        await Task.CompletedTask;
    }
}
