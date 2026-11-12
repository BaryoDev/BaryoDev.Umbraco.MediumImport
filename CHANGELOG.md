# Changelog

## Unreleased

- Preview and import return a 400 with a plain reason when the parent page or document type does not exist, the type is not allowed under the parent, or the field mapping is missing. Settings are not saved in that case.
- The default `MaxTotalBytes` is now 100 MB (was 500 MB).
- Finding a post by original url reads the parent's children once per run instead of once per story. Custom `IPostWriter` implementations can add `IndexByOriginalUrl` to use it.
- Only a Medium https link is used as the Medium canonical or original url.
- README: the import endpoint is gated by Settings section access only.
