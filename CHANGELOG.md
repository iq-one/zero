# Changelog

Zero follows semantic versioning from this first published package. Until 1.0 the API will
change; if you adopt it now, pin the version.

## 0.1.0

First release. What it does and what each package holds is in the [README](README.md); this
lists what a reader of a future version needs to know about this one.

**The API will change before 1.0.** Nothing here is settled by having shipped. Breaking
changes will be listed under their version with what to write instead.

### Known limits

- **No outbox.** `IQOne.Zero.Events` is in-process: a subscriber's database writes are inside
  the caller's transaction, but anything that leaves the process — an email, an HTTP call —
  is not, and rolling back does not recall it. Write the intent inside the transaction and
  dispatch it from a background job until `IQOne.Zero.Outbox` exists.
- **Background work does not coordinate across replicas.** Three instances each run the
  nightly job. `BackgroundWorkOptions.Disabled` on all but one is the crude answer; a lease
  taken as the job's first act is the better one.
- **Captive-dependency and duplicate-registration detection is per-assembly.** A singleton in
  one module taking a scoped dependency from another is not reported at compile time. The
  container's scope validation still catches it at startup.
- **No project template.** There is no `dotnet new zero`; start from
  `samples/Zero.Sample.Orders`, which uses every package.
