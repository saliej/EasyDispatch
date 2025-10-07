namespace EasyDispatch;

/// <summary>
/// Represents a void type for pipeline behaviors wrapping void commands and notifications.
/// This is used internally to provide a consistent response type for behaviors.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>
    /// Default value of Unit.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    /// Compares two Unit values for equality.
    /// </summary>
    public bool Equals(Unit other) => true;

    /// <summary>
    /// Compares this Unit to another object.
    /// </summary>
    public override bool Equals(object? obj) => obj is Unit;

    /// <summary>
    /// Returns the hash code for this Unit.
    /// </summary>
    public override int GetHashCode() => 0;

    /// <summary>
    /// Returns a string representation of Unit.
    /// </summary>
    public override string ToString() => "()";

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(Unit left, Unit right) => false;
}