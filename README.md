# ZV Cluster Monitor App
This single-project self contained app includes:
- DX cluster telnet client (basic IAC handling)
- Multi-cluster management, primary auto-connect
- Per-cluster login and default commands
- Color-coded spot list with per-band colors
- CI-V / Yaesu / Kenwood CAT support (configurable)
- QRZ / DXNews lookup via browser
- Settings UI and cluster editor

Build:
Requires .NET 9.0 SDK or later.

Notes:
- Settings are saved to `ZVClusterApp.settings.json`.
- CAT commands and exact CI-V sequences are basic and may need tuning per radio model.

