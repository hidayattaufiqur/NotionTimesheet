using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace NotionTimesheet;

[Guid("7C1F5D2A-8E4B-4F1A-9C3D-2B6E8A1D4F07")]
public sealed partial class NotionTimesheetExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent extensionDisposedEvent;
    private readonly NotionTimesheetCommandsProvider provider = new();

    public NotionTimesheetExtension(ManualResetEvent extensionDisposedEvent)
    {
        this.extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => provider,
            _ => null,
        };
    }

    public void Dispose() => extensionDisposedEvent.Set();
}

