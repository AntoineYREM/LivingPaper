# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Solution skeleton: .NET 10 solution with the hexagonal layers (`Domain`, `Application`, `Infrastructure`,
  `Persistence`, `Web`), one test project per layer, architecture tests that guard the allowed dependencies,
  a smoke test of the web application, and a GitHub Actions workflow that builds and tests every push.
  Tests use xUnit v3 on Microsoft.Testing.Platform.
