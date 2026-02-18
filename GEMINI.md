# DentalID

## Project Overview
**DentalID** is a sophisticated desktop application for dental forensic analysis. It utilizes Artificial Intelligence (AI) and ONNX models to perform tasks such as teeth detection, pathology identification, age/gender estimation, and identity matching from dental X-ray images.

The application is built using **.NET 8** and **Avalonia UI**, making it cross-platform (though currently targeted at Windows). It follows **Clean Architecture** principles to separate domain logic, application rules, infrastructure, and the user interface.

## Architecture & Structure
The solution is organized into the following layers:

*   **`src/DentalID.Core`**: The domain layer. Contains enterprise logic, entities (e.g., `Patient`, `DentalRecord`), and interface definitions. It has no external dependencies.
*   **`src/DentalID.Application`**: The application layer. Contains business logic, AI orchestration services (like `OnnxInferenceService`), and DTOs. It depends on `Core`.
*   **`src/DentalID.Infrastructure`**: The infrastructure layer. Implements interfaces from `Core` for data access (EF Core/SQLite) and external services. It depends on `Application` and `Core`.
*   **`src/DentalID.Desktop`**: The presentation layer. A **MVVM** (Model-View-ViewModel) application built with Avalonia UI. It serves as the composition root.
*   **`tests/DentalID.Tests`**: Unit and integration tests using xUnit.
*   **`models/`**: Directory containing the ONNX model files (`teeth_detect.onnx`, `pathology_detect.onnx`, etc.) required for the AI engine.

## Key Files & Entry Points
*   **`src/DentalID.Desktop/Program.cs`**: Standard Avalonia entry point.
*   **`src/DentalID.Desktop/App.axaml.cs`**: Handles application startup, configuration loading (`appsettings.json`), and dependency injection (via `Bootstrapper`). Implements a "Secure Boot" pattern that initializes services in the background while showing a startup screen.
*   **`src/DentalID.Application/Services/OnnxInferenceService.cs`**: The core service responsible for loading ONNX models and running inference. It manages thread safety for non-thread-safe ONNX sessions.
*   **`BUGS_AND_ISSUES.md`**: A critical file tracking known bugs, architectural issues, and missing implementations. **Review this file before starting any task.**

## Building and Running

### Prerequisites
*   .NET 8 SDK
*   ONNX models present in the `models/` directory.

### Commands
*   **Build Solution:**
    ```bash
    dotnet build
    ```
*   **Run Desktop Application:**
    ```bash
    dotnet run --project src/DentalID.Desktop
    ```
*   **Run Tests:**
    ```bash
    dotnet test
    ```
*   **Run Benchmarks:**
    ```bash
    dotnet run --project src/DentalID.Benchmark -- <args>
    ```

## Development Conventions

*   **Architecture:** Strictly adhere to Clean Architecture. Do not add infrastructure dependencies (like File I/O or DB access) directly to `Core` or `Application` (use interfaces).
*   **Dependency Injection:** Services are registered in `DentalID.Infrastructure/DependencyInjection.cs` and `DentalID.Desktop/Services/Bootstrapper.cs`. Note that `DbContext` and Repositories are registered as **Transient** to handle potential concurrency in the desktop environment.
*   **UI Pattern:** Use MVVM. Logic should reside in ViewModels (`src/DentalID.Desktop/ViewModels`), not in Code-Behind (`.axaml.cs`).
*   **AI/ONNX:** Interaction with ONNX models is centralized in `OnnxInferenceService`. This service handles input tensor preparation (using SkiaSharp) and output parsing.
*   **Configuration:** Application settings are stored in `src/DentalID.Desktop/appsettings.json`.

## Critical Context & Known Issues
*   **`BUGS_AND_ISSUES.md`**: This file lists over 50 known issues, ranging from critical bugs (stub methods, wrong tensor dimensions) to code quality improvements.
    *   *Note:* The "stub methods" issue for `DetectTeethAsync` in `OnnxInferenceService` appears to be addressed in the current code, but verification is recommended.
    *   *Critical:* There are reports of incorrect tensor dimension handling (HWC vs NCHW) in feature extraction that need validation.
*   **Model Dependencies:** The application heavily relies on specific ONNX models. Ensure these are correctly placed and versioned.
