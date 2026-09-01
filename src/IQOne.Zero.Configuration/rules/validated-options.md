---
id: zero.configuration.validated-options
title: Bind settings to a validated options type
package: IQOne.Zero.Configuration
applies-to: ["**/*.cs"]
---

Settings are bound to a type and validated at startup, so a missing or malformed value stops
the application with a message naming it.

## Do

```csharp
public sealed class MailOptions
{
    [Required] public required string Host { get; init; }
    [Range(1, 65535)] public int Port { get; init; } = 587;
}

context.Services.AddValidatedOptions<MailOptions>();
```

The section name defaults to the type name, so `MailOptions` reads the `"MailOptions"`
section. Pass a name only when it differs. For a rule data annotations cannot express, use
the overload taking a predicate and a message that says what a valid value looks like.

Read the settings by injecting `IOptions<MailOptions>` — or `IOptionsMonitor<T>` when the
value must follow a reload.

## Don't

Do not read configuration by string at the point of use:

```csharp
var host = configuration["Mail:Host"];   // untyped, unvalidated, fails on first use
```

A setting read this way fails the first time that code path runs, which in practice means in
production, on the path nobody exercised.
