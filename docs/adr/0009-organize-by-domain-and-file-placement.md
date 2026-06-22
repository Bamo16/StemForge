# Where each new file goes: by-domain Core, MVVM GUI, verb-based CLI

The solution is three projects (ADR 0006), and each is organized by the structure idiomatic to its role rather than one uniform scheme: `StemForge.Core` is organized **by domain** (#67), the GUI follows the **MVVM** convention, and the CLI is organized **by verb** plus shared rendering. This ADR is the router a future change follows when adding a file, so placement is decided once here instead of re-litigated per file. The project-level question (which of the three) is owned by ADR 0006; the folder-level question inside each project is owned here.

## The decision

```
Where does a new file go?

1. Which project?  ->  ADR 0006 owns this.
   - UI-agnostic logic (process running, paths, settings, the separator
     driver, the separation pipeline, catalogs, downloading)  -> StemForge.Core  (step 2)
   - Avalonia views / view-models                             -> StemForge (GUI) (step 3)
   - terminal commands / progress rendering                   -> StemForge.Cli   (step 4)
   - a test                                                   -> StemForge.Tests (step 5)

2. Inside StemForge.Core -> by domain.
   - Serves exactly one domain
     (Separation / Catalog / Tooling / Downloading / Platform)?
         -> that domain's folder
   - Used by two or more domains, OR is app-wide bootstrap/composition
     (logging, app info, path resolution, settings, update check,
     DI registration, HttpClient conventions)?
         -> Core root, as a flat file (NO Extensions/ or Helpers/ bucket)
   - A model/DTO? It lives with its domain. A Models/ subfolder is used
     ONLY in a domain large enough to earn it (today: Separation/Models/).
   - A genuinely new domain? add a new domain folder ("Adding a domain").
   Namespaces follow folders (StemForge.Core.Separation, .Catalog, ...).

3. Inside StemForge (GUI) -> by MVVM layer (the convention, not by domain).
   - A new page/feature = a View (Views/, namespace StemForge.Views) paired
     with a ViewModel (ViewModels/, namespace StemForge.ViewModels). The
     ViewLocator maps VM -> View by replacing "ViewModel" with "View" in the
     full type name, so the paired names and namespaces must line up.
   - GUI-specific service / adapter -> Services/.  Value converter -> Converters/.
   - GUI extension helper -> Extensions/.  (These layer folders ARE the GUI's
     organizing principle, so unlike Core they are kept.)

4. Inside StemForge.Cli -> by verb.
   - A new command = a Spectre verb in Commands/.
   - Shared terminal rendering / progress / input handling -> Progress/.

5. Inside StemForge.Tests.
   - A unit test for Core mirrors the domain of the code under test
     (Separation/, Catalog/, ...). GUI tests keep ViewModels/; CLI tests keep
     Commands/ and Cli/; Integration/ and Fakes/ are cross-cutting. Test
     namespaces are organizational only (xUnit discovers by class).
```

## The rules behind the tree

- **Each project is organized by its own idiom on purpose.** Core is a class library with no framework convention tying folders to behavior, so it is free to group by domain, which is what makes a feature's code sit together. The GUI's `Views/`+`ViewModels/` split is not arbitrary layering: the ViewLocator resolves a view-model to its view by namespace, and the GUI's natural unit is the *page* (Separate, Models, Queue, Settings, Setup, Logs), which only loosely maps to Core's domains. The CLI's unit is the *verb*. Imposing Core's domain scheme on the GUI or CLI would fight their conventions for no gain.
- **In Core, "lives with the domain it serves" is the test, not the file's *kind*.** An extension method, a helper, a small record, and a service all follow the same rule: serve one domain, live in that domain's folder. The pre-#67 instinct of "extensions go in `Extensions/`, helpers in `Helpers/`" is the by-layer grouping #67 removed; re-creating an `Extensions/` or `Helpers/` bucket *inside Core* reintroduces it. (The GUI keeps `Extensions/`/`Converters/`/`Services/` because there those layers *are* the organizing principle.)
- **The Core root is for cross-cutting and composition only.** A file earns the root by being used across two or more domains (`SpecialFolderExtensions`, `AppPaths`), being app-wide bootstrap (`AppInfo`, `AppLogger`, `AppSettings`, `UpdateCheck`), or being the composition root (`CoreServiceExtensions` and the `HttpClientBuilder` conventions it applies). Root files stay flat; if one later becomes single-domain, it moves down.
- **A Core `Models/` subfolder only where a domain earns it.** [[Separation]]'s job, audio-format, and source-tag DTOs are numerous enough to warrant `Separation/Models/`; smaller domains keep their one or two models flat. Uniformity is rejected as empty ceremony for small domains.
- **Adding a Core domain is deliberate.** Prefer extending an existing domain; add a new top-level folder (and namespace) only when the concept is genuinely distinct from the five that exist.

## Considered options

- **By-layer folders in Core (`Services/`, `Models/`, `Helpers/`, `Extensions/`).** Rejected; the pre-#67 layout. The files implementing one feature scatter across four folders, the buckets grow without bound, and `Services/` tells a reader nothing about what the system does.
- **One uniform by-domain scheme across all three projects.** Rejected. It would force a feature-folder rewrite of the GUI (reworking the ViewLocator and diverging from the Avalonia template) and a churn-only reshuffle of the small CLI, to make the GUI's pages and the CLI's verbs pretend to be Core's domains. Each project's existing idiom fits its role better.
- **No written rule (decide per file).** Rejected. Every new file re-opens the question, and the most natural wrong answer ("it's an extension, so it goes in an extensions folder") silently rebuilds the by-layer structure inside Core. Writing the rule down is what makes the reorg survive the feature work that follows it.

## Consequences

- The feature work sequenced after the reorg (model profile, keep set, output naming) creates Core files in the right domain from the start instead of being moved later, which is why #67 was done first.
- Inside Core there is no `Extensions/` or `Helpers/` folder by design; a utility serving one domain goes in that domain even when it is an extension method. The GUI deliberately differs.
- The Core root is kept small and meaningful, so "what is genuinely app-wide?" stays an answerable question rather than a catch-all.
- The GUI and CLI are left as they are; this ADR documents their structure rather than changing it, and a future GUI page or CLI verb has a clear home.
