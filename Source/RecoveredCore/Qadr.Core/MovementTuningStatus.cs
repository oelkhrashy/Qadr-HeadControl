namespace Qadr.Core;

public sealed record MovementTuningStatus(string Phase, string Message, MovementParameters Before, MovementParameters? Proposed = null, MovementParameters? Applied = null, string? Adjustment = null, bool UndoAvailable = false);
