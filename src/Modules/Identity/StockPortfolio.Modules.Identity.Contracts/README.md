# `StockPortfolio.Modules.Identity.Contracts` — intentionally empty

This project contains no types, and that is the finding, not an oversight.

A module's `.Contracts` assembly exists so **other** modules can talk to it. Nothing talks to
Identity. Authentication happens once, at the edge, and produces a self-contained JWT; every
downstream module reads the user id out of the `sub` claim and never asks Identity anything. There
is no `IUserLookup`, no `UserDto`, no event Portfolio or Alerts subscribes to.

So the project ships empty on purpose — as evidence rather than as a claim. The
[module-interactions diagram](../../../../docs/reference/module-interactions.md) argues that Identity is
the cheapest module to extract into its own service, because nothing points at it. An empty
`.Contracts` is what that argument looks like when the compiler is the one making it: if a runtime
dependency on Identity ever appears, it must appear here first, and the diff will say so.

Keep it empty. If something genuinely needs to reach Identity across a module boundary, that is a
design conversation, not a file.

Deleting the project instead would be the wrong shortcut. Every other module has a `.Contracts`
project, `Architecture.Tests` walks the five-project shape per module, and the absence would read as
"we forgot" rather than "we checked".
