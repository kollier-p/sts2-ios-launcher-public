# Steam Workshop browser (experimental)

This feature is currently on the \`feature/steam-workshop-browser\` branch, which was
created from \`feature/permissive-mods-workshop\`. Neither feature is merged into
\`main\`.

## Using it

1. Sign in to Steam using the launcher's existing sign-in flow.
2. Open **Mods** and choose **Browse Steam Workshop**.
3. Browse **Popular** and **Newest**, search by title/text, or choose
   **My subscriptions** to retrieve the current Steam account's subscriptions.
4. Select an item to view its details. Choose **Download and import**.
5. Steam's content is downloaded into a temporary folder and packaged into a
   ZIP; the existing ZIP mod import path handles confirmation and installation.
6. Close and reopen the launcher after a successful installation.

**My subscriptions** is an online query to the currently signed-in Steam
account (PublishedFile.GetUserFiles / \`mysubscriptions\`), not a local list.
Browsing an item does not subscribe or unsubscribe it on Steam.

## Implementation

- \`SteamConnection\` reuses its existing signed-in SteamKit2 CM connection.
- \`WorkshopCatalog\` uses PublishedFile.QueryFiles, GetUserFiles, GetDetails,
  and GetItemInfo (for a current manifest).
- \`WorkshopDownload\` handles direct HTTPS ZIPs and SteamPipe manifests via
  the existing \`SteamDepot\` and checksum-verified \`DepotDownload\` code.
- \`PickedModImport\` enforces the same ZIP layout and confirmation requirements
  as the "Import from Files" action.
- The SteamWire static protobuf contracts are explicitly registered for the
  JIT-free iOS runtime and have offline round-trip test fixtures.

## Known limitations / verification needed

- This is an initial implementation. It has **not** been built or tested on
  an iOS device or verified against a live signed-in Steam account.
- Some Workshop files use UGC download variants, redirects, or branch-specific
  snapshots not yet supported by this implementation.
- The direct-file path currently expects a ZIP. Non-ZIP single-file Workshop
  downloads are not auto-converted into mods.
- Workshop content must match the existing ZIP importer format: a mod manifest
  named \`<id>.json\` and its declared DLL and/or PCK, with supported package
  layout, archive limits, and user confirmations. An arbitrary Workshop asset
  is not automatically an installable STS2 mod.
- Automatic dependency installation, update checks, batch updates, and Steam
  subscribe/unsubscribe actions are not yet implemented.
- Import success does not mean the JIT-free game runtime will load every DLL
  or script-based mod.

Only install Workshop mods from authors you trust. Code mods execute with the
app's permissions, including access to the app's stored Steam sign-in.

## Testing

Run the repository's normal \`scripts/test.sh\` and Build IPA workflow on
this branch. Verify on device: Popular, Newest, search, My subscriptions,
item details, and downloads of both direct ZIP and SteamPipe items. The
initial SteamWire model changes must pass the iOS AOT/static-wire checks.
