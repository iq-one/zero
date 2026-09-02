; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZERO500 | Zero.Events | Error | Publishing an event that leads back to the one being handled
ZERO501 | Zero.Events | Warning | An event can be changed after it is published
ZERO502 | Zero.Events | Warning | A query handler publishes an event
