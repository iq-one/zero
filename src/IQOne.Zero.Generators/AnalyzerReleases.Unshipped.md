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
ZERO011 | Zero.Registration | Error | A type contradicts the lifetime its abstraction declares
ZERO303 | Zero.Web | Error | A route attribute names no method. Narrowed: deriving from one of the five method attributes is supported and recognised through the base chain.
ZERO220 | Zero.Persistence | Error | A projected member has no source
ZERO221 | Zero.Persistence | Error | An ignored member is not part of the result
ZERO222 | Zero.Persistence | Error | [Projection] is on something that is not a specification
ZERO223 | Zero.Persistence | Error | A projected specification is not partial
ZERO224 | Zero.Persistence | Error | A projected specification already declares its Selector
ZERO225 | Zero.Persistence | Error | A mapped member is not written anywhere
ZERO226 | Zero.Persistence | Error | An ignored member is not part of the source
ZERO227 | Zero.Persistence | Error | A mapping method has the wrong shape
ZERO228 | Zero.Persistence | Error | The type holding a mapping is not partial

