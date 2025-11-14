# VE7CCUserApp.WinForms.Full v2

This single-project WinForms app includes:
- DX cluster telnet client (basic IAC handling)
- Multi-cluster management, primary auto-connect
- Per-cluster login and default commands
- Color-coded spot list with per-band colors
- CI-V / Yaesu / Kenwood CAT support (configurable)
- QRZ / DXNews lookup via browser
- Settings UI and cluster editor

Build:
Requires .NET 7 and Windows.

    dotnet build

Open the produced EXE in bin\Debug\net7.0-windows and run.

Notes:
- Settings are saved to `ve7cc_winforms_full_settings.json`.
- CAT commands and exact CI-V sequences are basic and may need tuning per radio model.
- If you want DPAPI-encrypted credentials or richer telnet option negotiation I can add that.
