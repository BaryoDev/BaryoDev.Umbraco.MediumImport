# Changelog

## Unreleased

- Import returns a 400 with a plain reason when the parent page or document type does not exist, the type is not allowed under the parent, or the field mapping is missing. Settings are not saved in that case. Preview only refuses a missing mapping.
- The default `MaxTotalBytes` is now 100 MB (was 500 MB).
- Finding a post by original url reads the parent's children once per run instead of once per story. Custom `IPostWriter` implementations can add `IndexByOriginalUrl` to use it.
- Any https canonical link is still kept as the canonical and original url fields (custom-domain publications rely on it). Matching against existing pages still only uses Medium hosts.
- README: the import endpoint is gated by Settings section access only.
