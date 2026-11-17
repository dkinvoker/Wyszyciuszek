using Microsoft.UI.Xaml;

namespace Wyszyciuszek;

/// <summary>Tryb projektora — osobne okno na pełnym ekranie, odświeżane po każdej zmianie wzoru.</summary>
public sealed partial class MainWindow
{
    private ProjectorWindow? _projector;

    private void Projector_Click(object sender, RoutedEventArgs e)
    {
        if (Doc is not { HasPattern: true }) return;
        if (_projector is not null)
        {
            _projector.Activate();
            return;
        }
        var projector = new ProjectorWindow(_hwnd);
        projector.Closed += (_, _) =>
        {
            if (ReferenceEquals(_projector, projector)) _projector = null;
        };
        _projector = projector;
        RefreshProjector();
        projector.Activate();
    }

    /// <summary>Przekazuje projektorowi aktualny wzór (kontury tylko, gdy pasują do mapy kolorów).</summary>
    private void RefreshProjector() => _projector?.SetPattern(Doc, AnalysisIsCurrent ? _analysis : null);
}
