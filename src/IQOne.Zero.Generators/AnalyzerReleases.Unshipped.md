; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZERO006 | Zero.Registration | Error | More than one lifetime declared
ZERO007 | Zero.Registration | Error | Service type could not be determined
ZERO008 | Zero.Registration | Error | Open generic has no service type it can be registered under. Narrowed: an abstract type carrying a marker, and an open generic that forwards its type parameters to an interface, are both registered or skipped without a diagnostic.
ZERO009 | Zero.Registration | Error | Captive dependency
ZERO010 | Zero.Registration | Warning | Service type registered by two implementations. Not reported for a keyed registration, nor for an extension point resolved as IEnumerable<T>.
ZERO300 | Zero.Web | Error | A route is declared on something that is not a request
ZERO301 | Zero.Web | Error | Route pattern is empty
