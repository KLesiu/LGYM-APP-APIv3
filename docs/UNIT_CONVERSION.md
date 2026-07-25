# Unit Conversion System

## Overview

The application uses a generic, strategy-based unit conversion design for linear unit systems (for example weight or length).

The goal is to keep feature code free from hardcoded conversion formulas and to allow easy extension for new unit families.

## Core Concepts

### 1. Linear Strategy

`ILinearUnitStrategy<TUnit>` defines conversion rules for one unit family.

- `ConvertTo(value, unit)` converts a unit value to the strategy base unit.
- `ConvertFrom(value, unit)` converts from the strategy base unit to the requested unit.

For a linear family, every unit is represented by a factor relative to the base unit.

### 2. Generic Converter

`IUnitConverter<TUnit>` / `LinearUnitConverter<TUnit>` provides:

- `Convert(value, fromUnit, toUnit)`

The converter performs:

1. `fromUnit -> base`
2. `base -> toUnit`

This enables dynamic conversion between any two units in the same family.

### 3. Weight Implementation

Current implementation:

- Strategy: `WeightLinearUnitStrategy`
- Base unit: `WeightUnits.Kilograms`
- Supported units: `Kilograms`, `Pounds`

## Dependency Injection

Registered by the internal `LgymApi.Application/Platform/ReferenceData/ServiceCollectionExtensions.cs` helper, composed only through public `AddPlatformModule`:

- `ILinearUnitStrategy<WeightUnits> -> WeightLinearUnitStrategy`
- `IUnitConverter<WeightUnits> -> LinearUnitConverter<WeightUnits>`

## Usage in Training Max Logic

Training max synchronization compares values in normalized units (kilograms), while preserving source unit for persisted max records.

This ensures:

- correct comparison for mixed units (`kg` vs `lbs`),
- no data loss of original user-entered unit.

## How to Add a New Unit Family

1. Create an enum for the family (for example `LengthUnits`).
2. Implement `ILinearUnitStrategy<LengthUnits>`.
3. Register strategy and converter in DI:
   - `ILinearUnitStrategy<LengthUnits>`
   - `IUnitConverter<LengthUnits>`
4. Inject `IUnitConverter<LengthUnits>` where conversion is required.

## Notes

- This system is designed for linear conversions.
- Non-linear conversions (for example temperature scales with offsets) should use a separate strategy abstraction.
