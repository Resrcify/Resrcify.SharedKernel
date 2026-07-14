# .claude — project-local Claude home

This folder is Resrcify.SharedKernel's own Claude Code home: put project-scoped agents (`agents/`), skills
(`skills/`), settings, and memory here so they travel with the repo instead of living in a shared
workspace bucket keyed to some other project.

Durable project knowledge belongs in this repo's `docs/` (incl. `docs/OPERATIONS.md`), not in the
central `~/.claude` workspace memory — that bucket is keyed to whichever folder is first in the
workspace and should hold only cross-cutting process/preferences.
