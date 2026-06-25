# Changelog

All notable changes to AutoValidate.Generator will be documented here.

## [1.0.0] — 2026-06-25

### Added
- Convention-based discovery of `AbstractValidator<T>` subclasses at compile time
- `AddValidators()` extension method on `IServiceCollection` — generated automatically
- `[SkipValidator]` attribute — opt a validator out of registration
- `[ValidatorLifetime]` attribute — override DI lifetime per validator (`Scoped`, `Singleton`, `Transient`)
- `[ValidateOnStartup]` attribute — register a hosted service that validates a model at app startup
- `WithValidation<T>()` Minimal API extension — attaches a `ValidationFilter<T>` endpoint filter
- AV001 diagnostic — warning when multiple validators target the same model type
- Abstract validator classes are automatically excluded from registration
- Full support for indirect inheritance chains
- Fully qualified type names used throughout — no namespace collision issues
