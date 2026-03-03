# 🦷 DentalID — Forensic Dental Identification System

[![CI/CD — DentalID Build & Test](https://github.com/wisam79/dental/actions/workflows/ci.yml/badge.svg)](https://github.com/wisam79/dental/actions/workflows/ci.yml)

A forensic dental identification desktop application built with **.NET 10** and **Avalonia UI**, using AI-powered ONNX models for automated tooth detection, pathology analysis, and biometric matching.

---

## 🏗️ Architecture

```
DentalID/
├── src/
│   ├── DentalID.Core/           # Domain entities, interfaces, enums
│   ├── DentalID.Application/    # Services, AI pipeline, business logic
│   ├── DentalID.Infrastructure/ # EF Core + SQLite, repositories
│   └── DentalID.Desktop/        # Avalonia UI, ViewModels, Views
├── tests/
│   └── DentalID.Tests/          # xUnit + Moq unit tests
└── models/                      # ONNX AI models (not included in repo)
```

**Pattern**: Clean Architecture + MVVM (CommunityToolkit.Mvvm)

---

## 🤖 AI Models Required

Place the following models in the `models/` directory:

| Model | Purpose |
|-------|---------|
| `teeth_detect.onnx` | YOLOv8 — Tooth detection & FDI numbering |
| `pathology_detect.onnx` | YOLOv8 — Pathology detection (caries, implants, etc.) |
| `encoder.onnx` | Feature vector extraction for biometric matching |
| `genderage.onnx` | Age & gender estimation from dental X-rays |

---

## 🚀 Features

- **Automated Tooth Detection** — YOLOv8 with FDI numbering (11–48)
- **Pathology Detection** — Caries, implants, crowns, root canals, fractures
- **Biometric Matching** — SIMD cosine similarity with HMAC-encrypted database
- **Forensic Intelligence** — Age estimation, gender analysis, missing teeth analysis
- **Odontogram** — Visual dental chart with overlay rendering
- **Report Generation** — PDF forensic reports
- **Encrypted Database** — SQLite with AES field-level encryption + deterministic lookup hashes for encrypted PII
- **No Login Runtime Mode** — no in-app login flow and no runtime admin bootstrap
- **Multi-language UI** — Avalonia MVVM with localization support

---

## 🛠️ Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 x64

### Build

```bash
git clone https://github.com/wisam79/dental.git
cd dental
dotnet restore DentalID.sln
dotnet build DentalID.sln --configuration Release
```

### Run Tests

```bash
dotnet tool restore
pwsh ./scripts/test.ps1 -Suite all -Configuration Release -CollectCoverage -AllowKnownFailures
```

For strict mode (fail on any test failure), remove `-AllowKnownFailures`.
More details: [`TESTING.md`](TESTING.md)

### Run Application

```bash
dotnet run --project src/DentalID.Desktop/DentalID.Desktop.csproj
```

> ⚠️ **Note:** You must place ONNX model files in `models/` before running.

---

## 🔐 Security

- Passwords hashed with **BCrypt** (work factor 12)
- Field-level encryption with **AES-256** + HMAC-SHA256 integrity
- Deterministic keyed lookup hashes for exact matching on encrypted subject name/ID fields
- Model integrity baseline/manifest via SHA-256 (`data/model_integrity.json`)
- Security keys auto-generated on first run (stored in `data/.sealing_key`)
- Runtime auth/admin bootstrap is intentionally disabled (no-login mode)
- **Never commit** `appsettings.Development.json` or real keys

---

## 🧪 CI/CD

GitHub Actions runs on every push to `main`:

| Job | Description |
|-----|-------------|
| 🔨 Build & Test | Restore → Build → Run unified test suites → Coverage + TRX artifacts |
| 🔍 Code Quality | Build each project layer separately |
| 🚀 Publish Release | Auto-publish Windows x64 on `v*` tag push |

---

## 📄 License

Private / Research Use. All rights reserved.
