# Epic 123 – Stakeholder Summary

## Admins / Operators

- **Value**
  - Can update Quick Memory Server from a browser, without having to manually run a series of git, dotnet, and PowerShell commands.
  - Confidence that:
    - Data under `MemoryStores` will not be altered.
    - `QuickMemoryServer.toml` is preserved.
    - Updates only apply if the new build/test pipeline succeeds.
- **Usage**
  - Use the Admin Web UI to:
    - Check whether there is a newer release on the configured `release-x.y.z` branch.
    - Apply an update off-hours with a single click, watching status in the UI.

## Developers

- **Value**
  - Controlled update path that encourages clear release practices (`release-x.y.z` branches).
  - Fewer environment-specific “it works on my machine” install issues; everyone uses the same script.
- **Usage**
  - Tag and manage release branches that auto-update can consume.
  - Add regression tests that run as part of the update validation step.

## Security / Compliance

- **Value**
  - Clear rules: config and data are not touched by auto-update.
  - Audit trail from logs (and optionally audit log) showing:
    - Who initiated the update (API key / Admin user).
    - Which branch/commit was deployed.
    - Whether the update succeeded or failed.
- **Usage**
  - Review logs periodically for update activity.
  - Use the same mechanism to enforce only approved branches are deployed.

## Future Agent Users

- **Value**
  - Lower risk of breaking changes or partially applied updates disrupting MCP usage mid-session.
  - Confidence that updates are deployed in a consistent, tested way.
