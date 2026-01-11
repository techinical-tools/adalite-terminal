# Adalite Terminal

> *A kernel-style terminal with an integrated package manager* and a **verification-first** design.

## Why?
- Commands are internal **services**, not shell scripts
- One authority runtime (no shell fragmentation)
- Built-in package **verification and metadata rules**
- Cross-platform — no path hacks, no separator pain

  ## What makes it different?
- No external shell execution model
- Commands are internal services
- Package verification is mandatory, not optional
- Single runtime controls execution, IO, and metadata


## Requirements
- .NET **8.0+**
- Visual Studio *(optional)*

## Quick start
```bash
git clone https://github.com/techinical-tools/adalite-terminal.git
dotnet build
```

Then run the application, and inside the terminal execute:

```bash
bootstrap
reload --config
```

## Notes
- This codebase contains AI-assisted code — do not expect perfect quality everywhere
- The project is early-stage but usable; you can optionally try it as your daily terminal to get familiar with it
