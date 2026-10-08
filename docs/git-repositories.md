# Git repositories and optional model tracking

ThinkComposer's **Composition > Git > Repository...** and **Domain > Repository...** commands open the same repository window. Commands act on the visible working repository containing the selected file, including README, LICENSE, and other files. A branch belongs to that repository, not to a Domain or Composition. Separate repositories or worktrees are needed for independent branches.

Git for Windows must be on PATH. Existing Git Credential Manager and SSH configuration provide authentication; credentials are never stored in model manifests. Remote operations are explicit. Status is local and does not fetch.

## Ordinary repository work

Opening or saving a container in a repository does **not** enable managed model tracking or create companion directories. Files outside Git remain standalone. The Repository window can initialize a chosen directory or clone a remote into it.

The desktop window starts on **Changes**: select files to preview, choose **Stage selected files**, review **Staged**, and enter a commit summary. Commit uses the entire repository index; **Push** is a separate action. New containers are labeled **New** and include guidance for the first commit. **History** previews recent commits. The header contains a local branch selector and fetch/pull/push controls. Repository settings, stash, restore/revert, releases, and optional model tracking are grouped into menus. **Operation details** retains command results and errors. Light and dark themes apply to buttons, lists, menus, and prompts.

```text
thinkcomposer git status --repo C:\Models
thinkcomposer git branch-create --repo C:\Models --branch draft
thinkcomposer git switch --repo C:\Models --branch draft
thinkcomposer git stage --repo C:\Models --path README.md --path Standard.tdom
thinkcomposer git commit --repo C:\Models --message "Clarify terminology"
thinkcomposer git push --repo C:\Models --remote origin --branch draft
```

`--input <container>` can locate the repository instead of `--repo`; it never narrows commit, branch, or remote operations to that file. Save, stage, commit, and push are separate actions. Commit uses the displayed staged index. Whole-file staging includes any independently staged repository files only when the user commits them.

Fetch does not change working files. Pull fetches and fast-forwards when possible. If branches diverge, it reports `DIVERGED`; review `git diff --revision HEAD...@{upstream}` and explicitly choose Merge. App/CLI merges and reverts remain uncommitted for review. Branch switch, merge, pull, and revert require clean tracked changes. Stashing is explicit and excludes untracked files. No automatic reset, cleanup, force-push, or history rewriting is performed.

Use `thinkcomposer git --help` for remotes, branches, history, diff, restore, stash, revert, and annotated release-tag commands. Tag publication is separate from branch publication. Externally initiated rebase/cherry-pick operations must be completed in the originating client. Open documents are monitored even when the panel is closed. Clean changed documents reload; unsaved documents are preserved and saving over an externally changed file is blocked. **Compare unsaved with disk** preserves an editable snapshot and displays the differences before explicit reconciliation.

## Enable managed tracking only where needed

```text
thinkcomposer git tracking enable --input C:\Models\Standard.tdom
thinkcomposer git setup --repo C:\Models
thinkcomposer git stage --repo C:\Models --path Standard.tdom --path .gitattributes
thinkcomposer git commit --repo C:\Models --message "Enable managed model tracking"
```

Enabling creates `.tdom/index.json`, `.tdom/README.md`, and `.tdom/<tracking-id>/`. Compositions use `.tcom/` instead. A tracking ID is independent of the model's internal identity. Each artifact opts in separately. The authoritative container remains alongside the controlled directories:

```text
.git/
.tdom/
  index.json
  README.md
  <tracking-id>/
    projection.json
    Domain.json
    ...retained package content...
Standard.tdom
README.md
LICENSE
```

Container and projection manifests share a versioned SHA-256 content fingerprint. The projection also records the exact whole-container SHA-256 and hashes of projected files. Canonical JSON has sorted object keys while retaining meaningful array order. Export timestamps and generated previews/sidecars are excluded. Generated OPC property relationships are normalized out, and authored envelope extensions participate in the fingerprint. Commit IDs are not content fingerprints. Registry, projection, and resolution documents are described by [the version 1 tracking schema](thinkcomposer-git-tracking.schema.json).

`git verify` independently recomputes the expected projection; copying hash strings cannot make changed content pass validation. No-op refresh preserves container bytes. Registered desktop/CLI saves update the projection through the shared persistence layer and retain a recoverable local transaction journal. Transactions are recoverable, not filesystem-wide atomic.

Stage the container, its projection, and registry together. ThinkComposer expands a selected managed artifact to this group. External clients must stage the group themselves; verification checks the **index**, not just the working files.

## Editing projections

Containers remain authoritative. Payload JSON and retained package assets can be edited deliberately. `index.json`, `projection.json`, fingerprints, and hashes are tooling-owned. Preserve IDs and ordering. A projection contains complete snapshot data, never a queue of interchange operations.

```text
thinkcomposer git projection import --input C:\Models\Standard.tdom
thinkcomposer git projection import --input C:\Models\Standard.tdom --apply
```

Preview lists additions, modifications, and removals. Removing content proposes a deletion, unlike the existing additive JSON operations importer. Import validates before replacing the container. Independent edits block automatic refresh and normal saves until imported or explicitly discarded with `projection refresh --discard-edits`.

If the container also changed, import uses the projection's recorded source snapshot. Matching snapshots are recovered from the working container, Git's worktree-local cache, or repository history. Supply `--baseline <container>` if the snapshot cannot be found. It must match the recorded whole-file hash. Three-way conflicts retain both packages and projection inputs in a resumable resolver session; absence of a baseline is never interpreted as permission to overwrite.

After an external rename use `git tracking relocate --repo <directory> --id <tracking-id> --path <new-relative-path>`. Enable copies separately. Save As creates a standalone, unregistered copy. Opening a container outside its registered repository does not recreate directories.

`tracking disable` removes embedded tracking metadata and marks the registry entry inactive. It retains projection files so their removal can be reviewed separately. Run setup again after registration changes to refresh managed attributes.

## Conflicts and external clients

Setup installs local merge/diff driver configuration and hook scripts and adds attributes only for registered paths. Repeat setup in each clone; cloning never executes it automatically. Existing unrelated attributes and hooks are retained. If hooks already exist, setup stops with instructions for integrating the finalizer/verifier into them rather than overwriting them.

The semantic driver uses the ancestor, current, and incoming containers. It matches stable element identities, combines compatible edits, and reports competing edits or delete/reference conflicts. Ordered collections and opaque assets are handled conservatively. Original inputs and decisions remain in a merge session. Model changes and view changes appear under their corresponding payload paths.

The projection driver retains source versions for reconciliation. Finalization verifies source revisions and regenerates only derived projection files associated with a verified container merge. Unstaged edits or missing provenance block automatic finalization.

**Git can prepare its merge tree before `pre-merge-commit` runs.** When finalization changes the staged tree, the hook deliberately stops the automatic commit with `MERGE_READY_TO_COMMIT`. The semantic merge and projection repair have succeeded: review the staged result and run `git commit` to finish. A successful verified commit must never contain stale companion files.

For a competing model edit, select the conflicted container in the Repository window and choose **Resolve selected model**, or use:

```text
thinkcomposer git resolve --repo C:\Models --path Standard.tdom
thinkcomposer git resolve --repo C:\Models --session <reported-directory>
thinkcomposer git resolve --repo C:\Models --session <reported-directory> --apply
```

The session holds `1.tcom` (ancestor), `2.tcom` (current), `3.tcom` (incoming), `conflicts.json`, and `resolutions.json`. Resolution entries select `current`, `incoming`, `ancestor`, `delete`, or `custom` plus a JSON `value`, keyed by the reported path. No noninteractive operation chooses a winner. Validation failures cannot be dismissed as resolved. Closing the resolver preserves progress. Selecting the same conflict resumes matching inputs, or use **Resume resolution** with the session directory. `resolve --session <directory> --abort` abandons only that session while retaining its inputs; abort the Git merge separately. Original embedded previews are available beside property values. Validation cannot be overridden: reconcile decisions or explicitly select an entire valid input package.

Clients that bypass hooks/drivers do not gain automatic integrity guarantees. Post-checkout and post-merge checks report inconsistencies without rewriting history. CI should independently run:

```text
thinkcomposer git verify --repo . --revision HEAD --json
```

Use `--staged` for index checks or omit both options for working-tree checks. Exit codes remain `0` success, `1` expected failure/conflict, and `2` unexpected failure. JSON failures include a diagnostic code.

## Recovery and migration

After an interrupted tracked save, use `git recover --repo <directory> --mode complete` or `--mode rollback`. Recovery inputs live under Git's worktree-local state directory. Do not delete a transaction directory before recovering necessary work.

Legacy `gitSync` and `embeddedDomainGitSync` remain readable but no longer activate background synchronization. Old link, push-with-message, and pull-to-output commands report migration guidance. Existing private cache repositories are left intact.

Choose or clone a visible repository, then preview and apply migration:

```text
thinkcomposer git migrate --input C:\Old\Model.tcom --repo C:\Models --path Model.tcom
thinkcomposer git migrate --input C:\Old\Model.tcom --repo C:\Models --path Model.tcom --apply
```

Different destination content blocks migration; neither version is overwritten. Migration removes legacy linkage only through this explicit operation and does not enable managed tracking. Refreshing a Composition's embedded Domain remains a separate **Update Embedded Domain** action.

## Developer validation

Build the CLI through the solution's `Debug|Mixed Platforms` mapping, then run `tests/git-workflow.ps1`. It uses isolated local repositories and a bare remote under `artifacts/git-tests`; it never modifies this repository's index or history. Git shell hooks require a normal Windows execution environment. Retained test directories include the model inputs, histories, merge journals, and `passed.txt` for diagnosis.
