# LgymApi.Resources.csproj

- Purpose: localized resources for user-facing text.
- Contains: `.resx` messages, enums, and emails with generated strongly typed access.
- Rules: add/update English and Polish resources for user-facing text.
- Boundary: keep user-facing strings out of hardcoded application logic where possible.
- Enum resources use the `EnumType_EnumMember` key convention in both `Enums.resx` and `Enums.pl.resx`, including hidden members. Application maps those labels to lookup `name` and `displayName`; Domain stays localization-neutral and has no Resources dependency.
- Resources provides localized messages, enum labels, and email text. It does not own request-culture selection, which remains an API host concern using English by default and `Accept-Language` for `en` and `pl`.
