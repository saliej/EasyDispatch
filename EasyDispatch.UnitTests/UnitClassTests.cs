using AwesomeAssertions;
using EasyDispatch;

/// <summary>
/// Tests for the Unit type equality and comparison operations.
/// </summary>
public class UnitClassTests
{
	[Fact]
	public void Equals_WithUnit_ReturnsTrue()
	{
		// Arrange
		var unit1 = Unit.Value;
		var unit2 = Unit.Value;

		// Act
		var result = unit1.Equals(unit2);

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void Equals_WithObject_WhenUnit_ReturnsTrue()
	{
		// Arrange
		var unit = Unit.Value;
		object obj = Unit.Value;

		// Act
		var result = unit.Equals(obj);

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void Equals_WithObject_WhenNotUnit_ReturnsFalse()
	{
		// Arrange
		var unit = Unit.Value;
		object obj = "not a unit";

		// Act
		var result = unit.Equals(obj);

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void Equals_WithObject_WhenNull_ReturnsFalse()
	{
		// Arrange
		var unit = Unit.Value;

		// Act
		var result = unit.Equals(null);

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void GetHashCode_ReturnsZero()
	{
		// Arrange
		var unit = Unit.Value;

		// Act
		var hashCode = unit.GetHashCode();

		// Assert
		hashCode.Should().Be(0);
	}

	[Fact]
	public void ToString_ReturnsExpectedFormat()
	{
		// Arrange
		var unit = Unit.Value;

		// Act
		var result = unit.ToString();

		// Assert
		result.Should().Be("()");
	}

	[Fact]
	public void EqualityOperator_ReturnsTrue()
	{
		// Arrange
		var unit1 = Unit.Value;
		var unit2 = Unit.Value;

		// Act
		var result = unit1 == unit2;

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public void InequalityOperator_ReturnsFalse()
	{
		// Arrange
		var unit1 = Unit.Value;
		var unit2 = Unit.Value;

		// Act
		var result = unit1 != unit2;

		// Assert
		result.Should().BeFalse();
	}

	[Fact]
	public void DefaultValue_IsEqualToValue()
	{
		// Arrange & Act
		var defaultUnit = default(Unit);
		var valueUnit = Unit.Value;

		// Assert
		defaultUnit.Should().Be(valueUnit);
		(defaultUnit == valueUnit).Should().BeTrue();
	}
}