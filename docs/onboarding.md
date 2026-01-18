---
title: Developer Onboarding
description: Complete guide for new developers joining the GeneralsHub project
---

# Welcome to GeneralsHub Development

<div style="display: flex; align-items: center; margin-bottom: 2rem;">
  <img src="/assets/icon.png" alt="GeneralsHub Icon" style="width: 64px; height: 61px; margin-right: 1rem;" />
  <div>
    <h2 style="margin: 0; color: #7c3aed;">GeneralsHub</h2>
    <p style="margin: 0; color: #6b7280;">Universal C&C Launcher Development Team</p>
  </div>
</div>

Welcome to the **GeneralsHub** development team! This guide will get you up to speed with our project, architecture, development workflow, and contribution standards.

---

## **1️⃣ Project Overview**

GeneralsHub is a **cross-platform desktop application** for managing, launching, and customizing *Command & Conquer: Generals / Zero Hour*.
It solves the problem of **ecosystem fragmentation** by detecting game installations, managing multiple versions, and integrating mods/maps/patches from multiple sources into isolated, conflict-free workspaces.

The architecture is **modular** and **service-driven**, with a **three-tier content pipeline**:

1. **Content Orchestrator** – Coordinates all content providers.
2. **Content Providers** – Handle specific sources (GitHub, ModDB, CNC Labs, local files).
3. **Pipeline Components** – Specialized discoverers, resolvers, and deliverers.

### Key Features

- **🎮 Game Profile Management**: Custom configurations combining base games with mods and patches
- **🔍 Content Discovery**: Automated discovery from GitHub, ModDB, CNC Labs, and local sources
- **📁 Isolated Workspaces**: Each profile runs in its own workspace to prevent conflicts
- **🛠️ Tool Support**: Specialized support for modding utilities and standalone game tools
- **🌐 Cross-Platform**: Native Windows and Linux support

---

## **2️⃣ Workflow & Contribution Process**

We follow a **GitHub-first workflow**:

### 1. Find or Create an Issue

- All work starts with a GitHub Issue.
- If you have an idea, create an issue and label it appropriately.

### 2. Branching Strategy

Create a branch from `main` using the format:

```bash
feature/<short-description>
fix/<short-description>
refactor/<short-description>
```

### 3. Code Standards

- **StyleCop** is enforced — your code must pass style checks before merging.
- Follow **C# naming conventions** and keep methods/classes small and focused.
- XML documentation is required for **all public classes, methods, and properties**.

### 4. Testing Requirements

- All new code must have **xUnit tests**.
- Tests live in the **GenHub.Tests** project, mirroring the folder structure of the main code.
- Run tests locally before pushing.

### 5. Pull Request Process

- Open a PR linked to the issue.
- GitHub Actions will run:
  - Build on Windows & Linux
  - Run all tests
  - StyleCop analysis
- PRs failing checks will be rejected automatically.

### 6. Code Review

- At least **one approval** from a reviewer is required before merging.
- Be open to feedback and iterate quickly.

---

## **3️⃣ Repository Structure**

Here's the **high-level directory layout**:

```
GenHub/           → Main Avalonia UI application
GenHub.Core/      → Core business logic, models, interfaces (platform-agnostic)
GenHub.Windows/   → Windows-specific implementations
GenHub.Linux/     → Linux-specific implementations
GenHub.Tests/     → Unit & integration tests (xUnit)
```

### Inside GenHub.Core

- **Interfaces/** – Contracts for services (e.g., IGameInstallationDetector, IContentProvider)
- **Models/** – Data models, enums, DTOs
- **Features/** – Grouped by domain (Content, GameProfiles, Manifest, Workspace, etc.)
- **Services/** – Implementations of interfaces
- **Validation/** – Validators for installations, versions, and files

### Inside GenHub.Tests

- Mirrors the structure of `GenHub.Core` and `GenHub`
- Each service/class has a corresponding test file
- Uses **xUnit** + **Moq** for mocking dependencies

---

## **4️⃣ Infrastructure & Services**

GeneralsHub is built around **Dependency Injection** and **Service Modules**:

- **AppServices.cs** – Registers all core services
- **ContentDeliveryModule.cs** – Registers content pipeline components
- **GameDetectionModule.cs** – Registers installation/version detection
- **WorkspaceModule.cs** – Registers workspace strategies
- **ValidationModule.cs** – Registers validators

### Key Service Categories

- **Detection Services** – Find game installations & versions
- **Manifest Services** – Create & manage content manifests
- **Content Pipeline** – Orchestrator, providers, discoverers, resolvers, deliverers
- **Workspace Services** – Prepare isolated game directories
- **Launching Services** – Start games with correct configs
- **Storage Services** – Manage content storage (CAS system)

---

## **5️⃣ Coding Standards**

We enforce **StyleCop** rules:

- **PascalCase** for public members
- **camelCase** for private fields (with `_` prefix)
- XML documentation for all public APIs
- No unused usings
- Consistent spacing & brace style

### Example Code Style

```csharp
/// <summary>
/// Detects all game installations on the system.
/// </summary>
public interface IGameInstallationDetectionOrchestrator
{
    /// <summary>
    /// Asynchronously detects all available game installations.
    /// </summary>
    /// <returns>A read-only list of detected game installations.</returns>
    Task<IReadOnlyList<GameInstallation>> DetectAllInstallationsAsync();
}
```

---

## **6️⃣ Testing Guidelines**

All code changes must have **unit tests** in `GenHub.Tests`:

### Basic Test Structure

```csharp
[Fact]
public void ShouldDetectSteamInstallation()
{
    // Arrange
    var detector = new SteamInstallationDetector();

    // Act
    var result = detector.Detect();

    // Assert
    Assert.NotNull(result);
}
```

### Testing with Dependencies

For services with dependencies, use **Moq** to mock interfaces:

```csharp
[Fact]
public async Task ShouldDownloadContent()
{
    // Arrange
    var mockFileService = new Mock<IFileService>();
    var downloadService = new DownloadService(mockFileService.Object);

    // Act
    var result = await downloadService.DownloadAsync("http://test.com/file.zip");

    // Assert
    Assert.True(result.Success);
    mockFileService.Verify(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Once);
}
```

---

## **7️⃣ Getting Started**

### Prerequisites

- **.NET 8 SDK** or later
- **Visual Studio 2022** / **JetBrains Rider** / **VS Code**
- **Git** for version control

### Setup Instructions

1. **Clone the repository**

   ```bash
   git clone https://github.com/community-outpost/GenHub.git
   cd GenHub
   ```

2. **Restore dependencies**

   ```bash
   dotnet restore
   ```

3. **Build the solution**

   ```bash
   dotnet build
   ```

4. **Run tests**

   ```bash
   dotnet test
   ```

5. **Run the application**
   - Set `GenHub` as the startup project
   - Press F5 or run: `dotnet run --project GenHub`

### Development Environment

- **Windows**: Full development and testing capabilities
- **Linux**: Full development and testing capabilities
- **macOS**: Limited support (builds but not officially tested)

---

## **8️⃣ Communication & Collaboration**

We use multiple channels for different types of communication:

### Discord Server

Our primary communication hub with dedicated channels:

- **#general** – General discussion and introductions
- **#development** – Technical discussions and questions
- **#feedback** – User feedback and feature requests
- **#issues** – Issue tracking and bug reports
- **#pull-requests** – PR discussions and reviews
- **#releases** – Release announcements and updates

### GitHub

- **Issues** – Task tracking, bug reports, feature requests
- **Pull Requests** – Code review and discussion
- **Discussions** – Architecture decisions and long-form topics
- **Wiki** – Extended documentation and guides

### Best Practices

- **Ask questions early** – Don't struggle alone, we're here to help
- **Use the right channel** – Keep discussions organized
- **Search before asking** – Check existing issues and discussions
- **Be respectful and constructive** – We're all here to build something great

---

## **9️⃣ Architecture Deep Dive**

For a comprehensive understanding of the system architecture, see our [Architecture Documentation](./architecture.md).

### Key Architectural Concepts

1. **Three-Tier Content Pipeline**
   - **Tier 1**: Content Orchestrator (system-wide coordination)
   - **Tier 2**: Content Providers (source-specific orchestration)
   - **Tier 3**: Pipeline Components (specialized operations)

2. **Six Architectural Pillars**

1. **GameInstallation**: Physical game detection
2. **GameClient**: Executable identification
3. **GameManifest**: Declarative content packaging
4. **GameProfile**: User configuration (including **Tool Profiles**)
5. **Workspace**: Isolated execution environment
6. **GameLaunching**: Runtime orchestration & monitoring

3. **Service-Oriented Design**
   - Dependency injection throughout
   - Interface-based contracts
   - Platform abstraction layers
   - Modular service registration

---

## **🔟 Quick Reference**

### Common Commands

```bash
# Build and test
dotnet build
dotnet test

# Run specific tests
dotnet test --filter "TestName"

# Generate test coverage
dotnet test --collect:"XPlat Code Coverage"

# Format code
dotnet format
```

### File Locations

- **Main App**: `GenHub/`
- **Core Logic**: `GenHub.Core/`
- **Tests**: `GenHub.Tests/`
- **Documentation**: `docs/`
- **Build Scripts**: `.github/workflows/`

### Important Links

- [Full Architecture Guide](./architecture.md)
- [System Flowcharts](./FlowCharts/)
- [GitHub Repository](https://github.com/community-outpost/GenHub)
- [Project Issues](https://github.com/community-outpost/GenHub/issues)
