# LgymApi.DataSeeder.Tests.csproj

- Purpose: tests for DataSeeder behavior and assumptions.
- Contains: coverage for seeding inputs, defaults, composed Identity password hashing, deterministic ordering, and seeded entities.
- Stable-ID assertions consume public `IdentitySeedIds`, not an Identity friend-only seed configuration.
- Rules: update when seeder behavior changes.
- Boundary: keep these tests focused on seeder-specific behavior.
