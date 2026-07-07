# Repository Guidance

## User Documentation PDF

When changing user-facing documentation under `docs/user-manual/`, keep the generated PDF in mind.

1. Try to rebuild the manual after Markdown changes:

   ```powershell
   powershell -ExecutionPolicy Bypass -File docs/user-manual/build.ps1
   ```

2. If the build succeeds, include the updated PDF artifact:

   ```text
   docs/user-manual/output/ThinkComposer_User_Manual.pdf
   ```

   The installer project currently uses this PDF as its packaged user manual. If the installer source path is changed to an `Installer/` copy, copy the newly generated PDF to that referenced installer path as part of the same docs update.

3. The PDF build depends on the maintainer's local Pandoc/TeX setup and may fail on other machines. A failed manual PDF build should not block unrelated work. Report the failure clearly in the final response, leave the Markdown changes in place, and do not invent or hand-edit the generated PDF.
