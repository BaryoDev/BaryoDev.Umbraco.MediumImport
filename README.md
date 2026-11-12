# BaryoDev.Umbraco.MediumImport

Import your Medium export into Umbraco 17. Free, MIT licensed. Targets Umbraco 17 and .NET 10.

Each claim below names the test that backs it. Test classes are under `tests/`.

## What it does

- Reads the posts in a Medium export zip and lists them with their title, date, image count, gist count and status. (`EngineTests.Preview_statuses_for_fixture_export`)
- Drafts and responses are marked as such in the preview and counted, so you can leave them out. (`EngineTests.Preview_statuses_for_fixture_export`)
- Code blocks keep their exact text. `<br>` becomes a newline, the text is HTML-encoded, and the language Medium recorded becomes `class="language-x"`. A block with an unknown language gets no class. Adjacent blocks are not merged. (`CodeBlockTests.Br_becomes_newline_and_spans_drop`, `Text_is_encoded_never_copied`, `Unknown_language_gets_no_class`, `Adjacent_blocks_stay_separate`)
- The language class survives a save through Umbraco's rich text editor. (`RichTextRoundTripTests.Language_class_and_newlines_survive_rte_save`)
- Gists are fetched from GitHub and inlined as code blocks. If the fetch fails, the link stays and the post gets a warning. (`CodeBlockTests.Gist_alone_in_paragraph_is_replaced`, `EngineTests.Gist_failure_keeps_link_and_warns`)
- Each Medium image is copied into the media library and the body points at the media item. An image that cannot be fetched is dropped from the body and listed as a failure on that post. (`CodeBlockTests.Medium_image_rewritten_with_udi`, `EngineTests.Image_failure_drops_img_and_lists_failure`)
- The dashboard result panel shows each post's failures and warnings under its row, so dropped images are visible. (No automated test, the dashboard script has no test harness. It is checked with `node --check`.)
- Only JPEG, PNG, GIF and WebP images are accepted, judged by their first bytes. SVG and anything else is dropped and listed as a failure on the post. (`NetTests.Svg_image_is_rejected`)
- Images on other hosts are not fetched. They stay in the body and are listed as a warning. (`EngineTests.Other_host_image_listed_as_warning`)
- A rerun updates the posts it imported before and does not create duplicates. (`EngineTests.Import_creates_then_rerun_updates_without_duplicates`)
- A post's node and its import record are saved together. If the publish fails, nothing is left behind and a rerun creates exactly one node. (`AtomicStoryTests.Failed_publish_leaves_no_node_and_no_key_row_and_a_rerun_creates_one_node`)
- A post you edited after the import is skipped on rerun, with the reason "edited since import". (`EngineTests.Edited_post_skipped_with_reason_edited_since_import`)
- A post whose node was deleted is created again. (`EngineTests.Deleted_node_created_again`)
- A post that already exists with the same Medium URL in the mapped original URL field is adopted instead of duplicated. Without that field mapped there is no adoption. (`EngineTests.Adoption_by_original_url_writes_key_row`, `No_adoption_without_original_url_mapping`)
- The same image URL is fetched once per run. (`EngineTests.Same_image_url_fetched_once_per_run`)
- A post whose HTML nests deeper than 512 levels fails with a fixed reason. The rest of the export imports normally. (`EngineTests.Deeply_nested_body_fails_the_story_not_the_process`, `ParsingTests.Parse_reports_a_too_deep_post_as_a_failure_instead_of_throwing`, `SanitizerTests.Sanitize_refuses_a_too_deep_body_with_a_fixed_exception`)
- Script, event handlers and `javascript:` URLs from the export do not reach the page. `class` is allowed on `code` only. (`SanitizerTests.Sanitize_ReturnsExactOutput`, `CodeBlockTests.Language_class_allowed_only_on_code`, `Resolved_image_with_javascript_url_is_dropped`)
- Canonical URL: a custom-domain Medium publication keeps its https canonical link in these fields too; only medium.com links are matched against existing pages. "This site" leaves the mapped canonical field empty. "Keep Medium" writes the Medium URL into it. (`EngineTests.Canonical_this_site_leaves_field_empty`, `Canonical_keep_medium_writes_url`)

## Get the Medium export

In Medium, open Settings, then Security and apps, then Download your information. Medium emails you a zip. You upload that zip in the dashboard. The package reads only the `posts/` folder.

## Install

```
dotnet add package BaryoDev.Umbraco.MediumImport
```

## Dashboard

Only users with access to the Settings section can use it. Other users get a 403. (`ApiTests.Non_settings_user_gets_403`) That section check is the only gate on the import endpoint. It does not apply the user's start nodes, per-node permissions or publish rights, so give the Settings section only to people you would trust with those. Import returns a 400 with a plain reason when the parent does not exist, the document type does not exist or is not allowed under the parent, or the field mapping is missing. Preview only refuses a missing mapping, so the post list shows before the parent and type are chosen. (`ApiTests.Import_rejects_bad_settings_with_a_plain_400_and_preview_only_a_null_mapping`)

1. Open the Settings section and the "Medium import" dashboard.
2. Upload the export zip. An upload over `MaxUploadBytes` gets a 413. (`ApiTests.Upload_over_cap_gets_413`)
3. Check the post list. Preview returns the stories. (`ApiTests.Preview_returns_stories`)
4. Pick the parent page and the document type allowed under it.
5. Map the fields: title and body are required. Publish date, summary, cover, tags, canonical URL and original URL are optional.
6. Choose Save as draft (the default) or Publish, and the canonical mode (This site is the default).
7. Click Import. The import runs as a background job and the dashboard polls it. (`ApiTests.Import_then_poll_until_completed`)
8. A second import while one is running gets a 409. (`ApiTests.Second_import_gets_409`)
9. Cancel stops the import after the post being written. Posts already written stay, and a rerun shows them as already imported. (`EngineTests.Cancel_returns_partial_result_and_runs_completed`, `Cancel_after_first_post_then_rerun_shows_it_already_imported`)
10. The result lists what was imported, updated, skipped and failed, with a reason for each. (`EngineTests.One_story_throwing_does_not_stop_the_run`)

The settings you pick are saved and loaded next time. (`SettingsStoreTests.Settings_roundtrip`, `Settings_load_ignores_garbage`, `ApiTests.Settings_user_gets_204_when_nothing_is_saved`)

The uploaded zip is stored outside `wwwroot` and deleted if you cancel the job before it starts, and old uploads are cleaned up after `UploadTtl`. (`UploadTests.Upload_path_is_outside_wwwroot`, `JobTests.Cancelling_a_queued_job_deletes_its_upload`, `UploadTests.Cleanup_deletes_only_old_files`)

## Hooks

The snippets need `using BaryoDev.Umbraco.MediumImport.Engine;` and `using BaryoDev.Umbraco.MediumImport.Parsing;`. Register your implementation in DI. They apply to every import, from the dashboard or from your own code.

Filter which stories may be imported. A rejected story shows in the preview as skipped, with your reason. (`EngineTests.Filter_reason_shows_in_preview`)

```csharp
public sealed class OnlyTagged : IMediumStoryFilter
{
    public string? Reject(MediumStory story) =>
        story.Tags.Contains("umbraco") ? null : "not tagged umbraco";
}

builder.Services.AddSingleton<IMediumStoryFilter, OnlyTagged>();
```

Choose the url segment. Return null to let Umbraco derive it from the name. (`EngineTests.Slug_policy_value_passed_as_url_segment`)

```csharp
public sealed class KeepOldSlugs : IMediumSlugPolicy
{
    private static readonly Dictionary<string, string> OldSlugs = new() { ["0a1b2c3d4e5f"] = "my-old-slug" };

    public string? SlugFor(MediumStory story) => OldSlugs.TryGetValue(story.PostId, out var s) ? s : null;
}

builder.Services.AddSingleton<IMediumSlugPolicy, KeepOldSlugs>();
```

Run code after each story and once when the run ends. (`EngineTests.Callback_called_per_story_and_once_at_end`)

```csharp
public sealed class LogImports : IMediumImportCallback
{
    public Task OnStoryAsync(MediumStory story, StoryResult result, CancellationToken ct)
    {
        Console.WriteLine($"{story.Title}: {result.Outcome}");
        return Task.CompletedTask;
    }

    public Task OnRunCompletedAsync(ImportResult result, CancellationToken ct) => Task.CompletedTask;
}

builder.Services.AddSingleton<IMediumImportCallback, LogImports>();
```

A callback that throws at the end of a run becomes a warning on the result. (`EngineTests.Run_completed_callback_failure_becomes_warning`)

## Options

Section `BaryoDev:MediumImport`. These are the defaults from `MediumImportOptions`.

```json
"BaryoDev": {
  "MediumImport": {
    "MaxUploadBytes": 104857600,
    "MaxEntryBytes": 20971520,
    "MaxTotalBytes": 104857600,
    "MaxEntries": 5000,
    "MaxImageBytes": 10485760,
    "MaxGistBytes": 1048576,
    "HttpTimeoutSeconds": 30,
    "UploadTtl": "01:00:00"
  }
}
```

- `MaxUploadBytes`: largest zip you can upload (100 MB). (`SmokeTests.Options_defaults_match_spec`, `UploadTests.Upload_over_cap_throws_and_leaves_no_file`)
- `MaxEntryBytes`, `MaxTotalBytes`, `MaxEntries`: caps on what is read from the zip (20 MB per entry, 100 MB in total, 5000 entries). (`ParsingTests.Reader_stops_at_the_entry_size_cap`, `Reader_stops_at_the_total_size_cap`, `Reader_stops_at_the_entry_count_cap`)
- `MaxImageBytes`: largest image fetched (10 MB). (`NetTests.Fetch_refuses_oversize`)
- `MaxGistBytes`: largest gist response read (1 MB). (`NetTests.Gist_oversize_returns_null`)
- `HttpTimeoutSeconds`: timeout for each outbound request (30). (`NetTests.Slow_body_times_out_to_null`)
- `UploadTtl`: how long an unused upload is kept (1 hour). (`UploadTests.Cleanup_deletes_only_old_files`)

## What it reads

Only `posts/*.html` inside the zip. Other folders in the export (profile, sessions, IPs, reading history and so on) are ignored. (`ParsingTests.Reader_opens_only_posts_html`) Entry names with path traversal are rejected. (`ParsingTests.Reader_rejects_path_traversal_names`)

## Outbound requests

Over https only, with a size cap and a timeout:

- `cdn-images-1.medium.com` and `miro.medium.com` for images. Any other host is refused without a request, and a redirect to another host is refused. (`NetTests.Fetch_refuses_other_hosts_without_a_request`, `Fetch_refuses_redirect_to_other_host`, `Fetch_follows_redirect_to_medium_host`, `Redirect_to_http_medium_url_returns_null`)
- `api.github.com` for gists. An id that is not a gist id makes no request. (`NetTests.Gist_bad_id_makes_no_request`)

## License

MIT.
