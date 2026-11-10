# Project Controlling Excel Import: Implementation Specification

Status: implementation plan only; no application behavior has been changed.
Feature branch: `feature/project-controlling-import`.
Reference workbook: `SAE-Projektcontrolling_2026-11.xlsx` (1,931,718 bytes), inspected on 2026-11-10.
This specification replaces the reference workbook as implementation input. Build synthetic workbooks from the mappings and examples below; do not require the original file for development or tests.

## 1. Scope and Important Findings

Implement a read-only import of SAP project-controlling information for cost carriers already configured in TimeTrack. The Excel field **PSP-Element / Externe PSP-Element-ID is the application's Kostentraeger**. An external project ID identifies a parent project and is not an interchangeable matching key.

The reference workbook contains monetary plan/actual costs, project metadata, orders, and invoicing information. **It does not contain booked hours, planned hours, employee-level time entries, dated work records, or historical cost snapshots.** In particular:

- `Planbetrag in Buchungskreiswaehrung` and `Istbetrag in Buchungskreiswaehrung` are money, not hours.
- `Umsatzmenge` and `AuftrEingang - Menge` are invoice/order quantities, not reliable hours. Some rows have quantity `1`, `2`, or `*`; units and a conversion rule are absent.
- Monetary values must not be divided by an assumed hourly rate.
- Project start/end dates and invoice dates cannot reconstruct a history of worked hours.
- The workbook is a snapshot as of **2026-11-09 14:30:00**, despite `2026-11` in its filename. Do not assign all its values to November as monthly activity.

Provide a historical hours view using the application's existing local time entries. Label it as local tracking, not SAP hours. Persist successive imports to build a separate monetary snapshot history. One imported workbook produces one observation, not an invented past history.

Do not create projects, cost carriers, or time entries from this import. Do not change project names, customers, external/internal flags, or Quick Select settings. No SAP booking, Excel editing, recurring file watching, or employee synchronization is in scope.

## 2. Existing Application Integration

The application is WPF / MVVM, targets .NET Framework 4.8, and uses C# 7.3-compatible code and System.Data.SQLite. It uses a legacy project file with explicit file includes and `packages.config`, not an SDK-style project.

| Existing file / abstraction | Relevant behavior and planned integration |
| --- | --- |
| `TimeTracker.cs`, `EnsureSchema()` | Owns the SQLite connection and transactional schema initialization. Add backward-compatible import tables here; never alter existing hour triggers. |
| `TimeTracker.cs`, `GetProjects()` | Returns `Projektname`, `Kunde`, `AnzahlStunden`, `Kostentraeger`, `IstExtern`, `QuickSelectAktiv`. Read all projects for the allowlist. |
| `projects` table | `id` is the primary key; `projektname` is unique; `kostentraeger` is required text but **not unique**. Several local projects can share one carrier. |
| `time_entries` table | `datum` is `yyyy-MM-dd`; `startzeit` / `endzeit` are `HH:mm`; `projekt_id` references `projects.id`; `beschreibung` is optional. These are the only existing historical hours source. |
| `projects.anzahl_stunden` | Derived from time entries by triggers. Never write imported costs or hypothetical SAP hours here. |
| `ViewModel/ProjectsViewModel.cs` | Owns project management, search, date filtering, and local hours calculation. It currently handles overnight entries by adding 24 hours to a negative end-minus-start duration. Reuse that behavior in the new history query. |
| `ViewModel/MainViewModel.cs` | Owns `DbInstance`, page instances, `CurrentView`, and the `SelectViewCommandExecute` routing switch. Add a `controlling` page. |
| `Views/Templates.xaml` | Register the new view model's implicit data template. |
| `MainWindow.xaml` | Add a navigation item using the existing navigation conventions. |
| `Themes/Controls.xaml`, `Themes/Palettes/*` | Reuse current page, typography, DataGrid, icon, and dynamic theme resources. UI language remains German. |
| `Helpers/BackupHelper.cs` | The database is `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\timetracker.db`. New tables belong in this database so database backups include them. Do not introduce a separate Excel sidecar database. |
| `Zeitmanagement.csproj`, `packages.config` | Register new C# / XAML files and parser package references explicitly. |
| `Helpers/HelpContent.cs`, `Helpers/Changelog.cs` | Document the implemented behavior and its distinction between costs and hours at delivery time. Do not announce a completed feature during this planning change. |

The shared SQLite connection must not be used concurrently from a background worker. Capture the allowlist on its owning thread, parse the workbook independently in the background, then perform a short serialized database transaction on the owning thread. If a separate connection is introduced instead, explicitly test its WAL and transaction behavior; do not pass `DbInstance` into `Task.Run`.

## 3. Workbook Inventory and Physical Layout

Cell addresses below are Excel A1 notation, with one-based rows/columns. Hidden worksheets must remain readable. Worksheet order and ZIP part names are observations, not lookup contracts: resolve worksheet names through workbook relationships.

| Sheet name | Visibility | Observed used range | Header / data | Role |
| --- | --- | --- | --- | --- |
| `Auftraege` (actual XML name: `Auftr\u00e4ge`) | Visible | A1:O287 | Header row 4, body from 5 | Order pivot; not a primary import source. |
| `Projekte` | Visible | A1:R767 | Header row 4, body from 5 | Project/PSP pivot; useful for understanding UI values, not primary parsing. |
| `SAP#1PL` | Hidden | A1:F234 | Header row 1, body from 2 | Raw parent-project metadata. |
| `SAP#2PL` | Hidden | A1:G791 | Header row 1, body from 2 | **Primary PSP/cost source.** |
| `SAP#1AL` | Hidden | A1:J171 | Header row 1, body from 2 | Raw customer-order lines. |
| `SAP#2AL` | Hidden | A1:F772 | Header row 1, body from 2 | Raw order enrichment. |
| `SAP#1UL` | Hidden | A1:H146 | Header row 1, body from 2 | Raw invoicing lines. |
| `_PrjAuftragsListe` | Hidden | A1:P10002 | Header row 3, body from 4 | Formula-based order helper; not needed in version 1. |
| `_PrjListe` | Hidden | A1:BG999 | Header row 3, body from 4 | Formula-based PSP metadata and project financial enrichment. Actual relevant headers occupy A:T. |
| `_RefListe` | Hidden | A1:F999 | Header row 1, body from 2 | Formula-based parent-project reference; not the source of truth for project dates. |

Header spellings in this document use ASCII transliterations where necessary. Resolve `\u00e4`, `\u00fc`, `\u00f6`, `\u00df` in literal names to their Unicode characters in tests/code. For example, the actual `SAP#2PL!F1` text is `Planbetrag in Buchungskreisw\u00e4hrung`.

### 3.1 Snapshot timestamp

Both `Projekte!D1` and `Auftr\u00e4ge!D1` say `SAP-Daten von`. Their `E1` values are the numeric Excel date serial `46335.604166666664`, corresponding to `2026-11-09 14:30:00` in the reference workbook's 1900 date system. `workbookPr` does not set `date1904`.

Read `Projekte!E1` first; use the other visible sheet's E1 only if the first is absent. If both are present but disagree, require explicit selection of the authoritative source timestamp before commit. Respect the workbook's 1900/1904 date system. This is a source-local time with no specified timezone: store it as an unspecified local timestamp, not as UTC. Store actual import time separately in UTC.

If the timestamp is missing/invalid, ask the user to provide and confirm it; never silently use file modification time, import time, or filename month. The filename can suggest a month in that dialog but is not evidence of a day/time. `_PrjListe!AA1 = 46142.826388888891` is not the SAP timestamp and must not be used.

### 3.2 Primary source: `SAP#2PL`

| Column | Exact header text (escaped where necessary) | Domain field | Rule |
| --- | --- | --- | --- |
| A | Profitcenter | ProfitCenter | Text, optional. |
| B | Externe Projekt-ID | ExternalProjectId | Parent project ID; keep as text. |
| C | Externe Projekt-ID | ExternalProjectName | Parent project description; duplicate header is intentional. |
| D | Externe PSP-Element-ID | CostCarrier / PspElementId | **Required matching key**, keep complete ID including dot segments. |
| E | Externe PSP-Element-ID | PspName | PSP description; duplicate header is intentional. |
| F | Planbetrag in Buchungskreisw\u00e4hrung | PlannedCost | Optional numeric monetary amount. |
| G | Istbetrag in Buchungskreisw\u00e4hrung | ActualCost | Optional numeric monetary amount. |

Map duplicate headers by their known column positions and validate the entire header sequence. A dictionary keyed only by header text would lose columns B/C and D/E. Version 1 accepts this positional layout; fail clearly on structural changes instead of guessing.

The reference contains **519 distinct valid PSP IDs**, also present in `_PrjListe!A` and `Projekte!N`, with no duplicate valid PSP keys in any of these three sources. There are formatted/empty rows after the actual records; the dimension A1:G791 is not a record count. Iterate to the end and ignore genuinely empty rows. Exclude total/placeholder rows (`Gesamtsumme`, `Gesamtergebnis`, blank, `-`, `(Leer)`, `0`) and diagnose other nonempty invalid key rows. Do not require future IDs to begin with `1500`: that prefix describes the inspected dataset, not a confirmed universal business rule.

Use the raw F/G values as authoritative; do not import the visible pivot and raw sheet as separate records. A blank numeric cell is **unknown/not supplied**, distinct from an explicit zero. Store null and display `Nicht geliefert`. The helper/pivot frequently converts blanks to zero; do not use that conversion to claim a confirmed zero budget. Define `RemainingCost = PlannedCost - ActualCost` only when both values are known; negative results are valid overruns. A budget-consumption ratio is defined only for a strictly positive known plan.

The workbook does not supply an explicit currency code in these columns. Show `Buchungskreisw\u00e4hrung (nicht angegeben)` until a user explicitly configures/confirms a currency for the import series. Do not label these amounts EUR automatically. Currency is part of snapshot provenance; do not sum or compare different/unknown currency contexts as though they were interchangeable.

### 3.3 Enrichment: `_PrjListe`

Join `_PrjListe!A` to `SAP#2PL!D` by the normalized **full PSP ID**, never by worksheet row number. Header row 3; first data row 4. Its relevant columns are:

| Column | Header / meaning | Granularity and handling |
| --- | --- | --- |
| A | PSP-Element | Full carrier key. |
| B | Profitcenter | PSP metadata; raw A takes precedence on conflict. |
| C | Kunde | Parent-project customer label; `-` means unavailable. Do not replace local `projects.kunde`. |
| D | Auftragswert Gesamt | Parent-project total order value. |
| E | Abgerechnet Gesamt | Parent-project total invoiced value. |
| F | Offen Gesamt | Parent-project open order amount, normally D - E. |
| G | Abg % Gesamt | Parent-project invoiced ratio, stored as a fraction (1 = 100%). |
| H | Prj FG Gesamt | Parent-project progress ratio based on project cost totals, not hours. |
| I | WIP (offen) Gesamt | Parent-project WIP; can be negative. |
| J | Auftragswert lfd. Jahr | Parent-project order value through the workbook's current-year cutoff. |
| K | Offen lfd. Jahr | Parent-project open current-year amount. |
| L | Projekt-ID | Parent project identifier; raw B takes precedence on conflict. |
| M | PL | Parent-project manager display name, often surname. |
| N | Prj-Status | Parent-project status, e.g. `Freigegeben`, `Abgeschlossen`, or `-`. |
| O | Projekt | Parent project description. |
| P | PSP-Name | PSP description; raw E takes precedence. |
| Q | Plan-Kosten | Formula-derived equivalent of raw F, with blanks converted to zero. |
| R | Ist-Kosten | Formula-derived equivalent of raw G. |
| S | Kosten offen | Formula Q - R; not hours remaining. |
| T | FG | PSP ratio R / Q when Q > 0, otherwise 0; do not treat 0 as meaningful when raw plan is missing. |

Labels D:I contain embedded newlines/spaces; normalize header whitespace when validating these labels. D:K repeat across PSP rows of the same parent project and **must never be summed over those rows**. In the first release, store these as optional parent-project context, shown only in details with an explicit project-level label, not as matched-carrier totals. Use a single consistent value per parent per snapshot; conflicting repeats make that field unavailable and generate a warning. A parent record is retained only if at least one of its PSP rows matched the allowlist.

The source uses formulas including `XLOOKUP`, `UNIQUE`, `FILTER`, `SORT`, `SUMIFS`, shared formulas, and spilled arrays. Read saved/cached results, not formula text. Examples from the reference:

- Q4: `XLOOKUP(A4,'SAP#2PL'!D:D,'SAP#2PL'!F:F,0,0,1)` (stored with `_xlfn.` prefix).
- R4: corresponding lookup of raw column G.
- S5: `Q5-R5`.
- H4: `IF(D4>0,IFERROR(SUMIF(_PrjListe!L:L,L4,_PrjListe!R:R)/SUMIF(_PrjListe!L:L,L4,_PrjListe!Q:Q),0),0)`.
- J4: `SUMIFS('SAP#1AL'!I:I,'SAP#1AL'!B:B,L4,'SAP#1AL'!H:H,"<="&DATE(YEAR(TODAY()),12,31))`.

Do not recalculate formulas: `TODAY()` would change their original meaning. Do not implement an Excel formula engine. Missing/error caches in optional enrichment produce unavailable fields and warnings, not made-up values or failure of otherwise valid raw cost rows. Do not fall back to the helper/pivot when `SAP#2PL` is missing: report an unsupported layout.

### 3.4 Parent metadata: `SAP#1PL`

Header row 1; data starts at row 2. A = `Verantwortliche Kostenstelle`; B = `Projekt`; C = `Projektmanager`; D = `Ausf\u00fchrungsstatus`; E = `Geplanter Start`; F = `Geplantes Ende`.

B combines a description and a final parenthesized ID, e.g. `62D1001I_DTS10 Produktentwicklung (1500410A24RD001)`. Extract only the final `(...)` group and match it to raw `SAP#2PL!B`. Do not parse the first parentheses in a description. Keep the description and full manager name as optional context. Raw parent dates are Excel dates and are planning dates, not booking dates. For this example E5 = `45292` and F5 = `46387`. Prefer these raw metadata values to formula-derived `_RefListe` values when available.

Not every parent in `SAP#2PL` has usable metadata; allow missing manager/status. In particular `_RefListe` is unsuitable as an authoritative date source: its first reference row E2 and F2 both contain `46023`, whereas raw `SAP#1PL!E2 = 46023` and F2 = `46387`.

### 3.5 Visible pivot and excluded order sources

`Projekte` row 4 maps A = parent ID; B = project name; C = manager; D = status; E = customer; F = total order value; G = invoiced; H = open amount; I = invoiced ratio; J = WIP; K = project progress; L = current-year order value; M = current-year open amount; **N = full PSP ID**; O = PSP name; P = planned cost; Q = actual cost; R = remaining cost.

Parent metadata is not repeated on every row. Example: row 7 carries A:M plus PSP `1500410A24RD001.01`; row 8 contains only N:R for `1500410A24RD001.01.01`. There are subtotal rows such as A6 = `- Ergebnis`, blank/formatted tail rows, and pivot filter A1/B1 (`Profitcenter` / `(Alle)`). Do not forward-fill and import this presentation layer. The raw sheet includes the root `1500410A24RD001` independently of its children.

For completeness, these are the unused raw order/invoice layouts; do not confuse their quantities with hours:

| Source | Columns in order, from A | Version 1 |
| --- | --- | --- |
| `SAP#1AL` | Profitcenter; PSP-Element; Kundenauftrag; Auftraggeber (ID); Auftraggeber (name); Kundenreferenz; Gesamtlieferstatus (alle Pos.); Fakturadatum; Nettowert; AuftrEingang - Menge | Do not import detailed lines. |
| `SAP#2AL` | Kundenauftrag; Angebotsnummer; Auftraggeber; Nettowert; Ihr Zeichen; Lieferstatus (alle Positionen) | Do not import detailed lines. |
| `SAP#1UL` | PSP-Element; Auftraggeber; Verkaufsbeleg; Faktura; GesamtfakturierSts; Kundenreferenz; Nettowert; Umsatzmenge | Do not import detailed lines. |
| `Auftr\u00e4ge` | Auftrag; Angebot; Kunde; Kunden referenz; Kontakt; Auftragsstatus; Auftragswert Gesamt; Abgerechnet Gesamt; Offen Gesamt; Abg % Ges; Auftragswert lfd. Jahr; Offen lfd. Jahr; Prj-ID; Projekt; PL | Pivot, excluded. |
| `_PrjAuftragsListe` | Auftrag; Profitcenter; Angebot; Kunde; Kunden referenz; Kontakt; Auftragsstatus; Auftragswert Gesamt; Abgerechnet Gesamt; Offen Gesamt; Abg % Ges; Auftragswert lfd. Jahr; Offen lfd. Jahr; Prj-ID; Projekt; PL | Formula helper, excluded. |
| `_RefListe` | Prj-ID; Projekt; PM; Status; Start; Ende | Formula helper, excluded. |

Future order-line support must define independent order/invoice keys and preserve one-to-many joins; joining them directly to PSP cost rows would multiply costs. It is not required for this feature.

## 4. Matching and Filtering Contract

1. Capture all nonempty `projects.kostentraeger` values, regardless of `ist_extern`, `quickselect_aktiv`, current search text, or whether the project has time entries.
2. Normalize both sides by trimming surrounding Unicode whitespace (including NBSP), Unicode normalization Form C, and invariant case normalization. Retain the original value for display/provenance.
3. Do not remove internal spaces, punctuation, dots, leading zeros, or hierarchy segments. Do not use substring, prefix, fuzzy project-name/customer matching, or first-15-character truncation.
4. Import a raw PSP row only if its normalized D value is exactly in that allowlist. An allowlisted root does not authorize children; an allowlisted child does not authorize its parent/siblings.
5. Multiple local projects sharing a normalized carrier reference **one** imported carrier snapshot. Show all linked local projects. Never duplicate imported money when calculating totals.
6. If normalization makes differently spelled local carrier values equal, group them and show a preview warning; do not silently rewrite the local records.
7. Duplicate source keys with identical full payloads can be collapsed with a warning. Conflicting duplicates for a matched key block confirmation until resolved; never silently take the first/last row or sum them.
8. Re-read the allowlist before commit. If relevant projects/carriers changed during preview, rebuild preview and require confirmation. Do not commit using stale authorization.
9. Ignore unmatched rows; store only their aggregate count, not their names, carrier values, customers, or monetary data. Required parent context for a matched PSP is allowed but is clearly project-wide, not authorization to import sibling rows.
10. Existing imported history for a carrier no longer configured is excluded from current views and totals. Keep it as historical data until explicitly deleted; do not permanently link ownership to a mutable project name. Deleting a local project must not cascade-delete carrier history shared with another project.

Missing source rows are not zeros and do not delete older observations. Current overview uses the selected import's included carriers, not silently mixed dates. Offer a separate `Letzter bekannter Stand` view for configured carriers absent from that import; prominently mark their own older source date and exclude them from current-snapshot totals.

## 5. Parser, Validation, and Import Workflow

Use the maintained **DocumentFormat.OpenXml** SDK with a pinned package version verified to support .NET Framework 4.8 and the project's compiler/package format. Read raw cells and cached values without Excel COM, Office installation, macros, formula evaluation, or external-link refresh. If a chosen SDK API cannot expose formula caches, use its underlying cell values rather than a high-level recalculation API.

Handle shared strings, rich-text runs, inline strings, numeric cells, cached string formulas (`t="str"`), sparse cells, omitted blank cells, and cached error cells (`t="e"`). Preserve cell coordinates: the third serialized cell need not be column C. Use XML APIs, not regex/string splitting, for OOXML. Resolve relationship targets safely and reject paths escaping the package. Ignore unrelated charts, images, pivot caches, and external links.

Parse numeric OOXML values using invariant culture and `decimal`, keeping decimal precision through calculations and rounding only for display. Do not parse displayed German currency strings when a numeric value exists. Reject NaN/infinity, Excel error tokens, and nonnumeric nonblank matched cost cells. A negative actual value may be a credit/correction; retain it and flag it, do not reject it automatically.

Recommended initial safety bounds: 25 MiB compressed file, 100 MiB total uncompressed entries, 50,000 rows per consumed worksheet, and cancellation between read batches. Inspect ZIP metadata before parsing and bound actual decompression/XML reading too. Disable DTD/external entity resolution. These limits are implementation decisions, not observed workbook properties; show actionable errors when exceeded.

Workflow:

1. User chooses a local `.xlsx` through a file dialog. Open read-only and copy to a private temporary file or bounded memory buffer; hash exactly the bytes being parsed. Do not lock or change the original. Reject encrypted/invalid workbooks and unsupported extensions with a clear message.
2. Capture allowlist; validate required sheet and positional headers; obtain source timestamp and currency context. Read raw costs, then optional enrichment for matched keys only. A missing optional metadata sheet does not block raw costs.
3. Build a preview without database mutations. Show filename, SAP source date, import time, selected currency context, scanned valid PSP count, matched carriers, linked local project count, ignored count, missing configured carriers, warnings, and blocking errors. No match disables import and explains that only existing cost carriers are eligible.
4. Preview rows show full carrier, local project names, external project/PSP descriptions, plan/actual/remaining costs, and differences from the previous comparable snapshot. Distinguish missing values from zero. Validation details include sheet and cell reference.
5. On confirmation, revalidate authorization and write the batch, carrier observations, parent context, and revisions in one transaction. Failure/cancellation before commit leaves no partial batch. Cancellation cannot turn an already committed import into a reported failure.
6. Refresh controlling views only after commit; show a concise summary. Closing preview/canceling never writes data. Clean up temporary copies after success, cancellation, or error. Do not retain the workbook by default.

Classify errors: structural/raw matched-row errors block import; optional metadata/cache problems warn; unmatched malformed numeric data is ignored and not persisted. A malformed carrier key that could represent a configured carrier must be surfaced, not silently normalized into a different key. Include file-in-use/read/access errors and rollback behavior.

## 6. Persistence and Snapshot Semantics

Add the following logical schema through `EnsureSchema()` using idempotent additive migrations, foreign keys, indexes, and parameterized SQL. Final column names can follow existing conventions, but preserve these contracts:

| Table | Required fields / constraints |
| --- | --- |
| `controlling_imports` | Integer ID; basename only; SHA-256 file hash; source-local timestamp; UTC imported timestamp; timestamp origin (workbook/user); nullable currency code plus explicit currency-context ID; parser/schema version; deterministic normalized allowlist hash; deterministic accepted-payload hash; matched/ignored/warning counts. |
| `controlling_carriers` | Integer ID; unique normalized carrier key; original carrier display text. No foreign key to mutable project names. |
| `controlling_snapshots` | Import ID; carrier ID; source sheet/row; external parent ID; PSP name; profit center; nullable plan/actual money; unique `(import_id, carrier_id)`. Link to matching parent context if available. |
| `controlling_project_context` | Import ID; external parent ID; optional parent name/customer/manager/status/planning dates and D:K cached financial fields; unique `(import_id, external_parent_id)`. Retain only parents needed by accepted rows. |

SQLite REAL cannot preserve decimal money exactly. Store monetary values as invariant decimal TEXT (or a consistently specified exact scaled-integer representation), parse into `decimal` on read, and calculate totals in application code. Do not use SQL SUM on decimal text if it coerces values to floating point. Store ratios consistently as invariant decimal text too. Store unknown values as SQL NULL, not zero/string placeholders.

Idempotency and revisions:

- Define an identical import as the same source timestamp, currency context, parser version, allowlist hash, file hash, and accepted-payload hash. Enforce a unique fingerprint for that tuple and return `Already imported` without duplicate observations.
- Same file with an expanded/changed allowlist is allowed after a new preview: import only the now-eligible rows. This addresses adding a configured carrier after a previous import. A file-hash-only uniqueness rule is insufficient.
- Reimports/corrections are immutable new batches, not destructive overwrites. For a carrier at one source timestamp and currency context, its active observation is the one with greatest committed import ID. Older revisions remain inspectable. Rows omitted by a later partial import do not erase previous observations for other carriers.
- A batch overview is the exact contents of that batch. A carrier history resolves revisions per source timestamp/context, then orders by **source timestamp**, not import order. Importing an older file must not replace the newest source observation as the latest known value.
- Identical values at later source dates still form observations. A delta is the difference of consecutive comparable observations, not a booked daily/monthly cost. Different currency contexts have separate histories.
- Keep absent plan/actual values absent; no interpolation/backfilling. Do not add a zero observation for a disappeared PSP.

Provide APIs to save an accepted preview transactionally, list batches, load one batch, resolve per-carrier history/revisions, and delete an import with confirmation (fall back to the previous remaining revision). Associate local projects dynamically through current normalized carrier values. A later carrier edit must not relabel old observations to the new carrier.

## 7. Hours History and Double-Counting Rules

Compute local history by joining **current eligible local project IDs** to `time_entries.projekt_id`, then grouping by carrier and date. Use the same entry validation/overnight convention as `ProjectsViewModel`; include each entry exactly once and report invalid entries instead of inventing durations. For overnight entries, assign the duration to the stored `datum`, matching the existing project view. Read-only aggregation must not create bookings or change the running timer.

Offer day/week/month buckets (ISO weeks starting Monday), selectable date range, and optional cumulative local hours. Empty known buckets within the selected range can show 0 local hours. Keep the underlying duration precise; round only labels. Display decimal hours and optionally `HH:mm` with hours above 24 supported. State that local hours can include parallel bookings and are not SAP-reconciled billable time; do not feed this graph into `SapBookingCalculator`.

Default to all local projects sharing the selected carrier, with an explicit project filter. Changing a project's carrier affects current grouping of its historical local entries because the existing schema does not record historical carrier assignments; disclose this limitation. Do not pretend to reconstruct assignment history.

Keep monetary and hour charts separate: no shared scale, no costs labeled `h`, no plan-hours line derived from money. Overlapping PSP hierarchy rows are distinct exact cost objects, not a license to roll up descendants. Version 1 must not claim raw root values include children or vice versa; the workbook does not establish that accounting rule. Show per-PSP values and per-PSP ratios. Do not offer a grand cost sum across ancestor/descendant selections without a confirmed accounting rule. Parent D:K context is never summed across its PSP children. Pure counts and local-hour totals can be aggregated without duplicating carrier records or entry IDs.

## 8. UI Specification (German Product UI)

Add a dedicated **Projektcontrolling** page with an `Excel importieren` action. A project detail action can navigate there with its carrier selected. Keep project editing and existing hour filters unchanged.

### 8.1 Overview

- Compact header: page title, selected import/source date, filename, and import action. Mark stale/missing data explicitly; freshness is based on source timestamp.
- Summary strip: matched carrier count, linked local project count, warning count, and source date. Avoid a misleading portfolio money total with unresolved PSP hierarchy or currency semantics.
- Search by local project, carrier, external project name, PSP name, and customer. Filters for source batch, manager, project status, missing plan, and overrun. Searches do not change the import allowlist.
- Virtualized sortable DataGrid: full Kostentraeger, local project name(s), PSP name, SAP project, plan cost, actual cost, remaining cost, budget consumption, and status. Put source date in the row when showing last-known observations with different dates.
- Use `Nicht geliefert` for missing amounts; `Kein positives Budget` for unavailable consumption. Show textual `Budget ueberschritten` as well as color when actual > positive plan. Negative remaining cost is not clamped to zero.
- Selection opens an adjacent detail region when space permits, otherwise a full-width lower region. Avoid many dense nested cards. Resize columns and retain horizontal scrolling where full IDs would otherwise be truncated; provide copy carrier and tooltip for abbreviated names.

### 8.2 Carrier details

Show local linked projects and customer(s), exact carrier, external parent ID/name, PSP name, profit center, source date, import date, and currency context. Local and SAP customer names must be distinct labels. Show parent manager/status/planning dates and optional D:K values in a collapsible **SAP-Projektkontext** area explicitly marked as project-level, not selected-PSP totals.

Use tabs or a segmented control for:

1. **Kostenverlauf**: plan/actual monetary observations against source time; table fallback with exact values, deltas, source filenames, and revision indicators. One snapshot shows a point plus `Ein Importstand vorhanden`, not a fabricated line through preceding months. Missing values break lines; negative changes remain visible. Distinguish import revisions from new source dates.
2. **Lokaler Stundenverlauf**: day/week/month local hours, optional cumulative curve, date/project filters, exact-value table, and source label `Lokal erfasste Stunden`. Empty state `Keine lokalen Buchungen vorhanden`. Provide drill-through to the existing entries view without modifying data.
3. **Importdetails**: provenance, warnings, mapping summary, and source row/cells. No data for ignored carriers is exposed.

Use an existing chart library only if present and compatible; otherwise choose a pinned maintained WPF chart package compatible with .NET Framework 4.8 after a small build spike. Avoid a platform upgrade for charts. Charts are optional enhancements: the historical tables and accessible numeric data are required and must work without them. Use existing theme resources and icon glyph conventions; keyboard navigation, focus states, non-color status labels, light/dark theme support, and a responsive layout are required. Validate UI through builds and code review only; **do not take screenshots or use browser-based verification**.

## 9. Self-Contained Fixtures and Expected Results

Tests must generate small `.xlsx` files using Open XML SDK APIs. Include workbook relationships, sheet parts, optional shared strings/styles, explicit column references, and a numeric date cell. No dependency on the original workbook or installed Excel is permitted. The following values are sufficient to exercise the real layout.

### 9.1 Real observed sample cells

Common snapshot cells: `Projekte!D1 = "SAP-Daten von"`, `Projekte!E1 = 46335.604166666664`; put the 18 headers from section 3.5 in row 4 if testing the visible sheet. For the primary sheet, put the seven exact headers from section 3.2 into A1:G1.

| Raw row | A: Profit center | B: Parent ID | C: Parent name | D: Carrier | E: PSP name | F: Plan | G: Actual |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2 | SAE410 | 1500410A24RD001 | 62D1001I_DTS10 Produktentwicklung | 1500410A24RD001 | 62D1001I_DTS10 Produktentwicklung | **blank** | 15733.48 |
| 3 | SAE410 | 1500410A24RD001 | 62D1001I_DTS10 Produktentwicklung | 1500410A24RD001.01 | 6D1010ITI_DTS10 Produktentwicklung Step1 | **blank** | 83333.31 |
| 4 | SAE410 | 1500410A24RD001 | 62D1001I_DTS10 Produktentwicklung | 1500410A24RD001.01.01 | DTS10_ORG_DTS10 Organisation PMT | **blank** | 30383.5 |
| 520 | SAE630 | 1500630C26IC001 | GLX Verrechnung 1500630 | 1500630C26IC001.02.05 | KD_05_POCS | **blank** | 612.5 |
| 521 | SAE630 | 1500630C26IC001 | GLX Verrechnung 1500630 | 1500630C26IC001.02.06 | KD_06_DAT(STW) | **blank** | 2260.3000000000002 |

These are a subset, not an instruction to populate intervening rows. Sparse fixtures may contain row 520 directly after row 4. Do not hard-code the observed row count into runtime validation.

For helper enrichment, `_PrjListe` A3:T3 contains section 3.3's headers. Row 4 has A=`1500410A24RD001`, B=`SAE410`, C=`-`, D:K all numeric 0, L=`1500410A24RD001`, M=`Amato`, N=`Freigegeben`, O=`62D1001I_DTS10 Produktentwicklung ` (trailing space), P=`62D1001I_DTS10 Produktentwicklung`, Q=0, R=15733.48, S=-15733.48, T=0. Row 5 changes A to `1500410A24RD001.01`, P to the Step1 description, R to 83333.31, and S to -83333.31. **Raw blank F still means unknown plan**, regardless of helper Q=0.

For raw project metadata use `SAP#1PL!A5 = "DD - DTS (1500410)"`, B5=`62D1001I_DTS10 Produktentwicklung (1500410A24RD001)`, C5=`Claudio Amato`, D5=`Freigegeben`, E5=45292, F5=46387. Formula-cache tests should encode helper O4 as `t="str"` with a formula and `<v xml:space="preserve">62D1001I_DTS10 Produktentwicklung </v>`; extract inner text, never an XML-node object's string representation.

### 9.2 Required synthetic acceptance cases

1. Local projects A/B both have carrier `1500410A24RD001.01`, project C has `1500630C26IC001.02.06`, project D has an absent carrier. Using the five raw rows above imports **two carrier records**, links three local projects, ignores three valid source rows, and reports one missing configured carrier. The `.01.01` child and root must not be imported by prefix.
2. Whitespace/case/NBSP variants match after normalization; a missing dot, extra internal space, leading-zero change, or parent-only key does not match the child. Numeric-looking identifiers remain text. Quick Select disabled and internal projects remain eligible.
3. A matched row F=10000 and G=12000 yields remaining=-2000 and consumption=120%. F=0/G=10 has no defined percentage. F absent/G=10 has unknown plan/remaining. F=100/G=-10 remains a correction with remaining=110. These are synthetic money examples, not hours or observed workbook values.
4. Reordered/missing required headers fail; identical duplicate keys warn/collapse; conflicting matched duplicates block. A malformed numeric cell on an unmatched row is not persisted and does not block eligible valid rows.
5. Shared/inline/rich-text strings, sparse cells, cached formula strings/numbers, missing/error caches, hidden sheets, and formatted tail rows parse correctly. An absent optional helper does not prevent raw import. Missing raw sheet blocks it. An alternative sheet order/relationship target still works.
6. Timestamp serial converts to `2026-11-09 14:30:00`; conflicting dates require confirmation; missing dates require user input. Test a synthetic 1904-date workbook separately. Formula `TODAY()` and external links are never executed.
7. One snapshot yields one monetary observation. A synthetic later source snapshot for the same key with known plan=10000 and actual=12500 after actual=12000 produces a +500 cost-snapshot delta, **not 500 hours or a monthly booking**. Reverse import order does not change chronological latest selection.
8. Identical reimport writes nothing. Expanded allowlist can import newly eligible keys from identical bytes. A correction at the same timestamp creates an inspectable revision and only one active chart point. Deleting the latest correction reveals the previous revision.
9. Local A entry `2026-11-02 09:00-10:30` and B entry on the same day `11:00-12:00` yield 2.5 local hours for their shared carrier. B entry `2026-11-03 23:30-00:30` yields 1.0 hour on November 3. Monthly total is 3.5, with no imported time entries or monetary conversion. Grouping by carrier must not multiply these entries through the A/B relationship.
10. Zero matches, no local hours, missing metadata, missing budget, mixed source dates, negative costs, differing currency contexts, and a single observation have explicit UI states. Ancestor/descendant carriers do not produce an unqualified grand money total.
11. Cancellation, changed allowlist during preview, locked/corrupt/encrypted/oversized files, and forced SQL failure leave existing projects/time entries unchanged and create no partial import. Old databases migrate without data loss and backup/restore retains import history.
12. Changing/deleting one of several local projects sharing a carrier does not corrupt or delete the other project's imported carrier history. A carrier removed from the current allowlist disappears from current views without silently destroying history.

## 10. Implementation Sequence and Completion Gates

1. Add parser/domain DTOs and synthetic workbook tests first. Verify SDK/package compatibility with .NET Framework 4.8 and the legacy project format. Implement primary mapping, timestamp, allowlist filtering, and preview diagnostics independently of WPF/SQLite.
2. Add additive schema migration and transactional repository APIs. Test exact monetary persistence, idempotency, expanded allowlists, revisions, historical ordering, rollback, and older database upgrade.
3. Add local hours aggregation/history queries with shared-carrier deduplication and overnight/date-bucket tests. Keep imported costs entirely separate from local hours and SAP booking calculations.
4. Add `ProjectControllingViewModel`, `ProjectControllingView.xaml` / code-behind, navigation/template registration, import preview, overview, detail tables, and history filters. Suggested parser home: `Helpers/ProjectControllingExcelReader.cs`; domain types may live nearby or in a small feature folder. Do not turn `ProjectsViewModel` into the Excel parser.
5. Add optional charts only after both historical tables work. Register new files/packages explicitly in the project; update help/changelog only for delivered behavior.
6. Run focused parser/repository/history tests, then Debug and Release builds using a Windows MSBuild toolchain with .NET Framework 4.8 reference assemblies. Exercise migrations on a disposable database, never the user's live database. No existing test project is visible in the inspected repository; create one focused compatible test project if needed and document its test command.

Done means all acceptance cases above pass; no original Excel is needed to compile or run tests; only currently configured exact carriers are imported; no existing local project/bookings are mutated; all monetary units and time sources are honest; revisions survive restart; and the UI remains usable with long IDs, many rows, resizing, keyboard navigation, and every existing theme. Verification is build/test/code-based, without screenshots.