namespace InventoryWalker.Tests;

/// <summary>Which movement keys are held down this frame.</summary>
public readonly record struct Held(bool W = false, bool A = false, bool S = false, bool D = false)
{
    public static readonly Held None = new();
    public static readonly Held Forward = new(W: true);
    public static readonly Held Back = new(S: true);
    public static readonly Held Left = new(A: true);
    public static readonly Held Right = new(D: true);
    public static readonly Held ForwardLeft = new(W: true, A: true);
    public static readonly Held ForwardRight = new(W: true, D: true);
    public static readonly Held BackLeft = new(S: true, A: true);
    public static readonly Held BackRight = new(S: true, D: true);
}

/// <summary>What the player was actually told to do on a given frame.</summary>
public readonly record struct PlayerInput(bool Delivered, float MoveX, float MoveY, float TurnX, float TurnY, float Lean)
{
    public static readonly PlayerInput Blocked = new(false, 0, 0, 0, 0, 0);

    /// <summary>The player is standing still, whether because it was told to or told nothing.</summary>
    public bool IsStationary => !Delivered || (MoveX == 0f && MoveY == 0f);
}

/// <summary>
/// A model of EFT's per frame input delivery, written against the client's IL rather than
/// against the mod, so that the six cases in the brief can be asserted rather than argued.
///
/// What it mirrors, and where each step came from:
///
///   1. The input source zeroes the entire axes array and refills it from the current state of
///      each bound axis, every frame. Read out of the source's UpdateInput: a loop writing 0f
///      into every slot, then a loop writing each axis combination's GetValue() back in. This is
///      the step the whole design rests on, because it means held keys are sampled fresh each
///      frame and nothing anywhere is latched.
///   2. The array is threaded by reference through the input tree, whose nodes are visited
///      newest first, so a screen opened in raid is reached before the player's own node.
///   3. UIScreen.TranslateAxes assigns null to it, and InputNode.TranslateInput only calls a
///      node's TranslateAxes if the array is not null. That is the freeze.
///   4. Whatever survives reaches PlayerInputTranslator.TranslateAxes, which calls
///      Player.Move(axes[MoveX], axes[MoveY]) and Player.Rotate(axes[TurnX], axes[TurnY]).
///
/// This is a model, not the game. It proves the mod's logic behaves correctly against the
/// pipeline as read; it cannot prove the pipeline was read correctly.
/// </summary>
public sealed class InputPipeline
{
    private readonly float[] _axes = new float[GameAxis.Count];

    /// <summary>Whether the in raid inventory screen is on top of the input tree.</summary>
    public bool InventoryOpen { get; set; }

    /// <summary>The config toggle.</summary>
    public bool ModEnabled { get; set; } = true;

    /// <summary>
    /// Whether GamePlayerOwner.MyPlayer resolves to a live player. False out of raid, which is
    /// what keeps the mod off the character screen, that being the same InventoryScreen type.
    /// </summary>
    public bool RaidPlayerPresent { get; set; } = true;

    /// <summary>
    /// The tab currently selected inside the inventory screen. The pipeline ignores it, and that
    /// is the point: every top tab lives inside the one InventoryScreen, and the panel hosting
    /// them is a UIElement rather than an input node, so changing tabs cannot add, remove or
    /// reorder anything in the input tree.
    /// </summary>
    public string Tab { get; set; } = "Gear";

    /// <summary>Runs one frame and returns what the player was told.</summary>
    public PlayerInput Frame(Held held, float mouseX = 0f, float mouseY = 0f, float lean = 0f)
    {
        // 1. zeroed and refilled from current state, every frame
        Array.Clear(_axes, 0, _axes.Length);
        _axes[GameAxis.MoveX] = (held.D ? 1f : 0f) - (held.A ? 1f : 0f);
        _axes[GameAxis.MoveY] = (held.W ? 1f : 0f) - (held.S ? 1f : 0f);
        _axes[GameAxis.TurnX] = mouseX;
        _axes[GameAxis.TurnY] = mouseY;
        _axes[GameAxis.LeanX] = lean;

        // 2 and 3. the screen node, when the inventory is open
        float[]? threaded = _axes;
        if (InventoryOpen)
        {
            if (AxisGate.ShouldPassThrough(ModEnabled, isInventoryScreen: true, RaidPlayerPresent))
            {
                AxisGate.KeepMovementOnly(threaded);
            }
            else
            {
                threaded = null; // vanilla UIScreen.TranslateAxes
            }
        }

        // 4. the player's node, reached only if the array survived
        if (threaded is null)
        {
            return PlayerInput.Blocked;
        }

        return new PlayerInput(
            true,
            threaded[GameAxis.MoveX],
            threaded[GameAxis.MoveY],
            threaded[GameAxis.TurnX],
            threaded[GameAxis.TurnY],
            threaded[GameAxis.LeanX]);
    }
}
