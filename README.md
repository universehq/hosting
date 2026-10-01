# Hosting

## Introduction

Hosting provides C# wrappers around the .NET native hosting APIs for discovering the .NET host, initializing a runtime, running managed applications, and calling methods in managed components. The libraries target .NET 11 or later and provide typed results, managed delegates, and disposable runtime contexts.

- [Universe.Hosting.NetHost](./src/Hosting.NetHost/README.md) locates the hostfxr library and supports both JIT and Native AOT applications.
- [Universe.Hosting.HostFxr](./src/Hosting.HostFxr/README.md) loads hostfxr and manages runtime initialization and execution. The consuming host must be published with Native AOT; hosted applications and components run on CoreCLR.

See the library READMEs for usage examples, deployment requirements, and validation commands.

## Contributing

We welcome contributions to enhance the functionality and usability of this utility. Feel free to submit issues for bug reports or feature requests, and create pull requests to suggest improvements or fixes.

## License

This project is licensed under the MIT License - see the [LICENSE](./LICENSE) file for details.
