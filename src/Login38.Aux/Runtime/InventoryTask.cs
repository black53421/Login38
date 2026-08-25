using Login38.Aux.Game;

namespace Login38.Aux.Runtime;

/// <summary>
/// Keeps <see cref="InventoryWatch"/> up to date while the helper window is open.
/// </summary>
/// <remarks>
/// <para>
/// Once a second, which is how often a bag changes in a way anybody picking from a
/// dropdown would notice, and only while something is looking. The reference read the
/// bag on a timer that ran whether the window was open or not, and read it again from
/// each tab that had a dropdown to fill.
/// </para>
/// <para>
/// The bag itself comes from the pass rather than from a read of its own, so a pass where
/// another task already wanted it costs nothing extra.
/// </para>
/// </remarks>
public sealed class InventoryTask : IAuxTask
{
    private readonly InventoryWatch _watch;

    public InventoryTask(InventoryWatch watch) => _watch = watch;

    /// <inheritdoc/>
    public string Name => "inventory";

    /// <inheritdoc/>
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    /// <inheritdoc/>
    public void Tick(AuxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_watch.Wanted)
        {
            _watch.Clear();

            return;
        }

        // Out of the world the bag reads as the last character's, or as nothing at all.
        // Offering either would be offering items this character cannot use.
        _watch.Fill(context.IsInWorld ? context.Bag : []);
    }
}
